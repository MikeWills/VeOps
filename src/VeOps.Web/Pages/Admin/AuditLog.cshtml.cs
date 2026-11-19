using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;
using VeOps.Core.Authorization;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.Uls;

namespace VeOps.Web.Pages.Admin;

/// <summary>
/// Phase 9c: read-only audit log viewer, scoped via AdminAccessScope.ScopeAuditLog (SystemAdmin:
/// global; TeamAdmin: their own team's users' actions only — see that method's doc for the known
/// "misses unattributed background-job entries" limitation, tracked in #86).
///
/// <para><b>Filtering and paging, since #86.</b> This page was
/// <c>OrderByDescending(TimestampUtc).Take(200)</c> with no filter, no paging and no date range,
/// which is fine for ordinary activity and useless immediately after a bulk write: the one-off
/// VEC-submitted backfill inserted 176 rows in about a second, so ~88% of the visible log became one
/// operation and everything older fell off the page. Nothing was ever deleted — it was purely a
/// display cap — but the log could not be used to review anything else until new activity displaced
/// them.</para>
///
/// <para><b>Sorting is server-side and fixed at newest-first.</b> The table used to carry the
/// client-side <c>data-sortable</c> sorter, which is correct for an unpaged list and actively
/// misleading on a paged one — it would reorder the 25 rows on screen while presenting itself as
/// having sorted the log. CLAUDE.md's rule: a server-paged list must sort server-side. An audit log
/// has one meaningful order anyway; the filters are what make an older entry reachable.</para>
/// </summary>
[Authorize(Roles = RoleGroups.Admins)]
[RemembersFilters]
public class AuditLogModel(AppDbContext dbContext, UserManager<User> userManager, AdminAccessScope adminAccessScope, TimeProvider timeProvider) : PageModel
{
    /// <summary>
    /// Most rows one export will carry. Over this the export refuses rather than quietly truncating —
    /// a file that silently stops at row 50,000 looks complete and is not, which is the one thing an
    /// audit export must never do. Narrow the filters (the date range is the usual lever) and retry.
    /// </summary>
    internal const int ExportRowLimit = 50_000;

    internal static readonly int[] AllowedPageSizes = [25, 50, 100, 200];
    private const int DefaultPageSize = 25;

    public IReadOnlyList<AuditLogRow> Entries { get; private set; } = [];

    /// <summary>Distinct actions and entity types present in what this user can see — the filter
    /// dropdowns are built from the data rather than from a hardcoded list, so a new audit action
    /// appears without anyone remembering to add it here.</summary>
    public IReadOnlyList<string> AvailableActions { get; private set; } = [];
    public IReadOnlyList<string> AvailableEntityTypes { get; private set; } = [];
    public IReadOnlyList<(int Id, string Name)> AvailableUsers { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Action { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EntityType { get; set; }

    /// <summary>Null is "anyone". <see cref="BackgroundJobUserId"/> selects the unattributed rows.</summary>
    [BindProperty(SupportsGet = true)]
    public int? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? DateFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? DateTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "pageSize")]
    public int PageSize { get; set; } = DefaultPageSize;

