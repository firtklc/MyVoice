import Foundation
import whisper

enum WhisperEngineError: Error {
    case modelNotFound(String)
    case modelLoadFailed(String)
    case transcriptionFailed(Int32)
    case audioReadFailed(String)
}

final class WhisperEngine {
    private var context: OpaquePointer?

    init(modelPath: String) throws {
        guard FileManager.default.fileExists(atPath: modelPath) else {
            throw WhisperEngineError.modelNotFound(modelPath)
        }
        var params = whisper_context_default_params()
        params.flash_attn = true
        guard let ctx = whisper_init_from_file_with_params(modelPath, params) else {
            throw WhisperEngineError.modelLoadFailed(modelPath)
        }
        self.context = ctx
    }

    deinit {
        if let context {
            whisper_free(context)
        }
    }

    func transcribe(wavFileURL: URL, language: String) throws -> String {
        guard let context else {
            throw WhisperEngineError.modelLoadFailed("No context")
        }

        let data = try Data(contentsOf: wavFileURL)
        guard data.count > 44 else {
            throw WhisperEngineError.audioReadFailed("WAV file too small")
        }

        let samples: [Float] = stride(from: 44, to: data.count, by: 2).map { i in
            data[i..<i+2].withUnsafeBytes {
                let short = Int16(littleEndian: $0.load(as: Int16.self))
                return max(-1.0, min(Float(short) / 32767.0, 1.0))
            }
        }

        var fparams = whisper_full_default_params(WHISPER_SAMPLING_GREEDY)
        fparams.print_realtime   = false
        fparams.print_progress   = false
        fparams.print_timestamps = false
        fparams.print_special    = false
        fparams.n_threads        = Int32(max(1, min(8, ProcessInfo.processInfo.processorCount - 2)))

        // `fparams.language` is an UnsafePointer<CChar> read by whisper_full;
        // the backing storage must outlive that call. withCString guarantees it.
        let result: Int32 = language.withCString { langCStr in
            fparams.language = langCStr
            return samples.withUnsafeBufferPointer { buf in
                whisper_full(context, fparams, buf.baseAddress, Int32(buf.count))
            }
        }

        guard result == 0 else {
            throw WhisperEngineError.transcriptionFailed(result)
        }

        var transcription = ""
        for i in 0..<whisper_full_n_segments(context) {
            if let text = whisper_full_get_segment_text(context, i) {
                transcription += String(cString: text)
            }
        }

        return transcription.trimmingCharacters(in: .whitespaces)
    }
}
