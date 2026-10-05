using AutoReShade.Core.Game;
using AutoReShade.Core.Maps;
using AutoReShade.Core.ReShade;
using AutoReShade.Core.Settings;

namespace AutoReShade.Tests;

public class SettingsTests
{
    private static readonly MapCatalog Catalog = MapCatalog.LoadBuiltIn();

    [Fact]
    public void PresetFallsBackFromMapToRealmToDefault()
    {
        var s = new AppSettings { DefaultPreset = @"C:\p\default.ini" };
        s.RealmPresets["the-macmillan-estate"] = @"C:\p\macmillan.ini";
        s.MapPresets["coal-tower"] = @"C:\p\coal.ini";

        Assert.Equal(@"C:\p\coal.ini", s.ResolvePreset(Catalog.FindMap("coal-tower")!));
        Assert.Equal(@"C:\p\macmillan.ini", s.ResolvePreset(Catalog.FindMap("shelter-woods")!));
        Assert.Equal(@"C:\p\default.ini", s.ResolvePreset(Catalog.FindMap("the-game")!));
        Assert.Null(new AppSettings().ResolvePreset(Catalog.FindMap("the-game")!));
    }

    [Fact]
    public void ShortcutSlotsStayStableWhenPresetsChange()
    {
        var s = new AppSettings { DefaultPreset = @"C:\p\default.ini" };
        s.RealmPresets["coldwind-farm"] = @"C:\p\coldwind.ini";
        s.RealmPresets["red-forest"] = @"C:\p\red.ini";
        var first = s.BuildShortcuts(_ => true)!;
        var redKey = first.Single(x => x.PresetPath.EndsWith("red.ini")).Key;

        s.RealmPresets.Remove("coldwind-farm");
        s.MapPresets["the-game"] = @"C:\p\game.ini";
        var second = s.BuildShortcuts(_ => true)!;

        Assert.Equal(3, second.Count);
        Assert.Equal(redKey, second.Single(x => x.PresetPath.EndsWith("red.ini")).Key);
        Assert.Equal(second.Count, second.Select(x => x.Key).Distinct().Count());
        Assert.Equal(redKey, s.ShortcutFor(@"C:\p\red.ini"));
    }

    [Fact]
    public void SamePresetUsedTwiceGetsOneKey()
    {
        var s = new AppSettings { DefaultPreset = @"C:\p\a.ini" };
        s.MapPresets["the-game"] = @"C:\P\A.INI";
        Assert.Single(s.BuildShortcuts(_ => true)!);
    }

    [Fact]
    public void SettingsSurviveSaveAndLoad()
    {
        var file = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        try
        {
            var store = new SettingsStore(file);
            var s = new AppSettings { ReShadeDirectory = @"D:\Games\DBD" };
            s.MapClocks["Coal-Tower"] = @"C:\clocks\coal.png";
            s.Overlay.Opacity = 0.5;
            store.Save(s);

            var loaded = store.Load();
            Assert.Equal(@"D:\Games\DBD", loaded.ReShadeDirectory);
            Assert.Equal(@"C:\clocks\coal.png", loaded.ResolveClock(Catalog.FindMap("coal-tower")!));
            Assert.Equal(0.5, loaded.Overlay.Opacity);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void BrokenSettingsFileFallsBackToDefaults()
    {
        var file = Path.Combine(Path.GetTempPath(), $"settings-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, "{ broken");
        try
        {
            var loaded = new SettingsStore(file).Load();
            Assert.True(loaded.Overlay.Enabled);
            Assert.True(File.Exists(file + ".broken"));
        }
        finally
        {
            File.Delete(file);
            File.Delete(file + ".broken");
        }
    }

    [Fact]
    public void ClockFilesAreMatchedByName()
    {
        var files = new[]
        {
            @"C:\c\Coal Tower.png",
            @"C:\c\macmillan_shelter-woods_clock.PNG",
            @"C:\c\Badham Preschool III.png",
            @"C:\c\Badham Preschool I.png",
            @"C:\c\RPD East Wing.png",
            @"C:\c\Raccoon City Police Station East Wing callouts.png",
            @"C:\c\readme.txt",
            @"C:\c\random picture.png",
        };

        var result = ClockAutoAssigner.Assign(Catalog, files);

        Assert.Equal(@"C:\c\Coal Tower.png", result["coal-tower"]);
        Assert.Equal(@"C:\c\macmillan_shelter-woods_clock.PNG", result["shelter-woods"]);
        Assert.Equal(@"C:\c\Badham Preschool III.png", result["badham-preschool-iii"]);
        Assert.Equal(@"C:\c\Badham Preschool I.png", result["badham-preschool-i"]);
        Assert.Equal(@"C:\c\Raccoon City Police Station East Wing callouts.png", result["raccoon-city-police-station-east-wing"]);
        Assert.Equal(5, result.Count);
    }

    [Theory]
    [InlineData("0", WindowModeSetting.Fullscreen)]
    [InlineData("1", WindowModeSetting.WindowedFullscreen)]
    [InlineData("2", WindowModeSetting.Windowed)]
    [InlineData(null, WindowModeSetting.Unknown)]
    public void ReadsTheGameWindowMode(string? value, WindowModeSetting expected)
    {
        var root = Path.Combine(Path.GetTempPath(), $"dbdcfg-{Guid.NewGuid():N}");
        var dir = Path.Combine(root, "WindowsClient");
        Directory.CreateDirectory(dir);
        var lines = "[/Script/DeadByDaylight.DBDGameUserSettings]\r\nResolutionSizeX=1920\r\n" + (value is null ? "" : $"FullscreenMode={value}\r\n");
        File.WriteAllText(Path.Combine(dir, "GameUserSettings.ini"), lines);
        try
        {
            Assert.Equal(expected, GameWindowSettings.Read(root).Mode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
