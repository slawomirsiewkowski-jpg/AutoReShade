using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using AutoReShade.Core;
using AutoReShade.Core.Game;
using AutoReShade.Core.Maps;
using AutoReShade.Core.ReShade;
using AutoReShade.Core.Settings;
using AutoReShade.Native;
using AutoReShade.Overlay;
using AutoReShade.Views;

namespace AutoReShade.Services;

public enum MapSource
{
    Detected,
    Manual,
}

/// <summary>Connects everything: game watching, map detection, ReShade presets, the overlay, hotkeys and settings.</summary>
public sealed class AppController : IDisposable
{
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private bool _warnedFullscreenThisSession;
    private MainWindow? _mainWindow;
    private MapPickerWindow? _picker;
    private bool _exiting;

    public AppController(AppPaths paths)
    {
        Paths = paths;
        Store = new SettingsStore(paths.SettingsFile);
        Settings = Store.Load();
        Catalog = MapCatalog.LoadWithCustom(paths.CustomMapsFile, out var customError);
        CustomMapsError = customError;
        if (customError is not null) Log.Warn(customError);

        Game = new GameMonitor();
        Presets = new PresetSwitcher(() => Settings, Game);
        Overlay = new OverlayWindow();
        new WindowInteropHelper(Overlay).EnsureHandle();
        Detection = new DetectionService(paths, () => Settings, Game, Catalog, () => Overlay.ScreenArea);
        Hotkeys = new HotkeyManager();
        Tray = new TrayIcon(ShowSettings, PickMap, ToggleOverlay, Exit);

        _saveTimer.Tick += (_, _) => FlushSettings();
        Game.Changed += OnGameChanged;
        Game.Tick += OnTick;
        Detection.MapConfirmed += map => Dispatch(() => SelectMap(map, MapSource.Detected));
        Detection.MatchEnded += () => Dispatch(EndMatch);
        Detection.FrameProcessed += _ => Dispatch(RaiseStateChanged);
        Presets.StateChanged += RaiseStateChanged;
        Overlay.Adjusted += OnOverlayAdjusted;
        Overlay.AdjustFinished += () =>
        {
            SettingsChanged();
            RaiseStateChanged();
        };
    }

    public AppPaths Paths { get; }
    public SettingsStore Store { get; }
    public AppSettings Settings { get; }
    public MapCatalog Catalog { get; private set; }
    public string? CustomMapsError { get; private set; }
    public GameMonitor Game { get; }
    public DetectionService Detection { get; }
    public PresetSwitcher Presets { get; }
    public OverlayWindow Overlay { get; }
    public HotkeyManager Hotkeys { get; }
    public TrayIcon Tray { get; }

    public MapInfo? CurrentMap { get; private set; }
    public MapSource CurrentMapSource { get; private set; }
    public DateTime? CurrentMapTime { get; private set; }

    /// <summary>When the results screen or the lobby ended the last match; null while a map is active or before that.</summary>
    public DateTime? MatchEndedAt { get; private set; }

    public bool UserWantsClock { get; private set; } = true;
    public ApplyResult? LastReShadeResult { get; private set; }
    public bool GameRestartNeeded { get; private set; }
    public WindowModeReading DisplayModeSetting { get; private set; } = new(WindowModeSetting.Unknown, null);

    /// <summary>Raised on the UI thread whenever something shown in the main window may have changed.</summary>
    public event Action? StateChanged;

    public void Start(bool showWindow)
    {
        Paths.EnsureCreated();
        WriteClocksReadme();
        AutoDetectReShadeFolder();
        RegisterHotkeys();
        Game.Start();
        Detection.Start();
        SyncReShade();
        DisplayModeSetting = GameWindowSettings.Read();

        if (showWindow) ShowSettings();
        else Tray.ShowBalloon("AutoReShade is running", "It will switch presets and show map clocks automatically. Double-click the tray icon to open settings.");

        if (Settings.CheckWindowMode && DisplayModeSetting.Mode == WindowModeSetting.Fullscreen)
            ShowFullscreenWarning();
        RaiseStateChanged();
    }

    // ---------------------------------------------------------------- maps

    public void SelectMap(MapInfo map, MapSource source)
    {
        CurrentMap = map;
        CurrentMapSource = source;
        CurrentMapTime = DateTime.Now;
        MatchEndedAt = null;
        if (source == MapSource.Manual) Detection.SetCurrentMap(map);

        var clock = Settings.ResolveClock(map);
        if (!Overlay.SetClock(clock) && clock is not null)
            Tray.ShowBalloon("Clock image missing", $"The clock image for {map.DisplayName} could not be opened: {clock}");
        if (Settings.Overlay.ShowMapNameNotification)
            Overlay.ShowToast(source == MapSource.Manual ? $"{map.DisplayName} (chosen manually)" : map.DisplayName);

        if (Settings.PresetSwitchingEnabled)
            Presets.Request(Settings.ResolvePreset(map));

        Tray.SetTooltip($"AutoReShade - {map.DisplayName}");
        RaiseStateChanged();
    }

