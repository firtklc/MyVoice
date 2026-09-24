using System.Diagnostics;

namespace MyVoice.Windows.Platform;

sealed record PasteOutcome(bool Pasted, string Target, string? Reason = null);

/// <summary>
/// Clipboard + synthetic Ctrl+V into whatever window has focus (the Mac's Paster.swift). Never activates a
/// window: in the spike, stealing focus made the target swallow the first Ctrl+V. Call on the UI (STA) thread.
/// </summary>
sealed class Paster
{
    static readonly string[] TaskbarClasses = ["Shell_TrayWnd", "Shell_SecondaryTrayWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"];
    static readonly Keys[] Modifiers = [Keys.ControlKey, Keys.ShiftKey, Keys.Menu, Keys.LWin, Keys.RWin];

    /// <param name="onlyInto">Tests and --simulate: paste only if this window still has focus; otherwise touch
    /// neither the keyboard nor the clipboard (a test must never type into whatever the user is doing).</param>
    public async Task<PasteOutcome> PasteAsync(string text, IntPtr? onlyInto = null)
    {
        // The stop hotkey's Ctrl+Shift may still be held; Ctrl+Shift+V would paste as something else.
        var waited = Stopwatch.StartNew();
        while (Modifiers.Any(IsDown) && waited.ElapsedMilliseconds < 1000) await Task.Delay(10);

        var foreground = NativeMethods.GetForegroundWindow();
        var target = Describe(foreground, out var ours, out var taskbar);
        if (onlyInto is { } expected && foreground != expected) return new(false, target, "focus moved away from the expected window — nothing typed");
        Clipboard.SetDataObject(text, copy: true, retryTimes: 10, retryDelay: 20);

        if (foreground == IntPtr.Zero) return new(false, target, "no window has focus");
        if (ours) return new(false, target, "MyVoice's own window has focus");
        if (taskbar) return new(false, target, "the taskbar has focus");

        await Task.Delay(50); // let the clipboard settle before the target reads it
        var sent = NativeMethods.Chord(Keys.ControlKey, Keys.V);
        return sent == 4 ? new(true, target) : new(false, target, $"only {sent}/4 key events were sent");
    }

    static bool IsDown(Keys key) => NativeMethods.GetAsyncKeyState((int)key) < 0;

    static string Describe(IntPtr window, out bool ours, out bool taskbar)
    {
        ours = taskbar = false;
        if (window == IntPtr.Zero) return "none";
        NativeMethods.GetWindowThreadProcessId(window, out var pid);
        var className = NativeMethods.ClassName(window);
        ours = pid == Environment.ProcessId;
        taskbar = TaskbarClasses.Contains(className);
        string process;
        try { process = Process.GetProcessById((int)pid).ProcessName; }
        catch (Exception) { process = $"pid {pid}"; } // diagnostics only: must never stop the paste
        return $"{process} ({className})";
    }
}
