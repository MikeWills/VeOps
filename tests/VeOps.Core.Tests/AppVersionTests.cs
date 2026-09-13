using VeOps.Core;

namespace VeOps.Core.Tests;

/// <summary>
/// The footer shows the CI-stamped tag for a release and "pre-release" for anything else. Release
/// tags are calendar versions (<c>YYYY.MM.PATCH</c>, 2026-09-12); the SDK's untagged default is
/// <c>1.0.0</c>, which must never read as a release.
/// </summary>
public class AppVersionTests
{
    [Theory]
    [InlineData("2026.09.0+abc1234def", "2026.09.0")]
    [InlineData("2026.09.12+abc1234def", "2026.09.12")]
    [InlineData("2027.01.3", "2027.01.3")]
    public void CalendarVersionTag_IsShownAsIs(string informational, string expected)
    {
        Assert.Equal(expected, AppVersion.FromInformationalVersion(informational));
    }

    [Theory]
    [InlineData("1.0.0+abc1234def", "pre-release (abc1234)")]
    [InlineData("1.0.0", "pre-release")]
    [InlineData("", "pre-release")]
    [InlineData("v0.42.1+abc1234def", "pre-release (abc1234)")]
    [InlineData("2026.9.0+abc1234def", "pre-release (abc1234)")]
    public void AnythingElse_IsPreRelease(string informational, string expected)
    {
        Assert.Equal(expected, AppVersion.FromInformationalVersion(informational));
    }
}
