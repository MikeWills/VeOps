using Microsoft.EntityFrameworkCore;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.Notifications;

namespace VeOps.Core.Messaging.Scanners;

/// <summary>
/// "This candidate passed" (#401 PR3, narrowed 2026-09-09) — new, and nothing sent here before.
///
/// <para><b>The moment is <see cref="Candidate.TestedUtc"/>, a column added for this.</b>
/// <c>Tested</c> is a bool set from three different places, so before PR3 there was no answer to
/// "when did this become true" — and without one, a rule could not be bounded by its own creation and
/// would reach a year of imported history on its first tick. Candidates who tested before that column
/// existed hold null and are therefore never returned, which is the intended behaviour rather than a
/// gap.</para>
///
/// <para><b>Passing is the point, not merely sitting the exam (2026-09-09).</b> This fired for
/// everyone who tested, which meant the obvious message to hang on it — "congratulations, you
/// passed" — went to the people who failed as well. Mike: "I'd rather have this be just passed
/// candidates." <c>NewLicenseClass</c> is the predicate: <c>ExamResultSyncService</c> sets it only
/// when the sitting earned a class, so a failure never has one.</para>
///
/// <para><b>A useful side effect: it now waits for the result.</b> "Mark session completed" flips
/// <c>Tested</c> for the whole roster before anyone is graded, so the old shape could congratulate a
/// room on the night, outcome unknown. A candidate marked tested that way simply does not match
/// until their graded result arrives — and then does, on that day's scan, because nothing settles
/// the subject until it is actually sent.</para>
///
/// <para>For the FCC-side milestone — the call sign existing — see
/// <see cref="MessageTrigger.LicenseGranted"/>, which is days later and a different message.</para>
/// </summary>
public class CandidatePassedScanner(AppDbContext dbContext) : IMessageTriggerScanner
{
    public MessageTrigger Trigger => MessageTrigger.CandidatePassed;

    public async Task<IReadOnlyList<MessageSubject>> ScanAsync(
        Team team, MessageRule rule, EmailSettings emailSettings, DateTime nowUtc, int? onlySessionId, CancellationToken cancellationToken)
    {
        var settled = dbContext.MessageRuleRuns
            .Where(r => r.MessageRuleId == rule.Id && MessageRuleOutcomes.Terminal.Contains(r.Outcome))
            .Select(r => r.SubjectId);

        var floorUtc = MessageRuleEligibility.FloorUtc(team, rule);
        var candidates = await dbContext.Candidates
            .Include(c => c.Session)
            .Where(c => c.PiiPurgedUtc == null
                        && c.Email != null
                        && !settled.Contains(c.Id)
                        && c.Tested
                        // The moment, and the bound. Null means "tested before this column existed",
                        // which excludes every backfilled candidate without needing an age window.
                        && c.TestedUtc != null
                        && c.TestedUtc >= floorUtc
                        // A withdrawn candidate is ApplicationStatus.NotTested and never actually sat
                        // anything, whatever a bulk "mark session completed" left on the row.
                        && c.ApplicationStatus != CandidateApplicationStatus.NotTested
                        // Passed, and this is the whole of what "passed" means here: a class earned
                        // by this sitting. Not ApplicationStatus (Granted is the FCC weeks later,
                        // Received says nothing about the grade) and not an absence of Failed, which
                        // would let an ungraded row through the moment somebody marked the session
                        // completed.
                        && c.NewLicenseClass != null
                        && c.Session.TeamId == team.Id
                        && c.Session.Status == SessionStatus.Active
                        && (onlySessionId == null || c.SessionId == onlySessionId))
            .ToListAsync(cancellationToken);

        return [.. candidates.Select(candidate => new MessageSubject(
            candidate.Id,
            MessageSubjectType.Candidate,
            candidate.Email,
            new Dictionary<string, string>
            {
                ["CandidateName"] = candidate.Name ?? "",
                ["CandidateFirstName"] = candidate.FirstName ?? "",
                ["SessionDate"] = SessionTimeFormatter.ForCandidate(candidate.Session.ScheduledStartUtc),
                // Almost always blank here, and correctly so: the FCC has not issued anything yet.
                // Offered because a team may write "your call sign, once it arrives, will be…" and a
                // token that silently does not exist is worse than one that renders empty.
                ["CallSign"] = candidate.CallSign ?? ""
            })
            { SessionLeadCallSign = candidate.Session.TeamLeadCallSign })];
    }
}
