using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VeOps.Core.Authorization;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.VolunteerExaminers;

namespace VeOps.Web.Pages.SessionManager;

/// <summary>
/// Composing an invitation to a session's VEs (issue #142 phase 6).
///
/// <para>Reached from Session Detail, and gated on the same <c>SessionAccessScope.CanEdit</c> the
/// rest of that page's actions use — a Session Manager running the session is exactly who invites
/// people to it, so this is deliberately NOT restricted to admins the way the VE Directory is. It
/// shows names, tags and eligibility; it does not show contact details.</para>
/// </summary>
[Authorize(Roles = RoleGroups.SessionStaff)]
public class VeInviteModel(
    AppDbContext dbContext,
    UserManager<User> userManager,
    SessionAccessScope accessScope,
    VeSessionInvitationService invitationService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    /// <summary>The saved message to start from (2026-09-14); zero is the built-in starter draft. Bound as "message", the name the Email screens use.</summary>
    [BindProperty(SupportsGet = true, Name = "message")]
    public int SelectedMessageId { get; set; }

    /// <summary>The team's messages on the invite trigger — the only ones whose tokens all resolve here.</summary>
    public IReadOnlyList<ComposableMessages.Choice> Templates { get; private set; } = [];

    [BindProperty]
    public string Subject { get; set; } = "";

    [BindProperty]
    public string Body { get; set; } = "";

    [BindProperty]
    public int[] SelectedVeIds { get; set; } = [];

    public Session Session { get; private set; } = null!;
    public IReadOnlyList<VeInvitationCandidate> Candidates { get; private set; } = [];

    /// <summary>Tags actually in use on this list, for the filter. Built from the candidates rather than the team's whole vocabulary, so the dropdown never offers a tag that would match nothing.</summary>
    public IReadOnlyList<string> TagNames { get; private set; } = [];

    public static IReadOnlyList<string> Placeholders => VeSessionInvitationService.Placeholders;

    /// <summary>Shared with the Email VEs screen — see <see cref="VeTagFilter.UntaggedValue"/>, which carries the reasoning.</summary>
    public const string UntaggedFilterValue = VeTagFilter.UntaggedValue;

    public async Task<IActionResult> OnGetAsync()
    {
        var loaded = await LoadAsync();
        if (loaded is not null) return loaded;

        // A saved message replaces the starter text wholesale. Scoped to the session's team and the
        // invite trigger, so a message id from another team or another screen is simply not found.
        var message = SelectedMessageId == 0
            ? null
            : await dbContext.MessageRules
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.TeamId == Session.TeamId && r.Id == SelectedMessageId
                    && r.Trigger == MessageTrigger.ManualVeSessionInvite, HttpContext.RequestAborted);
        if (message is not null)
        {
            Subject = message.Subject;
            Body = message.Body;
            return Page();
        }

        Subject = $"Can you work {Session.Title}?";
        Body =
            """
            <p>Hi {{VeName}},</p>
            <p>We're looking for VEs for <strong>{{SessionTitle}}</strong> on {{SessionDate}}.</p>
            <p>Join here: {{ZoomJoinUrl}}</p>
            <p>Let us know if you can make it.</p>
            <p>{{TeamName}}</p>
            """;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var loaded = await LoadAsync();
        if (loaded is not null) return loaded;

        if (SelectedVeIds.Length == 0)
        {
            TempData["ErrorMessage"] = "Choose at least one VE to invite.";
            return RedirectToPage(new { id = Id });
        }

        // Only ids this page offered. A posted id from another team's roster must not become a
        // recipient just because someone edited the form.
        var allowed = Candidates.Select(c => c.VolunteerExaminer.Id).ToHashSet();
        if (SelectedVeIds.Any(id => !allowed.Contains(id)))
        {
            return Forbid();
        }

        var user = await userManager.GetRequiredUserAsync(dbContext, User);

        var result = await invitationService.SendAsync(Id, SelectedVeIds, Subject, Body, user.Id, HttpContext.RequestAborted);

        if (result.Error is not null)
        {
            TempData["ErrorMessage"] = result.Error;
            return RedirectToPage(new { id = Id });
        }

        var message = $"Sent {result.Sent} invitation(s).";
        if (result.Failed > 0) message += $" {result.Failed} failed to send.";
        if (result.NoEmailAddress > 0) message += $" {result.NoEmailAddress} had no email address on file.";
        if (result.Unsubscribed > 0) message += $" {result.Unsubscribed} have unsubscribed from email and were not invited.";
        if (result.TextOnlySkipped > 0) message += $" {result.TextOnlySkipped} are set to text only, which isn't available yet.";

        TempData[result.Sent > 0 ? "StatusMessage" : "ErrorMessage"] = message;
        return RedirectToPage("/SessionManager/Detail", new { id = Id });
    }

    private async Task<IActionResult?> LoadAsync()
    {
        var user = await userManager.GetRequiredUserAsync(dbContext, User);

        var session = await dbContext.Sessions
            .Include(s => s.Team)
            .FirstOrDefaultAsync(s => s.Id == Id, HttpContext.RequestAborted);
        if (session is null)
        {
            return NotFound();
        }

        if (!accessScope.CanEdit(user, session))
        {
            return Forbid();
        }

        Session = session;
        Templates = await ComposableMessages.LoadAsync(dbContext, session.TeamId, MessageTrigger.ManualVeSessionInvite, HttpContext.RequestAborted);
        Candidates = await invitationService.GetCandidatesAsync(Id, HttpContext.RequestAborted);
        TagNames = [.. Candidates.SelectMany(c => c.Tags).Distinct().OrderBy(n => n)];
        return null;
    }
}
