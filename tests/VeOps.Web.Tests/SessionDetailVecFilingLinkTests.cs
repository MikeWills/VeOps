using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.VecSubmissions;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// Reaching a filing after it has been filed.
///
/// <para>The archive exists for one reason — "an archive of what was sent in case there's ever a
/// question" — so the moment it becomes useful is <i>after</i> submission. The link to it used to
/// live inside the same <c>!VecSubmitted</c> branch as the "Submit to ARRL…" button, which meant it
/// disappeared at exactly that moment, and the only route left was editing the URL by hand
/// (reported 2026-09-09). These tests pin that the link survives the state change.</para>
///
/// <para>They assert against the <b>rendered page</b> rather than scanning the Razor source, because
/// what broke was a conditional: source that mentions the link proves nothing about whether a reader
/// in the submitted state can see it.</para>
/// </summary>
public class SessionDetailVecFilingLinkTests : IClassFixture<WebAppFactory>
{
    private readonly WebAppFactory _factory;

    public SessionDetailVecFilingLinkTests(WebAppFactory factory) => _factory = factory;

    private string DetailUrl => $"/SessionManager/Detail/{_factory.Seeded.SessionId}";

    private string FilingHref => $"/SessionManager/SubmitToVec/{_factory.Seeded.SessionId}";

    /// <summary>
    /// Puts the seeded session in the state the screenshot showed: VEC is ARRL, the session is
    /// Submitted, and a real filing row exists with an archive beside it.
    /// </summary>
    private async Task SeedFiledSessionAsync(bool withFilingRow = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var session = await db.Sessions
            .Include(s => s.Vec)
            .FirstAsync(s => s.Id == _factory.Seeded.SessionId);

        // Nothing to set for IsArrlSession: MatchCode is computed as ExamToolsCode ?? Name, the
        // seeded Vec is named "ARRL", and the comparison is case-insensitive. Asserted rather than
        // assumed, so this stops being silently true if the seed is ever renamed.
        Assert.Equal(
            ArrlSubmissionPreviewService.ArrlMatchCode,
            session.Vec.MatchCode,
            ignoreCase: true);

        session.VecSubmissionStatus = VecSubmissionStatus.Submitted;

        db.ArrlVecSubmissions.RemoveRange(await db.ArrlVecSubmissions.ToListAsync());

        if (withFilingRow)
        {
            db.ArrlVecSubmissions.Add(new ArrlVecSubmission
            {
                SessionId = session.Id,
                TeamId = session.TeamId,
                SubmittedUtc = DateTime.UtcNow.AddDays(-1),
                FullName = "Mike Wills",
                CallSign = "WX0MIK",
                Email = "ve@localhost",
                Phone = "5555550123",
                SessionDate = "2026-09-09",
                Location = "Mankato, MN",
                PaymentMethod = ArrlPaymentMethod.MailIn,
                AmountCharged = "14.00",
                ArchiveFileName = "ExamSession_arrl_archive.zip",
                ArchiveByteCount = 377_000,
                Outcome = ArrlReceiptOutcome.Succeeded
            });
        }

        await db.SaveChangesAsync();
    }

    private async Task ResetAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var session = await db.Sessions
            .Include(s => s.Vec)
            .FirstAsync(s => s.Id == _factory.Seeded.SessionId);

        session.VecSubmissionStatus = VecSubmissionStatus.NotSubmitted;
        db.ArrlVecSubmissions.RemoveRange(await db.ArrlVecSubmissions.ToListAsync());
        await db.SaveChangesAsync();
    }

    /// <summary>The reported bug: filed, and nothing on the page to click.</summary>
    [Fact]
    public async Task SubmittedSession_LinksToTheFiling()
    {
        await SeedFiledSessionAsync();
        try
        {
            var client = _factory.CreateClientAs(UserRole.SystemAdmin);
            var html = await client.GetStringAsync(DetailUrl);

            Assert.Contains(FilingHref, html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await ResetAsync();
        }
    }

    /// <summary>
    /// A TeamLead may read the filing page — it says so itself — so the link has to be rendered for
    /// them too. It used to sit behind <c>CanEdit</c>, which is false for that role, so the one link
    /// in the app was hidden from a reader the page would have served.
    /// </summary>
    [Fact]
    public async Task SubmittedSession_LinksToTheFiling_ForAReadOnlyTeamLead()
    {
        await SeedFiledSessionAsync();
        try
        {
            var client = _factory.CreateClientAs(UserRole.TeamLead);
            var html = await client.GetStringAsync(DetailUrl);

            Assert.Contains(FilingHref, html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await ResetAsync();
        }
    }

    /// <summary>
    /// The other half: a session marked submitted by hand has no filing to show, so linking to one
    /// would land the reader on a page that can only tell them there is nothing there.
    /// </summary>
    [Fact]
    public async Task SubmittedByHandWithNoFiling_ShowsNoLink()
    {
        await SeedFiledSessionAsync(withFilingRow: false);
        try
        {
            var client = _factory.CreateClientAs(UserRole.SystemAdmin);
            var html = await client.GetStringAsync(DetailUrl);

            Assert.DoesNotContain(FilingHref, html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            await ResetAsync();
        }
    }

    /// <summary>Regression: the way <i>in</i> must survive the change to the way back.</summary>
    [Fact]
    public async Task NotYetSubmitted_StillOffersTheSubmitButton()
    {
        await SeedFiledSessionAsync(withFilingRow: false);
        await ResetAsync();

        var client = _factory.CreateClientAs(UserRole.SystemAdmin);
        var html = await client.GetStringAsync(DetailUrl);

        Assert.Contains(FilingHref, html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Submit to ARRL", html, StringComparison.OrdinalIgnoreCase);
    }
}
