using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FokusKararMotoru.Models;

namespace FokusKararMotoru;

public partial class FocusOverlayWindow : Window
{
    private readonly DispatcherTimer _hoverRestoreTimer;
    private Rect _hiddenBounds;
    private bool _hiddenByHover;
    private bool _closing;

    public FocusOverlayWindow()
    {
        InitializeComponent();
        MouseEnter += Window_MouseEnter;
        _hoverRestoreTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _hoverRestoreTimer.Tick += HoverRestoreTimer_Tick;
    }

    public event EventHandler? RestoreRequested;

    public void Update(KararMotoruState state, int odakEsigi)
    {
        if (state.Duraklatildi)
        {
            ScoreText.Text = "--";
            DetailText.Text = "Ara verildi";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            return;
        }

        if (!state.PipeBagli || state.Odak is null)
        {
            ScoreText.Text = "--";
            DetailText.Text = "Bekleniyor";
            StatusDot.Fill = new SolidColorBrush(Color.FromRgb(100, 116, 139));
            return;
        }

        int puan = state.Odak.Puan;
        ScoreText.Text = puan.ToString(System.Globalization.CultureInfo.CurrentCulture);
        DetailText.Text = puan < odakEsigi ? "Düşük odak" : "Odak iyi";
        StatusDot.Fill = puan < odakEsigi
            ? new SolidColorBrush(Color.FromRgb(220, 38, 38))
            : new SolidColorBrush(Color.FromRgb(15, 118, 110));
    }

    public void SetStopped()
    {
        ScoreText.Text = "--";
        DetailText.Text = "Durduruldu";
        StatusDot.Fill = new SolidColorBrush(Color.FromRgb(100, 116, 139));
    }

    protected override void OnClosed(EventArgs e)
    {
        _closing = true;
        _hoverRestoreTimer.Stop();
        base.OnClosed(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PinToTopRight();
    }

    private void Window_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RestoreRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_hiddenByHover || _closing)
        {
            return;
        }

        _hiddenBounds = GetWindowBoundsInScreenPixels();
        _hiddenBounds.Inflate(8, 8);
        _hiddenByHover = true;
        Hide();
        _hoverRestoreTimer.Start();
    }

    private void HoverRestoreTimer_Tick(object? sender, EventArgs e)
    {
        if (_closing || !_hiddenByHover)
        {
            return;
        }

        if (!GetCursorPos(out POINT cursor))
        {
            return;
        }

        if (_hiddenBounds.Contains(new Point(cursor.X, cursor.Y)))
        {
            return;
        }

        _hoverRestoreTimer.Stop();
        _hiddenByHover = false;
        Show();
        PinToTopRight();
    }

    private void PinToTopRight()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 16;
        Top = workArea.Top + 16;
    }

    private Rect GetWindowBoundsInScreenPixels()
    {
        nint handle = new WindowInteropHelper(this).Handle;
        if (handle != 0 &&
            GetWindowRect(handle, out RECT rect) &&
            rect.Right > rect.Left &&
            rect.Bottom > rect.Top)
        {
            return new Rect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        }

        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : Height;
        Point topLeft = PointToScreen(new Point(0, 0));
        Point bottomRight = PointToScreen(new Point(width, height));
        return new Rect(topLeft, bottomRight);
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
