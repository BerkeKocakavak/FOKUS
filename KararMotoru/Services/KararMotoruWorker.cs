using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using FokusKararMotoru.Models;

namespace FokusKararMotoru.Services;

public sealed class KararMotoruWorker : IDisposable
{
    private readonly string _projeKoku;
    private readonly string _pipeName;
    private readonly FokusDb? _database;
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly SurecYonetici _surecYonetici = new();
    private readonly SurecTarayici _surecTarayici = new();
    private readonly OdakPuaniMotoru _odakMotoru = new();
    private readonly DurumYazici _durumYazici;
    private readonly object _syncRoot = new();
    private CancellationTokenSource? _cancellation;
    private Task? _workerTask;
    private GirdiIzleyici? _girdiIzleyici;
    private NamedPipeClientStream? _activePipeClient;
    private KararMotoruState _sonState = new();
    private string? _sessionId;
    private DateTimeOffset _lastDbWrite = DateTimeOffset.MinValue;
    private bool _disposed;

    public KararMotoruWorker(string projeKoku, KararMotoruAyarlari ayarlar, string pipeName = "fokus_pipe", FokusDb? database = null)
    {
        _projeKoku = projeKoku;
        _pipeName = pipeName;
        _database = database;
        Ayarlar = ayarlar;
        _durumYazici = new DurumYazici(projeKoku);
    }

    public event EventHandler<KararMotoruState>? StateChanged;

    public KararMotoruAyarlari Ayarlar { get; private set; }

    public bool MudahaleAktif { get; set; }

    public bool Calisiyor => _workerTask is { IsCompleted: false };

    public void AyarlariGuncelle(KararMotoruAyarlari ayarlar)
    {
        Ayarlar = ayarlar;
        AyarDeposu.Kaydet(_projeKoku, Ayarlar);
    }

    public void MudahaleDurumuAyarla(bool aktif)
    {
        MudahaleAktif = aktif;
        string? hata = null;

        if (!aktif)
        {
            try
            {
                _surecYonetici.SurecleriDevamEttir(Ayarlar.KaraListe);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                hata = "Mudahale kapatilirken hata: " + ex.Message;
            }
        }

        Publish(_sonState with
        {
            MudahaleAktif = aktif,
            Hata = hata,
            DurumMesaji = aktif ? "Mudahale acik" : "Mudahale kapali"
        });
    }

    public void Start()
    {
        if (Calisiyor)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _girdiIzleyici = new GirdiIzleyici(Ayarlar.GirdiOrneklemeMs);
        _sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        _lastDbWrite = DateTimeOffset.MinValue;
        _database?.EnsureCreated();
        _database?.StartSession(_sessionId, DateTimeOffset.Now);
        _workerTask = Task.Run(() => RunAsync(_cancellation.Token));
    }

