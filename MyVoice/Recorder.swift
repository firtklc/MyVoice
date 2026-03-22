import Foundation
import AVFoundation

enum RecorderError: Error {
    case couldNotStartRecording
}

final class Recorder {
    private var audioRecorder: AVAudioRecorder?
    private(set) var isRecording = false
    private(set) var recordingURL: URL?

    private let recordSettings: [String: Any] = [
        AVFormatIDKey: Int(kAudioFormatLinearPCM),
        AVSampleRateKey: 16000.0,
        AVNumberOfChannelsKey: 1,
        AVLinearPCMBitDepthKey: 16,
        AVLinearPCMIsFloatKey: false,
        AVLinearPCMIsBigEndianKey: false,
        AVEncoderAudioQualityKey: AVAudioQuality.high.rawValue
    ]

    func startRecording() throws -> URL {
        let url = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("myvoice-recording-\(UUID().uuidString).wav")

        let recorder = try AVAudioRecorder(url: url, settings: recordSettings)
        guard recorder.record() else {
            throw RecorderError.couldNotStartRecording
        }

        self.audioRecorder = recorder
        self.recordingURL = url
        self.isRecording = true
        return url
    }

    func stopRecording() -> URL? {
        audioRecorder?.stop()
        audioRecorder = nil
        isRecording = false
        return recordingURL
    }

    func cleanup() {
        if let url = recordingURL {
            try? FileManager.default.removeItem(at: url)
            recordingURL = nil
        }
    }
}
