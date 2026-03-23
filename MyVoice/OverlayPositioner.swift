import AppKit

/// Calculates overlay window position relative to the caret.
enum OverlayPositioner {
    private static let verticalOffset: CGFloat = 4

    static func position(for caretRect: CGRect, overlaySize: NSSize) -> NSPoint {
        // Position at upper-left of the caret
        // Use fixed offsets from caret origin, ignore reported caret height
        var x = caretRect.origin.x - overlaySize.width + 8  // slightly overlap horizontally
        var y = caretRect.origin.y - 12                        // align with top of cursor

        // Clamp to screen bounds
        if let screen = NSScreen.main {
            let screenFrame = screen.visibleFrame

            // Don't go off left edge — flip to right of caret
            if x < screenFrame.origin.x {
                x = max(screenFrame.origin.x, caretRect.origin.x + verticalOffset)
            }

            // Don't go off right edge
            if x + overlaySize.width > screenFrame.maxX {
                x = screenFrame.maxX - overlaySize.width
            }

            // Don't go off top edge — flip below caret
            if y + overlaySize.height > screenFrame.maxY {
                y = caretRect.origin.y - overlaySize.height - verticalOffset
            }

            // Don't go off bottom edge
            if y < screenFrame.origin.y {
                y = screenFrame.origin.y
            }
        }

        return NSPoint(x: x, y: y)
    }
}
