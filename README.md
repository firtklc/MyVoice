# MyVoice

A personal macOS menu bar dictation app. Press a hotkey, speak, and transcribed text appears in the active app — with custom word replacements applied automatically.

## Why

macOS Dictation and MacWhisper Pro both lack post-transcription custom dictionary support. MyVoice owns the full pipeline, so word replacements (e.g., "cloud" → "Claude") happen between transcription and paste.

## How It Works

```
Cmd+Shift+D → Record → whisper.cpp → Dictionary Replace → Auto-Paste
```

- **Speech engine:** whisper.cpp (large-v3-turbo, GGML format) — local, offline, no data leaves the machine
- **Custom dictionary:** `~/.myvoice/dictionary.json` — word-boundary regex, case-insensitive
- **Auto-paste:** Clipboard + CGEvent Cmd+V into the active app
- **Menu bar:** Shows recording/transcribing state via icon changes

## Setup

1. Place a GGML whisper model at `~/.myvoice/models/ggml-large-v3-turbo.bin`
2. Create your dictionary at `~/.myvoice/dictionary.json`:
   ```json
   {
     "cloud": "Claude",
     "Cloud": "Claude"
   }
   ```
3. Build and run from Xcode
4. Grant Microphone and Accessibility permissions when prompted

## Tech Stack

Swift 6 | SwiftUI | whisper.cpp | KeyboardShortcuts | AVFoundation | XcodeGen

## Requirements

- macOS 14+
- Apple Silicon

## Windows

A native Windows version lives in [`windows/`](windows/): a C# / .NET 10 tray app with the same pipeline,
the same model and the same `~/.myvoice/dictionary.json` (on Windows: `C:\Users\<you>\.myvoice\`).

```
Ctrl+Shift+D → wait for the chime → speak → Ctrl+Shift+D → Whisper (GPU, Vulkan) → Dictionary → Paste
```

- **Requirements:** Windows 11, .NET 10 SDK (`winget install Microsoft.DotNet.SDK.10`), a GPU with a Vulkan
  driver (any recent NVIDIA/AMD/Intel driver). Without one it falls back to the CPU (several seconds per dictation).
- **Setup:** put `ggml-large-v3-turbo.bin` in `%USERPROFILE%\.myvoice\models\` and your `dictionary.json` in
  `%USERPROFILE%\.myvoice\`, then run `powershell -ExecutionPolicy Bypass -File windows\build.ps1` and start
  MyVoice from the Start menu. It lives in the notification area (pin the icon so it stays visible).
- **Bluetooth headsets:** AirPods take up to ~10 s to switch into headset mode when the mic opens. MyVoice waits
  for audio to really flow before the chime, so nothing is lost; speak after the chime. A wired/USB mic starts at once.
- **Limits:** paste can't reach apps running as Administrator. Esc cancels a recording and is unavailable to other
  apps while you're recording. Ctrl+Shift+D overrides the same shortcut in other apps (VS Code, Chrome).
- **Logs:** `%USERPROFILE%\.myvoice\logs\myvoice.log` (tray menu → Open log folder). Timings only; transcripts are
  logged only with `"debug": true` in `%USERPROFILE%\.myvoice\settings.json`.
