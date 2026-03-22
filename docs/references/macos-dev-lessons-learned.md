# macOS App Development: Lessons Learned

**Source projects:** VoiceInk (speech-to-text, shelved) + NetBandwidth (network monitor, shipped)
**Date:** March 2026

---

## 1. Planning & Feasibility

### Always start with a spike

A 50-line throwaway script that proves the core system interaction works end-to-end. Only build real architecture after the spike succeeds. VoiceInk skipped this and built pure-logic components first while the hard parts (hotkeys, permissions) remained unproven. NetBandwidth did the spike first — nettop parsing was validated in minutes.

**Rule:** Identify the single riskiest technical question. Answer it in 50 lines before building anything else.

### Build in risk order, not comfort order

Hardest/most uncertain first, easy parts last. The easy parts are low-risk and can be built anytime. The hard parts determine whether the project is viable.

### Research system-level APIs before implementing

Don't iterate through wrong approaches. 15 minutes of research saves hours of debugging. When the first approach fails for a system-level feature, STOP — research what production macOS apps actually use.

---

## 2. macOS Development Rules

### Project setup

- **Use Xcode** (not SPM) if the app needs any system permissions, `.app` bundle features, or entitlements. SPM command-line tools lack the bundle identity and entitlements infrastructure.
- **`LSUIElement = YES`** in Info.plist for menu bar-only apps (no Dock icon).
- **Disable App Sandbox** if the app needs `CGEvent` posting, `Process()` invocation, or other low-level system access.
- **Ad-hoc signing (`-`)** changes identity on every rebuild, invalidating permissions. Use a self-signed certificate for stable identity if permissions are involved.
- **Launch via `open .app`**, never by running the binary directly — macOS permission checks rely on app bundle identity.

### XcodeGen gotchas

- XcodeGen auto-generates Info.plist and can override your custom keys. Set `GENERATE_INFOPLIST_FILE: false` to use your own plist.
- Always verify the BUILT Info.plist (inside the `.app` bundle) with `plutil -p`, not just the source plist.
- Post-build scripts can run before Xcode finishes processing Info.plist — copy from the fully-built `.app`, not intermediate build products.

### Permissions model

- **Accessibility**, **Input Monitoring**, and **Automation** are three separate TCC permission categories. Each must be granted independently.
- `NSEvent.addGlobalMonitorForEvents` → Input Monitoring
- `CGEvent.tapCreate` → Accessibility
- AppleScript `System Events` → Automation

### Swift 6 / macOS Tahoe

- `DispatchQueue.main.asyncAfter` is NOT `@MainActor`-isolated in Swift 6. Use `Task { @MainActor in }` instead.
- `NSTemporaryDirectory()` returns different paths for bundled apps vs terminal execution.

---

## 3. Debugging Protocol

### When something breaks

1. **Do NOT attempt a fix yet**
2. Add logging to reveal the actual system state
3. Read the logs — identify what IS happening vs what you EXPECTED
4. Form a hypothesis based on evidence, not intuition
5. Write a test that reproduces the problem
6. Commit current state
7. Attempt ONE fix
8. If it doesn't work, **revert completely**, return to step 2

### One-shot fix prevention

First fix attempt didn't work? **STOP.**

1. List 3 hypotheses ranked by probability
2. Write a test that distinguishes between them
3. Only proceed with the fix supported by test evidence
4. Never stack more than one unverified fix

### Not every problem is a code problem

Before modifying code to fix an external behavior, verify the external system works correctly for OTHER inputs. If Spotlight can't find your app AND can't find other new items → system indexing issue, not your app. A restart may be all that's needed.

**Rule:** "Does this problem affect OTHER similar items?" If yes → system-level, not your code. If no → likely your code.

### The user's observations are data

When debugging user-facing issues, ask the user to test adjacent scenarios. Their observations from actual usage are more valuable than programmatic diagnostics.

### Test the right thing

Before writing a test, ask: "If this test passes, does it prove the user's problem is solved?" If not, the test is measuring the wrong thing.

---

## 4. What Works (Keep Doing)

### TDD for pure logic

Any component that transforms data (parsing, formatting, cleaning, replacing) should be built test-first with comprehensive edge cases. Both projects confirmed: TDD logic components work flawlessly on first integration.

### Standalone test scripts

For system-level features, write a minimal standalone script that tests the feature in isolation. Get it working there first, then integrate.

### Diagnostic logging from day one

Add `os_log` / `Logger` from the start for system interactions. Logging should be the FIRST response to any unexpected behavior, not the last resort.

### Reference implementations for validation

When building something that has a comparable tool, compare your output against it. NetBandwidth vs Bandwidth+ (163 MB vs 169.8 MB = ~4% difference) gave confidence in measurement accuracy.

### Clipboard-based paste for text injection

`pbcopy` + CGEvent Cmd+V is more reliable than direct keystroke injection on macOS. Handles Unicode, special characters, works across all apps.

---

## 5. Process Rules

1. **"Before fixing anything, write test cases."** Testing should be reflexive, not an afterthought.
2. **Don't stack fixes.** Revert cleanly between attempts. Each fix attempt starts from a known state.
3. **When multiple approaches fail, stop and rethink methodology.** If three attempts have failed, the problem is likely misunderstood.
4. **When someone says "stop and research first," that is always the right call** for system-level work.
5. **AI-generated code for platform-specific APIs needs extra verification.** Models frequently confuse iOS and macOS APIs. Verify against official Apple docs.

---

## 6. Pre-Flight Checklist for macOS App Projects

### Phase 0: Feasibility (Before any architecture)

- [ ] List every macOS system permission the app needs
- [ ] For each permission: what triggers the prompt, what entitlements are needed, how to verify, what happens when revoked
- [ ] Identify the single highest-risk technical question
- [ ] Write a 50-line throwaway spike that answers it
- [ ] Does it work? If no → stop. If yes → proceed.

### Phase 1: Project Setup

- [ ] Use Xcode (not SPM) if any system permissions or `.app` bundle features needed
- [ ] Create self-signed dev certificate for stable code signing (if permissions involved)
- [ ] Configure Info.plist: `LSUIElement`, usage descriptions for all permissions
- [ ] If using XcodeGen: set `GENERATE_INFOPLIST_FILE: false`, verify built plist with `plutil -p`
- [ ] Disable App Sandbox if needed for low-level system access
- [ ] Verify app launches with stable bundle identity

### Phase 2: System Integration (Before feature code)

- [ ] Test each system interaction in isolation (standalone script per feature)
- [ ] Verify all permissions are granted and persist across rebuilds
- [ ] Integrate system features one at a time, testing each before adding the next

### Phase 3: Feature Development

- [ ] Build features in risk order (hardest first)
- [ ] TDD for all pure logic (parsers, transformers, formatters)
- [ ] Diagnostic logging on every system interaction from the start
- [ ] Commit working state before each experiment; revert cleanly on failure

### Phase 4: Debugging Protocol

When something breaks:

- [ ] Do NOT attempt a fix yet
- [ ] Add logging to reveal actual state
- [ ] First fix didn't work? List 3 hypotheses, rank by probability
- [ ] Ask: "Does this problem affect other similar items?" (code vs system issue)
- [ ] Write a test that distinguishes between hypotheses
- [ ] Attempt ONE fix. If it fails, revert completely.
- [ ] Never stack unverified fixes
