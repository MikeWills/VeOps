using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.Integrations;
using VeOps.Core.Notifications;
using VeOps.Core.Payments;

namespace VeOps.Core.Messaging.Scanners;

/// <summary>
/// "The session starts in N hours." Replaces
/// <c>CandidateNotificationService.SendDayBeforeRemindersAsync</c>, whose 24 is now a rule's
/// <c>ParameterHours</c>.
///
/// <para><b>A rolling window between two instants, never a calendar date (#220.)</b> This used to
/// compare against "tomorrow" as a UTC calendar date, which broke twice over. Sessions run in the
/// evening Eastern, and anything from ~8pm ET onward is already tomorrow in raw UTC — so a
/// Monday-evening session is stored on Tuesday, "tomorrow in UTC" is the session's own Eastern day,
/// and the "day before" reminder went out on the day of the session. On top of that the job ticks on
/// an interval from Worker start, so which side of UTC midnight it landed on depended on when the
/// Worker was last deployed: the same session could be reminded anywhere from ~36 hours out to ~3
/// hours out. Comparing two instants removes the whole class — there is no calendar date, so there is
/// no timezone to get wrong — and it is why the rule's parameter is hours.</para>
/// </summary>
public class BeforeSessionStartScanner(AppDbContext dbContext, IOptions<AppOptions> appOptions) : IMessageTriggerScanner
{
    public MessageTrigger Trigger => MessageTrigger.BeforeSessionStart;

    public async Task<IReadOnlyList<MessageSubject>> ScanAsync(
        Team team, MessageRule rule, EmailSettings emailSettings, DateTime nowUtc, int? onlySessionId, CancellationToken cancellationToken)
    {
        var parameterHours = MessageTriggerDefinitions.ParameterHoursOrDefault(rule);
        var windowEndUtc = nowUtc.AddHours(parameterHours);

        // The moment this trigger is about is (start - ParameterHours), so requiring that moment to
        // fall at or after the real floor means requiring the start itself to be at least that far
        // past it. This is the guarantee Mike asked for in the issue: add a 7-day rule today and
        // nobody already inside seven days of their session hears from it — and, since 2026-08-25,
        // the same guarantee on re-enabling a disabled rule or configuring email for the first time.
        // See MessageRuleEligibility.
        var earliestStartUtc = MessageRuleEligibility.FloorUtc(team, rule).AddHours(parameterHours);

        var settled = dbContext.MessageRuleRuns
            .Where(r => r.MessageRuleId == rule.Id && MessageRuleOutcomes.Terminal.Contains(r.Outcome))
            .Select(r => r.SubjectId);

        var candidates = await dbContext.Candidates
            .Include(c => c.Session)
            .Include(c => c.Payments)
            .Where(c => c.PiiPurgedUtc == null
                        && c.Email != null
                        && !settled.Contains(c.Id)
                        && c.Session.TeamId == team.Id
                        && c.Session.Status == SessionStatus.Active
                        // Starts within the window, and has not started yet. The upper bound is what
                        // makes this a reminder rather than a notification about something already
                        // under way; the run marker keeps it to once.
                        && c.Session.ScheduledStartUtc > nowUtc
                        && c.Session.ScheduledStartUtc <= windowEndUtc
                        && c.Session.ScheduledStartUtc >= earliestStartUtc
                        && (onlySessionId == null || c.SessionId == onlySessionId))
            .ToListAsync(cancellationToken);

        // Registered candidates per session (#491, same shape as CandidateRegisteredScanner) — needed
        // only so MessageSessionContext.RegisteredCandidateCount has an answer; the calendar invite
        // itself only needs Title/ScheduledStartUtc/DurationMinutes/ZoomJoinUrl, all already on
        // candidate.Session, but a rule set to MessageFanOut.PerSession wants this too.
        var sessionIds = candidates.Select(c => c.SessionId).Distinct().ToList();

        // Which of those sessions run under a VEC with a youth program, asked as a query rather than
        // read off candidate.Session.Vec (2026-09-08). A navigation would need a ThenInclude that
        // nothing would fail without: EF's change tracker fixes the navigation up from whatever the
        // test seeded in the same context, so a forgotten Include passes every test here and throws
        // in the Worker. This cannot be got wrong that way.
        var youthProgramSessionIds = (await dbContext.Sessions
            .Where(s => sessionIds.Contains(s.Id) && s.Vec.SupportsYouthProgram)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        var registeredCounts = await dbContext.Candidates
            .Where(c => sessionIds.Contains(c.SessionId))
            .GroupBy(c => c.SessionId)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count, cancellationToken);

        return [.. candidates.Select(candidate =>
        {
            // Same "most recent Unpaid, else most recent overall" rule the session roster's own
            // Payment chip uses (Detail.cshtml.cs) — an outstanding fee takes priority over an
            // older paid/not-applicable row, so this reads the same on both screens (#490).
            var primaryPayment = candidate.Payments.OrderByDescending(p => p.CreatedUtc).FirstOrDefault(p => p.Status == PaymentStatus.Unpaid)
                ?? candidate.Payments.OrderByDescending(p => p.CreatedUtc).FirstOrDefault();

            return new MessageSubject(
                candidate.Id,
                MessageSubjectType.Candidate,
                candidate.Email,
                new Dictionary<string, string>
                {
                    ["CandidateName"] = candidate.Name ?? "",
                    ["CandidateFirstName"] = candidate.FirstName ?? "",
                    ["SessionDate"] = SessionTimeFormatter.ForCandidate(candidate.Session.ScheduledStartUtc),
                    ["ZoomJoinUrl"] = candidate.Session.ZoomJoinUrl ?? "",
                    ["OutstandingPaymentLinkUrl"] = candidate.Payments
                        .Where(p => p.Status == PaymentStatus.Unpaid && p.PaymentLinkUrl != null)
                        .OrderByDescending(p => p.CreatedUtc)
                        .Select(p => p.PaymentLinkUrl)
                        .FirstOrDefault() ?? "",
                    // Blank once the fee is settled, deliberately: the youth page answers a payment
                    // that is no longer Unpaid with "already resolved", so offering the link to
                    // somebody who has paid sends them to a dead end. Same reasoning as
                    // OutstandingPaymentLinkUrl above, which is blank for exactly the same people.
                    ["YouthPaymentLinkUrl"] = YouthConfirmLink.For(
                        appOptions.Value.PublicBaseUrl,
                        youthProgramSessionIds.Contains(candidate.SessionId),
                        candidate.Payments
                            .Where(p => p.Status == PaymentStatus.Unpaid && p.YouthConfirmationToken != null)
                            .OrderByDescending(p => p.CreatedUtc)
                            .Select(p => p.YouthConfirmationToken)
                            .FirstOrDefault()),
                    ["PaymentStatus"] = PaymentStatusText.For(primaryPayment?.Status)
                },
                sentUtc => candidate.DayBeforeReminderSentUtc = sentUtc)
            {
                SessionLeadCallSign = candidate.Session.TeamLeadCallSign,
                Session = SessionContext(candidate.Session, registeredCounts)
            };
        })];
    }

    /// <summary>Same shape as <c>CandidateRegisteredScanner.SessionContext</c> — kept separate rather
    /// than shared, since sharing would mean a public static helper for two call sites that would
    /// otherwise stay private.</summary>
    private static MessageSessionContext SessionContext(Session session, IReadOnlyDictionary<int, int> registeredCounts) =>
        new(session.Id,
            session.Title,
            session.ScheduledStartUtc,
            registeredCounts.TryGetValue(session.Id, out var count) ? count : 0,
            session.DurationMinutes,
            session.ZoomJoinUrl);
}
