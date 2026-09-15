using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.VolunteerExaminers;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// The VE Directory's tag filter takes several tags at once, so "the regulars and the guests" is one
/// list. Two URL shapes have to bind to the same array: the filter form's repeated
/// <c>tagNames=a&amp;tagNames=b</c>, and the indexed <c>tagNames[0]=a&amp;tagNames[1]=b</c> that
/// <c>VeDirectoryFilterRoute</c> emits because a route dictionary cannot repeat a key. If either
/// stopped binding, the round trip list → VE → back would silently lose the filter.
/// </summary>
public class VeDirectoryTagFilterTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public VeDirectoryTagFilterTests(WebAppFactory factory) => _factory = factory;

    private const string TagName = "Filter Test Tag";

    private async Task SeedTagAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teamId = (await db.Teams.AsNoTracking().FirstAsync()).Id;

        if (!await db.VeTags.AnyAsync(t => t.TeamId == teamId && t.Name == TagName))
        {
            db.VeTags.Add(new VeTag { TeamId = teamId, Name = TagName });
            await db.SaveChangesAsync();
        }
    }

    private static bool IsChecked(string html, string value) =>
        Regex.IsMatch(html, $"""name="tagNames"\s+value="{Regex.Escape(value)}"\s+checked""");

    [Theory]
    [InlineData("tagNames=Filter%20Test%20Tag&tagNames=%20guest")]
    [InlineData("tagNames%5B0%5D=Filter%20Test%20Tag&tagNames%5B1%5D=%20guest")]
    public async Task BothQueryShapes_TickTheChosenTagsAndCountThemOnTheTrigger(string query)
    {
        await SeedTagAsync();
        using var client = _factory.CreateClientAs(UserRole.SystemAdmin);

        var html = await client.GetStringAsync($"/SessionManager/VeDirectory?{query}");

        Assert.True(IsChecked(html, TagName), "The named tag should be ticked.");
        Assert.True(IsChecked(html, VolunteerExaminerDirectoryService.GuestTagFilter), "Guests should be ticked.");
        Assert.Contains("Tags: 2 selected", html);
    }

    [Fact]
    public async Task OneTag_IsNamedOnTheTrigger()
    {
        await SeedTagAsync();
        using var client = _factory.CreateClientAs(UserRole.SystemAdmin);

        var html = await client.GetStringAsync("/SessionManager/VeDirectory?tagNames=Filter%20Test%20Tag");

        Assert.Contains($"Tags: {TagName}", html);
    }

    /// <summary>The row link into VE detail carries every ticked tag, so coming back lands on the same list.</summary>
    [Fact]
    public async Task RouteBuilder_EmitsOneIndexedKeyPerTag()
    {
        var route = VeDirectoryFilterRoute.Build(null, null, ["A", VolunteerExaminerDirectoryService.GuestTagFilter], false, null, null, null, null);

        Assert.Equal("A", route["tagNames[0]"]);
        Assert.Equal(VolunteerExaminerDirectoryService.GuestTagFilter, route["tagNames[1]"]);
    }
}
