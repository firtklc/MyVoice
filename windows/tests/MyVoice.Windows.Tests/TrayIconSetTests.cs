using System.Drawing;
using MyVoice.Windows.Core;
using MyVoice.Windows.Platform;

namespace MyVoice.Windows.Tests;

public class TrayIconSetTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void EveryStateHasAnIconOfTheTraySize(int size)
    {
        using var icons = new TrayIconSet(new Size(size, size));
        foreach (var kind in Enum.GetValues<TrayIconKind>())
            Assert.Equal(new Size(size, size), icons[kind].Size);
    }

    [Fact]
    public void BadgesTellTheStatesApart()
    {
        // The badge sits bottom-right: sample its fill in each state.
        const int size = 32;
        using var app = TrayIconSet.AppImage(size);
        var centres = Enum.GetValues<TrayIconKind>().ToDictionary(k => k, k =>
        {
            using var bitmap = TrayIconSet.Compose(app, k, size);
            var badge = TrayIconSet.BadgeBounds(size);
            return bitmap.GetPixel(badge.Left + badge.Width / 4 + 1, badge.Top + badge.Height / 2).ToArgb(); // beside the error's "!"
        });
        Assert.Equal(centres.Count, centres.Values.Distinct().Count());
    }

    [Fact]
    public void ReadyIsThePlainAppIcon()
    {
        const int size = 24;
        using var app = TrayIconSet.AppImage(size);
        using var ready = TrayIconSet.Compose(app, TrayIconKind.Ready, size);
        for (var x = 0; x < size; x += 3)
        for (var y = 0; y < size; y += 3)
            Assert.Equal(app.GetPixel(x, y), ready.GetPixel(x, y));
    }
}

public class KeyLabelsTests
{
    [Theory]
    [InlineData(Keys.OemPeriod)]
    [InlineData(Keys.Oemcomma)]
    public void PunctuationKeysTypeSomethingOnThisLayout(Keys key) => Assert.False(string.IsNullOrWhiteSpace(KeyLabels.ForCurrentLayout(key)));

    [Fact]
    public void NonCharacterKeysHaveNoLabel() => Assert.Null(KeyLabels.ForCurrentLayout(Keys.F5));
}
