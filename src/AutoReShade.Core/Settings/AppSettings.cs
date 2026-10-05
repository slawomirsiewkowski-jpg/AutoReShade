using AutoReShade.Core.Detection;
using AutoReShade.Core.Maps;
using AutoReShade.Core.ReShade;

namespace AutoReShade.Core.Settings;

public sealed class OverlaySettings
{
    public bool Enabled { get; set; } = true;

    /// <summary>Left edge as a fraction of the monitor width (0..1).</summary>
    public double X { get; set; } = 0.80;

    /// <summary>Top edge as a fraction of the monitor height (0..1). The default stays below the game's "[TAB] Match details" hint.</summary>
    public double Y { get; set; } = 0.10;

    /// <summary>Overlay height as a fraction of the monitor height.</summary>
    public double Size { get; set; } = 0.30;

    public double Opacity { get; set; } = 0.75;
    public bool OnlyWhenGameFocused { get; set; } = true;
    public bool ShowMapNameNotification { get; set; } = true;
}

public sealed class HotkeySettings
{
    public string ToggleOverlay { get; set; } = "F8";
    public string MoveOverlay { get; set; } = "Shift+F8";
    public string PickMap { get; set; } = "F9";
}

public sealed class DetectionSettings
{
    public bool Enabled { get; set; } = true;
    public int IntervalMs { get; set; } = 1500;
    public double RegionX { get; set; } = DetectionRegion.Default.X;
    public double RegionY { get; set; } = DetectionRegion.Default.Y;
    public double RegionWidth { get; set; } = DetectionRegion.Default.Width;
    public double RegionHeight { get; set; } = DetectionRegion.Default.Height;
    public int BrightnessThreshold { get; set; } = FramePreprocessor.DefaultThreshold;
    public bool SaveDebugCaptures { get; set; }

    public DetectionRegion Region => new(RegionX, RegionY, RegionWidth, RegionHeight);
}

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 1;

    /// <summary>Folder that holds ReShade.ini (next to the game's .exe).</summary>
    public string? ReShadeDirectory { get; set; }

    public bool PresetSwitchingEnabled { get; set; } = true;

    /// <summary>Preset used for maps (and realms) without their own preset.</summary>
    public string? DefaultPreset { get; set; }

    /// <summary>Switch back to the default preset when the match is over (results screen or lobby).</summary>
    public bool ShouldUseDefaultPresetAfterMatch { get; set; } = true;

    /// <summary>Realm id to preset file. Applies to every map of the realm without its own preset.</summary>
    public Dictionary<string, string> RealmPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Map id to preset file.</summary>
    public Dictionary<string, string> MapPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Map id to clock image file.</summary>
    public Dictionary<string, string> MapClocks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Preset file to index in <see cref="ShortcutKeyPool.Slots"/>. Kept stable so ReShade.ini rarely changes.</summary>
    public Dictionary<string, int> PresetKeySlots { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public OverlaySettings Overlay { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();
    public DetectionSettings Detection { get; set; } = new();

    public bool StartMinimized { get; set; }
    public bool CheckWindowMode { get; set; } = true;

    public string? ResolvePreset(MapInfo map)
    {
        if (MapPresets.TryGetValue(map.Id, out var mapPreset) && !string.IsNullOrWhiteSpace(mapPreset)) return mapPreset;
        if (RealmPresets.TryGetValue(map.RealmId, out var realmPreset) && !string.IsNullOrWhiteSpace(realmPreset)) return realmPreset;
        return string.IsNullOrWhiteSpace(DefaultPreset) ? null : DefaultPreset;
    }

    /// <summary>The preset for after the match, or null to keep the one from the match.</summary>
    public string? ResolveAfterMatchPreset() =>
        ShouldUseDefaultPresetAfterMatch && !string.IsNullOrWhiteSpace(DefaultPreset) ? DefaultPreset : null;

    public string? ResolveClock(MapInfo map) =>
        MapClocks.TryGetValue(map.Id, out var clock) && !string.IsNullOrWhiteSpace(clock) ? clock : null;

    public IReadOnlyList<string> PresetsInUse() =>
        new[] { DefaultPreset }
            .Concat(RealmPresets.Values)
            .Concat(MapPresets.Values)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// Gives every preset in use its own shortcut key. Presets keep the key they already had;
    /// presets no longer used give theirs back. Returns null if there are more presets than keys.
    /// </summary>
    /// <param name="presetExists">Presets whose file is missing get no key (ReShade would create an empty preset).</param>
    public IReadOnlyList<PresetShortcut>? BuildShortcuts(Func<string, bool>? presetExists = null)
    {
        presetExists ??= File.Exists;
        var inUse = PresetsInUse().Where(presetExists).ToList();
        foreach (var stale in PresetKeySlots.Keys.Where(k => !inUse.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            PresetKeySlots.Remove(stale);
        foreach (var bad in PresetKeySlots.Where(kv => kv.Value < 0 || kv.Value >= ShortcutKeyPool.Slots.Count).Select(kv => kv.Key).ToList())
            PresetKeySlots.Remove(bad);

        var taken = new HashSet<int>(PresetKeySlots.Values);
        foreach (var preset in inUse)
        {
            if (PresetKeySlots.ContainsKey(preset)) continue;
            var free = Enumerable.Range(0, ShortcutKeyPool.Slots.Count).FirstOrDefault(i => !taken.Contains(i), -1);
            if (free < 0) return null;
            PresetKeySlots[preset] = free;
            taken.Add(free);
        }

        return inUse.Select(p => new PresetShortcut(p, ShortcutKeyPool.Slots[PresetKeySlots[p]])).ToList();
    }

    public ShortcutKey? ShortcutFor(string presetPath)
    {
        var full = Path.GetFullPath(presetPath);
        return PresetKeySlots.TryGetValue(full, out var slot) && slot >= 0 && slot < ShortcutKeyPool.Slots.Count
            ? ShortcutKeyPool.Slots[slot]
            : null;
    }
}
