import AppKit
import Carbon

final class Paster: @unchecked Sendable {
    func paste(_ text: String) {
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        pasteboard.setString(text, forType: .string)

        // Small delay to ensure clipboard is ready
        Task { @MainActor [self] in
            try? await Task.sleep(for: .milliseconds(50))
            self.simulatePaste()
        }
    }

    private nonisolated func simulatePaste() {
        let source = CGEventSource(stateID: .hidSystemState)

        let keyDown = CGEvent(keyboardEventSource: source, virtualKey: CGKeyCode(9), keyDown: true)  // 9 = 'V'
        let keyUp = CGEvent(keyboardEventSource: source, virtualKey: CGKeyCode(9), keyDown: false)

        keyDown?.flags = .maskCommand
        keyUp?.flags = .maskCommand

        keyDown?.post(tap: .cghidEventTap)
        keyUp?.post(tap: .cghidEventTap)
    }
}
