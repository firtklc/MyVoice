# MyVoice — Feature Backlog

## Easy

- [x] **Sound on start/stop recording** — Play audio feedback when dictation starts and stops (`NSSound`)
- [x] **Last transcription in menu** — Show the most recent dictated text in the menu bar dropdown
- [x] **Recording duration** — Display elapsed time next to menu bar icon while recording ("3s")
- [ ] **Auto-launch at login** — Add MyVoice to Login Items via `SMAppService`

## Medium

- [ ] **Configurable hotkey** — Let user pick a different keyboard shortcut (stored in JSON config)

## Hard

- [ ] **Blue overlay near cursor** — Floating indicator near the active text field using Accessibility API to detect cursor position. Works in most apps but fragile in edge cases.
- [ ] **Live transcription preview** — Streaming whisper.cpp output in a floating window during recording. Requires switching from AVAudioRecorder to AVAudioEngine for real-time buffer access.

## Post-MVP (from spec)

- [ ] **Model selection** — Switch between base.en (speed) and large-v3-turbo (accuracy)
- [x] **Language switching** — Auto-detection enabled, supports 99 languages including Turkish and Russian
- [ ] **Dictionary hot-reload** — FSEvents watcher to reload dictionary.json on save without restarting
- [ ] **Settings UI** — Native SwiftUI settings panel for dictionary management
- [ ] **Audio device picker** — Select input microphone instead of system default
- [ ] **Clipboard save/restore** — Save clipboard before paste, restore after (with proper timing)
