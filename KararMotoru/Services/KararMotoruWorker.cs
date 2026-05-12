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
    private string? _sonOtomatikMudahaleHatasi;
    private bool _karaListeAskida;
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

        _surecYonetici.SurecleriDondur(hedefler);
        _karaListeAskida = true;
        string mesaj = $"{hedefler.Count} kara liste süreci askıya alındı.";
        Publish(_sonState with { DurumMesaji = mesaj, Hata = null });
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

        int sayi = _surecYonetici.SurecleriSonlandir(hedefler);
        _karaListeAskida = false;
        string mesaj = sayi == 0
            ? "Sonlandırılacak çalışan süreç bulunamadı."
            : $"{sayi} kara liste süreci sonlandırıldı.";
        Publish(_sonState with { DurumMesaji = mesaj, Hata = null });
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
                _surecYonetici.SurecleriDevamEttir(Ayarlar.KaraListe);
                _karaListeAskida = false;
                Publish(_sonState with
                {
                    PipeBagli = false,
                    Duraklatildi = Duraklatildi,
                    MudahaleAktif = MudahaleAktif,
                    DurumMesaji = "Pipe bağlantısı kesildi",
                    Hata = ex.Message
                });
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
                    OtomatikMudahalePlanla(() => _surecYonetici.SurecleriDondur(hedefler));
                    _karaListeAskida = true;
                }
            }
            else if (_karaListeAskida)
            {
                string[] hedefler = Ayarlar.KaraListe.ToArray();
                OtomatikMudahalePlanla(() => _surecYonetici.SurecleriDevamEttir(hedefler));
                _karaListeAskida = false;
            }

            return bekleyenHata;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {
            return "Müdahale hatası: " + ex.Message;
        }
    }

    private void OtomatikMudahalePlanla(Action islem)
    {
        _ = Task.Run(async () =>
        {
            await _mudahaleSiralama.WaitAsync();
            try
            {
                islem();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
            {
                lock (_hataSyncRoot)
                {
                    _sonOtomatikMudahaleHatasi = "MÃ¼dahale hatasÄ±: " + ex.Message;
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
