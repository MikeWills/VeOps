using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VeOps.Core.Data;
using VeOps.Core.Zoom;

namespace VeOps.Core.VolunteerExaminers;

public enum VeAttendanceAuditStatus
{
    Ready,
    SessionNotFound,
    NoZoomMeeting,
    ZoomNotConfigured,

    /// <summary>Zoom has no record of the meeting yet — still open, or only just ended.</summary>
    NotEndedYet,

    /// <summary>Zoom refused or failed — most likely the team's Zoom app lacks the past-participants scope.</summary>
    ZoomFailed
}

public sealed record VeAttendanceAuditResult(VeAttendanceAuditStatus Status, AttendanceComparison? Comparison = null);

/// <summary>
/// The VE attendance audit (#576): who was on Zoom against who is on the session's VE roster, so a
/// Session Manager can confirm those listed actually sat the session before filing with the VEC.
///
/// <para><b>Hints for a person, never a verdict, and nothing is stored.</b> Names are fetched, compared
/// and returned for one page render, so there is no Zoom-name PII to retain or purge. Only a finished
/// meeting's list is ever reported as attendance — Zoom's participant endpoints answer 404 until the
/// host ends the meeting (the live list is a Business-plan API), so "not ended yet" is its own status
/// and is never presented as an empty room. See docs/ve-attendance-audit.md.</para>
/// </summary>
public class VeAttendanceAuditService(AppDbContext dbContext, IZoomClient zoomClient, ILogger<VeAttendanceAuditService> logger)
{
    public async Task<VeAttendanceAuditResult> CheckAsync(int sessionId, CancellationToken cancellationToken)
    {
        var session = await dbContext.Sessions
            .AsNoTracking()
            .Include(s => s.Team)
            .Include(s => s.SessionVolunteerExaminers).ThenInclude(l => l.VolunteerExaminer)
            .AsSplitQuery()
            .FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken);

        if (session is null)
        {
            return new VeAttendanceAuditResult(VeAttendanceAuditStatus.SessionNotFound);
        }

        if (string.IsNullOrWhiteSpace(session.ZoomMeetingId))
        {
            return new VeAttendanceAuditResult(VeAttendanceAuditStatus.NoZoomMeeting);
        }

        if (!session.Team.IsZoomConfigured)
        {
            return new VeAttendanceAuditResult(VeAttendanceAuditStatus.ZoomNotConfigured);
        }

        ZoomPastParticipants participants;
        try
        {
            participants = await zoomClient.GetPastParticipantsAsync(session.Team.ToZoomCredentials(), session.ZoomMeetingId, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged with the exception (Zoom's body names the missing scope) but never shown raw.
            logger.LogWarning(ex, "Zoom attendance lookup failed for session {SessionId} (team {TeamId})", session.Id, session.TeamId);
            return new VeAttendanceAuditResult(VeAttendanceAuditStatus.ZoomFailed);
        }

        if (!participants.MeetingEnded)
        {
            return new VeAttendanceAuditResult(VeAttendanceAuditStatus.NotEndedYet);
        }

        var roster = session.SessionVolunteerExaminers
            .Select(l => new AttendanceRosterEntry(l.VolunteerExaminer.CallSign, l.VolunteerExaminer.Name))
            .OrderBy(e => e.CallSign ?? "~")
            .ToList();

        return new VeAttendanceAuditResult(VeAttendanceAuditStatus.Ready, ZoomAttendanceMatcher.Compare(roster, participants.Names));
    }
}
