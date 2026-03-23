import AppKit
import ApplicationServices

/// Gets the text cursor (caret) position from the focused app via Accessibility API.
/// Falls back to mouse cursor position if Accessibility fails.
enum CursorLocator {
    /// Returns the caret position in Cocoa coordinates, or mouse position as fallback.
    static func getCaretPosition() -> NSPoint {
        if let caretRect = getCaretRect(),
           caretRect.width > 0 || caretRect.height > 0 {
            return CoordinateConverter.quartzToCocoa(caretRect.origin)
        }
        // Fallback: mouse cursor position
        return NSEvent.mouseLocation
    }

    /// Returns the caret CGRect in Quartz coordinates, or nil if unavailable.
    static func getCaretRect() -> CGRect? {
        let systemElement = AXUIElementCreateSystemWide()

        var focusedElement: CFTypeRef?
        let focusedError = AXUIElementCopyAttributeValue(
            systemElement,
            kAXFocusedUIElementAttribute as CFString,
            &focusedElement
        )
        guard focusedError == .success, focusedElement != nil else {
            return nil
        }
        // AXUIElement is a CFTypeRef — use it directly
        let element = focusedElement!

        var selectedRange: CFTypeRef?
        let rangeError = AXUIElementCopyAttributeValue(
            element as! AXUIElement,
            kAXSelectedTextRangeAttribute as CFString,
            &selectedRange
        )
        guard rangeError == .success, let range = selectedRange else {
            return nil
        }

        var bounds: CFTypeRef?
        let boundsError = AXUIElementCopyParameterizedAttributeValue(
            element as! AXUIElement,
            kAXBoundsForRangeParameterizedAttribute as CFString,
            range,
            &bounds
        )
        guard boundsError == .success, bounds != nil else {
            return nil
        }

        var rect = CGRect.zero
        // swiftlint:disable:next force_cast
        AXValueGetValue(bounds as! AXValue, .cgRect, &rect)
        return rect
    }
}
