using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.VecSubmissions;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// ARRL's confirmation page opens in a tab, rendered — Mike, 2026-09-10, after a phone showed him
/// raw markup: it had been served as <c>text/plain</c> under a <c>.html</c> filename, a response
/// contradicting itself, and which half the browser believed decided what the reader saw.
///
/// <para><b>The sandbox is what makes this safe, and it is the thing to protect.</b> This is
/// third-party HTML carrying the submitter's name, call sign, email and phone plus ARRL's own
/// capture of an IP, and rendering it in an authenticated origin is a stored-XSS vector whatever
/// the markup happens to contain. It was previously kept out of the browser by being a download;
/// now it is neutered instead — <c>Content-Security-Policy: sandbox</c> puts the response in an
/// opaque origin with scripts and form submission blocked, so it can read no cookie of ours and
/// reach nothing. Deleting that header would silently turn a rendered receipt back into an XSS
/// sink, which is why it has a test of its own.</para>
/// </summary>
public class ArrlReceiptDownloadTests : IClassFixture<WebAppFactory>
{
    private const string ReceiptBody =
        "<html><body><h1>Upload Successful</h1><p>ExamSession_HRCC_20260910_0200_arrl.zip</p></body></html>";

    private readonly WebAppFactory _factory;

    public ArrlReceiptDownloadTests(WebAppFactory factory) => _factory = factory;

    private string ReceiptUrl => $"/SessionManager/SubmitToVec/{_factory.Seeded.SessionId}?handler=Receipt";

    private async Task SeedFilingAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.ArrlVecSubmissions.RemoveRange(await db.ArrlVecSubmissions.ToListAsync());

        var session = await db.Sessions.FirstAsync(s => s.Id == _factory.Seeded.SessionId);
        db.ArrlVecSubmissions.Add(new ArrlVecSubmission
        {
            SessionId = session.Id,
            TeamId = session.TeamId,
            SubmittedUtc = DateTime.UtcNow.AddDays(-1),
            FullName = "Mike Wills",
            CallSign = "WX0MIK",
            Email = "ve@localhost",
            Phone = "5555550123",
            SessionDate = "2026-09-09",
            Location = "Mankato, MN",
            PaymentMethod = ArrlPaymentMethod.MailIn,
            AmountCharged = "14.00",
            ArchiveFileName = "ExamSession_HRCC_20260910_0200_arrl.zip",
            ArchiveByteCount = 377_000,
            Outcome = ArrlReceiptOutcome.Succeeded,
            ResponseBody = ReceiptBody
        });

        await db.SaveChangesAsync();
    }

    private async Task ClearAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.ArrlVecSubmissions.RemoveRange(await db.ArrlVecSubmissions.ToListAsync());
        await db.SaveChangesAsync();
    }

    /// <summary>The reported bug: a browser has to be told this is HTML before it will draw it.</summary>
    [Fact]
    public async Task TheReceiptIsServedAsHtml()
    {
        await SeedFilingAsync();
        try
        {
            var client = _factory.CreateClientAs(UserRole.SystemAdmin);
            var response = await client.GetAsync(ReceiptUrl);

            response.EnsureSuccessStatusCode();
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            await ClearAsync();
        }
    }

    /// <summary>It opens in the tab rather than landing in Downloads, so it must not be an attachment.</summary>
    [Fact]
    public async Task TheReceiptIsNotAnAttachment()
    {
        await SeedFilingAsync();
        try
        {
            var client = _factory.CreateClientAs(UserRole.SystemAdmin);
            var response = await client.GetAsync(ReceiptUrl);

            Assert.NotEqual("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        }
        finally
        {
            await ClearAsync();
        }
    }

    /// <summary>
    /// ⚠️ <b>The load-bearing test.</b> Rendering ARRL's markup in this origin without the sandbox
    /// would execute anything it contained with a signed-in Session Manager's cookies. The bare
    /// <c>sandbox</c> directive — no <c>allow-scripts</c>, no <c>allow-same-origin</c> — is what
    /// makes a rendered receipt inert, and it must replace the app-wide policy rather than sit
    /// beside it.
    /// </summary>
    [Fact]
    public async Task TheReceiptIsSandboxed()
    {
        await SeedFilingAsync();
        try
        {
            var client = _factory.CreateClientAs(UserRole.SystemAdmin);
            var response = await client.GetAsync(ReceiptUrl);

            var csp = Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
            Assert.Equal("sandbox", csp);
            Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        }
        finally
        {
            await ClearAsync();
        }
    }

    /// <summary>The bytes are ARRL's, verbatim — the point of keeping it is evidentiary.</summary>
    [Fact]
    public async Task TheReceiptIsHandedBackVerbatim()
    {
        await SeedFilingAsync();
        try
        {
            var client = _factory.CreateClientAs(UserRole.SystemAdmin);
            var body = await client.GetStringAsync(ReceiptUrl);

            Assert.Equal(ReceiptBody, body);
        }
        finally
        {
            await ClearAsync();
        }
    }

    /// <summary>A session with no filing has no receipt to hand back.</summary>
    [Fact]
    public async Task WithNoFiling_ThereIsNoReceipt()
    {
        await ClearAsync();

        var client = _factory.CreateClientAs(UserRole.SystemAdmin);
        var response = await client.GetAsync(ReceiptUrl);

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }
}
