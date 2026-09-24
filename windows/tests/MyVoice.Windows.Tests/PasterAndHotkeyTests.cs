using System.Diagnostics;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

// These press keys system-wide: run only while nobody is typing (dotnet test --filter-trait "Resource=SendsKeys").
[Trait("Kind", "Integration"), Trait("Resource", "SendsKeys")]
[Collection("Desktop")] // never in parallel with each other
public class PasterTests
{
    [Fact]
    public void PastesIntoTheFocusedWindow()
    {
        using var target = new Desktop.TargetWindow();
        Desktop.Focus(target.Handle);
        const string text = "Hey Claude, Fırat here: ğüşiöç İ";
        PasteOutcome? outcome = null;
        Desktop.RunSta(async () => outcome = await new Paster().PasteAsync(text, onlyInto: target.Handle));
        Assert.True(outcome!.Pasted, outcome.Reason);
        Assert.Contains("powershell", outcome.Target);
        Assert.Equal(text, target.Text());
    }

    [Fact]
    public void WaitsUntilHeldModifiersAreReleased()
    {
        // The user releases Ctrl+Shift of the stop hotkey a moment after MyVoice is ready to paste.
        using var target = new Desktop.TargetWindow();
        Desktop.Focus(target.Handle);
        PasteOutcome? outcome = null;
        long pastedAfterMs = 0;
        Desktop.RunSta(async () =>
        {
            NativeMethods.SendInput(1, [NativeMethods.Key(Keys.ShiftKey, up: false)], System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
            var clock = Stopwatch.StartNew();
            try
            {
                var paste = new Paster().PasteAsync("held", onlyInto: target.Handle);
                await Task.Delay(300);
                NativeMethods.SendInput(1, [NativeMethods.Key(Keys.ShiftKey, up: true)], System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
                outcome = await paste;
                pastedAfterMs = clock.ElapsedMilliseconds;
            }
            finally
            {
                NativeMethods.SendInput(1, [NativeMethods.Key(Keys.ShiftKey, up: true)], System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>());
            }
        });
        Assert.True(outcome!.Pasted, outcome.Reason);
        Assert.InRange(pastedAfterMs, 300, 1200);
        Assert.Equal("held", target.Text());
    }

    [Fact]
    public void DoesNotPasteIntoItsOwnWindowButLeavesTheTextOnTheClipboard()
    {
        PasteOutcome? outcome = null;
        string? clipboard = null;
        Desktop.RunSta(async () =>
        {
            using var form = new Form { Text = "MyVoice own window", ShowInTaskbar = false };
            form.Show();
            Desktop.Focus(form.Handle);
            outcome = await new Paster().PasteAsync("kept for you", onlyInto: form.Handle);
            clipboard = Clipboard.GetText();
        });
        Assert.False(outcome!.Pasted);
        Assert.Equal("MyVoice's own window has focus", outcome.Reason);
        Assert.Equal("kept for you", clipboard);
    }
}

[Trait("Kind", "Integration"), Trait("Resource", "SendsKeys")]
[Collection("Desktop")]
public class GlobalHotkeyTests
{
    // Ctrl+Shift+F9: unlikely to be taken, and no Alt (an Alt tap can open menus in the focused app).
    const HotkeyModifiers Mods = HotkeyModifiers.Control | HotkeyModifiers.Shift;
    const Keys Key = Keys.F9;

    [Fact]
    public void FiresWhenPressedAnywhere()
    {
        int? fired = null;
        Desktop.RunSta(async () =>
        {
            using var hotkey = new GlobalHotkey();
            if (!hotkey.Register(7, Mods, Key, out var error)) Assert.Skip($"Ctrl+Shift+F9 is taken on this machine (error {error})");
            var pressed = new TaskCompletionSource<int>();
            hotkey.Pressed += id => pressed.TrySetResult(id);
            NativeMethods.Chord(Keys.ControlKey, Keys.ShiftKey, Key); // swallowed by the system because it's registered
            fired = await pressed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        });
        Assert.Equal(7, fired);
    }

    [Fact]
    public void ReportsAComboHeldByAnotherOwnerAndFreesItOnUnregister()
    {
        Desktop.RunSta(() =>
        {
            using var first = new GlobalHotkey();
            using var second = new GlobalHotkey();
            if (!first.Register(1, Mods, Key, out var error)) Assert.Skip($"Ctrl+Shift+F9 is taken on this machine (error {error})");
            Assert.False(second.Register(1, Mods, Key, out error));
            Assert.Equal(NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED, error);
            first.Unregister(1);
            Assert.True(second.Register(1, Mods, Key, out _));
            return Task.CompletedTask;
        });
    }
}

[CollectionDefinition("Desktop", DisableParallelization = true)]
public class DesktopCollection;
