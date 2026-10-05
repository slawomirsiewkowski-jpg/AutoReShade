using System.Drawing;
using System.Reflection;
using Tesseract;

namespace AutoReShade.Core.Detection;

/// <summary>
/// Text recognition with the Tesseract engine that ships inside the app.
/// Extra languages work by dropping more *.traineddata files into the tessdata folder.
/// </summary>
public sealed class TesseractOcr : IDisposable
{
    private const string NativeVersion = "tesseract-5.2.0";
    private static readonly string[] NativeFiles = { "leptonica-1.82.0.dll", "tesseract50.dll" };
    private static readonly string[] IgnoredTrainedData = { "osd", "equ" };

    private readonly TesseractEngine _engine;

    public TesseractOcr(string tessdataDir, IReadOnlyList<string> languages)
    {
        if (languages.Count == 0) throw new ArgumentException("At least one OCR language is required.", nameof(languages));
        Languages = string.Join('+', languages);
        _engine = new TesseractEngine(tessdataDir, Languages, EngineMode.LstmOnly);
        _engine.DefaultPageSegMode = PageSegMode.SparseText;
    }

    public string Languages { get; }

    /// <summary>Unpacks the native OCR libraries next to the user's profile and tells the wrapper where they are.</summary>
    public static void PrepareNativeLibraries(string runtimeDir)
    {
        var root = Path.Combine(runtimeDir, NativeVersion);
        var x64 = Path.Combine(root, "x64");
        Directory.CreateDirectory(x64);
        foreach (var file in NativeFiles)
            ExtractResource($"AutoReShade.native.{file}", Path.Combine(x64, file));
        TesseractEnviornment.CustomSearchPath = root;
    }

    /// <summary>Makes sure the English language file (built into the app) is in the tessdata folder.</summary>
    public static void EnsureBuiltInLanguage(string tessdataDir)
    {
        Directory.CreateDirectory(tessdataDir);
        ExtractResource("AutoReShade.tessdata.eng.traineddata", Path.Combine(tessdataDir, "eng.traineddata"));
    }

    public static IReadOnlyList<string> AvailableLanguages(string tessdataDir)
    {
        if (!Directory.Exists(tessdataDir)) return Array.Empty<string>();
        var languages = Directory.EnumerateFiles(tessdataDir, "*.traineddata")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n) && !IgnoredTrainedData.Contains(n, StringComparer.OrdinalIgnoreCase))
            .Select(n => n!)
            .OrderBy(n => n == "eng" ? 0 : 1)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return languages;
    }

    public IReadOnlyList<string> ReadLines(Bitmap image)
    {
        using var stream = new MemoryStream();
        image.Save(stream, System.Drawing.Imaging.ImageFormat.Bmp);
        using var pix = Pix.LoadFromMemory(stream.ToArray());
        using var page = _engine.Process(pix);
        var text = page.GetText() ?? string.Empty;
        return text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
    }

    public void Dispose() => _engine.Dispose();

    private static void ExtractResource(string resourceName, string targetPath)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource {resourceName} is missing.");
        if (File.Exists(targetPath) && new FileInfo(targetPath).Length == stream.Length)
            return;

        var temp = targetPath + ".tmp";
        using (var file = File.Create(temp))
            stream.CopyTo(file);
        File.Move(temp, targetPath, overwrite: true);
    }
}
