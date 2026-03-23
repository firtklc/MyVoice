import AppKit

/// Converts between Quartz (AX API) and Cocoa coordinate systems.
/// Quartz: origin top-left, Y increases downward.
/// Cocoa: origin bottom-left, Y increases upward.
enum CoordinateConverter {
    static func quartzToCocoa(_ point: CGPoint, screenHeight: CGFloat) -> NSPoint {
        return NSPoint(x: point.x, y: screenHeight - point.y)
    }

    /// Converts using the primary screen height automatically.
    static func quartzToCocoa(_ point: CGPoint) -> NSPoint {
        let screenHeight = NSScreen.screens.first?.frame.height ?? 1080
        return quartzToCocoa(point, screenHeight: screenHeight)
    }
}
