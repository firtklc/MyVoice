using MyVoice.Windows.Core;

namespace MyVoice.Windows.Platform;

/// <summary>
/// The dictation shortcut's registration. The Settings field suspends it while recording a new one (so pressing the
/// current shortcut there is recorded instead of starting a dictation), then changes it or resumes the old one.
/// </summary>
sealed class DictationHotkey(GlobalHotkey hotkeys, int id)
{
    public Hotkey Current { get; private set; } = Hotkey.Default;
    public bool Registered { get; private set; }

    /// <summary>Registers <paramref name="hotkey"/> at startup. It stays <see cref="Current"/> even when taken, so
    /// Settings shows what to change and <see cref="Resume"/> can retry.</summary>
    public bool Start(Hotkey hotkey, out int error)
    {
        Current = hotkey;
        return Resume(out error);
    }

    public void Suspend()
    {
        if (!Registered) return;
        hotkeys.Unregister(id);
        Registered = false;
    }

    public bool Resume(out int error)
    {
        error = 0;
        if (!Registered) Registered = hotkeys.Register(id, Current.Modifiers, Current.Key, out error);
        return Registered;
    }

    /// <summary>Makes <paramref name="next"/> the shortcut. When Windows refuses it (1409: another app holds it), the
    /// previous one stays — registered again if it was registered, still suspended if it was suspended.</summary>
    public bool TryChange(Hotkey next, out int error)
    {
        var wasRegistered = Registered;
        Suspend(); // RegisterHotKey keeps both combinations when an id is registered twice
        if (hotkeys.Register(id, next.Modifiers, next.Key, out error))
        {
            Current = next;
            Registered = true;
            return true;
        }
        if (wasRegistered) Resume(out _);
        return false;
    }
}
