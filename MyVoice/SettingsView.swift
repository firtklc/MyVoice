import SwiftUI
import KeyboardShortcuts

struct SettingsView: View {
    @ObservedObject var appState: AppState

    var body: some View {
        VStack(alignment: .leading, spacing: 20) {
            VStack(alignment: .leading, spacing: 8) {
                Text("Dictation Hotkey")
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

            Divider()

            VStack(alignment: .leading, spacing: 8) {
                Text("Language")
                    .font(.headline)
                Picker("", selection: $appState.languagePreference) {
                    ForEach(LanguagePreference.allCases) { pref in
                        Text(pref.displayName).tag(pref)
                    }
                }
                .pickerStyle(.segmented)
                .labelsHidden()
                Text("Auto-detect picks the language from your audio. Pick a specific language if auto-detect guesses wrong.")
                    .font(.caption)
                    .foregroundStyle(.secondary)
            }
        }
        .padding(24)
        .frame(width: 380)
    }
}
