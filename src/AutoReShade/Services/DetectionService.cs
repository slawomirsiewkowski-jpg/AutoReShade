using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using AutoReShade.Core;
using AutoReShade.Core.Detection;
using AutoReShade.Core.Maps;
using AutoReShade.Core.Settings;

namespace AutoReShade.Services;

/// <summary>
/// Background loop: while the game is focused, screenshot the loading-screen text area,
/// read it and report newly loaded maps. Runs on its own thread so the UI never stutters.
/// </summary>
public sealed class DetectionService : IDisposable
{
    private const int MaxDebugCaptures = 60;

    private readonly AppPaths _paths;
    private readonly Func<AppSettings> _settings;
    private readonly GameMonitor _game;
    private readonly Func<Rectangle?> _overlayArea;
    private readonly object _engineLock = new();
    private readonly CancellationTokenSource _cts = new();
    private Thread? _thread;
    private TesseractOcr? _ocr;
    private MapNameMatcher _matcher;
    private LobbyScreenMatcher _lobby;
    private MapDetector? _detector;
    private int _debugCaptureCount;

    public DetectionService(AppPaths paths, Func<AppSettings> settings, GameMonitor game, MapCatalog catalog, Func<Rectangle?> overlayArea)
    {
        _paths = paths;
        _settings = settings;
        _game = game;
        _overlayArea = overlayArea;
        _matcher = new MapNameMatcher(catalog);
        _lobby = new LobbyScreenMatcher(catalog);
    }

    public string? EngineError { get; private set; }
    public string? EngineLanguages { get; private set; }
    public DetectionResult? LastResult { get; private set; }
    public DateTime? LastReadTime { get; private set; }

    /// <summary>Raised on the detection thread after each OCR pass.</summary>
    public event Action<DetectionResult>? FrameProcessed;

    /// <summary>Raised on the detection thread when a different map has been confirmed.</summary>
    public event Action<MapInfo>? MapConfirmed;

    /// <summary>Raised on the detection thread when the player is back in the lobby (the match is over).</summary>
    public event Action? LobbyConfirmed;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "AutoReShade map detection", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void SetCurrentMap(MapInfo? map)
    {
        lock (_engineLock) _detector?.SetCurrentMap(map);
    }

    public void ReloadCatalog(MapCatalog catalog)
    {
        lock (_engineLock)
        {
            var current = _detector?.CurrentMap;
            _matcher = new MapNameMatcher(catalog);
            _lobby = new LobbyScreenMatcher(catalog);
            if (_ocr is not null)
            {
                _detector = new MapDetector(_matcher, _lobby, _ocr);
                _detector.SetCurrentMap(current is null ? null : catalog.FindMap(current.Id));
            }
        }
    }

    /// <summary>Runs detection on a full screenshot without changing the current map (used by "Test" buttons).</summary>
    public DetectionResult? Test(Bitmap fullFrame, DetectionRegion region, int threshold, out Bitmap? preparedPreview)
    {
        preparedPreview = null;
        lock (_engineLock)
        {
            if (!EnsureEngine() || _ocr is null) return null;
            var rect = region.ToPixels(fullFrame.Width, fullFrame.Height);
            using var crop = fullFrame.Clone(rect, PixelFormat.Format24bppRgb);
            using (var prepared = FramePreprocessor.Prepare(crop, fullFrame.Height, threshold))
                preparedPreview = new Bitmap(prepared.Image);
            var tester = new MapDetector(_matcher, _lobby, _ocr) { Threshold = threshold };
            return tester.ProcessRegion(crop, fullFrame.Height);
        }
    }

    private void Run()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            var settings = _settings();
            var delay = Math.Clamp(settings.Detection.IntervalMs, 300, 10000);
            try
            {
                var snapshot = _game.Current;
                if (settings.Detection.Enabled && snapshot.IsForeground && snapshot.HasUsableClientArea)
                    ReadOnce(settings, snapshot);
                else
                    delay = 500;
            }
            catch (Exception ex)
            {
                Log.Error("Map detection pass failed", ex);
                delay = 3000;
            }

            token.WaitHandle.WaitOne(delay);
        }
    }

    private void ReadOnce(AppSettings settings, GameSnapshot snapshot)
    {
        var client = ScreenCapture.ToRectangle(snapshot.ClientArea);
        var region = settings.Detection.Region.ToPixels(client.Width, client.Height);
        region.Offset(client.Left, client.Top);

        using var capture = ScreenCapture.Capture(region);
        ScreenCapture.MaskOut(capture, region, _overlayArea());

        DetectionResult result;
        lock (_engineLock)
        {
            if (!EnsureEngine() || _detector is null) return;
            _detector.Threshold = settings.Detection.BrightnessThreshold;
            result = _detector.ProcessRegion(capture, client.Height);
        }

        if (!result.SkippedOcr)
        {
            LastResult = result;
            LastReadTime = DateTime.Now;
            if (settings.Detection.SaveDebugCaptures) SaveDebugCapture(capture, result);
            FrameProcessed?.Invoke(result);
        }

        if (result.NewlyConfirmed is { } map)
        {
            Log.Info($"Detected map {map.DisplayName} (score {result.Match.Score:0.00}) from \"{result.Match.Best?.SourceText}\" in {result.Elapsed.TotalMilliseconds:0} ms");
            MapConfirmed?.Invoke(map);
        }

        if (result.LobbyConfirmed)
        {
            Log.Info("Back in the lobby; the match is over");
            LobbyConfirmed?.Invoke();
        }
    }

    private bool EnsureEngine()
    {
        if (_ocr is not null) return true;
        if (EngineError is not null) return false;
        try
        {
            TesseractOcr.PrepareNativeLibraries(_paths.RuntimeDir);
            TesseractOcr.EnsureBuiltInLanguage(_paths.TessdataDir);
            var languages = TesseractOcr.AvailableLanguages(_paths.TessdataDir);
            _ocr = new TesseractOcr(_paths.TessdataDir, languages);
            _detector = new MapDetector(_matcher, _lobby, _ocr);
            EngineLanguages = _ocr.Languages;
            Log.Info($"OCR engine ready (languages: {_ocr.Languages})");
            return true;
        }
        catch (Exception ex)
        {
            EngineError = ex is DllNotFoundException || ex.InnerException is DllNotFoundException
                ? "The text recognition engine could not start. Install the Microsoft Visual C++ Redistributable (x64) from https://aka.ms/vs/17/release/vc_redist.x64.exe and restart AutoReShade."
                : $"The text recognition engine could not start: {ex.Message}";
            Log.Error("OCR engine failed to start", ex);
            return false;
        }
    }

    private void SaveDebugCapture(Bitmap capture, DetectionResult result)
    {
        if (_debugCaptureCount >= MaxDebugCaptures) return;
        try
        {
            Directory.CreateDirectory(_paths.CapturesDir);
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}-{result.Match.Map?.Id ?? "none"}";
            capture.Save(Path.Combine(_paths.CapturesDir, name + ".png"), ImageFormat.Png);
            File.WriteAllLines(Path.Combine(_paths.CapturesDir, name + ".txt"), result.Lines);
            _debugCaptureCount++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException)
        {
            Log.Warn($"Could not save debug capture: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(3));
        lock (_engineLock) _ocr?.Dispose();
        _cts.Dispose();
    }
}
