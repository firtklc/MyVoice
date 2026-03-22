# VoiceInk Speech Engine Research

**Date:** March 2026
**Purpose:** Pre-build research for VoiceInk (macOS menu bar dictation app)
**Engines compared:** Apple SpeechAnalyzer (native) vs WhisperKit (third-party)

---

## 1. Streaming API

### How It Works
- Uses **LocalAgreement-n** streaming policy — compares consecutive hypothesis passes, confirms text that appears consistently across `n` passes.
- Two output streams:
  - **Confirmed segments** — stable, final text. Will not change.
  - **Hypothesis segments** — low-latency, may change or disappear.
- For dictation: display hypothesis text in lighter/italic style for responsiveness, replace with confirmed text once stable.

### `requiredSegmentsForConfirmation`
- **Default: 2** — needs 2 consecutive passes to agree before text is confirmed.
- Set to **1** for faster confirmation (more responsive dictation, slightly less accurate).
- Higher values = more stable but higher latency.

### Key Callbacks
- `TranscriptionCallback`: Takes `TranscriptionProgress`, returns `Bool?`. Must be lightweight — heavy processing causes extra decoding loops.
- `SegmentDiscoveryCallback`: Receives segments with accurate seek values for confirmed text handling.

---

## 2. Model Loading

### Model Sizes

| Model | Disk Size | RAM When Loaded | Best For |
|-------|-----------|-----------------|----------|
| tiny.en | ~30-40 MB | ~40-50 MB | Fastest, lowest quality |
| base.en | ~140 MB | ~180 MB | **Recommended for menu bar app** |
| small.en | ~460 MB | ~500 MB | Good balance |
| large-v3-turbo | ~950 MB | ~1.5 GB | Best quality, heavy |

### First-Run CoreML Compilation (BIGGEST GOTCHA)
- **First run on any device is brutally slow.** ANECompilerService compiles CoreML model to device-specific Neural Engine format.
- Turbo model: ~4 minutes first compilation.
- Large models on M1: reports of **2-3 hours** at 99.9% CPU.
- **Subsequent loads are fast** (seconds) — compiled result is cached.
- **Mitigation:** Call `prewarmModels()` at app launch to trigger compilation before the user needs transcription.

### Bundling vs Download
- **Download on first launch** (recommended) — standard pattern for all production apps.
- Bundle `tiny.en` as immediate fallback, download `base.en` or `small.en` in background.
- During download, extraction temporarily needs ~2x final disk space.

---

## 3. Segment Quirks

### Non-Sequential Segment IDs
- **Segment IDs are NOT guaranteed sequential.** Can jump from 0 → 4 → 1 within the same result.
- Text can be scattered across segment IDs — start of sentence in one, middle in another.
- **Rule:** Track segments by timestamp/seek values, NEVER by ID ordering.

### Special Tokens in Output
- Raw tokens appear in segment text: `<|startoftranscript|>`, `<|en|>`, `<|transcribe|>`, `<|notimestamps|>`, timestamp tokens like `<|0.00|>`.
- **Always set `skipSpecialTokens = true`** in `DecodingOptions`.

### Hallucination During Silence
- Whisper generates phantom text during silence or non-speech audio. Common: repeated text loops, ghost transcripts.
- **Mitigations:**
  - `noSpeechThreshold` — treats segment as silent if no-speech probability exceeds threshold.
  - `compressionRatioThreshold` — catches repetitive hallucinated text.
  - `chunkingStrategy: .vad` — Voice Activity Detection pre-filtering.
  - `windowClipTime` — clips audio window ends to prevent boundary hallucination.

---

## 4. macOS vs iOS Differences

### APIs That Don't Exist on macOS
- **`AVAudioSession`** — iOS only. Coordinates audio between apps.
- On macOS, use `AVAudioEngine` directly. WhisperKit handles this internally.
- Any `AVAudioSession` code must be wrapped in `#if os(iOS)`.

### Microphone Permissions (macOS)
- `NSMicrophoneUsageDescription` in Info.plist (always required).
- If sandboxed: `com.apple.security.device.microphone` + `com.apple.security.device.audio-input` entitlements.
- Non-sandboxed (our case): just the Info.plist key.

