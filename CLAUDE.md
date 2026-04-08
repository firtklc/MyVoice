# MyVoice — Project Instructions

## What This Is

macOS menu bar dictation app. Hotkey → record → whisper.cpp transcribe → custom dictionary replace → auto-paste into active app.

## Build & Run

```bash
# Generate Xcode project (after any project.yml changes)
xcodegen generate

# Build
xcodebuild -project MyVoice.xcodeproj -scheme MyVoice -configuration Debug build

# Run tests
xcodebuild -project MyVoice.xcodeproj -scheme MyVoice -configuration Debug -destination "platform=macOS" test

# Launch
open $(find ~/Library/Developer/Xcode/DerivedData/MyVoice-*/Build/Products/Debug -name "MyVoice.app" -maxdepth 1)
```

## Architecture

Linear pipeline of 5 components wired in `AppState` (`MyVoice/MyVoiceApp.swift`):

```
KeyboardShortcuts → Recorder → WhisperEngine → DictionaryReplacer → Paster
```

| Component | File | Responsibility |
|---|---|---|
| KeyboardShortcuts | SPM dependency (sindresorhus/KeyboardShortcuts) | Global hotkey registration, configurable via recorder UI |
| HotkeyDisplayHelper | `MyVoice/HotkeyDisplayHelper.swift` | Formats hotkey display strings (⌘⇧D) |
| HotkeySettingsView | `MyVoice/HotkeySettingsView.swift` | SwiftUI settings window with shortcut recorder |
| Recorder | `MyVoice/Recorder.swift` | AVAudioRecorder, 16kHz mono WAV to temp file |
| WhisperEngine | `MyVoice/WhisperEngine.swift` | whisper.cpp C API wrapper, singleton, loads GGML model |
| DictionaryReplacer | `MyVoice/DictionaryReplacer.swift` | Word-boundary regex replacement, JSON config |
| Paster | `MyVoice/Paster.swift` | NSPasteboard + CGEvent Cmd+V |

## Key Paths

- **Model:** `~/.myvoice/models/ggml-large-v3-turbo.bin` (GGML format, NOT .pt)
- **Dictionary:** `~/.myvoice/dictionary.json`
- **whisper.cpp libs:** `libs/*.dylib` (pre-built, install names fixed with `install_name_tool`)
- **whisper.cpp headers:** `include/` (whisper.h, ggml.h, etc.)
- **Module map:** `MyVoice/whisper-bridge/module.modulemap`
- **XcodeGen config:** `project.yml`
- **Feature backlog:** `backlog.md` (git-ignored — lives locally only, not in repo)

## Important Rules

- **XcodeGen:** Always run `xcodegen generate` after modifying `project.yml` or adding/removing Swift files
- **Signing:** Uses Apple Developer certificate (Team ID: M9UJ296PUQ, automatic signing) — do NOT change to ad-hoc (`-`) or permissions reset every rebuild
- **App Sandbox:** Disabled — required for CGEvent paste
- **Swift 6:** Do NOT use `DispatchQueue.main.asyncAfter` — use `Task { @MainActor in }` instead
- **whisper.cpp threading:** Never call `whisper_full()` concurrently on the same context — `isTranscribing` flag guards this
- **dylib install names:** Must match filenames exactly (no version suffixes). Use `install_name_tool -id @rpath/libname.dylib` if adding new libs
- **Launch:** Always via `open MyVoice.app`, never run the binary directly (macOS permission checks need bundle identity)

## Testing

- 13 unit tests for DictionaryReplacer (Swift Testing framework)
- 9 unit tests for HotkeyDisplayHelper (Swift Testing framework)
- 6 unit tests for CursorOverlay/CoordinateConverter (Swift Testing framework)
- System components (Recorder, Paster, WhisperEngine) tested via manual smoke tests
- TDD required for all pure logic components

## Reference Docs

- `docs/references/voiceink-lessons-learned.md` — Post-mortem from first failed attempt
- `docs/references/macos-dev-lessons-learned.md` — macOS dev checklist
- `docs/references/whisperkit-macos-research.md` — Speech engine comparison (SpeechAnalyzer vs WhisperKit vs whisper.cpp)
