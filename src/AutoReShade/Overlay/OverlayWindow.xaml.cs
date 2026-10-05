using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoReShade.Core;
using AutoReShade.Core.Settings;
using AutoReShade.Native;

namespace AutoReShade.Overlay;

/// <summary>
/// The clock overlay: a separate, transparent, always-on-top window that mouse clicks pass through.
/// It is an ordinary desktop window drawn by Windows above the game, never inside it.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private IntPtr _hwnd;
    private bool _adjusting;
    private bool _toastActive;
    private double _aspect = 1.0;
    private Win32.RECT _placement;
    private volatile object? _screenArea;

    public OverlayWindow()
    {
        InitializeComponent();
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            _toastActive = false;
        };
        _topmostTimer.Tick += (_, _) => KeepOnTop();
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseWheel += OnMouseWheel;
        KeyDown += OnKeyDown;
    }

    /// <summary>Raised while the player drags or resizes the overlay: X, Y and size as fractions of the monitor.</summary>
    public event Action<double, double, double>? Adjusted;

    public event Action? AdjustFinished;

    public bool HasClock => ClockImage.Source is not null;
    public bool IsAdjusting => _adjusting;
    public string? ClockPath { get; private set; }

    /// <summary>Where the overlay currently is on screen (physical pixels), or null when hidden. Safe from any thread.</summary>
    public System.Drawing.Rectangle? ScreenArea => _screenArea as System.Drawing.Rectangle?;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        SetClickThrough(true);
    }

    public bool SetClock(string? path)
    {
        ClockPath = path;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ClockImage.Source = null;
            return path is null;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // do not keep the file locked
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path!, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            ClockImage.Source = image;
            _aspect = image.PixelHeight > 0 ? (double)image.PixelWidth / image.PixelHeight : 1.0;
            return true;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            Log.Warn($"Could not load clock image {path}: {ex.Message}");
            ClockImage.Source = null;
            return false;
        }
    }

    public void ShowToast(string text)
    {
        ToastText.Text = text;
        _toastActive = true;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    public void BeginAdjust(string hint)
    {
        _adjusting = true;
        AdjustHint.Text = hint;
        AdjustFrame.Visibility = Visibility.Visible;
        SetClickThrough(false);
    }

    public void EndAdjust()
    {
        if (!_adjusting) return;
        _adjusting = false;
        AdjustFrame.Visibility = Visibility.Collapsed;
        SetClickThrough(true);
        AdjustFinished?.Invoke();
    }

    /// <summary>Decides what is visible and where the window sits. Called several times a second.</summary>
    public void Refresh(OverlaySettings settings, Win32.RECT monitor, bool gameFocused, bool userWantsClock)
    {
        var allowed = !settings.OnlyWhenGameFocused || gameFocused;
        var clockShown = HasClock && (_adjusting || (settings.Enabled && userWantsClock && allowed));
        var toastShown = _toastActive && settings.ShowMapNameNotification && (allowed || _adjusting);
        var visible = _adjusting || clockShown || toastShown;

        ClockImage.Visibility = clockShown ? Visibility.Visible : Visibility.Collapsed;
        ClockImage.Opacity = settings.Opacity;
        Placeholder.Visibility = _adjusting && !HasClock ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Opacity = Math.Max(0.5, settings.Opacity);
        Toast.Visibility = toastShown ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            if (IsVisible) Hide();
            _topmostTimer.Stop();
            _screenArea = null;
            return;
        }

        Place(settings, monitor, clockShown || _adjusting, toastShown);
        if (!IsVisible) Show();
        if (!_topmostTimer.IsEnabled)
        {
            _topmostTimer.Start();
            KeepOnTop();
        }
        _screenArea = clockShown || _adjusting
            ? new System.Drawing.Rectangle(_placement.Left, _placement.Top, _placement.Width, _placement.Height)
            : null;
    }

    private void Place(OverlaySettings settings, Win32.RECT monitor, bool clockArea, bool toastShown)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0) return;
        int width, clockHeight;
        if (clockArea)
        {
            clockHeight = (int)Math.Round(Math.Clamp(settings.Size, 0.05, 1.0) * monitor.Height);
            width = (int)Math.Round(clockHeight * (HasClock ? _aspect : 1.0));
        }
        else
        {
            // Only the map-name notification is shown.
            clockHeight = 0;
            width = Math.Max(320, (int)(monitor.Width * 0.22));
        }
        width = Math.Min(width, monitor.Width);
        var height = Math.Min(clockHeight + (toastShown ? ToastHeight(width) : 0), monitor.Height);

        var left = monitor.Left + (int)Math.Round(Math.Clamp(settings.X, 0, 1) * monitor.Width);
        var top = monitor.Top + (int)Math.Round(Math.Clamp(settings.Y, 0, 1) * monitor.Height);
        left = Math.Clamp(left, monitor.Left, monitor.Right - width);
        top = Math.Clamp(top, monitor.Top, monitor.Bottom - height);

        var target = new Win32.RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
        if (_hwnd == IntPtr.Zero)
        {
            // First show: let WPF create the window, then move it.
            _placement = target;
            Dispatcher.BeginInvoke(() => ApplyPlacement(target), DispatcherPriority.Render);
            return;
        }
        if (target.Equals(_placement) && Win32.GetWindowRect(_hwnd, out var actual) && actual.Equals(target)) return;
        ApplyPlacement(target);
    }

    /// <summary>Physical pixels the map-name notification needs below the clock at the given window width.</summary>
    private int ToastHeight(int widthPixels)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        Toast.Measure(new Size(widthPixels / dpi.DpiScaleX, double.PositiveInfinity));
        return (int)Math.Ceiling(Toast.DesiredSize.Height * dpi.DpiScaleY);
    }

    private void ApplyPlacement(Win32.RECT target)
    {
        if (_hwnd == IntPtr.Zero) return;
        _placement = target;
        Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, target.Left, target.Top, target.Width, target.Height, Win32.SWP_NOACTIVATE);
    }

    private void KeepOnTop()
    {
        if (_hwnd != IntPtr.Zero && IsVisible)
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private void SetClickThrough(bool enabled)
    {
        if (_hwnd == IntPtr.Zero) return;
        var style = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
        style |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_LAYERED;
        style = enabled
            ? style | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE
            : style & ~(Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE);
        Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(style));
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_adjusting) return;
        Activate();
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }
        ReportPosition(null);
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_adjusting) return;
        var monitor = Win32.MonitorBounds(Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST));
        if (monitor.Height <= 0 || !Win32.GetWindowRect(_hwnd, out var rect)) return;
        var size = rect.Height / (double)monitor.Height + (e.Delta > 0 ? 0.02 : -0.02);
        ReportPosition(Math.Clamp(size, 0.05, 0.9));
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (_adjusting && e.Key is Key.Enter or Key.Escape) EndAdjust();
    }

    private void ReportPosition(double? newSize)
    {
        if (!Win32.GetWindowRect(_hwnd, out var rect)) return;
        var monitor = Win32.MonitorBounds(Win32.MonitorFromWindow(_hwnd, Win32.MONITOR_DEFAULTTONEAREST));
        if (monitor.Width <= 0 || monitor.Height <= 0) return;
        var x = (rect.Left - monitor.Left) / (double)monitor.Width;
        var y = (rect.Top - monitor.Top) / (double)monitor.Height;
        var size = newSize ?? rect.Height / (double)monitor.Height;
        _placement = default; // force the next refresh to re-apply
        Adjusted?.Invoke(Math.Clamp(x, 0, 0.98), Math.Clamp(y, 0, 0.98), size);
    }
}
