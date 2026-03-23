import Testing
@testable import MyVoice

struct HotkeyTests {
    @Test func hotkeyDisplayStringFormatsCorrectly() {
        // Cmd+Shift+D should display as "⌘⇧D"
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 2, // D key
            modifiers: .init(arrayLiteral: .command, .shift)
        )
        #expect(display == "⌘⇧D")
    }

    @Test func hotkeyDisplayStringWithOption() {
        // Cmd+Option+R should display as "⌘⌥R"
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 15, // R key
            modifiers: .init(arrayLiteral: .command, .option)
        )
        #expect(display == "⌘⌥R")
    }

    @Test func hotkeyDisplayStringWithControl() {
        // Ctrl+Shift+M should display as "⌃⇧M"
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 46, // M key
            modifiers: .init(arrayLiteral: .control, .shift)
        )
        #expect(display == "⌃⇧M")
    }

    @Test func hotkeyDisplayStringAllModifiers() {
        // Cmd+Ctrl+Option+Shift+A should display as "⌘⌃⌥⇧A"
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 0, // A key
            modifiers: .init(arrayLiteral: .command, .control, .option, .shift)
        )
        #expect(display == "⌘⌃⌥⇧A")
    }

    @Test func hotkeyDisplayStringFunctionKey() {
        // Cmd+F5 should display as "⌘F5"
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 96, // F5 key
            modifiers: .init(arrayLiteral: .command)
        )
        #expect(display == "⌘F5")
    }

    @Test func hotkeyMenuTextShowsCurrentShortcut() {
        // The menu hint should dynamically show the current shortcut
        let menuText = HotkeyDisplayHelper.menuHintText(
            keyCode: 2,
            modifiers: .init(arrayLiteral: .command, .shift)
        )
        #expect(menuText == "⌘⇧D to dictate")
    }

    @Test func hotkeyMenuTextUpdatesWithNewShortcut() {
        // After changing to Cmd+Option+R, menu should reflect it
        let menuText = HotkeyDisplayHelper.menuHintText(
            keyCode: 15,
            modifiers: .init(arrayLiteral: .command, .option)
        )
        #expect(menuText == "⌘⌥R to dictate")
    }

    @Test func defaultHotkeyIsCmdShiftD() {
        let defaultCombo = HotkeyDisplayHelper.defaultKeyCode
        let defaultModifiers = HotkeyDisplayHelper.defaultModifiers
        #expect(defaultCombo == 2) // D key
        #expect(defaultModifiers.contains(.command))
        #expect(defaultModifiers.contains(.shift))
    }
}
