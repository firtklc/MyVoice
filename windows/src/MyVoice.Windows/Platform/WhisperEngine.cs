using MyVoice.Windows.Core;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace MyVoice.Windows.Platform;

sealed record Transcript(string Text, string Language);

/// <summary>
/// Whisper.net wrapper (the Mac's WhisperEngine.swift): the model is loaded once for the app's lifetime and
/// transcriptions run one at a time. GPU via the Vulkan runtime (needs only the graphics driver), CPU as fallback.
/// </summary>
sealed class WhisperEngine : IAsyncDisposable
{
    readonly WhisperFactory _factory;
    readonly Dictionary<string, WhisperProcessor> _processors = [];
    readonly SemaphoreSlim _oneAtATime = new(1, 1);

    public string Runtime { get; }

    WhisperEngine(WhisperFactory factory, string runtime)
    {
        _factory = factory;
        Runtime = runtime;
    }

    static WhisperEngine()
    {
        // whisper.cpp's own log: keep the GPU device list and anything that went wrong.
        LogProvider.AddLogger((level, text) =>
        {
            var message = text ?? "";
            if (level is WhisperLogLevel.Error or WhisperLogLevel.Warning) Log.Warn("whisper.cpp: " + message.TrimEnd());
            else if (message.StartsWith("ggml_vulkan: ", StringComparison.Ordinal) || message.Contains("using ")) Log.Info("whisper.cpp: " + message.TrimEnd());
        });
    }

    /// <summary>Blocking (1–5 s); call from a background thread.</summary>
    public static WhisperEngine Load(string modelPath)
    {
        if (!File.Exists(modelPath)) throw new FileNotFoundException($"Model not found: {modelPath}", modelPath);
        // Explicit order: a broken CUDA runtime can crash instead of falling back (Whisper.net #462), and we ship none.
        RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
        var factory = WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = true, UseFlashAttention = true });
        return new WhisperEngine(factory, RuntimeOptions.LoadedLibrary?.ToString() ?? "unknown");
    }

    /// <summary>One second of silence in the language the app will use: the first transcription after a launch pays
    /// the GPU setup cost here, not on the user's first dictation.</summary>
    public Task WarmUpAsync(string language) => TranscribeAsync(new float[CapturedAudio.SampleRate], language);

    /// <summary>Text on one line, plus the language Whisper used (detected when <paramref name="language"/> is "auto").</summary>
    public async Task<Transcript> TranscribeAsync(float[] samples, string language, CancellationToken cancel = default)
    {
        await _oneAtATime.WaitAsync(cancel);
        try
        {
            var segments = new List<string>();
            var detected = language;
            await foreach (var segment in Processor(language).ProcessAsync(samples, cancel))
            {
                segments.Add(segment.Text);
                detected = segment.Language;
            }
            return new Transcript(TranscriptText.Join(segments), detected);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    // The language is fixed when a processor is built, so keep one per language. Never WithStringPool (#541).
    WhisperProcessor Processor(string language)
    {
        if (!_processors.TryGetValue(language, out var processor))
        {
            processor = _factory.CreateBuilder()
                .WithLanguage(language)
                .WithGreedySamplingStrategy()
                .WithNoContext()
                .WithThreads(Math.Clamp(Environment.ProcessorCount - 2, 1, 8)) // the Mac's thread rule
                .Build();
            _processors[language] = processor;
        }
        return processor;
    }

    public static float[] ToFloat(short[] samples)
    {
        var result = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++) result[i] = samples[i] / 32768f;
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var processor in _processors.Values) await processor.DisposeAsync();
        _factory.Dispose();
        _oneAtATime.Dispose();
    }
}
