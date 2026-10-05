using AutoReShade.Core.ReShade;

namespace AutoReShade.Core.Game;

public enum WindowModeSetting
{
    Unknown,
    Fullscreen,
    WindowedFullscreen,
    Windowed,
}

public sealed record WindowModeReading(WindowModeSetting Mode, string? SourceFile);

/// <summary>
/// Reads (never writes) the display mode from the game's own settings file, so AutoReShade can warn
/// before the game even starts. Unreal Engine stores it as FullscreenMode: 0 = fullscreen,
/// 1 = windowed fullscreen (borderless), 2 = windowed.
/// </summary>
public static class GameWindowSettings
{
    public static string ConfigRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeadByDaylight", "Saved", "Config");

    public static WindowModeReading Read() => Read(ConfigRoot);

    public static WindowModeReading Read(string configRoot)
    {
        if (!Directory.Exists(configRoot)) return new WindowModeReading(WindowModeSetting.Unknown, null);

        // One folder per platform build (WindowsClient = Steam, EGSClient = Epic, WinGDK = Microsoft Store).
        // The most recently saved one belongs to the version the player actually uses.
        var newest = Directory.EnumerateDirectories(configRoot)
            .Select(d => Path.Combine(d, "GameUserSettings.ini"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        if (newest is null) return new WindowModeReading(WindowModeSetting.Unknown, null);

        try
        {
            var value = IniDocument.Load(newest).Get("/Script/DeadByDaylight.DBDGameUserSettings", "FullscreenMode")
                        ?? FindAnywhere(newest, "FullscreenMode");
            return new WindowModeReading(Parse(value), newest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new WindowModeReading(WindowModeSetting.Unknown, newest);
        }
    }

    public static WindowModeSetting Parse(string? value) => value?.Trim() switch
    {
        "0" => WindowModeSetting.Fullscreen,
        "1" => WindowModeSetting.WindowedFullscreen,
        "2" => WindowModeSetting.Windowed,
        _ => WindowModeSetting.Unknown,
    };

    private static string? FindAnywhere(string file, string key)
    {
        foreach (var line in File.ReadLines(file))
        {
            var t = line.Trim();
            if (t.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                return t[(key.Length + 1)..];
        }
        return null;
    }
}
