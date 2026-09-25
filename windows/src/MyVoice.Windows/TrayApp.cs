using System.Diagnostics;
using System.Globalization;
using MyVoice.Windows.Core;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows;

/// <summary>Command-line options for dev testing without speaking: --simulate feeds a WAV through the recorder
/// instead of the mic, dictates it once into the window titled --target (required, and nowhere else), then quits.
/// --stall imitates a cold Bluetooth headset.</summary>
sealed record Options(string? SimulateWav, string? TargetTitle, double StallSeconds, string? Error)
{
    public static Options Parse(string[] args)
    {
        string? wav = null, target = null;
        double stall = 0;
        for (var i = 0; i < args.Length; i++)
        {
            var name = args[i];
            if (name is not ("--simulate" or "--target" or "--stall")) return Fail($"unknown argument {name}");
            if (i + 1 >= args.Length) return Fail($"{name} needs a value");
            var value = args[++i];
            switch (name)
            {
                case "--simulate": wav = value; break;
                case "--target": target = value; break;
                case "--stall" when !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out stall):
                    return Fail($"--stall needs seconds like 2.5, not '{value}'");
            }
        }
        if (wav is not null && target is null)
            return Fail("--simulate needs --target \"<window title>\": a simulated dictation never types into an arbitrary window");
        return new(wav, target, stall, null);
    }

    static Options Fail(string error) => new(null, null, 0, error);
}

/// <summary>
/// The tray app (the Mac's MyVoiceApp + AppState): wires hotkey → recorder → Whisper → dictionary → paste through
/// <see cref="SessionStateMachine"/>, which owns every decision. Everything here runs on the UI thread.
/// </summary>
sealed class TrayApp : ApplicationContext
{
    const int DictateId = 1, EscId = 2;

    readonly Options _options;
    readonly SessionStateMachine _machine = new();
    readonly DictionaryReplacer _dictionary;
    readonly Recorder _recorder;
    readonly Paster _paster = new();
    readonly Sounds _sounds = new();
    readonly GlobalHotkey _hotkeys = new();
    readonly DictationHotkey _dictationHotkey;
    readonly RecordingOverlay _overlay = new(CaretLocator.Locate);
    readonly TrayIconSet _icons = new(SystemInformation.SmallIconSize);
    readonly NotifyIcon _tray;
    readonly ToolStripMenuItem _statusItem = new() { Enabled = false };
    readonly ToolStripMenuItem _lastItem = new() { Enabled = false, Visible = false };
    readonly ToolStripMenuItem _hintItem = new() { Enabled = false };
    readonly ToolStripMenuItem _languageMenu = new("Language");
    readonly ToolStripMenuItem _settingsItem = new("Settings…");
    readonly System.Windows.Forms.Timer _timer = new() { Interval = 50 };
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly CommandQueue _commands;

    AppSettings _settings;
    WhisperEngine? _engine;
    WavFileSource? _simulatedMic;
    SettingsForm? _settingsForm;
    TrayIconKind? _shownIcon;
    bool _simulationStarted, _simulationStopped, _simulationQuitting, _exitStarted;
    string? _note;
    double _noteUntil, _recordingSince;
    float _meterMax, _shownMax;   // DEBUG: highest mic level the overlay was given / showed this recording
    int _meterTicks, _meterLive;

    bool Simulating => _options.SimulateWav is not null;
    double Now => _clock.Elapsed.TotalSeconds;

    public TrayApp(Options options)
    {
        _options = options;
        _commands = new CommandQueue(Handle);
        var ui = SynchronizationContext.Current!;
        Log.Info($"MyVoice {Application.ProductVersion} starting on {Environment.OSVersion}{(Simulating ? $" — simulating with {options.SimulateWav}" : "")}");

        _settings = AppSettings.Load(AppPaths.Settings, out var settingsProblem);
        if (settingsProblem is not null) Log.Warn(settingsProblem);
        _dictionary = DictionaryReplacer.Load(AppPaths.Dictionary, out var dictionaryStatus);
        Log.Info(dictionaryStatus);

        _tray = new NotifyIcon { Icon = _icons[TrayIconKind.Busy], Visible = true, ContextMenuStrip = BuildMenu() };

        _recorder = new Recorder(CreateMic, action => ui.Post(_ => action(), null), () => Now);
        _recorder.Ready += OnMicReady;
        _recorder.TimedOut += session => Execute(_machine.MicTimedOut(session));
        _recorder.Lost += session => Execute(_machine.MicLost(session));
        _recorder.Captured += OnAudioCaptured;

        _hotkeys.Pressed += id => Execute(id == EscId ? _machine.EscPressed() : _machine.HotkeyPressed());
        _dictationHotkey = new DictationHotkey(_hotkeys, DictateId);
        if (!Simulating && !_dictationHotkey.Start(_settings.ParseHotkey(), out var error))
        {
            HotkeyTaken(error);
            _tray.ShowBalloonTip(8000, "MyVoice", $"{ShortcutName} is already used by another app, so dictation can't start. Pick another shortcut in Settings (MyVoice tray menu).", ToolTipIcon.Warning);
        }

        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
        UpdateUi();
        Forget(LoadModelAsync(), "model load");
    }

