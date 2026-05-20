import Testing
import Foundation
@testable import MyVoice

struct LanguagePreferenceTests {
    private func freshDefaults(_ suite: String = UUID().uuidString) -> UserDefaults {
        let d = UserDefaults(suiteName: suite)!
        d.removePersistentDomain(forName: suite)
        return d
    }

    @Test func defaultIsAutoWhenKeyAbsent() {
        let defaults = freshDefaults()
        #expect(LanguagePreference.load(from: defaults) == .auto)
    }

    @Test func roundTripPersistence() {
        let defaults = freshDefaults()
        LanguagePreference.english.save(to: defaults)
        #expect(LanguagePreference.load(from: defaults) == .english)

        LanguagePreference.turkish.save(to: defaults)
        #expect(LanguagePreference.load(from: defaults) == .turkish)

        LanguagePreference.auto.save(to: defaults)
        #expect(LanguagePreference.load(from: defaults) == .auto)
    }

    @Test func invalidRawValueFallsBackToAuto() {
        let defaults = freshDefaults()
        defaults.set("klingon", forKey: LanguagePreference.userDefaultsKey)
        #expect(LanguagePreference.load(from: defaults) == .auto)
    }

    @Test func rawValuesMatchWhisperCodes() {
        #expect(LanguagePreference.auto.rawValue == "auto")
        #expect(LanguagePreference.english.rawValue == "en")
        #expect(LanguagePreference.turkish.rawValue == "tr")
    }

    @Test func displayNamesAreHumanReadable() {
        #expect(LanguagePreference.auto.displayName == "Auto-detect")
        #expect(LanguagePreference.english.displayName == "English")
        #expect(LanguagePreference.turkish.displayName == "Türkçe")
    }

    @Test func allCasesCoversThreeOptions() {
        #expect(LanguagePreference.allCases.count == 3)
        #expect(LanguagePreference.allCases.contains(.auto))
        #expect(LanguagePreference.allCases.contains(.english))
        #expect(LanguagePreference.allCases.contains(.turkish))
    }
}
