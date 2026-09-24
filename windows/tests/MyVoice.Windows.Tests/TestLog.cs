using System.Runtime.CompilerServices;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

static class TestLog
{
    /// <summary>Tests log to a temp file, never to the user's ~/.myvoice/logs/myvoice.log
    /// (the Recorder tests used to write "mic stopped by itself" warnings there).</summary>
    [ModuleInitializer]
    internal static void RedirectLog() => Log.Path = Path.Combine(Path.GetTempPath(), "myvoice-tests.log");
}
