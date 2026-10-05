using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AutoReShade.Core.Detection;

namespace AutoReShade.Tests;

/// <summary>End-to-end checks that the results screen and the lobby end the current map.</summary>
public class MatchEndDetectionTests : IClassFixture<OcrFixture>
{
    private readonly OcrFixture _fx;

    public MatchEndDetectionTests(OcrFixture fx) => _fx = fx;

    [Theory]
    [InlineData(1920, 1080)]
    [InlineData(2560, 1440)]
    [InlineData(3840, 2160)]
    public void ResultsScreenEndsTheMap(int width, int height)
    {
        using var loading = OcrDetectionTests.SyntheticLoadingScreen(width, height, "Gideon Meat Plant", "The Game");
        using var results = SyntheticResultsScreen(width, height);
        var detector = new MapDetector(_fx.Matcher, _fx.EndScreens, _fx.Ocr);
        detector.ProcessFrame(loading, DetectionRegion.Default);

        var ended = detector.ProcessContinueButtonFrame(results);

        Assert.True(ended);
        Assert.Null(detector.CurrentMap);
    }

    [Fact]
    public void MatchFrameDoesNotEndTheMap()
    {
        using var loading = OcrDetectionTests.SyntheticLoadingScreen(1920, 1080, "Gideon Meat Plant", "The Game");
        var detector = new MapDetector(_fx.Matcher, _fx.EndScreens, _fx.Ocr);
        detector.ProcessFrame(loading, DetectionRegion.Default);

        var ended = detector.ProcessContinueButtonFrame(loading);

        Assert.False(ended);
        Assert.Equal("the-game", detector.CurrentMap?.Id);
    }

    /// <summary>Players often press Ready at once, so the button may be visible for a single read only.</summary>
    [Fact]
    public void LobbyClearsTheMapOnTheFirstRead()
    {
        using var loading = OcrDetectionTests.SyntheticLoadingScreen(1920, 1080, "The MacMillan Estate", "Coal Tower");
        using var lobby = SyntheticLobby(1920, 1080);
        var detector = new MapDetector(_fx.Matcher, _fx.EndScreens, _fx.Ocr);
        detector.ProcessFrame(loading, DetectionRegion.Default);

        var first = detector.ProcessFrame(lobby, DetectionRegion.Default);
        var second = detector.ProcessFrame(lobby, DetectionRegion.Default);

        Assert.True(first.LobbyConfirmed, $"OCR read: {string.Join(" | ", first.Lines)}");
        Assert.False(second.LobbyConfirmed);
        Assert.Null(detector.CurrentMap);
    }

    [Fact]
    public void SameMapAfterTheLobbyIsReportedAgain()
    {
        using var loading = OcrDetectionTests.SyntheticLoadingScreen(1920, 1080, "The MacMillan Estate", "Coal Tower");
        using var lobby = SyntheticLobby(1920, 1080);
        var detector = new MapDetector(_fx.Matcher, _fx.EndScreens, _fx.Ocr);
        detector.ProcessFrame(loading, DetectionRegion.Default);
        detector.ProcessFrame(lobby, DetectionRegion.Default);
        detector.ProcessFrame(lobby, DetectionRegion.Default);

        var next = detector.ProcessFrame(loading, DetectionRegion.Default);

        Assert.Equal("coal-tower", next.NewlyConfirmed?.Id);
    }

    [Fact]
    public void MapChosenByHandInTheLobbyStays()
    {
        using var lobby = SyntheticLobby(1920, 1080);
        var detector = new MapDetector(_fx.Matcher, _fx.EndScreens, _fx.Ocr);
        detector.ProcessFrame(lobby, DetectionRegion.Default);
        detector.ProcessFrame(lobby, DetectionRegion.Default);
        detector.SetCurrentMap(_fx.Catalog.FindMap("the-game"));

        var later = detector.ProcessFrame(lobby, DetectionRegion.Default);

        Assert.False(later.LobbyConfirmed);
        Assert.Equal("the-game", detector.CurrentMap?.Id);
    }

    /// <summary>The results screen: a misty background and the Continue button in the bottom-right corner.</summary>
    internal static Bitmap SyntheticResultsScreen(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var mist = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(110, 104, 96), Color.FromArgb(60, 44, 30), 30f))
            g.FillRectangle(mist, 0, 0, width, height);

        var scale = height / 1080f;
        using var buttonFont = new Font("Arial", 21 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("CONTINUE", buttonFont, Brushes.White, width * 0.878f, height * 0.915f);
        return bitmap;
    }

    internal static Bitmap SyntheticLobby(int width, int height)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var floor = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(70, 68, 64), Color.FromArgb(25, 24, 22), 90f))
            g.FillRectangle(floor, 0, 0, width, height);

        var scale = height / 1080f;
        using (var button = new SolidBrush(Color.FromArgb(150, 12, 12)))
            g.FillRectangle(button, width * 0.735f, height * 0.875f, width * 0.2f, height * 0.07f);
        using var readyFont = new Font("Arial", 30 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("READY", readyFont, Brushes.White, width * 0.74f, height * 0.89f);

        using var hintFont = new Font("Arial", 20 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString("BACK [ESC]", hintFont, Brushes.White, width * 0.065f, height * 0.925f);
        return bitmap;
    }
}
