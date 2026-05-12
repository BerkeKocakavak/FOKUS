using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using FokusKararMotoru.Models;
using FokusKararMotoru.Services;

namespace FokusKararMotoru;

public partial class MainWindow : Window
{
    private const int HistoryLimit = 120;

    private readonly string _projeKoku;
    private readonly FokusDb _database;
    private readonly KararMotoruWorker _kararMotoruWorker;
    private readonly PythonCameraWorker _pythonCameraWorker;
    private readonly OturumAnalizMotoru _oturumAnalizMotoru = new();
    private readonly List<int> _odakGecmisi = [];
    private readonly Dictionary<string, DateTimeOffset> _uyariZamanlari = new(StringComparer.OrdinalIgnoreCase);

    private KararMotoruAyarlari _ayarlar;
    private DateTimeOffset _sonUyariZamani = DateTimeOffset.MinValue;
    private DateTimeOffset _sessionStart = DateTimeOffset.Now;
    private DateTimeOffset? _lastStateTime;
    private DateTimeOffset? _pauseStartedAt;
    private DateTimeOffset? _uykuBaslangic;
    private FocusAlertWindow? _uyariPenceresi;
    private FocusOverlayWindow? _overlayWindow;
    private DashboardWindow? _dashboardWindow;
    private SessionAnalysisWindow? _sessionAnalysisWindow;
    private SettingsWindow? _settingsWindow;
    private int _sessionSamples;
    private int _minimumFocus = 100;
    private int _lastFocusScore = 100;
    private double _focusScoreTotal;
    private double _lowFocusSeconds;
    private TimeSpan _pausedDuration = TimeSpan.Zero;
    private bool _kapaniyor;
    private bool _kapanisTamamlandi;
    private bool _baslatiliyor;
    private bool _bagimlilikKontroluGecti;
    private bool _duraklatildi;
    private bool _kameraOnizlemeAktif;
    private string _sonHata = "Yok";

    public MainWindow()
    {
        InitializeComponent();

        _projeKoku = ProjeYolu.Bul();
        _database = new FokusDb(_projeKoku);
        _database.EnsureCreated();
        _pythonCameraWorker = new PythonCameraWorker(_projeKoku);
        _ayarlar = AyarDeposu.YukleVeyaOlustur(_projeKoku);
        KameraAyarlariniUygula();
        _kararMotoruWorker = new KararMotoruWorker(_projeKoku, _ayarlar, _pythonCameraWorker.PipeName, _database);

        _kararMotoruWorker.StateChanged += (_, state) =>
            Dispatcher.BeginInvoke(() => DurumuGoster(state));
        _pythonCameraWorker.LogChanged += (_, mesaj) =>
            Dispatcher.BeginInvoke(() => PythonLogGoster(mesaj));
        _pythonCameraWorker.FrameReceived += (_, frame) =>
            Dispatcher.BeginInvoke(() => KameraKaresiniGoster(frame.JpegBytes));
        HistoryCanvas.SizeChanged += (_, _) => OdakGecmisiniCiz();

        KamerayiTemizle();
        OturumSayaclariniSifirla();
        RaporlariYukle();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _overlayWindow = new FocusOverlayWindow();
        _overlayWindow.RestoreRequested += (_, _) => AnaPencereyiGoster();
        _overlayWindow.Show();
        Baslat();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_kapanisTamamlandi)
        {
            return;
        }

