using System.Diagnostics;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

/// <summary>
/// The whole app: MyVoice.exe --simulate plays a WAV through the real recorder (after a simulated cold-headset
/// stall), transcribes it on the GPU, applies ~/.myvoice/dictionary.json and pastes into the focused window.
/// </summary>
[Trait("Kind", "Integration"), Trait("Resource", "SendsKeys"), Trait("Resource", "GPU")]
[Collection("Desktop")]
public class EndToEndTests
{
    [Fact]
    public void SimulatedDictationPastesTheCorrectedTextOnce()
    {
        if (!File.Exists(AppPaths.Model)) Assert.Skip($"no model at {AppPaths.Model}");
        if (!File.Exists(AppPaths.Dictionary) || !File.ReadAllText(AppPaths.Dictionary).Contains("\"Claude\"")) Assert.Skip("dictionary.json has no cloud → Claude entry");

        using var target = new Desktop.TargetWindow();
        Desktop.Focus(target.Handle);
        var wav = Path.Combine(AppContext.BaseDirectory, "Fixtures", "short.wav");
        // MYVOICE_EXE=%LOCALAPPDATA%\Programs\MyVoice\MyVoice.exe checks the installed Release build instead.
        var exe = Environment.GetEnvironmentVariable("MYVOICE_EXE") is { Length: > 0 } installed ? installed : Path.Combine(AppContext.BaseDirectory, "MyVoice.exe");
        using var app = Process.Start(new ProcessStartInfo(exe,
            $"--simulate \"{wav}\" --target \"{target.Title}\" --stall 2") { UseShellExecute = false })!;
        Assert.True(app.WaitForExit(TimeSpan.FromSeconds(60)), "MyVoice --simulate did not finish within 60 s");
        Assert.Equal(0, app.ExitCode);

        var text = target.Text();
        Assert.Contains("Hey Claude,", text);                                    // "cloud" corrected by the dictionary
        Assert.Contains("dictation app", text, StringComparison.OrdinalIgnoreCase);
        Assert.Single(text.Split("Hey Claude"), part => part.Length > 0);          // pasted exactly once
        Assert.DoesNotContain('\n', text);
    }
}
