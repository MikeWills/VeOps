using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// Both shared layouts' footers link back to the project source (2026-09-07).
///
/// <para>The repo went public in August, and the README is written for a stranger who might
/// self-host — but a deployed instance had no path back to the source at all. The footer showed a
/// name, a version and a privacy link, and nothing else. Signed-in users could reach GitHub through
/// the nav menu's "Support" item; <c>_PublicLayout</c> has no nav at all, so a candidate-facing page
/// was a dead end.</para>
///
/// <para><b>MIT does not require this.</b> Its attribution clause binds copies of the software — the
/// notice shipping in <c>LICENSE</c> already satisfies it — and says nothing about the running UI.
/// Mike's call was a source link only, not the license text or an "MIT" label: the license is one
/// click away once somebody is at the repo.</para>
///
/// <para>Scanned rather than rendered because the footer is markup with no behaviour. A rendering
/// test would need a signed-in principal for <c>_AppLayout</c> and would assert the same string.</para>
/// </summary>
public class FooterSourceLinkTests
{
    private const string SourceUrl = "https://github.com/MikeWills/VeOps";

    private static string SharedPagesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "VeOps.Web")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "Could not locate the repository root.");
        return Path.Combine(directory!.FullName, "src", "VeOps.Web", "Pages", "Shared");
    }

    [Theory]
    [InlineData("_AppLayout.cshtml")]
    [InlineData("_PublicLayout.cshtml")]
    public void EveryLayoutFooterLinksToTheSource(string layoutFileName)
    {
        var path = Path.Combine(SharedPagesRoot(), layoutFileName);
        Assert.True(File.Exists(path), $"{layoutFileName} not found at {path}.");

        var markup = File.ReadAllText(path);
        var footerStart = markup.IndexOf("<footer", StringComparison.Ordinal);
        Assert.True(footerStart >= 0, $"{layoutFileName} has no <footer>.");

        var footerEnd = markup.IndexOf("</footer>", footerStart, StringComparison.Ordinal);
        Assert.True(footerEnd > footerStart, $"{layoutFileName}'s <footer> is not closed.");

        var footer = markup[footerStart..footerEnd];
        Assert.Contains(SourceUrl, footer, StringComparison.Ordinal);
    }

    /// <summary>
    /// An outbound link opened in a new tab needs <c>rel="noopener"</c> — without it the opened page
    /// gets a handle on this one through <c>window.opener</c>. The nav menu's Support link already
    /// does this; the footer's is the same shape and must not be the exception.
    /// </summary>
    [Theory]
    [InlineData("_AppLayout.cshtml")]
    [InlineData("_PublicLayout.cshtml")]
    public void TheSourceLinkOpensSafely(string layoutFileName)
    {
        var markup = File.ReadAllText(Path.Combine(SharedPagesRoot(), layoutFileName));
        var footerStart = markup.IndexOf("<footer", StringComparison.Ordinal);
        var footer = markup[footerStart..markup.IndexOf("</footer>", footerStart, StringComparison.Ordinal)];

        var linkStart = footer.IndexOf(SourceUrl, StringComparison.Ordinal);
        var tagStart = footer.LastIndexOf('<', linkStart);
        var tag = footer[tagStart..(footer.IndexOf('>', linkStart) + 1)];

        Assert.Contains("rel=", tag, StringComparison.Ordinal);
        Assert.Contains("noopener", tag, StringComparison.Ordinal);
    }
}
