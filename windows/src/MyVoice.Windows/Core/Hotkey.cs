using System.Diagnostics.CodeAnalysis;

namespace MyVoice.Windows.Core;

/// <summary>RegisterHotKey's fsModifiers bits.</summary>
[Flags]
public enum HotkeyModifiers : uint { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

/// <summary>
/// The dictation shortcut (the Mac's KeyboardShortcuts + HotkeyDisplayHelper): modifiers plus one key.
/// <see cref="ToString"/> is the layout-independent text kept in settings.json ("Ctrl+Shift+D");
/// <see cref="Display"/> is what people see, with punctuation keys shown as the character they type.
/// </summary>
public sealed record Hotkey(HotkeyModifiers Modifiers, Keys Key)
{
    public static readonly Hotkey Default = new(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.D);

    // Windows order (PowerToys, Settings): Win, Ctrl, Alt, Shift.
    static readonly (HotkeyModifiers Flag, string Name)[] ModifierNames =
        [(HotkeyModifiers.Win, "Win"), (HotkeyModifiers.Control, "Ctrl"), (HotkeyModifiers.Alt, "Alt"), (HotkeyModifiers.Shift, "Shift")];

    /// <summary>Punctuation keys: their character depends on the keyboard layout (VK_OEM_*).</summary>
    static readonly Dictionary<Keys, string> OemNames = new()
    {
        [Keys.Oem1] = "Oem1", [Keys.Oemplus] = "OemPlus", [Keys.Oemcomma] = "OemComma", [Keys.OemMinus] = "OemMinus",
        [Keys.OemPeriod] = "OemPeriod", [Keys.Oem2] = "Oem2", [Keys.Oem3] = "Oem3", [Keys.Oem4] = "Oem4",
        [Keys.Oem5] = "Oem5", [Keys.Oem6] = "Oem6", [Keys.Oem7] = "Oem7", [Keys.Oem8] = "Oem8", [Keys.Oem102] = "Oem102",
    };

    /// <summary>The keys a shortcut may use, with their settings.json names. Several Keys members share a value
    /// (Enter/Return, PageUp/Prior), so names come from this table, never from Keys.ToString().</summary>
    static readonly Dictionary<Keys, string> Names = BuildNames();

    static readonly Dictionary<string, Keys> ByName = Names.ToDictionary(n => n.Value, n => n.Key, StringComparer.OrdinalIgnoreCase);

    static Dictionary<Keys, string> BuildNames()
    {
        var names = new Dictionary<Keys, string>();
        for (var k = Keys.A; k <= Keys.Z; k++) names[k] = ((char)k).ToString();
        for (var i = 0; i <= 9; i++) names[Keys.D0 + i] = ((char)('0' + i)).ToString();
        for (var i = 1; i <= 24; i++) names[Keys.F1 + (i - 1)] = $"F{i}";
        (Keys, string)[] named =
        [
            (Keys.Space, "Space"), (Keys.Enter, "Enter"), (Keys.Tab, "Tab"), (Keys.Back, "Backspace"),
            (Keys.Insert, "Insert"), (Keys.Delete, "Delete"), (Keys.Home, "Home"), (Keys.End, "End"),
            (Keys.PageUp, "PageUp"), (Keys.PageDown, "PageDown"), (Keys.Up, "Up"), (Keys.Down, "Down"),
            (Keys.Left, "Left"), (Keys.Right, "Right"), (Keys.Pause, "Pause"),
        ];
        foreach (var (key, name) in named) names[key] = name;
        foreach (var (key, name) in OemNames) names[key] = name;
        return names;
    }

    public static IEnumerable<Keys> SupportedKeys => Names.Keys;

    public override string ToString() => Join(Modifiers, KeyName());

    /// <summary>For people: punctuation keys show what they type on the current layout (<paramref name="keyLabel"/>).</summary>
    public string Display(Func<Keys, string?> keyLabel) =>
        Join(Modifiers, OemNames.ContainsKey(Key) && keyLabel(Key) is { Length: > 0 } label ? label : KeyName());

