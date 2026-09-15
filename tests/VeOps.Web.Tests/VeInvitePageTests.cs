using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// Invite VEs starts from a saved message now (2026-09-14). Mike wanted a VE session reminder
/// pre-filled with the date, the Zoom link and the candidate count; the by-hand VE email has no
/// session to fill those from, and this screen — opened from one session — always did.
/// </summary>
public class VeInvitePageTests
{
    private static async Task<int> SeedMessageAsync(WebAppFactory factory, MessageTrigger trigger, string name, string subject, string body)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = new MessageRule
        {
            TeamId = factory.Seeded.TeamId,
            Name = name,
            Trigger = trigger,
            Subject = subject,
            Body = body,
            CreatedUtc = DateTime.UtcNow.AddYears(-1)
        };
        db.MessageRules.Add(rule);
        await db.SaveChangesAsync();
        return rule.Id;
    }

    [Fact]
    public async Task ChoosingAMessage_FillsTheDraftFromIt()
    {
        using var factory = new WebAppFactory();
        var messageId = await SeedMessageAsync(factory, MessageTrigger.ManualVeSessionInvite,
            "Reminder of VE Session", "VE Session for {{SessionDate}}", "<p>I have {{RegisteredCount}} registered. Zoom: {{ZoomJoinUrl}}</p>");
        var client = factory.CreateClientAs(UserRole.SystemAdmin);

        var html = await client.GetStringAsync($"/SessionManager/VeInvite/{factory.Seeded.SessionId}?message={messageId}");

        Assert.Contains("VE Session for {{SessionDate}}", html);
        Assert.Contains("I have {{RegisteredCount}} registered.", html);
        Assert.Contains("Reminder of VE Session", html);
    }

    /// <summary>A directory message is written against tokens this screen supplies too, but it is not about a session; only this screen's own trigger is offered.</summary>
    [Fact]
    public async Task OnlyMessagesOnTheInviteTrigger_AreOffered()
    {
        using var factory = new WebAppFactory();
        await SeedMessageAsync(factory, MessageTrigger.ManualToVe, "Directory-only message", "S", "<p>B</p>");
        var client = factory.CreateClientAs(UserRole.SystemAdmin);

        var html = await client.GetStringAsync($"/SessionManager/VeInvite/{factory.Seeded.SessionId}");

        Assert.DoesNotContain("Directory-only message", html);
        Assert.Contains("{{RegisteredCount}}", html);
    }

    /// <summary>With no message chosen the screen still opens with its own starter text, as it always has.</summary>
    [Fact]
    public async Task NoMessageChosen_KeepsTheStarterDraft()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.SystemAdmin);

        var html = await client.GetStringAsync($"/SessionManager/VeInvite/{factory.Seeded.SessionId}");

        Assert.Contains("Can you work", html);
    }
}
