using System.Windows;
using System.Windows.Threading;

namespace FokusKararMotoru;

public partial class FocusAlertWindow : Window
{
    private readonly DispatcherTimer _timer;

    public FocusAlertWindow(string baslik, string mesaj, string notMesaji)
    {
        InitializeComponent();
        TitleText.Text = baslik;
        MessageText.Text = mesaj;
        NoteText.Text = notMesaji;

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
