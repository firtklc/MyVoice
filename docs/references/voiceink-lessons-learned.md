# VoiceInk: Lessons Learned

**Project:** Local speech-to-text dictation app (macOS menu bar)
**Stack:** Swift 6.2, SwiftUI, WhisperKit (CoreML), Carbon API
**Duration:** Multiple sessions, ultimately shelved
**Date:** March 2026

---

## 1. Planning Failures

### Started with the wrong project structure

Chose SPM (Swift Package Manager) instead of Xcode from the start. This cascaded into hours spent writing manual `bundle.sh` scripts, debugging ad-hoc code signing, and wrestling with permission systems that assume proper `.app` bundles.

**Rule for next time:** If the app needs _any_ macOS system permissions (Accessibility, Input Monitoring, Automation, microphone), start with Xcode. SPM command-line tools don't have the bundle identity, entitlements infrastructure, or Info.plist that macOS permission systems require.

### No upfront research on system-level APIs

Dove into implementation without understanding the macOS permission model. Accessibility, Input Monitoring, and Automation are three _separate_ permission gates with different requirements. Code signing identity matters for permission persistence. None of this was researched before writing code.

**Rule for next time:** Before writing a single line of feature code, document every system permission the app will need, what each one requires (entitlements, signing, user grants), and how to test each in isolation.

### Built the easy parts first

Started with DictationDictionary and WhisperTokenCleaner -- pure logic components that were satisfying to build and test but carried zero technical risk. The actual hard problems (global hotkeys, text injection, permission management) were deferred.

**Rule for next time:** Identify the single riskiest technical question and answer it first. For VoiceInk, that question was: "Can I record audio, transcribe it, and paste the result into any app from a menu bar app?" That should have been a 50-line proof-of-concept on day one.

### No spike/proof-of-concept phase

The project went straight from idea to full architecture. There was no "can this even work?" phase. A minimal spike testing `record -> transcribe -> paste` would have surfaced the signing, permission, and hotkey issues immediately, before any architecture was built around assumptions.

**Rule for next time:** Every macOS app project starts with a throwaway spike that proves the core system interaction works end-to-end. Only then build the real architecture.

---

## 2. Technical Lessons (macOS-Specific)

### Code Signing & Permissions

- **Ad-hoc signing (`-`)** changes identity on every rebuild. Every rebuild invalidates Accessibility and Input Monitoring permissions, requiring the user to re-grant them manually in System Settings.
- **Self-signed certificate** (e.g., "VoiceInk Dev") gives a stable identity across rebuilds. Create one in Keychain Access, sign with it, and permissions persist.
- **App Sandbox must be disabled** for `CGEvent` posting. Sandboxed apps cannot inject keystrokes into other apps.
- **Launch via `open .app`**, never by running the binary directly. macOS permission checks rely on the app bundle identity, not the raw executable.

### Permissions Model

- **`NSEvent.addGlobalMonitorForEvents`** requires **Input Monitoring** permission, _not_ just Accessibility. These are separate TCC categories.
- **`CGEvent.tapCreate`** requires **Accessibility** permission.
- **AppleScript `System Events` keystroke** requires **Automation** permission (a third, separate permission).
- Each permission must be granted independently in System Settings. They are not inherited from each other.

### Global Hotkeys

- **Carbon `RegisterEventHotKey`** with `GetEventDispatcherTarget()` is the correct API for global hotkeys in SwiftUI menu bar apps. This is what production apps (including MacWhisper) use.
- The path to discovering this was wasteful: HotKey package -> `NSEvent.addGlobalMonitorForEvents` -> `CGEvent.tapCreate` -> Carbon API. Each intermediate attempt had its own permission issues.
- **Option-only hotkeys** are blocked on modern macOS (error -9878). Don't attempt them.
- **The period key (`.`)** does not work with Carbon hotkeys on Turkish keyboard layout. Keyboard layout affects key code mapping.
- **Cmd+Shift+D** works reliably across layouts and doesn't conflict with common system shortcuts.

### Text Injection

- **Clipboard paste (`pbcopy` + CGEvent Cmd+V)** is more reliable than direct CGEvent character-by-character typing. Direct typing has issues with Unicode, special characters, and keyboard layout mismatches.
- CGEvent text injection requires both Accessibility permission and proper app bundle identity.
- The word-cutoff issue consumed 10+ fix attempts. Every fix broke the working paste mechanism. Diagnostic logging (when finally added) showed the real problem was timing, not the paste logic.

