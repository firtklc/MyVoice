namespace MyVoice.Windows.Tests;

public class OptionsTests
{
    [Fact]
    public void NoArgumentsIsANormalStart() => Assert.Equal(new Options(null, null, 0, null), Options.Parse([]));

    [Fact]
    public void ParsesTheSimulationSwitches() =>
        Assert.Equal(new Options("a.wav", "Target 1", 2.5, null), Options.Parse(["--simulate", "a.wav", "--target", "Target 1", "--stall", "2.5"]));

    [Fact]
    public void StallUsesADecimalPointWhateverTheLocale()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal(2.5, Options.Parse(["--simulate", "a.wav", "--target", "T", "--stall", "2.5"]).StallSeconds);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [Fact]
    public void SimulationWithoutATargetIsAnError() =>
        Assert.Contains("--target", Options.Parse(["--simulate", "a.wav"]).Error);

    [Fact]
    public void BadStallIsAnErrorNotACrash() =>
        Assert.Contains("--stall", Options.Parse(["--simulate", "a.wav", "--target", "T", "--stall", "2,5x"]).Error);

    [Fact]
    public void ASwitchMissingItsValueIsAnError() =>
        Assert.Contains("--simulate", Options.Parse(["--simulate"]).Error);

    [Fact]
    public void UnknownArgumentsAreAnError() =>
        Assert.Contains("--nope", Options.Parse(["--nope"]).Error);
}
