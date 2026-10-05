using System.Text.Json;

namespace AutoReShade.Core.Settings;

/// <summary>Loads and saves settings.json. A damaged file is kept aside and replaced with defaults.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly object _gate = new();

    public SettingsStore(string file)
    {
        FilePath = file;
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
                return Normalize(settings);
            }
            catch (Exception ex) when (ex is JsonException or IOException or NotSupportedException)
            {
                Log.Error("settings.json could not be read, starting with defaults", ex);
                try
                {
                    File.Copy(FilePath, FilePath + ".broken", overwrite: true);
                }
                catch (IOException)
                {
                }
                return new AppSettings();
            }
        }
    }

    public void Save(AppSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
    }

    /// <summary>JSON loses the case-insensitive comparers; restore them and fill missing sections.</summary>
    private static AppSettings Normalize(AppSettings s)
    {
        s.RealmPresets = new Dictionary<string, string>(s.RealmPresets ?? new(), StringComparer.OrdinalIgnoreCase);
        s.MapPresets = new Dictionary<string, string>(s.MapPresets ?? new(), StringComparer.OrdinalIgnoreCase);
        s.MapClocks = new Dictionary<string, string>(s.MapClocks ?? new(), StringComparer.OrdinalIgnoreCase);
        s.PresetKeySlots = new Dictionary<string, int>(s.PresetKeySlots ?? new(), StringComparer.OrdinalIgnoreCase);
        s.Overlay ??= new OverlaySettings();
        s.Hotkeys ??= new HotkeySettings();
        s.Detection ??= new DetectionSettings();
        s.Overlay.Size = Math.Clamp(s.Overlay.Size, 0.05, 1.0);
        s.Overlay.Opacity = Math.Clamp(s.Overlay.Opacity, 0.1, 1.0);
        s.Overlay.X = Math.Clamp(s.Overlay.X, 0, 0.98);
        s.Overlay.Y = Math.Clamp(s.Overlay.Y, 0, 0.98);
        s.Detection.IntervalMs = Math.Clamp(s.Detection.IntervalMs, 300, 10000);
        s.Detection.BrightnessThreshold = Math.Clamp(s.Detection.BrightnessThreshold, 60, 254);
        return s;
    }
}