        e.Cancel = true;
        await KapatAsync();
        _kapanisTamamlandi = true;
        Close();
    }

    private async Task KapatAsync()
    {
        if (_kapaniyor)
        {
            return;
        }

        _kapaniyor = true;
        IsEnabled = false;
        Hide();
        _kameraOnizlemeAktif = false;
        KamerayiTemizle();
        _uyariPenceresi?.Close();
        _overlayWindow?.Close();
        _dashboardWindow?.Close();
        _sessionAnalysisWindow?.Close();
        _settingsWindow?.Close();
        _overlayWindow = null;
        _dashboardWindow = null;
        _sessionAnalysisWindow = null;
        _settingsWindow = null;

        try
        {
            await Task.WhenAll(
                _pythonCameraWorker.StopAsync(TimeSpan.FromSeconds(2)),
                _kararMotoruWorker.StopAsync(TimeSpan.FromSeconds(2)));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException or System.ComponentModel.Win32Exception)
        {
            StatusText.Text = "Kapanış sırasında hata: " + ex.Message;
        }

        _kararMotoruWorker.Dispose();
        _pythonCameraWorker.Dispose();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        Baslat();
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        try
        {
            _kameraOnizlemeAktif = false;
            await _kararMotoruWorker.StopAsync(TimeSpan.FromSeconds(3));
            await _pythonCameraWorker.StopAsync(TimeSpan.FromSeconds(3));
            DuraklatmaUiGuncelle(false);
            KamerayiTemizle();
            _overlayWindow?.SetStopped();
            RaporlariYukle();
            OturumAnaliziniGoster();
            StatusText.Text = "Durduruldu";
        }
        catch (Exception ex)
        {
            HataGoster("Durdurma hatası: " + ex.Message);
            StatusText.Text = "Durdurma sırasında hata oluştu.";
        }
        finally
        {
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = true;
        }
    }

    private void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        DuraklatmaUiGuncelle(!_duraklatildi);
        _kararMotoruWorker.DuraklatmaDurumuAyarla(_duraklatildi);
        if (!_duraklatildi)
        {
            _kararMotoruWorker.MudahaleDurumuAyarla(_ayarlar.KaraListeMudahalesiAktif);
        }

        StatusText.Text = _duraklatildi ? "Duraklatma modu aktif." : "Takip devam ediyor.";
    }

    private void ShowDashboardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_dashboardWindow is null)
        {
            _dashboardWindow = new DashboardWindow(_database, () => _ayarlar.OdakEsigi) { Owner = this };
            _dashboardWindow.Closed += (_, _) => _dashboardWindow = null;
        }

        _dashboardWindow.Show();
        _dashboardWindow.Activate();
    }

    private void ShowSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_ayarlar) { Owner = this };
            _settingsWindow.SettingsSaved += SettingsWindow_SettingsSaved;
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        else
        {
            _settingsWindow.ApplySettings(_ayarlar);
        }

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private async void SettingsWindow_SettingsSaved(object? sender, SettingsSavedEventArgs e)
    {
        _ayarlar = e.Ayarlar;
        _kararMotoruWorker.AyarlariGuncelle(_ayarlar);
        KameraAyarlariniUygula();
        _settingsWindow?.ApplySettings(_ayarlar);
        if (!_duraklatildi)
        {
            _kararMotoruWorker.MudahaleDurumuAyarla(_ayarlar.KaraListeMudahalesiAktif);
        }

        if ((e.PreviewFpsChanged || e.AnalysisFpsChanged) && _pythonCameraWorker.Calisiyor)
        {
            try
            {
                StatusText.Text = "FPS ayarları uygulanıyor...";
                _kameraOnizlemeAktif = false;
                await _pythonCameraWorker.StopAsync(TimeSpan.FromSeconds(2));
                KamerayiTemizle();
                _kameraOnizlemeAktif = true;
                _pythonCameraWorker.Start();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _kameraOnizlemeAktif = false;
                HataGoster("FPS ayarları uygulanamadı: " + ex.Message);
                StatusText.Text = "FPS ayarları uygulanamadı.";
                return;
            }
        }

        StatusText.Text = "Ayarlar kaydedildi.";
    }

    private async void Baslat()
    {
        if (_baslatiliyor)
        {
            return;
        }

        _baslatiliyor = true;
        StartButton.IsEnabled = false;
        try
        {
            _kameraOnizlemeAktif = false;
            KamerayiTemizle();

            if (!_bagimlilikKontroluGecti)
            {
                StatusText.Text = "Başlangıç kontrolü yapılıyor...";
                PythonDependencyCheckResult kontrol = await _pythonCameraWorker.CheckDependenciesAsync(fast: true, timeout: TimeSpan.FromSeconds(8));
                if (!kontrol.Ok)
                {
                    HataGoster(kontrol.Message + Environment.NewLine + "Eksikleri kurmak için: python -m pip install -r requirements.txt");
                    StatusText.Text = "Python paketleri eksik.";
                    KamerayiTemizle();
                    _overlayWindow?.SetStopped();
                    return;
                }

                _bagimlilikKontroluGecti = true;
            }

            DuraklatmaUiGuncelle(false);
            OturumSayaclariniSifirla();
            _kararMotoruWorker.DuraklatmaDurumuAyarla(false);
            _kararMotoruWorker.MudahaleDurumuAyarla(_ayarlar.KaraListeMudahalesiAktif);
            _kameraOnizlemeAktif = true;
            _pythonCameraWorker.Start();
            _kararMotoruWorker.Start();
            StatusText.Text = "Python kamera ve karar motoru çalışıyor.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Başlatılamadı: " + ex.Message;
            HataGoster(ex.Message);
            _kameraOnizlemeAktif = false;
            KamerayiTemizle();
        }
        finally
        {
            _baslatiliyor = false;
            StartButton.IsEnabled = true;
        }
    }

    private void DurumuGoster(KararMotoruState state)
    {
        if (state.Duraklatildi)
        {
            DuraklatmaUiGuncelle(true);
            FocusScoreText.Text = "--";
            FocusProgress.Value = 0;
            FocusProgress.Foreground = (Brush)FindResource("MutedTextBrush");
            FocusScoreText.Foreground = (Brush)FindResource("MutedTextBrush");
            StatusText.Text = state.DurumMesaji;
            _overlayWindow?.Update(state, _ayarlar.OdakEsigi);
            FaceStatusText.Text = state.Biyometrik?.YuzVar == true ? "Var" : "Yok";
            PipeStatusText.Text = state.PipeBagli ? "Bağlı" : "Bekleniyor";
            InterventionStatusText.Text = "Duraklatıldı";
            ForegroundProcessText.Text = "-";
            CalibrationStatusText.Text = state.Biyometrik?.KalibrasyonTamam == true ? "Tamam" : "Bekleniyor";
            AnalizDegerleriniGoster(state);
            HataGoster(state.Hata);
            DetaylariGoster(state);
            return;
        }

        DuraklatmaUiGuncelle(false);
        int puan = state.Odak?.Puan ?? 100;
        FocusScoreText.Text = puan.ToString(CultureInfo.CurrentCulture);
        FocusProgress.Value = puan;
        FocusProgress.Foreground = PuanFircasi(puan);
        FocusScoreText.Foreground = PuanFircasi(puan);
        StatusText.Text = state.DurumMesaji;
        _overlayWindow?.Update(state, _ayarlar.OdakEsigi);
        OturumuGuncelle(state, puan);

        FaceStatusText.Text = state.Biyometrik?.YuzVar == true ? "Var" : "Yok";
        PipeStatusText.Text = state.PipeBagli ? "Bağlı" : "Bekleniyor";
        InterventionStatusText.Text = state.MudahaleAktif ? "Açık" : "Kapalı";
        ForegroundProcessText.Text = string.IsNullOrWhiteSpace(state.Surec?.OnPlanSurec) ? "-" : state.Surec.OnPlanSurec;
        CalibrationStatusText.Text = state.Biyometrik is null
            ? "-"
            : state.Biyometrik.KalibrasyonTamam
                ? "Tamam"
                : state.Biyometrik.KalibrasyonKalanSaniye > 0
                    ? $"{state.Biyometrik.KalibrasyonKalanSaniye} sn"
                    : "Bekleniyor";

        AnalizDegerleriniGoster(state);
        HataGoster(state.Hata);
        DetaylariGoster(state);

        if (state.Odak is not null)
        {
            _odakGecmisi.Add(state.Odak.Puan);
            if (_odakGecmisi.Count > HistoryLimit)
            {
                _odakGecmisi.RemoveAt(0);
            }

            OdakGecmisiniCiz();
            UyariKosullariniKontrolEt(state, state.Odak.Puan);
        }
    }

    private void KameraKaresiniGoster(byte[] jpegBytes)
    {
        if (!_kameraOnizlemeAktif || jpegBytes.Length == 0)
        {
            return;
        }

        try
        {
            using var stream = new MemoryStream(jpegBytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            CameraImage.Source = bitmap;
            CameraPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
        }
    }

    private void KamerayiTemizle()
    {
        CameraImage.Source = null;
        CameraPlaceholder.Text = string.Empty;
        CameraPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void OdakGecmisiniCiz()
    {
        HistoryCanvas.Children.Clear();
        if (_odakGecmisi.Count < 2 || HistoryCanvas.ActualWidth <= 0 || HistoryCanvas.ActualHeight <= 0)
        {
            return;
        }

        double width = HistoryCanvas.ActualWidth;
        double height = HistoryCanvas.ActualHeight;
        double esikY = height - (_ayarlar.OdakEsigi / 100.0 * height);

        HistoryCanvas.Children.Add(new Line
        {
            X1 = 0,
            X2 = width,
            Y1 = esikY,
            Y2 = esikY,
            Stroke = (Brush)FindResource("WarningBrush"),
            StrokeThickness = 1,
            Opacity = 0.55
        });

        var polyline = new Polyline
        {
            Stroke = (Brush)FindResource("AccentBrush"),
            StrokeThickness = 2
        };

        double adim = width / (_odakGecmisi.Count - 1);
        for (int i = 0; i < _odakGecmisi.Count; i++)
        {
            double x = i * adim;
            double y = height - (_odakGecmisi[i] / 100.0 * height);
            polyline.Points.Add(new Point(x, y));
        }

        HistoryCanvas.Children.Add(polyline);
    }

    private void OturumuGuncelle(KararMotoruState state, int puan)
    {
        DateTimeOffset simdi = state.Zaman;
        if (_sessionSamples == 0)
        {
            _sessionStart = simdi;
        }

        if (_lastStateTime is DateTimeOffset onceki)
        {
            double saniye = (simdi - onceki).TotalSeconds;
            if (saniye is > 0 and < 5 && _lastFocusScore < _ayarlar.OdakEsigi)
            {
                _lowFocusSeconds += saniye;
            }
        }

        _sessionSamples++;
        _focusScoreTotal += puan;
        _minimumFocus = Math.Min(_minimumFocus, puan);
        _lastFocusScore = puan;
        _lastStateTime = simdi;

        DetayOturumunuGuncelle();
    }

    private void DetayOturumunuGuncelle()
    {
        TimeSpan sure = _sessionSamples == 0 ? TimeSpan.Zero : AktifOturumSuresi();
        double ortalama = _sessionSamples == 0 ? 0 : _focusScoreTotal / _sessionSamples;
        SessionDurationText.Text = sure.TotalHours >= 1
            ? sure.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture)
            : sure.ToString(@"mm\:ss", CultureInfo.CurrentCulture);
        AverageFocusText.Text = ortalama <= 0 ? "-" : ortalama.ToString("0.0", CultureInfo.CurrentCulture);
        MinimumFocusText.Text = _sessionSamples == 0 ? "-" : _minimumFocus.ToString(CultureInfo.CurrentCulture);
        LowFocusTimeText.Text = $"{_lowFocusSeconds:0} sn";
    }

    private void RaporlariYukle()
    {
        try
        {
            string[] liste = _database.GetSessionSummaries(12, _ayarlar.OdakEsigi)
                .Select(OturumOzeti)
                .ToArray();

            SessionHistoryList.ItemsSource = liste.Length == 0
                ? new[] { "Henüz raporlanabilir oturum yok." }
                : liste;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            SessionHistoryList.ItemsSource = new[] { "Rapor okunamadı: " + ex.Message };
        }
    }

    private void OturumAnaliziniGoster()
    {
        try
        {
            SessionEndAnalysis? analysis = _database.GetLatestSessionAnalysis(_ayarlar.OdakEsigi);
            if (analysis is null)
            {
                return;
            }

            OturumAnalizSonucu sonuc = _oturumAnalizMotoru.Uret(analysis, _ayarlar.OdakEsigi);
            if (_sessionAnalysisWindow is null)
            {
                _sessionAnalysisWindow = new SessionAnalysisWindow { Owner = this };
                _sessionAnalysisWindow.Closed += (_, _) => _sessionAnalysisWindow = null;
            }

            _sessionAnalysisWindow.SetAnalysis(analysis, sonuc);
            _sessionAnalysisWindow.Show();
            _sessionAnalysisWindow.Activate();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            HataGoster("Oturum analizi okunamadı: " + ex.Message);
        }
    }

    private void AnalizDegerleriniGoster(KararMotoruState state)
    {
        BiyometrikVeri? biyometrik = state.Biyometrik;
        GirdiAktiviteOzeti? girdi = state.Girdi;

        AnalysisStatusText.Text = state.Duraklatildi
            ? biyometrik?.AnalizDurumu ?? "Ara modu"
            : biyometrik?.AnalizDurumu ?? "Analiz bekleniyor";
        GazeDirectionText.Text = biyometrik?.GazeYon ?? "-";
        PostureStatusText.Text = biyometrik?.BasDurum ?? "-";
        EarText.Text = biyometrik is null
            ? "-"
            : $"{biyometrik.Ear:0.00} / {biyometrik.EarEsik:0.00}";
        PoseText.Text = biyometrik is null
            ? "-"
            : $"{biyometrik.GazeSapma:0.000} / {biyometrik.OneSapma:0.0}";
        BlinkText.Text = (biyometrik?.KirpmaSayisi ?? 0).ToString(CultureInfo.CurrentCulture);
        KeysPerMinuteText.Text = (girdi?.TusDakika ?? 0).ToString("0.0", CultureInfo.CurrentCulture);
        MouseIdleText.Text = $"{(girdi?.FarePikselDakika ?? 0):0} / {(girdi?.HareketsizSaniye ?? 0):0} sn";
    }

    private void UyariKosullariniKontrolEt(KararMotoruState state, int puan)
    {
        if (puan < _ayarlar.OdakEsigi)
        {
            UyariGoster(
                "odak-esik",
                "Odak düştü",
                $"Odak puanı eşik değerinin altına indi: {puan}/100.",
                TimeSpan.FromSeconds(15));
        }

        BiyometrikVeri? biyometrik = state.Biyometrik;
        bool gozKapali = biyometrik?.YuzVar == true &&
            biyometrik.EarEsik > 0 &&
            biyometrik.Ear > 0 &&
            biyometrik.Ear < biyometrik.EarEsik;

        if (gozKapali)
        {
            _uykuBaslangic ??= state.Zaman;
            if (state.Zaman - _uykuBaslangic.Value >= TimeSpan.FromSeconds(2))
            {
                UyariGoster(
                    "uyuma",
                    "Uyuma uyarısı",
                    "Gözlerin kısa süredir kapalı görünüyor.",
                    TimeSpan.FromSeconds(20));
            }
        }
        else
        {
            _uykuBaslangic = null;
        }

        bool bosBakis = biyometrik?.YuzVar == true &&
            biyometrik.KalibrasyonTamam &&
            Math.Abs(biyometrik.GazeSapma) <= Math.Max(_ayarlar.GazeEsigi, 0.015) &&
            (state.Girdi?.HareketsizSaniye ?? 0) >= 15;

        if (bosBakis)
        {
            UyariGoster(
                "bos-bakis",
                "Boş bakış uyarısı",
                "15 saniyedir ekrana bakıyorsun ama klavye/fare aktivitesi görünmüyor.",
                TimeSpan.FromSeconds(30));
        }
    }

    private void UyariGoster(string anahtar, string baslik, string mesaj, TimeSpan bekleme)
    {
        DateTimeOffset simdi = DateTimeOffset.Now;
        if (_kapaniyor ||
            (_uyariZamanlari.TryGetValue(anahtar, out DateTimeOffset sonUyari) && simdi - sonUyari < bekleme))
        {
            return;
        }

        _sonUyariZamani = simdi;
        _uyariZamanlari[anahtar] = simdi;
        LastAlertText.Text = $"{_sonUyariZamani:HH:mm:ss} - {baslik}";
        SesliUyariCal();
        _uyariPenceresi?.Close();
        _uyariPenceresi = new FocusAlertWindow(baslik, mesaj, _ayarlar.UyariMesajiNotu);
        _uyariPenceresi.Closed += (_, _) => _uyariPenceresi = null;
        _uyariPenceresi.Show();
    }

    private static void SesliUyariCal()
    {
        try
        {
            SystemSounds.Exclamation.Play();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void HataGoster(string? mesaj)
    {
        _sonHata = string.IsNullOrWhiteSpace(mesaj) ? "Yok" : mesaj;
        ErrorText.Text = _sonHata;
    }

    private void DetaylariGoster(KararMotoruState state)
    {
        BlacklistText.Text = KaraListeOzeti(state.Surec);
        PenaltyList.ItemsSource = state.Odak?.Cezalar.Count > 0
            ? state.Odak.Cezalar.Select(ceza => $"{ceza.Kaynak}: -{ceza.Deger:0.#}  {ceza.Aciklama}").ToArray()
            : new[] { "Ceza yok" };
    }

    private void RefreshReportsButton_Click(object sender, RoutedEventArgs e) => RaporlariYukle();

    private void DuraklatmaUiGuncelle(bool aktif)
    {
        if (_duraklatildi != aktif)
        {
            DateTimeOffset simdi = DateTimeOffset.Now;
            if (aktif)
            {
                _pauseStartedAt = simdi;
            }
            else if (_pauseStartedAt is DateTimeOffset baslangic)
            {
                _pausedDuration += simdi - baslangic;
                _pauseStartedAt = null;
                _lastStateTime = null;
            }
        }

        _duraklatildi = aktif;
        PauseButton.Content = aktif ? "Devam et" : "Ara ver";
        DetayOturumunuGuncelle();
    }

    private void PythonLogGoster(string mesaj)
    {
        if (mesaj.Contains("kapandı", StringComparison.OrdinalIgnoreCase) ||
            mesaj.Contains("kapandi", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = mesaj;
        }
    }

    private void AnaPencereyiGoster()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
    }

    private void KameraAyarlariniUygula()
    {
        _pythonCameraWorker.PreviewFps = _ayarlar.KameraOnizlemeFps;
        _pythonCameraWorker.AnalysisFps = _ayarlar.KameraAnalizFps;
    }

    private Brush PuanFircasi(int puan)
    {
        if (puan < _ayarlar.OdakEsigi)
        {
            return (Brush)FindResource("DangerBrush");
        }

        return puan < 75
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("AccentBrush");
    }

    private void OturumSayaclariniSifirla()
    {
        _sessionStart = DateTimeOffset.Now;
        _lastStateTime = null;
        _pauseStartedAt = null;
        _sessionSamples = 0;
        _minimumFocus = 100;
        _lastFocusScore = 100;
        _focusScoreTotal = 0;
        _lowFocusSeconds = 0;
        _pausedDuration = TimeSpan.Zero;
        _uykuBaslangic = null;
        _odakGecmisi.Clear();
        OdakGecmisiniCiz();
        DetayOturumunuGuncelle();
        PenaltyList.ItemsSource = new[] { "Ceza yok" };
        BlacklistText.Text = "Yok";
        HataGoster(null);
    }

    private TimeSpan AktifOturumSuresi()
    {
        DateTimeOffset simdi = DateTimeOffset.Now;
        TimeSpan duraklama = _pausedDuration;
        if (_pauseStartedAt is DateTimeOffset baslangic)
        {
            duraklama += simdi - baslangic;
        }

        TimeSpan sure = simdi - _sessionStart - duraklama;
        return sure > TimeSpan.Zero ? sure : TimeSpan.Zero;
    }

    private static string KaraListeOzeti(SurecTaramaSonucu? sonuc)
    {
        if (sonuc is null || sonuc.KaraListedekiSurecler.Count == 0)
        {
            return "Yok";
        }

        return $"{sonuc.KaraListedekiSurecler.Count} süreç ({string.Join(", ", sonuc.KaraListedekiSurecler)})";
    }

    private static string OturumOzeti(SessionSummary summary)
    {
        string sure = summary.Duration.TotalHours >= 1
            ? summary.Duration.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture)
            : summary.Duration.ToString(@"mm\:ss", CultureInfo.CurrentCulture);

        return $"{summary.EndedAt:dd.MM HH:mm}  Süre: {sure}  Ort: {summary.AverageFocus:0.0}  Min: {summary.MinimumFocus}  Düşük: {summary.LowFocusSamples}  Kara liste: {summary.BlacklistSamples}";
    }
}
