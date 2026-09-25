using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVoice.Windows.Core;

/// <summary>~/.myvoice/settings.json (the Mac keeps these in UserDefaults). Unknown or bad values fall back to defaults.</summary>
public sealed record AppSettings
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Whisper language code: "auto", "en" or "tr" (the Mac's LanguagePreference raw values).</summary>
    public string Language { get; init; } = "auto";

    /// <summary>The dictation shortcut as settings text ("Ctrl+Shift+D"). Load guarantees it parses and is allowed.</summary>
    public string Hotkey { get; init; } = Core.Hotkey.Default.ToString();

    /// <summary>When true, the log also records transcript text and last_recording.wav is kept. Off by default.</summary>
    public bool Debug { get; init; }

    /// <summary>Keys this version doesn't know, written back unchanged: MyVoice rewrites the file when settings change.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Other { get; init; }

    public static AppSettings Load(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
            settings = settings with { Language = LanguagePreference.FromCode(settings.Language).Code };
            if (Allowed(settings.Hotkey) is { } hotkey) return settings with { Hotkey = hotkey.ToString() };
            problem = $"hotkey '{settings.Hotkey}' in settings.json can't be used — using {Core.Hotkey.Default}";
            return settings with { Hotkey = Core.Hotkey.Default.ToString() };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = $"settings.json could not be read ({e.Message}) — using defaults";
            return new AppSettings();
        }
    }

    public Core.Hotkey ParseHotkey() => Allowed(Hotkey) ?? Core.Hotkey.Default;

    static Core.Hotkey? Allowed(string? text) => Core.Hotkey.TryParse(text, out var hotkey) && hotkey.Problem is null ? hotkey : null;

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
