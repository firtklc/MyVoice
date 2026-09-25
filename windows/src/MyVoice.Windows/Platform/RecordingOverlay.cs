using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using MyVoice.Windows.Core;

namespace MyVoice.Windows.Platform;

/// <summary>
/// The pill near the caret while dictating (the Mac's RecordingOverlay): grey with a slow wave while the mic connects,
/// blue with live bars once "speak now" has sounded. A raw window rather than a Form so nothing can activate it: it is
/// WS_EX_NOACTIVATE (never takes focus — the paste goes to whatever has focus), WS_EX_TRANSPARENT (clicks fall
/// through), topmost, a tool window (no taskbar button, no Alt+Tab entry), and layered with per-pixel alpha.
/// Create and use on the UI thread.
/// </summary>
sealed class RecordingOverlay : NativeWindow, IDisposable
{
    // The Mac pill at 96 DPI: 7 bars 3 px wide and 2 px apart, 10 px side padding, 8 px top and bottom padding.
    const int BaseWidth = 53, BaseHeight = 36, BarWidth = 3, BarGap = 2;
    const float CornerRadius = 10;
    static readonly Color RecordingColor = Color.FromArgb(240, 0, 122, 255);   // the Mac's system blue
    static readonly Color ConnectingColor = Color.FromArgb(240, 110, 112, 118); // grey: not listening yet

    readonly Func<Rectangle> _locateCaret;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    OverlayKind _kind;
    float _level;
    Point _position;

    public RecordingOverlay(Func<Rectangle> locateCaret)
    {
        _locateCaret = locateCaret;
        CreateHandle(new CreateParams
        {
            Caption = "MyVoice recording",
            Style = NativeMethods.WS_POPUP,
            ExStyle = NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW
                      | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_LAYERED,
        });
    }

    public bool Visible { get; private set; }

    /// <summary>The monitor's DPI / 96 where the overlay was last placed.</summary>
    public double Scale { get; private set; } = 1;

    public Rectangle Bounds => new(_position, new Size(Px(BaseWidth), Px(BaseHeight)));

    int Px(double points) => (int)Math.Round(points * Scale);

    /// <summary>Shows the overlay next to the caret, or — already showing — switches its look where it is.</summary>
    public void Show(OverlayKind kind)
    {
        _kind = kind;
        if (!Visible)
        {
            var caret = _locateCaret();
            Scale = NativeMethods.DpiAt(caret.Location) / 96.0;
            _position = OverlayPositioner.Position(caret, Bounds.Size, Screen.FromRectangle(caret).WorkingArea, Scale);
            _level = 0;
        }
        Render();
        if (Visible) return;
        // Shows it and re-asserts topmost without activating it.
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        Visible = true;
    }

    /// <summary>Redraws the bars; call every UI tick with the mic's latest peak (0…1).</summary>
    public void Tick(float micPeak)
    {
        if (!Visible) return;
        _level = OverlayMeter.Next(_level, micPeak);
        Render();
    }

    public void Hide()
    {
        if (!Visible) return;
        NativeMethods.ShowWindow(Handle, NativeMethods.SW_HIDE);
        Visible = false;
    }

    void Render()
    {
        var size = Bounds.Size;
        using var bitmap = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pill = RoundedRectangle(new RectangleF(0, 0, size.Width, size.Height), CornerRadius * (float)Scale))
            using (var fill = new SolidBrush(_kind == OverlayKind.Recording ? RecordingColor : ConnectingColor))
                g.FillPath(fill, pill);

            var scale = (float)Scale;
            var heights = OverlayMeter.BarHeights(_kind, _level, _clock.Elapsed.TotalSeconds);
            var barsWidth = (OverlayMeter.BarCount * BarWidth + (OverlayMeter.BarCount - 1) * BarGap) * scale;
            var left = (size.Width - barsWidth) / 2;
            for (var i = 0; i < heights.Length; i++)
            {
                var height = heights[i] * scale;
                var bar = new RectangleF(left + i * (BarWidth + BarGap) * scale, (size.Height - height) / 2, BarWidth * scale, height);
                using var path = RoundedRectangle(bar, 1.5f * scale);
                g.FillPath(Brushes.White, path);
            }
        }
        NativeMethods.UpdateLayeredWindow(Handle, bitmap, _position);
    }

    static GraphicsPath RoundedRectangle(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_MOUSEACTIVATE) { m.Result = NativeMethods.MA_NOACTIVATE; return; } // belt and braces
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
