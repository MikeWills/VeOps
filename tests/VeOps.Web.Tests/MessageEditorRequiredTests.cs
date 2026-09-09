using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// The message body must never be `required` in the markup (2026-09-08, reported live).
///
/// <para>That textarea is <c>display:none</c> behind the Quill editor, and a browser refuses to
/// submit a form holding an invalid control it cannot focus — silently. The only trace was a console
/// line nobody had open: <c>An invalid form control with name='Body' is not focusable</c>. The rule
/// would not save, and the page said nothing at all.</para>
///
/// <para>It was worse than a missing message: the editor only copied its content into the textarea on
/// the form's <c>submit</c> event, and constraint validation runs <i>before</i> that — so the
/// textarea was empty at exactly the moment it was judged, however much the person had typed.</para>
/// </summary>
public class MessageEditorRequiredTests
{
    private static async Task<int> SeedRuleAsync(WebAppFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rule = new MessageRule
        {
            TeamId = factory.Seeded.TeamId,
            Name = "A rule",
            Trigger = MessageTrigger.BeforeSessionStart,
            ParameterHours = 24,
            Subject = "s",
            Body = "<p>b</p>",
            Recipient = MessageRecipient.Candidate,
            CreatedUtc = DateTime.UtcNow
        };
        db.MessageRules.Add(rule);
        await db.SaveChangesAsync();
        return rule.Id;
    }

    private static async Task<string> NewRulePageAsync(WebAppFactory factory) =>
        await factory.CreateClientAs(UserRole.TeamAdmin)
            .GetStringAsync($"/Admin/MessageRuleNew?teamId={factory.Seeded.TeamId}");

    private static async Task<string> EditRulePageAsync(WebAppFactory factory, int ruleId) =>
        await factory.CreateClientAs(UserRole.TeamAdmin).GetStringAsync($"/Admin/MessageRuleEdit/{ruleId}");

    /// <summary>The exact string the browser refused on. Nothing may put it back.</summary>
    [Fact]
    public async Task NewRuleForm_BodyTextarea_IsNotBrowserRequired()
    {
        using var factory = new WebAppFactory();

        var html = await NewRulePageAsync(factory);

        Assert.Contains("data-editor-required", html);
        Assert.DoesNotContain("rows=\"12\" required", html);
    }

    [Fact]
    public async Task EditRuleForm_BodyTextarea_IsNotBrowserRequired()
    {
        using var factory = new WebAppFactory();
        var ruleId = await SeedRuleAsync(factory);

        var html = await EditRulePageAsync(factory, ruleId);

        Assert.Contains("data-editor-required", html);
        Assert.DoesNotContain("rows=\"12\" required", html);
    }

    /// <summary>
    /// Dropping `required` only helps if the check moved somewhere it can be reported. The script
    /// unhides this element; if the markup ever loses it, an empty body would be refused with no
    /// visible reason again — the original defect, wearing different clothes.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothForms_CarryTheMessageTheScriptShowsForAnEmptyBody(bool editForm)
    {
        using var factory = new WebAppFactory();

        var html = editForm
            ? await EditRulePageAsync(factory, await SeedRuleAsync(factory))
            : await NewRulePageAsync(factory);

        Assert.Contains("data-editor-empty-message", html);
        Assert.Contains("Give the message something to say.", html);
    }

    /// <summary>
    /// The subject sits in the open and can show its own bubble, so it keeps the browser's check —
    /// this pins that the fix was surgical rather than "delete every required attribute".
    /// </summary>
    [Fact]
    public async Task TheSubjectField_KeepsItsBrowserRequiredCheck()
    {
        using var factory = new WebAppFactory();

        var html = await NewRulePageAsync(factory);

        Assert.Contains("data-token-target=\"subject\" required", html);
    }
}
