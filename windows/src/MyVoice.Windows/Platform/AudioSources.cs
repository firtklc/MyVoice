using System.Diagnostics;
using System.Runtime.InteropServices;
using MyVoice.Windows.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MyVoice.Windows.Platform;

delegate void SamplesHandler(ReadOnlySpan<short> samples);

/// <summary>A 16 kHz mono 16-bit sample stream. Events fire on a background thread.</summary>
interface IAudioSource : IDisposable
{
    string DeviceName { get; }
    event SamplesHandler? Samples;
    /// <summary>Raised once when capture ends; the exception is non-null when it ended on its own (e.g. device removed).</summary>
    event Action<Exception?>? Stopped;
    /// <summary>Throws when no microphone can be opened.</summary>
    void Start();
    void Stop();
}

sealed class MicOpenException(string message, Exception? inner = null) : Exception(message, inner)
{
    const int ElementNotFound = unchecked((int)0x80070490); // no default capture endpoint
    const int AccessDenied = unchecked((int)0x80070005);    // Settings › Privacy & security › Microphone

    public static MicOpenException From(Exception e) => e.HResult switch
    {
        ElementNotFound => new("No microphone found", e),
        AccessDenied => new("Microphone access is blocked — Settings › Privacy & security › Microphone", e),
        _ => new($"Microphone could not start: {e.Message}", e),
    };
}

/// <summary>The system default microphone, opened fresh for every recording so a newly connected mic is used.</summary>
sealed class MicSource : IAudioSource
{
    WasapiRecorder? _recorder;

    public string DeviceName { get; private set; } = "";
    public event SamplesHandler? Samples;
    public event Action<Exception?>? Stopped;

    public void Start()
    {
        // Windows converts the device format to 16 kHz mono (AutoConvertPcm). 20 ms buffers keep the
        // readiness window steady; the 100 ms default makes it flicker between 4 and 5 blocks.
        _recorder = new WasapiRecorderBuilder().WithFormat(new WaveFormat(CapturedAudio.SampleRate, 16, 1)).WithBufferLength(20).Build();
        DeviceName = _recorder.DeviceFriendlyName;
        _recorder.DataAvailable += (buffer, flags, _, _) =>
        {
            var samples = MemoryMarshal.Cast<byte, short>(buffer);
            if (flags.HasFlag(AudioClientBufferFlags.Silent)) samples = new short[samples.Length]; // the engine marks the data as meaningless
            Samples?.Invoke(samples);
        };
        _recorder.RecordingStopped += (_, e) => Stopped?.Invoke(e.Exception);
        _recorder.StartRecording();
    }

    public void Stop() => _recorder?.StopRecording();

    public void Dispose() => _recorder?.Dispose();
}

/// <summary>
/// Plays a WAV as if it were the mic, in real time: nothing during <paramref name="stallSeconds"/> (a cold
/// Bluetooth headset), then silence until <see cref="BeginSpeech"/> (the user waiting for the chime), then the
/// WAV, then silence. Used by tests and the --simulate switch.
/// </summary>
sealed class WavFileSource(short[] speech, double stallSeconds = 0) : IAudioSource
{
    const int Block = 320; // 20 ms, like the real mic
    volatile bool _stop, _speaking;
    volatile bool _finishedSpeech;
    Thread? _thread;

    public string DeviceName => "WAV file (simulated mic)";
    public event SamplesHandler? Samples;
    public event Action<Exception?>? Stopped;

    /// <summary>True once the whole WAV has been delivered.</summary>
    public bool FinishedSpeech => _finishedSpeech;

    public void BeginSpeech() => _speaking = true;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "WavFileSource" };
        _thread.Start();
    }

    void Run()
    {
        var clock = Stopwatch.StartNew();
        while (!_stop && clock.Elapsed.TotalSeconds < stallSeconds) Thread.Sleep(5);
        var origin = clock.Elapsed.TotalSeconds;
        long delivered = 0, spoken = 0;
        var block = new short[Block];
        while (!_stop)
        {
            var due = (long)((clock.Elapsed.TotalSeconds - origin) * CapturedAudio.SampleRate);
            while (delivered + Block <= due && !_stop)
            {
                Array.Clear(block);
                if (_speaking && spoken < speech.Length)
                {
                    var n = (int)Math.Min(Block, speech.Length - spoken);
                    Array.Copy(speech, spoken, block, 0, n);
                    spoken += n;
                    if (spoken >= speech.Length) _finishedSpeech = true;
                }
                Samples?.Invoke(block);
                delivered += Block;
            }
            Thread.Sleep(5);
        }
        Stopped?.Invoke(null);
    }

    public void Stop() => _stop = true;

    public void Dispose() => _stop = true;
}
