using AutoReShade.Core.Detection;
using AutoReShade.Core.Maps;

namespace AutoReShade.Tests;

/// <summary>The fixtures are real OCR readings of the detection area, taken in the English game.</summary>
public class LobbyScreenMatcherTests
{
    private static readonly LobbyScreenMatcher Matcher = new(MapCatalog.LoadBuiltIn());

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
        var matcher = new LobbyScreenMatcher(MapCatalog.Load(MapCatalog.ReadBuiltInJson(), custom));

        var isLobby = matcher.IsLobby(["VALMIS", "TAKAISIN [ESC]"]);

        Assert.True(isLobby);
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
