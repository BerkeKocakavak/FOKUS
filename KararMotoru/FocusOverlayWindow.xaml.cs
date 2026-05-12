using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using FokusKararMotoru.Models;

namespace FokusKararMotoru;

public partial class FocusOverlayWindow : Window
{
    private readonly DispatcherTimer _pinTimer;

    public FocusOverlayWindow()
    {
        InitializeComponent();

        _pinTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _pinTimer.Tick += (_, _) => PinToTopRight();
    }

    public event EventHandler? RestoreRequested;

    public void Update(KararMotoruState state, int odakEsigi)
    {
        if (state.Duraklatildi)
        {
            ScoreText.Text = "--";
            DetailText.Text = "Ara";
            Brush duraklatmaRengi = new SolidColorBrush(Color.FromRgb(100, 112, 137));
            ScoreText.Foreground = duraklatmaRengi;
            StatusDot.Fill = duraklatmaRengi;
            RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
            return;
        }

        int puan = state.Odak?.Puan ?? 100;
        ScoreText.Text = puan.ToString();
        DetailText.Text = state.PipeBagli
            ? state.Biyometrik?.YuzVar == true ? "Yüz var" : "Yüz yok"
            : "Pipe yok";

        Brush renk = PuanFircasi(puan, odakEsigi);
        ScoreText.Foreground = renk;
        StatusDot.Fill = renk;
        RootBorder.BorderBrush = renk;
    }

    public void SetStopped()
    {
        ScoreText.Text = "--";
        DetailText.Text = "Kapalı";
        Brush renk = new SolidColorBrush(Color.FromRgb(100, 112, 137));
        ScoreText.Foreground = renk;
        StatusDot.Fill = renk;
        RootBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225));
    }

    protected override void OnClosed(EventArgs e)
    {
        _pinTimer.Stop();
        base.OnClosed(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PinToTopRight();
        _pinTimer.Start();
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PinToTopRight()
    {
        Left = SystemParameters.WorkArea.Right - Width - 16;
        Top = SystemParameters.WorkArea.Top + 16;

        // Windows bazen topmost sırasını değiştiriyor; küçük göstergeyi tekrar öne alıyoruz.
        Topmost = false;
        Topmost = true;
    }

    private static Brush PuanFircasi(int puan, int odakEsigi)
    {
        if (puan < odakEsigi)
        {
            return new SolidColorBrush(Color.FromRgb(185, 28, 28));
        }

        return puan < 75
            ? new SolidColorBrush(Color.FromRgb(180, 83, 9))
            : new SolidColorBrush(Color.FromRgb(15, 118, 110));
    }
}
