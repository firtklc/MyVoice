# Windows port: spike findings

**Date:** 2026-09-24 · **Machine:** Windows 11 (26200), AMD Ryzen 9 9950X3D, NVIDIA RTX 5080 (Blackwell, 16 GB), AirPods Pro

Before any architecture, a throwaway Python spike ran the whole pipeline (hotkey → record → whisper.cpp → dictionary
→ paste) on the Windows PC and passed UAT; a second, C# spike proved the two libraries the app uses. Every finding
below came from a measurement, and the three bugs were each reproduced by a failing test before they were fixed.

## Why a Windows version at all

- Only ~17 % of the Swift code is portable (DictionaryReplacer, LanguagePreference, the whisper.cpp wrapper); the rest
  is AppKit/SwiftUI/AVFoundation/CGEvent. Swift on Windows has no SwiftUI, and its WinUI bindings are immature.
- Windows voice typing (Win+H) has no custom vocabulary. **Handy** (open source, Windows) only passes custom words to
  Whisper as a prompt — prompting with "Claude" still produced "Hey Cloud". MyVoice's deterministic dictionary is the
  reason it exists, so the port is a rewrite of the same design in C#.

## Speed

| | Load | 10 s clip | 35 s clip |
|---|---|---|---|
| whisper.cpp CUDA 12.4 (Python spike) | 1.2 s | 0.22–0.31 s | 0.41–0.52 s |
| **Whisper.net Vulkan (the app)** | 1.4 s | **0.48–0.52 s** | **0.95–1.02 s** |
| CPU only (8 threads) | 0.8 s | 4.3 s | 6.8 s |

- Vulkan is ~2× slower than CUDA but needs only the graphics driver; Whisper.net's CUDA runtime needs the CUDA 13
  toolkit DLLs, which this PC doesn't have. Real dictation (UAT): 18.8 s of speech in 0.38 s (CUDA).
- The very first run of a new executable compiles GPU shaders (~5–6 s); the app does it in a silent warm-up at launch.
- ~1.6–1.8 GB of VRAM stays in use while the app runs.
- ggml enumerates the RTX 5080 as Vulkan device 0 and the AMD iGPU as device 1; the log records the list.

## Four Windows behaviours the app must handle

1. **Cold Bluetooth mic.** When the mic opens, AirPods switch from music (A2DP) to headset (HFP) mode. On Windows
   that took 0.7–11 s, with little or no audio arriving meanwhile — on MME and WASAPI alike, with PortAudio and with
   NAudio 3.1. UAT symptom: 19 s of speech, 8.9 s captured, only the last sentence transcribed. A user counting aloud
   from the moment the mic opened: 2.5 s captured in 12 s, Whisper heard only "11, 12". The stall shows up as
   *missing* data, not silent padding, so the fix is to chime "speak now" only once ≥90 % of real time has arrived
   over 0.5 s (15 s timeout). With cold AirPods the chime comes ~10 s after the hotkey; a wired/USB mic is immediate.
2. **Late Bluetooth audio.** Stopping the capture the moment the hotkey was pressed lost the last ~0.6 s ("…into
   the [notepad]"). Truncation tests showed Whisper keeps a clipped word until ~0.6 s is missing, so the audio itself
   was gone. Fix: keep recording 0.8 s after the stop key; the stop chime plays after the tail so it isn't recorded.
3. **One line.** whisper.cpp's server joined segments with newlines; the app joins segments on one line.
4. **Never steal focus.** Forcing a test window to the front with an injected Alt tap left it in menu mode, and it
   swallowed the next Ctrl+V (paste 1 lost 3/3, paste 2 landed 3/3; with `AttachThreadInput` instead, 6/6 landed).
   The app never activates a window; paste goes to whatever has focus.

Also: Whisper turns 2 s of pure silence into **"Thank you."**, so recordings peaking below −45 dBFS are treated as
"Nothing heard" and never transcribed (idle AirPods: −66 to −84 dBFS; speech: −10 to −16 dBFS).

And (Slice B UAT): **AirPods deliver audio in uneven slices.** Asked for 20 ms buffers, the WASAPI capture of the
AirPods fired 3,116 callbacks in 36 s averaging 79 samples, half of them (1,558) empty — a real block, then empty
ones. A level meter that shows the *latest* block therefore read 0.000 on all 173 UI ticks while the recording itself
was complete (−9 dBFS peak), so the overlay's bars never moved. The meter now takes the loudest sample since the UI
last read it, and empty callbacks are dropped. A WAV test source that always sends full blocks can't show this.

## Lessons for testing on a live desktop

- **Tests that press keys can type into whatever the user is doing.** An end-to-end test focused its window, but by
  the time the app pasted (20 s later) the user had switched to a game, and the paste went there. Every key-sending
  test now passes the window it expects (`onlyInto:` / `--target`); if focus moved, nothing is typed and the clipboard
  is untouched. Run `Resource=SendsKeys` tests only when nobody is using the PC.
- Claude's tool shell starts processes with Ctrl+C ignored (inherited), so a Ctrl+C test must re-enable it first.
- **A test that moves focus or presses keys carries `Resource=SendsKeys` as its only resource tag.** The end-to-end
  test was tagged GPU too, so a "GPU only" run focused a test window and pasted into it while the user was at the PC.
- **The test process must be per-monitor DPI aware, like the app.** Without it Windows scales the test's coordinates
  on a 150 % display: window-position tests still passed, but a screen capture of the overlay caught the desktop
  behind it instead.
- **Known quirk (open):** in a full `Resource=SendsKeys` run, `PasterTests.PastesIntoTheFocusedWindow` and
  `WaitsUntilHeldModifiersAreReleased` can find Windows refusing `SetForegroundWindow` (Edge or Claude in front);
  the focus guard then fails them without sending keys. Run alone (`--filter-class "*PasterTests"`) they pass. The
  app is unaffected — it never activates windows.

## Smart App Control

Smart App Control is on (enforced) on this PC. It judged each new unsigned build by its file hash: one build of the
test DLL was blocked ("An Application Control policy has blocked this file", 0x800711C7, Code Integrity event 3033),
the next build of the same code with one comment changed was allowed, and reverting the comment reproduced the
blocked hash (builds are deterministic) — which stayed blocked. One of whisper.cpp's CPU DLLs was blocked too. The
app itself hasn't been blocked so far, but any rebuild can be. Smart App Control has no per-app allow list; the
options are to rebuild with a change, sign with a trusted (paid) code-signing certificate, or turn Smart App Control
off in Windows Security — a security decision for the machine's owner.
