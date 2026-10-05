using System.Drawing;
using System.Drawing.Imaging;
using AutoReShade.Native;

namespace AutoReShade.Services;

/// <summary>Takes a screenshot of part of the screen, exactly like the Print Screen key would.</summary>
public static class ScreenCapture
{
    public static Bitmap Capture(Rectangle screenArea)
    {
        var bitmap = new Bitmap(screenArea.Width, screenArea.Height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bitmap);
        g.CopyFromScreen(screenArea.Left, screenArea.Top, 0, 0, screenArea.Size, CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    public static Rectangle ToRectangle(Win32.RECT r) => new(r.Left, r.Top, r.Width, r.Height);

    /// <summary>Blacks out the part of a capture covered by our own overlay, so the clock image is never read as text.</summary>
    public static void MaskOut(Bitmap capture, Rectangle captureArea, Rectangle? overlayArea)
    {
        if (overlayArea is not { } overlay) return;
        var hit = Rectangle.Intersect(captureArea, overlay);
        if (hit.IsEmpty) return;
        hit.Offset(-captureArea.Left, -captureArea.Top);
        using var g = Graphics.FromImage(capture);
        g.FillRectangle(Brushes.Black, hit);
    }
}
