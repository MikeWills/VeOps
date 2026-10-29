using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VeOps.Core.Data;
using VeOps.Core.Entities;
using VeOps.Core.VolunteerExaminers;
using VeOps.Core.Zoom;
using Xunit;

namespace VeOps.Core.Tests;

/// <summary>
/// #576. The audit exists so a Session Manager can compare who was on Zoom with who is on the roster
/// before filing. The states that must never be confused are "Zoom had nothing for us" and "nobody
/// attended": only a finished meeting's list is ever reported as attendance.
/// </summary>
public class VeAttendanceAuditServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeZoom : IZoomClient
    {
        public ZoomPastParticipants Response { get; set; } = new(true, []);
        public Exception? Throw { get; set; }
        public List<(ZoomCredentials Credentials, string MeetingId)> Calls { get; } = [];

        public Task<ZoomPastParticipants> GetPastParticipantsAsync(ZoomCredentials credentials, string meetingId, CancellationToken cancellationToken)
        {
            Calls.Add((credentials, meetingId));
            return Throw is not null ? throw Throw : Task.FromResult(Response);
        }

        public Task<ZoomMeeting> CreateMeetingAsync(ZoomCredentials c, ZoomMeetingRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task UpdateMeetingAsync(ZoomCredentials c, string id, ZoomMeetingRequest r, CancellationToken t) => throw new NotSupportedException();
        public Task DeleteMeetingAsync(ZoomCredentials c, string id, CancellationToken t) => throw new NotSupportedException();
        public Task<IReadOnlyList<ZoomMeeting>> ListMeetingsAsync(ZoomCredentials c, CancellationToken t) => throw new NotSupportedException();
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static VeAttendanceAuditService Create(AppDbContext db, FakeZoom zoom) =>
        new(db, zoom, NullLogger<VeAttendanceAuditService>.Instance);

    private static async Task<Session> SeedAsync(
        AppDbContext db, bool zoomConfigured = true, string? zoomMeetingId = "81479182462", params (string? CallSign, string Name)[] roster)
    {
        var team = new Team
        {
            Name = "TESTTEAM",
            ZoomAccountId = zoomConfigured ? "acct" : null,
            ZoomClientId = zoomConfigured ? "id" : null,
            ZoomClientSecret = zoomConfigured ? "secret" : null,
            CreatedUtc = Now
        };
        var vec = new Vec { Name = "ARRL" };
        var user = new User { Name = "System", Email = "system@localhost", Role = UserRole.SystemAdmin };
        var fee = new FeeConfiguration
        {
            Vec = vec, EffectiveDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            FeeCollectionEnabled = true, ExamFeeAmount = 15m, CreatedByUser = user, CreatedUtc = Now
        };
        db.Teams.Add(team);
        db.FeeConfigurations.Add(fee);
        await db.SaveChangesAsync();

        var session = new Session
        {
            ExamToolsSessionId = "s1", Title = "October Session", ScheduledStartUtc = Now.AddHours(-3), DurationMinutes = 120,
            VecId = vec.Id, TeamId = team.Id, FeeConfigurationId = fee.Id, ZoomMeetingId = zoomMeetingId, CreatedUtc = Now
        };
        foreach (var (callSign, name) in roster)
        {
            session.SessionVolunteerExaminers.Add(new SessionVolunteerExaminer
            {
                VolunteerExaminer = new VolunteerExaminer { Name = name, CallSign = callSign }
            });
        }

        db.Sessions.Add(session);
        await db.SaveChangesAsync();
        return session;
    }

    [Fact]
    public async Task EndedMeeting_ComparesRosterAgainstZoomNames()
    {
        await using var db = CreateContext();
        var session = await SeedAsync(db, roster: [("W0MIK", "Mike Wills"), ("K0ABC", "Absent Ann")]);
        var zoom = new FakeZoom { Response = new ZoomPastParticipants(true, ["Mike - W0MIK", "Observer Jane"]) };

        var result = await Create(db, zoom).CheckAsync(session.Id, CancellationToken.None);

        Assert.Equal(VeAttendanceAuditStatus.Ready, result.Status);
        Assert.Equal(["W0MIK"], result.Comparison!.Present.Select(p => p.Ve.CallSign));
        Assert.Equal(["K0ABC"], result.Comparison.NotInZoom.Select(v => v.CallSign));
        Assert.Equal(["Observer Jane"], result.Comparison.NotOnRoster);
    }

    [Fact]
    public async Task AsksZoomForTheSessionsOwnMeetingWithTheTeamsCredentials()
    {
        await using var db = CreateContext();
        var session = await SeedAsync(db, roster: [("W0MIK", "Mike")]);
        var zoom = new FakeZoom();

        await Create(db, zoom).CheckAsync(session.Id, CancellationToken.None);

        var call = Assert.Single(zoom.Calls);
        Assert.Equal("81479182462", call.MeetingId);
        Assert.Equal(session.TeamId, call.Credentials.TeamId);
        Assert.Equal("acct", call.Credentials.AccountId);
    }

    [Fact]
    public async Task MeetingNotEndedYet_IsNotReady_AndNeverReportedAsEmptyAttendance()
    {
        await using var db = CreateContext();
        var session = await SeedAsync(db, roster: [("W0MIK", "Mike")]);
        var zoom = new FakeZoom { Response = new ZoomPastParticipants(false, []) };

        var result = await Create(db, zoom).CheckAsync(session.Id, CancellationToken.None);

        Assert.Equal(VeAttendanceAuditStatus.NotEndedYet, result.Status);
        Assert.Null(result.Comparison);
    }

    [Fact]
    public async Task SessionWithNoZoomMeeting_NeverCallsZoom()
    {
        await using var db = CreateContext();
        var session = await SeedAsync(db, zoomMeetingId: null, roster: [("W0MIK", "Mike")]);
        var zoom = new FakeZoom();

        var result = await Create(db, zoom).CheckAsync(session.Id, CancellationToken.None);

        Assert.Equal(VeAttendanceAuditStatus.NoZoomMeeting, result.Status);
        Assert.Empty(zoom.Calls);
    }

    [Fact]
    public async Task TeamWithoutZoomCredentials_NeverCallsZoom()
    {
        await using var db = CreateContext();
        var session = await SeedAsync(db, zoomConfigured: false, roster: [("W0MIK", "Mike")]);
        var zoom = new FakeZoom();

        var result = await Create(db, zoom).CheckAsync(session.Id, CancellationToken.None);

        Assert.Equal(VeAttendanceAuditStatus.ZoomNotConfigured, result.Status);
        Assert.Empty(zoom.Calls);
    }

    [Fact]
    public async Task ZoomFailure_IsReportedAsFailed_NotAsNobodyAttended()
    {
        await using var db = CreateContext();
        var session = await SeedAsync(db, roster: [("W0MIK", "Mike")]);
        var zoom = new FakeZoom { Throw = new HttpRequestException("missing scope list_past_participants") };

        var result = await Create(db, zoom).CheckAsync(session.Id, CancellationToken.None);

        Assert.Equal(VeAttendanceAuditStatus.ZoomFailed, result.Status);
        Assert.Null(result.Comparison);
    }

    [Fact]
    public async Task UnknownSession_IsNotFound()
    {
        await using var db = CreateContext();

        var result = await Create(db, new FakeZoom()).CheckAsync(12345, CancellationToken.None);

        Assert.Equal(VeAttendanceAuditStatus.SessionNotFound, result.Status);
    }
}
