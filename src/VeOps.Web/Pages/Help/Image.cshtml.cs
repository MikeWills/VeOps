using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VeOps.Web.Pages.Help;

/// <summary>Screenshots for the manual. HelpPages.ImagePath admits only a plain lowercase PNG
/// name, so nothing outside <c>Help/images</c> is reachable through here.</summary>
[AllowAnonymous]
public class ImageModel : PageModel
{
    public IActionResult OnGet(string file)
    {
        var path = HelpPages.ImagePath(file);
        return path is null ? NotFound() : PhysicalFile(path, "image/png");
    }
}
