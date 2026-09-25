namespace MyVoice.Windows.Core;

/// <summary>The Mac's LanguagePreference: Whisper language code plus its menu name.</summary>
public sealed record LanguagePreference(string Code, string DisplayName)
{
    public static readonly LanguagePreference Auto = new("auto", "Auto-detect");
    public static readonly LanguagePreference English = new("en", "English");
    public static readonly LanguagePreference Turkish = new("tr", "Türkçe");

    public static readonly IReadOnlyList<LanguagePreference> All = [Auto, English, Turkish];

    /// <summary>Unknown or missing codes mean auto-detect, as on the Mac.</summary>
    public static LanguagePreference FromCode(string? code) => All.FirstOrDefault(p => p.Code == code) ?? Auto;
}
