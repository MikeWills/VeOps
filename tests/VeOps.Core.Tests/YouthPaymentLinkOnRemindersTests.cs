using Microsoft.EntityFrameworkCore;
using VeOps.Core.Data;
using VeOps.Core.Email;
using VeOps.Core.Entities;
using VeOps.Core.Messaging;
using Xunit;

namespace VeOps.Core.Tests;

/// <summary>
/// <c>{{YouthPaymentLinkUrl}}</c> on the two triggers that chase a fee before the session —
/// "before a session starts" and "before a session, if the exam fee is still unpaid" (2026-09-08,
/// reported live). Only the registration confirmation offered the youth rate, so a candidate who
/// missed it in that first email had no way back to it from any reminder: every later message
/// pointed exclusively at the standard fee.
///
/// <para>The token renders blank rather than being absent wherever it would lead somewhere useless —
/// a VEC with no youth program, a payment carrying no youth token, or one already settled, whose
/// confirmation page answers "already resolved". A link that lands on a dead page is worse than no
/// link.</para>
/// </summary>
public class YouthPaymentLinkOnRemindersTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid YouthToken = new("11111111-2222-3333-4444-555555555555");

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class FakeEmailSender : IEmailSender
    {
        public List<EmailMessage> SentMessages { get; } = [];

        public Task SendAsync(EmailCredentials credentials, EmailMessage message, CancellationToken cancellationToken)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<Team> SeedTeamAsync(AppDbContext dbContext)
    {
        var team = new Team { Name = "TESTTEAM", CreatedUtc = Now.AddYears(-1), SmtpHost = "smtp.example.org", SmtpUsername = "smtp-user", SmtpPassword = "smtp-pass" };
        dbContext.Teams.Add(team);
        await dbContext.SaveChangesAsync();

        dbContext.EmailSettings.Add(new EmailSettings
        {
            TeamId = team.Id,
            FromAddress = "noreply@example.org",
            ReplyToAddress = "reply@example.org",
            PrivacyPolicyUrl = "https://example.org/privacy",
            AdminNotificationEmail = "admin@example.org"
        });
        await dbContext.SaveChangesAsync();
        return team;
    }

    private static async Task SeedCandidateAsync(
        AppDbContext dbContext, Team team,
        bool supportsYouthProgram = true,
        PaymentStatus paymentStatus = PaymentStatus.Unpaid,
        bool withYouthToken = true)
    {
        var vec = new Vec { Name = "ARRL", SupportsYouthProgram = supportsYouthProgram };
        var user = new User { Name = "System", Email = "system@localhost", Role = UserRole.SystemAdmin };
        var session = new Session
        {
            ExamToolsSessionId = $"session-{Guid.NewGuid():N}", Title = "September Session",
            ScheduledStartUtc = Now.AddHours(12), DurationMinutes = 60, Vec = vec, TeamId = team.Id,
            FeeConfiguration = new FeeConfiguration
            {
                Vec = vec, EffectiveDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FeeCollectionEnabled = true, ExamFeeAmount = 15m, YouthExamFeeAmount = 5m,
                CreatedByUser = user, CreatedUtc = Now
            },
            Status = SessionStatus.Active
        };
        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync();

        var candidate = new Candidate
        {
            ExamToolsApplicantId = $"applicant-{Guid.NewGuid():N}", SessionId = session.Id, Name = "Roana Glory",
            Email = "roana@example.com", DateRegisteredUtc = Now.AddDays(-3)
        };
        dbContext.Candidates.Add(candidate);
        await dbContext.SaveChangesAsync();

        dbContext.Payments.Add(new Payment
        {
            CandidateId = candidate.Id, Reason = PaymentReason.InitialExam, Amount = 15m,
            Status = paymentStatus, PaymentLinkUrl = "https://square.link/u/abc",
            YouthConfirmationToken = withYouthToken ? YouthToken : null, CreatedUtc = Now.AddDays(-3)
        });
        await dbContext.SaveChangesAsync();
    }

    private static async Task<List<EmailMessage>> RunAsync(AppDbContext dbContext, Team team, MessageTrigger trigger, string body)
    {
        dbContext.MessageRules.Add(MessageRuleTestHarness.NewRule(
            team, trigger, body, parameterHours: 24, createdUtc: Now.AddYears(-1)));
        await dbContext.SaveChangesAsync();

        var sender = new FakeEmailSender();
        await MessageRuleTestHarness.Create(dbContext, sender, new FixedTimeProvider(Now))
            .RunAsync(team, [trigger], null, CancellationToken.None);
        return sender.SentMessages;
    }

    private const string YouthBody = "Youth rate: {{YouthPaymentLinkUrl}}.";

    private static string ExpectedLink => $"{MessageRuleTestHarness.PublicBaseUrl}/youth-confirm/{YouthToken}";

    [Theory]
    [InlineData(MessageTrigger.BeforeSessionStart)]
    [InlineData(MessageTrigger.PaymentUnpaidBeforeSession)]
    public async Task YouthProgramVec_WithAnUnpaidFee_RendersTheYouthConfirmLink(MessageTrigger trigger)
    {
        await using var dbContext = CreateContext();
        var team = await SeedTeamAsync(dbContext);
        await SeedCandidateAsync(dbContext, team);

        var messages = await RunAsync(dbContext, team, trigger, YouthBody);

        Assert.Contains(ExpectedLink, Assert.Single(messages).HtmlBody);
    }

    /// <summary>Blank, not a link to a page that would tell the candidate the youth rate is not set up here.</summary>
    [Theory]
    [InlineData(MessageTrigger.BeforeSessionStart)]
    [InlineData(MessageTrigger.PaymentUnpaidBeforeSession)]
    public async Task VecWithoutAYouthProgram_RendersBlank(MessageTrigger trigger)
    {
        await using var dbContext = CreateContext();
        var team = await SeedTeamAsync(dbContext);
        await SeedCandidateAsync(dbContext, team, supportsYouthProgram: false);

        var messages = await RunAsync(dbContext, team, trigger, YouthBody);

        var body = Assert.Single(messages).HtmlBody;
        Assert.DoesNotContain("youth-confirm", body);
        Assert.DoesNotContain("{{YouthPaymentLinkUrl}}", body);
    }

    /// <summary>
    /// No token on the payment — the youth flow never applied to it. Same blank answer as a
    /// non-youth VEC, and specifically not a malformed link with an empty guid in it.
    /// </summary>
    [Theory]
    [InlineData(MessageTrigger.BeforeSessionStart)]
    [InlineData(MessageTrigger.PaymentUnpaidBeforeSession)]
    public async Task APaymentWithNoYouthToken_RendersBlank(MessageTrigger trigger)
    {
        await using var dbContext = CreateContext();
        var team = await SeedTeamAsync(dbContext);
        await SeedCandidateAsync(dbContext, team, withYouthToken: false);

        var messages = await RunAsync(dbContext, team, trigger, YouthBody);

        Assert.DoesNotContain("youth-confirm", Assert.Single(messages).HtmlBody);
    }

    /// <summary>
    /// Only "before a session starts" can reach an already-paid candidate — the unpaid trigger's own
    /// filter excludes them. The youth page answers a settled payment with "already resolved", so
    /// the link must not be offered.
    /// </summary>
    [Fact]
    public async Task BeforeSessionStart_AlreadyPaid_RendersBlank()
    {
        await using var dbContext = CreateContext();
        var team = await SeedTeamAsync(dbContext);
        await SeedCandidateAsync(dbContext, team, paymentStatus: PaymentStatus.Paid);

        var messages = await RunAsync(dbContext, team, MessageTrigger.BeforeSessionStart, YouthBody);

        Assert.DoesNotContain("youth-confirm", Assert.Single(messages).HtmlBody);
    }

    [Theory]
    [InlineData(MessageTrigger.BeforeSessionStart)]
    [InlineData(MessageTrigger.PaymentUnpaidBeforeSession)]
    public void TheTriggerAdvertisesTheToken(MessageTrigger trigger)
    {
        Assert.Contains("YouthPaymentLinkUrl", MessageTriggerDefinitions.For(trigger).Placeholders);
    }

    /// <summary>
    /// The drift this class exists to prevent, generalized: a token a rule's editor offers as a
    /// clickable chip must actually resolve when that trigger fires. An advertised token nothing
    /// fills reaches a candidate as a literal <c>{{YouthPaymentLinkUrl}}</c> — the failure #116 hit
    /// with <c>{{ZoomJoinUrl}}</c> on the per-session digests.
    /// </summary>
    [Theory]
    [InlineData(MessageTrigger.BeforeSessionStart)]
    [InlineData(MessageTrigger.PaymentUnpaidBeforeSession)]
    public async Task EveryAdvertisedToken_ResolvesWhenTheTriggerFires(MessageTrigger trigger)
    {
        await using var dbContext = CreateContext();
        var team = await SeedTeamAsync(dbContext);
        await SeedCandidateAsync(dbContext, team);

        var tokens = MessageTriggerDefinitions.For(trigger).Placeholders;
        var body = string.Join(" | ", tokens.Select(t => t + "=[{{" + t + "}}]"));

        var messages = await RunAsync(dbContext, team, trigger, body);

        Assert.DoesNotContain("{{", Assert.Single(messages).HtmlBody);
    }
}
