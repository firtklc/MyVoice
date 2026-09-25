using System.Globalization;
using MyVoice.Windows.Core;

namespace MyVoice.Windows.Tests;

// Display cases follow MyVoiceTests/HotkeyTests.swift (Windows names instead of ⌘⇧ symbols); the rest is Windows-only.
public class HotkeyTests
{
    const HotkeyModifiers CtrlShift = HotkeyModifiers.Control | HotkeyModifiers.Shift;
    static readonly Func<Keys, string?> NoLabels = _ => null;

    // ---- display and settings text ----

    [Fact] public void DefaultIsCtrlShiftD() => Assert.Equal("Ctrl+Shift+D", Hotkey.Default.ToString());

    [Fact]
    public void ModifiersAreWrittenInWindowsOrder() =>
        Assert.Equal("Win+Ctrl+Alt+Shift+A", new Hotkey(HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Win | HotkeyModifiers.Control, Keys.A).ToString());

    [Theory]
    [InlineData(Keys.D1, "Ctrl+Shift+1")]
    [InlineData(Keys.D0, "Ctrl+Shift+0")]
    [InlineData(Keys.F5, "Ctrl+Shift+F5")]
    [InlineData(Keys.F24, "Ctrl+Shift+F24")]
    [InlineData(Keys.Space, "Ctrl+Shift+Space")]
    [InlineData(Keys.Enter, "Ctrl+Shift+Enter")]
    [InlineData(Keys.PageUp, "Ctrl+Shift+PageUp")]
    [InlineData(Keys.Back, "Ctrl+Shift+Backspace")]
    [InlineData(Keys.OemPeriod, "Ctrl+Shift+OemPeriod")]
    [InlineData(Keys.Oemplus, "Ctrl+Shift+OemPlus")]
    [InlineData(Keys.OemQuestion, "Ctrl+Shift+Oem2")]
    public void KeysHaveStableNames(Keys key, string text) => Assert.Equal(text, new Hotkey(CtrlShift, key).ToString());

    [Fact]
    public void DisplayShowsWhatAnOemKeyTypesOnTheCurrentLayout() =>
        Assert.Equal("Ctrl+Shift+.", new Hotkey(CtrlShift, Keys.OemPeriod).Display(k => k == Keys.OemPeriod ? "." : null));

    [Fact]
    public void DisplayFallsBackToTheStableNameWithoutALabel() =>
        Assert.Equal("Ctrl+Shift+OemPeriod", new Hotkey(CtrlShift, Keys.OemPeriod).Display(NoLabels));

    [Fact]
    public void DisplayKeepsLettersUppercaseWhateverTheLayoutSays() =>
        Assert.Equal("Ctrl+Shift+D", Hotkey.Default.Display(_ => "d"));

    [Fact]
    public void MenuHintNamesTheShortcut() => Assert.Equal("Ctrl+Shift+D to dictate", Hotkey.Default.MenuHint(NoLabels));

