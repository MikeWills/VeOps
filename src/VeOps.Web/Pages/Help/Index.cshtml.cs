using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VeOps.Web.Pages.Help;

// Public like the GitHub wiki it mirrors: the candidate guide is written for people who cannot
// sign in, and the rest is world-readable on github.com already.
[AllowAnonymous]
public class IndexModel : PageModel
{
    public HelpPages.Rendered Rendered { get; private set; } = null!;

    // The route value is "name", not "page": "page" is the key Razor Pages itself uses to pick the
    // page, and a template that reuses it never matches.
    public IActionResult OnGet(string? name)
    {
        var rendered = HelpPages.Render(name, Request.PathBase.Value ?? "");
        if (rendered is null) return NotFound();
        Rendered = rendered;
        return Page();
    }
}
