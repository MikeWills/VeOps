using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// The session roster lists candidates in the order they registered, oldest first (2026-09-15), with
/// a sortable "Registered" column so the order is visible rather than implied. It used to be
/// alphabetical, which answers no question a Session Manager actually has on the day.
/// </summary>
public class SessionDetailRosterOrderTests
{
    [Fact]
    public async Task Roster_ListsCandidatesOldestRegistrationFirst_NotAlphabetically()
    {
        using var factory = new WebAppFactory();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seeded = await db.Candidates.FirstAsync(c => c.Id == factory.Seeded.CandidateId);
            seeded.DateRegisteredUtc = new DateTime(2026, 9, 10, 1, 0, 0, DateTimeKind.Utc);
            // Alphabetically first, registered last — the two orders disagree on purpose.
            db.Candidates.Add(new Candidate
            {
                SessionId = factory.Seeded.SessionId,
                Name = "Aaron Latecomer",
                Email = "aaron@localhost",
                DateRegisteredUtc = new DateTime(2026, 9, 12, 1, 0, 0, DateTimeKind.Utc)
            });
            db.Candidates.Add(new Candidate
            {
                SessionId = factory.Seeded.SessionId,
                Name = "Zoe Early",
                Email = "zoe@localhost",
                DateRegisteredUtc = new DateTime(2026, 9, 1, 1, 0, 0, DateTimeKind.Utc)
            });
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClientAs(UserRole.SessionManager);

        var html = await client.GetStringAsync($"/SessionManager/Detail/{factory.Seeded.SessionId}");

        var zoe = html.IndexOf("Zoe Early", StringComparison.Ordinal);
        var test = html.IndexOf("Test Candidate", StringComparison.Ordinal);
        var aaron = html.IndexOf("Aaron Latecomer", StringComparison.Ordinal);
        Assert.True(zoe > 0 && test > zoe && aaron > test,
            $"expected Zoe < Test < Aaron in the page, got {zoe}/{test}/{aaron}");
    }

    [Fact]
    public async Task Roster_HasARegisteredColumn_SortableByInstant()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.SessionManager);

        var html = await client.GetStringAsync($"/SessionManager/Detail/{factory.Seeded.SessionId}");

        Assert.Contains("<th>Registered</th>", html);
        // ISO-8601 sort value so the column orders as an instant, not as "Sep 1" vs "Aug 30" text.
        Assert.Matches(@"data-sort-value=""\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z""", html);
    }
}
