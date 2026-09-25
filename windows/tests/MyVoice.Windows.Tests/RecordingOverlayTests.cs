using System.Drawing;
using System.Runtime.InteropServices;
using MyVoice.Windows.Core;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

// Shows a small click-through window for about a second; presses no keys and moves no focus, so it is safe while
// Fırat works (--filter-trait "Resource=Win32"). The spike's lesson: anything that activates a window can eat the
// first Ctrl+V, so the overlay must never become the foreground window.
[Trait("Kind", "Integration"), Trait("Resource", "Win32")]
[Collection("Desktop")]
public class RecordingOverlayTests
{
    const int GWL_EXSTYLE = -20;

    static Rectangle CaretOnPrimary()
    {
        var area = Screen.PrimaryScreen!.WorkingArea;
        return new Rectangle(area.Left + area.Width / 2, area.Top + area.Height / 2, 2, 20);
    }

    [Fact]
    public void NeverTakesTheForeground()
    {
        var before = NativeMethods.GetForegroundWindow();
        if (before == IntPtr.Zero) Assert.Skip("no foreground window (locked screen?)");
        Desktop.RunSta(async () =>
        {
            using var overlay = new RecordingOverlay(CaretOnPrimary);
            overlay.Show(OverlayKind.Connecting);
            for (var i = 0; i < 10; i++) { overlay.Tick(0); await Task.Delay(30); Assert.Equal(before, NativeMethods.GetForegroundWindow()); }
            overlay.Show(OverlayKind.Recording);
            for (var i = 0; i < 10; i++) { overlay.Tick(i / 10f); await Task.Delay(30); Assert.Equal(before, NativeMethods.GetForegroundWindow()); }
            Assert.True(Desktop.IsWindowVisible(overlay.Handle));
            overlay.Hide();
            Assert.False(Desktop.IsWindowVisible(overlay.Handle));
        });
        Assert.Equal(before, NativeMethods.GetForegroundWindow());
    }

    [Fact]
    public void IsANonActivatingClickThroughTopmostToolWindow()
    {
        Desktop.RunSta(() =>
        {
            using var overlay = new RecordingOverlay(CaretOnPrimary);
            overlay.Show(OverlayKind.Recording);
            var style = Desktop.GetWindowLong(overlay.Handle, GWL_EXSTYLE);
            const int noActivate = 0x08000000, topmost = 0x8, transparent = 0x20, layered = 0x80000, toolWindow = 0x80;
            Assert.Equal(noActivate | topmost | transparent | layered | toolWindow, style & (noActivate | topmost | transparent | layered | toolWindow));
            var center = new Point(overlay.Bounds.Left + overlay.Bounds.Width / 2, overlay.Bounds.Top + overlay.Bounds.Height / 2);
            Assert.NotEqual(overlay.Handle, Desktop.WindowFromPoint(center)); // clicks fall through to the app underneath
            return Task.CompletedTask;
        });
    }

    [Fact]
    public void SitsWhereThePositionerSaysAndStaysPutWhenRecordingStarts()
    {
        Desktop.RunSta(() =>
        {
            var caret = CaretOnPrimary();
            var calls = 0;
            using var overlay = new RecordingOverlay(() => { calls++; return caret; });
            overlay.Show(OverlayKind.Connecting);
            var shown = Desktop.WindowRect(overlay.Handle);
            var expected = OverlayPositioner.Position(caret, overlay.Bounds.Size, Screen.FromRectangle(caret).WorkingArea, overlay.Scale);
            Assert.Equal(expected, shown.Location);
            Assert.Equal(overlay.Bounds.Size, shown.Size);

            caret.Offset(300, 100); // the caret or mouse moves while the mic connects
            overlay.Show(OverlayKind.Recording);
            Assert.Equal(shown, Desktop.WindowRect(overlay.Handle));
            Assert.Equal(1, calls);
            return Task.CompletedTask;
        });
    }

    /// <summary>Height in pixels of the white middle bar as it is on screen now (not as drawn in memory).</summary>
    static int MiddleBarOnScreen(RecordingOverlay overlay)
    {
        using var shot = Desktop.Capture(overlay.Bounds);
        var x = shot.Width / 2;
        return Enumerable.Range(0, shot.Height).Count(y => shot.GetPixel(x, y) is { R: > 200, G: > 200, B: > 200 });
    }

    [Fact]
    public void BarsFollowTheLevelOnScreen()
    {
        // UAT B: the blue pill's bars stayed at their minimum while Fırat spoke.
        Desktop.RunSta(async () =>
        {
            using var overlay = new RecordingOverlay(CaretOnPrimary);
            overlay.Show(OverlayKind.Recording);
            for (var i = 0; i < 5; i++) { overlay.Tick(0); await Task.Delay(50); }
            var quiet = MiddleBarOnScreen(overlay);
            for (var i = 0; i < 5; i++) { overlay.Tick(0.3f); await Task.Delay(50); } // speech: about −10 dBFS
            var loud = MiddleBarOnScreen(overlay);
            overlay.Hide();
            Assert.True(loud > quiet * 2, $"middle bar {quiet} px when quiet, {loud} px when loud");
        });
    }

    [Fact]
    public void GrowsWithTheMonitorDpi()
    {
        Desktop.RunSta(() =>
        {
            using var overlay = new RecordingOverlay(CaretOnPrimary);
            overlay.Show(OverlayKind.Recording);
            Assert.InRange(overlay.Scale, 1, 4);
            Assert.Equal((int)Math.Round(36 * overlay.Scale), overlay.Bounds.Height); // the Mac pill's height at 96 DPI
            return Task.CompletedTask;
        });
    }
}

// These move the focus and press keys: only while nobody is typing (--filter-trait "Resource=SendsKeys").
[Trait("Kind", "Integration"), Trait("Resource", "SendsKeys")]
[Collection("Desktop")]
public class CaretAndOverlayPasteTests
{
    [Fact]
    public void FindsTheCaretOfAFocusedTextBox()
    {
        using var target = new Desktop.TargetWindow();
        Desktop.Focus(target.Handle);
        Thread.Sleep(300); // let the text box create its caret
        var caret = CaretLocator.Caret();
        Assert.NotNull(caret);
        Assert.True(Desktop.WindowRect(target.Handle).Contains(caret.Value.Location),
            $"caret {caret} is outside the target window");
    }

    [Fact]
    public void FirstPasteLandsWhileTheOverlayIsShowing()
    {
        using var target = new Desktop.TargetWindow();
        Desktop.Focus(target.Handle);
        PasteOutcome? outcome = null;
        Desktop.RunSta(async () =>
        {
            using var overlay = new RecordingOverlay(CaretLocator.Locate);
            overlay.Show(OverlayKind.Connecting);
            await Task.Delay(200);
            overlay.Show(OverlayKind.Recording);
            for (var i = 0; i < 5; i++) { overlay.Tick(0.3f); await Task.Delay(50); }
            outcome = await new Paster().PasteAsync("overlay stays out of the way", onlyInto: target.Handle);
            overlay.Hide();
        });
        Assert.True(outcome!.Pasted, outcome.Reason);
        Assert.Equal("overlay stays out of the way", target.Text());
    }
}