    /// <summary>The match is over: hide the clock until the next map loads and optionally go back to the default preset.</summary>
    private void EndMatch()
    {
        CurrentMap = null;
        CurrentMapTime = null;
        MatchEndedAt = DateTime.Now;
        Overlay.SetClock(null);
        if (Settings.PresetSwitchingEnabled)
            Presets.Request(Settings.ResolveAfterMatchPreset());
        Tray.SetTooltip("AutoReShade - match over");
        RaiseStateChanged();
    }

    /// <summary>Re-applies clock and preset of the current map (after the player changed its assignments).</summary>
    public void RefreshCurrentMap()
    {
        if (CurrentMap is null) return;
        Overlay.SetClock(Settings.ResolveClock(CurrentMap));
        if (Settings.PresetSwitchingEnabled) Presets.Request(Settings.ResolvePreset(CurrentMap));
        RaiseStateChanged();
    }

    public void ReloadMapList()
    {
        Catalog = MapCatalog.LoadWithCustom(Paths.CustomMapsFile, out var error);
        CustomMapsError = error;
        Detection.ReloadCatalog(Catalog);
        if (CurrentMap is not null) CurrentMap = Catalog.FindMap(CurrentMap.Id);
        RaiseStateChanged();
    }

    public void PickMap()
    {
        if (_picker is { IsVisible: true })
        {
            _picker.Activate();
            return;
        }
        _picker = new MapPickerWindow(Catalog, CurrentMap);
        _picker.MapChosen += map =>
        {
            SelectMap(map, MapSource.Manual);
            var game = Game.Current;
            if (game.IsRunning && game.Window != IntPtr.Zero) Win32.SetForegroundWindow(game.Window);
        };
        _picker.Show();
        _picker.Activate();
    }

    // ---------------------------------------------------------------- overlay

    public void ToggleOverlay()
    {
        UserWantsClock = !UserWantsClock;
        Tray.SetOverlayChecked(UserWantsClock);
        if (UserWantsClock && !Overlay.HasClock && CurrentMap is null)
            Overlay.ShowToast("No map detected yet");
        RefreshOverlay();
        RaiseStateChanged();
    }

    public void ToggleAdjustOverlay()
    {
        if (Overlay.IsAdjusting)
        {
            Overlay.EndAdjust();
            return;
        }
        var hint = $"Drag to move - scroll to resize - press Enter{HotkeySuffix(Settings.Hotkeys.MoveOverlay)} when done";
        Overlay.BeginAdjust(hint);
        RefreshOverlay();
        Overlay.Activate();
    }

    public void ResetOverlayPosition()
    {
        var defaults = new OverlaySettings();
        Settings.Overlay.X = defaults.X;
        Settings.Overlay.Y = defaults.Y;
        Settings.Overlay.Size = defaults.Size;
        SettingsChanged();
        RefreshOverlay();
    }

    private static string HotkeySuffix(string? hotkey) => string.IsNullOrWhiteSpace(hotkey) ? string.Empty : $" or {hotkey}";

    private void OnOverlayAdjusted(double x, double y, double size)
    {
        Settings.Overlay.X = x;
        Settings.Overlay.Y = y;
        Settings.Overlay.Size = size;
        RefreshOverlay();
        RaiseStateChanged();
    }

    public void RefreshOverlay()
    {
        var game = Game.Current;
        var monitor = game.IsRunning && game.Window != IntPtr.Zero
            ? Win32.MonitorBounds(Win32.MonitorFromWindow(game.Window, Win32.MONITOR_DEFAULTTONEAREST))
            : Win32.PrimaryMonitorBounds();
        Overlay.Refresh(Settings.Overlay, monitor, game.IsForeground, UserWantsClock);
    }

    // ---------------------------------------------------------------- game

    private void OnTick()
    {
        RefreshOverlay();
        Presets.Tick();
    }

    private void OnGameChanged(GameSnapshot previous, GameSnapshot current)
    {
        if (!previous.IsRunning && current.IsRunning)
        {
            Log.Info("Dead by Daylight started");
            // ReShade starts with its own last preset, and the first map must be detected again.
            Presets.Reset();
            Detection.SetCurrentMap(null);
            _warnedFullscreenThisSession = false;
            GameRestartNeeded = false;
            DisplayModeSetting = GameWindowSettings.Read();
        }
        else if (previous.IsRunning && !current.IsRunning)
        {
            Log.Info("Dead by Daylight closed");
            Presets.Reset();
            DisplayModeSetting = GameWindowSettings.Read();
        }

        if (current.Mode == RuntimeWindowMode.ExclusiveFullscreen && Settings.CheckWindowMode && !_warnedFullscreenThisSession
            && DisplayModeSetting.Mode != WindowModeSetting.WindowedFullscreen)
        {
            _warnedFullscreenThisSession = true;
            ShowFullscreenWarning();
        }
        RaiseStateChanged();
    }

