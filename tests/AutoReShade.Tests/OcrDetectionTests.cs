using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using AutoReShade.Core.Detection;
using AutoReShade.Core.Maps;

namespace AutoReShade.Tests;

/// <summary>Shared OCR engine (loading it takes a moment, so all tests reuse it).</summary>
public sealed class OcrFixture : IDisposable
{
    public OcrFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "autoreshade-tests", "ocr");
        var tessdata = Path.Combine(root, "tessdata");
        TesseractOcr.PrepareNativeLibraries(Path.Combine(root, "runtime"));
        TesseractOcr.EnsureBuiltInLanguage(tessdata);
        Ocr = new TesseractOcr(tessdata, TesseractOcr.AvailableLanguages(tessdata));
        Catalog = MapCatalog.LoadBuiltIn();
        Matcher = new MapNameMatcher(Catalog);
        Lobby = new LobbyScreenMatcher(Catalog);
    }

    public TesseractOcr Ocr { get; }
    public MapCatalog Catalog { get; }
    public MapNameMatcher Matcher { get; }
    public LobbyScreenMatcher Lobby { get; }

    public void Dispose() => Ocr.Dispose();
}

/// <summary>
/// End-to-end check of the detection pipeline on synthetic loading screens:
/// realm and map name in white capitals in the lower-left corner over a dark, noisy background.
/// </summary>
public class OcrDetectionTests : IClassFixture<OcrFixture>
{
    private readonly OcrFixture _fx;

    public OcrDetectionTests(OcrFixture fx) => _fx = fx;

    [Theory]
    [InlineData(1920, 1080, "The MacMillan Estate", "Coal Tower", "coal-tower")]
    [InlineData(2560, 1440, "Springwood", "Badham Preschool I", "badham-preschool-i")]
    [InlineData(3840, 2160, "Raccoon City", "Raccoon City Police Station East Wing", "raccoon-city-police-station-east-wing")]
    [InlineData(1920, 1080, "Autohaven Wreckers", "Azarov's Resting Place", "azarovs-resting-place")]
    [InlineData(2560, 1440, "Léry's Memorial Institute", "Treatment Theatre", "treatment-theatre")]
    [InlineData(3840, 2160, "Gideon Meat Plant", "The Game", "the-game")]
    [InlineData(1280, 720, "Withered Isle", "Freddy Fazbear's Pizza", "freddy-fazbears-pizza")]
    public void DetectsTheMapAtCommonResolutions(int width, int height, string realm, string map, string expectedId)
    {
        using var frame = SyntheticLoadingScreen(width, height, realm, map);
        var detector = new MapDetector(_fx.Matcher, _fx.Lobby, _fx.Ocr);

        var result = detector.ProcessFrame(frame, DetectionRegion.Default);

        Assert.False(result.SkippedOcr, $"OCR was skipped (bright fraction {result.BrightFraction:P2})");
        Assert.True(expectedId == result.Match.Map?.Id,
            $"Expected {expectedId}, got {result.Match.Map?.Id ?? "nothing"}. OCR read: {string.Join(" | ", result.Lines)}");
        Assert.Equal(expectedId, result.NewlyConfirmed?.Id);
    }

    [Fact]
    public void EmptyDarkScreenSkipsOcr()
    {
        using var frame = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(frame)) g.Clear(Color.FromArgb(12, 10, 10));
        var detector = new MapDetector(_fx.Matcher, _fx.Lobby, _fx.Ocr);

        var result = detector.ProcessFrame(frame, DetectionRegion.Default);

        Assert.True(result.SkippedOcr);
        Assert.Null(result.NewlyConfirmed);
    }

    [Fact]
    public void SameMapIsReportedOnlyOnce()
    {
        using var frame = SyntheticLoadingScreen(1920, 1080, "Red Forest", "Mother's Dwelling");
        var detector = new MapDetector(_fx.Matcher, _fx.Lobby, _fx.Ocr);

        Assert.Equal("mothers-dwelling", detector.ProcessFrame(frame, DetectionRegion.Default).NewlyConfirmed?.Id);
        Assert.Null(detector.ProcessFrame(frame, DetectionRegion.Default).NewlyConfirmed);
    }

    internal static Bitmap SyntheticLoadingScreen(int width, int height, string realm, string map)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using (var background = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(40, 30, 28), Color.FromArgb(8, 8, 10), 75f))
            g.FillRectangle(background, 0, 0, width, height);

        var random = new Random(width ^ map.GetHashCode());
        for (var i = 0; i < 400; i++)
        {
            using var speck = new SolidBrush(Color.FromArgb(random.Next(20, 90), random.Next(60, 140), random.Next(40, 90), random.Next(40, 80)));
            g.FillEllipse(speck, random.Next(width), random.Next(height), random.Next(2, 30), random.Next(2, 30));
        }

        var scale = height / 1080f;
        using var realmFont = new Font("Arial", 22 * scale, FontStyle.Regular, GraphicsUnit.Pixel);
        using var mapFont = new Font("Arial", 38 * scale, FontStyle.Bold, GraphicsUnit.Pixel);
        g.DrawString(realm.ToUpperInvariant(), realmFont, Brushes.Gainsboro, 80 * scale, height * 0.80f);
        g.DrawString(map.ToUpperInvariant(), mapFont, Brushes.White, 80 * scale, height * 0.83f);

        using var tipFont = new Font("Arial", 18 * scale, GraphicsUnit.Pixel);
        using var tipBrush = new SolidBrush(Color.FromArgb(150, 150, 150));
        g.DrawString("Tip: Hold the button to repair generators faster with a toolbox.", tipFont, tipBrush, 80 * scale, height * 0.92f);
        return bitmap;
    }
}
