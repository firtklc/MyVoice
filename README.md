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
