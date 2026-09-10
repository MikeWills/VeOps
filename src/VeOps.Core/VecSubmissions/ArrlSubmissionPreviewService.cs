using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.ExamTools;
using VeOps.Core.Uls;

namespace VeOps.Core.VecSubmissions;

/// <summary>
/// Builds the ARRL submission preview (issue #197): every form field with the value that would be
/// posted, the archive that would go with it, and the review aids a human needs to judge it.
///
/// <para><b>Resolves and reports; it never sends.</b> The POST lives in its own service — this one
/// exists so the screen can show exactly what would be filed, which is the only safeguard available
/// for a code path that has no sandbox and cannot be tested end to end.</para>
/// </summary>
public class ArrlSubmissionPreviewService(
    AppDbContext dbContext,
    IExamToolsClient examToolsClient,
    IOptions<ExamToolsOptions> examToolsOptions,
    ILogger<ArrlSubmissionPreviewService> logger)
{
    /// <summary>
    /// <c>Vec.MatchCode</c> for ARRL, lower-cased. Matched on the code and <b>never the display
    /// name</b>, which is "ARRL" on this deployment and "ARRL-VEC" upstream.
    /// </summary>
    public const string ArrlMatchCode = "arrl";

    public async Task<ArrlSubmissionPreview> BuildAsync(int sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .Include(s => s.Team)
            .Include(s => s.Vec)
            .Include(s => s.FeeConfiguration)
            .Include(s => s.Candidates).ThenInclude(c => c.Payments).ThenInclude(p => p.Refunds)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
        {
            return new ArrlSubmissionPreview { Status = ArrlSubmissionPreviewStatus.SessionNotFound, SessionId = sessionId };
        }

        var team = session.Team;
        var basics = new ArrlSubmissionPreview
        {
            Status = ArrlSubmissionPreviewStatus.Ready,
            SessionId = session.Id,
            SessionTitle = session.Title,
            TeamName = team.Name,
            AlreadySubmitted = session.VecSubmissionStatus == VecSubmissionStatus.Submitted
        };

        // One submitter, no fallback (#197's first constraint): a session under any other VEC must
        // find nothing rather than be handed ARRL's submitter.
        if (!string.Equals(session.Vec.MatchCode, ArrlMatchCode, StringComparison.OrdinalIgnoreCase))
        {
            return basics with { Status = ArrlSubmissionPreviewStatus.NotAnArrlSession };
        }

        // Checked before the archive is fetched, so an unconfigured team costs nothing and is told
        // what is wrong rather than watching a download succeed into a form it cannot fill.
        if (!team.IsArrlSubmissionConfigured)
        {
            return basics with { Status = ArrlSubmissionPreviewStatus.TeamNotConfigured };
        }

        // A team running against ExamTools' test site has no business filing a real session with a
        // real VEC — checked before anything is fetched, same as the two guards above. Whether *this
        // deployment* is Production or not is irrelevant here: ArrlSubmissionOptions already gates that
        // globally, and this is a second, per-team question — "is the data behind this session real."
        if (ExamToolsCredentials.For(team, examToolsOptions.Value.BaseUrl).IsTestEnvironment)
        {
            return basics with { Status = ArrlSubmissionPreviewStatus.TeamOnTestExamTools };
        }

        var lead = await ResolveLeadAsync(session, cancellationToken);
        var fees = session.GetFeeSummary();

        var email = team.ArrlSubmissionEmailSource == ArrlSubmissionEmailSource.TeamAddress
            ? team.ArrlSubmissionEmail
            : lead?.Email;

        // The lead's name plus the team's postfix, concatenated verbatim — HRCC's real value opens
        // with a slash and no space, and inserting a separator would change what is filed.
        var fullName = lead?.Name is { } leadName
            ? leadName + (team.ArrlSubmissionNamePostfix ?? "")
            : null;

        var preview = basics with
        {
            FullName = NullIfBlank(fullName),
            CallSign = NullIfBlank(lead?.CallSign),
            Email = NullIfBlank(email),
            Phone = NullIfBlank(lead?.Phone),
            // Eastern, not UTC. 697 of 867 stored sessions start between 23:00 and 04:00 UTC, so
            // .Date would file tomorrow's date for most of them — the #248 bug class.
            SessionDate = UlsSchedule.ToEasternDate(session.ScheduledStartUtc)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Location = team.ArrlSubmissionLocation,
            PaymentMethod = team.ArrlSubmissionPaymentMethod,
            AmountCharged = Usd.Raw(fees.TotalRemitToVec),
            Note = team.ArrlSubmissionNote,
            Fees = fees,
            AmountWarnings = BuildAmountWarnings(session),
            YouthFormExpected = HasYouthRatePayment(session)
        };

        preview = preview with { MissingRequiredFields = FindMissingFields(preview) };

        return AttachArchive(preview, session, team);
    }

    /// <summary>
    /// The same resolution <c>MessageDispatchService</c> does for a rule's Reply-To — normalized call
    /// sign to a <see cref="VolunteerExaminer"/>. Null when the session names no lead, or names one
    /// with no matching record.
    /// </summary>
    private async Task<VolunteerExaminer?> ResolveLeadAsync(Session session, CancellationToken cancellationToken)
    {
        if (CallSign.Normalize(session.TeamLeadCallSign) is not { } callSign)
        {
            return null;
        }

        var lead = await dbContext.VolunteerExaminers.FirstOrDefaultAsync(v => v.CallSign == callSign, cancellationToken);
        if (lead is null)
        {
            logger.LogInformation(
                "Session {SessionId} names lead {CallSign}, who has no VE record — the ARRL form's contact fields cannot be prefilled",
                session.Id, callSign);
        }

        return lead;
    }

    /// <summary>
    /// ARRL marks all four contact fields required, and none of them is guaranteed: ExamTools supplies
    /// no contact details at all, so email and phone are only ever filled in by an admin or the VE —
    /// and the retention purge clears both. Named individually so the operator knows which box to fill.
    /// </summary>
    private static List<string> FindMissingFields(ArrlSubmissionPreview preview)
    {
        var missing = new List<string>();
        if (preview.FullName is null) missing.Add("Full name");
        if (preview.CallSign is null) missing.Add("Call sign");
        if (preview.Email is null) missing.Add("Email address");
        if (preview.Phone is null) missing.Add("Phone number");
        return missing;
    }

    /// <summary>
    /// The two ways <c>GetFeeSummary</c>'s total can disagree with money actually received. Neither is
    /// corrected automatically — only a human knows whether a refunded candidate was filed, or whether
    /// a short payment is being chased — but a confident number with no sign that its inputs are
    /// unusual is worse than no derivation at all.
    /// </summary>
    private static List<string> BuildAmountWarnings(Session session)
    {
        var warnings = new List<string>();
        var paid = session.Candidates.SelectMany(c => c.Payments).Where(p => p.Status == PaymentStatus.Paid).ToList();

        // Refunds are netted out of the amount now (Mike, 2026-08-19: a refunded fee is not owed to
        // the VEC, because the person did not test). Still surfaced, because a total that is lower
        // than the session's headcount implies is exactly the figure somebody will query — this says
        // why before they ask, rather than warning them about something the code has handled.
        var refunded = paid.Count(p => p.Refunds.Any(r => r.Status is not (RefundStatus.Rejected or RefundStatus.Failed)));
        if (refunded > 0)
        {
            warnings.Add($"{refunded} refunded payment(s) have been left out of this amount — a refunded candidate did not test, so nothing is owed for them.");
        }

        // Square reported a different figure than was owed — the out-of-band youth rate is the routine
        // cause, and it leaves Amount at the standard rate while less money arrived.
        var mismatched = paid.Count(p => p.AmountMismatchFlaggedUtc is not null);
        if (mismatched > 0)
        {
            warnings.Add($"{mismatched} payment(s) were flagged because the amount Square reported differs from the amount owed.");
        }

        return warnings;
    }

    /// <summary>A youth-rate payment is when ARRL also expects the youth grant program form — the second of the two files.</summary>
    private static bool HasYouthRatePayment(Session session)
    {
        if (session.FeeConfiguration.YouthExamFeeAmount is not { } youthAmount)
        {
            return false;
        }

        return session.Candidates
            .SelectMany(c => c.Payments)
            .Where(p => p.Status == PaymentStatus.Paid)
            .Any(p => p.Amount == youthAmount || p.SquareAmountPaidUsd == youthAmount);
    }

    /// <summary>
    /// Downloads the archive at submission time. <b>The only place this app downloads one</b> — the
    /// preview names it from local state instead (see <c>AttachArchive</c>), so ExamTools records one
    /// download per filing rather than one per page view plus one at the button.
    ///
    /// <para>Nothing is carried over or cached between the two requests, so what is filed is the
    /// archive as it stands when the button is pressed, and a page left open costs nothing.</para>
    /// </summary>
    public async Task<ArrlSubmissionFile?> FetchArchiveFileAsync(int sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .Include(s => s.Team)
            .Include(s => s.Vec)
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null || !session.Team.IsExamToolsConfigured)
        {
            return null;
        }

        var credentials = ExamToolsCredentials.For(session.Team, examToolsOptions.Value.BaseUrl);
        var download = await examToolsClient.DownloadVecArchiveAsync(
            credentials, session.ExamToolsSessionId, session.Vec.MatchCode, cancellationToken);

        if (download.Outcome != VecArchiveDownloadOutcome.Succeeded || download.Content is null)
        {
            return null;
        }

        var fileName = download.FileName
                       ?? VecArchiveFileName.Build(session.Team.ExamToolsTeamCode!, session.ScheduledStartUtc, session.Vec.MatchCode);

        return new ArrlSubmissionFile(fileName, download.Content);
    }

    /// <summary>
    /// Names the archive and says whether it can be filed — <b>without downloading anything</b>.
    ///
    /// <para>This used to pull the whole archive on every render and keep only the filename and the
    /// byte count, so ExamTools' audit log recorded a download per page view on top of the one at
    /// filing. Three showed up against a single session (reported 2026-09-09), because the count
    /// tracked page views rather than filings.</para>
    ///
    /// <para>Mike: <i>"You can check to see if the session is closed without downloading the file."</i>
    /// Both facts the preview needs are already here. Readiness is
    /// <see cref="Session.ExamToolsClosedUtc"/>, stamped by ingestion — <b>deliberately that and not
    /// <see cref="Session.IsCompleted"/></b>, which is also true when a Session Manager clicked "Mark
    /// session completed"; a person marking it does not make ExamTools produce an archive. The name is
    /// rebuilt by <see cref="VecArchiveFileName"/>, the same helper that already covered a missing
    /// Content-Disposition.</para>
    ///
    /// <para>What is given up: the exact byte count, and ExamTools' own wording for a session that is
    /// not ready. Neither is worth a download per page view — and the wording is still surfaced at
    /// filing time, where <see cref="FetchArchiveFileAsync"/> genuinely asks.</para>
    /// </summary>
    private static ArrlSubmissionPreview AttachArchive(ArrlSubmissionPreview preview, Session session, Team team)
    {
        if (!team.IsExamToolsConfigured)
        {
            return preview with
            {
                ArchiveMessage = "This team has no ExamTools credentials, so the VEC archive cannot be downloaded."
            };
        }

        if (session.ExamToolsClosedUtc is null)
        {
            return preview with
            {
                ArchiveOutcome = VecArchiveDownloadOutcome.SessionNotComplete,
                ArchiveMessage = "ExamTools has not closed this session yet."
            };
        }

        return preview with
        {
            ArchiveOutcome = VecArchiveDownloadOutcome.Succeeded,
            ArchiveFileName = VecArchiveFileName.Build(team.ExamToolsTeamCode!, session.ScheduledStartUtc, session.Vec.MatchCode)
        };
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
