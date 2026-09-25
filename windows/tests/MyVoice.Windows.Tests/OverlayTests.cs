using System.Drawing;
using MyVoice.Windows.Platform;
using MyVoice.Windows.Core;

namespace MyVoice.Windows.Tests;

// The Mac's CursorOverlayTests.swift cases, in Windows screen coordinates (origin top-left, y grows downward,
// secondary monitors can sit at negative x or y), plus multi-monitor and DPI cases.
public class OverlayPositionerTests
{
    static readonly Size Overlay = new(54, 36);
    static readonly Rectangle Primary = new(0, 0, 1920, 1040); // 1080p minus the taskbar

    static Point Place(Rectangle caret, Rectangle? workArea = null) => OverlayPositioner.Position(caret, Overlay, workArea ?? Primary);

    [Fact]
    public void OverlayPositionUpperLeftOfCaret()
    {
        var caret = new Rectangle(300, 500, 2, 20);
        var p = Place(caret);
        Assert.True(p.X < caret.Left);                                          // left of the caret
        Assert.InRange(p.Y + Overlay.Height, caret.Top, caret.Bottom);          // its bottom edge on the caret's line
        Assert.Equal(new Point(254, 476), p);
    }

    [Fact] public void OverlayPositionClampsToScreenLeft() => Assert.Equal(0, Place(new Rectangle(-10, 500, 2, 20)).X);

    [Fact]
    public void OverlayFlipsRightOfACaretNearTheLeftEdge() => Assert.Equal(24, Place(new Rectangle(20, 500, 2, 20)).X);

    [Fact]
    public void OverlayPositionClampsToScreenBottom() =>
        Assert.Equal(1040 - Overlay.Height, Place(new Rectangle(300, 1030, 2, 20)).Y); // stays above the taskbar

    [Fact]
    public void OverlayFlipsBelowACaretNearTheTop() => Assert.Equal(5 + 20 + 4, Place(new Rectangle(300, 5, 2, 20)).Y);

    [Fact]
    public void OverlayPositionClampsToScreenRight() => Assert.Equal(1920 - Overlay.Width, Place(new Rectangle(1915, 500, 2, 20)).X);

    [Fact]
    public void OverlayStaysOnAMonitorLeftOfThePrimary()
    {
        var left = new Rectangle(-1920, 0, 1920, 1040);
        Assert.Equal(new Point(-76, 476), Place(new Rectangle(-30, 500, 2, 20), left)); // not pulled onto the primary
        Assert.Equal(-1911, Place(new Rectangle(-1915, 500, 2, 20), left).X);           // flips right at its left edge
    }

    [Fact]
    public void OverlayFlipsBelowAtTheTopOfAMonitorAboveThePrimary() =>
        Assert.Equal(-1075 + 20 + 4, Place(new Rectangle(300, -1075, 2, 20), new Rectangle(0, -1080, 1920, 1040)).Y);

    [Fact]
    public void OffsetsScaleWithTheMonitorDpi()
    {
        var big = new Size(108, 72);
        var workArea = new Rectangle(0, 0, 3840, 2080);
        Assert.Equal(new Point(600 - 108 + 16, 1000 + 24 - 72), OverlayPositioner.Position(new Rectangle(600, 1000, 4, 40), big, workArea, scale: 2));
        Assert.Equal(10 + 40 + 8, OverlayPositioner.Position(new Rectangle(600, 10, 4, 40), big, workArea, scale: 2).Y); // real caret height
    }
}

public class OverlayMeterTests
{
    static float Peak(double dbfs) => (float)Math.Pow(10, dbfs / 20);

    [Fact]
    public void LevelUsesTheMacFormula()
    {
        // Mac: dB → (dB + 50) / 50, clamped to 0…1.
        Assert.Equal(1, OverlayMeter.Normalize(1), 3);
        Assert.Equal(0.5, OverlayMeter.Normalize(Peak(-25)), 3);
        Assert.Equal(0, OverlayMeter.Normalize(Peak(-60)), 3);
        Assert.Equal(0, OverlayMeter.Normalize(0));
    }

    [Fact]
    public void MeterRisesAtOnceAndFallsSmoothly()
    {
        Assert.Equal(1, OverlayMeter.Next(0.2f, 1), 3);
        var falling = OverlayMeter.Next(1, 0);
        Assert.InRange(falling, 0.1f, 0.9f);
        Assert.True(OverlayMeter.Next(falling, 0) < falling);
    }

    [Fact]
    public void BarHeightsStayWithinTheMacBounds()
    {
        foreach (var kind in new[] { OverlayKind.Connecting, OverlayKind.Recording })
        for (var level = 0f; level <= 1; level += 0.05f)
        for (var t = 0.0; t < 2; t += 0.037)
        {
            var bars = OverlayMeter.BarHeights(kind, level, t);
            Assert.Equal(7, bars.Length);
            Assert.All(bars, h => Assert.InRange(h, OverlayMeter.MinBar, OverlayMeter.MaxBar));
        }
    }

    [Fact]
    public void SilenceKeepsTheBarsLow() =>
        Assert.All(OverlayMeter.BarHeights(OverlayKind.Recording, 0, 0), h => Assert.True(h <= OverlayMeter.MinBar * 1.15f));

    [Fact]
    public void LoudSpeechFillsTheMiddleBar() => Assert.Equal(OverlayMeter.MaxBar, OverlayMeter.BarHeights(OverlayKind.Recording, 1, 0)[3]);

    [Fact]
    public void ConnectingIgnoresTheMicAndMovesOnItsOwn()
    {
        // Waiting for a cold Bluetooth mic: a gentle wave that must not look like speech.
        Assert.Equal(OverlayMeter.BarHeights(OverlayKind.Connecting, 0, 0.3), OverlayMeter.BarHeights(OverlayKind.Connecting, 1, 0.3));
        Assert.NotEqual(OverlayMeter.BarHeights(OverlayKind.Connecting, 0, 0), OverlayMeter.BarHeights(OverlayKind.Connecting, 0, 0.25));
        Assert.All(OverlayMeter.BarHeights(OverlayKind.Connecting, 0, 0.3), h => Assert.True(h <= OverlayMeter.MaxBar * 0.6f));
    }
}

public class CaretGeometryTests
{
    static NativeMethods.RECT Rect(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

    [Fact]
    public void AnEmptyCaretMeansNoCaret() =>
        // The Mac's CursorLocator falls back to the mouse when the caret rect is empty; apps can own a 0×0 caret at 0,0.
        Assert.Null(CaretLocator.ToScreen(Rect(0, 0, 0, 0), new Point(500, 300), scale: 1));

    [Fact]
    public void APerMonitorAwareAppsCaretIsOffsetFromItsClientOrigin() =>
        Assert.Equal(new Rectangle(540, 320, 2, 24), CaretLocator.ToScreen(Rect(40, 20, 42, 44), new Point(500, 300), scale: 1));

    [Fact]
    public void ADpiUnawareAppsCaretIsScaledToPhysicalPixels() =>
        // At 150 % a DPI-unaware app reports its caret in 96-DPI units; MyVoice works in physical pixels.
        Assert.Equal(new Rectangle(560, 330, 2, 30), CaretLocator.ToScreen(Rect(40, 20, 41, 40), new Point(500, 300), scale: 1.5));
}
