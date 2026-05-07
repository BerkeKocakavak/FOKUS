using System.Windows;
using System.Windows.Threading;

namespace FokusKararMotoru;

public partial class FocusAlertWindow : Window
{
    private readonly DispatcherTimer _timer;

    public FocusAlertWindow(int puan, string mesaj)
    {
        InitializeComponent();
        MessageText.Text = $"{mesaj}\nOdak puanı: {puan}/100";

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Close();
        };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Top + 24;
        _timer.Start();
    }
}
