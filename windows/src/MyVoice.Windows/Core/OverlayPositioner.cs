using System.Drawing;

namespace MyVoice.Windows.Core;

/// <summary>
/// Where the recording overlay goes (the Mac's OverlayPositioner, in Windows screen coordinates: origin top-left,
/// y downward). Upper-left of the caret, its bottom edge on the caret's line, kept inside the work area of the
/// caret's monitor — which can sit at negative x or y.
/// </summary>
public static class OverlayPositioner
{
    /// <param name="caret">Caret (or mouse) rectangle in physical screen pixels.</param>
    /// <param name="overlay">Overlay size in physical pixels.</param>
    /// <param name="workArea">Work area of the caret's monitor (without the taskbar).</param>
    /// <param name="scale">That monitor's DPI / 96: the Mac's offsets are in 96-DPI pixels.</param>
    public static Point Position(Rectangle caret, Size overlay, Rectangle workArea, double scale = 1)
    {
        int Px(double points) => (int)Math.Round(points * scale);
        var x = caret.Left - overlay.Width + Px(8); // overlaps the caret slightly, like the Mac
        var y = caret.Top + Px(12) - overlay.Height;

        if (x < workArea.Left) x = Math.Max(workArea.Left, caret.Left + Px(4));   // off the left edge: right of the caret
        if (x + overlay.Width > workArea.Right) x = workArea.Right - overlay.Width;
        if (y < workArea.Top) y = caret.Bottom + Px(4);                           // off the top: below the caret
        if (y + overlay.Height > workArea.Bottom) y = workArea.Bottom - overlay.Height;

        return new Point(Math.Max(x, workArea.Left), Math.Max(y, workArea.Top));
    }
}