    string KeyName() => Names.TryGetValue(Key, out var name) ? name : Key.ToString();

    public string MenuHint(Func<Keys, string?> keyLabel) => $"{Display(keyLabel)} to dictate";

    /// <summary>Why this can't be the dictation shortcut, or null when it can.</summary>
    public string? Problem =>
        Key == Keys.Escape ? "Esc cancels a dictation — pick another key"
        : !Names.ContainsKey(Key) ? "Pick a letter, digit, F-key or punctuation key together with Ctrl or Win"
        : Key == Keys.F12 ? "F12 is reserved by Windows for debuggers — pick another key"
        : !Modifiers.HasFlag(HotkeyModifiers.Control) && !Modifiers.HasFlag(HotkeyModifiers.Win)
            ? "Use Ctrl or Win in the shortcut, so it can't type text or open menus"
        : Modifiers.HasFlag(HotkeyModifiers.Control | HotkeyModifiers.Alt)
            ? "Ctrl+Alt is AltGr on Turkish and other keyboards (it types @, € …) — use Ctrl+Shift or Win instead"
        : null;

    /// <summary>Reads settings.json text such as "Ctrl+Shift+D" (any order and case). Allowed-ness is <see cref="Problem"/>'s job.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+').Select(p => p.Trim()).ToArray();
        if (parts.Any(p => p.Length == 0) || !ByName.TryGetValue(parts[^1], out var key)) return false;
        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            if (part.Equals("Control", StringComparison.OrdinalIgnoreCase)) { modifiers |= HotkeyModifiers.Control; continue; }
            var match = ModifierNames.FirstOrDefault(m => m.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (match.Name is null) return false;
            modifiers |= match.Flag;
        }
        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    internal static string Join(HotkeyModifiers modifiers, string key) =>
        string.Join("+", ModifierNames.Where(m => modifiers.HasFlag(m.Flag)).Select(m => m.Name).Append(key));
}

public abstract record CaptureStep;
/// <summary>Only modifiers so far: show them ("Ctrl+Shift+…") and keep listening.</summary>
public sealed record CaptureHeld(string Text) : CaptureStep;
public sealed record CaptureRejected(string Text, string Reason) : CaptureStep;
public sealed record CaptureChosen(Hotkey Hotkey) : CaptureStep;
/// <summary>Esc on its own: stop recording and keep the current shortcut.</summary>
public sealed record CaptureCancelled : CaptureStep;

/// <summary>Turns key presses in the Settings shortcut field into a new <see cref="Hotkey"/>.</summary>
public static class HotkeyCapture
{
    static readonly HashSet<Keys> ModifierKeys =
        [Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
         Keys.Menu, Keys.LMenu, Keys.RMenu, Keys.LWin, Keys.RWin];

    /// <param name="keyData">WinForms key data (key code plus Control/Shift/Alt bits).</param>
    /// <param name="win">Whether a Win key is down — WinForms key data has no bit for it.</param>
    public static CaptureStep KeyDown(Keys keyData, bool win, Func<Keys, string?> keyLabel)
    {
        var key = keyData & Keys.KeyCode;
        var modifiers = (win ? HotkeyModifiers.Win : 0)
            | (keyData.HasFlag(Keys.Control) ? HotkeyModifiers.Control : 0)
            | (keyData.HasFlag(Keys.Alt) ? HotkeyModifiers.Alt : 0)
            | (keyData.HasFlag(Keys.Shift) ? HotkeyModifiers.Shift : 0);
        if (ModifierKeys.Contains(key)) return new CaptureHeld(Hotkey.Join(modifiers, "…"));
        if (key == Keys.Escape && modifiers == HotkeyModifiers.None) return new CaptureCancelled();
        var hotkey = new Hotkey(modifiers, key);
        return hotkey.Problem is { } reason ? new CaptureRejected(hotkey.Display(keyLabel), reason) : new CaptureChosen(hotkey);
    }
}
