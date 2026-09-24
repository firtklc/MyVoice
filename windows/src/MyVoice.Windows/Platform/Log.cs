namespace MyVoice.Windows.Platform;

/// <summary>Plain-text log at ~/.myvoice/logs/myvoice.log, rolled to myvoice.1.log at 1 MB. Thread-safe.
/// Never pass transcript text here unless settings.json has "debug": true.</summary>
static class Log
{
    const long MaxBytes = 1_000_000;
    static readonly object Gate = new();

    public static string Path { get; set; } = AppPaths.Log;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? e = null) => Write("ERROR", e is null ? message : $"{message}: {e}");

    static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes)
                    File.Move(Path, System.IO.Path.ChangeExtension(Path, ".1.log"), overwrite: true);
                File.AppendAllText(Path, line);
            }
            catch (Exception)
            {
                // Logging must never break dictation — it also runs inside whisper.cpp's native log callback.
            }
        }
    }
}
