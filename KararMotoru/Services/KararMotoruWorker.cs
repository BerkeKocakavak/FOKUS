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
    private readonly MedyaYonetici _medyaYonetici = new();
    private readonly DurumYazici _durumYazici;
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _mudahaleSiralama = new(1, 1);
    private readonly object _hataSyncRoot = new();
    private CancellationTokenSource? _cancellation;
    private Task? _workerTask;
    private GirdiIzleyici? _girdiIzleyici;
    private NamedPipeClientStream? _activePipeClient;
    private KararMotoruState _sonState = new();
    private string? _sessionId;
    private DateTimeOffset _lastDbWrite = DateTimeOffset.MinValue;
    private DateTimeOffset? _bosBakisBaslangic;
    private string? _sonOtomatikMudahaleHatasi;
    private bool _karaListeAskida;
    private bool _kameraNedeniyleDuraklatildi;
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

    public bool Duraklatildi { get; private set; }

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
                _karaListeAskida = false;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                hata = "Müdahale kapatılırken hata: " + ex.Message;
            }
        }

        Publish(_sonState with
        {
            MudahaleAktif = aktif,
            Hata = hata,
            DurumMesaji = aktif ? "Müdahale açık" : "Müdahale kapalı"
        });
    }

    public void DuraklatmaDurumuAyarla(bool aktif)
    {
        Duraklatildi = aktif;
        _bosBakisBaslangic = null;
        if (!aktif)
        {
            _kameraNedeniyleDuraklatildi = false;
        }

        _girdiIzleyici?.DuraklatmaDurumuAyarla(aktif);
        string? hata = null;

        if (aktif)
        {
            try
            {
                _surecYonetici.SurecleriDevamEttir(Ayarlar.KaraListe);
                _medyaYonetici.DevamEttir();
                _karaListeAskida = false;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                hata = "Duraklatılırken süreçler devam ettirilemedi: " + ex.Message;
            }
        }

        Publish(_sonState with
        {
            Duraklatildi = aktif,
            MudahaleAktif = aktif ? false : MudahaleAktif,
            Hata = hata,
            DurumMesaji = aktif ? "Duraklatma modu aktif" : "Takip devam ediyor"
        });
    }

    public string ManuelAskıyaAl()
    {
        IReadOnlyList<string> hedefler = AktifKaraListeHedefleri();
        if (hedefler.Count == 0)
        {
            return "Askıya alınacak kara liste süreci yok.";
        }

        SurecMudahaleSonucu sonuc = _surecYonetici.SurecleriDondur(hedefler);
        _karaListeAskida = sonuc.EtkilenenSurecSayisi > 0;
        string? uyari = KritikSurecUyarisi(sonuc, "askıya alma");
        string mesaj = uyari ?? $"{sonuc.EtkilenenSurecSayisi} kara liste süreci askıya alındı.";
        Publish(_sonState with { DurumMesaji = mesaj, Hata = uyari });
        return mesaj;
    }

    public string ManuelDevamEttir()
    {
        IReadOnlyList<string> hedefler = AktifKaraListeHedefleri();
        if (hedefler.Count == 0)
        {
            hedefler = Ayarlar.KaraListe;
        }

        _surecYonetici.SurecleriDevamEttir(hedefler);
        _karaListeAskida = false;
        string mesaj = "Kara liste süreçleri devam ettirildi.";
        Publish(_sonState with { DurumMesaji = mesaj, Hata = null });
        return mesaj;
    }

    public string ManuelSonlandir()
    {
        IReadOnlyList<string> hedefler = AktifKaraListeHedefleri();
        if (hedefler.Count == 0)
        {
            return "Sonlandırılacak kara liste süreci yok.";
        }

        SurecMudahaleSonucu sonuc = _surecYonetici.SurecleriSonlandir(hedefler);
        _karaListeAskida = false;
        string? uyari = KritikSurecUyarisi(sonuc, "sonlandırma");
        string mesaj = uyari ?? (sonuc.EtkilenenSurecSayisi == 0
            ? "Sonlandırılacak çalışan süreç bulunamadı."
            : $"{sonuc.EtkilenenSurecSayisi} kara liste süreci sonlandırıldı.");
        Publish(_sonState with { DurumMesaji = mesaj, Hata = uyari });
        return mesaj;
    }

    public void Start()
    {
        if (Calisiyor)
        {
            return;
        }

        _cancellation = new CancellationTokenSource();
        _girdiIzleyici = new GirdiIzleyici(Ayarlar.GirdiOrneklemeMs);
        _kameraNedeniyleDuraklatildi = false;
        _bosBakisBaslangic = null;
        _sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        _lastDbWrite = DateTimeOffset.MinValue;
        _database?.EnsureCreated();
        _database?.StartSession(_sessionId, DateTimeOffset.Now);
        _workerTask = Task.Run(() => RunAsync(_cancellation.Token));
    }

    // Kapanista pipe'i zorla dispose eder; aksi halde ReadLine beklemesi WPF kapanisini kilitleyebilir.
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

        try
        {
            if (workerTask is not null)
            {
                Task tamamlanma = await Task.WhenAny(workerTask, Task.Delay(timeout ?? TimeSpan.FromSeconds(2)));
                if (tamamlanma == workerTask)
                {
                    await workerTask;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            _surecYonetici.SurecleriDevamEttir(Ayarlar.KaraListe);
            _medyaYonetici.DevamEttir();
            _karaListeAskida = false;
            _kameraNedeniyleDuraklatildi = false;
            _girdiIzleyici?.Dispose();
            _girdiIzleyici = null;
            cancellation.Dispose();
            _workerTask = null;

            if (_sessionId is not null)
            {
                _database?.EndSession(_sessionId, DateTimeOffset.Now);
                _sessionId = null;
            }

            _cancellation = null;
            Duraklatildi = false;
            Publish(_sonState with { PipeBagli = false, Duraklatildi = false, DurumMesaji = "Karar motoru durduruldu" });
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
            Duraklatildi = Duraklatildi,
            MudahaleAktif = MudahaleAktif,
            DurumMesaji = "Python pipe bağlantısı bekleniyor"
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
                    Duraklatildi = Duraklatildi,
                    MudahaleAktif = MudahaleAktif,
                    DurumMesaji = "Pipe hazır değil; yeniden denenecek"
                });
                await Task.Delay(1000, cancellationToken);
            }
            catch (IOException ex)
            {
                KameraSorunuylaDuraklat(
                    "Kamera/pipe bağlantısı kesildi; sistem duraklatıldı.",
                    ex.Message,
                    pipeBagli: false);
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    // Python named pipe baglantisini tek yerde kurar ve kopunca RunAsync yeniden deneme dongusune doner.
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
                Duraklatildi = Duraklatildi,
                MudahaleAktif = MudahaleAktif,
                DurumMesaji = "Pipe bağlandı",
                Hata = null
            });

            using var reader = new StreamReader(client, Encoding.UTF8);
            while (client.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                string? jsonVeri = await reader.ReadLineAsync(cancellationToken);
                if (jsonVeri is null)
                {
                    throw new EndOfStreamException("Pipe veri akışı sonlandı.");
                }

                if (string.IsNullOrWhiteSpace(jsonVeri))
                {
                    continue;
                }

                PaketIsle(jsonVeri, DateTimeOffset.Now);
            }
        }
        finally
        {
            ClearActivePipe(client);
        }
    }

    // Kamera paketini karar motoru state'ine cevirir: girdi, surec, puan, medya ve DB yazimi burada birlesir.
    private void PaketIsle(string jsonVeri, DateTimeOffset paketZamani)
    {
        BiyometrikVeri? veri;
        try
        {
            veri = JsonSerializer.Deserialize<BiyometrikVeri>(jsonVeri, _jsonOptions);
        }
        catch (JsonException ex)
        {
            Publish(_sonState with { Hata = "Geçersiz biyometrik veri: " + ex.Message });
            return;
        }

        if (veri is null || _girdiIzleyici is null)
        {
            return;
        }

        if (KameraSorunuVar(veri, paketZamani, out string kameraMesaji))
        {
            KameraSorunuylaDuraklat(kameraMesaji, kameraMesaji, pipeBagli: true, veri);
            return;
        }

        if (_kameraNedeniyleDuraklatildi && Duraklatildi)
        {
            Publish(new KararMotoruState
            {
                Zaman = paketZamani,
                PipeBagli = true,
                Duraklatildi = true,
                MudahaleAktif = false,
                DurumMesaji = "Kamera yeniden bağlandı; devam etmek için Devam et'e bas.",
                Biyometrik = veri,
                Girdi = _girdiIzleyici.OzetAl(Ayarlar.AktivitePenceresiSaniye)
            });
            return;
        }

        if (Duraklatildi)
        {
            Publish(new KararMotoruState
            {
                Zaman = paketZamani,
                PipeBagli = true,
                Duraklatildi = true,
                MudahaleAktif = false,
                DurumMesaji = "Duraklatma modu aktif",
                Biyometrik = veri
            });
            return;
        }

        GirdiAktiviteOzeti girdiOzeti = _girdiIzleyici.OzetAl(Ayarlar.AktivitePenceresiSaniye);
        BosBakisDurumunuGuncelle(veri, girdiOzeti, paketZamani);
        SurecTaramaSonucu surecSonucu = _surecTarayici.Tara(Ayarlar);
        OdakSonucu odakSonucu = _odakMotoru.Hesapla(veri, girdiOzeti, surecSonucu, Ayarlar);

        string? mudahaleHatasi = MudahaleUygula(odakSonucu, surecSonucu);
        MedyaDurumunuUygula(odakSonucu);
        _durumYazici.Yaz(odakSonucu, veri, girdiOzeti, surecSonucu);

        KararMotoruState state = new()
        {
            Zaman = paketZamani,
            PipeBagli = true,
            Duraklatildi = false,
            MudahaleAktif = MudahaleAktif,
            DurumMesaji = KolayDurumMesaji(odakSonucu),
            Biyometrik = veri,
            Girdi = girdiOzeti,
            Surec = surecSonucu,
            Odak = odakSonucu,
            Hata = mudahaleHatasi
        };

        VeritabaniKaydiYaz(state);
        Publish(state);
    }

    private bool KameraSorunuVar(BiyometrikVeri veri, DateTimeOffset paketZamani, out string mesaj)
    {
        if (!veri.KameraBagli)
        {
            mesaj = "Kamera bağlantısı kesildi; sistem duraklatıldı.";
            return true;
        }

        if (!veri.AnalizHazir &&
            veri.AnalizDurumu?.Contains("kamera", StringComparison.OrdinalIgnoreCase) == true)
        {
            mesaj = "Kamera analizi durdu; sistem duraklatıldı.";
            return true;
        }

        mesaj = string.Empty;
        return false;
    }

    private void BosBakisDurumunuGuncelle(BiyometrikVeri veri, GirdiAktiviteOzeti girdi, DateTimeOffset paketZamani)
    {
        bool gozAcik = veri.EarEsik <= 0 ||
            veri.Ear <= 0 ||
            veri.Ear >= veri.EarEsik;
        bool bakisMerkezde = veri.YuzVar &&
            veri.AnalizHazir &&
            veri.KalibrasyonTamam &&
            Math.Abs(veri.GazeSapma) <= Math.Max(Ayarlar.GazeEsigi, 0.015);
        bool basDuzgun = Math.Sqrt(Math.Pow(veri.OneSapma, 2) + Math.Pow(veri.YanaSapma, 2)) <= Math.Max(Ayarlar.PosturEsigi, 8);
        bool girdiYok = girdi.HareketsizSaniye >= 1;

        if (bakisMerkezde && basDuzgun && gozAcik && girdiYok)
        {
            _bosBakisBaslangic ??= paketZamani;
            veri.BosBakisSaniye = Math.Max(0, (paketZamani - _bosBakisBaslangic.Value).TotalSeconds);
            veri.BosBakis = veri.BosBakisSaniye >= Ayarlar.BosBakisSaniyesi;
            return;
        }

        _bosBakisBaslangic = null;
        veri.BosBakis = false;
        veri.BosBakisSaniye = 0;
    }

    private void KameraSorunuylaDuraklat(string durumMesaji, string? hata, bool pipeBagli, BiyometrikVeri? veri = null)
    {
        Duraklatildi = true;
        _kameraNedeniyleDuraklatildi = true;
        _bosBakisBaslangic = null;
        _girdiIzleyici?.DuraklatmaDurumuAyarla(true);

        string? hataMesaji = hata;
        try
        {
            _surecYonetici.SurecleriDevamEttir(Ayarlar.KaraListe);
            _medyaYonetici.DevamEttir();
            _karaListeAskida = false;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            hataMesaji = string.IsNullOrWhiteSpace(hataMesaji)
                ? "Duraklatılırken süreçler devam ettirilemedi: " + ex.Message
                : hataMesaji + " | Duraklatma temizliği: " + ex.Message;
        }

        Publish(new KararMotoruState
        {
            Zaman = DateTimeOffset.Now,
            PipeBagli = pipeBagli,
            Duraklatildi = true,
            MudahaleAktif = false,
            DurumMesaji = durumMesaji,
            Biyometrik = veri,
            Girdi = _girdiIzleyici?.OzetAl(Ayarlar.AktivitePenceresiSaniye),
            Hata = hataMesaji
        });
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

    // Otomatik kara liste mudahalesi yavas Win32 islemlerini arka plana alir, paket akisinin donmasini engeller.
    private string? MudahaleUygula(OdakSonucu odakSonucu, SurecTaramaSonucu surecSonucu)
    {
        string? bekleyenHata = OtomatikMudahaleHatasiniAl();
        if (!MudahaleAktif)
        {
            return bekleyenHata;
        }

        try
        {
            if (odakSonucu.MudahaleGerekli)
            {
                if (!_karaListeAskida && surecSonucu.KaraListedekiSurecler.Count > 0)
                {
                    string[] hedefler = surecSonucu.KaraListedekiSurecler.ToArray();
                    OtomatikMudahalePlanla(() => _surecYonetici.SurecleriDondur(hedefler), "askıya alma");
                    _karaListeAskida = true;
                }
            }
            else if (_karaListeAskida)
            {
                string[] hedefler = Ayarlar.KaraListe.ToArray();
                OtomatikMudahalePlanla(() => _surecYonetici.SurecleriDevamEttir(hedefler), "devam ettirme");
                _karaListeAskida = false;
            }

            return bekleyenHata;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return "Müdahale hatası: " + ex.Message;
        }
    }

    private static string? KritikSurecUyarisi(SurecMudahaleSonucu sonuc, string eylem)
    {
        if (!sonuc.KritikSurecReddedildi)
        {
            return null;
        }

        string surecler = string.Join(", ", sonuc.ReddedilenKritikSurecler);
        return $"Kritik süreç korundu: {surecler}. {eylem} işlemi engellendi.";
    }

    private void OtomatikMudahalePlanla(Func<SurecMudahaleSonucu> islem, string eylem)
    {
        _ = Task.Run(async () =>
        {
            await _mudahaleSiralama.WaitAsync();
            try
            {
                SurecMudahaleSonucu sonuc = islem();
                string? uyari = KritikSurecUyarisi(sonuc, eylem);
                if (uyari is not null)
                {
                    lock (_hataSyncRoot)
                    {
                        _sonOtomatikMudahaleHatasi = uyari;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                lock (_hataSyncRoot)
                {
                    _sonOtomatikMudahaleHatasi = "Müdahale hatası: " + ex.Message;
                }
            }
            finally
            {
                _mudahaleSiralama.Release();
            }
        });
    }

    private string? OtomatikMudahaleHatasiniAl()
    {
        lock (_hataSyncRoot)
        {
            string? hata = _sonOtomatikMudahaleHatasi;
            _sonOtomatikMudahaleHatasi = null;
            return hata;
        }
    }

    private void MedyaDurumunuUygula(OdakSonucu odakSonucu)
    {
        bool odakDusuk = odakSonucu.Puan < Ayarlar.OdakEsigi;
        _medyaYonetici.OdakDurumunuUygula(odakDusuk);
    }

    private IReadOnlyList<string> AktifKaraListeHedefleri()
    {
        return _sonState.Surec?.KaraListedekiSurecler.Count > 0
            ? _sonState.Surec.KaraListedekiSurecler
            : Array.Empty<string>();
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
            Publish(_sonState with { Hata = "Veritabanı kayıt hatası: " + ex.Message });
        }
    }

    private static string KolayDurumMesaji(OdakSonucu odakSonucu)
    {
        return odakSonucu.MudahaleGerekli
            ? "Odak düşük; kara liste için müdahale öneriliyor"
            : "Odak izleniyor";
    }
}
