using System.Runtime.InteropServices;
using MyVoice.Windows.Core;

namespace MyVoice.Windows.Platform;

/// <summary>
/// System-wide hotkeys via RegisterHotKey on a message-only window (the Mac uses Carbon RegisterEventHotKey).
/// A registered combination is taken from every other app while registered. Create and use on the UI thread.
/// </summary>
sealed class GlobalHotkey : NativeWindow, IDisposable
{
    const uint NoRepeat = 0x4000; // holding the keys doesn't fire again
    static readonly IntPtr MessageOnlyParent = new(-3);
    readonly HashSet<int> _registered = [];

    public event Action<int>? Pressed;

    public GlobalHotkey() => CreateHandle(new CreateParams { Parent = MessageOnlyParent });

    /// <summary>False when another app holds the combination (<paramref name="error"/> 1409) or it can't be registered.</summary>
    public bool Register(int id, HotkeyModifiers modifiers, Keys key, out int error)
    {
        if (NativeMethods.RegisterHotKey(Handle, id, (uint)modifiers | NoRepeat, (uint)key))
        {
            _registered.Add(id);
            error = 0;
            return true;
        }
        error = Marshal.GetLastWin32Error();
        return false;
    }

    public void Unregister(int id)
    {
        if (_registered.Remove(id)) NativeMethods.UnregisterHotKey(Handle, id);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY) Pressed?.Invoke((int)m.WParam);
        else base.WndProc(ref m);
    }

    public void Dispose()
    {
        foreach (var id in _registered.ToArray()) Unregister(id);
        DestroyHandle();
    }
}