### WhisperKit Specifics

- **`requiredSegmentsForConfirmation`** defaults to 2, meaning you need 3+ segments before any text is confirmed during streaming. Set to 1 for more responsive dictation.
- **Segment `.text`** includes raw tokens like `<|startoftranscript|>` and `<|en|>`. These must be stripped. The `WhisperTokenCleaner` regex approach worked well.
- **`AVAudioSession` does not exist on macOS.** WhisperKit handles audio internally on macOS. Claude (Sonnet 4.5) generated iOS-style code referencing this API -- it doesn't compile on macOS.

### Swift 6 / macOS Tahoe Gotchas

- **`DispatchQueue.main.asyncAfter`** is NOT `@MainActor`-isolated in Swift 6. This causes concurrency warnings/errors. Use `Task { @MainActor in ... }` instead.
- **`NSTemporaryDirectory()`** returns different paths for bundled apps vs terminal execution. Don't assume `/tmp/`.
- **`UserDefaults(suiteName:)`** has a malloc bug (address 0x2a598fa50) in test environments on macOS Tahoe. Tests that rely on UserDefaults may crash for reasons unrelated to your code.
- **`LSUIElement = YES`** in Info.plist makes a menu bar-only app (no Dock icon). Essential for dictation utility UX.

---

## 3. Process Failures

### Fix-first, diagnose-never

The dominant failure pattern was: something breaks -> immediately attempt a fix -> fix doesn't work -> try another fix -> that breaks something else -> repeat. At no point was diagnostic logging added to understand the actual system state.

**Rule for next time:** When something breaks, the first action is ALWAYS to add logging that reveals the true state. Print what's actually happening before theorizing about what's going wrong.

### No proper revert between attempts

Each failed fix attempt left residue. The next attempt was built on top of the previous broken state rather than a clean revert. After 3-4 stacked failed fixes, the code was in a state no one understood.

**Rule for next time:** Before attempting any fix, commit (or stash) the current state. If the fix doesn't work, revert completely before trying the next approach. Never stack speculative fixes.

### Rabbit-hole hotkey debugging

The hotkey implementation went through four completely different APIs before landing on the correct one. Each API had its own permission requirements, failure modes, and debugging overhead. A 15-minute research session would have pointed directly to Carbon `RegisterEventHotKey`.

**Rule for next time:** When the first approach fails for a system-level feature, STOP. Research what production macOS apps actually use. Don't iterate through APIs hoping one works.

### AI-generated code quality issues

Claude in Xcode (Sonnet 4.5) produced:
- iOS-specific code (`AVAudioSession`) that doesn't exist on macOS
- Wrong test framework (Swift Testing syntax for XCTest targets, and vice versa)
- Incorrect WhisperKit API usage
- Fixes that were plausible-sounding but wrong

**Rule for next time:** Treat AI-generated code for platform-specific APIs with extra skepticism. Verify against official Apple documentation, not just "does it look right." AI models frequently confuse iOS and macOS APIs.

### Ignored user instincts

The user repeatedly asked "can you research properly before trying another approach?" -- and was correct every time. The pattern of the user having to pull the AI out of fix-loops was a recurring friction point.

**Rule for next time:** When someone (including yourself) says "stop and research first," that is always the right call for system-level work.

---

## 4. What Worked Well

### TDD for pure logic

The DictationDictionary and WhisperTokenCleaner were built test-first. 47+ tests, all passing. Word-boundary regex replacement, token cleaning, dictionary loading -- all correct on first integration. TDD works perfectly for deterministic, isolated logic.

**Keep doing this:** Any component that transforms data (cleaning, formatting, replacing, parsing) should be built test-first with comprehensive edge cases.

### Standalone test scripts

A standalone hotkey test script that tested multiple key combinations simultaneously found the working combo (Cmd+Shift+D) in a single run. This was more effective than debugging hotkeys inside the full app.

**Keep doing this:** For system-level features, write a minimal standalone script that tests the feature in isolation. Get it working there first, then integrate.

### Diagnostic logging (when finally used)

When logging was finally added to the text injection pipeline, it immediately revealed that the word-cutoff issue was a timing problem, not a logic problem. The 10+ previous fix attempts had been solving the wrong problem.

**Keep doing this:** Logging should be the FIRST response to any unexpected behavior, not the last resort after everything else fails.

