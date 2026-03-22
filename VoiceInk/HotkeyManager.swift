import Carbon
import Foundation

final class HotkeyManager {
    private var hotkeyRef: EventHotKeyRef?
    private var handler: (() -> Void)?

    init(onToggle: @escaping () -> Void) {
        self.handler = onToggle
        registerHotkey()
    }

    private func registerHotkey() {
        let modifiers: UInt32 = UInt32(cmdKey | shiftKey)
        let keyCode: UInt32 = 2  // 'D' key

        var hotKeyID = EventHotKeyID()
        hotKeyID.signature = OSType(0x564B4559)  // "VKEY"
        hotKeyID.id = 1

        var eventType = EventTypeSpec()
        eventType.eventClass = OSType(kEventClassKeyboard)
        eventType.eventKind = OSType(kEventHotKeyPressed)

        let refcon = Unmanaged.passUnretained(self).toOpaque()
        InstallEventHandler(
            GetEventDispatcherTarget(),
            { _, event, refcon -> OSStatus in
                guard let refcon else { return OSStatus(eventNotHandledErr) }
                let manager = Unmanaged<HotkeyManager>.fromOpaque(refcon).takeUnretainedValue()
                manager.handler?()
                return noErr
            },
            1,
            &eventType,
            refcon,
            nil
        )

        let status = RegisterEventHotKey(
            keyCode,
            modifiers,
            hotKeyID,
            GetEventDispatcherTarget(),
            0,
            &hotkeyRef
        )

        if status != noErr {
            print("Failed to register hotkey: \(status)")
        } else {
            print("Hotkey registered: Cmd+Shift+D")
        }
    }

    deinit {
        if let hotkeyRef {
            UnregisterEventHotKey(hotkeyRef)
        }
    }
}
