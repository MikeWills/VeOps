using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.ExamTools;
using VeOps.Core.VecSubmissions;
using Xunit;

namespace VeOps.Core.Tests;

/// <summary>
/// Building the ARRL submission preview (issue #197).
///
/// <para><b>This carries most of the coverage for the whole feature, deliberately.</b> There is no
/// sandbox on ARRL's side and no dry-run: the POST itself can only ever be exercised by filing a
/// real session with a real VEC. The preview is the part that *can* be tested, so what it resolves
/// has to be pinned here.</para>
/// </summary>
public class ArrlSubmissionPreviewServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>The real MARC session behind the receipt on #197: 01:30 UTC on the 22nd is the evening of the 21st in Eastern.</summary>
    private static readonly DateTime SessionStartUtc = new(2026, 4, 22, 1, 30, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class FakeArchiveClient : IExamToolsClient
    {
        public VecArchiveDownload Result { get; set; } =
            VecArchiveDownload.Succeeded([1, 2, 3, 4], "ExamSession_MARC_20260422_0130_arrl.zip");

        public string? RequestedVecCode { get; private set; }
        public string? RequestedSessionId { get; private set; }
        public int Calls { get; private set; }

        public Task<VecArchiveDownload> DownloadVecArchiveAsync(ExamToolsCredentials credentials, string examToolsSessionId, string vecCode, CancellationToken cancellationToken)
        {
            Calls++;
            RequestedSessionId = examToolsSessionId;
            RequestedVecCode = vecCode;
            return Task.FromResult(Result);
        }

        public Task<IReadOnlyList<ExamToolsSession>> GetTeamSessionsAsync(ExamToolsCredentials c, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExamToolsSession>> GetTeamClosedSessionsAsync(ExamToolsCredentials c, DateOnly s, DateOnly e, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExamToolsApplicant>> GetSessionApplicantsAsync(ExamToolsCredentials c, string s, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<ExamToolsVe>> GetSessionVeRosterAsync(ExamToolsCredentials c, string s, CancellationToken ct) => throw new NotSupportedException();
        public Task<ExamToolsApplicantDetail?> GetApplicantDetailAsync(ExamToolsCredentials c, string s, string a, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class World
    {
        public required AppDbContext Db { get; init; }
        public required FakeArchiveClient Client { get; init; }
        public required Session Session { get; init; }
        public required Team Team { get; init; }
        public string GlobalBaseUrl { get; init; } = "https://exam.tools";

        public ArrlSubmissionPreviewService Service => new(
            Db, Client, Options.Create(new ExamToolsOptions { BaseUrl = GlobalBaseUrl }),
            NullLogger<ArrlSubmissionPreviewService>.Instance);

        public Task<ArrlSubmissionPreview> BuildAsync() =>
            Service.BuildAsync(Session.Id, CancellationToken.None);
    }

    private static async Task<World> SeedAsync(
        string vecName = "ARRL",
        Action<Team>? configureTeam = null,
        Action<Session>? configureSession = null,
        string globalBaseUrl = "https://exam.tools")
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var team = new Team
        {
            Name = "MARC",
            CreatedUtc = Now,
            ExamToolsTeamCode = "MARC",
            ExamToolsUsername = "ve@example.org",
            ExamToolsPassword = "pw",
            ArrlSubmissionNamePostfix = null,
            ArrlSubmissionEmailSource = ArrlSubmissionEmailSource.SessionLead,
            ArrlSubmissionLocation = "Remote Online",
            ArrlSubmissionPaymentMethod = ArrlPaymentMethod.CreditCardOnFile
        };
        configureTeam?.Invoke(team);

        var vec = new Vec { Name = vecName };
        var fee = new FeeConfiguration
        {
            Vec = vec, EffectiveDate = new DateTime(2026, 1, 1), FeeCollectionEnabled = true,
            ExamFeeAmount = 15m, RetainedAmount = 7m, YouthExamFeeAmount = 5m, CreatedUtc = Now,
            CreatedByUser = new User { Name = "Seed", Role = UserRole.SystemAdmin }
        };

        var lead = new VolunteerExaminer
        {
            Name = "Mike Wills", CallSign = "WX0MIK", Email = "wx0mik@gmail.com",
            Phone = "5073814969", CreatedUtc = Now
        };

        var session = new Session
        {
            ExamToolsSessionId = "6950a2cbf593f706d2e92247",
            Title = "Testing for Minnesotans",
            Team = team, Vec = vec, FeeConfiguration = fee,
            TeamLeadCallSign = "WX0MIK",
            ScheduledStartUtc = SessionStartUtc,
            DurationMinutes = 120,
            ExamToolsClosedUtc = Now,
            CreatedUtc = Now
        };
        configureSession?.Invoke(session);

        db.AddRange(team, vec, fee, lead, session);
        await db.SaveChangesAsync();

        return new World { Db = db, Client = new FakeArchiveClient(), Session = session, Team = team, GlobalBaseUrl = globalBaseUrl };
    }

    private static void AddPaidCandidate(World world, decimal amount, Action<Payment>? configure = null)
    {
        var candidate = new Candidate
        {
            Session = world.Session, ExamToolsApplicantId = Guid.NewGuid().ToString(),
            Name = "Test Candidate", FirstName = "Test", DateRegisteredUtc = Now
        };
        var payment = new Payment { Candidate = candidate, Amount = amount, Status = PaymentStatus.Paid };
        configure?.Invoke(payment);
        world.Db.AddRange(candidate, payment);
        world.Db.SaveChanges();
    }

    // ---- The happy path ---------------------------------------------------------------------

    [Fact]
    public async Task EveryFieldResolvesFromTheTeamAndTheSessionLead()
    {
        var world = await SeedAsync();
        AddPaidCandidate(world, 15m);

        var preview = await world.BuildAsync();

        Assert.Equal(ArrlSubmissionPreviewStatus.Ready, preview.Status);
        Assert.Equal("Mike Wills", preview.FullName);
        Assert.Equal("WX0MIK", preview.CallSign);
        Assert.Equal("wx0mik@gmail.com", preview.Email);
        Assert.Equal("5073814969", preview.Phone);
        Assert.Equal("Remote Online", preview.Location);
        Assert.Equal(ArrlPaymentMethod.CreditCardOnFile, preview.PaymentMethod);
        Assert.Empty(preview.MissingRequiredFields);
        Assert.True(preview.CanSubmit);
    }

    /// <summary>
    /// The session ran on the evening of 21 April Eastern and starts at 01:30 UTC on the 22nd. Using
    /// <c>.Date</c> would file the 22nd — the #248 bug class, which is wrong for ~80% of this
    /// deployment's sessions. The real receipt says <c>2026-04-21</c>.
    /// </summary>
    [Fact]
    public async Task TheSessionDateIsTheEasternCalendarDate_NotTheUtcOne()
    {
        var world = await SeedAsync();

        var preview = await world.BuildAsync();

        Assert.Equal("2026-04-21", preview.SessionDate);
    }

    /// <summary>HRCC's real value opens with a slash and no space, so nothing may be inserted between the two.</summary>
    [Fact]
    public async Task ThePostfixIsAppendedVerbatim_WithNoSeparator()
    {
        var world = await SeedAsync(configureTeam: t => t.ArrlSubmissionNamePostfix = "/Nick Booth (CC)/HRCC VE Team");

        var preview = await world.BuildAsync();

        Assert.Equal("Mike Wills/Nick Booth (CC)/HRCC VE Team", preview.FullName);
    }

    [Fact]
    public async Task ATeamAddressOverridesTheLeadsEmail()
    {
        var world = await SeedAsync(configureTeam: t =>
        {
            t.ArrlSubmissionEmailSource = ArrlSubmissionEmailSource.TeamAddress;
            t.ArrlSubmissionEmail = "vec@marcradio.org";
        });

        var preview = await world.BuildAsync();

        Assert.Equal("vec@marcradio.org", preview.Email);
    }

    // ---- The amount -------------------------------------------------------------------------

    /// <summary>
    /// Two candidates at $15 with $7 retained each is $16 remitted — which is exactly what HRCC's real
    /// receipt shows for a two-candidate session.
    /// </summary>
    [Fact]
    public async Task TheAmountIsTheRemitToVecTotal_FormattedWithoutADollarSign()
    {
        var world = await SeedAsync();
        AddPaidCandidate(world, 15m);
        AddPaidCandidate(world, 15m);

        var preview = await world.BuildAsync();

        Assert.Equal("16.00", preview.AmountCharged);
        Assert.Equal(30m, preview.Fees!.TotalCollected);
        Assert.Equal(16m, preview.Fees.TotalRemitToVec);
    }

    /// <summary>
    /// A refund does not move a payment off Paid (#375, deliberately — otherwise the "unpaid and no
    /// link" scan would issue a fresh checkout link), so the total still counts it. Surfaced rather
    /// than silently corrected: only a human knows whether the candidate was filed.
    /// </summary>
    [Fact]
    public async Task ARefundedPaymentIsFlagged()
    {
        var world = await SeedAsync();
        AddPaidCandidate(world, 15m, p => p.Refunds.Add(new Refund
        {
            Team = world.Team, AmountUsd = 15m, Status = RefundStatus.Completed,
            RequestedUtc = Now, SquarePaymentId = "sq-pay-1", SquareIdempotencyKey = "idem-1", SquareRefundId = "r1"
        }));

        var preview = await world.BuildAsync();

        Assert.Contains(preview.AmountWarnings, w => w.Contains("refund", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The out-of-band youth path: Square reports $5 while Amount stays $15, so the remit is computed
    /// on money that never arrived.
    /// </summary>
    [Fact]
    public async Task AnAmountMismatchIsFlagged()
    {
        var world = await SeedAsync();
        AddPaidCandidate(world, 15m, p =>
        {
            p.SquareAmountPaidUsd = 5m;
            p.AmountMismatchFlaggedUtc = Now;
        });

        var preview = await world.BuildAsync();

        Assert.Contains(preview.AmountWarnings, w => w.Contains("differ", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AnOrdinarySessionHasNoAmountWarnings()
    {
        var world = await SeedAsync();
        AddPaidCandidate(world, 15m);

        var preview = await world.BuildAsync();

        Assert.Empty(preview.AmountWarnings);
    }

    /// <summary>A youth-rate payment is when ARRL also wants the grant program form — the second of the two files.</summary>
    [Fact]
    public async Task AYouthRatePaymentExpectsTheGrantForm()
    {
        var world = await SeedAsync();
        AddPaidCandidate(world, 5m);

        var preview = await world.BuildAsync();

        Assert.True(preview.YouthFormExpected);
    }

    [Fact]
    public async Task AStandardRateSessionDoesNotExpectTheGrantForm()
    {
        var world = await SeedAsync();
        AddPaidCandidate(world, 15m);

        var preview = await world.BuildAsync();

        Assert.False(preview.YouthFormExpected);
    }

    // ---- Missing required values -------------------------------------------------------------

    /// <summary>
    /// ExamTools supplies no contact details at all and the VE retention purge clears them, so a lead
    /// with no phone on record is a real, ordinary state — named individually so the operator knows
    /// which box to fill rather than being told "something is missing".
    /// </summary>
    [Fact]
    public async Task ALeadWithNoPhone_IsNamedAndBlocksSubmission()
    {
        var world = await SeedAsync();
        var lead = await world.Db.VolunteerExaminers.SingleAsync();
        lead.Phone = null;
        await world.Db.SaveChangesAsync();

        var preview = await world.BuildAsync();

        Assert.Contains(preview.MissingRequiredFields, f => f.Contains("phone", StringComparison.OrdinalIgnoreCase));
        Assert.False(preview.CanSubmit);
    }

    /// <summary>A session whose lead call sign matches no VE record leaves four fields empty at once, and must say so rather than rendering a form of blanks.</summary>
    [Fact]
    public async Task AnUnresolvableLead_NamesEveryFieldItWouldHaveFilled()
    {
        var world = await SeedAsync(configureSession: s => s.TeamLeadCallSign = "N0BODY");

        var preview = await world.BuildAsync();

        Assert.Contains(preview.MissingRequiredFields, f => f.Contains("name", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(preview.MissingRequiredFields, f => f.Contains("call sign", StringComparison.OrdinalIgnoreCase));
        Assert.False(preview.CanSubmit);
    }

    // ---- Gates ------------------------------------------------------------------------------

    /// <summary>
    /// One submitter, no default (#197's first constraint). A session under another VEC finds nothing
    /// rather than being handed ARRL's.
    /// </summary>
    [Fact]
    public async Task ANonArrlSession_IsRefusedRatherThanHandedTheArrlSubmitter()
    {
        var world = await SeedAsync(vecName: "GLAARG");

        var preview = await world.BuildAsync();

        Assert.Equal(ArrlSubmissionPreviewStatus.NotAnArrlSession, preview.Status);
        Assert.False(preview.CanSubmit);
        Assert.Equal(0, world.Client.Calls);
    }

    [Fact]
    public async Task AnUnconfiguredTeam_IsToldSoBeforeAnythingIsFetched()
    {
        var world = await SeedAsync(configureTeam: t => t.ArrlSubmissionLocation = null);

        var preview = await world.BuildAsync();

        Assert.Equal(ArrlSubmissionPreviewStatus.TeamNotConfigured, preview.Status);
        Assert.Equal(0, world.Client.Calls);
    }

    /// <summary>A team on ExamTools' test site is practicing with test data — it must never be able to file with ARRL, whatever this deployment's own environment is.</summary>
    [Fact]
    public async Task ATeamOnExamToolsTestSite_IsRefusedBeforeAnythingIsFetched()
    {
        var world = await SeedAsync(globalBaseUrl: "https://examtools.dev");

        var preview = await world.BuildAsync();

        Assert.Equal(ArrlSubmissionPreviewStatus.TeamOnTestExamTools, preview.Status);
        Assert.False(preview.CanSubmit);
        Assert.Equal(0, world.Client.Calls);
    }

    /// <summary>A team's own per-team override to the test site is caught the same way as the deployment default being test.</summary>
    [Fact]
    public async Task ATeamsOwnOverrideToTheTestSite_IsAlsoRefused()
    {
        var world = await SeedAsync(configureTeam: t => t.ExamToolsBaseUrl = "https://examtools.dev");

        var preview = await world.BuildAsync();

        Assert.Equal(ArrlSubmissionPreviewStatus.TeamOnTestExamTools, preview.Status);
    }

    [Fact]
    public async Task AnAlreadySubmittedSession_CannotBeSubmittedAgain()
    {
        var world = await SeedAsync(configureSession: s => s.VecSubmissionStatus = VecSubmissionStatus.Submitted);

        var preview = await world.BuildAsync();

        Assert.True(preview.AlreadySubmitted);
        Assert.False(preview.CanSubmit);
    }

    // ---- The archive ------------------------------------------------------------------------

    /// <summary>
    /// <b>The preview does not download anything.</b> It used to pull the whole ~377KB archive on
    /// every render just to show a name and a byte count, so ExamTools' audit log recorded a download
    /// per page view plus one per submit — three for one filing, reported 2026-09-09.
    ///
    /// <para>Mike: "You can check to see if the session is closed without downloading the file." The
    /// app already knows: <c>ExamToolsClosedUtc</c> is stamped by ingestion. So readiness is answered
    /// locally and the name is rebuilt locally, leaving exactly one download, at the moment of
    /// filing.</para>
    /// </summary>
    [Fact]
    public async Task ThePreviewNamesTheArchiveWithoutDownloadingIt()
    {
        var world = await SeedAsync();

        var preview = await world.BuildAsync();

        Assert.Equal(0, world.Client.Calls);
        Assert.Equal(VecArchiveDownloadOutcome.Succeeded, preview.ArchiveOutcome);
        Assert.Equal("ExamSession_MARC_20260422_0130_arrl.zip", preview.ArchiveFileName);
        Assert.True(preview.CanSubmit);
    }

    /// <summary>
    /// The commonest expected failure, and self-correcting. Answered from
    /// <c>ExamToolsClosedUtc</c> — deliberately that, and not <c>IsCompleted</c>, which is also true
    /// when a Session Manager clicked "Mark session completed". A person marking it does not make
    /// ExamTools produce an archive.
    /// </summary>
    [Fact]
    public async Task ASessionExamToolsHasNotClosed_BlocksSubmission_WithoutAsking()
    {
        var world = await SeedAsync(configureSession: session => session.ExamToolsClosedUtc = null);

        var preview = await world.BuildAsync();

        Assert.Equal(0, world.Client.Calls);
        Assert.Equal(VecArchiveDownloadOutcome.SessionNotComplete, preview.ArchiveOutcome);
        Assert.False(preview.CanSubmit);
    }

    /// <summary>
    /// A Session Manager marking the session completed is not ExamTools closing it, so it must not
    /// unlock filing on its own — the archive would not exist.
    /// </summary>
    [Fact]
    public async Task MarkedCompletedByHandButNotClosedByExamTools_StillBlocksSubmission()
    {
        var world = await SeedAsync(configureSession: session =>
        {
            session.ExamToolsClosedUtc = null;
            session.TestingCompletedUtc = Now;
        });

        var preview = await world.BuildAsync();

        Assert.Equal(VecArchiveDownloadOutcome.SessionNotComplete, preview.ArchiveOutcome);
        Assert.False(preview.CanSubmit);
    }

    /// <summary>
    /// The one download, at the moment of filing. Its filename still comes from
    /// Content-Disposition when ExamTools sends one — the preview's locally-built name is for
    /// display, and what is stored beside the filing is what actually went.
    /// </summary>
    [Fact]
    public async Task FilingDownloadsTheArchiveExactlyOnce()
    {
        var world = await SeedAsync();

        var archive = await world.Service.FetchArchiveFileAsync(world.Session.Id, CancellationToken.None);

        Assert.Equal(1, world.Client.Calls);
        Assert.Equal("6950a2cbf593f706d2e92247", world.Client.RequestedSessionId);
        Assert.Equal("arrl", world.Client.RequestedVecCode, ignoreCase: true);
        Assert.Equal("ExamSession_MARC_20260422_0130_arrl.zip", archive!.FileName);
    }

    /// <summary>
    /// Content-Disposition normally supplies the name at filing time. When it does not, the
    /// descriptive one is rebuilt rather than falling back to the URL's, which is identical for
    /// every session of every team.
    /// </summary>
    [Fact]
    public async Task WithNoFilenameFromExamTools_TheDescriptiveOneIsRebuilt()
    {
        var world = await SeedAsync();
        world.Client.Result = VecArchiveDownload.Succeeded([1, 2, 3, 4], fileName: null);

        var archive = await world.Service.FetchArchiveFileAsync(world.Session.Id, CancellationToken.None);

        Assert.Equal("ExamSession_MARC_20260422_0130_arrl.zip", archive!.FileName);
    }

    [Fact]
    public async Task AnUnknownSession_IsNotFound()
    {
        var world = await SeedAsync();

        var preview = await world.Service.BuildAsync(999999, CancellationToken.None);

        Assert.Equal(ArrlSubmissionPreviewStatus.SessionNotFound, preview.Status);
    }
}
