using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using FokusKararMotoru.Models;
using FokusKararMotoru.Services;

namespace FokusKararMotoru;

public partial class MainWindow : Window
{
    private const int HistoryLimit = 120;
    private const string HistoryCsvHeader = "session_id,zaman,odak_puani,yuz_var,pipe_bagli,kara_liste,tus_dakika,fare_piksel_dakika,hareketsiz_saniye";

    private readonly string _projeKoku;
    private readonly string _sessionId = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
    private readonly KararMotoruWorker _kararMotoruWorker;
    private readonly PythonCameraWorker _pythonCameraWorker;
    private readonly DispatcherTimer _frameTimer;
    private readonly List<int> _odakGecmisi = [];

    private KararMotoruAyarlari _ayarlar;
    private string _historyCsvPath = string.Empty;
    private DateTime _sonFrameZamani;
    private DateTimeOffset _sonUyariZamani = DateTimeOffset.MinValue;
    private DateTimeOffset _sessionStart = DateTimeOffset.Now;
    private DateTimeOffset _lastHistoryWrite = DateTimeOffset.MinValue;
    private DateTimeOffset? _lastStateTime;
    private FocusAlertWindow? _uyariPenceresi;
    private FocusOverlayWindow? _overlayWindow;
    private int _sessionSamples;
    private int _minimumFocus = 100;
    private int _lastFocusScore = 100;
    private double _focusScoreTotal;
    private double _lowFocusSeconds;
    private bool _kapaniyor;
    private bool _kapanisTamamlandi;
    private bool _baslatiliyor;
    private bool _bagimlilikKontroluGecti;

    public MainWindow()
    {
        InitializeComponent();

        _projeKoku = ProjeYolu.Bul();
        _historyCsvPath = System.IO.Path.Combine(_projeKoku, "odak_oturum_gecmisi.csv");
        _pythonCameraWorker = new PythonCameraWorker(_projeKoku);
        _ayarlar = AyarDeposu.YukleVeyaOlustur(_projeKoku);
        KameraAyarlariniUygula();
        _kararMotoruWorker = new KararMotoruWorker(_projeKoku, _ayarlar, _pythonCameraWorker.PipeName);
        _frameTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _frameTimer.Tick += (_, _) => KameraKaresiniGuncelle();

        _kararMotoruWorker.StateChanged += (_, state) =>
            Dispatcher.BeginInvoke(() => DurumuGoster(state));
        _pythonCameraWorker.LogChanged += (_, mesaj) =>
            Dispatcher.BeginInvoke(() => PythonLogGoster(mesaj));
        HistoryCanvas.SizeChanged += (_, _) => OdakGecmisiniCiz();

        AyarlariFormaYukle();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _overlayWindow = new FocusOverlayWindow();
        _overlayWindow.RestoreRequested += (_, _) => AnaPencereyiGoster();
        _overlayWindow.Show();
        RaporlariYukle();
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
        _frameTimer.Stop();
        _uyariPenceresi?.Close();
        _overlayWindow?.Close();
        _overlayWindow = null;

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
        _frameTimer.Stop();
        await _pythonCameraWorker.StopAsync(TimeSpan.FromSeconds(2));
        await _kararMotoruWorker.StopAsync(TimeSpan.FromSeconds(2));
        _overlayWindow?.SetStopped();
        StatusText.Text = "Durduruldu";
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(ThresholdTextBox.Text, out int odakEsigi))
        {
            StatusText.Text = "Odak eşiği sayı olmalı.";
            return;
        }

        if (!int.TryParse(PreviewFpsTextBox.Text, out int previewFps) ||
            !int.TryParse(AnalysisFpsTextBox.Text, out int analysisFps))
        {
            StatusText.Text = "FPS değerleri sayı olmalı.";
            return;
        }

        if (!DoubleOku(KeyboardExpectedTextBox.Text, out double klavyeBeklenen) ||
            !DoubleOku(MouseExpectedTextBox.Text, out double fareBeklenen) ||
            !DoubleOku(EarPenaltyFactorTextBox.Text, out double earCezaKatsayisi) ||
            !DoubleOku(EarPenaltyCapTextBox.Text, out double earCezaTavani) ||
            !DoubleOku(GazeThresholdTextBox.Text, out double gazeEsigi) ||
            !DoubleOku(GazePenaltyFactorTextBox.Text, out double gazeCezaKatsayisi) ||
            !DoubleOku(PostureThresholdTextBox.Text, out double posturEsigi) ||
            !DoubleOku(PosturePenaltyFactorTextBox.Text, out double posturCezaKatsayisi))
        {
            StatusText.Text = "Hassasiyet alanları sayı olmalı.";
            return;
        }

