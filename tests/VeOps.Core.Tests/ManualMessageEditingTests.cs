using Microsoft.EntityFrameworkCore;
using VeOps.Core.Admin;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.Messaging;
using Xunit;

namespace VeOps.Core.Tests;

/// <summary>
/// A message on a <b>manual</b> trigger can be edited (2026-09-09, reported live: "That trigger
/// cannot send to that recipient", on a form whose recipient dropdown was empty).
///
/// <para>Manual triggers carry <c>LegalRecipients: []</c> on purpose — a hand-composed message is
/// addressed at send time by picking people on the compose screen, so there is no recipient to choose
/// on the rule. But <c>ValidateAsync</c> asked whether the posted recipient was in that list, and
/// nothing is in an empty list: <b>every save of every manual message was refused</b>, including one
/// that only changed the subject or the wording. The four seeded manual messages — felony disclosure
/// instructions, youth program instructions, and the two by-hand ones — were therefore uneditable in
/// the admin UI from the day they shipped.</para>
/// </summary>
public class ManualMessageEditingTests
{
    private static readonly DateTime Now = new(2026, 9, 9, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static MessageRuleAdminService CreateService(AppDbContext dbContext) =>
        new(dbContext, new FixedTimeProvider(Now));

    private static async Task<(Team Team, int UserId)> SeedAsync(AppDbContext dbContext)
    {
        var team = new Team { Name = "TESTTEAM", CreatedUtc = Now };
        var user = new User { Name = "Admin", Email = "admin@example.org", Role = UserRole.TeamAdmin };
        dbContext.Teams.Add(team);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return (team, user.Id);
    }

    private static async Task<MessageRule> SeedManualRuleAsync(AppDbContext dbContext, Team team, MessageTrigger trigger)
    {
        var rule = new MessageRule
        {
            TeamId = team.Id,
            Name = "Youth program instructions",
            Trigger = trigger,
            Subject = "ARRL Youth Program",
            Body = "<p>Congratulations.</p>",
            // Whatever the seeder left here. It is not a question the form asks for a manual message,
            // so an edit must carry it through untouched rather than overwrite it with a posted value.
            Recipient = MessageRecipient.Candidate,
            CreatedUtc = Now.AddMonths(-1)
        };
        dbContext.MessageRules.Add(rule);
        await dbContext.SaveChangesAsync();
        return rule;
    }

    public static IEnumerable<object[]> ManualTriggers() =>
        MessageTriggerDefinitions.All
            .Where(d => d.Mechanism == MessageTriggerMechanism.Manual)
            .Select(d => new object[] { d.Trigger });

    /// <summary>Every manual trigger, so a new one cannot be added with the same hole.</summary>
    [Theory]
    [MemberData(nameof(ManualTriggers))]
    public async Task EditingTheWordingOfAManualMessage_Succeeds(MessageTrigger trigger)
    {
        await using var dbContext = CreateContext();
        var (team, userId) = await SeedAsync(dbContext);
        var rule = await SeedManualRuleAsync(dbContext, team, trigger);

        var result = await CreateService(dbContext).UpdateAsync(
            rule.Id, rule.Name, "A better subject", "<p>Better words.</p>", null,
            // What the form posts when its recipient dropdown had nothing to offer.
            recipient: default, userId, CancellationToken.None);

        Assert.Equal(MessageRuleActionResult.Success, result);
        var saved = await dbContext.MessageRules.SingleAsync();
        Assert.Equal("A better subject", saved.Subject);
        Assert.Equal("<p>Better words.</p>", saved.Body);
    }

    /// <summary>
    /// The stored recipient survives the edit. The form cannot ask the question, so an empty answer
    /// must not be treated as one — least of all by writing whichever enum value happens to be 0.
    /// </summary>
    [Fact]
    public async Task EditingAManualMessage_LeavesTheStoredRecipientAlone()
    {
        await using var dbContext = CreateContext();
        var (team, userId) = await SeedAsync(dbContext);
        var rule = await SeedManualRuleAsync(dbContext, team, MessageTrigger.ManualYouthProgramInstructions);
        rule.Recipient = MessageRecipient.SessionLead;
        await dbContext.SaveChangesAsync();

        await CreateService(dbContext).UpdateAsync(
            rule.Id, rule.Name, "Subject", "<p>Body.</p>", null, recipient: MessageRecipient.Candidate,
            userId, CancellationToken.None);

        Assert.Equal(MessageRecipient.SessionLead, (await dbContext.MessageRules.SingleAsync()).Recipient);
    }

    /// <summary>
    /// The check still bites where it means something. A scanned trigger has a real list of legal
    /// recipients, and posting one that is not on it is still refused — this is not "stop validating".
    /// </summary>
    [Fact]
    public async Task AScannedTrigger_StillRefusesARecipientItCannotAddress()
    {
        await using var dbContext = CreateContext();
        var (team, userId) = await SeedAsync(dbContext);
        var definition = MessageTriggerDefinitions.For(MessageTrigger.FelonyDisclosureDeclared);
        var illegal = Enum.GetValues<MessageRecipient>().First(r => !definition.LegalRecipients.Contains(r));

        var result = await CreateService(dbContext).CreateAsync(
            team.Id, MessageTrigger.FelonyDisclosureDeclared, "A rule", "Subject", "<p>Body.</p>",
            null, illegal, userId, CancellationToken.None);

        Assert.Equal(MessageRuleActionResult.RecipientNotLegal, result);
    }

    /// <summary>Creating a manual message works too — the same validation gate refuses both paths.</summary>
    [Fact]
    public async Task CreatingAManualMessage_Succeeds()
    {
        await using var dbContext = CreateContext();
        var (team, userId) = await SeedAsync(dbContext);

        var result = await CreateService(dbContext).CreateAsync(
            team.Id, MessageTrigger.ManualToCandidate, "By hand", "Subject", "<p>Body.</p>",
            null, recipient: default, userId, CancellationToken.None);

        Assert.Equal(MessageRuleActionResult.Success, result);
    }
}
