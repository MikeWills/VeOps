using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using VeOps.Core.Authorization;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.Payments;

namespace VeOps.Web.Pages.Admin;

/// <summary>
/// Renders the public youth-rate confirmation page exactly as a candidate sees it, for a team that
/// is editing its intro paragraph in Team Settings. That paragraph is rich text a team writes
/// itself, and until this page existed the only way to see the result was to hold a real candidate's
/// confirmation token — i.e. to send oneself a registration email — so nobody checked.
///
/// <para>The markup is the public page's own partial, not a copy: a preview that can drift from the
/// page it previews is worse than no preview. The only difference is that nothing submits — see
/// _YouthConfirmForm's own remarks.</para>
/// </summary>
// No candidate data is read or written here, but the intro is a team's own configuration, so the
// page is scoped the same way Team Settings is rather than being open to every signed-in role.
[Authorize(Roles = RoleGroups.Admins)]
public class YouthConfirmPreviewModel(
    AppDbContext dbContext,
    UserManager<User> userManager,
    AdminAccessScope adminAccessScope) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int? TeamId { get; set; }

    /// <summary>The team's own intro paragraph, or the shipped default — resolved exactly as YouthPaymentConfirmationService.CheckEligibilityAsync resolves it for a real candidate.</summary>
    public string IntroHtml { get; private set; } = YouthConfirmDefaults.IntroHtml;

    /// <summary>Empty, so the preview opens in the state a candidate first sees: nothing ticked, no answer chosen, the COPPA panel closed.</summary>
    public Public.YouthConfirmModel.InputModel Input { get; } = new();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var user = await userManager.GetUserWithManagerAsync(dbContext, User);
        if (user is null)
        {
            return Forbid();
        }

        var availableTeams = await adminAccessScope.GetAvailableTeamsAsync(dbContext, user);
        // ...ForWrite, the refusing resolver, even though this is a read-only GET: the forgiving one
        // substitutes the acting user's first team, and a preview that silently shows a *different*
        // team's wording than the one being edited would be actively misleading.
        var teamId = adminAccessScope.TryResolveManageableTeamIdForWrite(user, TeamId, availableTeams.Select(t => t.Id).ToList());
        if (teamId is null)
        {
            return Forbid();
        }

        TeamId = teamId;
        var settings = await dbContext.EmailSettings
            .Where(e => e.TeamId == teamId.Value)
            .Select(e => e.YouthConfirmIntroHtml)
            .FirstOrDefaultAsync(cancellationToken);
        IntroHtml = string.IsNullOrWhiteSpace(settings) ? YouthConfirmDefaults.IntroHtml : settings;
        return Page();
    }
}
