using System.ComponentModel;
using System.Globalization;
using System.IO;
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
    private readonly List<int> _odakGecmisi = [];

    private KararMotoruAyarlari _ayarlar;
    private DateTimeOffset _sonUyariZamani = DateTimeOffset.MinValue;
    private DateTimeOffset _sessionStart = DateTimeOffset.Now;
    private DateTimeOffset? _lastStateTime;
    private FocusAlertWindow? _uyariPenceresi;
    private FocusOverlayWindow? _overlayWindow;
    private DetailsWindow? _detailsWindow;
    private SettingsWindow? _settingsWindow;
    private int _sessionSamples;
    private int _minimumFocus = 100;
    private int _lastFocusScore = 100;
    private double _focusScoreTotal;
    private double _lowFocusSeconds;
    private bool _kapaniyor;
    private bool _kapanisTamamlandi;
    private bool _baslatiliyor;
    private bool _bagimlilikKontroluGecti;
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
        KamerayiTemizle();
        _uyariPenceresi?.Close();
        _overlayWindow?.Close();
        _detailsWindow?.Close();
        _settingsWindow?.Close();
        _overlayWindow = null;
        _detailsWindow = null;
        _settingsWindow = null;

        try
        {
            await Task.WhenAll(
                _pythonCameraWorker.StopAsync(TimeSpan.FromSeconds(2)),
                _kararMotoruWorker.StopAsync(TimeSpan.FromSeconds(2)));
        }
        catch (Exception ex)
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
        await _pythonCameraWorker.StopAsync(TimeSpan.FromSeconds(2));
        await _kararMotoruWorker.StopAsync(TimeSpan.FromSeconds(2));
        KamerayiTemizle();
        _overlayWindow?.SetStopped();
        RaporlariYukle();
        StatusText.Text = "Durduruldu";
    }

    private void ShowDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_detailsWindow is null)
        {
            _detailsWindow = new DetailsWindow { Owner = this };
            _detailsWindow.RefreshReportsRequested += (_, _) => RaporlariYukle();
            _detailsWindow.Closed += (_, _) => _detailsWindow = null;
        }

        _detailsWindow.Show();
        _detailsWindow.Activate();
        _detailsWindow.SetError(_sonHata);
        DetayOturumunuGuncelle();
        RaporlariYukle();
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

        if ((e.PreviewFpsChanged || e.AnalysisFpsChanged) && _pythonCameraWorker.Calisiyor)
        {
            try
            {
                StatusText.Text = "FPS ayarlari uygulaniyor...";
                await _pythonCameraWorker.StopAsync(TimeSpan.FromSeconds(2));
                KamerayiTemizle();
                _pythonCameraWorker.Start();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                HataGoster("FPS ayarlari uygulanamadi: " + ex.Message);
                StatusText.Text = "FPS ayarlari uygulanamadi.";
                return;
            }
        }

        StatusText.Text = "Ayarlar kaydedildi.";
    }

    private void InterventionToggle_Changed(object sender, RoutedEventArgs e)
    {
        bool aktif = InterventionToggle.IsChecked == true;
        _kararMotoruWorker.MudahaleDurumuAyarla(aktif);
        InterventionStatusText.Text = aktif ? "Açık" : "Kapalı";
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

            _kararMotoruWorker.MudahaleDurumuAyarla(InterventionToggle.IsChecked == true);
            _pythonCameraWorker.Start();
            _kararMotoruWorker.Start();
            StatusText.Text = "Python kamera ve karar motoru çalışıyor.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Başlatılamadı: " + ex.Message;
            HataGoster(ex.Message);
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
        AnalysisStatusText.Text = state.Biyometrik?.AnalizDurumu ?? "-";
        CalibrationStatusText.Text = state.Biyometrik is null
            ? "-"
            : state.Biyometrik.KalibrasyonTamam
                ? "Tamam"
                : state.Biyometrik.KalibrasyonKalanSaniye > 0
                    ? $"{state.Biyometrik.KalibrasyonKalanSaniye} sn"
                    : "Bekleniyor";

        HataGoster(state.Hata);
        _detailsWindow?.UpdateState(state);

        if (state.Odak is not null)
        {
            _odakGecmisi.Add(state.Odak.Puan);
            if (_odakGecmisi.Count > HistoryLimit)
            {
                _odakGecmisi.RemoveAt(0);
            }

            OdakGecmisiniCiz();
            if (state.Odak.MudahaleGerekli)
            {
                UyariGoster(state.Odak.Puan, state.DurumMesaji);
            }
        }
    }

    private void KameraKaresiniGoster(byte[] jpegBytes)
    {
        if (jpegBytes.Length == 0)
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
        if (_detailsWindow is null)
        {
            return;
        }

        TimeSpan sure = _sessionSamples == 0
            ? TimeSpan.Zero
            : DateTimeOffset.Now - _sessionStart;
        double ortalama = _sessionSamples == 0 ? 0 : _focusScoreTotal / _sessionSamples;
        _detailsWindow.UpdateSessionStats(sure, ortalama, _minimumFocus, _lowFocusSeconds);
    }

    private void RaporlariYukle()
    {
        if (_detailsWindow is null)
        {
            return;
        }

        try
        {
            string[] liste = _database.GetSessionSummaries(12, _ayarlar.OdakEsigi)
                .Select(OturumOzeti)
                .ToArray();

            _detailsWindow.SetReports(liste.Length == 0
                ? new[] { "Henuz raporlanabilir oturum yok." }
                : liste);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
        {
            _detailsWindow.SetReports(["Rapor okunamadı: " + ex.Message]);
        }
    }

    private void UyariGoster(int puan, string mesaj)
    {
        if (_kapaniyor || DateTimeOffset.Now - _sonUyariZamani < TimeSpan.FromSeconds(15))
        {
            return;
        }

        _sonUyariZamani = DateTimeOffset.Now;
        LastAlertText.Text = $"{_sonUyariZamani:HH:mm:ss} - {mesaj}";
        _uyariPenceresi?.Close();
        _uyariPenceresi = new FocusAlertWindow(puan, mesaj);
        _uyariPenceresi.Closed += (_, _) => _uyariPenceresi = null;
        _uyariPenceresi.Show();
    }

    private void HataGoster(string? mesaj)
    {
        _sonHata = string.IsNullOrWhiteSpace(mesaj) ? "Yok" : mesaj;
        _detailsWindow?.SetError(_sonHata);
    }

    private void PythonLogGoster(string mesaj)
    {
        if (mesaj.Contains("kapandi", StringComparison.OrdinalIgnoreCase))
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

    private static string OturumOzeti(SessionSummary summary)
    {
        string sure = summary.Duration.TotalHours >= 1
            ? summary.Duration.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture)
            : summary.Duration.ToString(@"mm\:ss", CultureInfo.CurrentCulture);

        return $"{summary.EndedAt:dd.MM HH:mm}  Süre: {sure}  Ort: {summary.AverageFocus:0.0}  Min: {summary.MinimumFocus}  Düşük: {summary.LowFocusSamples}  Kara liste: {summary.BlacklistSamples}";
    }
}
