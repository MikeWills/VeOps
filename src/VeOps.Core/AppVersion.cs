using System.Reflection;
using System.Text.RegularExpressions;

namespace VeOps.Core;

/// <summary>
/// The build's version as shown in the UI footer.
/// </summary>
public static partial class AppVersion
{
    /// <summary>
    /// A released build is stamped by CI with the pushed git tag as its whole informational
    /// version. Tags are calendar versions, <c>YYYY.MM.PATCH</c> (<c>2026.09.0</c>, month
    /// zero-padded, patch counting from 0 each month) — so a four-digit year up front is what
    /// distinguishes a release from a local/untagged build carrying the SDK's default 1.0.0.
    /// Tags before 2026-09-12 were <c>v0.42.1</c>-style semver; none of those is ever re-deployed,
    /// so they are not recognised here.
    /// </summary>
    public static string Display { get; } = FromInformationalVersion(
        (Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty);

    public static string FromInformationalVersion(string informational)
    {
        // The SDK appends "+{commit sha}" to whatever InformationalVersion was set.
        var plus = informational.IndexOf('+');
        var version = plus >= 0 ? informational[..plus] : informational;
        var commit = plus >= 0 ? informational[(plus + 1)..] : string.Empty;

        if (CalendarVersion().IsMatch(version))
        {
            return version;
        }

        return commit.Length >= 7 ? $"pre-release ({commit[..7]})" : "pre-release";
    }

    [GeneratedRegex(@"^\d{4}\.\d{2}\.\d+$")]
    private static partial Regex CalendarVersion();
}
