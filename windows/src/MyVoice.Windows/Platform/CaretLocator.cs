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
        var origin = new NativeMethods.POINT();
        if (!NativeMethods.ClientToScreen(info.Caret, ref origin)) return null; // physical: MyVoice is per-monitor aware
        var windowDpi = NativeMethods.GetDpiForWindow(info.Caret);
        var scale = windowDpi == 0 ? 1 : NativeMethods.DpiAt(new Point(origin.X, origin.Y)) / (double)windowDpi;
        return ToScreen(info.CaretRect, new Point(origin.X, origin.Y), scale);
    }

    /// <summary>The caret rect (in the caret window's own client units) in physical screen pixels, or null when it is
    /// empty (the Mac falls back to the mouse then too). <paramref name="scale"/> is the monitor DPI over the window's
    /// DPI: 1 for per-monitor-aware apps, 1.5 for a DPI-unaware app on a 150 % display, whose units are 96-DPI pixels.</summary>
    internal static Rectangle? ToScreen(NativeMethods.RECT caret, Point clientOrigin, double scale)
    {
        if (caret.Right <= caret.Left && caret.Bottom <= caret.Top) return null;
        int Px(int units) => (int)Math.Round(units * scale);
        return new Rectangle(clientOrigin.X + Px(caret.Left), clientOrigin.Y + Px(caret.Top),
            Math.Max(1, Px(caret.Right - caret.Left)), Math.Max(1, Px(caret.Bottom - caret.Top)));
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
