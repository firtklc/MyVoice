import Testing
@testable import VoiceInk

struct DictionaryReplacerTests {
    @Test func replacesExactWord() {
        let replacer = DictionaryReplacer(dictionary: ["cloud": "Claude"])
        #expect(replacer.replace("Hello cloud") == "Hello Claude")
    }

    @Test func replacesCaseInsensitive() {
        let replacer = DictionaryReplacer(dictionary: ["cloud": "Claude"])
        #expect(replacer.replace("Hello Cloud") == "Hello Claude")
    }

    @Test func replacesMultipleOccurrences() {
        let replacer = DictionaryReplacer(dictionary: ["cloud": "Claude"])
        #expect(replacer.replace("cloud said hello to cloud") == "Claude said hello to Claude")
    }

    @Test func respectsWordBoundaries() {
        let replacer = DictionaryReplacer(dictionary: ["cloud": "Claude"])
        #expect(replacer.replace("cloudy weather") == "cloudy weather")
        #expect(replacer.replace("icloud service") == "icloud service")
        #expect(replacer.replace("thundercloud") == "thundercloud")
    }

    @Test func multipleReplacements() {
        let replacer = DictionaryReplacer(dictionary: [
            "cloud": "Claude",
            "kay": "K8s"
        ])
        #expect(replacer.replace("cloud and kay") == "Claude and K8s")
    }

    @Test func noMatchReturnsOriginal() {
        let replacer = DictionaryReplacer(dictionary: ["cloud": "Claude"])
        #expect(replacer.replace("Hello world") == "Hello world")
    }

    @Test func emptyDictionary() {
        let replacer = DictionaryReplacer(dictionary: [:])
        #expect(replacer.replace("Hello cloud") == "Hello cloud")
    }

    @Test func emptyInput() {
        let replacer = DictionaryReplacer(dictionary: ["cloud": "Claude"])
        #expect(replacer.replace("") == "")
    }

    @Test func punctuationAdjacent() {
        let replacer = DictionaryReplacer(dictionary: ["cloud": "Claude"])
        #expect(replacer.replace("Hello, cloud!") == "Hello, Claude!")
        #expect(replacer.replace("cloud.") == "Claude.")
        #expect(replacer.replace("(cloud)") == "(Claude)")
    }

    @Test func preservesSurroundingCase() {
        let replacer = DictionaryReplacer(dictionary: ["cloud": "Claude"])
        #expect(replacer.replace("CLOUD is great") == "Claude is great")
    }

    @Test func loadsFromJSON() throws {
        let json = """
        {"cloud": "Claude", "kay": "K8s"}
        """
        let data = json.data(using: .utf8)!
        let replacer = try DictionaryReplacer(jsonData: data)
        #expect(replacer.replace("cloud and kay") == "Claude and K8s")
    }

    @Test func handlesInvalidJSON() {
        let data = "not json".data(using: .utf8)!
        let replacer = try? DictionaryReplacer(jsonData: data)
        #expect(replacer == nil)
    }

    @Test func handlesEmptyJSON() throws {
        let data = "{}".data(using: .utf8)!
        let replacer = try DictionaryReplacer(jsonData: data)
        #expect(replacer.replace("cloud") == "cloud")
    }
}
