using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

/// <summary>Loads the real model on the GPU once for all tests in this class (~1.5 s; ~5 s on a machine's very first run).</summary>
public sealed class WhisperFixture : IAsyncLifetime
{
    internal WhisperEngine? Engine { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (File.Exists(AppPaths.Model)) Engine = await Task.Run(() => WhisperEngine.Load(AppPaths.Model));
    }

    public async ValueTask DisposeAsync()
    {
        if (Engine is not null) await Engine.DisposeAsync();
    }
}

[Trait("Kind", "Integration"), Trait("Resource", "GPU")]
public class WhisperEngineTests(WhisperFixture fixture) : IClassFixture<WhisperFixture>
{
    WhisperEngine Engine => fixture.Engine ?? throw Skip();

    static Exception Skip()
    {
        Assert.Skip($"no model at {AppPaths.Model}");
        return null!;
    }

    static float[] Fixture(string name) => WhisperEngine.ToFloat(WavFile.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", name)));

    [Fact]
    public void RunsOnTheGpu() => Assert.Equal("Vulkan", Engine.Runtime);

    static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TranscribesTheShortClip()
    {
        // Text-to-speech clip from the spike; runtimes differ slightly, so match loosely.
        var result = await Engine.TranscribeAsync(Fixture("short.wav"), "auto", Cancel);
        Assert.Contains("hey cloud", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dictation app", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("windows", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("en", result.Language); // auto-detected
    }

    [Fact]
    public async Task ClipsLongerThan30SecondsComeBackOnOneLine()
    {
        var text = (await Engine.TranscribeAsync(Fixture("long.wav"), "auto", Cancel)).Text;
        Assert.StartsWith("This morning I trained legs", text);
        Assert.Contains("overhead press", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain("  ", text);
    }

    [Fact]
    public async Task ForcedLanguageIsPassedToWhisper()
    {
        // Whisper still writes clear English speech in English when forced to Turkish, so check the language it used.
        Assert.Equal("tr", (await Engine.TranscribeAsync(Fixture("short.wav"), "tr", Cancel)).Language);
        Assert.Equal("en", (await Engine.TranscribeAsync(Fixture("short.wav"), "en", Cancel)).Language);
    }

    [Fact]
    public async Task WarmUpRuns() => await Engine.WarmUpAsync("auto");
}
