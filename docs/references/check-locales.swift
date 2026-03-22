import Speech

@main struct Check {
    static func main() async {
        let locales = await SpeechTranscriber.supportedLocales
        print("Supported locales (\(locales.count)):")
        for l in locales.sorted(by: { $0.identifier < $1.identifier }) {
            let t = SpeechTranscriber(locale: l, preset: .progressiveTranscription)
            let status = await AssetInventory.status(forModules: [t])
            let marker = status == .installed ? "✅" : (status == .supported ? "📦" : "❌")
            print("  \(marker) \(l.identifier) — \(status)")
        }
    }
}
