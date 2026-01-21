using CodeNOW.Cli.Common.Console.Presentation;
using Xunit;

namespace CodeNOW.Cli.Tests.Common.Console.Presentation;

public class BannerVisibilityPolicyTests
{
    [Fact]
    public void ShouldShowBanner_ReturnsTrueForHelp()
    {
        var policy = CreatePolicy();

        Assert.True(policy.ShouldShowBanner(["--help"]));
        Assert.True(policy.ShouldShowBanner(["dp", "dashboard", "--help"]));
    }

    [Fact]
    public void ShouldShowBanner_HidesForDashboardByDefault()
    {
        var policy = CreatePolicy();

        Assert.False(policy.ShouldShowBanner(["dp", "dashboard"]));
    }

    [Fact]
    public void ShouldShowBanner_ShowsForUnknownCommand()
    {
        var policy = CreatePolicy();

        Assert.True(policy.ShouldShowBanner(["dp", "unknown"]));
    }

    private static BannerVisibilityPolicy CreatePolicy()
    {
        return new BannerVisibilityPolicy(
            [
                new CommandDescriptor("dp", "bootstrap", HideBanner: false),
                new CommandDescriptor("dp", "dashboard", HideBanner: true)
            ]);
    }
}
