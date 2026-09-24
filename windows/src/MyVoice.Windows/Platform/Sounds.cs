using System.Media;
using MyVoice.Windows.Core;

namespace MyVoice.Windows.Platform;

/// <summary>Windows' own speech sounds stand in for the Mac's Tink / Pop / Funk.</summary>
sealed class Sounds : IDisposable
{
    readonly Dictionary<Sound, SoundPlayer> _players = [];

    public Sounds()
    {
        var media = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media");
        Add(Sound.Start, Path.Combine(media, "Speech On.wav"));
        Add(Sound.Stop, Path.Combine(media, "Speech Off.wav"));
        Add(Sound.Cancel, Path.Combine(media, "Speech Misrecognition.wav"));
    }

    void Add(Sound sound, string file)
    {
        if (!File.Exists(file)) return;
        var player = new SoundPlayer(file);
        player.Load();
        _players[sound] = player;
    }

    public void Play(Sound sound)
    {
        if (_players.TryGetValue(sound, out var player)) player.Play(); // asynchronous
        else SystemSounds.Asterisk.Play();
    }

    public void Dispose()
    {
        foreach (var player in _players.Values) player.Dispose();
    }
}
