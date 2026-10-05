using AutoReShade.Core.Maps;

namespace AutoReShade.Tests;

public class FileNameMatcherTests
{
    private static readonly MapCatalog Catalog = MapCatalog.LoadBuiltIn();

    [Theory]
    [InlineData("01.STX.Autohaven.ini", null, "autohaven-wreckers")]
    [InlineData("03.STX.Coldwind Farm.ini", null, "coldwind-farm")]
    [InlineData("05.STX.Eyrie of Crows.ini", "eyrie-of-crows", "forsaken-boneyard")]
    [InlineData("07.STX.Greenville.ini", "greenville-square", "withered-isle")]
    [InlineData("10.STX.Léry's.ini", null, "lerys-memorial-institute")]
    [InlineData("11.STX.Macmillan.ini", null, "the-macmillan-estate")]
    [InlineData("12.STX.Midwich.ini", "midwich-elementary-school", "silent-hill")]
    [InlineData("13.STX.Badham.ini", null, "springwood")]
    [InlineData("14.STX.Ormond.ini", null, "ormond")]
    [InlineData("15.STX.Ormond Lake.ini", "ormond-lake-mine", "ormond")]
    [InlineData("16.STX.Raccoon City.ini", null, "raccoon-city")]
    [InlineData("18.STX.Saloon.ini", "dead-dawg-saloon", "grave-of-glenvale")]
    [InlineData("19.STX.Shattered Square.ini", "the-shattered-square", "the-decimated-borgo")]
    [InlineData("21.STX.The Game.ini", "the-game", "gideon-meat-plant")]
    [InlineData("Coal Tower clock.png", "coal-tower", "the-macmillan-estate")]
    [InlineData("06.STX.Garden of Joy.ini", "garden-of-joy", "withered-isle")]
    [InlineData("02.STX.Backwater.ini", null, "backwater-swamp")]
    [InlineData("09.STX.Hawkins.ini", null, "hawkins-national-laboratory")]
    [InlineData("23.STX.Yamaoka.ini", null, "yamaoka-estate")]
    [InlineData("22.STX.Toba Landing.ini", "toba-landing", "dvarka-deepwood")]
    [InlineData("13.STX.Nostromo Wreckage.ini", "nostromo-wreckage", "dvarka-deepwood")]
    [InlineData("04.STX.Crotus Prenn.ini", null, "crotus-prenn-asylum")]
    [InlineData("08.STX.Haddonfield.ini", null, "haddonfield")]
    [InlineData("17.STX.Red Forest.ini", null, "red-forest")]
    [InlineData("20.STX.Springwood.ini", null, "springwood")]
    public void RecognisesShortNames(string file, string? mapId, string realmId)
    {
        var match = FileNameMatcher.Match(Catalog, file);
        Assert.NotNull(match);
        Assert.Equal(mapId, match!.MapId);
        Assert.Equal(realmId, match.RealmId);
    }

    [Theory]
    [InlineData("00.STX.Universal.ini")]
    [InlineData("ReShadePreset.ini")]
    [InlineData("random.png")]
    public void IgnoresUnrelatedFiles(string file) => Assert.Null(FileNameMatcher.Match(Catalog, file));

    [Fact]
    public void AssignsPresetFolderToRealmsAndMaps()
    {
        var files = new[] { @"C:\r\11.STX.Macmillan.ini", @"C:\r\13.STX.Badham.ini", @"C:\r\20.STX.Springwood.ini", @"C:\r\18.STX.Saloon.ini", @"C:\r\00.STX.Universal.ini" };
        var (realms, maps) = FileNameMatcher.AssignPresets(Catalog, files);

        Assert.Equal(@"C:\r\11.STX.Macmillan.ini", realms["the-macmillan-estate"]);
        Assert.Equal(@"C:\r\20.STX.Springwood.ini", realms["springwood"]);
        Assert.Equal(@"C:\r\18.STX.Saloon.ini", maps["dead-dawg-saloon"]);
        Assert.Equal(2, realms.Count);
        Assert.Single(maps);
    }
}
