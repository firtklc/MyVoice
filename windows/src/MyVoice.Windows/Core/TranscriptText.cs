using System.Text.RegularExpressions;

namespace MyVoice.Windows.Core;

/// <summary>Turns Whisper segments into the one line that gets pasted.</summary>
public static class TranscriptText
{
    static readonly Regex Whitespace = new(@"\s+");

    /// <summary>Concatenates segments (Whisper starts each with a space) on one line; whisper.cpp's
    /// "[BLANK_AUDIO]" marker for silence is dropped so it is never pasted.</summary>
    public static string Join(IEnumerable<string> segments) =>
        Whitespace.Replace(string.Concat(segments).Replace("[BLANK_AUDIO]", " "), " ").Trim();
}
