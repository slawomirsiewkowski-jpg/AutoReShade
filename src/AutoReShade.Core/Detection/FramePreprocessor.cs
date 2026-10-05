using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AutoReShade.Core.Detection;

/// <summary>Part of the game frame that is read, as fractions of the frame size (works at any resolution).</summary>
public sealed record DetectionRegion(double X, double Y, double Width, double Height)
{
    /// <summary>Lower-left part of the screen, where the loading screen prints the realm and map name.</summary>
    public static DetectionRegion Default { get; } = new(0.0, 0.65, 0.85, 0.30);

    /// <summary>Bottom-right corner, where the results screen after a match shows its Continue button.</summary>
    public static DetectionRegion ContinueButton { get; } = new(0.80, 0.87, 0.19, 0.11);

    public Rectangle ToPixels(int frameWidth, int frameHeight)
    {
        var x = Clamp01(X);
        var y = Clamp01(Y);
        var left = (int)Math.Round(x * frameWidth);
        var top = (int)Math.Round(y * frameHeight);
        var width = (int)Math.Round(Math.Min(Clamp01(Width), 1 - x) * frameWidth);
        var height = (int)Math.Round(Math.Min(Clamp01(Height), 1 - y) * frameHeight);
        return new Rectangle(left, top, Math.Max(1, width), Math.Max(1, height));
    }

    private static double Clamp01(double v) => Math.Clamp(double.IsFinite(v) ? v : 0, 0, 1);
}

public sealed record PreparedImage(Bitmap Image, double BrightFraction) : IDisposable
{
    public void Dispose() => Image.Dispose();
}

/// <summary>
/// Turns a piece of the game frame into a clean black-on-white picture for OCR:
/// scaled to the same text size at every resolution, lightly blurred, then thresholded so only bright text remains.
/// </summary>
public static class FramePreprocessor
{
    /// <summary>Frames are scaled as if the game ran at this height (1080p text upscaled 2x reads best).</summary>
    public const int ReferenceHeight = 2160;

    public const int DefaultThreshold = 200;

    public static PreparedImage Prepare(Bitmap crop, int frameHeight, int threshold = DefaultThreshold)
    {
        var scale = Math.Clamp((double)ReferenceHeight / Math.Max(1, frameHeight), 0.5, 4.0);
        var width = Math.Max(1, (int)Math.Round(crop.Width * scale));
        var height = Math.Max(1, (int)Math.Round(crop.Height * scale));

        using var scaled = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(scaled))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(crop, new Rectangle(0, 0, width, height));
        }

        var luminance = ReadLuminance(scaled);
        var blurred = BoxBlur3(luminance, width, height);

        var output = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        var data = output.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        long bright = 0;
        try
        {
            var row = new byte[data.Stride];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var isText = blurred[y * width + x] >= threshold;
                    if (isText) bright++;
                    var value = isText ? (byte)0 : (byte)255;
                    var i = x * 3;
                    row[i] = row[i + 1] = row[i + 2] = value;
                }
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, data.Stride);
            }
        }
        finally
        {
            output.UnlockBits(data);
        }

        return new PreparedImage(output, (double)bright / ((long)width * height));
    }

    private static byte[] ReadLuminance(Bitmap bitmap)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var result = new byte[width * height];
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var row = new byte[data.Stride];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                for (var x = 0; x < width; x++)
                {
                    var i = x * 3; // BGR
                    result[y * width + x] = (byte)((row[i + 2] * 299 + row[i + 1] * 587 + row[i] * 114) / 1000);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return result;
    }

    /// <summary>3x3 box blur: removes single-pixel speckles from busy backgrounds before thresholding.</summary>
    private static byte[] BoxBlur3(byte[] source, int width, int height)
    {
        var result = new byte[source.Length];
        for (var y = 0; y < height; y++)
        {
            var y0 = Math.Max(0, y - 1);
            var y1 = Math.Min(height - 1, y + 1);
            for (var x = 0; x < width; x++)
            {
                var x0 = Math.Max(0, x - 1);
                var x1 = Math.Min(width - 1, x + 1);
                var sum = 0;
                var count = 0;
                for (var yy = y0; yy <= y1; yy++)
                    for (var xx = x0; xx <= x1; xx++)
                    {
                        sum += source[yy * width + xx];
                        count++;
                    }
                result[y * width + x] = (byte)(sum / count);
            }
        }
        return result;
    }
}
