// VoiceInk SpeechAnalyzer Spike
// Compile: swiftc voiceink-speechanalyzer-spike.swift -o spike-sa -parse-as-library
// Run: ./spike-sa
// Speak into your mic. Press Ctrl+C to stop.

import Speech
import AVFoundation
import Foundation

@main
struct Spike {
    static func main() async throws {
        // 1. Check locale support
        let locale = Locale(identifier: "en-US")
        let supported = await SpeechTranscriber.supportedLocales
        print("en-US supported: \(supported.contains(where: { $0.identifier == locale.identifier }))")

        // 2. Create transcriber
        let transcriber = SpeechTranscriber(
            locale: locale,
            preset: .progressiveTranscription
        )

        // 3. Check model status
        let status = await AssetInventory.status(forModules: [transcriber])
        print("Model status: \(status)")

        if status == .unsupported {
            print("ERROR: en-US not supported on this device")
            return
        }

        // 4. Get optimal audio format
        guard let optimalFormat = await SpeechAnalyzer.bestAvailableAudioFormat(
            compatibleWith: [transcriber]
        ) else {
            print("ERROR: Could not get optimal audio format")
            print("Model may need to be downloaded first. Status: \(status)")
            if status == .supported {
                print("Requesting model installation...")
                if let request = try await AssetInventory.assetInstallationRequest(supporting: [transcriber]) {
                    print("Installation request created. Please approve in System Settings if prompted.")
                    // Wait a bit and retry
                    try await Task.sleep(for: .seconds(5))
                    guard let fmt = await SpeechAnalyzer.bestAvailableAudioFormat(compatibleWith: [transcriber]) else {
                        print("Still no format available. Model may still be downloading.")
                        return
                    }
                    print("Got format after install: \(fmt)")
                }
            }
            return
        }

        // 5. Set up audio engine
        let engine = AVAudioEngine()
        let inputNode = engine.inputNode
        let inputFormat = inputNode.outputFormat(forBus: 0)

        print("\n🎤 SpeechAnalyzer Spike")
        print("Mic: \(Int(inputFormat.sampleRate))Hz \(inputFormat.channelCount)ch")
        print("Optimal: \(Int(optimalFormat.sampleRate))Hz \(optimalFormat.channelCount)ch")

        // 6. Create async stream for audio input
        let (inputStream, continuation) = AsyncStream.makeStream(of: AnalyzerInput.self)

        // 7. Create analyzer
        let analyzer = SpeechAnalyzer(
            inputSequence: inputStream,
            modules: [transcriber]
        )

        // 8. Prepare
        print("Preparing analyzer...")
        try await analyzer.prepareToAnalyze(in: optimalFormat)
        print("Ready! Speak now... (Ctrl+C to stop)\n")

        // 9. Install mic tap
        if inputFormat.sampleRate == optimalFormat.sampleRate &&
           inputFormat.channelCount == optimalFormat.channelCount {
            inputNode.installTap(onBus: 0, bufferSize: 4096, format: optimalFormat) { buffer, _ in
                continuation.yield(AnalyzerInput(buffer: buffer))
            }
        } else {
            let converter = AVAudioConverter(from: inputFormat, to: optimalFormat)!
            inputNode.installTap(onBus: 0, bufferSize: 4096, format: inputFormat) { buffer, _ in
                let ratio = optimalFormat.sampleRate / inputFormat.sampleRate
                let frameCount = AVAudioFrameCount(Double(buffer.frameLength) * ratio)
                guard let converted = AVAudioPCMBuffer(pcmFormat: optimalFormat, frameCapacity: frameCount) else { return }

                var error: NSError?
                converter.convert(to: converted, error: &error) { _, outStatus in
                    outStatus.pointee = .haveData
                    return buffer
                }
                if error == nil {
                    continuation.yield(AnalyzerInput(buffer: converted))
                }
            }
        }

        // 10. Start
        try engine.start()

        Task {
            try await analyzer.start(inputSequence: inputStream)
        }

        // 11. Read results
        for try await result in transcriber.results {
            let text = String(result.text.characters)
            if result.isFinal {
                print("\n✅ FINAL: \(text)")
            } else {
                print("\u{1B}[2K\r💭 \(text)", terminator: "")
                fflush(stdout)
            }
        }
    }
}
