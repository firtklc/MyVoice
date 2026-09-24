namespace MyVoice.Windows.Core;

public enum MicStatus { Waiting, Ready, TimedOut }

/// <summary>
/// Decides when it is safe to say "speak now". Bluetooth headsets (AirPods) switch into headset mode when
/// the mic opens and deliver little or no audio for 0.7–11 s; the mic counts as ready once the last
/// <c>window</c> seconds delivered at least <c>ratio</c> of real time. Thread-safe: blocks arrive on the
/// audio thread, <see cref="Poll"/> runs on the UI thread. Times are seconds on one monotonic clock.
/// </summary>
public sealed class MicReadiness
{
    readonly object _gate = new();
    readonly List<(double At, int Samples)> _recent = [];
    readonly double _openedAt, _window, _timeout, _samplesNeeded;
    double? _readyAt;

    public MicReadiness(double openedAt, int sampleRate = 16000, double window = 0.5, double ratio = 0.9, double timeout = 15)
    {
        _openedAt = openedAt;
        _window = window;
        _timeout = timeout;
        _samplesNeeded = ratio * window * sampleRate;
    }

    public double? ReadyAt { get { lock (_gate) return _readyAt; } }

    public void AddBlock(double arrivedAt, int samples)
    {
        lock (_gate)
            if (_readyAt is null) _recent.Add((arrivedAt, samples));
    }

    public MicStatus Poll(double now)
    {
        lock (_gate)
        {
            if (_readyAt is not null) return MicStatus.Ready;
            _recent.RemoveAll(b => b.At <= now - _window);
            // A full window must have passed since opening, so a burst right at open can't count as flowing audio.
            if (now - _openedAt >= _window && _recent.Sum(b => b.Samples) >= _samplesNeeded)
            {
                _readyAt = now;
                _recent.Clear();
                return MicStatus.Ready;
            }
            return now - _openedAt > _timeout ? MicStatus.TimedOut : MicStatus.Waiting;
        }
    }
}

/// <summary>Captured 16-bit samples with their arrival times; keeps only what arrived after "speak now".</summary>
public sealed class CaptureBuffer
{
    readonly object _gate = new();
    readonly List<(double At, short[] Samples)> _blocks = [];

    public void Append(double arrivedAt, ReadOnlySpan<short> samples)
    {
        var copy = samples.ToArray(); // the audio callback's span is only valid during the call
        lock (_gate) _blocks.Add((arrivedAt, copy));
    }

    public short[] TakeFrom(double since)
    {
        lock (_gate) return _blocks.Where(b => b.At >= since).SelectMany(b => b.Samples).ToArray();
    }
}
