using MyVoice.Windows.Core;

namespace MyVoice.Windows.Tests;

public sealed class AppSettingsTests : IDisposable
{
    readonly string _path = Path.Combine(Path.GetTempPath(), $"myvoice-settings-{Guid.NewGuid()}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void MissingFileGivesDefaultsWithoutAProblem()
    {
        var s = AppSettings.Load(_path, out var problem);
        Assert.Equal("auto", s.Language);
        Assert.False(s.Debug);
        Assert.Null(problem);
    }

    [Fact]
    public void RoundTrips()
    {
        new AppSettings { Language = "tr", Debug = true }.Save(_path);
        Assert.Equal(new AppSettings { Language = "tr", Debug = true }, AppSettings.Load(_path, out _));
    }

    [Fact]
    public void CorruptFileGivesDefaultsAndSaysSo()
    {
        File.WriteAllText(_path, "{ nope");
        var s = AppSettings.Load(_path, out var problem);
        Assert.Equal(new AppSettings(), s);
        Assert.NotNull(problem);
    }

    [Fact]
    public void UnknownLanguageFallsBackToAuto()
    {
        File.WriteAllText(_path, """{"language": "klingon", "debug": true}""");
        var s = AppSettings.Load(_path, out _);
        Assert.Equal("auto", s.Language);
        Assert.True(s.Debug);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        File.WriteAllText(_path, """{"language": "en", "somethingNew": 1}""");
        Assert.Equal("en", AppSettings.Load(_path, out var problem).Language);
        Assert.Null(problem);
    }

    [Fact]
    public void SaveCreatesTheFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"myvoice-{Guid.NewGuid()}");
        var path = Path.Combine(dir, "settings.json");
        try
        {
            new AppSettings { Language = "en" }.Save(path);
            Assert.Equal("en", AppSettings.Load(path, out _).Language);
        }
        finally { Directory.Delete(dir, true); }
    }
}