    /// <summary>
    /// <b><c>pageNumber</c>, not <c>page</c>, and this is load-bearing.</b> Razor Pages puts the
    /// page's own path into route values under the key <c>page</c> (here, "/Admin/AuditLog"), and
    /// the route value provider runs <i>before</i> the query string provider. So <c>?page=2</c>
    /// never reaches this property: binding sees the route value, fails to parse it as an int, and
    /// leaves the default. Every page renders as page 1.
    ///
    /// <para>It fails in complete silence — no exception, no warning, a pager that renders correctly
    /// and simply does not move. Found here by a paging test walking every page and finding it had
    /// been handed the same 25 rows eight times.</para>
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "pageNumber")]
    public int PageNumber { get; set; } = 1;

    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }

    /// <summary>
    /// Sentinel for "entries with no user", i.e. background jobs. A real <c>User.Id</c> is always
    /// positive, so a negative value can never collide with one — the same reasoning as the
    /// printable-sentinel rule in CLAUDE.md, which exists because an invisible sentinel (a NUL byte)
    /// once silently broke a filter in exactly this position.
    /// </summary>
    public const int BackgroundJobUserId = -1;

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserWithManagerAsync(dbContext, User);
        if (user is null)
        {
            return Forbid();
        }

        PageSize = AllowedPageSizes.Contains(PageSize) ? PageSize : DefaultPageSize;
        PageNumber = PageNumber < 1 ? 1 : PageNumber;

        var scoped = adminAccessScope.ScopeAuditLog(dbContext.AuditLogs, user);

        // Built from the scoped set before the other filters are applied, so the dropdowns keep
        // offering every value this user could pick rather than collapsing to whatever the current
        // filter left behind.
        AvailableActions = await scoped.Select(a => a.Action).Distinct().OrderBy(a => a)
            .ToListAsync(HttpContext.RequestAborted);
        AvailableEntityTypes = await scoped.Select(a => a.EntityType).Distinct().OrderBy(e => e)
            .ToListAsync(HttpContext.RequestAborted);
        // Anonymous type, then tuples in memory. EF Core cannot translate Distinct() over a
        // ValueTuple *constructor* projection — it throws at query time, which renders as a 500 on a
        // page that otherwise looks fine.
        AvailableUsers = (await scoped.Where(a => a.User != null)
                .Select(a => new { a.User!.Id, a.User.Name })
                .Distinct()
                .ToListAsync(HttpContext.RequestAborted))
            .OrderBy(u => u.Name)
            .Select(u => new ValueTuple<int, string>(u.Id, u.Name))
            .ToList();

        var query = ApplyFilters(scoped);

        TotalCount = await query.CountAsync(HttpContext.RequestAborted);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        PageNumber = Math.Min(PageNumber, TotalPages);

        var entries = await query
            .OrderByDescending(a => a.TimestampUtc)
            .ThenByDescending(a => a.Id)
            .Skip((PageNumber - 1) * PageSize)
            .Take(PageSize)
            .Select(a => new AuditLogRow(
                a.TimestampUtc, a.User != null ? a.User.Name : null, a.Action, a.EntityType, a.EntityId, a.Details))
            .ToListAsync(HttpContext.RequestAborted);

        Entries = entries;
        return Page();
    }

    /// <summary>
    /// CSV of <b>everything matching the current filters</b>, not the page on screen — see
    /// <c>VeDirectoryExportTests</c> for why a short CSV is the failure to guard against.
    ///
    /// <para><b>POST, not GET</b>, and it writes an <c>AuditLogExported</c> row: the log holds names,
    /// call signs and payment references, and a file someone can mail onward is a different exposure
    /// from a screen. A GET would let a cross-site link forge that attestation (#265/L-11). The new
    /// row is added after the query, so it is not in its own file — the next export will show it.</para>
    ///
    /// <para>Scoped through the same <c>ScopeAuditLog</c> and <c>ApplyFilters</c> as the page, so a
    /// TeamAdmin cannot export entries they cannot see. Timestamps are UTC ISO plus an Eastern column,
    /// because the screen shows Eastern and a reader matching the file to it should not have to convert.</para>
    /// </summary>
    public async Task<IActionResult> OnPostExportAsync()
    {
        var user = await userManager.GetUserWithManagerAsync(dbContext, User);
        if (user is null)
        {
            return Forbid();
        }

        var query = ApplyFilters(adminAccessScope.ScopeAuditLog(dbContext.AuditLogs, user));

        var count = await query.CountAsync(HttpContext.RequestAborted);
        if (count > ExportRowLimit)
        {
            return BadRequest($"{count:N0} entries match; one export carries at most {ExportRowLimit:N0}. Narrow the filters (the date range is the easiest way) and try again.");
        }

        var rows = await query
            .OrderByDescending(a => a.TimestampUtc)
            .ThenByDescending(a => a.Id)
            .Select(a => new AuditLogRow(
                a.TimestampUtc, a.User != null ? a.User.Name : null, a.Action, a.EntityType, a.EntityId, a.Details))
            .ToListAsync(HttpContext.RequestAborted);

        var csv = new StringBuilder();
        csv.AppendLine("WhenUtc,WhenEastern,User,Action,EntityType,EntityId,Details");
        foreach (var row in rows)
        {
            csv.AppendLine(CsvExport.Row(
                row.TimestampUtc.ToString("o", CultureInfo.InvariantCulture),
                EasternTimeFormatter.Format(row.TimestampUtc, "yyyy-MM-dd HH:mm:ss"),
                row.UserName ?? "(background job)",
                row.Action,
                row.EntityType,
                row.EntityId.ToString(CultureInfo.InvariantCulture),
                row.Details));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        dbContext.AddAuditLog(user.Id, "AuditLogExported", nameof(AuditLog), 0,
            $"Exported {rows.Count} audit log entr{(rows.Count == 1 ? "y" : "ies")}.", now,
            SourceIp.For(HttpContext));
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);

        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        return File(CsvExport.ToBytes(csv), "text/csv", $"audit-log-{stamp}.csv");
    }

    /// <summary>
    /// <c>ThenByDescending(Id)</c> above is not decoration. Bulk writes land many rows on the same
    /// timestamp — 176 of them in about a second, which is the case this page exists to survive — and
    /// paging over a non-deterministic order silently drops and repeats rows across page boundaries.
    /// </summary>
    private IQueryable<AuditLog> ApplyFilters(IQueryable<AuditLog> query)
    {
        if (!string.IsNullOrWhiteSpace(Action))
        {
            query = query.Where(a => a.Action == Action);
        }

        if (!string.IsNullOrWhiteSpace(EntityType))
        {
            query = query.Where(a => a.EntityType == EntityType);
        }

        if (UserId == BackgroundJobUserId)
        {
            query = query.Where(a => a.UserId == null);
        }
        else if (UserId is { } userId)
        {
            query = query.Where(a => a.UserId == userId);
        }

        // From/To are Eastern calendar days — the date the "When" column displays — so each bound is
        // Eastern midnight converted to UTC, not UTC midnight. As UTC days, an 8 PM ET entry (already
        // tomorrow in UTC) showed one date and filtered under the next. See UlsSchedule.ToEasternDate.
        if (DateFrom is { } from)
        {
            var fromUtc = EasternMidnightUtc(from);
            query = query.Where(a => a.TimestampUtc >= fromUtc);
        }

        if (DateTo is { } to)
        {
            // Exclusive upper bound on the next day, so "to = today" includes everything logged
            // today rather than only the instant of midnight.
            var toUtc = EasternMidnightUtc(to.AddDays(1));
            query = query.Where(a => a.TimestampUtc < toUtc);
        }

        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(a => a.Details != null && EF.Functions.Like(a.Details, $"%{term}%"));
        }

        return query;
    }

    /// <summary>The UTC instant at which the given Eastern calendar day begins. Midnight is never
    /// inside a DST gap (those are at 2 AM), so the conversion cannot be invalid.</summary>
    private static DateTime EasternMidnightUtc(DateOnly easternDay) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(easternDay.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified),
            UlsSchedule.EasternTimeZone);

    public bool HasFilters =>
        !string.IsNullOrWhiteSpace(Action) || !string.IsNullOrWhiteSpace(EntityType) || UserId is not null
        || DateFrom is not null || DateTo is not null || !string.IsNullOrWhiteSpace(Search);

    /// <summary>
    /// Keeps every active filter on a page link. Without this, paging a filtered list silently
    /// returns to page 1 of the unfiltered log — the same trap CLAUDE.md records for row-action
    /// forms on filtered list pages.
    /// </summary>
    public string BuildPageUrl(int page, int? pageSizeOverride = null)
    {
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(Action))
        {
            qs.Add($"action={Uri.EscapeDataString(Action)}");
        }
        if (!string.IsNullOrWhiteSpace(EntityType))
        {
            qs.Add($"entityType={Uri.EscapeDataString(EntityType)}");
        }
        if (UserId is not null)
        {
            qs.Add($"userId={UserId}");
        }
        if (DateFrom is not null)
        {
            qs.Add($"dateFrom={DateFrom:yyyy-MM-dd}");
        }
        if (DateTo is not null)
        {
            qs.Add($"dateTo={DateTo:yyyy-MM-dd}");
        }
        if (!string.IsNullOrWhiteSpace(Search))
        {
            qs.Add($"search={Uri.EscapeDataString(Search)}");
        }
        qs.Add($"pageSize={pageSizeOverride ?? PageSize}");
        qs.Add($"pageNumber={page}");
        return "/Admin/AuditLog?" + string.Join("&", qs);
    }

    public record AuditLogRow(DateTime TimestampUtc, string? UserName, string Action, string EntityType, int EntityId, string? Details);
}
