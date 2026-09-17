using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace VeOps.Web;

/// <summary>
/// The user manual, served inside the app. Reads the Markdown pages that <c>VeOps.Web.csproj</c>
/// copies from <c>docs/wiki</c> into the build output's <c>Help/</c> folder and renders them at
/// request time, rewriting links the same way <c>scripts/build_wiki.py</c> does for the GitHub
/// wiki: a sibling <c>Page.md</c> becomes <c>/Help/Page</c>, <c>../x.md</c> (the rest of
/// <c>/docs</c>) becomes an absolute GitHub URL, and <c>images/x.png</c> is served by the
/// image route. One master copy, two readers.
/// </summary>
/// <remarks>
/// <para><see cref="Order"/>, <see cref="Titles"/> and <see cref="Headings"/> mirror the same
/// three tables in <c>scripts/build_wiki.py</c>; change both. A page missing from the order still
/// appears — alphabetically after the listed ones — rather than being hidden.</para>
/// <para>Read from <see cref="AppContext.BaseDirectory"/>, not the content root: on the server the
/// content root is the working directory (see CLAUDE.md), and in the test host it is the source
/// project, where the copied folder does not exist. The build output is where the files are in
/// both.</para>
/// </remarks>
public static class HelpPages
{
    public const string Home = "README";
    private const string Repo = "MikeWills/VeOps";
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Help");
    private static readonly Regex PageName = new("^[A-Za-z0-9-]+$", RegexOptions.Compiled);
    private static readonly Regex ImageName = new("^[a-z0-9-]+\\.png$", RegexOptions.Compiled);

    private static readonly string[] Order =
    [
        Home, "Your-first-session", "Guide-Candidate", "Guide-Volunteer-Examiner", "Guide-Team-Lead",
        "Guide-Session-Manager", "Guide-Team-Admin", "Guide-System-Admin", "Something-is-wrong",
        "Tasks", "Roles-and-Permissions", "Glossary", "About-this-wiki",
    ];

    private static readonly Dictionary<string, string> Titles = new()
    {
        [Home] = "Start here",
        ["Your-first-session"] = "Your first session",
        ["Guide-Candidate"] = "Candidate",
        ["Guide-Volunteer-Examiner"] = "Volunteer Examiner",
        ["Guide-Team-Lead"] = "Team Lead",
        ["Guide-Session-Manager"] = "Session Manager",
        ["Guide-Team-Admin"] = "Team Admin",
        ["Guide-System-Admin"] = "System Admin",
        ["Something-is-wrong"] = "Something is wrong",
        ["Tasks"] = "Tasks",
        ["Roles-and-Permissions"] = "Roles and permissions",
        ["Glossary"] = "Glossary",
        ["About-this-wiki"] = "About this wiki",
    };

    /// <summary>Where the sidebar breaks into groups, keyed by the page the heading precedes.</summary>
    private static readonly Dictionary<string, string> Headings = new()
    {
        ["Guide-Candidate"] = "Role guides",
        ["Something-is-wrong"] = "Reference",
    };

    // No raw HTML: the pages are ours, but the CSP is script-src 'self' and there is no reason a
    // manual should ever carry markup that Markdown cannot express.
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAutoLinks()
        .DisableHtml()
        .Build();

    public sealed record NavEntry(string Page, string Title, string? HeadingBefore, bool IsCurrent);

    public sealed record Rendered(string Page, string Title, string Html, IReadOnlyList<NavEntry> Nav);

    /// <summary>Every page name in sidebar order — the listed ones first, any newcomer after.</summary>
    public static IReadOnlyList<string> AllPages()
    {
        if (!Directory.Exists(Root)) return [];
        var present = Directory.GetFiles(Root, "*.md").Select(f => Path.GetFileNameWithoutExtension(f)!).ToHashSet();
        var known = Order.Where(present.Contains);
        var rest = present.Where(p => !Order.Contains(p)).OrderBy(p => p, StringComparer.Ordinal);
        return [.. known, .. rest];
    }

    public static string TitleOf(string page) => Titles.TryGetValue(page, out var t) ? t : page.Replace('-', ' ');

    /// <summary>Null when there is no such page — including any name that is not a bare page name.</summary>
    public static Rendered? Render(string? page, string basePath)
    {
        page = string.IsNullOrEmpty(page) ? Home : page;
        if (!PageName.IsMatch(page)) return null;
        var file = Path.Combine(Root, page + ".md");
        if (!File.Exists(file)) return null;

        var document = Markdown.Parse(File.ReadAllText(file), Pipeline);
        foreach (var link in document.Descendants<LinkInline>())
        {
            link.Url = Rewrite(link.Url, link.IsImage, basePath);
        }

        var nav = AllPages()
            .Select(p => new NavEntry(p, TitleOf(p), Headings.GetValueOrDefault(p), p == page))
            .ToList();
        return new Rendered(page, TitleOf(page), document.ToHtml(Pipeline), nav);
    }

    /// <summary>The on-disk file for a screenshot, or null for any name that is not a plain lowercase PNG name.</summary>
    public static string? ImagePath(string? file)
    {
        if (file is null || !ImageName.IsMatch(file)) return null;
        var path = Path.Combine(Root, "images", file);
        return File.Exists(path) ? path : null;
    }

    private static string? Rewrite(string? target, bool isImage, string basePath)
    {
        if (string.IsNullOrEmpty(target)) return target;
        if (target.StartsWith("http://", StringComparison.Ordinal) || target.StartsWith("https://", StringComparison.Ordinal)
            || target.StartsWith('#') || target.StartsWith("mailto:", StringComparison.Ordinal))
        {
            return target;
        }

        if (isImage)
        {
            return $"{basePath}/Help/{target.TrimStart('.', '/')}";
        }

        // Into the rest of /docs — not shipped with the app, so name it where it lives.
        if (target.StartsWith("../", StringComparison.Ordinal))
        {
            return $"https://github.com/{Repo}/blob/main/docs/{target[3..]}";
        }

        var hash = target.IndexOf('#');
        var (path, anchor) = hash >= 0 ? (target[..hash], target[hash..]) : (target, "");
        if (!path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) return target;
        var name = Path.GetFileNameWithoutExtension(path);
        var route = string.Equals(name, Home, StringComparison.OrdinalIgnoreCase) ? "" : "/" + name;
        return $"{basePath}/Help{route}{anchor}";
    }
}
