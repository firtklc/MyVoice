import Testing
@testable import MyVoice

struct HotkeyTests {
    @Test func hotkeyDisplayStringFormatsCorrectly() {
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 2,
            modifiers: .init(arrayLiteral: .command, .shift)
        )
        #expect(display == "⌘⇧D")
    }

    @Test func hotkeyDisplayStringWithOption() {
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 15,
            modifiers: .init(arrayLiteral: .command, .option)
        )
        #expect(display == "⌘⌥R")
    }

    @Test func hotkeyDisplayStringWithControl() {
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 46,
            modifiers: .init(arrayLiteral: .control, .shift)
        )
        #expect(display == "⌃⇧M")
    }

    @Test func hotkeyDisplayStringAllModifiers() {
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 0,
            modifiers: .init(arrayLiteral: .command, .control, .option, .shift)
        )
        #expect(display == "⌘⌃⌥⇧A")
    }

    @Test func hotkeyDisplayStringFunctionKey() {
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 96,
            modifiers: .init(arrayLiteral: .command)
        )
        #expect(display == "⌘F5")
    }

    @Test func hotkeyDisplayStringPeriodKey() {
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 47,
            modifiers: .init(arrayLiteral: .option)
        )
        #expect(display == "⌥.")
    }

    @Test func hotkeyDisplayStringUnmappedKeyFallback() {
        let display = HotkeyDisplayHelper.displayString(
            keyCode: 999,
            modifiers: .init(arrayLiteral: .command)
        )
        #expect(display == "⌘Key999")
    }

    @Test func hotkeyMenuTextShowsCurrentShortcut() {
        let menuText = HotkeyDisplayHelper.menuHintText(
            keyCode: 2,
            modifiers: .init(arrayLiteral: .command, .shift)
        )
        #expect(menuText == "⌘⇧D to dictate")
    }

    @Test func hotkeyMenuTextUpdatesWithNewShortcut() {
        let menuText = HotkeyDisplayHelper.menuHintText(
            keyCode: 15,
            modifiers: .init(arrayLiteral: .command, .option)
        )
        #expect(menuText == "⌘⌥R to dictate")
    }
}
