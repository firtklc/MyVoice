using System.Globalization;
using System.Text.Json;
using MyVoice.Windows.Core;

namespace MyVoice.Windows.Tests;

// The first 13 cases are ported one-to-one from MyVoiceTests/DictionaryReplacerTests.swift.
public class DictionaryReplacerTests
{
    static DictionaryReplacer With(params (string Key, string Value)[] entries) =>
        new(entries.ToDictionary(e => e.Key, e => e.Value));

    [Fact] public void ReplacesExactWord() => Assert.Equal("Hello Claude", With(("cloud", "Claude")).Replace("Hello cloud"));

    [Fact] public void ReplacesCaseInsensitive() => Assert.Equal("Hello Claude", With(("cloud", "Claude")).Replace("Hello Cloud"));

    [Fact]
    public void ReplacesMultipleOccurrences() =>
        Assert.Equal("Claude said hello to Claude", With(("cloud", "Claude")).Replace("cloud said hello to cloud"));

    [Fact]
    public void RespectsWordBoundaries()
    {
        var r = With(("cloud", "Claude"));
        Assert.Equal("cloudy weather", r.Replace("cloudy weather"));
        Assert.Equal("icloud service", r.Replace("icloud service"));
        Assert.Equal("thundercloud", r.Replace("thundercloud"));
    }

    [Fact]
    public void MultipleReplacements() =>
        Assert.Equal("Claude and K8s", With(("cloud", "Claude"), ("kay", "K8s")).Replace("cloud and kay"));

    [Fact] public void NoMatchReturnsOriginal() => Assert.Equal("Hello world", With(("cloud", "Claude")).Replace("Hello world"));

    [Fact] public void EmptyDictionary() => Assert.Equal("Hello cloud", DictionaryReplacer.Empty.Replace("Hello cloud"));

    [Fact] public void EmptyInput() => Assert.Equal("", With(("cloud", "Claude")).Replace(""));

    [Fact]
    public void PunctuationAdjacent()
    {
        var r = With(("cloud", "Claude"));
        Assert.Equal("Hello, Claude!", r.Replace("Hello, cloud!"));
        Assert.Equal("Claude.", r.Replace("cloud."));
        Assert.Equal("(Claude)", r.Replace("(cloud)"));
    }

    [Fact] public void PreservesSurroundingCase() => Assert.Equal("Claude is great", With(("cloud", "Claude")).Replace("CLOUD is great"));

    [Fact]
    public void LoadsFromJson() =>
        Assert.Equal("Claude and K8s", DictionaryReplacer.FromJson("""{"cloud": "Claude", "kay": "K8s"}""").Replace("cloud and kay"));

    [Fact] public void HandlesInvalidJson() => Assert.ThrowsAny<JsonException>(() => DictionaryReplacer.FromJson("not json"));

    [Fact] public void HandlesEmptyJson() => Assert.Equal("cloud", DictionaryReplacer.FromJson("{}").Replace("cloud"));

    // ---- Windows additions ----

    [Fact]
    public void RegexCharactersInKeysAreLiteral() =>
        Assert.Equal("use node.js here, not nodexjs", With(("node.js", "node.js")).Replace("use NODE.JS here, not nodexjs"));

    [Fact]
    public void DollarSignInReplacementStaysLiteral() => Assert.Equal("costs $5 now", With(("five", "$5")).Replace("costs five now"));

    [Fact]
    public void TurkishLettersAreWordCharacters()
    {
        var r = With(("ağ", "network"), ("şirket", "Şirket A.Ş."));
        Assert.Equal("ağaç ve network", r.Replace("ağaç ve ağ"));          // "ağaç" is a different word
        Assert.Equal("Şirket A.Ş. toplantısı", r.Replace("şirket toplantısı"));
        Assert.Equal("Şirket A.Ş. toplantısı", r.Replace("Şirket toplantısı")); // Ş/ş fold without Turkish culture rules
    }

    [Fact]
    public void AcronymMatchesUnderTurkishCulture()
    {
        // tr-TR maps I↔ı and İ↔i, so a culture-sensitive IgnoreCase would miss "AI" for the key "ai".
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var r = With(("ai", "AI"), ("claude ai", "Claude AI"));
            Assert.Equal("AI tools", r.Replace("Ai tools"));
            Assert.Equal("I use AI daily", r.Replace("I use ai daily"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void JsonWithNonStringValueIsInvalid() =>
        Assert.ThrowsAny<JsonException>(() => DictionaryReplacer.FromJson("""{"cloud": 5}"""));

    [Fact]
    public void LoadMissingFileGivesEmptyReplacer()
    {
        var r = DictionaryReplacer.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), out var status);
        Assert.Equal(0, r.Count);
        Assert.Contains("No dictionary", status);
    }

    [Fact]
    public void LoadInvalidFileGivesEmptyReplacer()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var r = DictionaryReplacer.Load(path, out var status);
            Assert.Equal(0, r.Count);
            Assert.Contains("not valid", status);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadValidFileReportsCount()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, """{"cloud": "Claude", "Cloud": "Claude"}""");
        try
        {
            var r = DictionaryReplacer.Load(path, out var status);
            Assert.Equal("Hey Claude", r.Replace("Hey Cloud"));
            Assert.Contains("2 entries", status);
        }
        finally { File.Delete(path); }
    }
}
