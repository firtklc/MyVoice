using MyVoice.Windows.Core;

namespace MyVoice.Windows.Tests;

// Ported one-to-one from MyVoiceTests/LanguagePreferenceTests.swift; persistence goes through settings.json.
public sealed class LanguagePreferenceTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"myvoice-language-{Guid.NewGuid()}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void DefaultIsAutoWhenKeyAbsent()
    {
        File.WriteAllText(_path, """{"debug": false}""");
        Assert.Equal(LanguagePreference.Auto, LanguagePreference.FromCode(AppSettings.Load(_path, out _).Language));
        Assert.Equal(LanguagePreference.Auto, LanguagePreference.FromCode(null));
    }

    [Fact]
    public void RoundTripPersistence()
    {
        foreach (var preference in new[] { LanguagePreference.English, LanguagePreference.Turkish, LanguagePreference.Auto })
        {
            new AppSettings { Language = preference.Code }.Save(_path);
            Assert.Equal(preference, LanguagePreference.FromCode(AppSettings.Load(_path, out _).Language));
        }
    }

    [Fact]
    public void InvalidRawValueFallsBackToAuto() => Assert.Equal(LanguagePreference.Auto, LanguagePreference.FromCode("klingon"));

    [Fact]
    public void RawValuesMatchWhisperCodes()
    {
        Assert.Equal("auto", LanguagePreference.Auto.Code);
        Assert.Equal("en", LanguagePreference.English.Code);
        Assert.Equal("tr", LanguagePreference.Turkish.Code);
    }

    [Fact]
    public void DisplayNamesAreHumanReadable()
    {
        Assert.Equal("Auto-detect", LanguagePreference.Auto.DisplayName);
        Assert.Equal("English", LanguagePreference.English.DisplayName);
        Assert.Equal("Türkçe", LanguagePreference.Turkish.DisplayName);
    }

    [Fact]
    public void AllCasesCoversThreeOptions()
    {
        Assert.Equal(3, LanguagePreference.All.Count);
        Assert.Contains(LanguagePreference.Auto, LanguagePreference.All);
        Assert.Contains(LanguagePreference.English, LanguagePreference.All);
        Assert.Contains(LanguagePreference.Turkish, LanguagePreference.All);
    }
}
