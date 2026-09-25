using System.Runtime.InteropServices;
using System.Text;

namespace MyVoice.Windows.Platform;

static class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public InputUnion U;
    }

    // The union must be as large as MOUSEINPUT, or SendInput rejects the size (40 bytes per INPUT on x64).
    [StructLayout(LayoutKind.Explicit)]
    public struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public IntPtr ExtraInfo;
    }

    public static INPUT Key(Keys key, bool up) => new()
    {
        Type = INPUT_KEYBOARD,
        U = new InputUnion { Keyboard = new KEYBDINPUT { Vk = (ushort)key, Flags = up ? KEYEVENTF_KEYUP : 0 } },
    };

    /// <summary>Presses and releases the keys in order (e.g. Ctrl, V → Ctrl↓ V↓ V↑ Ctrl↑). Returns how many events were sent.</summary>
    public static uint Chord(params Keys[] keys)
    {
        var inputs = keys.Select(k => Key(k, up: false)).Concat(keys.Reverse().Select(k => Key(k, up: true))).ToArray();
        return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    public static string ClassName(IntPtr hWnd)
    {
        var name = new StringBuilder(256);
        GetClassName(hWnd, name, name.Capacity);
        return name.ToString();
    }

    // ---- caret (CaretLocator) ----

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int Size;
        public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public RECT CaretRect;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetGUIThreadInfo(uint threadId, ref GUITHREADINFO info);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);

    // ---- keys (Settings shortcut field) ----

    public const uint MAPVK_VK_TO_CHAR = 2;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint MapVirtualKey(uint code, uint mapType);

    /// <summary>Key state as of the message being processed (unlike GetAsyncKeyState, which is "now").</summary>
    [DllImport("user32.dll")]
    public static extern short GetKeyState(int vKey);

    // ---- overlay window (RecordingOverlay) ----

    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOPMOST = 0x8, WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    public const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    public const int SW_HIDE = 0;
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int command);

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE { public int Width, Height; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr screenDc, ref POINT destination, ref SIZE size,
        IntPtr sourceDc, ref POINT sourcePoint, int colorKey, ref BLENDFUNCTION blend, int flags);

    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr gdiObject);

    /// <summary>Puts a 32-bit ARGB bitmap on a WS_EX_LAYERED window with per-pixel alpha (anti-aliased rounded corners),
    /// moving and sizing the window in the same call.</summary>
    public static void UpdateLayeredWindow(IntPtr hWnd, Bitmap bitmap, Point position)
    {
        var screenDc = GetDC(IntPtr.Zero);
        var memoryDc = CreateCompatibleDC(screenDc);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0)); // premultiplied 32-bit DIB, as UpdateLayeredWindow needs
        var previous = SelectObject(memoryDc, hBitmap);
        try
        {
            var destination = new POINT { X = position.X, Y = position.Y };
            var size = new SIZE { Width = bitmap.Width, Height = bitmap.Height };
            var source = new POINT();
            var blend = new BLENDFUNCTION { BlendOp = 0 /* AC_SRC_OVER */, SourceConstantAlpha = 255, AlphaFormat = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(hWnd, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, 2 /* ULW_ALPHA */);
        }
        finally
        {
            SelectObject(memoryDc, previous);
            DeleteObject(hBitmap);
            DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("user32.dll")]
    static extern IntPtr MonitorFromPoint(POINT point, uint flags);

    [DllImport("shcore.dll")]
    static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>Effective DPI of the monitor nearest <paramref name="point"/> (96 = 100 %).</summary>
    public static int DpiAt(Point point)
    {
        var monitor = MonitorFromPoint(new POINT { X = point.X, Y = point.Y }, 2 /* MONITOR_DEFAULTTONEAREST */);
        return GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out var dpi, out _) == 0 ? (int)dpi : 96;
    }
}
