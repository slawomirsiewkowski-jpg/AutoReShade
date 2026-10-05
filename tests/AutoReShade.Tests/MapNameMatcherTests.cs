using AutoReShade.Core.Detection;
using AutoReShade.Core.Maps;

namespace AutoReShade.Tests;

public class MapNameMatcherTests
{
    private static readonly MapNameMatcher Matcher = new(MapCatalog.LoadBuiltIn());

    private static string? Detect(params string[] lines) => Matcher.Match(lines).Map?.Id;

    [Theory]
    [InlineData("COAL TOWER", "coal-tower")]
    [InlineData("Azarov's Resting Place", "azarovs-resting-place")]
    [InlineData("AZAROV’S RESTING PLACE", "azarovs-resting-place")]
    [InlineData("Father Campbells Chapel", "father-campbells-chapel")]
    [InlineData("FREDDY FAZBEAR'S PIZZA", "freddy-fazbears-pizza")]
    [InlineData("The Game", "the-game")]
    [InlineData("MOUNT ORMOND RESORT", "mount-ormond-resort")]
    [InlineData("ORMOND LAKE MINE", "ormond-lake-mine")]
    [InlineData("COAL TOWER II", "coal-tower-ii")]
    [InlineData("SHELTER WOODS Il", "shelter-woods-ii")]
    [InlineData("MOUNT ORMOND RESORT III", "mount-ormond-resort-iii")]
    [InlineData("SANCTUM OF WRATH II", "sanctum-of-wrath-ii")]
    public void ReadsCleanNames(string line, string expected) => Assert.Equal(expected, Detect(line));

    [Theory]
    [InlineData("C0AL T0WER", "coal-tower")]
    [InlineData("RANCID ABATTO1R", "rancid-abattoir")]
    [InlineData("~ SUFFOCATION PIT .", "suffocation-pit")]
    [InlineData("Treatrnent Theatre", "treatment-theatre")]
    [InlineData("GROANING STOREH0USE", "groaning-storehouse")]
    public void ToleratesTypicalOcrMistakes(string line, string expected) => Assert.Equal(expected, Detect(line));

    [Theory]
    [InlineData("BADHAM PRESCHOOL I", "badham-preschool-i")]
    [InlineData("BADHAM PRESCHOOL II", "badham-preschool-ii")]
    [InlineData("BADHAM PRESCHOOL Il", "badham-preschool-ii")]
    [InlineData("BADHAM PRESCHOOL lll", "badham-preschool-iii")]
    [InlineData("BADHAM PRESCHOOL IV", "badham-preschool-iv")]
    [InlineData("BADHAM PRESCHOOL V", "badham-preschool-v")]
    public void TellsBadhamVariantsApart(string line, string expected) => Assert.Equal(expected, Detect(line));

    [Fact]
    public void PrefersTheMoreSpecificRaccoonCityMap()
    {
        Assert.Equal("raccoon-city-police-station-east-wing", Detect("RACCOON CITY", "RACCOON CITY POLICE STATION EAST WING"));
        Assert.Equal("raccoon-city-police-station-west-wing", Detect("RACCOON CITY POLICE STATION WEST WING"));
        Assert.Equal("raccoon-city-police-station", Detect("RACCOON CITY", "RACCOON CITY POLICE STATION"));
        Assert.Equal("raccoon-city-police-station-east-wing", Detect("RACCOON CITY POLICE STATION EAST WLNG"));
    }

    [Fact]
    public void ReadsNamesThatWrapOntoTwoLines() =>
        Assert.Equal("raccoon-city-police-station-east-wing", Detect("RACCOON CITY POLICE STATION", "EAST WING"));

    [Fact]
    public void RealmAndMapOnOneLine()
    {
        Assert.Equal("coal-tower", Detect("THE MACMILLAN ESTATE - COAL TOWER"));
        Assert.Equal("lampkin-lane", Detect("HADDONFIELD  LAMPKIN LANE"));
    }

    [Fact]
    public void UsesTheRealmLineAsContext()
    {
        var result = Matcher.Match(new[] { "THE MACMILLAN ESTATE", "SHELTER WOODS", "Tip: Survivors can hide in lockers." });
        Assert.Equal("shelter-woods", result.Map?.Id);
        Assert.Equal("the-macmillan-estate", result.RealmId);
    }

    [Theory]
    [InlineData("Escape The Game three times in a row to complete this challenge")]
    [InlineData("Survivors repaired 5 generators")]
    [InlineData("THE MACMILLAN ESTATE")]
    [InlineData("Loading")]
    [InlineData("")]
    [InlineData("ORM0ND")]
    public void IgnoresTextThatIsNotAMapName(string line) => Assert.Null(Detect(line));

    [Theory]
    [InlineData("WIEŻA WĘGLOWA", "coal-tower")]
    [InlineData("Wieza Weglowa", "coal-tower")]
    [InlineData("Przedszkole w Badham III", "badham-preschool-iii")]
    [InlineData("KOHLELAGER", "coal-tower")]
    public void ReadsOtherGameLanguages(string line, string expected) => Assert.Equal(expected, Detect(line));

    [Fact]
    public void EveryMapNameInEveryLanguageMatchesItself()
    {
        var catalog = MapCatalog.LoadBuiltIn();
        foreach (var map in catalog.Maps)
            foreach (var (lang, name) in map.Names)
            {
                var result = Matcher.Match(new[] { name });
                Assert.True(map.Id == result.Map?.Id, $"{lang} \"{name}\" was read as {result.Map?.Id ?? "nothing"}");
            }
    }
}
