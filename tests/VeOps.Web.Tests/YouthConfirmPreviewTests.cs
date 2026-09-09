using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.Payments;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// The admin preview of the public youth-rate confirmation page (2026-09-08). A team writes that
/// page's intro paragraph itself, in Team Settings, and before this the only way to see the result
/// was to hold a real candidate's confirmation token. The preview therefore has to render the
/// candidate's page — the same partial, not a copy — while submitting nothing.
/// </summary>
public class YouthConfirmPreviewTests
{
    private static string Url(int teamId) => $"/Admin/YouthConfirmPreview?teamId={teamId}";

    private static async Task SetIntroAsync(WebAppFactory factory, string? introHtml)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var settings = await db.EmailSettings.FirstOrDefaultAsync(e => e.TeamId == factory.Seeded.TeamId);
        if (settings is null)
        {
            db.EmailSettings.Add(new EmailSettings
            {
                TeamId = factory.Seeded.TeamId,
                FromAddress = "ve@example.org",
                ReplyToAddress = "ve@example.org",
                PrivacyPolicyUrl = "https://example.org/privacy",
                AdminNotificationEmail = "admin@example.org",
                YouthConfirmIntroHtml = introHtml
            });
        }
        else
        {
            settings.YouthConfirmIntroHtml = introHtml;
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Preview_ShowsTheTeamsOwnIntroParagraph()
    {
        using var factory = new WebAppFactory();
        await SetIntroAsync(factory, "<p>Half price for anyone still in school.</p>");
        var client = factory.CreateClientAs(UserRole.TeamAdmin);

        var html = await client.GetStringAsync(Url(factory.Seeded.TeamId));

        Assert.Contains("<p>Half price for anyone still in school.</p>", html);
        Assert.DoesNotContain("ARRL runs a youth program", html);
    }

    [Fact]
    public async Task Preview_TeamHasNoIntroOfItsOwn_ShowsTheShippedDefault()
    {
        using var factory = new WebAppFactory();
        await SetIntroAsync(factory, "   ");
        var client = factory.CreateClientAs(UserRole.TeamAdmin);

        var html = await client.GetStringAsync(Url(factory.Seeded.TeamId));

        Assert.Contains("ARRL runs a youth program", html);
    }

    /// <summary>The whole point: what is previewed must be the candidate's page, down to the COPPA panel.</summary>
    [Fact]
    public async Task Preview_RendersTheSameFieldsAsTheCandidatesPage()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.TeamAdmin);

        var html = await client.GetStringAsync(Url(factory.Seeded.TeamId));

        Assert.Contains("name=\"Input.ConfirmYouth\"", html);
        Assert.Contains("name=\"Input.DeclaredUnder13\"", html);
        Assert.Contains("name=\"Input.CoppaFormSent\"", html);
        Assert.Contains("id=\"coppaPanel\"", html);
        Assert.Contains("Continue to $5 payment", html);
    }

    /// <summary>
    /// Nothing on the preview may submit. It carries no token and there is no POST handler behind it,
    /// so a submit button would only ever produce an error — and a preview that can act is a preview
    /// nobody trusts.
    /// </summary>
    [Fact]
    public async Task Preview_SubmitsNothing()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.TeamAdmin);

        var html = await client.GetStringAsync(Url(factory.Seeded.TeamId));

        Assert.DoesNotContain("method=\"post\"", html);
        Assert.DoesNotContain("type=\"submit\"", html);
        Assert.Contains("type=\"button\" class=\"btn-primary\"", html);
    }

    [Fact]
    public async Task Preview_IsAdminsOnly()
    {
        using var factory = new WebAppFactory();

        var sessionManager = await factory.CreateClientAs(UserRole.SessionManager).GetAsync(Url(factory.Seeded.TeamId));
        var anonymous = await factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false })
            .GetAsync(Url(factory.Seeded.TeamId));

        Assert.NotEqual(HttpStatusCode.OK, sessionManager.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
    }

    [Fact]
    public async Task TeamSettings_LinksToThePreview()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.TeamAdmin);

        var html = await client.GetStringAsync($"/Admin/TeamSettings?teamId={factory.Seeded.TeamId}");

        Assert.Contains($"/Admin/YouthConfirmPreview?teamId={factory.Seeded.TeamId}", html);
    }

    /// <summary>Guards the fallback the preview shares with the candidate's page — one definition, so the two can never say different things.</summary>
    [Fact]
    public async Task Preview_DefaultIntro_IsTheSameOneCandidatesSee()
    {
        using var factory = new WebAppFactory();
        await SetIntroAsync(factory, null);
        var client = factory.CreateClientAs(UserRole.TeamAdmin);

        var html = await client.GetStringAsync(Url(factory.Seeded.TeamId));

        Assert.Contains(YouthConfirmDefaults.IntroHtml, html);
    }
}
