using System.Diagnostics;
using System.Windows.Threading;
using AutoReShade.Native;

namespace AutoReShade.Services;

public enum RuntimeWindowMode
{
    Unknown,
    Windowed,
    Borderless,
    ExclusiveFullscreen,
}

/// <summary>What AutoReShade knows about the game right now. Only window information, never the game's memory.</summary>
public sealed record GameSnapshot(
    bool IsRunning,
    bool IsForeground,
    IntPtr Window,
    Win32.RECT ClientArea,
    RuntimeWindowMode Mode)
{
    public static readonly GameSnapshot NotRunning = new(false, false, IntPtr.Zero, default, RuntimeWindowMode.Unknown);

    public bool HasUsableClientArea => ClientArea.Width >= 320 && ClientArea.Height >= 200;
}

/// <summary>Watches whether Dead by Daylight is running and focused, and where its window is.</summary>
public sealed class GameMonitor : IDisposable
{
    private static readonly TimeSpan ProcessScanInterval = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _timer;
    private int _processId;
    private DateTime _lastScan = DateTime.MinValue;
    private IntPtr _mainWindow;
    private volatile GameSnapshot _current = GameSnapshot.NotRunning;

    public GameMonitor()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => Update();
    }

    /// <summary>Latest state; safe to read from any thread.</summary>
    public GameSnapshot Current => _current;

    /// <summary>Raised on the UI thread when running/focus/window mode changes.</summary>
    public event Action<GameSnapshot, GameSnapshot>? Changed;

    /// <summary>Raised on the UI thread four times a second.</summary>
    public event Action? Tick;

    public void Start()
    {
        Update();
        _timer.Start();
    }

    public static bool IsGameProcessName(string name) =>
        name.StartsWith("DeadByDaylight", StringComparison.OrdinalIgnoreCase)
        && name.EndsWith("Shipping", StringComparison.OrdinalIgnoreCase);

    private void Update()
    {
        var previous = _current;
        var next = Read();
        _current = next;
        if (previous.IsRunning != next.IsRunning || previous.IsForeground != next.IsForeground || previous.Mode != next.Mode)
            Changed?.Invoke(previous, next);
        Tick?.Invoke();
    }

    private GameSnapshot Read()
    {
        var foreground = Win32.GetForegroundWindow();
        Win32.GetWindowThreadProcessId(foreground, out var foregroundPid);

        if (_processId == 0 || DateTime.UtcNow - _lastScan > ProcessScanInterval)
            ScanForProcess();
        if (_processId == 0) return GameSnapshot.NotRunning;

        var isForeground = foregroundPid == _processId;
        if (isForeground) _mainWindow = foreground;
        else if (_mainWindow == IntPtr.Zero || !Win32.IsWindow(_mainWindow)) _mainWindow = FindMainWindow(_processId);

        if (_mainWindow == IntPtr.Zero || Win32.IsIconic(_mainWindow))
            return new GameSnapshot(true, false, _mainWindow, default, RuntimeWindowMode.Unknown);

        var client = ClientAreaOnScreen(_mainWindow);
        var mode = isForeground ? DetectMode(_mainWindow) : _current.Mode;
        return new GameSnapshot(true, isForeground, _mainWindow, client, mode);
    }

    private void ScanForProcess()
    {
        _lastScan = DateTime.UtcNow;
        if (_processId != 0)
        {
            try
            {
                using var existing = Process.GetProcessById(_processId);
                if (!existing.HasExited && IsGameProcessName(existing.ProcessName)) return;
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            _processId = 0;
            _mainWindow = IntPtr.Zero;
        }

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (_processId == 0 && IsGameProcessName(process.ProcessName))
                    _processId = process.Id;
            }
        }
    }

    private static IntPtr FindMainWindow(int processId)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = 0;
        Win32.EnumWindows((hwnd, _) =>
        {
            Win32.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid != processId || !Win32.IsWindowVisible(hwnd)) return true;
            if (Win32.GetWindowRect(hwnd, out var rect))
            {
                var area = (long)rect.Width * rect.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = hwnd;
                }
            }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    private static Win32.RECT ClientAreaOnScreen(IntPtr hwnd)
    {
        if (!Win32.GetClientRect(hwnd, out var client)) return default;
        var origin = new Win32.POINT();
        Win32.ClientToScreen(hwnd, ref origin);
        return new Win32.RECT { Left = origin.X, Top = origin.Y, Right = origin.X + client.Width, Bottom = origin.Y + client.Height };
    }

    /// <summary>
    /// Borderless = no title bar and covering the whole monitor. Exclusive fullscreen is reported by
    /// Windows itself (the same signal it uses to hold back notifications during games).
    /// </summary>
    private static RuntimeWindowMode DetectMode(IntPtr hwnd)
    {
        if (!Win32.GetWindowRect(hwnd, out var rect)) return RuntimeWindowMode.Unknown;
        var monitor = Win32.MonitorBounds(Win32.MonitorFromWindow(hwnd, Win32.MONITOR_DEFAULTTONEAREST));
        var style = (long)Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE);
        var coversMonitor = Math.Abs(rect.Left - monitor.Left) <= 2 && Math.Abs(rect.Top - monitor.Top) <= 2
                            && Math.Abs(rect.Right - monitor.Right) <= 2 && Math.Abs(rect.Bottom - monitor.Bottom) <= 2;
        if (!coversMonitor || (style & Win32.WS_CAPTION) == Win32.WS_CAPTION)
            return RuntimeWindowMode.Windowed;

        if (Win32.SHQueryUserNotificationState(out var state) == 0 && state == Win32.QUNS_RUNNING_D3D_FULL_SCREEN)
            return RuntimeWindowMode.ExclusiveFullscreen;
        return RuntimeWindowMode.Borderless;
    }

    public void Dispose() => _timer.Stop();
}