    IAudioSource CreateMic()
    {
        if (!Simulating) return new MicSource();
        _simulatedMic = new WavFileSource(WavFile.Read(_options.SimulateWav!), _options.StallSeconds);
        return _simulatedMic;
    }

    async Task LoadModelAsync()
    {
        try
        {
            var watch = Stopwatch.StartNew();
            _engine = await Task.Run(() => WhisperEngine.Load(AppPaths.Model));
            Log.Info(Invariant($"model loaded in {watch.ElapsedMilliseconds} ms, runtime {_engine.Runtime}"));
            watch.Restart();
            var engine = _engine;
            await Task.Run(() => engine.WarmUpAsync(_settings.Language));
            Log.Info(Invariant($"warm-up transcription {watch.ElapsedMilliseconds} ms"));
            if (_engine.Runtime != "Vulkan")
                _tray.ShowBalloonTip(8000, "MyVoice", "The GPU isn't available, so MyVoice runs on the CPU — transcription will take several seconds.", ToolTipIcon.Warning);
            Execute(_machine.ModelLoaded());
        }
        catch (Exception e)
        {
            Log.Error("model load failed", e);
            var message = e is FileNotFoundException ? $"Model not found: {AppPaths.Model}" : $"Model failed to load: {e.Message}";
            Execute(_machine.ModelFailed(message));
            _tray.ShowBalloonTip(10000, "MyVoice", message, ToolTipIcon.Error);
        }
    }

    // ---- commands from the state machine ----

    /// <summary>Runs the state machine's commands in order; commands raised while handling one queue behind the rest.</summary>
    void Execute(IReadOnlyList<Command> commands)
    {
        _commands.Run(commands);
        UpdateUi();
    }

    void Handle(Command command)
    {
        switch (command)
        {
            case OpenMic(var session):
                try
                {
                    _recorder.Open(session);
                    Log.Info($"session {session}: mic opened — {_recorder.DeviceName}");
                }
                catch (MicOpenException e)
                {
                    Log.Warn($"session {session}: {e.Message} ({e.InnerException?.Message})");
                    Execute(_machine.MicOpenFailed(session, e.Message)); // queued behind this list's RegisterEsc
                }
                break;
            case CloseMic(var session, var keep):
                _recorder.Close(session, keep);
                break;
            case StartTail(var session):
                Forget(TailAsync(session), "stop tail");
                break;
            case RegisterEsc:
                if (!Simulating && !_hotkeys.Register(EscId, HotkeyModifiers.None, Keys.Escape, out var error))
                    Log.Warn($"Esc could not be registered (error {error}) — cancel from the tray instead");
                break;
            case UnregisterEsc:
                _hotkeys.Unregister(EscId);
                break;
            case PlaySound(var sound):
                _sounds.Play(sound);
                break;
            case Transcribe(var session, var audio):
                Forget(TranscribeAsync(session, audio), "transcription");
                break;
            case Paste(var text):
                Forget(PasteAsync(text), "paste");
                break;
            case Notify(var message, var kind):
                ShowNote(message, kind);
                break;
            case ShowOverlay(var kind):
                _overlay.Show(kind);
                break;
            case HideOverlay:
                _overlay.Hide();
                break;
            case ExitApp:
                Forget(ExitAsync(), "exit");
                break;
        }
    }

    /// <summary>Starts a background step whose failure must at least reach the log (an un-awaited Task swallows it).</summary>
    static async void Forget(Task task, string what)
    {
        try { await task; }
        catch (Exception e) { Log.Error($"{what} failed", e); }
    }

    async Task TailAsync(int session)
    {
        await Task.Delay(TimeSpan.FromSeconds(SessionStateMachine.TailSeconds));
        Execute(_machine.TailElapsed(session));
    }

    void OnMicReady(int session)
    {
        _recordingSince = Now;
        _meterMax = _shownMax = 0;
        _meterTicks = _meterLive = 0;
        Log.Info($"session {session}: speak now");
        Execute(_machine.MicReady(session));
        _simulatedMic?.BeginSpeech(); // the simulated user starts talking after the chime
    }