    public const string FullscreenHelp =
        "Dead by Daylight is set to Fullscreen. The clock overlay can only appear on top of the game in Windowed Fullscreen (borderless) mode.\n\n" +
        "How to change it:\n" +
        "1. In Dead by Daylight, open Settings and go to the Graphics tab.\n" +
        "2. Change the window / display mode setting from \"Fullscreen\" to \"Windowed Fullscreen\".\n" +
        "3. Apply the change (restart the game if it asks you to).\n\n" +
        "Automatic ReShade preset switching works in every mode; only the clock overlay needs Windowed Fullscreen.";

    private void ShowFullscreenWarning()
    {
        Log.Warn("Game is in fullscreen mode; showing help");
        var owner = _mainWindow is { IsVisible: true } ? _mainWindow : null;
        if (owner is not null) MessageBox.Show(owner, FullscreenHelp, "AutoReShade - change the game's display mode", MessageBoxButton.OK, MessageBoxImage.Warning);
        else MessageBox.Show(FullscreenHelp, "AutoReShade - change the game's display mode", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // ---------------------------------------------------------------- settings & ReShade

    /// <summary>Call after any settings change. Saving and ReShade.ini updates are batched.</summary>
    public void SettingsChanged()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void FlushSettings()
    {
        _saveTimer.Stop();
        SyncReShade();
        try
        {
            Store.Save(Settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Could not save settings", ex);
        }
        RaiseStateChanged();
    }

    public void SyncReShade()
    {
        var dir = Settings.ReShadeDirectory;
        if (!Settings.PresetSwitchingEnabled || string.IsNullOrWhiteSpace(dir))
        {
            LastReShadeResult = null;
            return;
        }

        var shortcuts = Settings.BuildShortcuts();
        if (shortcuts is null)
        {
            LastReShadeResult = new ApplyResult(false, false, $"You have assigned more different presets than AutoReShade can give keys to ({ShortcutKeyPool.Slots.Count}).");
            return;
        }
        if (shortcuts.Count == 0)
        {
            LastReShadeResult = new ApplyResult(true, false, "No presets are assigned yet.");
            return;
        }

        var result = ReShadeConfigurator.Apply(dir, shortcuts);
        LastReShadeResult = result;
        if (result.Changed)
        {
            Log.Info(result.Message);
            if (Game.Current.IsRunning)
            {
                GameRestartNeeded = true;
                Tray.ShowBalloon("Restart Dead by Daylight", "Preset shortcuts were saved to ReShade.ini. ReShade reads them when the game starts.");
            }
        }
        else if (!result.Success)
        {
            Log.Warn(result.Message);
        }
    }

    public void AutoDetectReShadeFolder(bool force = false)
    {
        if (!force && !string.IsNullOrWhiteSpace(Settings.ReShadeDirectory)) return;
        var found = ReShadeLocator.FindInstallations().FirstOrDefault(i => i.ReShadeVersion is not null && i.HasIni)
                    ?? ReShadeLocator.FindInstallations().FirstOrDefault();
        if (found is null) return;
        Settings.ReShadeDirectory = found.Directory;
        Log.Info($"Found ReShade folder {found.Directory} (ReShade {found.ReShadeVersion ?? "?"})");
        SettingsChanged();
    }

    public void RegisterHotkeys()
    {
        Hotkeys.Set(new (string, string?, Action)[]
        {
            ("Show / hide clock overlay", Settings.Hotkeys.ToggleOverlay, ToggleOverlay),
            ("Move / resize overlay", Settings.Hotkeys.MoveOverlay, ToggleAdjustOverlay),
            ("Choose map manually", Settings.Hotkeys.PickMap, PickMap),
        });
        foreach (var (name, error) in Hotkeys.Errors) Log.Warn($"Hotkey \"{name}\": {error}");
    }

    private void WriteClocksReadme()
    {
        var readme = Path.Combine(Paths.ClocksDir, "README.txt");
        if (File.Exists(readme)) return;
        try
        {
            File.WriteAllText(readme,
                "Put your map clock images (PNG or JPG) in this folder.\r\n\r\n" +
                "Name each file after its map, for example:\r\n" +
                "  Coal Tower.png\r\n  Badham Preschool III.png\r\n  Raccoon City Police Station East Wing.png\r\n\r\n" +
                "Then click \"Assign clocks from folder\" on the Maps tab of AutoReShade.\r\n" +
                "You can also choose an image for each map by hand on the same tab.\r\n");
        }
        catch (IOException)
        {
        }
    }

    // ---------------------------------------------------------------- windows

    public void ShowSettings()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow(this);
            _mainWindow.Closing += (_, e) =>
            {
                if (_exiting) return;
                e.Cancel = true;
                _mainWindow.Hide();
                FlushSettings();
                Tray.ShowBalloon("AutoReShade is still running", "It keeps working in the background. Right-click the tray icon to exit.");
            };
        }
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized) _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void Exit()
    {
        _exiting = true;
        FlushSettings();
        _mainWindow?.Close();
        Application.Current.Shutdown();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();

    private static void Dispatch(Action action) =>
        Application.Current?.Dispatcher.BeginInvoke(action, DispatcherPriority.Normal);

    public void Dispose()
    {
        Detection.Dispose();
        Hotkeys.Dispose();
        Game.Dispose();
        Tray.Dispose();
        Overlay.Close();
    }
}
