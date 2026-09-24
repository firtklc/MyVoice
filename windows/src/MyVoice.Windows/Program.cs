using MyVoice.Windows.Platform;

namespace MyVoice.Windows;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        var options = Options.Parse(args);
        if (options.Error is not null)
        {
            Log.Error(options.Error);
            return 4;
        }

        // One MyVoice at a time; a --simulate run may happen alongside the real one.
        using var instance = new Mutex(true, options.SimulateWav is null ? @"Local\MyVoice.Windows" : @"Local\MyVoice.Windows.Simulate", out var first);
        if (!first)
        {
            if (options.SimulateWav is null)
                MessageBox.Show("MyVoice is already running — look for its icon in the notification area.", "MyVoice");
            return 1;
        }

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("unhandled exception on the UI thread", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("unhandled exception", e.ExceptionObject as Exception);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        using var app = new TrayApp(options);
        Application.Run(app);
        return Environment.ExitCode;
    }
}
