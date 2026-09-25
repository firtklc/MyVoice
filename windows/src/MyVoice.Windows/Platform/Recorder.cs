using MyVoice.Windows.Core;

namespace MyVoice.Windows.Platform;

sealed record RecordingStats(string Device, double ReadyWaitSeconds, double CapturedSeconds, double WallSeconds, double PeakDbfs);

/// <summary>
/// One recording per session on top of an <see cref="IAudioSource"/>. Called on the UI thread only; the audio
/// thread just appends to thread-safe buffers, and stop notifications come back through <c>post</c> (never a
/// blocking Invoke, which deadlocks when capture stops). <see cref="Poll"/> runs on a UI timer.
/// </summary>
sealed class Recorder(Func<IAudioSource> createSource, Action<Action> post, Func<double> now) : IDisposable
{
    /// <summary>If the device never reports that it stopped, finish with what was captured after this long.</summary>
    public const double StopWatchdogSeconds = 2;

    sealed class Take(int session, IAudioSource source, double openedAt)
    {
        public readonly int Session = session;
        public readonly IAudioSource Source = source;
        public readonly double OpenedAt = openedAt;
        public readonly MicReadiness Readiness = new(openedAt);
        public readonly CaptureBuffer Buffer = new();
        public int PeakSinceRead; // written on the audio thread, taken on the UI thread (Interlocked)
        public bool Signalled, Stopped, CloseRequested, Keep, Finished;
        public double CloseRequestedAt;
    }

    readonly List<Take> _takes = [];
    Take? _active;

    public event Action<int>? Ready;
    public event Action<int>? TimedOut;
    public event Action<int>? Lost;
    public event Action<int, CapturedAudio, RecordingStats>? Captured;

    /// <summary>For the level meter: the loudest sample since the last call, 0…1. Not just the latest block's:
    /// AirPods deliver a 20 ms block followed by near-empty ones, so the latest block is usually silent.</summary>
    public float TakeLevel() => _active is { } take ? Interlocked.Exchange(ref take.PeakSinceRead, 0) / 32768f : 0;

    public string? DeviceName => _active?.Source.DeviceName;

    /// <summary>Throws <see cref="MicOpenException"/> when the mic can't be opened.</summary>
    public void Open(int session)
    {
        var source = createSource();
        var take = new Take(session, source, now());
        source.Samples += (samples, engineSilence) =>
        {
            var at = now();
            if (!engineSilence) take.Readiness.AddBlock(at, samples.Length); // padding isn't the device delivering
            take.Buffer.Append(at, samples);
            var peak = 0;
            foreach (var s in samples) peak = Math.Max(peak, Math.Abs((int)s));
            for (var seen = take.PeakSinceRead; peak > seen; seen = take.PeakSinceRead) // lock-free max
                if (Interlocked.CompareExchange(ref take.PeakSinceRead, peak, seen) == seen) break;
        };
        source.Stopped += error => post(() => OnStopped(take, error));
        try
        {
            source.Start();
        }
        catch (Exception e)
        {
            source.Dispose();
            throw e as MicOpenException ?? MicOpenException.From(e);
        }
        _takes.Add(take);
        _active = take;
    }

    public void Poll()
    {
        var t = now();
        if (_active is { Signalled: false, CloseRequested: false } a)
        {
            switch (a.Readiness.Poll(t))
            {
                case MicStatus.Ready: a.Signalled = true; Ready?.Invoke(a.Session); break;
                case MicStatus.TimedOut: a.Signalled = true; TimedOut?.Invoke(a.Session); break;
            }
        }
        foreach (var take in _takes.ToArray())
        {
            if (take is { CloseRequested: true, Finished: false } && t - take.CloseRequestedAt > StopWatchdogSeconds)
            {
                Log.Warn($"mic did not report stopping within {StopWatchdogSeconds} s — finishing session {take.Session} with what was captured");
                Finish(take);
            }
        }
    }

    /// <summary>Stops the session's capture; with <paramref name="keep"/>, <see cref="Captured"/> follows once the device has stopped.</summary>
    public void Close(int session, bool keep)
    {
        var take = _takes.FirstOrDefault(x => x.Session == session && !x.CloseRequested);
        if (take is null) return;
        take.CloseRequested = true;
        take.CloseRequestedAt = now();
        take.Keep = keep;
        if (_active == take) _active = null;
        if (take.Stopped) Finish(take);
        else take.Source.Stop();
    }

    void OnStopped(Take take, Exception? error)
    {
        take.Stopped = true;
        if (take.CloseRequested)
        {
            Finish(take);
            return;
        }
        Log.Warn($"mic stopped by itself in session {take.Session}: {error?.Message ?? "no error given"}");
        Lost?.Invoke(take.Session); // the state machine answers with CloseMic, which finishes the take
    }

    void Finish(Take take)
    {
        if (take.Finished) return;
        take.Finished = true;
        _takes.Remove(take);
        take.Source.Dispose();
        if (!take.Keep) return;
        var readyAt = take.Readiness.ReadyAt;
        var audio = new CapturedAudio(take.Buffer.TakeFrom(readyAt ?? double.PositiveInfinity));
        var stats = new RecordingStats(
            take.Source.DeviceName,
            ReadyWaitSeconds: (readyAt ?? take.OpenedAt) - take.OpenedAt,
            CapturedSeconds: audio.Seconds,
            WallSeconds: readyAt is null ? 0 : take.CloseRequestedAt - readyAt.Value,
            PeakDbfs: audio.PeakDbfs);
        Captured?.Invoke(take.Session, audio, stats);
    }

    public void Dispose()
    {
        foreach (var take in _takes) take.Source.Dispose();
        _takes.Clear();
        _active = null;
    }
}
