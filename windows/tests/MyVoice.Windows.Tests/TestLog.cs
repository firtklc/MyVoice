using System.Runtime.CompilerServices;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

static class TestLog
{
    /// <summary>Tests log to a temp file, never to the user's ~/.myvoice/logs/myvoice.log
    /// (the Recorder tests used to write "mic stopped by itself" warnings there).</summary>
    [ModuleInitializer]
    internal static void RedirectLog() => Log.Path = Path.Combine(Path.GetTempPath(), "myvoice-tests.log");

    /// <summary>Screen coordinates like MyVoice's (per-monitor DPI aware, set by ApplicationConfiguration.Initialize).
    /// Without this the test process is DPI-unaware: Windows scales its coordinates, the overlay lands elsewhere on a
    /// 150 % display, and screen captures miss it.</summary>
    [ModuleInitializer]
    internal static void DpiAwareLikeTheApp() => Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
}
