using System.Diagnostics;
using System.Runtime.InteropServices;
using MyVoice.Windows.Core;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

/// <summary>A mic the test drives by hand: no threads, so every Recorder behaviour is deterministic.</summary>
sealed class FakeSource : IAudioSource
{
    public Exception? StartError;
    public bool Started, StopRequested, Disposed;
    public string DeviceName => "Fake mic";
    public event SamplesHandler? Samples;
    public event Action<Exception?>? Stopped;
    public void Start() { if (StartError is not null) throw StartError; Started = true; }
    public void Stop() => StopRequested = true;
    public void Dispose() => Disposed = true;
    public void Emit(short value, int count = 320, bool engineSilence = false) => Samples?.Invoke(Enumerable.Repeat(value, count).ToArray(), engineSilence);
    public void RaiseStopped(Exception? error = null) => Stopped?.Invoke(error);
}

public class RecorderTests
{
    double _now;
    readonly Queue<Action> _ui = new();
    readonly List<string> _events = [];
    CapturedAudio? _audio;
    RecordingStats? _stats;
    readonly List<FakeSource> _sources = [];

    Recorder Make(Func<FakeSource>? create = null)
    {
        var r = new Recorder(() => { var s = create?.Invoke() ?? new FakeSource(); _sources.Add(s); return s; }, _ui.Enqueue, () => _now);
        r.Ready += s => _events.Add($"ready {s}");
        r.TimedOut += s => _events.Add($"timeout {s}");
        r.Lost += s => _events.Add($"lost {s}");
        r.Captured += (s, a, st) => { _events.Add($"captured {s}"); _audio = a; _stats = st; };
        return r;
    }

    FakeSource Source => _sources[^1];

    void Pump() { while (_ui.TryDequeue(out var a)) a(); }

    /// <summary>Real-time 20 ms blocks of <paramref name="value"/> for <paramref name="seconds"/>, polling like the UI timer.</summary>
    void Speak(Recorder r, double seconds, short value = 1000)
    {
        for (var end = _now + seconds; _now < end - 1e-9; _now += 0.02) { Source.Emit(value); r.Poll(); }
    }

    [Fact]
    public void SignalsReadyOnceAudioFlows()
    {
        var r = Make();
        r.Open(1);
        Assert.True(Source.Started);
        Speak(r, 0.3);
        Assert.Empty(_events);
        Speak(r, 0.4);
        Assert.Equal(["ready 1"], _events);
    }

    [Fact]
    public void SilencePaddedByTheAudioEngineDoesNotCountAsAudioFlowing()
    {
        // Code review finding: packets flagged Silent by Windows are not from the device; if a cold headset's
        // stall were padded that way, "speak now" would come too early and the first words would be lost.
        var r = Make();
        r.Open(1);
        for (var end = _now + 3; _now < end; _now += 0.02) { Source.Emit(0, engineSilence: true); r.Poll(); }
        Assert.Empty(_events);
        Speak(r, 0.6);
        Assert.Equal(["ready 1"], _events);
    }

    [Fact]
    public void SignalsTimeoutWhenNoAudioArrives()
    {
        var r = Make();
        r.Open(1);
        _now = 15.1;
        r.Poll();
        r.Poll();
        Assert.Equal(["timeout 1"], _events); // once
    }

    [Fact]
    public void KeepsOnlyAudioFromSpeakNowUntilTheMicStops()
    {
        var r = Make();
        r.Open(1);
        _now = 5;                        // a 5 s headset switch with nothing delivered
        Speak(r, 0.6, value: 7);         // flows → ready at ~5.44; "pre-chime" audio (value 7) up to then
        var readyAt = _now;
        Speak(r, 2.0, value: 1000);      // the user speaks after the chime
        r.Close(1, keep: true);
        Assert.True(Source.StopRequested);
        Assert.DoesNotContain("captured 1", _events);   // not until the device reports it stopped
        Source.RaiseStopped();
        Pump();
        Assert.Contains("captured 1", _events);
        Assert.InRange(_audio!.Seconds, 2.0, 2.3);                     // speech plus the blocks after readiness
        Assert.All(_audio.Samples[^32000..], s => Assert.Equal(1000, s)); // the last 2 s are the speech
        Assert.True(Source.Disposed);
        Assert.Equal("Fake mic", _stats!.Device);
        Assert.InRange(_stats.ReadyWaitSeconds, 5.4, 5.6);
        Assert.InRange(_stats.WallSeconds, 2.0, 2.3);
    }

    [Fact]
    public void CloseWithoutKeepingDiscardsTheAudio()
    {
        var r = Make();
        r.Open(1);
        Speak(r, 1);
        r.Close(1, keep: false);
        Source.RaiseStopped();
        Pump();
        Assert.DoesNotContain("captured 1", _events);
        Assert.True(Source.Disposed);
    }

    [Fact]
    public void AMicThatStopsByItselfIsLostAndItsAudioCanStillBeKept()
    {
        var r = Make();
        r.Open(1);
        Speak(r, 2);
        Source.RaiseStopped(new IOException("device removed"));
        Pump();
        Assert.Contains("lost 1", _events);
        r.Close(1, keep: true);                     // the state machine's answer
        Assert.Contains("captured 1", _events);     // immediately: the device already stopped
        Assert.InRange(_audio!.Seconds, 1.4, 1.6);
    }

