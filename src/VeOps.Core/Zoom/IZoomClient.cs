namespace VeOps.Core.Zoom;

/// <summary>
/// Client for the subset of the Zoom Meetings API Phase 2 needs. Wrapped in an interface so
/// scheduling logic can be unit tested without live calls (per the spec's testing rules).
/// </summary>
public interface IZoomClient
{
    Task<ZoomMeeting> CreateMeetingAsync(ZoomCredentials credentials, ZoomMeetingRequest request, CancellationToken cancellationToken);

    Task UpdateMeetingAsync(ZoomCredentials credentials, string meetingId, ZoomMeetingRequest request, CancellationToken cancellationToken);

    Task DeleteMeetingAsync(ZoomCredentials credentials, string meetingId, CancellationToken cancellationToken);

    /// <summary>
    /// This team's scheduled (not yet started/expired) meetings. Used by
    /// SessionEventSchedulingService to check for an already-existing meeting (matched by topic +
    /// start time) before calling CreateMeetingAsync, so a poll that crashed after Zoom's API call
    /// succeeded but before the returned id was persisted doesn't create a duplicate on retry —
    /// same reasoning as IDiscordEventClient.ListEventsAsync, see docs/zoom-discord-scheduling.md.
    /// </summary>
    Task<IReadOnlyList<ZoomMeeting>> ListMeetingsAsync(ZoomCredentials credentials, CancellationToken cancellationToken);

    /// <summary>
    /// Display names of everyone who joined a meeting that has ended — the VE attendance audit (#576).
    /// Needs the <c>meeting:read:list_past_participants:admin</c> scope on the team's Zoom app. A meeting
    /// that is still open returns <c>MeetingEnded = false</c> rather than throwing: that is an expected
    /// state the Session Manager can act on (end the meeting, ask again). Anything else, a missing scope
    /// above all, throws so it is never shown as an empty attendance list. Zoom's live participant list
    /// is a Business-plan API and is deliberately not used — see docs/zoom-discord-scheduling.md.
    /// </summary>
    Task<ZoomPastParticipants> GetPastParticipantsAsync(ZoomCredentials credentials, string meetingId, CancellationToken cancellationToken);
}
