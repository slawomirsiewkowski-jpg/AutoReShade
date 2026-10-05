using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AutoReShade.Core;
using AutoReShade.Core.Detection;
using AutoReShade.Core.Game;
using AutoReShade.Core.Maps;
using AutoReShade.Core.ReShade;
using AutoReShade.Core.Settings;
using AutoReShade.Services;
using Microsoft.Win32;

namespace AutoReShade.Views;

public partial class MainWindow : Window
{
    public const string ProjectUrl = "https://github.com/slawomirsiewkowski-jpg/AutoReShade";

    private readonly AppController _app;
    private List<RealmViewModel> _realms = new();
    private IReadOnlyList<string> _presetFiles = Array.Empty<string>();
    private bool _loading;

    public MainWindow(AppController app)
    {
        _app = app;
        InitializeComponent();
        BuildMapList();
        LoadControls();
        RefreshPresetFiles();
        UpdateStatus();
        _app.StateChanged += UpdateStatus;
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) UpdateStatus();
        };
        Loaded += (_, _) =>
        {
            Tabs.SelectedIndex = 0;
            MapScroller.ScrollToTop();
            MapSearchBox.Focus();
        };
    }

    private AppSettings S => _app.Settings;

    // ------------------------------------------------------------------ setup

    private void BuildMapList()
    {
        _realms = _app.Catalog.Realms
            .Select(r => new RealmViewModel(r, S, _app.SettingsChanged, OnMapAssignmentChanged))
            .ToList();
        RealmList.ItemsSource = _realms;
        foreach (var realm in _realms) realm.SetPresetFiles(_presetFiles);
        ApplyMapFilter();
    }

    private void LoadControls()
    {
        _loading = true;
        ReShadeFolderBox.Text = S.ReShadeDirectory ?? string.Empty;
        SwitchingEnabledBox.IsChecked = S.PresetSwitchingEnabled;

        OverlayEnabledBox.IsChecked = S.Overlay.Enabled;
        SizeSlider.Value = Math.Round(S.Overlay.Size * 100);
        OpacitySlider.Value = Math.Round(S.Overlay.Opacity * 100);
        OnlyFocusedBox.IsChecked = S.Overlay.OnlyWhenGameFocused;
        ToastBox.IsChecked = S.Overlay.ShowMapNameNotification;

        HotkeyToggleBox.Text = S.Hotkeys.ToggleOverlay;
        HotkeyMoveBox.Text = S.Hotkeys.MoveOverlay;
        HotkeyPickBox.Text = S.Hotkeys.PickMap;

        DetectionEnabledBox.IsChecked = S.Detection.Enabled;
        DebugCapturesBox.IsChecked = S.Detection.SaveDebugCaptures;
        RegionXSlider.Value = Math.Round(S.Detection.RegionX * 100);
        RegionYSlider.Value = Math.Round(S.Detection.RegionY * 100);
        RegionWSlider.Value = Math.Round(S.Detection.RegionWidth * 100);
        RegionHSlider.Value = Math.Round(S.Detection.RegionHeight * 100);
        ThresholdSlider.Value = S.Detection.BrightnessThreshold;
        IntervalSlider.Value = S.Detection.IntervalMs;

        StartWithWindowsBox.IsChecked = SafeStartupState();
        StartMinimizedBox.IsChecked = S.StartMinimized;
        CheckWindowModeBox.IsChecked = S.CheckWindowMode;
        DataFolderText.Text = $"Data folder: {_app.Paths.DataDir}";
        VersionText.Text = $"AutoReShade {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        _loading = false;
        UpdateSliderTexts();
    }

    private static bool SafeStartupState()
    {
        try
        {
            return StartupRegistration.IsEnabled();
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    private void RefreshPresetFiles()
    {
        _presetFiles = string.IsNullOrWhiteSpace(S.ReShadeDirectory)
            ? Array.Empty<string>()
            : ReShadeLocator.ListPresets(S.ReShadeDirectory);
        foreach (var realm in _realms) realm.SetPresetFiles(_presetFiles);

        _loading = true;
        var options = RealmViewModel.BuildOptions(new PresetOption("(none)", null), _presetFiles, S.DefaultPreset);
        DefaultPresetBox.ItemsSource = options;
        DefaultPresetBox.SelectedItem = options.FirstOrDefault(o => string.Equals(o.Path, S.DefaultPreset, StringComparison.OrdinalIgnoreCase)) ?? options[0];
        _loading = false;
        UpdateReShadeStatus();
    }

    // ------------------------------------------------------------------ status

    private void UpdateStatus()
    {
        if (!IsLoaded && !IsVisible) return;
        var game = _app.Game.Current;
        GameStatusText.Text = !game.IsRunning ? "Not running" : game.IsForeground ? "Running (active)" : "Running in background";
        WindowModeText.Text = WindowModeDescription(game);

        var map = _app.CurrentMap;
        MapStatusText.Text = map?.DisplayName ?? "None yet";
        MapDetailText.Text = map is null
            ? "Detected on the loading screen"
            : $"{_app.Catalog.FindRealm(map.RealmId)?.DisplayName} - {(_app.CurrentMapSource == MapSource.Manual ? "chosen manually" : "detected")} at {_app.CurrentMapTime:HH:mm:ss}";
        foreach (var realm in _realms)
            foreach (var row in realm.Maps)
                row.IsCurrent = map is not null && row.Map.Id == map.Id;

        var presets = _app.Presets;
        if (!S.PresetSwitchingEnabled)
        {
            PresetStatusText.Text = "Switching is off";
            PresetDetailText.Text = string.Empty;
        }
        else if (presets.PendingPreset is { } pending)
        {
            PresetStatusText.Text = PresetOption.LabelFor(pending);
            PresetDetailText.Text = "Waiting for the game to be the active window";
        }
        else if (presets.ActivePreset is { } active)
        {
            PresetStatusText.Text = PresetOption.LabelFor(active);
            PresetDetailText.Text = presets.LastProblem ?? "Switched by AutoReShade";
        }
        else
        {
            PresetStatusText.Text = "-";
            PresetDetailText.Text = presets.LastProblem ?? "Switches when a map is detected";
        }

        UpdateProblems();
        UpdateReShadeStatus();
        UpdateDetectionStatus();
        HotkeyErrorsText.Text = string.Join("\n", _app.Hotkeys.Errors.Select(e => $"{e.Key}: {e.Value}"));
    }

    private string WindowModeDescription(GameSnapshot game)
    {
        if (game.IsRunning && game.Mode == RuntimeWindowMode.ExclusiveFullscreen) return "Fullscreen - the clock cannot be shown";
        if (game.IsRunning && game.Mode == RuntimeWindowMode.Borderless) return "Windowed Fullscreen - OK";
        if (game.IsRunning && game.Mode == RuntimeWindowMode.Windowed) return "Windowed - OK";
        return _app.DisplayModeSetting.Mode switch
        {
            WindowModeSetting.WindowedFullscreen => "Display mode: Windowed Fullscreen - OK",
            WindowModeSetting.Windowed => "Display mode: Windowed - OK",
            WindowModeSetting.Fullscreen => "Display mode: Fullscreen - change it (see General tab)",
            _ => "Display mode: unknown",
        };
    }

    private void UpdateProblems()
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(S.ReShadeDirectory))
            problems.Add("ReShade folder is not set. Open the ReShade tab and click \"Find automatically\" or \"Browse...\".");
        else if (_app.LastReShadeResult is { Success: false } bad)
            problems.Add(bad.Message);
        if (_app.GameRestartNeeded)
            problems.Add("Restart Dead by Daylight so ReShade loads the new preset shortcuts.");
        if (_app.Detection.EngineError is { } engine)
            problems.Add(engine);
        if (_app.CustomMapsError is { } custom)
            problems.Add(custom);
        if (_app.DisplayModeSetting.Mode == WindowModeSetting.Fullscreen || _app.Game.Current.Mode == RuntimeWindowMode.ExclusiveFullscreen)
            problems.Add("The game is set to Fullscreen, so the clock overlay cannot appear. Switch the game to Windowed Fullscreen (General tab explains how).");
        if (_app.Presets.LastProblem is { } presetProblem)
            problems.Add(presetProblem);

        ProblemsText.Text = string.Join("\n", problems.Select(p => "• " + p));
        ProblemsPanel.Visibility = problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateReShadeStatus()
    {
        var dir = S.ReShadeDirectory;
        if (string.IsNullOrWhiteSpace(dir))
        {
            ReShadeFolderStatus.Text = "Not set.";
        }
        else
        {
            var info = ReShadeLocator.Inspect(dir);
            ReShadeFolderStatus.Text = info is null
                ? "This folder does not exist."
                : $"{(info.ReShadeVersion is { } v ? $"ReShade {v} found" : "No ReShade DLL found here")} - {(info.HasIni ? "ReShade.ini found" : "ReShade.ini missing (start the game once with ReShade)")} - {_presetFiles.Count} preset(s)";
        }

        ReShadeSyncStatus.Text = _app.LastReShadeResult?.Message ?? (S.PresetSwitchingEnabled ? "Nothing saved yet." : "Automatic switching is off.");

        var lines = S.PresetKeySlots
            .Where(kv => kv.Value >= 0 && kv.Value < ShortcutKeyPool.Slots.Count)
            .OrderBy(kv => kv.Value)
            .Select(kv => $"{ShortcutKeyPool.Slots[kv.Value],-12} {Path.GetFileName(kv.Key)}");
        ShortcutListText.Text = string.Join("\n", lines);
    }

    private void UpdateDetectionStatus()
    {
        var detection = _app.Detection;
        EngineStatusText.Text = detection.EngineError is { } error
            ? error
            : detection.EngineLanguages is { } languages
                ? $"Text reader ready (languages: {languages})."
                : "Text reader starts when the game becomes the active window.";

        if (detection.LastResult is { } last && detection.LastReadTime is { } time)
        {
            var match = last.Match.Map is { } m ? $"{m.DisplayName} ({Math.Min(1, last.Match.Score):P0})" : "no map name";
            LastReadText.Text = $"Last read at {time:HH:mm:ss}: {match}. Text: {Shorten(string.Join(" | ", last.Lines), 160)}";
        }
        else
        {
            LastReadText.Text = "Nothing read yet.";
        }
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + "...";

    private void ApplyMapFilter()
    {
        var query = TextNormalizer.Compact(MapSearchBox?.Text ?? string.Empty);
        foreach (var realm in _realms)
        {
            var realmMatches = query.Length == 0 || TextNormalizer.Compact(realm.Name).Contains(query, StringComparison.Ordinal);
            var any = false;
            foreach (var row in realm.Maps)
            {
                row.IsVisible = realmMatches || TextNormalizer.Compact(string.Join(" ", row.Map.AllNames)).Contains(query, StringComparison.Ordinal);
                any |= row.IsVisible;
            }
            realm.IsVisible = any;
        }
    }

    private void OnMapAssignmentChanged(MapRowViewModel row)
    {
        if (_app.CurrentMap?.Id == row.Map.Id) _app.RefreshCurrentMap();
    }

    // ------------------------------------------------------------------ maps tab

    /// <summary>Only a real pick by the player counts; list refreshes also raise SelectionChanged.</summary>
    private static PresetOption? PickedOption(object sender, SelectionChangedEventArgs e) =>
        sender is ComboBox { IsDropDownOpen: true } or ComboBox { IsKeyboardFocusWithin: true }
            && e.AddedItems.Count == 1 && e.AddedItems[0] is PresetOption option
            ? option
            : null;

    private void OnRealmPresetPicked(object sender, SelectionChangedEventArgs e)
    {
        if (PickedOption(sender, e) is { } option && (sender as FrameworkElement)?.DataContext is RealmViewModel realm)
        {
            realm.Choose(option);
            _app.RefreshCurrentMap();
        }
    }

    private void OnMapPresetPicked(object sender, SelectionChangedEventArgs e)
    {
        if (PickedOption(sender, e) is { } option && (sender as FrameworkElement)?.DataContext is MapRowViewModel row)
            row.Choose(option);
    }

    private void OnMapSearchChanged(object sender, TextChangedEventArgs e) => ApplyMapFilter();

    private void OnChooseClock(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not MapRowViewModel row) return;
        var dialog = new OpenFileDialog
        {
            Title = $"Clock image for {row.Name}",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files|*.*",
            InitialDirectory = row.ClockPath is { } p && File.Exists(p) ? Path.GetDirectoryName(p) : _app.Paths.ClocksDir,
        };
        if (dialog.ShowDialog(this) == true) row.ClockPath = dialog.FileName;
    }

    private void OnClearClock(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MapRowViewModel row) row.ClockPath = null;
    }

    private void OnShowMap(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is MapRowViewModel row) _app.SelectMap(row.Map, MapSource.Manual);
    }

    private void OnAutoAssignClocks(object sender, RoutedEventArgs e)
    {
        var images = ClockAutoAssigner.ImagesIn(_app.Paths.ClocksDir).ToList();
        if (images.Count == 0)
        {
            MessageBox.Show(this,
                $"There are no images in the clocks folder yet.\n\nPut your clock images (named after the maps, e.g. \"Coal Tower.png\") into:\n{_app.Paths.ClocksDir}\n\nThe folder will open now.",
                "AutoReShade", MessageBoxButton.OK, MessageBoxImage.Information);
            OpenFolder(_app.Paths.ClocksDir);
            return;
        }

        var assigned = new Dictionary<string, string>(ClockAutoAssigner.Assign(_app.Catalog, images), StringComparer.OrdinalIgnoreCase);
        // Short names ("Midwich.png", "Saloon.png") that are not full map names.
        foreach (var image in images.Where(i => !assigned.Values.Contains(i, StringComparer.OrdinalIgnoreCase)))
            if (FileNameMatcher.Match(_app.Catalog, image)?.MapId is { } mapId && !assigned.ContainsKey(mapId))
                assigned[mapId] = image;
        foreach (var realm in _realms)
            foreach (var row in realm.Maps)
                if (assigned.TryGetValue(row.Map.Id, out var file))
                    row.ClockPath = file;

        var unmatched = images.Where(i => !assigned.Values.Contains(i, StringComparer.OrdinalIgnoreCase)).Select(Path.GetFileName).ToList();
        var message = $"Assigned {assigned.Count} clock image(s).";
        if (unmatched.Count > 0)
            message += $"\n\nThese files did not match any map name (rename them after the map, or choose them by hand):\n{string.Join("\n", unmatched.Take(15))}{(unmatched.Count > 15 ? "\n..." : string.Empty)}";
        MessageBox.Show(this, message, "AutoReShade", MessageBoxButton.OK, MessageBoxImage.Information);
        _app.RefreshCurrentMap();
    }

    private void OnAutoAssignPresets(object sender, RoutedEventArgs e)
    {
        if (_presetFiles.Count == 0)
        {
            MessageBox.Show(this, "No ReShade presets were found. Set the ReShade folder on the ReShade tab first.", "AutoReShade", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var (realms, maps) = FileNameMatcher.AssignPresets(_app.Catalog, _presetFiles);
        var count = 0;
        foreach (var (realmId, file) in realms)
            if (!S.RealmPresets.ContainsKey(realmId)) { S.RealmPresets[realmId] = file; count++; }
        foreach (var (mapId, file) in maps)
            if (!S.MapPresets.ContainsKey(mapId)) { S.MapPresets[mapId] = file; count++; }
        foreach (var realm in _realms) realm.SetPresetFiles(_presetFiles);
        _app.FlushSettings();
        _app.RefreshCurrentMap();

        var used = realms.Values.Concat(maps.Values).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unused = _presetFiles.Where(p => !used.Contains(p)).Select(Path.GetFileName).ToList();
        var message = $"Assigned {count} preset(s) to realms and maps based on their file names. Existing choices were kept.";
        if (unused.Count > 0)
            message += "\n\nThese presets did not match a realm or map name (you can still pick them by hand, e.g. as the default preset on the ReShade tab):\n"
                       + string.Join("\n", unused.Take(15));
        MessageBox.Show(this, message, "AutoReShade", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OnOpenClocksFolder(object sender, RoutedEventArgs e) => OpenFolder(_app.Paths.ClocksDir);

    private void OnReloadMaps(object sender, RoutedEventArgs e)
    {
        _app.ReloadMapList();
        BuildMapList();
        MessageBox.Show(this,
            _app.CustomMapsError ?? $"Map list loaded: {_app.Catalog.Realms.Count} realms, {_app.Catalog.Maps.Count} maps.\n\nYou can add maps or languages in:\n{_app.Paths.CustomMapsFile}",
            "AutoReShade", MessageBoxButton.OK, _app.CustomMapsError is null ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    // ------------------------------------------------------------------ ReShade tab

    private void OnBrowseReShade(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose the folder that contains ReShade.ini",
            InitialDirectory = Directory.Exists(S.ReShadeDirectory) ? S.ReShadeDirectory : string.Empty,
        };
        if (dialog.ShowDialog(this) != true) return;
        SetReShadeFolder(dialog.FolderName);
    }

    private void OnFindReShade(object sender, RoutedEventArgs e)
    {
        var found = ReShadeLocator.FindInstallations();
        if (found.Count == 0)
        {
            MessageBox.Show(this,
                "AutoReShade could not find ReShade in your Dead by Daylight folder.\n\nInstall ReShade for Dead by Daylight first, start the game once, then try again - or choose the folder with \"Browse...\".",
                "AutoReShade", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        SetReShadeFolder(found[0].Directory);
    }

    private void SetReShadeFolder(string folder)
    {
        S.ReShadeDirectory = folder;
        ReShadeFolderBox.Text = folder;
        RefreshPresetFiles();
        _app.FlushSettings();
    }

    private void OnSwitchingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.PresetSwitchingEnabled = SwitchingEnabledBox.IsChecked == true;
        _app.FlushSettings();
    }

    private void OnDefaultPresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || DefaultPresetBox.SelectedItem is not PresetOption option) return;
        S.DefaultPreset = option.Path;
        _app.SettingsChanged();
        _app.RefreshCurrentMap();
    }

    private void OnRefreshPresets(object sender, RoutedEventArgs e) => RefreshPresetFiles();

    private void OnSaveReShadeNow(object sender, RoutedEventArgs e)
    {
        _app.FlushSettings();
        UpdateReShadeStatus();
    }

    // ------------------------------------------------------------------ overlay tab

    private void OnOverlaySettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.Overlay.Enabled = OverlayEnabledBox.IsChecked == true;
        S.Overlay.OnlyWhenGameFocused = OnlyFocusedBox.IsChecked == true;
        S.Overlay.ShowMapNameNotification = ToastBox.IsChecked == true;
        _app.SettingsChanged();
        _app.RefreshOverlay();
    }

    private void OnOverlaySliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSliderTexts();
        if (_loading || !IsLoaded) return;
        S.Overlay.Size = SizeSlider.Value / 100.0;
        S.Overlay.Opacity = OpacitySlider.Value / 100.0;
        _app.SettingsChanged();
        _app.RefreshOverlay();
    }

    private void OnAdjustOverlay(object sender, RoutedEventArgs e) => _app.ToggleAdjustOverlay();

    private void OnResetOverlay(object sender, RoutedEventArgs e)
    {
        _app.ResetOverlayPosition();
        LoadControls();
    }

    // ------------------------------------------------------------------ hotkeys tab

    private void OnHotkeyFocus(object sender, KeyboardFocusChangedEventArgs e) => _app.Hotkeys.Suspend();

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e)
    {
        _app.Hotkeys.Resume();
        _app.RegisterHotkeys();
        UpdateStatus();
    }

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.Tab) return;

        string text;
        if (key is Key.Back or Key.Delete) text = string.Empty;
        else if (HotkeyGesture.IsModifierKey(key)) return;
        else text = new HotkeyGesture(Keyboard.Modifiers, key).ToString();

        box.Text = text;
        switch (box.Tag as string)
        {
            case "ToggleOverlay": S.Hotkeys.ToggleOverlay = text; break;
            case "MoveOverlay": S.Hotkeys.MoveOverlay = text; break;
            case "PickMap": S.Hotkeys.PickMap = text; break;
        }
        _app.SettingsChanged();
    }

    // ------------------------------------------------------------------ detection tab

    private void OnDetectionSettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.Detection.Enabled = DetectionEnabledBox.IsChecked == true;
        S.Detection.SaveDebugCaptures = DebugCapturesBox.IsChecked == true;
        _app.SettingsChanged();
    }

    private void OnDetectionSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSliderTexts();
        if (_loading || !IsLoaded) return;
        S.Detection.RegionX = RegionXSlider.Value / 100.0;
        S.Detection.RegionY = RegionYSlider.Value / 100.0;
        S.Detection.RegionWidth = RegionWSlider.Value / 100.0;
        S.Detection.RegionHeight = RegionHSlider.Value / 100.0;
        S.Detection.BrightnessThreshold = (int)ThresholdSlider.Value;
        S.Detection.IntervalMs = (int)IntervalSlider.Value;
        _app.SettingsChanged();
    }

    private void OnResetDetection(object sender, RoutedEventArgs e)
    {
        var defaults = new DetectionSettings { Enabled = S.Detection.Enabled, SaveDebugCaptures = S.Detection.SaveDebugCaptures };
        S.Detection = defaults;
        LoadControls();
        _app.SettingsChanged();
    }

    private void UpdateSliderTexts()
    {
        if (SizeValueText is null || IntervalText is null) return;
        SizeValueText.Text = $"{SizeSlider.Value:0}%";
        OpacityValueText.Text = $"{OpacitySlider.Value:0}%";
        RegionXText.Text = $"{RegionXSlider.Value:0}%";
        RegionYText.Text = $"{RegionYSlider.Value:0}%";
        RegionWText.Text = $"{RegionWSlider.Value:0}%";
        RegionHText.Text = $"{RegionHSlider.Value:0}%";
        ThresholdText.Text = $"{ThresholdSlider.Value:0}";
        IntervalText.Text = $"{IntervalSlider.Value / 1000.0:0.##} s";
    }

    private async void OnTestFile(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a screenshot of a Dead by Daylight loading screen",
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true) return;

        System.Drawing.Bitmap bitmap;
        try
        {
            using var loaded = new System.Drawing.Bitmap(dialog.FileName);
            bitmap = new System.Drawing.Bitmap(loaded);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or OutOfMemoryException)
        {
            MessageBox.Show(this, $"That file could not be opened as an image: {ex.Message}", "AutoReShade", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        await RunTestAsync(bitmap, Path.GetFileName(dialog.FileName));
    }

    private async void OnTestScreen(object sender, RoutedEventArgs e)
    {
        var game = _app.Game.Current;
        System.Drawing.Rectangle area;
        if (game.IsRunning && game.HasUsableClientArea)
        {
            area = ScreenCapture.ToRectangle(game.ClientArea);
        }
        else
        {
            var primary = Native.Win32.PrimaryMonitorBounds();
            area = new System.Drawing.Rectangle(primary.Left, primary.Top, primary.Width, primary.Height);
        }
        await RunTestAsync(ScreenCapture.Capture(area), game.IsRunning ? "the game window" : "the main screen");
    }

    private async Task RunTestAsync(System.Drawing.Bitmap frame, string source)
    {
        TestResultPanel.Visibility = Visibility.Visible;
        TestResultText.Text = "Reading...";
        TestLinesText.Text = string.Empty;
        TestPreviewImage.Source = null;

        var region = S.Detection.Region;
        var threshold = S.Detection.BrightnessThreshold;
        System.Drawing.Bitmap? preview = null;
        var result = await Task.Run(() =>
        {
            using (frame) return _app.Detection.Test(frame, region, threshold, out preview);
        });

        if (result is null)
        {
            TestResultText.Text = _app.Detection.EngineError ?? "The text reader is not available.";
            return;
        }

        TestResultText.Text = result.Match.Map is { } map
            ? $"Found: {map.DisplayName} ({_app.Catalog.FindRealm(map.RealmId)?.DisplayName}) - confidence {Math.Min(1, result.Match.Score):P0} - read from {source} in {result.Elapsed.TotalMilliseconds:0} ms"
            : result.SkippedOcr
                ? $"No text found in the detection area of {source}."
                : $"No map name recognised in {source}.";
        TestLinesText.Text = result.Lines.Count > 0 ? "Text read: " + string.Join(" | ", result.Lines) : string.Empty;
        if (preview is not null)
        {
            using (preview) TestPreviewImage.Source = ToImageSource(preview);
        }
    }

    private static BitmapSource ToImageSource(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void OnOpenCaptures(object sender, RoutedEventArgs e) => OpenFolder(_app.Paths.CapturesDir);

    private void OnOpenTessdata(object sender, RoutedEventArgs e) => OpenFolder(_app.Paths.TessdataDir);

    // ------------------------------------------------------------------ general tab

    private void OnGeneralChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        S.StartMinimized = StartMinimizedBox.IsChecked == true;
        S.CheckWindowMode = CheckWindowModeBox.IsChecked == true;
        try
        {
            StartupRegistration.SetEnabled(StartWithWindowsBox.IsChecked == true);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            MessageBox.Show(this, $"Could not change the Windows startup setting: {ex.Message}", "AutoReShade", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        _app.SettingsChanged();
    }

    private void OnShowWindowModeHelp(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, AppController.FullscreenHelp, "AutoReShade - display mode", MessageBoxButton.OK, MessageBoxImage.Information);

    private void OnOpenDataFolder(object sender, RoutedEventArgs e) => OpenFolder(_app.Paths.DataDir);

    private void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (Log.FilePath is { } file && File.Exists(file)) Open(file);
        else OpenFolder(_app.Paths.LogsDir);
    }

    private void OnOpenProjectPage(object sender, RoutedEventArgs e) => Open(ProjectUrl);

    private static void OpenFolder(string folder)
    {
        Directory.CreateDirectory(folder);
        Open(folder);
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Could not open {target}: {ex.Message}");
        }
    }
}
