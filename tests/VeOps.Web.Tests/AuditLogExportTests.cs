using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// CSV export of the audit log, and the filter bar's date fields lining up with the controls beside
/// them (both reported 2026-11-18).
///
/// <para>The export is <b>POST, not GET</b>, for the reason <c>SecurityHygieneTests</c> records for the
/// VE Directory: it writes an <c>AuditLogExported</c> row attesting who took a copy of the log out of
/// the building, and a GET would let a cross-site link forge that row. And it ships <b>everything
/// matching the filters</b>, not the 25 rows on screen — the failure <c>VeDirectoryExportTests</c>
/// exists for, where a short CSV is not obviously wrong to look at.</para>
/// </summary>
public class AuditLogExportTests : IClassFixture<WebAppFactory>
{
    private const string Url = "/Admin/AuditLog";

    private readonly WebAppFactory _factory;

    public AuditLogExportTests(WebAppFactory factory) => _factory = factory;

    /// <summary>60 identical rows (more than the 25-row default page) and one differently-shaped one.</summary>
    private async Task SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AuditLogs.RemoveRange(await db.AuditLogs.ToListAsync());
        await db.SaveChangesAsync();

        for (var i = 0; i < 60; i++)
        {
            db.AuditLogs.Add(new AuditLog
            {
                Action = "VecSubmissionMarked",
                EntityType = "Session",
                EntityId = i + 1,
                Details = $"Bulk row {i}",
                TimestampUtc = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc)
            });
        }

        db.AuditLogs.Add(new AuditLog
        {
            Action = "CandidateWithdrawnFromFeed",
            EntityType = "Candidate",
            EntityId = 9001,
            // A leading '=' is how a spreadsheet formula starts; CsvExport must neutralise it.
            Details = "=HYPERLINK(\"http://example.invalid\")",
            TimestampUtc = new DateTime(2026, 7, 1, 9, 0, 0, DateTimeKind.Utc)
        });

        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> PostExportAsync(HttpClient client, string query = "")
    {
        var page = await client.GetStringAsync(Url);
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        Assert.NotEmpty(token);

        var fields = new List<KeyValuePair<string, string>> { new("__RequestVerificationToken", token) };
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            fields.Add(new(kv[0], kv[1]));
        }

        return await client.PostAsync($"{Url}?handler=Export", new FormUrlEncodedContent(fields));
    }

    /// <summary>Data rows only: the header line is dropped.</summary>
    private static string[] DataLines(string csv) =>
        csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();

    [Fact]
    public async Task Export_ShipsEveryMatchingRow_NotJustTheVisiblePage()
    {
        await SeedAsync();
        using var client = _factory.CreateClientAs(UserRole.SystemAdmin);

        var response = await PostExportAsync(client);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(61, DataLines(await response.Content.ReadAsStringAsync()).Length);
    }

    [Fact]
    public async Task Export_HonorsTheActiveFilters()
    {
        await SeedAsync();
        using var client = _factory.CreateClientAs(UserRole.SystemAdmin);

        var response = await PostExportAsync(client, "action=CandidateWithdrawnFromFeed");

        Assert.Single(DataLines(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Export_NeutralisesSpreadsheetFormulas()
    {
        await SeedAsync();
        using var client = _factory.CreateClientAs(UserRole.SystemAdmin);

        var csv = await (await PostExportAsync(client, "action=CandidateWithdrawnFromFeed"))
            .Content.ReadAsStringAsync();

        Assert.Contains("\"'=HYPERLINK(", csv);
    }

    [Fact]
    public async Task Export_ByGet_ProducesNoCsvAndNoAuditRow()
    {
        await SeedAsync();
        using var client = _factory.CreateClientAs(UserRole.SystemAdmin);
        var before = await ExportAuditRowCountAsync();

        var response = await client.GetAsync($"{Url}?handler=Export");

        Assert.NotEqual("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(before, await ExportAuditRowCountAsync());
    }

    [Fact]
    public async Task Export_WritesOneAuditRowRecordingHowManyRowsLeft()
    {
        await SeedAsync();
        using var client = _factory.CreateClientAs(UserRole.SystemAdmin);
        var before = await ExportAuditRowCountAsync();

        await PostExportAsync(client);

        Assert.Equal(before + 1, await ExportAuditRowCountAsync());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AuditLogs.SingleAsync(a => a.Action == "AuditLogExported");
        Assert.Contains("61", row.Details);
    }

    /// <summary>
    /// The page offers the button, and its form carries the antiforgery token — an explicit POST form
    /// that lost its token would 400 in middleware with nothing in the log.
    /// </summary>
    [Fact]
    public async Task Page_OffersAnExportButton()
    {
        await SeedAsync();
        using var client = _factory.CreateClientAs(UserRole.SystemAdmin);

        var html = await client.GetStringAsync(Url);

        Assert.Matches(@"<form[^>]*method=""post""[^>]*handler=Export|<form[^>]*handler=Export[^>]*method=""post""", html);
        Assert.Contains("Export CSV", html);
    }

    /// <summary>
    /// The From/To fields carry a caption above the input, so they are taller than the label-less
    /// pills and search box beside them. Top-aligning that row left the date inputs hanging below
    /// everything else; bottom-aligning puts every control on one baseline (Transactions report does
    /// the same).
    /// </summary>
    [Fact]
    public async Task FilterBar_BottomAlignsTheDateFieldsWithTheOtherControls()
    {
        await SeedAsync();
        using var client = _factory.CreateClientAs(UserRole.SystemAdmin);

        var html = await client.GetStringAsync(Url);

        var bar = Regex.Match(html, @"<form[^>]*class=""filters""[^>]*>").Value;
        Assert.Contains("align-items:flex-end", bar);
        Assert.DoesNotContain("align-items:flex-start", bar);
    }

    private async Task<int> ExportAuditRowCountAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.AuditLogs.CountAsync(a => a.Action == "AuditLogExported");
    }
}
