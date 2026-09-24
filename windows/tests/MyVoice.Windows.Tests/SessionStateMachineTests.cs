using MyVoice.Windows.Core;

namespace MyVoice.Windows.Tests;

public class SessionStateMachineTests
{
    static CapturedAudio Speech(double seconds) => new(Enumerable.Repeat((short)1000, (int)(seconds * 16000)).ToArray());
    static CapturedAudio Zeros(double seconds) => new(new short[(int)(seconds * 16000)]);
    static readonly Command[] None = [];
    static Command[] Cancelled(int s) => [new CloseMic(s, false), new UnregisterEsc(), new PlaySound(Sound.Cancel), new Notify("Cancelled", NoteKind.Info)];

    static SessionStateMachine Ready() { var m = new SessionStateMachine(); m.ModelLoaded(); return m; }
    static SessionStateMachine Connecting() { var m = Ready(); m.HotkeyPressed(); return m; }
    static SessionStateMachine Recording() { var m = Connecting(); m.MicReady(1); return m; }
    static SessionStateMachine InTail() { var m = Recording(); m.HotkeyPressed(); return m; }
    static SessionStateMachine AwaitingAudio() { var m = InTail(); m.TailElapsed(1); return m; }
    static SessionStateMachine Transcribing(CapturedAudio? audio = null) { var m = AwaitingAudio(); m.AudioCaptured(1, audio ?? Speech(3)); return m; }

    // ---- startup ----

    [Fact]
    public void StartsLoadingTheModel()
    {
        var m = new SessionStateMachine();
        Assert.Equal(AppState.LoadingModel, m.State);
        Assert.Equal("Loading model…", m.StatusText);
    }

    [Fact]
    public void HotkeyWhileLoadingSaysSoInsteadOfRecording()
    {
        // Code review finding: silently ignoring it looked like broken dictation (first launch loads for ~6 s).
        var m = new SessionStateMachine();
        Assert.Equal([new Notify("Still loading the model — try again in a moment", NoteKind.Warning)], m.HotkeyPressed());
        Assert.Equal(AppState.LoadingModel, m.State);
    }

    [Fact]
    public void ModelLoadedGoesReady()
    {
        var m = new SessionStateMachine();
        Assert.Equal(None, m.ModelLoaded());
        Assert.Equal(AppState.Ready, m.State);
        Assert.Equal("Ready", m.StatusText);
    }

    [Fact]
    public void ModelFailureIsAnErrorThatBlocksDictation()
    {
        var m = new SessionStateMachine();
        m.ModelFailed("Model not found: C:\\x.bin");
        Assert.Equal(AppState.Error, m.State);
        Assert.Equal("Model not found: C:\\x.bin", m.StatusText);
        Assert.Equal(None, m.HotkeyPressed());
    }

    [Fact]
    public void HotkeyUnavailableIsAnError()
    {
        var m = Ready();
        m.HotkeyUnavailable("Ctrl+Shift+D is used by another app");
        Assert.Equal(AppState.Error, m.State);
        Assert.Equal("Ctrl+Shift+D is used by another app", m.StatusText);
    }

    [Fact]
    public void HotkeyUnavailableBeforeModelLoadStaysAnError()
    {
        var m = new SessionStateMachine();
        m.HotkeyUnavailable("taken");
        m.ModelLoaded();
        Assert.Equal(AppState.Error, m.State);
    }

    // ---- happy path ----

    [Fact]
    public void HotkeyOpensTheMicAndListensForEsc()
    {
        var m = Ready();
        Assert.Equal([new OpenMic(1), new RegisterEsc()], m.HotkeyPressed());
        Assert.Equal(AppState.Connecting, m.State);
        Assert.Equal("Connecting to microphone…", m.StatusText);
        Assert.Equal(1, m.Session);
    }

    [Fact]
    public void MicReadyStartsRecordingWithTheChime()
    {
        var m = Connecting();
        Assert.Equal([new PlaySound(Sound.Start)], m.MicReady(1));
        Assert.Equal(AppState.Recording, m.State);
        Assert.Equal("Recording…", m.StatusText);
    }

