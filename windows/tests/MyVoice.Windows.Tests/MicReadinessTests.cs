using MyVoice.Windows.Core;

namespace MyVoice.Windows.Tests;

// Cases from the Python spike's mic_unit_test.py and the AirPods measurements, at NAudio's 20 ms blocks.
public class MicReadinessTests
{
    const int Block = 320; // 20 ms at 16 kHz

    /// <summary>Feeds real-time 20 ms blocks from <paramref name="from"/> to <paramref name="to"/>, polling after each.</summary>
    static MicStatus Feed(MicReadiness m, double from, double to)
    {
        var status = MicStatus.Waiting;
        for (var t = from; t < to - 1e-9; t += 0.02) { m.AddBlock(t, Block); status = m.Poll(t); if (status != MicStatus.Waiting) break; }
        return status;
    }

    [Fact]
    public void NoAudioTimesOutAfter15Seconds()
    {
        var m = new MicReadiness(openedAt: 0);
        Assert.Equal(MicStatus.Waiting, m.Poll(14.9));
        Assert.Equal(MicStatus.TimedOut, m.Poll(15.1));
        Assert.Null(m.ReadyAt);
    }

    [Fact]
    public void RealTimeAudioIsReadyAfterOneWindow()
    {
        var m = new MicReadiness(openedAt: 0);
        Assert.Equal(MicStatus.Ready, Feed(m, 0, 2));
        Assert.InRange(m.ReadyAt!.Value, 0.46, 0.54);
    }

    [Fact]
    public void HeadsetSwitchDelaysReadiness()
    {
        // 10 s of nothing (the cold-AirPods case from UAT), then real-time audio.
        var m = new MicReadiness(openedAt: 0);
        Assert.Equal(MicStatus.Waiting, m.Poll(10));
        Assert.Equal(MicStatus.Ready, Feed(m, 10, 12));
        // 0.45 s of audio (90 % of the 0.5 s window) = 23 blocks of 20 ms → ready at ~10.44 s.
        Assert.InRange(m.ReadyAt!.Value, 10.42, 10.50);
    }

    [Fact]
    public void TrickleIsNotReady()
    {
        // The stall signature: a few small blocks with long gaps (0.2 s of audio over 5 s).
        var m = new MicReadiness(openedAt: 0);
        foreach (var t in new[] { 3.9, 4.2, 4.6, 5.0, 5.8, 6.4, 7.4 }) { m.AddBlock(t, 457); Assert.Equal(MicStatus.Waiting, m.Poll(t)); }
    }

    [Fact]
    public void ReadinessIsSticky()
    {
        var m = new MicReadiness(openedAt: 0);
        Feed(m, 0, 1);
        var readyAt = m.ReadyAt;
        Assert.Equal(MicStatus.Ready, m.Poll(30)); // a later gap or the timeout no longer matters
        Assert.Equal(readyAt, m.ReadyAt);
    }

    [Fact]
    public void AudioArrivingJustAfterOpenIsNotReadyBeforeAFullWindow()
    {
        var m = new MicReadiness(openedAt: 0);
        m.AddBlock(0.0, 16000); // a burst of 1 s arriving at once, right at open
        Assert.Equal(MicStatus.Waiting, m.Poll(0.1));
    }
}

public class CaptureBufferTests
{
    [Fact]
    public void KeepsOnlySamplesArrivingAfterSpeakNow()
    {
        var b = new CaptureBuffer();
        b.Append(0.1, [1, 1]);
        b.Append(0.5, [2, 2]);
        b.Append(0.9, [3]);
        Assert.Equal(new short[] { 2, 2, 3 }, b.TakeFrom(0.5));
    }

    [Fact]
    public void EmptyWhenNothingArrived() => Assert.Empty(new CaptureBuffer().TakeFrom(0));

    [Fact]
    public void NothingWhenNeverReady() => Assert.Empty(WithOneBlock().TakeFrom(double.PositiveInfinity));

    static CaptureBuffer WithOneBlock() { var b = new CaptureBuffer(); b.Append(1, [5, 5]); return b; }
}