    void OnAudioCaptured(int session, CapturedAudio audio, RecordingStats stats)
    {
        Log.Info(Invariant($"session {session}: '{stats.Device}', ready after {stats.ReadyWaitSeconds:F1} s, captured {stats.CapturedSeconds:F1} s of {stats.WallSeconds:F1} s, peak {stats.PeakDbfs:F0} dBFS"));
        Log.Info(Invariant($"session {session}: DEBUG meter: {_meterTicks} ticks, {_meterLive} above -50 dBFS, max mic level {_meterMax:F3}, max shown {_shownMax:F3}"));
        if (_settings.Debug) WavFile.Write(AppPaths.LastRecording, audio.Samples);
        Execute(_machine.AudioCaptured(session, audio));
    }

    async Task TranscribeAsync(int session, CapturedAudio audio)
    {
        try
        {
            var engine = _engine!;
            var language = _settings.Language; // read here, on the UI thread, where Settings changes it
            var watch = Stopwatch.StartNew();
            var result = await Task.Run(() => engine.TranscribeAsync(WhisperEngine.ToFloat(audio.Samples), language));
            var text = _dictionary.Replace(result.Text);
            Log.Info(Invariant($"session {session}: {audio.Seconds:F1} s transcribed in {watch.ElapsedMilliseconds} ms ({engine.Runtime}, language {result.Language}), {text.Length} characters"));
            if (_settings.Debug) Log.Info($"session {session}: \"{result.Text}\" → \"{text}\"");
            Execute(_machine.TranscriptionDone(session, text));
        }
        catch (Exception e)
        {
            Log.Error($"session {session}: transcription failed", e);
            Execute(_machine.TranscriptionFailed(session, e.Message));
        }
    }

    async Task PasteAsync(string text)
    {
        try
        {
            var outcome = Simulating
                ? await _paster.PasteAsync(text, onlyInto: NativeMethods.FindWindow(null, _options.TargetTitle!))
                : await _paster.PasteAsync(text);
            Log.Info($"paste into {outcome.Target}: {(outcome.Pasted ? "done" : outcome.Reason)}");
            if (!outcome.Pasted) ShowNote($"Copied — press Ctrl+V to paste ({outcome.Reason})", NoteKind.Warning);
            if (Simulating && !outcome.Pasted) Environment.ExitCode = 2;
        }
        catch (Exception e)
        {
            Log.Error("paste failed", e);
            ShowNote("Paste failed — the clipboard was busy", NoteKind.Warning);
            if (Simulating) Environment.ExitCode = 2;
        }
        UpdateUi();
    }

    // ---- UI ----

    void OnTick()
    {
        _recorder.Poll();
        var level = _recorder.TakeLevel();
        _overlay.Tick(level);
        if (_machine.State == AppState.Recording)
        {
            _meterTicks++;
            if (level > 0.0032f) _meterLive++;
            _meterMax = Math.Max(_meterMax, level);
            _shownMax = Math.Max(_shownMax, _overlay.ShownLevel);
        }
        if (Simulating) DriveSimulation();
        if (_note is not null && Now > _noteUntil) _note = null;
        UpdateUi();
    }

    void DriveSimulation()
    {
        if (_machine.State == AppState.Ready && !_simulationStarted)
        {
            _simulationStarted = true;
            Execute(_machine.HotkeyPressed());
        }
        else if (_machine.State == AppState.Recording && _simulatedMic?.FinishedSpeech == true && !_simulationStopped)
        {
            _simulationStopped = true;
            Execute(_machine.HotkeyPressed());
        }
        else if (_simulationStarted && _machine.State is AppState.Ready or AppState.Error && !_simulationQuitting)
        {
            // Done — or the session ended early (mic timeout, open failure): quit either way, never hang.
            _simulationQuitting = true;
            if (!_simulationStopped) Environment.ExitCode = 3;
            Forget(QuitSoonAsync(), "simulated quit");
        }
    }

    async Task QuitSoonAsync()
    {
        await Task.Delay(1500); // let the paste land
        Execute(_machine.QuitRequested());
    }

    void ShowNote(string message, NoteKind kind)
    {
        _note = message;
        _noteUntil = Now + (kind == NoteKind.Warning ? 6 : 1.5); // the Mac shows "Cancelled" for 1.5 s
        if (kind == NoteKind.Warning) _tray.ShowBalloonTip(5000, "MyVoice", message, ToolTipIcon.Warning);
        Log.Info($"note: {message}");
    }