    [Fact]
    public void HotkeyWhileRecordingStartsTheTailWithoutClosingTheMic()
    {
        var m = Recording();
        Assert.Equal([new StartTail(1)], m.HotkeyPressed());
        Assert.Equal(AppState.Stopping, m.State);
        Assert.Equal("Finishing…", m.StatusText);
    }

    [Fact]
    public void TailElapsedClosesTheMicKeepingAudioThenChimes()
    {
        // The stop chime plays after the tail so it is not recorded.
        var m = InTail();
        Assert.Equal([new CloseMic(1, true), new PlaySound(Sound.Stop)], m.TailElapsed(1));
        Assert.Equal(AppState.Stopping, m.State);
    }

    [Fact]
    public void CapturedAudioIsTranscribed()
    {
        var m = AwaitingAudio();
        var audio = Speech(3);
        Assert.Equal([new UnregisterEsc(), new Transcribe(1, audio)], m.AudioCaptured(1, audio));
        Assert.Equal(AppState.Transcribing, m.State);
        Assert.Equal("Transcribing…", m.StatusText);
    }

    [Fact]
    public void TranscriptIsPasted()
    {
        var m = Transcribing();
        Assert.Equal([new Paste("Hey Claude")], m.TranscriptionDone(1, "Hey Claude"));
        Assert.Equal(AppState.Ready, m.State);
        Assert.Equal("Hey Claude", m.LastTranscription);
    }

    [Fact]
    public void EmptyTranscriptIsNotPasted()
    {
        var m = Transcribing();
        Assert.Equal([new Notify("Nothing heard", NoteKind.Info)], m.TranscriptionDone(1, ""));
        Assert.Equal(AppState.Ready, m.State);
        Assert.Null(m.LastTranscription);
    }

    [Fact]
    public void NextDictationIsANewSession()
    {
        var m = Transcribing();
        m.TranscriptionDone(1, "one");
        Assert.Equal([new OpenMic(2), new RegisterEsc()], m.HotkeyPressed());
        Assert.Equal(2, m.Session);
    }

    // ---- cancelling ----

    [Fact] public void EscWhileConnectingCancels() => AssertCancels(Connecting(), m => m.EscPressed());

    [Fact] public void HotkeyWhileConnectingCancels() => AssertCancels(Connecting(), m => m.HotkeyPressed());

    [Fact] public void EscWhileRecordingCancels() => AssertCancels(Recording(), m => m.EscPressed());

    [Fact] public void EscDuringTheTailCancels() => AssertCancels(InTail(), m => m.EscPressed());

    static void AssertCancels(SessionStateMachine m, Func<SessionStateMachine, IReadOnlyList<Command>> act)
    {
        Assert.Equal(Cancelled(1), act(m));
        Assert.Equal(AppState.Ready, m.State);
    }

    [Fact]
    public void EscWhileWaitingForTheCapturedAudioCancelsAndTheAudioIsIgnored()
    {
        var m = AwaitingAudio();
        Assert.Equal([new UnregisterEsc(), new PlaySound(Sound.Cancel), new Notify("Cancelled", NoteKind.Info)], m.EscPressed());
        Assert.Equal(None, m.AudioCaptured(1, Speech(3)));
        Assert.Equal(AppState.Ready, m.State);
    }

    [Fact]
    public void HotkeyDuringTheTailIsIgnored()
    {
        var m = InTail();
        Assert.Equal(None, m.HotkeyPressed());
        Assert.Equal(AppState.Stopping, m.State);
    }

    [Fact]
    public void HotkeyWhileWaitingForAudioIsIgnored()
    {
        var m = AwaitingAudio();
        Assert.Equal(None, m.HotkeyPressed());
        Assert.Equal(AppState.Stopping, m.State);
    }

