using CodexQuotaWidget.Services;
using Xunit;

namespace CodexQuotaWidget.Tests;

public sealed class ComposerThemeStabilizerTests
{
    [Fact]
    public void KeepsStableLightThemeForASingleBlackSample()
    {
        var stabilizer = new ComposerThemeStabilizer();
        _ = stabilizer.Observe(isLightBackground: true, backgroundRgb: 0xFAFAFA);

        var decision = stabilizer.Observe(isLightBackground: false, backgroundRgb: 0x000000);

        Assert.True(decision.RequiresConfirmation);
        Assert.Equal(new ComposerTheme(true, 0xFAFAFA), decision.Theme);
        Assert.Equal(decision.Theme, stabilizer.Current);
    }

    [Fact]
    public void AcceptsAStableDarkThemeAfterTwoConsistentSamples()
    {
        var stabilizer = new ComposerThemeStabilizer();
        _ = stabilizer.Observe(isLightBackground: true, backgroundRgb: 0xFAFAFA);

        var first = stabilizer.Observe(isLightBackground: false, backgroundRgb: 0x212121);
        var second = stabilizer.Observe(isLightBackground: false, backgroundRgb: 0x202020);

        Assert.True(first.RequiresConfirmation);
        Assert.False(second.RequiresConfirmation);
        Assert.Equal(new ComposerTheme(false, 0x202020), second.Theme);
    }

    [Fact]
    public void RestoredStableThemeCancelsAPendingBlackSample()
    {
        var stabilizer = new ComposerThemeStabilizer();
        _ = stabilizer.Observe(isLightBackground: true, backgroundRgb: 0xFAFAFA);
        _ = stabilizer.Observe(isLightBackground: false, backgroundRgb: 0x000000);

        var restored = stabilizer.Observe(isLightBackground: true, backgroundRgb: 0xF9F9F9);
        var nextBlack = stabilizer.Observe(isLightBackground: false, backgroundRgb: 0x000000);

        Assert.False(restored.RequiresConfirmation);
        Assert.Equal(new ComposerTheme(true, 0xF9F9F9), restored.Theme);
        Assert.True(nextBlack.RequiresConfirmation);
        Assert.Equal(restored.Theme, nextBlack.Theme);
    }

    [Fact]
    public void AcceptsAnOrdinaryInitialDarkSampleImmediately()
    {
        var stabilizer = new ComposerThemeStabilizer();

        var decision = stabilizer.Observe(isLightBackground: false, backgroundRgb: 0x212121);

        Assert.False(decision.RequiresConfirmation);
        Assert.Equal(new ComposerTheme(false, 0x212121), decision.Theme);
    }

    [Fact]
    public void RequiresTwoSamplesBeforeAcceptingAnInitialPureBlackTheme()
    {
        var stabilizer = new ComposerThemeStabilizer();

        var first = stabilizer.Observe(isLightBackground: false, backgroundRgb: 0x000000);
        var second = stabilizer.Observe(isLightBackground: false, backgroundRgb: 0x000000);

        Assert.True(first.RequiresConfirmation);
        Assert.Equal(new ComposerTheme(true, 0xFAFAFA), first.Theme);
        Assert.False(second.RequiresConfirmation);
        Assert.Equal(new ComposerTheme(false, 0x000000), second.Theme);
    }
}
