using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoReShade.Core.Maps;
using AutoReShade.Core.Settings;

namespace AutoReShade.Views;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

/// <summary>One entry of a preset drop-down. <see cref="Path"/> null means "inherit".</summary>
public sealed record PresetOption(string Label, string? Path)
{
    public static string LabelFor(string path) => System.IO.Path.GetFileNameWithoutExtension(path) is { Length: > 0 } name ? name : System.IO.Path.GetFileName(path);

    public override string ToString() => Label;
}

public sealed class RealmViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly Action _changed;
    private IReadOnlyList<PresetOption> _presetOptions = Array.Empty<PresetOption>();
    private bool _isVisible = true;

    public RealmViewModel(RealmInfo realm, AppSettings settings, Action changed, Action<MapRowViewModel> onClockChanged)
    {
        Realm = realm;
        _settings = settings;
        _changed = changed;
        Maps = new ObservableCollection<MapRowViewModel>(realm.Maps.Select(m => new MapRowViewModel(m, this, settings, changed, onClockChanged)));
    }

    public RealmInfo Realm { get; }
    public string Name => Realm.DisplayName;
    public ObservableCollection<MapRowViewModel> Maps { get; }

    public IReadOnlyList<PresetOption> PresetOptions
    {
        get => _presetOptions;
        private set => Set(ref _presetOptions, value);
    }

    public PresetOption? SelectedPreset => FindOption(_settings.RealmPresets.GetValueOrDefault(Realm.Id));

    /// <summary>Called only when the player picks an entry (not when the list is refreshed).</summary>
    public void Choose(PresetOption option)
    {
        if (option.Path is null) _settings.RealmPresets.Remove(Realm.Id);
        else _settings.RealmPresets[Realm.Id] = option.Path;
        Raise(nameof(SelectedPreset));
        foreach (var map in Maps) map.RefreshEffectivePreset();
        _changed();
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    /// <summary>The preset the realm's maps get when they have none of their own.</summary>
    public string? EffectivePreset => _settings.RealmPresets.GetValueOrDefault(Realm.Id) ?? _settings.DefaultPreset;

    public void SetPresetFiles(IReadOnlyList<string> presets)
    {
        PresetOptions = BuildOptions(new PresetOption("(default preset)", null), presets, _settings.RealmPresets.GetValueOrDefault(Realm.Id));
        Raise(nameof(SelectedPreset));
        foreach (var map in Maps) map.SetPresetFiles(presets);
    }

    internal static IReadOnlyList<PresetOption> BuildOptions(PresetOption inherit, IReadOnlyList<string> presets, string? current)
    {
        var options = new List<PresetOption> { inherit };
        options.AddRange(presets.Select(p => new PresetOption(PresetOption.LabelFor(p), p)));
        if (!string.IsNullOrWhiteSpace(current) && !presets.Contains(current, StringComparer.OrdinalIgnoreCase))
            options.Add(new PresetOption(PresetOption.LabelFor(current) + (File.Exists(current) ? string.Empty : " (missing)"), current));
        return options;
    }

    private PresetOption? FindOption(string? path) =>
        path is null ? PresetOptions.FirstOrDefault() : PresetOptions.FirstOrDefault(o => string.Equals(o.Path, path, StringComparison.OrdinalIgnoreCase));
}

public sealed class MapRowViewModel : ObservableObject
{
    private readonly RealmViewModel _realm;
    private readonly AppSettings _settings;
    private readonly Action _changed;
    private readonly Action<MapRowViewModel> _onClockChanged;
    private IReadOnlyList<PresetOption> _presetOptions = Array.Empty<PresetOption>();
    private ImageSource? _thumbnail;
    private bool _isVisible = true;
    private bool _isCurrent;

    public MapRowViewModel(MapInfo map, RealmViewModel realm, AppSettings settings, Action changed, Action<MapRowViewModel> onClockChanged)
    {
        Map = map;
        _realm = realm;
        _settings = settings;
        _changed = changed;
        _onClockChanged = onClockChanged;
        LoadThumbnail();
    }

    public MapInfo Map { get; }
    public string Name => Map.DisplayName;

    public IReadOnlyList<PresetOption> PresetOptions
    {
        get => _presetOptions;
        private set => Set(ref _presetOptions, value);
    }

    public PresetOption? SelectedPreset
    {
        get
        {
            var path = _settings.MapPresets.GetValueOrDefault(Map.Id);
            return path is null ? PresetOptions.FirstOrDefault() : PresetOptions.FirstOrDefault(o => string.Equals(o.Path, path, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Called only when the player picks an entry (not when the list is refreshed).</summary>
    public void Choose(PresetOption option)
    {
        if (option.Path is null) _settings.MapPresets.Remove(Map.Id);
        else _settings.MapPresets[Map.Id] = option.Path;
        Raise(nameof(SelectedPreset));
        _changed();
        _onClockChanged(this);
    }

    public string? ClockPath
    {
        get => _settings.MapClocks.GetValueOrDefault(Map.Id);
        set
        {
            if (string.IsNullOrWhiteSpace(value)) _settings.MapClocks.Remove(Map.Id);
            else _settings.MapClocks[Map.Id] = value;
            Raise();
            Raise(nameof(ClockLabel));
            Raise(nameof(HasClock));
            LoadThumbnail();
            _changed();
            _onClockChanged(this);
        }
    }

    public bool HasClock => !string.IsNullOrWhiteSpace(ClockPath);

    public string ClockLabel => ClockPath is { } p
        ? (File.Exists(p) ? Path.GetFileName(p) : Path.GetFileName(p) + " (missing)")
        : "No clock image";

    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        private set => Set(ref _thumbnail, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => Set(ref _isCurrent, value);
    }

    public void SetPresetFiles(IReadOnlyList<string> presets)
    {
        var inheritLabel = "(realm / default)";
        PresetOptions = RealmViewModel.BuildOptions(new PresetOption(inheritLabel, null), presets, _settings.MapPresets.GetValueOrDefault(Map.Id));
        Raise(nameof(SelectedPreset));
    }

    public void RefreshEffectivePreset() => Raise(nameof(SelectedPreset));

    private void LoadThumbnail()
    {
        var path = ClockPath;
        if (path is null || !File.Exists(path))
        {
            Thumbnail = null;
            return;
        }
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelHeight = 96;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            Thumbnail = image;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or InvalidOperationException)
        {
            Thumbnail = null;
        }
    }
}
