using System.Globalization;
using System.Windows;
using FokusKararMotoru.Services;

namespace FokusKararMotoru;

public partial class SessionAnalysisWindow : Window
{
    public SessionAnalysisWindow()
    {
        InitializeComponent();
    }

    public void SetAnalysis(SessionEndAnalysis analysis, OturumAnalizSonucu sonuc)
    {
        GradeText.Text = sonuc.Derece;
        SummaryText.Text = sonuc.Ozet;
        AverageText.Text = analysis.AverageFocus.ToString("0.0", CultureInfo.CurrentCulture);
        LowFocusText.Text = analysis.LowFocusRate.ToString("P0", CultureInfo.CurrentCulture);
        DurationText.Text = SureYaz(analysis.Duration);

        FindingsList.ItemsSource = sonuc.Bulgular;
        RecommendationsList.ItemsSource = sonuc.Oneriler;
        PenaltyText.Text = CezaOzeti(analysis);
        BlacklistText.Text = KaraListeOzeti(analysis);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string SureYaz(TimeSpan sure)
    {
        if (sure.TotalHours >= 1)
        {
            return sure.ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture);
        }

        return sure.ToString(@"mm\:ss", CultureInfo.CurrentCulture);
    }

    private static string CezaOzeti(SessionEndAnalysis analysis)
    {
        if (analysis.Penalties.Count == 0)
        {
            return "Bu oturumda ceza kırılımı oluşmadı.";
        }

        return string.Join(
            Environment.NewLine,
            analysis.Penalties.Select(item =>
                $"{item.Source}: {item.Hits} kez, toplam {item.TotalPenalty:0.0} puan"));
    }

    private static string KaraListeOzeti(SessionEndAnalysis analysis)
    {
        if (analysis.Blacklist.Count == 0)
        {
            return "Kara liste yakalaması yok.";
        }

        return string.Join(
            Environment.NewLine,
            analysis.Blacklist.Select(item => $"{item.ProcessName}: {item.Hits} örnek"));
    }
}