    public async Task StopAsync(TimeSpan? timeout = null)
    {
        CancellationTokenSource? cancellation = _cancellation;
        Task? workerTask = _workerTask;
        if (cancellation is null)
        {
            return;
        }

        cancellation.Cancel();
        DisposeActivePipe();

        bool tamamlandi = workerTask is null;
        try
        {
            if (workerTask is not null)
            {
                Task tamamlanma = await Task.WhenAny(workerTask, Task.Delay(timeout ?? TimeSpan.FromSeconds(2)));
                if (tamamlanma == workerTask)
                {
                    tamamlandi = true;
                    await workerTask;
                }
            }
        }
        catch (OperationCanceledException)
        {
            tamamlandi = true;
        }
        catch (ObjectDisposedException)
        {
            tamamlandi = true;
        }
        catch (IOException)
        {
            tamamlandi = true;
        }
        finally
        {
            _surecYonetici.SurecleriDevamEttir(Ayarlar.KaraListe);
            _girdiIzleyici?.Dispose();
            _girdiIzleyici = null;
            if (tamamlandi)
            {
                cancellation.Dispose();
                _workerTask = null;
            }

            if (_sessionId is not null)
            {
                _database?.EndSession(_sessionId, DateTimeOffset.Now);
                _sessionId = null;
            }

            _cancellation = null;
            Publish(_sonState with { PipeBagli = false, DurumMesaji = "Karar motoru durduruldu" });
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _cancellation?.Cancel();
        DisposeActivePipe();
        _girdiIzleyici?.Dispose();
        _cancellation?.Dispose();
        _disposed = true;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Publish(new KararMotoruState
        {
            MudahaleAktif = MudahaleAktif,
            DurumMesaji = "Python pipe baglantisi bekleniyor"
        });

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PipeDongusuAsync(cancellationToken);
            }
            catch (TimeoutException)
            {
                Publish(_sonState with
                {
                    PipeBagli = false,
                    MudahaleAktif = MudahaleAktif,
                    DurumMesaji = "Pipe hazir degil; yeniden denenecek"
                });
                await Task.Delay(1000, cancellationToken);
            }
            catch (IOException ex)
            {
                _surecYonetici.SurecleriDevamEttir(Ayarlar.KaraListe);
                Publish(_sonState with
                {
                    PipeBagli = false,
                    MudahaleAktif = MudahaleAktif,
                    DurumMesaji = "Pipe baglantisi kesildi",
                    Hata = ex.Message
                });
                await Task.Delay(1000, cancellationToken);
            }
        }
    }

    private async Task PipeDongusuAsync(CancellationToken cancellationToken)
    {
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.In, PipeOptions.Asynchronous);
        SetActivePipe(client);
        try
        {
            await client.ConnectAsync(Ayarlar.PipeBaglantiZamanAsimiMs, cancellationToken);

            Publish(_sonState with
            {
                PipeBagli = true,
                MudahaleAktif = MudahaleAktif,
                DurumMesaji = "Pipe baglandi",
                Hata = null
            });

            using var reader = new StreamReader(client, Encoding.UTF8);
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
                    veri = JsonSerializer.Deserialize<BiyometrikVeri>(jsonVeri, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    Publish(_sonState with { Hata = "Gecersiz biyometrik veri: " + ex.Message });
                    continue;
                }

                if (veri is null || _girdiIzleyici is null)
                {
                    continue;
                }

                GirdiAktiviteOzeti girdiOzeti = _girdiIzleyici.OzetAl(Ayarlar.AktivitePenceresiSaniye);
                SurecTaramaSonucu surecSonucu = _surecTarayici.Tara(Ayarlar);
                OdakSonucu odakSonucu = _odakMotoru.Hesapla(veri, girdiOzeti, surecSonucu, Ayarlar);

                string? mudahaleHatasi = MudahaleUygula(odakSonucu, surecSonucu);
                _durumYazici.Yaz(odakSonucu, veri, girdiOzeti, surecSonucu);

                KararMotoruState state = new()
                {
                    Zaman = DateTimeOffset.Now,
                    PipeBagli = true,
                    MudahaleAktif = MudahaleAktif,
                    DurumMesaji = kolayDurumMesaji(odakSonucu),
                    Biyometrik = veri,
                    Girdi = girdiOzeti,
                    Surec = surecSonucu,
                    Odak = odakSonucu,
                    Hata = mudahaleHatasi
                };

                VeritabaniKaydiYaz(state);
                Publish(state);
            }
        }
        finally
        {
            ClearActivePipe(client);
        }
    }

    private void SetActivePipe(NamedPipeClientStream client)
    {
        lock (_syncRoot)
        {
            _activePipeClient = client;
        }
    }

    private void ClearActivePipe(NamedPipeClientStream client)
    {
        lock (_syncRoot)
        {
            if (ReferenceEquals(_activePipeClient, client))
            {
                _activePipeClient = null;
            }
        }
    }

    private void DisposeActivePipe()
    {
        NamedPipeClientStream? client;
        lock (_syncRoot)
        {
            client = _activePipeClient;
            _activePipeClient = null;
        }

        try
        {
            client?.Dispose();
        }
        catch (IOException)
        {
        }
    }

    private string? MudahaleUygula(OdakSonucu odakSonucu, SurecTaramaSonucu surecSonucu)
    {
        if (!MudahaleAktif)
        {
            return null;
        }

        try
        {
            if (odakSonucu.MudahaleGerekli)
            {
                _surecYonetici.SurecleriDondur(surecSonucu.KaraListedekiSurecler);
            }
            else if (surecSonucu.KaraListedekiSurecler.Count > 0)
            {
                _surecYonetici.SurecleriDevamEttir(surecSonucu.KaraListedekiSurecler);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return "Mudahale hatasi: " + ex.Message;
        }
    }

    private void Publish(KararMotoruState state)
    {
        _sonState = state;
        StateChanged?.Invoke(this, state);
    }

    private void VeritabaniKaydiYaz(KararMotoruState state)
    {
        if (_database is null || _sessionId is null || state.Odak is null)
        {
            return;
        }

        if (state.Zaman - _lastDbWrite < TimeSpan.FromSeconds(1))
        {
            return;
        }

        try
        {
            _database.SaveSample(_sessionId, state);
            _lastDbWrite = state.Zaman;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            Publish(_sonState with { Hata = "Veritabani kayit hatasi: " + ex.Message });
        }
    }

    private static string kolayDurumMesaji(OdakSonucu odakSonucu)
    {
        return odakSonucu.MudahaleGerekli
            ? "Odak dusuk; kara liste icin mudahale oneriliyor"
            : "Odak izleniyor";
    }
}
