import SwiftUI
import os

@main
struct MyVoiceApp: App {
    @StateObject private var appState = AppState()

    var body: some Scene {
        MenuBarExtra {
            MenuBarView(appState: appState)
        } label: {
            HStack(spacing: 4) {
                Image(systemName: appState.menuBarIcon)
                if appState.menuBarLabel != nil {
                    Text(appState.menuBarLabel!)
                        .font(.caption)
                }
            }
        }
    }
}

struct MenuBarView: View {
    @ObservedObject var appState: AppState

    var body: some View {
        Text(appState.statusText)
        Divider()
        if let last = appState.lastTranscription {
            let truncated = last.count > 80 ? String(last.prefix(80)) + "..." : last
            Text("Last: \(truncated)")
                .foregroundStyle(.secondary)
                .font(.caption)
            Divider()
        }
        Text("Cmd+Shift+D to dictate")
            .foregroundStyle(.secondary)
        Divider()
        Button("Quit") {
            NSApplication.shared.terminate(nil)
        }
        .keyboardShortcut("q")
    }
}

@MainActor
final class AppState: ObservableObject {
    @Published var menuBarIcon = "mic.fill"
    @Published var menuBarLabel: String?
    @Published var statusText = "Ready"
    @Published var lastTranscription: String?
    @Published var recordingDuration: Int = 0

    private let logger = Logger(subsystem: "com.firat.MyVoice", category: "AppState")
    private var whisperEngine: WhisperEngine?
    private let recorder = Recorder()
    private let paster = Paster()
    private var replacer: DictionaryReplacer?
    private var hotkeyManager: HotkeyManager?
    private var isTranscribing = false
    private var durationTimer: Timer?

    init() {
        loadWhisperEngine()
        loadDictionary()
        setupHotkey()
    }

    private func loadWhisperEngine() {
        let modelPath = NSString("~/.myvoice/models/ggml-large-v3-turbo.bin").expandingTildeInPath
        do {
            whisperEngine = try WhisperEngine(modelPath: modelPath)
            logger.info("Model loaded")
        } catch {
            logger.error("Failed to load model: \(error.localizedDescription)")
            statusText = "Error: Model not found"
            menuBarIcon = "exclamationmark.triangle.fill"
        }
    }

    private func loadDictionary() {
        let dictPath = NSString("~/.myvoice/dictionary.json").expandingTildeInPath
        let url = URL(fileURLWithPath: dictPath)
        do {
            replacer = try DictionaryReplacer(jsonFileURL: url)
            logger.info("Dictionary loaded")
        } catch {
            logger.info("No dictionary found, continuing without replacements")
            replacer = DictionaryReplacer(dictionary: [:])
        }
    }

    private func setupHotkey() {
        hotkeyManager = HotkeyManager { [weak self] in
            Task { @MainActor in
                self?.toggleRecording()
            }
        }
    }

    private func toggleRecording() {
        if recorder.isRecording {
            stopAndTranscribe()
        } else {
            startRecording()
        }
    }

    private func startRecording() {
        guard whisperEngine != nil else {
            logger.error("Cannot record: no model loaded")
            return
        }
        guard !isTranscribing else {
            logger.info("Still transcribing, ignoring hotkey")
            return
        }
        do {
            let _ = try recorder.startRecording()
            NSSound(named: "Tink")?.play()
            menuBarIcon = "mic.fill.badge.plus"
            recordingDuration = 0
            menuBarLabel = "0s"
            statusText = "Recording... 0s"
            durationTimer = Timer.scheduledTimer(withTimeInterval: 1.0, repeats: true) { [weak self] _ in
                Task { @MainActor in
                    guard let self, self.recorder.isRecording else { return }
                    self.recordingDuration += 1
                    self.menuBarLabel = "\(self.recordingDuration)s"
                    self.statusText = "Recording... \(self.recordingDuration)s"
                }
            }
            logger.info("Recording started")
        } catch {
            logger.error("Failed to start recording: \(error.localizedDescription)")
            statusText = "Mic error — check permissions"
            menuBarIcon = "exclamationmark.triangle.fill"
        }
    }

    private func stopAndTranscribe() {
        guard let url = recorder.stopRecording() else { return }
        durationTimer?.invalidate()
        durationTimer = nil
        menuBarLabel = nil
        NSSound(named: "Pop")?.play()
        isTranscribing = true
        menuBarIcon = "ellipsis.circle.fill"
        statusText = "Transcribing..."
        logger.info("Recording stopped, transcribing...")

        Task {
            do {
                guard let engine = whisperEngine else { return }
                let rawText = try engine.transcribe(wavFileURL: url)
                logger.info("Raw transcription: \(rawText)")

                let fixedText = replacer?.replace(rawText) ?? rawText
                logger.info("After dictionary: \(fixedText)")

                if !fixedText.isEmpty {
                    paster.paste(fixedText)
                }

                await MainActor.run {
                    menuBarIcon = "mic.fill"
                    statusText = "Ready"
                    lastTranscription = fixedText
                    isTranscribing = false
                }
            } catch {
                logger.error("Transcription failed: \(error.localizedDescription)")
                await MainActor.run {
                    menuBarIcon = "mic.fill"
                    statusText = "Error"
                    isTranscribing = false
                }
            }

            recorder.cleanup()
        }
    }
}
