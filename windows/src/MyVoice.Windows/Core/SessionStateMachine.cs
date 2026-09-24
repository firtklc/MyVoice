namespace MyVoice.Windows.Core;

public enum AppState { LoadingModel, Ready, Connecting, Recording, Stopping, Transcribing, Error }

public enum Sound { Start, Stop, Cancel }

public enum NoteKind { Info, Warning }

/// <summary>16 kHz mono samples captured after "speak now" (plus the stop tail).</summary>
public sealed record CapturedAudio(short[] Samples)
{
    public const int SampleRate = 16000;
    public double Seconds => Samples.Length / (double)SampleRate;
    public bool IsAllZero => Samples.All(s => s == 0);

    /// <summary>Loudest sample relative to full scale; −∞ for digital silence.</summary>
    public double PeakDbfs
    {
        get
        {
            var peak = Samples.Length == 0 ? 0 : Samples.Max(s => Math.Abs((int)s));
            return peak == 0 ? double.NegativeInfinity : 20 * Math.Log10(peak / 32768.0);
        }
    }
}

public abstract record Command;
public sealed record OpenMic(int Session) : Command;
public sealed record CloseMic(int Session, bool KeepAudio) : Command;
/// <summary>Start the stop tail; the shell calls <see cref="SessionStateMachine.TailElapsed"/> after <see cref="SessionStateMachine.TailSeconds"/>.</summary>
public sealed record StartTail(int Session) : Command;
public sealed record RegisterEsc : Command;
public sealed record UnregisterEsc : Command;
public sealed record PlaySound(Sound Sound) : Command;
public sealed record Transcribe(int Session, CapturedAudio Audio) : Command;
public sealed record Paste(string Text) : Command;
public sealed record Notify(string Message, NoteKind Kind) : Command;
public sealed record ExitApp : Command;

/// <summary>
/// The dictation session logic (the Mac's AppState), free of Win32 so every transition and race is unit-tested.
/// Only ever called on the UI thread. Each event returns the commands the shell must carry out, in order.
/// Async completions carry the session number they belong to; completions for an older session are ignored.
/// </summary>
public sealed class SessionStateMachine
{
    /// <summary>Bluetooth mic audio arrives late and people press stop while saying the last word (UAT lost ~0.6 s).</summary>
    public const double TailSeconds = 0.8;

    /// <summary>Recordings whose loudest moment is below this are room noise: Whisper turns them into "Thank you.".
    /// Measured: idle AirPods −66 to −84 dBFS, speech −10 to −16 dBFS.</summary>
    public const double SilenceGateDbfs = -45;

    enum Phase { Idle, Connecting, Recording, Tail, AwaitingAudio, Transcribing }

    static readonly Command[] None = [];

    Phase _phase = Phase.Idle;
    bool _modelLoaded, _quitPending;
    string? _modelError, _hotkeyError;

    public int Session { get; private set; }
    public string? LastTranscription { get; private set; }

    public AppState State =>
        _modelError is not null || _hotkeyError is not null ? AppState.Error
        : !_modelLoaded ? AppState.LoadingModel
        : _phase switch
        {
            Phase.Idle => AppState.Ready,
            Phase.Connecting => AppState.Connecting,
            Phase.Recording => AppState.Recording,
            Phase.Tail or Phase.AwaitingAudio => AppState.Stopping,
            _ => AppState.Transcribing,
        };

    public string StatusText => State switch
    {
        AppState.Error => _modelError ?? _hotkeyError!,
        AppState.LoadingModel => "Loading model…",
        AppState.Ready => "Ready",
        AppState.Connecting => "Connecting to microphone…",
        AppState.Recording => "Recording…",
        AppState.Stopping => "Finishing…",
        _ => _quitPending ? "Quitting after transcription…" : "Transcribing…",
    };

    bool LoadingModel => !_modelLoaded && _modelError is null;

    bool Current(int session, Phase phase) => session == Session && _phase == phase && _modelError is null;

    // ---- startup ----

    public IReadOnlyList<Command> ModelLoaded()
    {
        _modelLoaded = true;
        return _quitPending ? [new ExitApp()] : None;
    }

    public IReadOnlyList<Command> ModelFailed(string message)
    {
        _modelError = message;
        return _quitPending ? [new ExitApp()] : None;
    }

    public IReadOnlyList<Command> HotkeyUnavailable(string message)
    {
        _hotkeyError = message;
        return None;
    }

    // ---- user input ----

    public IReadOnlyList<Command> HotkeyPressed()
    {
        if (State is AppState.LoadingModel) return [new Notify("Still loading the model — try again in a moment", NoteKind.Warning)];
        if (State is AppState.Error) return None;
        switch (_phase)
        {
            case Phase.Idle:
                Session++;
                _phase = Phase.Connecting;
                return [new OpenMic(Session), new RegisterEsc()];
            case Phase.Connecting:
                return Cancel(closeMic: true);
            case Phase.Recording:
                _phase = Phase.Tail;
                return [new StartTail(Session)];
            default:
                return None; // tail, waiting for audio, transcribing: ignored (the Mac's isTranscribing guard)
        }
    }

