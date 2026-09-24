using System.Text.Json;

namespace MyVoice.Windows.Core;

/// <summary>~/.myvoice/settings.json (the Mac keeps these in UserDefaults). Unknown or bad values fall back to defaults.</summary>
public sealed record AppSettings
{
    static readonly string[] Languages = ["auto", "en", "tr"];

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    /// <summary>Whisper language code: "auto", "en" or "tr" (the Mac's LanguagePreference raw values).</summary>
    public string Language { get; init; } = "auto";

    /// <summary>When true, the log also records transcript text and last_recording.wav is kept. Off by default.</summary>
    public bool Debug { get; init; }

    public static AppSettings Load(string path, out string? problem)
    {
        problem = null;
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
            return Languages.Contains(settings.Language) ? settings : settings with { Language = "auto" };
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            problem = $"settings.json could not be read ({e.Message}) — using defaults";
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, Json));
    }
}
