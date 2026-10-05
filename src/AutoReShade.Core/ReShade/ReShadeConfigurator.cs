namespace AutoReShade.Core.ReShade;

public sealed record PresetShortcut(string PresetPath, ShortcutKey Key);

public sealed record ApplyResult(bool Success, bool Changed, string Message);

/// <summary>
/// Writes AutoReShade's preset shortcuts into ReShade.ini using ReShade's built-in
/// "PresetShortcutKeys" / "PresetShortcutPaths" feature. Shortcuts the player set up
/// themselves are kept. ReShade reads these settings when the game starts.
/// </summary>
public static class ReShadeConfigurator
{
    public const string IniFileName = "ReShade.ini";
    public const string BackupSuffix = ".autoreshade-backup";
    private const string Section = "GENERAL";
    private const string KeysKey = "PresetShortcutKeys";
    private const string PathsKey = "PresetShortcutPaths";

    public static string IniPath(string reshadeDir) => Path.Combine(reshadeDir, IniFileName);

    /// <summary>ReShade style path: ".\name.ini" for presets in the ReShade folder, full path otherwise.</summary>
    public static string FormatPresetPath(string reshadeDir, string presetPath)
    {
        var full = Path.GetFullPath(presetPath);
        var relative = Path.GetRelativePath(Path.GetFullPath(reshadeDir), full);
        if (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            return ".\\" + relative;
        return full;
    }

    public static string ResolvePresetPath(string reshadeDir, string reshadePath)
    {
        var trimmed = reshadePath.Trim().Trim('"');
        return Path.GetFullPath(Path.IsPathRooted(trimmed) ? trimmed : Path.Combine(reshadeDir, trimmed));
    }

    public static IReadOnlyList<PresetShortcut> ReadShortcuts(string reshadeDir)
    {
        var path = IniPath(reshadeDir);
        if (!File.Exists(path)) return Array.Empty<PresetShortcut>();
        return ReadShortcuts(IniDocument.Load(path), reshadeDir);
    }

    /// <summary>The preset ReShade had active when it last saved its settings.</summary>
    public static string? ReadCurrentPreset(string reshadeDir)
    {
        var path = IniPath(reshadeDir);
        if (!File.Exists(path)) return null;
        var value = IniDocument.Load(path).Get(Section, "PresetPath");
        return string.IsNullOrWhiteSpace(value) ? null : ResolvePresetPath(reshadeDir, value);
    }

    public static bool IsUpToDate(string reshadeDir, IReadOnlyList<PresetShortcut> wanted)
    {
        var existing = ReadShortcuts(reshadeDir);
        return wanted.All(w => existing.Any(e => e.Key == w.Key && SamePath(e.PresetPath, w.PresetPath)))
            && existing.Where(e => ShortcutKeyPool.Contains(e.Key))
                       .All(e => wanted.Any(w => w.Key == e.Key && SamePath(e.PresetPath, w.PresetPath)));
    }

    public static ApplyResult Apply(string reshadeDir, IReadOnlyList<PresetShortcut> wanted)
    {
        var path = IniPath(reshadeDir);
        if (!File.Exists(path))
            return new ApplyResult(false, false, $"ReShade.ini was not found in {reshadeDir}. Start the game once with ReShade installed, then try again.");

        foreach (var shortcut in wanted)
            if (shortcut.PresetPath.Contains(','))
                return new ApplyResult(false, false, $"ReShade cannot use preset paths that contain a comma: {shortcut.PresetPath}. Please rename the file or folder.");

        try
        {
            var document = IniDocument.Load(path);
            var existing = ReadShortcuts(document, reshadeDir);

            // Keep the player's own shortcuts unless they use one of our keys or point at a preset we manage.
            var kept = existing.Where(e => !ShortcutKeyPool.Contains(e.Key)
                                           && !wanted.Any(w => w.Key == e.Key || SamePath(w.PresetPath, e.PresetPath)))
                               .ToList();
            var final = kept.Concat(wanted).ToList();

            var keys = string.Join(",", final.SelectMany(s => s.Key.ToReShadeNumbers()));
            var paths = string.Join(",", final.Select(s => FormatPresetPath(reshadeDir, s.PresetPath)));

            if (document.Get(Section, KeysKey) == keys && document.Get(Section, PathsKey) == paths)
                return new ApplyResult(true, false, "ReShade preset shortcuts are already up to date.");

            var backup = path + BackupSuffix;
            if (!File.Exists(backup)) File.Copy(path, backup);

            document.Set(Section, KeysKey, keys);
            document.Set(Section, PathsKey, paths);
            document.Save(path);
            return new ApplyResult(true, true, $"Saved {wanted.Count} preset shortcut(s) to ReShade.ini.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ApplyResult(false, false, $"Could not write ReShade.ini: {ex.Message}");
        }
    }

    private static List<PresetShortcut> ReadShortcuts(IniDocument document, string reshadeDir)
    {
        var numbers = (document.Get(Section, KeysKey) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .ToList();
        var paths = (document.Get(Section, PathsKey) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        var result = new List<PresetShortcut>();
        for (var i = 0; i < paths.Count && i * 4 + 4 <= numbers.Count; i++)
        {
            var key = new ShortcutKey((byte)Math.Clamp(numbers[i * 4], 0, 255), numbers[i * 4 + 1] != 0, numbers[i * 4 + 2] != 0, numbers[i * 4 + 3] != 0);
            result.Add(new PresetShortcut(ResolvePresetPath(reshadeDir, paths[i]), key));
        }
        return result;
    }

    public static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
}
