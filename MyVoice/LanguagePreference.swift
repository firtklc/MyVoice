import Foundation

enum LanguagePreference: String, CaseIterable, Identifiable {
    case auto = "auto"
    case english = "en"
    case turkish = "tr"

    static let userDefaultsKey = "languagePreference"

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .auto: "Auto-detect"
        case .english: "English"
        case .turkish: "Türkçe"
        }
    }

    static func load(from defaults: UserDefaults = .standard) -> LanguagePreference {
        guard let raw = defaults.string(forKey: userDefaultsKey),
              let value = LanguagePreference(rawValue: raw) else {
            return .auto
        }
        return value
    }

    func save(to defaults: UserDefaults = .standard) {
        defaults.set(rawValue, forKey: Self.userDefaultsKey)
    }
}
