using System.Globalization;
using System.Windows;
using FokusKararMotoru.Models;

namespace FokusKararMotoru;

public partial class DetailsWindow : Window
{
    public DetailsWindow()
    {
        InitializeComponent();
    }

    public event EventHandler? RefreshReportsRequested;

    public void UpdateState(KararMotoruState state)
    {
        BlacklistText.Text = KaraListeOzeti(state.Surec);
        SetError(state.Hata);

        KeysPerMinuteText.Text = (state.Girdi?.TusDakika ?? 0).ToString("0.0", CultureInfo.CurrentCulture);
        MousePerMinuteText.Text = (state.Girdi?.FarePikselDakika ?? 0).ToString("0", CultureInfo.CurrentCulture);
        IdleSecondsText.Text = (state.Girdi?.HareketsizSaniye ?? 0).ToString("0", CultureInfo.CurrentCulture);
        GazeDirectionText.Text = state.Biyometrik?.GazeYon ?? "-";
        PostureStatusText.Text = state.Biyometrik?.BasDurum ?? "-";
        EarText.Text = state.Biyometrik is null
            ? "-"
            : $"{state.Biyometrik.Ear:0.00} / {state.Biyometrik.EarEsik:0.00}";
        PoseText.Text = state.Biyometrik is null
            ? "-"
            : $"{state.Biyometrik.GazeSapma:0.000} / {state.Biyometrik.OneSapma:0.0}";
        BlinkText.Text = (state.Biyometrik?.KirpmaSayisi ?? 0).ToString(CultureInfo.CurrentCulture);

        PenaltyList.ItemsSource = state.Odak?.Cezalar.Count > 0
            ? state.Odak.Cezalar.Select(ceza => $"{ceza.Kaynak}: -{ceza.Deger:0.#}  {ceza.Aciklama}").ToArray()
            : new[] { "Ceza yok" };
    }

    public void UpdateSessionStats(TimeSpan duration, double averageFocus, int minimumFocus, double lowFocusSeconds)
    {
        SessionDurationText.Text = duration.TotalHours >= 1
            ? duration.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture)
            : duration.ToString(@"mm\:ss", CultureInfo.CurrentCulture);
        AverageFocusText.Text = averageFocus <= 0 ? "-" : averageFocus.ToString("0.0", CultureInfo.CurrentCulture);
        MinimumFocusText.Text = minimumFocus.ToString(CultureInfo.CurrentCulture);
        LowFocusTimeText.Text = $"{lowFocusSeconds:0} sn";
    }

    public void SetReports(IEnumerable<string> reports)
    {
        SessionHistoryList.ItemsSource = reports.ToArray();
    }

    public void SetError(string? message)
    {
        ErrorText.Text = string.IsNullOrWhiteSpace(message) ? "Yok" : message;
    }

    private void RefreshReportsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshReportsRequested?.Invoke(this, EventArgs.Empty);
    }

    private static string KaraListeOzeti(SurecTaramaSonucu? sonuc)
    {
        if (sonuc is null || sonuc.KaraListedekiSurecler.Count == 0)
        {
            return "Yok";
        }

        return $"{sonuc.KaraListedekiSurecler.Count} süreç ({string.Join(", ", sonuc.KaraListedekiSurecler)}) [Ceza: -{sonuc.KaraListeCezasi}]";
    }
}
