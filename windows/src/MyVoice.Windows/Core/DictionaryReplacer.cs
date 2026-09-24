using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyVoice.Windows.Core;

/// <summary>Port of DictionaryReplacer.swift: whole-word, case-insensitive replacements from ~/.myvoice/dictionary.json.</summary>
public sealed class DictionaryReplacer
{
    readonly (Regex Pattern, string Replacement)[] _entries;

    public static DictionaryReplacer Empty => new(new Dictionary<string, string>());

    // CultureInvariant: under a Turkish system locale a culture-aware IgnoreCase maps I↔ı, so "ai" would miss "AI".
    public DictionaryReplacer(IReadOnlyDictionary<string, string> dictionary) =>
        _entries = dictionary
            .Where(e => e.Key.Length > 0)
            .Select(e => (new Regex($@"\b{Regex.Escape(e.Key)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), e.Value))
            .ToArray();

    public int Count => _entries.Length;

    /// <summary>Throws <see cref="JsonException"/> when the JSON is not a string→string object.</summary>
    public static DictionaryReplacer FromJson(string json) =>
        new(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? throw new JsonException("dictionary.json contains null"));

    /// <summary>Missing or invalid file → no replacements (Mac behaviour); <paramref name="status"/> says which.</summary>
    public static DictionaryReplacer Load(string path, out string status)
    {
        if (!File.Exists(path))
        {
            status = $"No dictionary at {path} — no replacements";
            return Empty;
        }
        try
        {
            var replacer = FromJson(File.ReadAllText(path));
            status = $"Dictionary loaded: {replacer.Count} entries";
            return replacer;
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            status = $"Dictionary at {path} is not valid ({e.Message}) — no replacements";
            return Empty;
        }
    }

    public string Replace(string text)
    {
        foreach (var (pattern, replacement) in _entries)
            text = pattern.Replace(text, _ => replacement); // evaluator, so "$1" in a replacement stays literal
        return text;
    }
}