        bool fpsDegisti = previewFps != _ayarlar.KameraOnizlemeFps ||
                          analysisFps != _ayarlar.KameraAnalizFps;

        _ayarlar.OdakEsigi = odakEsigi;
        _ayarlar.KameraOnizlemeFps = previewFps;
        _ayarlar.KameraAnalizFps = analysisFps;
        _ayarlar.KlavyeDakikaBeklenen = klavyeBeklenen;
        _ayarlar.FarePikselDakikaBeklenen = fareBeklenen;
        _ayarlar.EarCezaKatsayisi = earCezaKatsayisi;
        _ayarlar.EarCezaTavani = earCezaTavani;
        _ayarlar.GazeEsigi = gazeEsigi;
        _ayarlar.GazeCezaKatsayisi = gazeCezaKatsayisi;
        _ayarlar.PosturEsigi = posturEsigi;
        _ayarlar.PosturCezaKatsayisi = posturCezaKatsayisi;
        _ayarlar.KaraListe = ListeOku(BlacklistSettingsTextBox.Text);
        _ayarlar.BeyazListe = ListeOku(WhitelistSettingsTextBox.Text);
        _ayarlar.Normalize();

        _kararMotoruWorker.AyarlariGuncelle(_ayarlar);
        KameraAyarlariniUygula();
        AyarlariFormaYukle();

