using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Buddy.App.State;
using DrawingColor = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;

namespace Buddy.App.WinUI;

internal static class TrayIconRenderer
{
    private const int CanvasSize = 64;

    public static Icon Create(BuddyRuntimeMode mode)
    {
        (string glyph, DrawingColor background) = mode switch
        {
            BuddyRuntimeMode.Recording =>
                ("r", DrawingColor.FromArgb(220, 53, 69)),
            BuddyRuntimeMode.Processing =>
                ("…", DrawingColor.FromArgb(91, 92, 226)),
            BuddyRuntimeMode.Attention =>
                ("!", DrawingColor.FromArgb(217, 119, 6)),
            _ => ("b", DrawingColor.FromArgb(91, 92, 226)),
        };

        using var bitmap = new Bitmap(
            CanvasSize,
            CanvasSize,
            PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(DrawingColor.Transparent);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        using var backgroundBrush = new SolidBrush(background);
        graphics.FillEllipse(
            backgroundBrush,
            x: 2,
            y: 2,
            width: CanvasSize - 4,
            height: CanvasSize - 4);

        using var font = new DrawingFont(
            "Segoe UI",
            emSize: 38,
            FontStyle.Bold,
            GraphicsUnit.Pixel);
        using var foregroundBrush = new SolidBrush(DrawingColor.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        graphics.DrawString(
            glyph,
            font,
            foregroundBrush,
            new RectangleF(0, -2, CanvasSize, CanvasSize),
            format);

        IntPtr iconHandle = bitmap.GetHicon();
        try
        {
            using Icon borrowedIcon = Icon.FromHandle(iconHandle);
            return (Icon)borrowedIcon.Clone();
        }
        finally
        {
            _ = DestroyIcon(iconHandle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr iconHandle);
}
