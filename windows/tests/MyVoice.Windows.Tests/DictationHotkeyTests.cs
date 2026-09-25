using MyVoice.Windows.Core;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

// Registers system-wide hotkeys briefly but presses nothing: safe while Fırat works (--filter-trait "Resource=Win32").
// F13–F15 are on no physical keyboard, so the test registrations can't swallow a real keystroke.
[Trait("Kind", "Integration"), Trait("Resource", "Win32")]
[Collection("Desktop")]
public class DictationHotkeyTests
{
    const int Id = 1;
    static readonly Hotkey F13 = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.F13);
    static readonly Hotkey F14 = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.F14);

    /// <summary>Whether some other window could take <paramref name="hotkey"/> right now (it then gives it back).</summary>
    static bool IsFree(Hotkey hotkey)
    {
        using var probe = new GlobalHotkey();
        return probe.Register(9, hotkey.Modifiers, hotkey.Key, out _);
    }

    static void OnUiThread(Action<GlobalHotkey> body) => Desktop.RunSta(() =>
    {
        using var hotkeys = new GlobalHotkey();
        if (!IsFree(F13) || !IsFree(F14)) Assert.Skip("Ctrl+Shift+F13/F14 are taken on this machine");
        body(hotkeys);
        return Task.CompletedTask;
    });

    [Fact]
    public void StartTakesTheShortcut() => OnUiThread(hotkeys =>
    {
        var dictation = new DictationHotkey(hotkeys, Id);
        Assert.True(dictation.Start(F13, out _));
        Assert.True(dictation.Registered);
        Assert.False(IsFree(F13));
    });

    [Fact]
    public void SuspendFreesItForTheSettingsFieldAndResumeTakesItBack() => OnUiThread(hotkeys =>
    {
        var dictation = new DictationHotkey(hotkeys, Id);
        dictation.Start(F13, out _);
        dictation.Suspend();
        Assert.False(dictation.Registered);
        Assert.True(IsFree(F13)); // pressing it now reaches the Settings field instead of starting a dictation
        Assert.True(dictation.Resume(out _));
        Assert.False(IsFree(F13));
    });

    [Fact]
    public void ChangingTakesTheNewShortcutAndFreesTheOld() => OnUiThread(hotkeys =>
    {
        var dictation = new DictationHotkey(hotkeys, Id);
        dictation.Start(F13, out _);
        Assert.True(dictation.TryChange(F14, out _));
        Assert.Equal(F14, dictation.Current);
        Assert.False(IsFree(F14));
        Assert.True(IsFree(F13));
    });

    [Fact]
    public void ChangingToATakenShortcutKeepsTheOldOne() => OnUiThread(hotkeys =>
    {
        using var otherApp = new GlobalHotkey();
        Assert.True(otherApp.Register(1, F14.Modifiers, F14.Key, out _));
        var dictation = new DictationHotkey(hotkeys, Id);
        dictation.Start(F13, out _);
        Assert.False(dictation.TryChange(F14, out var error));
        Assert.Equal(NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED, error);
        Assert.Equal(F13, dictation.Current);
        Assert.True(dictation.Registered);
        Assert.False(IsFree(F13));
    });

    [Fact]
    public void AFailedChangeWhileSuspendedStaysSuspendedUntilResumed() => OnUiThread(hotkeys =>
    {
        // The Settings field keeps listening after a conflict, so the old shortcut must stay free until it closes.
        using var otherApp = new GlobalHotkey();
        otherApp.Register(1, F14.Modifiers, F14.Key, out _);
        var dictation = new DictationHotkey(hotkeys, Id);
        dictation.Start(F13, out _);
        dictation.Suspend();
        Assert.False(dictation.TryChange(F14, out _));
        Assert.False(dictation.Registered);
        Assert.True(dictation.Resume(out _));
        Assert.Equal(F13, dictation.Current);
    });

    [Fact]
    public void AShortcutTakenAtStartupIsKeptAndRetriedOnResume() => OnUiThread(hotkeys =>
    {
        var otherApp = new GlobalHotkey();
        otherApp.Register(1, F13.Modifiers, F13.Key, out _);
        var dictation = new DictationHotkey(hotkeys, Id);
        Assert.False(dictation.Start(F13, out var error));
        Assert.Equal(NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED, error);
        Assert.Equal(F13, dictation.Current); // Settings still shows it, so the user knows what to change
        otherApp.Dispose();                   // the other app quits
        Assert.True(dictation.Resume(out _));
    });
}
