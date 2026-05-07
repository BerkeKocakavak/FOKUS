using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FokusKararMotoru.Models;
using FokusKararMotoru.Services;

namespace FokusKararMotoru;

internal static class Program
{
    private const string PipeName = "fokus_pipe";
    private static DateTimeOffset _sonYonlendirilmisCikti = DateTimeOffset.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            return SelfTest.Run();
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        string projeKoku = ProjeYolu.Bul();
        KararMotoruAyarlari ayarlar = AyarDeposu.YukleVeyaOlustur(projeKoku);

        using var girdiIzleyici = new GirdiIzleyici(ayarlar.GirdiOrneklemeMs);
        var surecTarayici = new SurecTarayici();
        var odakMotoru = new OdakPuaniMotoru();
        var durumYazici = new DurumYazici(projeKoku);

        Console.WriteLine("FOKUS Karar Motoru başlatıldı.");
        Console.WriteLine($"Ayar dosyası: {AyarDeposu.AyarDosyasiYolu(projeKoku)}");
        Console.WriteLine("Python pipe bağlantısı bekleniyor...");

        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await PipeDongusu(ayarlar, girdiIzleyici, surecTarayici, odakMotoru, durumYazici, cancellation.Token);
            }
            catch (TimeoutException)
            {
                Console.WriteLine("Pipe henüz hazır değil; tekrar denenecek.");
                await Task.Delay(1000, cancellation.Token);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Pipe bağlantısı kesildi: {ex.Message}");
                await Task.Delay(1000, cancellation.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        Console.WriteLine("Karar motoru durduruldu.");
        return 0;
    }

    private static async Task PipeDongusu(
        KararMotoruAyarlari ayarlar,
        GirdiIzleyici girdiIzleyici,
        SurecTarayici surecTarayici,
        OdakPuaniMotoru odakMotoru,
        DurumYazici durumYazici,
        CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.In, PipeOptions.Asynchronous);
        await client.ConnectAsync(ayarlar.PipeBaglantiZamanAsimiMs, cancellationToken);

        using var reader = new StreamReader(client, Encoding.UTF8);
        Console.WriteLine("Python pipe bağlantısı kuruldu.");

        while (client.IsConnected && !cancellationToken.IsCancellationRequested)
        {
            string? jsonVeri = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(jsonVeri))
            {
                continue;
            }

            BiyometrikVeri? veri;
            try
            {
                veri = JsonSerializer.Deserialize<BiyometrikVeri>(jsonVeri, JsonOptions);
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Geçersiz biyometrik veri atlandı: {ex.Message}");
                continue;
            }

            if (veri is null)
            {
                continue;
            }

            GirdiAktiviteOzeti girdiOzeti = girdiIzleyici.OzetAl(ayarlar.AktivitePenceresiSaniye);
            SurecTaramaSonucu surecSonucu = surecTarayici.Tara(ayarlar);
            OdakSonucu odakSonucu = odakMotoru.Hesapla(veri, girdiOzeti, surecSonucu, ayarlar);

            durumYazici.Yaz(odakSonucu, veri, girdiOzeti, surecSonucu);
            KonsolaYaz(veri, girdiOzeti, surecSonucu, odakSonucu, ayarlar);
        }
    }

    private static void KonsolaYaz(
        BiyometrikVeri veri,
        GirdiAktiviteOzeti girdi,
        SurecTaramaSonucu surec,
        OdakSonucu odak,
        KararMotoruAyarlari ayarlar)
    {
        if (Console.IsOutputRedirected)
        {
            DateTimeOffset simdi = DateTimeOffset.Now;
            if ((simdi - _sonYonlendirilmisCikti).TotalSeconds < 2)
            {
                return;
            }

            _sonYonlendirilmisCikti = simdi;
            Console.WriteLine(
                $"{simdi:HH:mm:ss} | Odak {odak.Puan}/100 | Yüz: {(veri.YuzVar ? "var" : "yok")} | Kara liste: {surec.KaraListeOzeti} | Cezalar: {odak.CezaOzeti}");
            return;
        }

        if (!Console.IsOutputRedirected)
        {
            try
            {
                Console.SetCursorPosition(0, 0);
            }
            catch (IOException)
            {
                // Bazı terminaller imleç konumlandırmayı desteklemeyebilir.
            }
        }

        Console.WriteLine("================ FOKUS KARAR MOTORU ================".PadRight(90));
        Console.WriteLine($"Durum:              {(veri.YuzVar ? "Kullanıcı ekran başında" : "KULLANICI YOK").PadRight(55)}");
        Console.WriteLine($"Odak eşiği:         {ayarlar.OdakEsigi}".PadRight(90));
        Console.WriteLine($"Göz kırpma:         {veri.KirpmaSayisi}".PadRight(90));
        Console.WriteLine($"Baş eğimi:          Öne {veri.OneSapma:0.0}, Yana {veri.YanaSapma:0.0}".PadRight(90));
        Console.WriteLine($"Girdi izleme:       {girdi.TusDakika:0.0} tuş/dk, {girdi.FarePikselDakika:0} px/dk, boşta {girdi.HareketsizSaniye:0}s".PadRight(90));
        Console.WriteLine($"Ön plan süreç:      {(surec.OnPlanSurec ?? "Bilinmiyor")}".PadRight(90));
        Console.WriteLine($"Kara liste:         {surec.KaraListeOzeti}".PadRight(90));
        Console.WriteLine("----------------------------------------------------".PadRight(90));
        Console.Write("ANLIK ODAK PUANI:   ");

        ConsoleColor eskiRenk = Console.ForegroundColor;
        Console.ForegroundColor = odak.Puan switch
        {
            >= 70 => ConsoleColor.Green,
            >= 40 => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.WriteLine($"{odak.Puan} / 100".PadRight(70));
        Console.ForegroundColor = eskiRenk;

        string karar = odak.MudahaleGerekli
            ? "Müdahale gerekli: kara listedeki süreçler için SY modülüne komut üretilebilir."
            : "Müdahale gerekmiyor.";

        Console.WriteLine($"Karar:              {karar}".PadRight(90));
        Console.WriteLine($"Cezalar:            {odak.CezaOzeti}".PadRight(90));
        Console.WriteLine("====================================================".PadRight(90));
    }
}
