import SwiftUI
import whisper

@main
struct VoiceInkApp: App {
    init() {
        // Verify whisper.cpp is linked
        var params = whisper_context_default_params()
        print("VoiceInk: whisper.cpp linked OK, flash_attn default: \(params.flash_attn)")
    }

    var body: some Scene {
        MenuBarExtra("VoiceInk", systemImage: "mic.fill") {
            Text("VoiceInk")
            Divider()
            Button("Quit") {
                NSApplication.shared.terminate(nil)
            }
        }
    }
}
