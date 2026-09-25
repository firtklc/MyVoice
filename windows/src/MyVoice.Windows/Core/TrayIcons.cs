namespace MyVoice.Windows.Core;

/// <summary>The tray icon's badge (the Mac swaps its menu-bar symbol per state).</summary>
public enum TrayIconKind { Ready, Connecting, Recording, Busy, Error }

public static class TrayIcons
{
    public static TrayIconKind For(AppState state) => state switch
    {
        AppState.Ready => TrayIconKind.Ready,
        AppState.Connecting => TrayIconKind.Connecting,
        AppState.Recording => TrayIconKind.Recording,
        AppState.Error => TrayIconKind.Error,
        _ => TrayIconKind.Busy, // loading the model, finishing (stop was pressed), transcribing
    };
}
