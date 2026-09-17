using System.Net;
using System.Text.RegularExpressions;
using VeOps.Core.Entities;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// The user manual in <c>docs/wiki</c> is served inside the app at <c>/Help</c> (2026-09-17) —
/// the same Markdown files the GitHub wiki is built from, rendered at request time, so there is
/// still exactly one master copy. Public like the wiki is: the candidate guide is written for
/// people who cannot sign in.
/// </summary>
public class HelpPagesTests
{
    private static readonly string WikiDir = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "docs", "wiki"));

    [Fact]
    public async Task Landing_RendersTheWikiHomePage_SignedOut()
    {
        using var factory = new WebAppFactory();
        using var client = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/Help");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Start with your role", html);
    }

    [Fact]
    public async Task SiblingLinks_PointAtHelpPages_NotAtMdFiles()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.SessionManager);

        var html = await client.GetStringAsync("/Help");

        Assert.Contains("href=\"/Help/Guide-Session-Manager\"", html);
        // A relative .md target would be a link the wiki build rewrote and this one did not.
        Assert.DoesNotMatch(@"href=""[^""h][^""]*\.md""", html);
    }

    [Fact]
    public async Task DocsLinks_PointAtGitHub_AndImagesAtTheImageRoute()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.SessionManager);

        var html = await client.GetStringAsync("/Help/Guide-System-Admin");

        Assert.Contains("https://github.com/MikeWills/VeOps/blob/main/docs/", html);
        var sessions = await client.GetStringAsync("/Help/Guide-Session-Manager");
        Assert.Matches(@"src=""/Help/images/[a-z0-9-]+\.png""", sessions);
    }

    [Fact]
    public async Task Image_IsServedAsPng()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.SessionManager);

        var response = await client.GetAsync("/Help/images/session-detail.png");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("/Help/Nope")]
    [InlineData("/Help/images/nope.png")]
    [InlineData("/Help/images/..%2FGlossary.md")]
    [InlineData("/Help/..%2F..%2Fappsettings.json")]
    public async Task UnknownOrTraversingName_Is404(string url)
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.SessionManager);

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task HelpMenu_LinksToDocumentation()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.SessionManager);

        var html = await client.GetStringAsync("/SessionManager");

        Assert.Contains("href=\"/Help\"", html);
        Assert.DoesNotContain("title=\"Coming soon\"", html);
    }

    /// <summary>Every page in the folder renders, and every sibling link on it resolves — a dead
    /// link in the wiki used to be found by a reader.</summary>
    [Fact]
    public async Task EveryWikiPage_Renders_AndHasNoDeadSiblingLinks()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.SessionManager);
        var pages = Directory.GetFiles(WikiDir, "*.md").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        Assert.NotEmpty(pages);

        foreach (var page in pages)
        {
            var url = page == "README" ? "/Help" : $"/Help/{page}";
            var response = await client.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url} returned {(int)response.StatusCode}");
            var html = await response.Content.ReadAsStringAsync();
            foreach (Match m in Regex.Matches(html, "href=\"/Help/([A-Za-z0-9-]+)"))
            {
                var target = m.Groups[1].Value;
                Assert.True(target == "images" || pages.Contains(target), $"{url} links to missing page {target}");
            }
        }
    }
}