    [Fact]
    public void HotkeyAndEscWhileTranscribingAreIgnored()
    {
        var m = Transcribing();
        Assert.Equal(None, m.HotkeyPressed());
        Assert.Equal(None, m.EscPressed());
        Assert.Equal(AppState.Transcribing, m.State);
    }

    [Fact]
    public void AnewSessionCanStartRightAfterCancelling()
    {
        var m = Recording();
        m.EscPressed();
        Assert.Equal([new OpenMic(2), new RegisterEsc()], m.HotkeyPressed());
    }

    // ---- stale completions ----

    [Fact]
    public void MicReadyFromACancelledSessionIsIgnored()
    {
        var m = Connecting();
        m.EscPressed();
        Assert.Equal(None, m.MicReady(1));
        Assert.Equal(AppState.Ready, m.State);
    }

    [Fact]
    public void MicReadyFromAnOlderSessionDoesNotStartTheCurrentOne()
    {
        var m = Connecting();
        m.EscPressed();
        m.HotkeyPressed();              // session 2 connecting
        Assert.Equal(None, m.MicReady(1));
        Assert.Equal(AppState.Connecting, m.State);
    }

    [Fact]
    public void TailFromACancelledSessionIsIgnored()
    {
        var m = InTail();
        m.EscPressed();
        Assert.Equal(None, m.TailElapsed(1));
    }

    [Fact]
    public void TranscriptFromAnotherSessionIsIgnored()
    {
        var m = Transcribing();
        Assert.Equal(None, m.TranscriptionDone(99, "ghost"));
        Assert.Equal(AppState.Transcribing, m.State);
    }

    [Fact]
    public void TimeoutFromAnOlderSessionIsIgnored()
    {
        var m = Recording();
        Assert.Equal(None, m.MicTimedOut(1)); // readiness already happened
        Assert.Equal(AppState.Recording, m.State);
    }

    // ---- microphone problems ----

    [Fact]
    public void MicTimeoutGivesUpWithAWarning()
    {
        var m = Connecting();
        Assert.Equal([new CloseMic(1, false), new UnregisterEsc(), new Notify("Mic not delivering audio — nothing recorded", NoteKind.Warning)], m.MicTimedOut(1));
        Assert.Equal(AppState.Ready, m.State);
    }

    [Fact]
    public void MicThatCannotOpenIsReported()
    {
        var m = Connecting();
        Assert.Equal([new UnregisterEsc(), new Notify("No microphone found", NoteKind.Warning)], m.MicOpenFailed(1, "No microphone found"));
        Assert.Equal(AppState.Ready, m.State);
    }

    [Fact]
    public void MicLostWhileConnectingRecordsNothing()
    {
        var m = Connecting();
        Assert.Equal([new CloseMic(1, false), new UnregisterEsc(), new Notify("Microphone disconnected — nothing recorded", NoteKind.Warning)], m.MicLost(1));
        Assert.Equal(AppState.Ready, m.State);
    }

    [Fact]
    public void MicLostWhileRecordingTranscribesWhatWasCaptured()
    {
        var m = Recording();
        Assert.Equal([new CloseMic(1, true), new PlaySound(Sound.Stop)], m.MicLost(1));
        Assert.Equal(AppState.Stopping, m.State);
        var audio = Speech(2);
        Assert.Equal([new UnregisterEsc(), new Transcribe(1, audio)], m.AudioCaptured(1, audio));
    }

    [Fact]
    public void MicLostDuringTheTailEndsTheTailEarly()
    {
        var m = InTail();
        Assert.Equal([new CloseMic(1, true), new PlaySound(Sound.Stop)], m.MicLost(1));
        Assert.Equal(None, m.TailElapsed(1));
    }

    [Fact]
    public void AllZeroAudioMeansTheMicIsMutedOrBlocked()
    {
        var m = AwaitingAudio();
        Assert.Equal([new UnregisterEsc(), new Notify("Mic muted or blocked — check Settings › Privacy & security › Microphone", NoteKind.Warning)],
            m.AudioCaptured(1, Zeros(3)));
        Assert.Equal(AppState.Ready, m.State);
    }

