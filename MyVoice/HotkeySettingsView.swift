import SwiftUI
import KeyboardShortcuts

struct HotkeySettingsView: View {
    let appState: AppState

    var body: some View {
        VStack(spacing: 16) {
            Text("Set Dictation Hotkey")
                .font(.headline)

            HStack {
                Text("Shortcut:")
                KeyboardShortcuts.Recorder(for: .toggleRecording) { _ in
                    Task { @MainActor in
                        appState.updateHotkeyHintText()
                    }
                }
            }

            Text("Press the recorder field, then type your shortcut.")
                .font(.caption)
                .foregroundStyle(.secondary)
        }
        .padding(24)
    }
}
