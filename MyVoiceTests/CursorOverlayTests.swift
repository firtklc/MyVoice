import Testing
@testable import MyVoice
import AppKit

struct CursorOverlayTests {
    @Test func quartzToCocoaConversion() {
        // Quartz: origin top-left, Y increases downward
        // Cocoa: origin bottom-left, Y increases upward
        // On a 1080p screen: Quartz (100, 200) → Cocoa (100, 880)
        let screenHeight: CGFloat = 1080
        let quartzPoint = CGPoint(x: 100, y: 200)
        let cocoaPoint = CoordinateConverter.quartzToCocoa(quartzPoint, screenHeight: screenHeight)
        #expect(cocoaPoint.x == 100)
        #expect(cocoaPoint.y == 880) // 1080 - 200
    }

    @Test func quartzToCocoaConversionAtOrigin() {
        let screenHeight: CGFloat = 1440
        let quartzPoint = CGPoint(x: 0, y: 0)
        let cocoaPoint = CoordinateConverter.quartzToCocoa(quartzPoint, screenHeight: screenHeight)
        #expect(cocoaPoint.x == 0)
        #expect(cocoaPoint.y == 1440)
    }

    @Test func quartzToCocoaConversionAtBottom() {
        let screenHeight: CGFloat = 900
        let quartzPoint = CGPoint(x: 500, y: 900)
        let cocoaPoint = CoordinateConverter.quartzToCocoa(quartzPoint, screenHeight: screenHeight)
        #expect(cocoaPoint.x == 500)
        #expect(cocoaPoint.y == 0)
    }

    @Test func overlayPositionUpperLeftOfCaret() {
        // Overlay should appear to the left and near the top of the caret
        let caretRect = CGRect(x: 300, y: 500, width: 2, height: 20)
        let position = OverlayPositioner.position(for: caretRect, overlaySize: NSSize(width: 70, height: 40))
        #expect(position.x < caretRect.origin.x) // Left of the caret
        // Position should be near the caret vertically (within 20px)
        #expect(abs(position.y - caretRect.origin.y) < 20)
    }

    @Test func overlayPositionClampsToScreenLeft() {
        // Caret near left edge — overlay shouldn't go off-screen
        let caretRect = CGRect(x: -10, y: 500, width: 2, height: 20)
        let position = OverlayPositioner.position(for: caretRect, overlaySize: NSSize(width: 40, height: 40))
        #expect(position.x >= 0)
    }

    @Test func overlayPositionClampsToScreenBottom() {
        // Caret near bottom edge — overlay should flip above
        let caretRect = CGRect(x: 300, y: 10, width: 2, height: 20)
        let position = OverlayPositioner.position(for: caretRect, overlaySize: NSSize(width: 40, height: 40))
        #expect(position.y >= 0)
    }
}