### Requirements
- **macOS 13 Ventura minimum.**
- **Apple Silicon only.** No Intel support.

---

## 5. Performance

### Benchmarks by Chip

| Chip | Model | Speed | Notes |
|------|-------|-------|-------|
| M1 | base | ~6s for 1 min audio | ~10x real-time |
| M2 Ultra | large-v3-turbo (GPU+ANE) | 72x real-time | Best config |
| M3 | text decoder | 4.6 ms per forward pass | With stateful models |
| M4 | general | 17-27x real-time | Varies by model |

### Compute Unit Configuration
- `melCompute`: `.cpuAndGPU` (default)
- `audioEncoderCompute`: `.cpuAndNeuralEngine` (default)
- `textDecoderCompute`: `.cpuAndNeuralEngine` (default)
- `prefillCompute`: `.cpuOnly` (default)

### Memory
- Base model: ~180 MB loaded + 100-200 MB spike during transcription.
- **Use a singleton WhisperKit instance** — creating/destroying repeatedly causes memory leaks (Issue #265).

---

## 6. Known Issues & Gotchas

| Issue | Severity | Workaround |
|-------|----------|------------|
| First-run CoreML compilation (minutes to hours) | **Critical** | `prewarmModels()` at app launch |
| AirPods/Bluetooth cause recording failure (-10877) | High | Handle audio device change notifications, restart audio engine |
| Segment IDs non-sequential | High | Track by timestamp, not ID |
| Hallucination during silence | High | Enable VAD, set noise thresholds |
| Turbo models unsupported on baseline M1 | Medium | Use base/small model instead |
| Memory leak on repeated instance creation | Medium | Use singleton pattern |
| Audio captures only first channel (stereo interfaces) | Low | May need manual channel selection |
| Hangs on M4 Pro (Issue #340) | Low | Under investigation |
| Build failure on macOS 14 with v0.15.0 | Low | Pin to stable version |

---

## 7. Production App Patterns

### What Shipping Apps Do
- **MacWhisper** and **Superwhisper** both use **whisper.cpp**, not WhisperKit.
- All production apps follow the same UX pattern:
  1. Menu bar presence (always accessible, minimal footprint)
  2. Push-to-talk global hotkey
  3. Model download on first launch (not bundled)
  4. Show hypothesis text in real-time, finalize on confirmation
  5. Auto-copy to clipboard after transcription
  6. Non-App Store distribution (avoids sandbox restrictions)

### Open Source References
- **AudioWhisper** (mazdak/AudioWhisper) — lightweight menu bar app
- **OpenSuperWhisper** (Starmel/OpenSuperWhisper) — open-source dictation app

---

## 8. Apple SpeechAnalyzer (PRIMARY CANDIDATE)

**Status:** Available NOW on macOS Tahoe 26.3 (Fırat's current OS).

### API Overview
- **Framework:** `import Speech`
- **Main classes:**
  - `SpeechAnalyzer` — session manager, receives audio, routes to modules
  - `SpeechTranscriber` — speech-to-text (new model, raw words, minimal formatting)
  - `DictationTranscriber` — punctuation-aware dictation with sentence structure (fallback engine)
  - `SpeechDetector` — voice activity detection without transcribing
  - `AssetInventory` — model asset manager (download/install language models)

### Streaming / Real-Time Transcription
- Fully supports real-time streaming via `AsyncStream<AnalyzerInput>`.
- **Presets:** `.progressiveLiveTranscription` (real-time) and `.offlineTranscription` (batch).
- **Volatile vs Finalized results:**
  - Enable via `reportingOptions: [.volatileResults]`
  - Each result has `isFinal` property
  - Volatile = interim guesses (display in lighter style), finalized = confirmed text
- **Push-to-talk:** Not built-in. You control when audio flows into the stream — start yielding buffers on hotkey press, stop on release.

### Code Pattern
```swift
import Speech

let transcriber = SpeechTranscriber(
    locale: Locale(identifier: "en-US"),
    reportingOptions: [.volatileResults],
    attributeOptions: [.audioTimeRange]
)
let analyzer = SpeechAnalyzer(modules: [transcriber])
let audioFormat = SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: transcriber)

// Feed audio via AsyncStream<AnalyzerInput>, iterate results:
for await result in transcriber.results {
    if result.isFinal {
        // Append to final transcript, clear volatile
    } else {
        // Update volatile/interim display
    }
}
```

### Microphone Access
- **You handle capture yourself** via `AVAudioEngine` (same as WhisperKit).
- Required Info.plist keys: `NSMicrophoneUsageDescription` + `NSSpeechRecognitionUsageDescription`.
- Audio capture layer is shared with WhisperKit — can swap engines without rewriting capture code.

### Language Support
- **English:** en_US, en_GB, en_AU, en_CA, en_IE, en_IN, en_NZ, en_SG, en_ZA
- **Turkish:** tr_TR ✓
- **Russian:** ru_RU ✓
- Models download automatically on first use. No user configuration needed.

### Head-to-Head: SpeechAnalyzer vs WhisperKit

| | SpeechAnalyzer | WhisperKit (Large V3 Turbo) |
|---|---|---|
| **Speed** | ~45 sec for 34-min file | ~1 min 41 sec (2.2x slower) |
| **Word Error Rate** | **8%** | **1%** |
| **Character Error Rate** | 3% | 0.3% |
| **Model management** | System-managed, zero app size | Bundle or download, significant size |
| **First-run penalty** | Model download only | CoreML compilation (minutes to hours) |
| **API style** | Native Swift async/await | Third-party dependency |
| **Dependencies** | None (built into OS) | WhisperKit SPM package |
| **Memory** | System-managed | 180 MB – 1.5 GB depending on model |
| **Custom vocabulary** | No | No |
| **Offline** | Yes (after model download) | Yes |
| **Min OS** | macOS 26 | macOS 13 |

### Limitations
1. **8% WER vs 1% WER** — 8x more errors than Whisper Large V3 Turbo. The key trade-off.
2. No custom vocabulary.
3. No automatic language detection — must specify locale upfront.
4. No model size choice — one model per locale, system-managed.
5. `SpeechDetector` doesn't formally conform to `SpeechModule` protocol (rough edge).
6. macOS 26+ only.

### Open Source References Using SpeechAnalyzer
- **speechdock** (yohasebe/speechdock) — menu bar app, real-time transcription. **Closest to VoiceInk.**
- **voicewrite** (leftouterjoins/voicewrite) — private, on-device voice-to-text for macOS.
- **Stenographer** (otaviocc/Stenographer) — drag-and-drop media transcription.
- **argmax CLI example** (argmaxinc/apple-speechanalyzer-cli-example) — offline + live modes.

### vs SFSpeechRecognizer (Legacy)
- SpeechAnalyzer is the modern replacement: async/await, modular, fully on-device.
- `SFSpeechRecognizer` not deprecated but no longer recommended for new projects.
- Old API had custom vocabulary support — SpeechAnalyzer does not.

---

## 9. Verdict & Spike Strategy

### Recommended Approach
**Spike SpeechAnalyzer first** — dramatically simpler implementation, zero dependencies. If accuracy is acceptable for personal dictation use, ship with it. If not, swap to WhisperKit (audio capture layer is shared).

### SpeechAnalyzer Spike Checklist
- [ ] `AVAudioEngine` captures microphone audio on macOS
- [ ] `SpeechTranscriber` produces real-time volatile + finalized results
- [ ] Accuracy is acceptable for English dictation (subjective test)
- [ ] Result can be pasted into another app via clipboard + CGEvent Cmd+V
- [ ] Carbon `RegisterEventHotKey` triggers recording start/stop

### WhisperKit Spike Checklist (Fallback)
- [ ] WhisperKit can load `base.en` model and transcribe a buffer
- [ ] Streaming output produces both hypothesis and confirmed text
- [ ] `skipSpecialTokens` removes raw tokens from output
- [ ] Same paste + hotkey integration as above

If SpeechAnalyzer spike works → build on it. No WhisperKit needed.
If accuracy is unacceptable → switch to WhisperKit spike, same audio layer.
