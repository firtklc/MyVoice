import Foundation

final class DictionaryReplacer {
    private let dictionary: [(pattern: NSRegularExpression, replacement: String)]

    init(dictionary: [String: String]) {
        self.dictionary = dictionary.compactMap { key, value in
            guard let regex = try? NSRegularExpression(
                pattern: "\\b\(NSRegularExpression.escapedPattern(for: key))\\b",
                options: [.caseInsensitive]
            ) else { return nil }
            return (regex, value)
        }
    }

    convenience init(jsonData: Data) throws {
        let dict = try JSONDecoder().decode([String: String].self, from: jsonData)
        self.init(dictionary: dict)
    }

    convenience init(jsonFileURL: URL) throws {
        let data = try Data(contentsOf: jsonFileURL)
        try self.init(jsonData: data)
    }

    func replace(_ text: String) -> String {
        var result = text
        for entry in dictionary {
            result = entry.pattern.stringByReplacingMatches(
                in: result,
                range: NSRange(result.startIndex..., in: result),
                withTemplate: entry.replacement
            )
        }
        return result
    }
}
