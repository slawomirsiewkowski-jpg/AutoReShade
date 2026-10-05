using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AutoReShade.Core.ReShade;

public sealed record ReShadeInstallation(string Directory, string? ReShadeVersion, bool HasIni);

/// <summary>Finds Dead by Daylight installs (Steam, Epic, Microsoft Store) and the ReShade inside them.</summary>
public static partial class ReShadeLocator
{
    private const string GameFolderName = "Dead by Daylight";
    private static readonly string[] BinaryFolders = { @"DeadByDaylight\Binaries\Win64", @"DeadByDaylight\Binaries\WinGDK", @"DeadByDaylight\Binaries\EGS" };
    private static readonly string[] ReShadeDllNames = { "dxgi.dll", "d3d12.dll", "d3d11.dll", "d3d9.dll", "dinput8.dll", "opengl32.dll", "ReShade64.dll" };

    public static IReadOnlyList<ReShadeInstallation> FindInstallations()
    {
        var results = new List<ReShadeInstallation>();
        foreach (var root in FindGameRoots())
        {
            foreach (var sub in BinaryFolders)
            {
                var dir = Path.Combine(root, sub);
                var info = Inspect(dir);
                if (info is not null && (info.ReShadeVersion is not null || info.HasIni))
                    results.Add(info);
            }
        }
        return results
            .GroupBy(r => r.Directory, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderByDescending(r => r.ReShadeVersion is not null)
            .ThenByDescending(r => r.HasIni)
            .ToList();
    }

    /// <summary>Looks at one folder: does it hold a ReShade DLL and/or ReShade.ini?</summary>
    public static ReShadeInstallation? Inspect(string dir)
    {
        if (!Directory.Exists(dir)) return null;
        string? version = null;
        foreach (var name in ReShadeDllNames)
        {
            var dll = Path.Combine(dir, name);
            if (!File.Exists(dll)) continue;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(dll);
                if ((info.ProductName ?? string.Empty).Contains("ReShade", StringComparison.OrdinalIgnoreCase)
                    || (info.FileDescription ?? string.Empty).Contains("ReShade", StringComparison.OrdinalIgnoreCase))
                {
                    version = info.ProductVersion ?? info.FileVersion ?? "unknown";
                    break;
                }
            }
            catch (FileNotFoundException)
            {
            }
        }
        return new ReShadeInstallation(dir, version, File.Exists(Path.Combine(dir, ReShadeConfigurator.IniFileName)));
    }

    /// <summary>Preset files in the ReShade folder (and one level of sub-folders, except the shader folders).</summary>
    public static IReadOnlyList<string> ListPresets(string reshadeDir)
    {
        if (!Directory.Exists(reshadeDir)) return Array.Empty<string>();
        var files = new List<string>();
        AddPresets(reshadeDir, files);
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(reshadeDir))
            {
                var name = Path.GetFileName(sub);
                if (name.StartsWith("reshade-shaders", StringComparison.OrdinalIgnoreCase)) continue;
                AddPresets(sub, files);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        return files.OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool LooksLikePreset(string file)
    {
        var name = Path.GetFileName(file);
        if (name.Equals(ReShadeConfigurator.IniFileName, StringComparison.OrdinalIgnoreCase)) return false;
        if (name.StartsWith("ReShade", StringComparison.OrdinalIgnoreCase) && !name.Contains("Preset", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            var info = new FileInfo(file);
            if (info.Length == 0) return name.Contains("Preset", StringComparison.OrdinalIgnoreCase);
            if (info.Length > 2 * 1024 * 1024) return false;
            foreach (var line in File.ReadLines(file).Take(400))
            {
                var t = line.Trim();
                if (t.StartsWith("Techniques=", StringComparison.OrdinalIgnoreCase)
                    || t.StartsWith("TechniqueSorting=", StringComparison.OrdinalIgnoreCase)
                    || (t.StartsWith('[') && t.EndsWith(".fx]", StringComparison.OrdinalIgnoreCase)))
                    return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return false;
    }

    private static void AddPresets(string dir, List<string> files)
    {
        try
        {
            files.AddRange(Directory.EnumerateFiles(dir, "*.ini").Where(LooksLikePreset));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static IEnumerable<string> FindGameRoots()
    {
        var roots = new List<string>();
        roots.AddRange(SteamLibraries().Select(lib => Path.Combine(lib, "steamapps", "common", GameFolderName)));
        roots.AddRange(EpicInstallLocations());
        foreach (var drive in SafeDrives())
            roots.Add(Path.Combine(drive, "XboxGames", GameFolderName, "Content"));
        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var steamPaths = new List<string>();
        foreach (var (hive, key, value) in new[]
                 {
                     (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                     (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                     (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                 })
        {
            try
            {
                if (hive.OpenSubKey(key)?.GetValue(value) is string path && path.Length > 0)
                    steamPaths.Add(Path.GetFullPath(path.Replace('/', '\\')));
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or IOException or ArgumentException)
            {
            }
        }

        var libraries = new List<string>(steamPaths);
        foreach (var steam in steamPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                foreach (Match m in VdfPathRegex().Matches(File.ReadAllText(vdf)))
                    libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
            }
            catch (IOException)
            {
            }
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EpicInstallLocations()
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) yield break;
        foreach (var item in Directory.EnumerateFiles(manifests, "*.item"))
        {
            string? location = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(item));
                var root = doc.RootElement;
                var name = root.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : null;
                if (name is not null && name.Contains(GameFolderName, StringComparison.OrdinalIgnoreCase)
                    && root.TryGetProperty("InstallLocation", out var loc))
                    location = loc.GetString();
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
            }
            if (!string.IsNullOrEmpty(location)) yield return location;
        }
    }

    private static IEnumerable<string> SafeDrives()
    {
        try
        {
            return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName).ToList();
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
    }

    [GeneratedRegex("\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPathRegex();
}
