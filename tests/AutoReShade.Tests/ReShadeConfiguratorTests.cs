using AutoReShade.Core.ReShade;

namespace AutoReShade.Tests;

public sealed class ReShadeConfiguratorTests : IDisposable
{
    // Shortened copy of a real ReShade 6.8 ReShade.ini.
    private const string SampleIni =
        "[GENERAL]\r\n" +
        "EffectSearchPaths=.\\reshade-shaders\\Shaders\\**\r\n" +
        "NoDebugInfo=1\r\n" +
        "PresetPath=.\\00.Universal.ini\r\n" +
        "PresetShortcutKeys=\r\n" +
        "PresetShortcutPaths=\r\n" +
        "PresetTransitionDuration=1000\r\n" +
        "\r\n" +
        "[INPUT]\r\n" +
        "ForceShortcutModifiers=1\r\n" +
        "KeyOverlay=36,0,0,0\r\n" +
        "\r\n" +
        "[OVERLAY]\r\n" +
        "Window=[Window][Debug##Default],Pos=60,,60,Size=400,,400,Collapsed=0\r\n";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "autoreshade-tests", Guid.NewGuid().ToString("N"));

    public ReShadeConfiguratorTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "ReShade.ini"), SampleIni);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Preset(string name) => Path.Combine(_dir, name);

    [Fact]
    public void WritesShortcutsAndKeepsEverythingElse()
    {
        var wanted = new[]
        {
            new PresetShortcut(Preset("01.Autohaven.ini"), ShortcutKeyPool.Slots[0]),
            new PresetShortcut(Preset("10.Léry's.ini"), ShortcutKeyPool.Slots[1]),
        };

        var result = ReShadeConfigurator.Apply(_dir, wanted);

        Assert.True(result.Success, result.Message);
        Assert.True(result.Changed);
        var text = File.ReadAllText(Path.Combine(_dir, "ReShade.ini"));
        Assert.Contains("PresetShortcutKeys=124,0,0,0,125,0,0,0\r\n", text);
        Assert.Contains("PresetShortcutPaths=.\\01.Autohaven.ini,.\\10.Léry's.ini\r\n", text);
        Assert.Equal(SampleIni.Length, text.Length - "124,0,0,0,125,0,0,0".Length - ".\\01.Autohaven.ini,.\\10.Léry's.ini".Length);
        Assert.Contains("Window=[Window][Debug##Default],Pos=60,,60,Size=400,,400,Collapsed=0", text);
        Assert.True(File.Exists(Path.Combine(_dir, "ReShade.ini" + ReShadeConfigurator.BackupSuffix)));
        Assert.True(ReShadeConfigurator.IsUpToDate(_dir, wanted));
    }

    [Fact]
    public void SecondApplyWithSameDataChangesNothing()
    {
        var wanted = new[] { new PresetShortcut(Preset("a.ini"), ShortcutKeyPool.Slots[0]) };
        ReShadeConfigurator.Apply(_dir, wanted);
        var before = File.GetLastWriteTimeUtc(Path.Combine(_dir, "ReShade.ini"));

        var result = ReShadeConfigurator.Apply(_dir, wanted);

        Assert.True(result.Success);
        Assert.False(result.Changed);
        Assert.Equal(before, File.GetLastWriteTimeUtc(Path.Combine(_dir, "ReShade.ini")));
    }

    [Fact]
    public void KeepsThePlayersOwnShortcuts()
    {
        var ini = SampleIni
            .Replace("PresetShortcutKeys=\r\n", "PresetShortcutKeys=49,1,0,0,124,0,0,0\r\n")
            .Replace("PresetShortcutPaths=\r\n", "PresetShortcutPaths=.\\mine.ini,.\\old-managed.ini\r\n");
        File.WriteAllText(Path.Combine(_dir, "ReShade.ini"), ini);

        ReShadeConfigurator.Apply(_dir, new[] { new PresetShortcut(Preset("new.ini"), ShortcutKeyPool.Slots[0]) });

        var shortcuts = ReShadeConfigurator.ReadShortcuts(_dir);
        Assert.Equal(2, shortcuts.Count);
        Assert.Contains(shortcuts, s => s.Key == new ShortcutKey(49, Ctrl: true) && s.PresetPath.EndsWith("mine.ini"));
        Assert.Contains(shortcuts, s => s.Key == ShortcutKeyPool.Slots[0] && s.PresetPath.EndsWith("new.ini"));
    }

    [Fact]
    public void PresetsOutsideTheReShadeFolderUseFullPaths()
    {
        var outside = Path.Combine(Path.GetTempPath(), "presets elsewhere", "x.ini");
        Assert.Equal(".\\x.ini", ReShadeConfigurator.FormatPresetPath(_dir, Preset("x.ini")));
        Assert.Equal(".\\sub\\x.ini", ReShadeConfigurator.FormatPresetPath(_dir, Path.Combine(_dir, "sub", "x.ini")));
        Assert.Equal(Path.GetFullPath(outside), ReShadeConfigurator.FormatPresetPath(_dir, outside));
    }

    [Fact]
    public void RefusesPathsWithCommas()
    {
        var result = ReShadeConfigurator.Apply(_dir, new[] { new PresetShortcut(Preset("a,b.ini"), ShortcutKeyPool.Slots[0]) });
        Assert.False(result.Success);
        Assert.Equal(SampleIni, File.ReadAllText(Path.Combine(_dir, "ReShade.ini")));
    }

    [Fact]
    public void ReportsMissingReShadeIni()
    {
        File.Delete(Path.Combine(_dir, "ReShade.ini"));
        var result = ReShadeConfigurator.Apply(_dir, new[] { new PresetShortcut(Preset("a.ini"), ShortcutKeyPool.Slots[0]) });
        Assert.False(result.Success);
    }

    [Fact]
    public void ReadsTheCurrentPreset() =>
        Assert.Equal(Preset("00.Universal.ini"), ReShadeConfigurator.ReadCurrentPreset(_dir));

    [Fact]
    public void RecognisesPresetFiles()
    {
        File.WriteAllText(Preset("good.ini"), "Techniques=Clarity@Clarity.fx\r\n\r\n[Clarity.fx]\r\nClarityRadius=3\r\n");
        File.WriteAllText(Preset("ReShadePreset.ini"), "");
        File.WriteAllText(Preset("other.ini"), "[Settings]\r\nFoo=1\r\n");

        var presets = ReShadeLocator.ListPresets(_dir).Select(Path.GetFileName).ToList();

        Assert.Contains("good.ini", presets);
        Assert.Contains("ReShadePreset.ini", presets);
        Assert.DoesNotContain("other.ini", presets);
        Assert.DoesNotContain("ReShade.ini", presets);
    }

    [Fact]
    public void ShortcutPoolHasEnoughDistinctKeys()
    {
        Assert.True(ShortcutKeyPool.Slots.Count >= 51);
        Assert.Equal(ShortcutKeyPool.Slots.Count, ShortcutKeyPool.Slots.Distinct().Count());
        Assert.Equal("F13", ShortcutKeyPool.Slots[0].ToString());
    }
}
