namespace AutoReShade.Core;

/// <summary>
/// Locations of everything AutoReShade stores on disk. Nothing here depends on the
/// user's machine layout: all paths are derived from the standard Windows profile folders.
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string dataDir, string runtimeDir)
    {
        DataDir = dataDir;
        RuntimeDir = runtimeDir;
    }

    /// <summary>User-visible data: settings, clock images, OCR languages, logs.</summary>
    public string DataDir { get; }

    /// <summary>Extracted native libraries (not meant to be touched by the user).</summary>
    public string RuntimeDir { get; }

    public string SettingsFile => Path.Combine(DataDir, "settings.json");
    public string ClocksDir => Path.Combine(DataDir, "Clocks");
    public string TessdataDir => Path.Combine(DataDir, "tessdata");
    public string CustomMapsFile => Path.Combine(DataDir, "custom-maps.json");
    public string LogsDir => Path.Combine(DataDir, "logs");
    public string CapturesDir => Path.Combine(DataDir, "captures");

    public static AppPaths CreateDefault() => new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoReShade"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoReShade", "runtime"));

    public void EnsureCreated()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(ClocksDir);
        Directory.CreateDirectory(TessdataDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(RuntimeDir);
    }
}
