using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AutoReShade.Core.Detection;

namespace AutoReShade.Tests;

/// <summary>End-to-end check that returning to the lobby ends the current map.</summary>
public class LobbyDetectionTests : IClassFixture<OcrFixture>
{
    private readonly OcrFixture _fx;

    public LobbyDetectionTests(OcrFixture fx) => _fx = fx;

    [Fact]
    public void LobbyClearsTheMapAfterTwoReads()
    {
        using var loading = OcrDetectionTests.SyntheticLoadingScreen(1920, 1080, "The MacMillan Estate", "Coal Tower");
        using var lobby = SyntheticLobby(1920, 1080);
        var detector = new MapDetector(_fx.Matcher, _fx.Lobby, _fx.Ocr);
        detector.ProcessFrame(loading, DetectionRegion.Default);

        var first = detector.ProcessFrame(lobby, DetectionRegion.Default);
        var mapAfterFirstRead = detector.CurrentMap;
        var second = detector.ProcessFrame(lobby, DetectionRegion.Default);
        var third = detector.ProcessFrame(lobby, DetectionRegion.Default);

        Assert.False(first.LobbyConfirmed, $"OCR read: {string.Join(" | ", first.Lines)}");
        Assert.Equal("coal-tower", mapAfterFirstRead?.Id);
        Assert.True(second.LobbyConfirmed, $"OCR read: {string.Join(" | ", second.Lines)}");
        Assert.False(third.LobbyConfirmed);
        Assert.Null(detector.CurrentMap);
    }

    [Fact]
    public void SameMapAfterTheLobbyIsReportedAgain()
    {
        using var loading = OcrDetectionTests.SyntheticLoadingScreen(1920, 1080, "The MacMillan Estate", "Coal Tower");
        using var lobby = SyntheticLobby(1920, 1080);
        var detector = new MapDetector(_fx.Matcher, _fx.Lobby, _fx.Ocr);
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
        var detector = new MapDetector(_fx.Matcher, _fx.Lobby, _fx.Ocr);
        detector.ProcessFrame(lobby, DetectionRegion.Default);
        detector.ProcessFrame(lobby, DetectionRegion.Default);
        detector.SetCurrentMap(_fx.Catalog.FindMap("the-game"));

        var later = detector.ProcessFrame(lobby, DetectionRegion.Default);

        Assert.False(later.LobbyConfirmed);
        Assert.Equal("the-game", detector.CurrentMap?.Id);
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