    public IReadOnlyList<Command> EscPressed() => _phase switch
    {
        Phase.Connecting or Phase.Recording or Phase.Tail => Cancel(closeMic: true),
        Phase.AwaitingAudio => Cancel(closeMic: false), // mic already closing; its audio will arrive stale and be ignored
        _ => None,
    };

    IReadOnlyList<Command> Cancel(bool closeMic)
    {
        _phase = Phase.Idle;
        var cancel = new Command[] { new UnregisterEsc(), new PlaySound(Sound.Cancel), new Notify("Cancelled", NoteKind.Info) };
        return closeMic ? [new CloseMic(Session, false), .. cancel] : cancel;
    }

    // ---- microphone ----

    public IReadOnlyList<Command> MicOpenFailed(int session, string message)
    {
        if (!Current(session, Phase.Connecting)) return None;
        _phase = Phase.Idle;
        return [new UnregisterEsc(), new Notify(message, NoteKind.Warning)];
    }

    public IReadOnlyList<Command> MicReady(int session)
    {
        if (!Current(session, Phase.Connecting)) return None;
        _phase = Phase.Recording;
        return [new PlaySound(Sound.Start)];
    }

    public IReadOnlyList<Command> MicTimedOut(int session)
    {
        if (!Current(session, Phase.Connecting)) return None;
        _phase = Phase.Idle;
        return [new CloseMic(session, false), new UnregisterEsc(), new Notify("Mic not delivering audio — nothing recorded", NoteKind.Warning)];
    }

    public IReadOnlyList<Command> MicLost(int session)
    {
        if (Current(session, Phase.Connecting))
        {
            _phase = Phase.Idle;
            return [new CloseMic(session, false), new UnregisterEsc(), new Notify("Microphone disconnected — nothing recorded", NoteKind.Warning)];
        }
        if (Current(session, Phase.Recording) || Current(session, Phase.Tail))
            return CloseKeepingAudio(session); // transcribe what was captured
        return None;
    }

    public IReadOnlyList<Command> TailElapsed(int session) =>
        Current(session, Phase.Tail) ? CloseKeepingAudio(session) : None;

    IReadOnlyList<Command> CloseKeepingAudio(int session)
    {
        _phase = Phase.AwaitingAudio;
        return [new CloseMic(session, true), new PlaySound(Sound.Stop)]; // chime after the tail so it isn't recorded
    }

    public IReadOnlyList<Command> AudioCaptured(int session, CapturedAudio audio)
    {
        if (!Current(session, Phase.AwaitingAudio)) return None;
        if (audio.Seconds < 0.1)
        {
            _phase = Phase.Idle;
            return [new UnregisterEsc(), new Notify("Nothing recorded", NoteKind.Info)];
        }
        if (audio.IsAllZero)
        {
            _phase = Phase.Idle;
            return [new UnregisterEsc(), new Notify("Mic muted or blocked — check Settings › Privacy & security › Microphone", NoteKind.Warning)];
        }
        if (audio.PeakDbfs < SilenceGateDbfs)
        {
            _phase = Phase.Idle;
            return [new UnregisterEsc(), new Notify("Nothing heard", NoteKind.Info)];
        }
        _phase = Phase.Transcribing;
        return [new UnregisterEsc(), new Transcribe(session, audio)];
    }

    // ---- transcription ----

    public IReadOnlyList<Command> TranscriptionDone(int session, string text)
    {
        if (!Current(session, Phase.Transcribing)) return None;
        _phase = Phase.Idle;
        if (_quitPending) return [new ExitApp()];
        if (text.Length == 0) return [new Notify("Nothing heard", NoteKind.Info)];
        LastTranscription = text;
        return [new Paste(text)];
    }

    public IReadOnlyList<Command> TranscriptionFailed(int session, string message)
    {
        if (!Current(session, Phase.Transcribing)) return None;
        _phase = Phase.Idle;
        return _quitPending ? [new ExitApp()] : [new Notify($"Transcription failed: {message}", NoteKind.Warning)];
    }

    // ---- quitting ----

    public IReadOnlyList<Command> QuitRequested()
    {
        // Whisper must not be disposed while it is loading or transcribing, so those wait for completion.
        if (LoadingModel || _phase == Phase.Transcribing)
        {
            _quitPending = true;
            return None;
        }
        var phase = _phase;
        _phase = Phase.Idle;
        return phase switch
        {
            Phase.Connecting or Phase.Recording or Phase.Tail => [new CloseMic(Session, false), new UnregisterEsc(), new ExitApp()],
            Phase.AwaitingAudio => [new UnregisterEsc(), new ExitApp()],
            _ => [new ExitApp()],
        };
    }
}
