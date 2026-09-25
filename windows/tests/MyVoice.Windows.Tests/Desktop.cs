using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

/// <summary>Helpers for tests that drive real windows. Tests using them are tagged Resource=SendsKeys:
/// they press keys system-wide, so run them only while nobody is typing.</summary>
static class Desktop
{
    // FindWindow, GetForegroundWindow and GetWindowThreadProcessId come from the app's NativeMethods.
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out NativeMethods.RECT rect);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr dest, int x, int y, int w, int h, IntPtr source, int sx, int sy, int rop);

    static string Describe(IntPtr window)
    {
        if (window == IntPtr.Zero) return "none";
        NativeMethods.GetWindowThreadProcessId(window, out var pid);
        string name;
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch (ArgumentException) { name = "?"; }
        return $"{name} ({NativeMethods.ClassName(window)})";
    }

    public static Rectangle WindowRect(IntPtr window)
    {
        GetWindowRect(window, out var r);
        return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
    }

    /// <summary>What is on screen in <paramref name="area"/> now, layered windows included (CAPTUREBLT).</summary>
    public static Bitmap Capture(Rectangle area)
    {
        var shot = new Bitmap(area.Width, area.Height);
        using var g = Graphics.FromImage(shot);
        var screen = GetDC(IntPtr.Zero);
        var target = g.GetHdc();
        BitBlt(target, 0, 0, area.Width, area.Height, screen, area.Left, area.Top, 0x00CC0020 | 0x40000000); // SRCCOPY | CAPTUREBLT
        g.ReleaseHdc(target);
        ReleaseDC(IntPtr.Zero, screen);
        return shot;
    }

    /// <summary>Runs <paramref name="body"/> on an STA thread with a WinForms message loop, like MyVoice's UI thread.</summary>
    public static void RunSta(Func<Task> body, int timeoutMs = 20000)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            var task = body();
            var clock = Stopwatch.StartNew();
            while (!task.IsCompleted && clock.ElapsedMilliseconds < timeoutMs) { Application.DoEvents(); Thread.Sleep(5); }
            error = !task.IsCompleted ? new TimeoutException("STA body timed out") : task.Exception?.InnerException;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
    }

    /// <summary>Focuses a window without injecting keys (joining the foreground thread's input, as in the spike).</summary>
    public static void Focus(IntPtr window)
    {
        var me = GetCurrentThreadId();
        var before = NativeMethods.GetForegroundWindow();
        var foreground = NativeMethods.GetWindowThreadProcessId(before, out _);
        var attached = AttachThreadInput(me, foreground, true);
        var set = SetForegroundWindow(window);
        AttachThreadInput(me, foreground, false);
        var clock = Stopwatch.StartNew();
        while (NativeMethods.GetForegroundWindow() != window && clock.ElapsedMilliseconds < 2000) Thread.Sleep(20);
        var after = NativeMethods.GetForegroundWindow();
        Assert.True(after == window, "could not focus the test window — refusing to send keys elsewhere " +
            $"(foreground before: {Describe(before)}, attached {attached}, SetForegroundWindow {set}, foreground after: {Describe(after)})");
    }

    /// <summary>tools/TargetWindow.ps1: a text box that mirrors its text to a file, standing in for "the app you're dictating into".</summary>
    public sealed class TargetWindow : IDisposable
    {
        readonly Process _process;
        public IntPtr Handle { get; }
        public string Title { get; } = $"MyVoice Test Target {Guid.NewGuid():N}";
        public string OutFile { get; } = Path.Combine(Path.GetTempPath(), $"myvoice-target-{Guid.NewGuid():N}.txt");

        public TargetWindow()
        {
            var script = FindUp(Path.Combine("tools", "TargetWindow.ps1"));
            _process = Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" -Out \"{OutFile}\" -Seconds 30 -Title \"{Title}\"") { UseShellExecute = false, CreateNoWindow = true })!;
            var clock = Stopwatch.StartNew();
            while ((Handle = NativeMethods.FindWindow(null, Title)) == IntPtr.Zero && clock.ElapsedMilliseconds < 15000) Thread.Sleep(100);
            Assert.True(Handle != IntPtr.Zero, "test target window did not appear");
        }

        /// <summary>The text box's content once it stops changing.</summary>
        public string Text(int waitMs = 1500)
        {
            Thread.Sleep(waitMs);
            return File.Exists(OutFile) ? File.ReadAllText(OutFile) : "";
        }

        public void Dispose()
        {
            try { _process.Kill(); } catch (InvalidOperationException) { }
            File.Delete(OutFile);
            File.Delete(OutFile + ".log");
        }
    }

    static string FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, relative))) return Path.Combine(dir.FullName, relative);
        throw new FileNotFoundException(relative);
    }
}
