namespace MyVoice.Windows.Platform;

/// <summary>Everything lives in %USERPROFILE%\.myvoice, the same layout as ~/.myvoice on the Mac.
/// MYVOICE_HOME overrides the folder (tests, or trying a missing-model start without renaming files).</summary>
static class AppPaths
{
    public static string Root =>
        Environment.GetEnvironmentVariable("MYVOICE_HOME") is { Length: > 0 } home
            ? home
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".myvoice");

    public static string Model => Path.Combine(Root, "models", "ggml-large-v3-turbo.bin");
    public static string Dictionary => Path.Combine(Root, "dictionary.json");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string Log => Path.Combine(Root, "logs", "myvoice.log");
    public static string LastRecording => Path.Combine(Root, "last_recording.wav");
}
