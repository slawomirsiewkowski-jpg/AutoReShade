using System.Diagnostics;
using System.Drawing;
using AutoReShade.Core.Maps;

namespace AutoReShade.Core.Detection;

public sealed record DetectionResult(
    IReadOnlyList<string> Lines,
    MatchResult Match,
    bool SkippedOcr,
    double BrightFraction,
    MapInfo? NewlyConfirmed,
    bool LobbyConfirmed,
    TimeSpan Elapsed);

/// <summary>
/// Reads one frame (or the detection region of it) and decides whether a new map has been loaded
/// or the match is over (lobby, or results screen via the Continue button area). A map is confirmed after a near-perfect read, or after the
/// same map is read twice in a row, so a single bad OCR result never switches anything.
/// </summary>
public sealed class MapDetector
{
    /// <summary>Reads at or above this score are trusted immediately.</summary>
    public const double InstantConfirmScore = 0.97;

    /// <summary>OCR is skipped when (almost) nothing bright is in the region, or when it is mostly bright (gameplay, not text).</summary>
    public const double MinBrightFraction = 0.0005;
    public const double MaxBrightFraction = 0.25;

    private readonly MapNameMatcher _matcher;
    private readonly MatchEndMatcher _endScreens;
    private readonly TesseractOcr _ocr;
    private string? _pendingMapId;
    private int _pendingCount;
    private bool _lobbyReported;

    public MapDetector(MapNameMatcher matcher, MatchEndMatcher endScreens, TesseractOcr ocr)
    {
        _matcher = matcher;
        _endScreens = endScreens;
        _ocr = ocr;
    }

    public int Threshold { get; set; } = FramePreprocessor.DefaultThreshold;

    public MapInfo? CurrentMap { get; private set; }

    public void SetCurrentMap(MapInfo? map)
    {
        CurrentMap = map;
        _pendingMapId = null;
        _pendingCount = 0;
    }

    /// <summary>Processes a whole game frame, cutting out the detection region first.</summary>
    public DetectionResult ProcessFrame(Bitmap frame, DetectionRegion region)
    {
        var rect = region.ToPixels(frame.Width, frame.Height);
        using var crop = frame.Clone(rect, frame.PixelFormat);
        return ProcessRegion(crop, frame.Height);
    }

    /// <summary>Processes an already cut-out detection region. <paramref name="frameHeight"/> is the full game height.</summary>
    public DetectionResult ProcessRegion(Bitmap regionImage, int frameHeight)
    {
        var watch = Stopwatch.StartNew();
        using var prepared = FramePreprocessor.Prepare(regionImage, frameHeight, Threshold);
        if (prepared.BrightFraction < MinBrightFraction || prepared.BrightFraction > MaxBrightFraction)
            return new DetectionResult(Array.Empty<string>(), MatchResult.None, true, prepared.BrightFraction, null, false, watch.Elapsed);

        var lines = _ocr.ReadLines(prepared.Image);
        var match = _matcher.Match(lines);
        var confirmed = Confirm(match);
        var lobbyConfirmed = ConfirmLobby(lines);
        return new DetectionResult(lines, match, false, prepared.BrightFraction, confirmed, lobbyConfirmed, watch.Elapsed);
    }

    /// <summary>Processes a whole game frame, cutting out the Continue button area first.</summary>
    public bool ProcessContinueButtonFrame(Bitmap frame)
    {
        var rect = DetectionRegion.ContinueButton.ToPixels(frame.Width, frame.Height);
        using var crop = frame.Clone(rect, frame.PixelFormat);
        return ProcessContinueButtonRegion(crop, frame.Height);
    }

    /// <summary>
    /// Reads the Continue button area (see <see cref="DetectionRegion.ContinueButton"/>). Seeing the
    /// results screen's button ends the current map. Returns true when it did.
    /// </summary>
    public bool ProcessContinueButtonRegion(Bitmap regionImage, int frameHeight)
    {
        if (CurrentMap is null) return false;
        using var prepared = FramePreprocessor.Prepare(regionImage, frameHeight, Threshold);
        if (prepared.BrightFraction < MinBrightFraction || prepared.BrightFraction > MaxBrightFraction) return false;
        if (!_endScreens.IsScoreboard(_ocr.ReadLines(prepared.Image))) return false;

        SetCurrentMap(null);
        return true;
    }

    /// <summary>
    /// Reports the lobby once per visit; the current map ends there. One read is enough: players often
    /// press Ready at once, and the Ready button with the [ESC] hint never appears during a match.
    /// </summary>
    private bool ConfirmLobby(IReadOnlyList<string> lines)
    {
        if (!_endScreens.IsLobby(lines))
        {
            _lobbyReported = false;
            return false;
        }
        if (_lobbyReported) return false;

        _lobbyReported = true;
        SetCurrentMap(null);
        return true;
    }

    private MapInfo? Confirm(MatchResult match)
    {
        var map = match.Map;
        if (map is null) return null;

        if (CurrentMap is not null && map.Id == CurrentMap.Id)
        {
            _pendingMapId = null;
            _pendingCount = 0;
            return null;
        }

        if (map.Id == _pendingMapId) _pendingCount++;
        else
        {
            _pendingMapId = map.Id;
            _pendingCount = 1;
        }

        if (match.Score < InstantConfirmScore && _pendingCount < 2)
            return null;

        SetCurrentMap(map);
        return map;
    }
}