### Clipboard-based paste

The `pbcopy` + CGEvent Cmd+V approach was more robust than direct keystroke injection. It handles Unicode, special characters, and works across all apps consistently.

**Keep doing this:** For text injection on macOS, clipboard paste is the pragmatic approach. Don't over-engineer direct typing.

---

## 5. Methodology Lessons (Universal)

1. **"Before fixing anything, write test cases."** The user had to remind this multiple times. Testing should be reflexive, not an afterthought.

2. **Research system-level APIs before implementing.** Don't iterate through wrong approaches. 15 minutes of research saves hours of debugging.

3. **Prototype the riskiest part first, not the easiest.** The easy parts are low-risk and can be built anytime. The hard parts determine whether the project is viable.

4. **When something breaks, add logging to understand WHY before attempting fixes.** Guessing at fixes is the most expensive debugging strategy.

5. **Don't stack fixes.** Revert cleanly between attempts. Each fix attempt should start from a known state.

6. **When multiple approaches fail, stop and rethink methodology.** If three attempts have failed, the problem is likely misunderstood. Step back and re-examine assumptions.

7. **AI assistants are weakest on platform-specific system APIs.** They frequently hallucinate iOS APIs on macOS, invent nonexistent framework methods, and suggest permission models that don't match reality. Always verify against official docs.

8. **A working end-to-end demo in 50 lines beats a perfect architecture with no proof of concept.** Architecture should follow feasibility, not precede it.

---

## 6. Pre-Flight Checklist for Future macOS App Projects

Run through this before writing any application code.

### Phase 0: Feasibility (Day 1)

- [ ] List every macOS system permission the app will need (Accessibility, Input Monitoring, Automation, Microphone, Screen Recording, etc.)
- [ ] For each permission, document: what triggers the prompt, what entitlements are needed, how to verify it's granted, what happens when it's revoked
- [ ] Identify the single highest-risk technical question (the thing most likely to make the project infeasible)
- [ ] Write a throwaway 50-line spike that answers that question
- [ ] Does it work? If no, stop here. If yes, proceed.

### Phase 1: Project Setup

- [ ] Use Xcode (not SPM) if the app needs any system permissions or `.app` bundle features
- [ ] Create a self-signed developer certificate in Keychain Access for stable code signing identity
- [ ] Set up signing in Xcode to use the self-signed certificate
- [ ] Configure Info.plist: `LSUIElement` (if menu bar only), usage descriptions for all permissions
- [ ] Disable App Sandbox if the app needs CGEvent posting or other low-level system access
- [ ] Verify the app launches, gets a stable bundle identity, and can request permissions

### Phase 2: System Integration (Before Any Feature Code)

- [ ] Build and test each system interaction in isolation (standalone test script per feature)
- [ ] Global hotkey: use Carbon `RegisterEventHotKey` with `GetEventDispatcherTarget()`
- [ ] Text injection: use clipboard paste (pbcopy + CGEvent Cmd+V), not direct typing
- [ ] Microphone access: test recording in isolation
- [ ] Verify all permissions are granted and persist across rebuilds
- [ ] Integrate system features into the app one at a time, testing each before adding the next

### Phase 3: Feature Development

- [ ] Build features in risk order (hardest/most uncertain first)
- [ ] TDD for all pure logic components (parsers, transformers, formatters)
- [ ] Add diagnostic logging to every system interaction from the start
- [ ] Commit working state before each experiment; revert cleanly on failure

### Phase 4: Debugging Protocol

When something breaks:

1. **Do NOT attempt a fix yet**
2. Add logging to reveal the actual system state
3. Read the logs. Identify what is actually happening vs. what you expected
4. Form a hypothesis based on evidence, not intuition
5. Write a test that reproduces the problem
6. Commit current state
7. Attempt ONE fix
8. If it doesn't work, revert completely, then return to step 2

---

## Summary

The VoiceInk project's core technical approach was sound -- WhisperKit transcription, clipboard paste, Carbon hotkeys, menu bar UI all worked. The project was derailed not by impossible requirements but by process failures: building in the wrong order, skipping research, stacking speculative fixes, and ignoring diagnostic fundamentals. The pure-logic components built with TDD worked flawlessly. The system-integration components built without research or logging consumed the majority of time.

The single highest-leverage change for future projects: **prove the riskiest system interaction works in 50 lines before building anything else.**
