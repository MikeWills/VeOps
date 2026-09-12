using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// The deployment is public on the internet but must not appear in a search engine (Mike,
/// 2026-09-12: "I don't want Google indexing this"). Two layers, because they stop different
/// things: <c>robots.txt</c> stops a well-behaved crawler fetching pages at all, and
/// <c>X-Robots-Tag</c> stops a URL being indexed even when the engine learned it from a link
/// elsewhere and never crawled it — <c>robots.txt</c> alone leaves that second case open.
/// </summary>
public class NoIndexingTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public NoIndexingTests(WebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task RobotsTxt_IsAnonymousAndDisallowsEverything()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync("/robots.txt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("User-agent: *", body);
        Assert.Contains("Disallow: /", body);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/Account/Login")]
    [InlineData("/Privacy")]
    [InlineData("/css/app.css")]
    public async Task EveryResponse_CarriesNoIndexHeader(string path)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync(path);

        Assert.True(response.Headers.TryGetValues("X-Robots-Tag", out var values), $"{path} has no X-Robots-Tag");
        Assert.Contains("noindex", string.Join(",", values));
    }
}
