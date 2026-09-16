using VeOps.Core.Entities;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// Candidate detail's Email is a mailto: link with a copy button beside it (2026-09-15), the same
/// affordance the FRN already has — a Session Manager reaching for a candidate's address is about
/// to write to them or paste it somewhere, and neither should need selecting text by hand.
/// </summary>
public class CandidateDetailContactTests
{
    [Fact]
    public async Task Email_IsAMailtoLink_WithACopyButton()
    {
        using var factory = new WebAppFactory();
        var client = factory.CreateClientAs(UserRole.SessionManager);

        var html = await client.GetStringAsync($"/SessionManager/CandidateDetail/{factory.Seeded.CandidateId}");

        Assert.Contains("href=\"mailto:candidate@localhost\"", html);
        Assert.Contains("data-copy-value=\"candidate@localhost\"", html);
    }
}