    void UpdateUi()
    {
        var status = _note ?? (_machine.State == AppState.Recording
            ? $"Recording… {(int)(Now - _recordingSince)}s"
            : _machine.StatusText);
        _statusItem.Text = Escape(status);
        if (_machine.LastTranscription is { } last)
        {
            _lastItem.Text = Escape("Last: " + (last.Length > 80 ? last[..80] + "..." : last)); // the Mac's 80-character cut
            _lastItem.Visible = true;
        }
        var tooltip = $"MyVoice — {status}";
        _tray.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip; // NotifyIcon's limit

        var icon = TrayIcons.For(_machine.State);
        if (icon != _shownIcon) { _tray.Icon = _icons[icon]; _shownIcon = icon; } // only on change: each set is a shell call
        _hintItem.Text = Escape(_dictationHotkey.Current.MenuHint(KeyLabels.ForCurrentLayout));
        var canChange = _machine.CanChangeSettings;
        foreach (ToolStripMenuItem item in _languageMenu.DropDownItems)
        {
            item.Checked = (string)item.Tag! == _settings.Language;
            item.Enabled = canChange;
        }
        _settingsItem.Enabled = canChange || _settingsForm is not null;
        _settingsForm?.ShowState(_dictationHotkey.Current, _settings.Language, canChange);
    }

    string ShortcutName => _dictationHotkey.Current.Display(KeyLabels.ForCurrentLayout);

    static string Escape(string menuText) => menuText.Replace("&", "&&"); // '&' would become a keyboard accelerator

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_lastItem);
        menu.Items.Add(_hintItem);
        menu.Items.Add(new ToolStripSeparator());
        foreach (var preference in LanguagePreference.All)
            _languageMenu.DropDownItems.Add(new ToolStripMenuItem(preference.DisplayName, null, (_, _) => SetLanguage(preference.Code)) { Tag = preference.Code });
        menu.Items.Add(_languageMenu);
        _settingsItem.Click += (_, _) => OpenSettings();
        menu.Items.Add(_settingsItem);
        menu.Items.Add("Open log folder", null, (_, _) => Process.Start("explorer.exe", Path.GetDirectoryName(AppPaths.Log)!));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Execute(_machine.QuitRequested()));
        return menu;
    }

    // ---- settings ----

    void OpenSettings()
    {
        if (_settingsForm is not null) { _settingsForm.Activate(); return; }
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("MyVoice.Windows.Assets.MyVoice.ico")!;
        var form = new SettingsForm(new SettingsActions(BeginShortcutCapture, ApplyShortcut, EndShortcutCapture, SetLanguage), new Icon(stream));
        form.FormClosed += (_, _) => { form.Dispose(); _settingsForm = null; UpdateUi(); };
        _settingsForm = form;
        UpdateUi();
        form.Show();
        form.Activate();
    }

    bool BeginShortcutCapture()
    {
        if (!_machine.CanChangeSettings) return false;
        _dictationHotkey.Suspend();
        return true;
    }

    string? ApplyShortcut(Hotkey hotkey)
    {
        var name = hotkey.Display(KeyLabels.ForCurrentLayout);
        if (!_dictationHotkey.TryChange(hotkey, out var error))
        {
            Log.Warn($"{hotkey} could not be registered (error {error})");
            return error == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"{name} is already used by another app — try another shortcut."
                : $"Windows didn't accept {name} (error {error}) — try another shortcut.";
        }
        Log.Info($"shortcut changed to {hotkey}");
        SaveSettings(_settings with { Hotkey = hotkey.ToString() });
        Execute(_machine.HotkeyAvailable());
        return null;
    }

    void EndShortcutCapture()
    {
        if (_dictationHotkey.Resume(out var error)) Execute(_machine.HotkeyAvailable());
        else HotkeyTaken(error);
    }

    void HotkeyTaken(int error)
    {
        Log.Warn($"{_dictationHotkey.Current} could not be registered (error {error})");
        Execute(_machine.HotkeyUnavailable($"{ShortcutName} is used by another app — pick another shortcut in Settings"));
    }

    void SetLanguage(string code)
    {
        if (!_machine.CanChangeSettings || code == _settings.Language) return;
        Log.Info($"language changed to {code}");
        SaveSettings(_settings with { Language = code });
    }

    void SaveSettings(AppSettings settings)
    {
        _settings = settings;
        try { settings.Save(AppPaths.Settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"settings.json could not be saved: {e.Message}");
            ShowNote("Settings couldn't be saved — the change lasts until MyVoice quits", NoteKind.Warning);
        }
        UpdateUi();
    }

    async Task ExitAsync()
    {
        if (_exitStarted) return; // a second Quit must not dispose Whisper's native state twice
        _exitStarted = true;
        try
        {
            _timer.Stop();
            _settingsForm?.Close();
            _overlay.Dispose();
            _hotkeys.Dispose();
            _recorder.Dispose();
            _sounds.Dispose();
            if (_engine is not null) await _engine.DisposeAsync(); // safe: the state machine never exits mid-transcription
        }
        finally
        {
            _tray.Visible = false;
            _tray.Dispose();
            _icons.Dispose();
            Log.Info("MyVoice exited");
            ExitThread();
        }
    }

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