    [Fact]
    public void WatchdogFinishesWhenTheDeviceNeverReportsStopping()
    {
        var r = Make();
        r.Open(1);
        Speak(r, 1.5);
        r.Close(1, keep: true);
        _now += Recorder.StopWatchdogSeconds - 0.1;
        r.Poll();
        Assert.DoesNotContain("captured 1", _events);
        _now += 0.2;
        r.Poll();
        Assert.Contains("captured 1", _events);
    }

    [Fact]
    public void ALateStopAfterTheWatchdogIsIgnored()
    {
        var r = Make();
        r.Open(1);
        Speak(r, 1);
        r.Close(1, keep: true);
        _now += Recorder.StopWatchdogSeconds + 0.1;
        r.Poll();
        Source.RaiseStopped();
        Pump();
        Assert.Single(_events, e => e == "captured 1");
    }

    [Fact]
    public void ANewSessionCanOpenWhileTheLastOneIsStillStopping()
    {
        var r = Make();
        r.Open(1);
        Speak(r, 1);
        var first = Source;
        r.Close(1, keep: false);        // cancelled; device still stopping
        r.Open(2);
        Speak(r, 0.6);
        Assert.Contains("ready 2", _events);
        first.RaiseStopped();
        Pump();
        Assert.True(first.Disposed);
        Assert.False(Source.Disposed);
    }

    [Fact]
    public void MissingMicIsReportedPlainly()
    {
        var r = Make(() => new FakeSource { StartError = new COMException("Element not found", unchecked((int)0x80070490)) });
        var e = Assert.Throws<MicOpenException>(() => r.Open(1));
        Assert.Equal("No microphone found", e.Message);
        Assert.True(Source.Disposed);
    }

    [Fact]
    public void BlockedMicPointsToThePrivacySetting()
    {
        var r = Make(() => new FakeSource { StartError = new UnauthorizedAccessException("denied") { HResult = unchecked((int)0x80070005) } });
        Assert.Contains("Privacy & security", Assert.Throws<MicOpenException>(() => r.Open(1)).Message);
    }

    [Fact]
    public void LevelIsTheLoudestBlockSinceTheLastRead()
    {
        // UAT B: AirPods deliver a 20 ms block followed by near-empty ones (2452 blocks averaging 79 samples), so the
        // latest block was almost always empty and the overlay's bars never moved. The meter must see the speech.
        var r = Make();
        r.Open(1);
        Source.Emit(16384);
        Source.Emit(0, count: 0);
        Source.Emit(3, count: 79);
        Source.Emit(0, count: 0);
        Assert.Equal(0.5f, r.TakeLevel(), 3);
        Assert.Equal(0f, r.TakeLevel()); // nothing new since
        Source.Emit(8192);
        Assert.Equal(0.25f, r.TakeLevel(), 3);
    }

    [Fact]
    public void LevelIsZeroWithoutAnOpenMic() => Assert.Equal(0f, Make().TakeLevel());

    [Fact]
    public void LevelStopsWithTheMic()
    {
        var r = Make();
        r.Open(1);
        Source.Emit(16384);
        r.Close(1, keep: false);
        Assert.Equal(0f, r.TakeLevel());
    }
}

/// <summary>The real threaded path: a WAV played in real time through the Recorder, with a simulated headset stall.</summary>
[Trait("Kind", "Integration"), Trait("Resource", "Realtime")]
public class RecorderRealtimeTests
{
    [Fact]
    public void WavSourceWithAStallIsCapturedCompletelyAfterSpeakNow()
    {
        var speech = WavFile.Read(Path.Combine(AppContext.BaseDirectory, "Fixtures", "short.wav"));
        var source = new WavFileSource(speech, stallSeconds: 1.0);
        var ui = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var clock = Stopwatch.StartNew();
        var r = new Recorder(() => source, ui.Enqueue, () => clock.Elapsed.TotalSeconds);
        double? readyAt = null;
        CapturedAudio? audio = null;
        r.Ready += _ => { readyAt = clock.Elapsed.TotalSeconds; source.BeginSpeech(); };
        r.Captured += (_, a, _) => audio = a;
        r.Open(1);
        var deadline = clock.Elapsed.TotalSeconds + 20;
        while (audio is null && clock.Elapsed.TotalSeconds < deadline)
        {
            while (ui.TryDequeue(out var a)) a();
            r.Poll();
            if (source.FinishedSpeech && readyAt is not null) { Thread.Sleep(800); r.Close(1, keep: true); source.Stop(); } // tail, then stop
            Thread.Sleep(20);
        }
        Assert.NotNull(readyAt);
        Assert.InRange(readyAt.Value, 1.4, 1.8);                    // stall + one readiness window
        Assert.NotNull(audio);
        Assert.InRange(audio.Seconds, speech.Length / 16000.0 + 0.6, speech.Length / 16000.0 + 1.3);
        // The speech is all there, in order (the chime came before it started).
        var start = Array.FindIndex(audio.Samples, s => s != 0);
        var speechStart = Array.FindIndex(speech, s => s != 0); // the TTS clip itself begins with silence
        Assert.Equal(speech.Skip(speechStart).Take(16000), audio.Samples.Skip(start).Take(16000));
    }
}
