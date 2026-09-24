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

## Windows version (`windows/`)

C# / .NET 10 WinForms tray app with the same pipeline and the same `~/.myvoice/` files. Independent of the Xcode
project (XcodeGen only reads `MyVoice/`). Work from `windows/` so its `global.json` applies.

```powershell
dotnet build                                                   # from windows/
dotnet test --filter-not-trait "Kind=Integration"              # unit tests (fast, safe any time)
dotnet test --culture tr-TR --filter-not-trait "Kind=Integration"   # same under a Turkish locale
dotnet test --filter-trait "Resource=GPU"                      # Whisper on the GPU (needs the model)
dotnet test --filter-trait "Resource=SendsKeys"                # presses keys system-wide — ONLY when Fırat is away
powershell -ExecutionPolicy Bypass -File build.ps1             # publish → %LOCALAPPDATA%\Programs\MyVoice + Start-menu shortcut
```

| Component | File | Mac counterpart |
|---|---|---|
| SessionStateMachine | `Core/SessionStateMachine.cs` | AppState — every decision, race and stale completion; pure, unit-tested |
| DictionaryReplacer | `Core/DictionaryReplacer.cs` | DictionaryReplacer.swift (Swift tests ported) |
| MicReadiness / CaptureBuffer | `Core/MicReadiness.cs` | — (Windows-only: cold Bluetooth mic) |
| Recorder / MicSource | `Platform/Recorder.cs`, `Platform/AudioSources.cs` | Recorder.swift — NAudio 3.1 WASAPI, 16 kHz mono |
| WhisperEngine | `Platform/WhisperEngine.cs` | WhisperEngine.swift — Whisper.net, Vulkan runtime |
| Paster | `Platform/Paster.cs` | Paster.swift — clipboard + SendInput Ctrl+V |
| GlobalHotkey | `Platform/GlobalHotkey.cs` | KeyboardShortcuts — RegisterHotKey |
| TrayApp | `TrayApp.cs` | MyVoiceApp.swift menu bar UI |

Windows rules (evidence in `docs/references/windows-port-spike.md`):
- **Never activate a window or inject Alt** — it ate the first Ctrl+V in the spike. Paste goes to whatever has focus.
- **"Speak now" only once audio flows** (`MicReadiness`: ≥90 % of real time over 0.5 s, 15 s timeout) — AirPods
  deliver nothing for up to ~11 s after the mic opens. **Keep recording 0.8 s after stop** (Bluetooth latency).
- **Silence gate:** recordings peaking below −45 dBFS never reach Whisper, which turns silence into "Thank you.".
- **Threading:** state lives on the UI thread; the audio callback only appends to locked buffers; completions come
  back via `SynchronizationContext.Post`, never `Invoke`.
- **Whisper.net:** `RuntimeLibraryOrder = [Vulkan, Cpu]` (no CUDA: it needs the CUDA 13 toolkit); never
  `WithStringPool()`; one processor per language; never dispose while transcribing.
- **Tests that press keys** must pass `onlyInto:` / `--target` so a moved focus types nothing. `--simulate` refuses to
  run without `--target`.
- **Smart App Control** (on in this PC) judges every new unsigned build by its hash and can block one; a blocked
  build stays blocked until the code changes. Details and options in the spike doc.
- **Logs** never contain transcript text unless `"debug": true` in `~/.myvoice/settings.json`.

## Reference Docs

- `docs/references/voiceink-lessons-learned.md` — Post-mortem from first failed attempt
- `docs/references/macos-dev-lessons-learned.md` — macOS dev checklist
- `docs/references/whisperkit-macos-research.md` — Speech engine comparison (SpeechAnalyzer vs WhisperKit vs whisper.cpp)
- `docs/references/windows-port-spike.md` — Windows spike: measurements, Bluetooth mic findings, test-safety and Smart App Control lessons