    [Fact]
    public void QuietAudioIsNotSentToWhisper()
    {
        // Whisper turns silence into "Thank you." (seen in WhisperEngineTests), so room noise must never be transcribed.
        // Peak 100/32768 = -50 dBFS; idle AirPods measured -66 to -84, speech -10 to -16.
        var m = AwaitingAudio();
        Assert.Equal([new UnregisterEsc(), new Notify("Nothing heard", NoteKind.Info)],
            m.AudioCaptured(1, new CapturedAudio(Enumerable.Repeat((short)100, 48000).ToArray())));
        Assert.Equal(AppState.Ready, m.State);
    }

    [Fact]
    public void QuietSpeechAboveTheGateIsTranscribed()
    {
        var m = AwaitingAudio();
        var audio = new CapturedAudio(Enumerable.Repeat((short)300, 48000).ToArray()); // -41 dBFS
        Assert.Equal([new UnregisterEsc(), new Transcribe(1, audio)], m.AudioCaptured(1, audio));
    }

    [Fact]
    public void PeakLevelIsInDbfs()
    {
        Assert.Equal(-6.02, new CapturedAudio([0, -16384, 100]).PeakDbfs, 2);
        Assert.Equal(double.NegativeInfinity, new CapturedAudio(new short[10]).PeakDbfs);
        Assert.Equal(0, new CapturedAudio([short.MinValue]).PeakDbfs, 2);
    }

    [Fact]
    public void VeryShortAudioIsNothingRecorded()
    {
        var m = AwaitingAudio();
        Assert.Equal([new UnregisterEsc(), new Notify("Nothing recorded", NoteKind.Info)], m.AudioCaptured(1, Speech(0.05)));
        Assert.Equal(AppState.Ready, m.State);
    }

    [Fact]
    public void TranscriptionFailureIsReported()
    {
        var m = Transcribing();
        Assert.Equal([new Notify("Transcription failed: boom", NoteKind.Warning)], m.TranscriptionFailed(1, "boom"));
        Assert.Equal(AppState.Ready, m.State);
    }

    // ---- quitting ----

    [Fact] public void QuitWhenReadyExits() => Assert.Equal([new ExitApp()], Ready().QuitRequested());

    [Fact] public void QuitInErrorExits()
    {
        var m = new SessionStateMachine();
        m.ModelFailed("x");
        Assert.Equal([new ExitApp()], m.QuitRequested());
    }

    [Fact]
    public void QuitWhileRecordingDiscardsAndExits() =>
        Assert.Equal([new CloseMic(1, false), new UnregisterEsc(), new ExitApp()], Recording().QuitRequested());

    [Fact]
    public void QuitDuringTheTailDiscardsAndExits() =>
        Assert.Equal([new CloseMic(1, false), new UnregisterEsc(), new ExitApp()], InTail().QuitRequested());

    [Fact]
    public void QuitWhileTranscribingWaitsForWhisperAndDoesNotPaste()
    {
        // Disposing Whisper mid-transcription is unsafe, so the exit waits for the result.
        var m = Transcribing();
        Assert.Equal(None, m.QuitRequested());
        Assert.Equal(AppState.Transcribing, m.State);
        Assert.Equal([new ExitApp()], m.TranscriptionDone(1, "text"));
    }

    [Fact]
    public void QuitWhileTranscribingExitsAfterAFailureToo()
    {
        var m = Transcribing();
        m.QuitRequested();
        Assert.Equal([new ExitApp()], m.TranscriptionFailed(1, "boom"));
    }

    [Fact]
    public void QuitWhileLoadingWaitsForTheModel()
    {
        var m = new SessionStateMachine();
        Assert.Equal(None, m.QuitRequested());
        Assert.Equal([new ExitApp()], m.ModelLoaded());
    }

    [Fact]
    public void QuitWhileLoadingExitsIfTheModelFails()
    {
        var m = new SessionStateMachine();
        m.QuitRequested();
        Assert.Equal([new ExitApp()], m.ModelFailed("x"));
    }
}