    [Fact]
    public void MenuHintFollowsANewShortcut() =>
        Assert.Equal("Win+Shift+Space to dictate", new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, Keys.Space).MenuHint(NoLabels));

    // ---- parsing settings.json ----

    [Fact]
    public void EverySupportedKeyRoundTrips()
    {
        foreach (var key in Hotkey.SupportedKeys)
        foreach (var mods in new[] { CtrlShift, HotkeyModifiers.Win, HotkeyModifiers.Win | HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift })
        {
            var hotkey = new Hotkey(mods, key);
            Assert.True(Hotkey.TryParse(hotkey.ToString(), out var parsed), hotkey.ToString());
            Assert.Equal(hotkey, parsed);
        }
    }

    [Theory]
    [InlineData("ctrl+shift+d")]
    [InlineData(" Ctrl + Shift + D ")]
    [InlineData("Control+Shift+D")]
    [InlineData("Shift+Ctrl+D")]
    public void ParsingIsForgiving(string text)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(Hotkey.Default, hotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Ctrl+Shift+")]
    [InlineData("Ctrl++D")]
    [InlineData("Ctrl+Shift+NoSuchKey")]
    [InlineData("Ctrl+Hyper+D")]
    [InlineData("Ctrl+Shift+68")]      // a number is not a key name (68 would be Keys.D)
    [InlineData("Ctrl+Shift+D,E")]     // Enum.Parse would accept a flags list
    [InlineData("Ctrl+Shift+VolumeUp")] // a real key, but not one MyVoice offers
    public void GarbageDoesNotParse(string? text) => Assert.False(Hotkey.TryParse(text, out _));

    [Fact]
    public void ParsingIgnoresTheTurkishDottedI()
    {
        // Under tr-TR, "i".ToUpper() is "İ": a culture-sensitive comparison would not find the key I.
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.True(Hotkey.TryParse("ctrl+shift+i", out var hotkey));
            Assert.Equal(new Hotkey(CtrlShift, Keys.I), hotkey);
            Assert.True(Hotkey.TryParse("win+shift+pageup", out hotkey));
            Assert.Equal(new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, Keys.PageUp), hotkey);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    // ---- which shortcuts are allowed ----

    [Theory]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.D)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.Space)]
    [InlineData(HotkeyModifiers.Win | HotkeyModifiers.Shift, Keys.D)]
    [InlineData(HotkeyModifiers.Control, Keys.F9)]
    [InlineData(HotkeyModifiers.Win, Keys.F9)]
    public void UsualShortcutsAreAllowed(HotkeyModifiers mods, Keys key) => Assert.Null(new Hotkey(mods, key).Problem);

    [Theory]
    [InlineData(HotkeyModifiers.None, Keys.D)]
    [InlineData(HotkeyModifiers.Shift, Keys.D)]
    [InlineData(HotkeyModifiers.Alt, Keys.D)]
    [InlineData(HotkeyModifiers.Alt | HotkeyModifiers.Shift, Keys.D)]
    public void ShortcutsWithoutCtrlOrWinAreRejected(HotkeyModifiers mods, Keys key) =>
        Assert.Contains("Ctrl or Win", new Hotkey(mods, key).Problem);

    [Theory]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt)]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift)]
    [InlineData(HotkeyModifiers.Win | HotkeyModifiers.Control | HotkeyModifiers.Alt)]
    public void CtrlAltIsRejectedBecauseItIsAltGr(HotkeyModifiers mods) =>
        Assert.Contains("AltGr", new Hotkey(mods, Keys.Q).Problem); // AltGr+Q types @ on the Turkish Q layout

    [Fact] public void F12IsRejected() => Assert.Contains("F12", new Hotkey(CtrlShift, Keys.F12).Problem);

    [Fact] public void EscIsRejectedBecauseItCancels() => Assert.Contains("Esc", new Hotkey(HotkeyModifiers.Control, Keys.Escape).Problem);

    [Theory]
    [InlineData(Keys.VolumeUp)]
    [InlineData(Keys.NumPad1)]
    [InlineData(Keys.ShiftKey)]
    [InlineData(Keys.LWin)]
    public void UnsupportedKeysAreRejected(Keys key) => Assert.NotNull(new Hotkey(CtrlShift, key).Problem);

    // ---- recording a new shortcut in Settings ----

    static CaptureStep Press(Keys keyData, bool win = false) => HotkeyCapture.KeyDown(keyData, win, k => k == Keys.OemPeriod ? "." : null);

    [Fact] public void HeldModifiersAreShownWhileWaitingForTheKey() =>
        Assert.Equal(new CaptureHeld("Ctrl+Shift+…"), Press(Keys.ShiftKey | Keys.Control | Keys.Shift));

    [Fact] public void AltAloneIsHeld() => Assert.Equal(new CaptureHeld("Alt+…"), Press(Keys.Menu | Keys.Alt));

    [Fact] public void WinAloneIsHeld() => Assert.Equal(new CaptureHeld("Win+…"), Press(Keys.LWin, win: true));

    [Fact]
    public void AValidShortcutIsChosen() =>
        Assert.Equal(new CaptureChosen(new Hotkey(CtrlShift, Keys.Space)), Press(Keys.Space | Keys.Control | Keys.Shift));

    [Fact]
    public void TheWinKeyComesFromItsOwnState() =>
        Assert.Equal(new CaptureChosen(new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, Keys.D)), Press(Keys.D | Keys.Shift, win: true));

    [Fact]
    public void ARejectedShortcutSaysWhy()
    {
        var step = Assert.IsType<CaptureRejected>(Press(Keys.Q | Keys.Control | Keys.Alt));
        Assert.Equal("Ctrl+Alt+Q", step.Text);
        Assert.Contains("AltGr", step.Reason);
    }

    [Fact]
    public void RejectedOemKeysShowTheirCharacter() =>
        Assert.Equal("Shift+.", Assert.IsType<CaptureRejected>(Press(Keys.OemPeriod | Keys.Shift)).Text);

    [Fact] public void EscAloneCancelsRecording() => Assert.Equal(new CaptureCancelled(), Press(Keys.Escape));

    [Fact] public void EscWithModifiersIsRejectedNotCancelled() => Assert.IsType<CaptureRejected>(Press(Keys.Escape | Keys.Control));

    [Fact] public void TabAloneIsRejectedNotAFocusMove() => Assert.IsType<CaptureRejected>(Press(Keys.Tab));
}
