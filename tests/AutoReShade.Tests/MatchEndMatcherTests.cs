using AutoReShade.Core.Detection;
using AutoReShade.Core.Maps;

namespace AutoReShade.Tests;

/// <summary>The fixtures are real OCR readings of the detection area, taken in the English game.</summary>
public class MatchEndMatcherTests
{
    private static readonly MatchEndMatcher Matcher = new(MapCatalog.LoadBuiltIn());

    private static string[] Fixture(string name) =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Fixtures", "ocr", name + ".txt"));

    [Theory]
    [InlineData("lobby-ready-back-esc")]
    [InlineData("lobby-ready-noisy")]
    [InlineData("lobby-ready-misread-back")]
    public void RecognisesTheLobby(string fixture)
    {
        var lines = Fixture(fixture);

        var isLobby = Matcher.IsLobby(lines);

        Assert.True(isLobby);
    }

    [Theory]
    [InlineData("matchmaking-cancel")]
    [InlineData("loading-tip")]
    [InlineData("loading-map-name")]
    [InlineData("in-match-power-prompt")]
    [InlineData("main-menu")]
    [InlineData("credits")]
    public void IgnoresOtherScreens(string fixture)
    {
        var lines = Fixture(fixture);

        var isLobby = Matcher.IsLobby(lines);

        Assert.False(isLobby);
    }

    [Fact]
    public void OtherLanguagesCanBeAddedInTheCustomFile()
    {
        const string custom = """{ "readyButton": { "xx": "Valmis" } }""";
        var matcher = new MatchEndMatcher(MapCatalog.Load(MapCatalog.ReadBuiltInJson(), custom));

        var isLobby = matcher.IsLobby(["VALMIS", "TAKAISIN [ESC]"]);

        Assert.True(isLobby);
    }

    [Fact]
    public void OtherLanguagesOfTheContinueButtonCanBeAddedInTheCustomFile()
    {
        const string custom = """{ "continueButton": { "xx": "Jatka" } }""";
        var matcher = new MatchEndMatcher(MapCatalog.Load(MapCatalog.ReadBuiltInJson(), custom));

        var isScoreboard = matcher.IsScoreboard(["JATKA"]);

        Assert.True(isScoreboard);
    }

    [Theory]
    [InlineData("CONTINUE")]
    [InlineData("< CONTINUE |")]
    public void RecognisesTheResultsScreen(params string[] lines)
    {
        var isScoreboard = Matcher.IsScoreboard(lines);

        Assert.True(isScoreboard);
    }

    [Theory]
    [InlineData("yet")]
    [InlineData("Continue repairing to finish the generator")]
    [InlineData("READY", "BACK [ESC]")]
    public void IgnoresOtherTextInTheContinueButtonArea(params string[] lines)
    {
        var isScoreboard = Matcher.IsScoreboard(lines);

        Assert.False(isScoreboard);
    }

    [Theory]
    [InlineData("READY")]
    [InlineData("Get ready to escape", "BACK [ESC]")]
    [InlineData("READY", "Press ESC to open the menu and get ready")]
    public void NeedsTheButtonAndTheKeyHintOnTheirOwnLines(params string[] lines)
    {
        var isLobby = Matcher.IsLobby(lines);

        Assert.False(isLobby);
    }
}
