using System.Windows;

namespace FokusKararMotoru;

public partial class WorkCheckWindow : Window
{
    public WorkCheckWindow()
    {
        InitializeComponent();
    }

    public event EventHandler<bool>? Answered;

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Left = SystemParameters.WorkArea.Right - Width - 24;
        Top = SystemParameters.WorkArea.Top + 220;
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        Answered?.Invoke(this, true);
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        Answered?.Invoke(this, false);
        Close();
    }
}
