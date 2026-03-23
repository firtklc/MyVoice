import AppKit
import AVFoundation
import SwiftUI

/// Floating overlay that appears near the text cursor during recording.
@MainActor
final class RecordingOverlay {
    private var panel: NSPanel?
    private let overlaySize = NSSize(width: 70, height: 40)
    let audioLevel = AudioLevelMonitor()
    private var meteringTimer: Timer?

    func show() {
        let caretPosition = CursorLocator.getCaretPosition()
        let caretRect = CGRect(origin: caretPosition, size: CGSize(width: 2, height: 20))
        let position = OverlayPositioner.position(for: caretRect, overlaySize: overlaySize)

        if panel == nil {
            createPanel()
        }

        panel?.setFrameOrigin(position)
        panel?.orderFront(nil)
    }

    func hide() {
        meteringTimer?.invalidate()
        meteringTimer = nil
        audioLevel.level = 0
        panel?.orderOut(nil)
    }

    func startMetering(recorder: AVAudioRecorder) {
        recorder.isMeteringEnabled = true
        meteringTimer = Timer.scheduledTimer(withTimeInterval: 0.05, repeats: true) { [weak self] _ in
            Task { @MainActor in
                guard let self else { return }
                recorder.updateMeters()
                let db = recorder.averagePower(forChannel: 0) // -160 to 0
                // Normalize to 0.0 - 1.0 range
                let normalized = max(0, min(1, (db + 50) / 50))
                self.audioLevel.level = normalized
            }
        }
    }

    private func createPanel() {
        let panel = NSPanel(
            contentRect: NSRect(origin: .zero, size: overlaySize),
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isFloatingPanel = true
        panel.backgroundColor = .clear
        panel.isOpaque = false
        panel.hasShadow = false
        panel.ignoresMouseEvents = true
        panel.hidesOnDeactivate = false
        panel.level = .floating
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

        let hostingView = NSHostingView(rootView: OverlayIndicatorView(audioLevel: audioLevel))
        hostingView.frame = NSRect(origin: .zero, size: overlaySize)
        panel.contentView = hostingView

        self.panel = panel
    }
}

/// Animated sound wave indicator — bars respond to audio level.
struct OverlayIndicatorView: View {
    @ObservedObject var audioLevel: AudioLevelMonitor

    private let barCount = 7
    private let barWidth: CGFloat = 3
    private let barSpacing: CGFloat = 2
    private let maxBarHeight: CGFloat = 20
    private let minBarHeight: CGFloat = 4

    var body: some View {
        HStack(spacing: barSpacing) {
            ForEach(0..<barCount, id: \.self) { index in
                RoundedRectangle(cornerRadius: 1.5)
                    .fill(.white)
                    .frame(width: barWidth, height: barHeight(for: index))
                    .animation(.easeInOut(duration: 0.1), value: audioLevel.level)
            }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 8)
        .background(
            RoundedRectangle(cornerRadius: 10)
                .fill(Color.blue)
        )
    }

    private func barHeight(for index: Int) -> CGFloat {
        let center = barCount / 2
        let distanceFromCenter = abs(index - center)
        let centerBias = 1.0 - (Double(distanceFromCenter) / Double(center + 1)) * 0.4

        let level = Double(audioLevel.level)
        let height = minBarHeight + (maxBarHeight - minBarHeight) * level * centerBias

        // Add slight variation per bar
        let variation = sin(Double(index) * 1.8 + level * 10) * 0.15 + 1.0
        return min(maxBarHeight, max(minBarHeight, height * variation))
    }
}

/// Monitors microphone audio levels via AVAudioRecorder metering.
@MainActor
final class AudioLevelMonitor: ObservableObject {
    @Published var level: Float = 0.0
}
