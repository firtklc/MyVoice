using System.Runtime.InteropServices;

namespace MyVoice.Windows.Platform;

/// <summary>
/// Where the user is typing (the Mac's CursorLocator): the focused app's Win32 caret, or the mouse pointer when the
/// app draws its own (Chrome, VS Code and other Chromium/Electron apps expose no Win32 caret — seen in the spike).
/// </summary>
static class CaretLocator
{
    /// <summary>The foreground app's caret in screen pixels, or null when it has none.</summary>
    public static Rectangle? Caret()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return null;
        var info = new NativeMethods.GUITHREADINFO { Size = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        if (!NativeMethods.GetGUIThreadInfo(NativeMethods.GetWindowThreadProcessId(foreground, out _), ref info) || info.Caret == IntPtr.Zero)
            return null;
        var rect = info.CaretRect;
        var topLeft = new NativeMethods.POINT { X = rect.Left, Y = rect.Top };
        if (!NativeMethods.ClientToScreen(info.Caret, ref topLeft)) return null;
        return new Rectangle(topLeft.X, topLeft.Y, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top));
    }

    /// <summary>The caret, else a caret-sized spot just left of the mouse pointer so the overlay ends at the pointer
    /// instead of covering its arrow.</summary>
    public static Rectangle Locate() => Caret() ?? new Rectangle(Cursor.Position.X - 8, Cursor.Position.Y, 1, 20);
}

static class KeyLabels
{
    /// <summary>What a key types on the current keyboard layout ("." or "ç"), or null for keys that type nothing.</summary>
    public static string? ForCurrentLayout(Keys key)
    {
        var mapped = NativeMethods.MapVirtualKey((uint)key, NativeMethods.MAPVK_VK_TO_CHAR);
        var character = (char)(mapped & 0xFFFF); // the top bit marks dead keys (accents); the character is still right
        return mapped == 0 || char.IsControl(character) || char.IsWhiteSpace(character) ? null : character.ToString();
    }
}