        if (fpsDegisti && _pythonCameraWorker.Calisiyor)
        {
            StatusText.Text = "FPS ayarları uygulanıyor...";
            await _pythonCameraWorker.StopAsync(TimeSpan.FromSeconds(2));
            _pythonCameraWorker.Start();
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
            if (!_bagimlilikKontroluGecti)
            {
                StatusText.Text = "Başlangıç kontrolü yapılıyor...";
                PythonDependencyCheckResult kontrol = await _pythonCameraWorker.CheckDependenciesAsync(fast: true, timeout: TimeSpan.FromSeconds(8));
                if (!kontrol.Ok)
                {
                    ErrorText.Text = kontrol.Message + Environment.NewLine + "Eksikleri kurmak için: python -m pip install -r requirements.txt";
                    StatusText.Text = "Python paketleri eksik.";
                    _overlayWindow?.SetStopped();
                    return;
                }

                _bagimlilikKontroluGecti = true;
            }

            _kararMotoruWorker.MudahaleDurumuAyarla(InterventionToggle.IsChecked == true);
            _pythonCameraWorker.Start();
            _kararMotoruWorker.Start();
            _frameTimer.Start();
            StatusText.Text = "Python kamera ve karar motoru çalışıyor.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Başlatılamadı: " + ex.Message;
            ErrorText.Text = ex.Message;
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
        BlacklistText.Text = KaraListeOzeti(state.Surec);
        ErrorText.Text = string.IsNullOrWhiteSpace(state.Hata) ? "Yok" : state.Hata;

        KeysPerMinuteText.Text = (state.Girdi?.TusDakika ?? 0).ToString("0.0", CultureInfo.CurrentCulture);
        MousePerMinuteText.Text = (state.Girdi?.FarePikselDakika ?? 0).ToString("0", CultureInfo.CurrentCulture);
        IdleSecondsText.Text = (state.Girdi?.HareketsizSaniye ?? 0).ToString("0", CultureInfo.CurrentCulture);
        EarText.Text = state.Biyometrik is null
            ? "-"
            : $"{state.Biyometrik.Ear:0.00} / {state.Biyometrik.EarEsik:0.00}";
        PoseText.Text = state.Biyometrik is null
            ? "-"
            : $"{state.Biyometrik.GazeSapma:0.000} / {state.Biyometrik.OneSapma:0.0}";

        PenaltyList.ItemsSource = state.Odak?.Cezalar.Count > 0
            ? state.Odak.Cezalar.Select(ceza => $"{ceza.Kaynak}: -{ceza.Deger:0.#}  {ceza.Aciklama}").ToArray()
            : new[] { "Ceza yok" };

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

    private void KameraKaresiniGuncelle()
    {
        string framePath = _pythonCameraWorker.FramePath;
        if (!File.Exists(framePath))
        {
            return;
        }

        DateTime yazmaZamani = File.GetLastWriteTimeUtc(framePath);
        if (yazmaZamani <= _sonFrameZamani)
        {
            return;
        }

        try
        {
            using var stream = new FileStream(framePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            CameraImage.Source = bitmap;
            CameraPlaceholder.Visibility = Visibility.Collapsed;
            _sonFrameZamani = yazmaZamani;
        }
        catch (IOException)
        {
        }
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

        TimeSpan sure = simdi - _sessionStart;
        SessionDurationText.Text = sure.TotalHours >= 1
            ? sure.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture)
            : sure.ToString(@"mm\:ss", CultureInfo.CurrentCulture);
        AverageFocusText.Text = (_focusScoreTotal / _sessionSamples).ToString("0.0", CultureInfo.CurrentCulture);
        MinimumFocusText.Text = _minimumFocus.ToString(CultureInfo.CurrentCulture);
        LowFocusTimeText.Text = $"{_lowFocusSeconds:0} sn";

        if (simdi - _lastHistoryWrite >= TimeSpan.FromSeconds(1))
        {
            OturumCsvYaz(state, puan, simdi);
            _lastHistoryWrite = simdi;
        }
    }

    private void OturumCsvYaz(KararMotoruState state, int puan, DateTimeOffset zaman)
    {
        try
        {
            CsvBasliginiHazirla();

            string karaListe = state.Surec is null
                ? string.Empty
                : string.Join("|", state.Surec.KaraListedekiSurecler);
            string satir = string.Join(",",
                Csv(_sessionId),
                Csv(zaman.ToString("O", CultureInfo.InvariantCulture)),
                Csv(puan.ToString(CultureInfo.InvariantCulture)),
                Csv((state.Biyometrik?.YuzVar == true).ToString(CultureInfo.InvariantCulture)),
                Csv(state.PipeBagli.ToString(CultureInfo.InvariantCulture)),
                Csv(karaListe),
                Csv((state.Girdi?.TusDakika ?? 0).ToString("0.###", CultureInfo.InvariantCulture)),
                Csv((state.Girdi?.FarePikselDakika ?? 0).ToString("0.###", CultureInfo.InvariantCulture)),
                Csv((state.Girdi?.HareketsizSaniye ?? 0).ToString("0.###", CultureInfo.InvariantCulture)));

            File.AppendAllText(_historyCsvPath, satir + Environment.NewLine);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void RefreshReportsButton_Click(object sender, RoutedEventArgs e)
    {
        RaporlariYukle();
    }

    private void RaporlariYukle()
    {
        if (!File.Exists(_historyCsvPath))
        {
            SessionHistoryList.ItemsSource = new[] { "Henüz kayıtlı oturum yok." };
            return;
        }

        var oturumlar = new Dictionary<string, OturumRaporu>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (string satir in File.ReadLines(_historyCsvPath).Skip(1))
            {
                string[] alanlar = CsvAyir(satir);
                if (alanlar.Length < 9)
                {
                    continue;
                }

                string sessionId = alanlar[0];
                if (!DateTimeOffset.TryParse(alanlar[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset zaman))
                {
                    continue;
                }

                if (!int.TryParse(alanlar[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int puan))
                {
                    continue;
                }

                if (!oturumlar.TryGetValue(sessionId, out OturumRaporu? rapor))
                {
                    rapor = new OturumRaporu(sessionId);
                    oturumlar[sessionId] = rapor;
                }

                rapor.OrnekEkle(zaman, puan, !string.IsNullOrWhiteSpace(alanlar[5]), _ayarlar.OdakEsigi);
            }

            string[] liste = oturumlar.Values
                .OrderByDescending(rapor => rapor.SonZaman)
                .Take(12)
                .Select(rapor => rapor.Ozetle())
                .ToArray();

            SessionHistoryList.ItemsSource = liste.Length == 0
                ? new[] { "Henüz raporlanabilir oturum yok." }
                : liste;
        }
        catch (IOException ex)
        {
            SessionHistoryList.ItemsSource = new[] { "Rapor okunamadı: " + ex.Message };
        }
    }

    private void CsvBasliginiHazirla()
    {
        if (!File.Exists(_historyCsvPath) || new FileInfo(_historyCsvPath).Length == 0)
        {
            File.WriteAllText(_historyCsvPath, HistoryCsvHeader + Environment.NewLine);
            return;
        }

        string? ilkSatir = File.ReadLines(_historyCsvPath).FirstOrDefault();
        if (string.Equals(ilkSatir, HistoryCsvHeader, StringComparison.Ordinal))
        {
            return;
        }

        string yedek = System.IO.Path.Combine(
            _projeKoku,
            "odak_oturum_gecmisi_legacy_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
        File.Move(_historyCsvPath, yedek, overwrite: true);
        File.WriteAllText(_historyCsvPath, HistoryCsvHeader + Environment.NewLine);
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

    private void AyarlariFormaYukle()
    {
        ThresholdTextBox.Text = _ayarlar.OdakEsigi.ToString(CultureInfo.CurrentCulture);
        PreviewFpsTextBox.Text = _ayarlar.KameraOnizlemeFps.ToString(CultureInfo.CurrentCulture);
        AnalysisFpsTextBox.Text = _ayarlar.KameraAnalizFps.ToString(CultureInfo.CurrentCulture);
        KeyboardExpectedTextBox.Text = _ayarlar.KlavyeDakikaBeklenen.ToString("0.##", CultureInfo.CurrentCulture);
        MouseExpectedTextBox.Text = _ayarlar.FarePikselDakikaBeklenen.ToString("0.##", CultureInfo.CurrentCulture);
        EarPenaltyFactorTextBox.Text = _ayarlar.EarCezaKatsayisi.ToString("0.##", CultureInfo.CurrentCulture);
        EarPenaltyCapTextBox.Text = _ayarlar.EarCezaTavani.ToString("0.##", CultureInfo.CurrentCulture);
        GazeThresholdTextBox.Text = _ayarlar.GazeEsigi.ToString("0.###", CultureInfo.CurrentCulture);
        GazePenaltyFactorTextBox.Text = _ayarlar.GazeCezaKatsayisi.ToString("0.##", CultureInfo.CurrentCulture);
        PostureThresholdTextBox.Text = _ayarlar.PosturEsigi.ToString("0.##", CultureInfo.CurrentCulture);
        PosturePenaltyFactorTextBox.Text = _ayarlar.PosturCezaKatsayisi.ToString("0.##", CultureInfo.CurrentCulture);
        BlacklistSettingsTextBox.Text = string.Join(Environment.NewLine, _ayarlar.KaraListe);
        WhitelistSettingsTextBox.Text = string.Join(Environment.NewLine, _ayarlar.BeyazListe);
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

    private static bool DoubleOku(string text, out double value)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
               double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string[] ListeOku(string text)
    {
        return text
            .Split(["\r\n", "\n", "\r", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string Csv(string text)
    {
        if (text.Contains('"') || text.Contains(',') || text.Contains('\n') || text.Contains('\r'))
        {
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        return text;
    }

    private static string[] CsvAyir(string satir)
    {
        var alanlar = new List<string>();
        var aktif = new System.Text.StringBuilder();
        bool tirnakIcinde = false;

        for (int i = 0; i < satir.Length; i++)
        {
            char karakter = satir[i];
            if (karakter == '"')
            {
                if (tirnakIcinde && i + 1 < satir.Length && satir[i + 1] == '"')
                {
                    aktif.Append('"');
                    i++;
                }
                else
                {
                    tirnakIcinde = !tirnakIcinde;
                }
            }
            else if (karakter == ',' && !tirnakIcinde)
            {
                alanlar.Add(aktif.ToString());
                aktif.Clear();
            }
            else
            {
                aktif.Append(karakter);
            }
        }

        alanlar.Add(aktif.ToString());
        return alanlar.ToArray();
    }

    private static string KaraListeOzeti(SurecTaramaSonucu? sonuc)
    {
        if (sonuc is null || sonuc.KaraListedekiSurecler.Count == 0)
        {
            return "Yok";
        }

        return $"{sonuc.KaraListedekiSurecler.Count} süreç ({string.Join(", ", sonuc.KaraListedekiSurecler)}) [Ceza: -{sonuc.KaraListeCezasi}]";
    }

    private sealed class OturumRaporu
    {
        private int _toplamPuan;
        private int _ornekSayisi;
        private int _dusukOdakSayisi;
        private int _karaListeSayisi;
        private int _minimumPuan = 100;

        public OturumRaporu(string sessionId)
        {
            SessionId = sessionId;
        }

        public string SessionId { get; }

        public DateTimeOffset SonZaman { get; private set; } = DateTimeOffset.MinValue;

        public void OrnekEkle(DateTimeOffset zaman, int puan, bool karaListeVar, int odakEsigi)
        {
            _ornekSayisi++;
            _toplamPuan += puan;
            _minimumPuan = Math.Min(_minimumPuan, puan);
            SonZaman = zaman > SonZaman ? zaman : SonZaman;

            if (puan < odakEsigi)
            {
                _dusukOdakSayisi++;
            }

            if (karaListeVar)
            {
                _karaListeSayisi++;
            }
        }

        public string Ozetle()
        {
            double ortalama = _ornekSayisi == 0 ? 0 : (double)_toplamPuan / _ornekSayisi;
            return $"{SonZaman:dd.MM HH:mm}  Ort: {ortalama:0.0}  Min: {_minimumPuan}  Düşük: {_dusukOdakSayisi}  Kara liste: {_karaListeSayisi}";
        }
    }
}
