using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using VeOps.Core.Zoom;
using Xunit;

namespace VeOps.Core.Tests;

/// <summary>
/// <c>GetPastParticipantsAsync</c> (#576). Verified live 2026-10-28 on a Zoom Pro account:
/// <c>GET /past_meetings/{id}/participants</c> answers 404 <c>3001</c> while the meeting is still open
/// and 200 once it has ended. The two behaviours that matter are that a 404 is "not ready yet", which
/// the page tells the Session Manager about, and that every other failure — above all a missing scope,
/// which is a 400 — still throws so it is never shown as "nobody attended".
/// </summary>
public class ZoomClientPastParticipantsTests
{
    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    private sealed class FakeZoomHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];
        private int _next;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/oauth/token", StringComparison.Ordinal))
            {
                return Task.FromResult(Respond(HttpStatusCode.OK, """{"access_token":"test-token","expires_in":3600}"""));
            }

            RequestedUrls.Add(url);
            var (status, body) = _next < responses.Length ? responses[_next] : (HttpStatusCode.OK, """{"participants":[]}""");
            _next++;
            return Task.FromResult(Respond(status, body));
        }

        private static HttpResponseMessage Respond(HttpStatusCode status, string body) => new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    private static ZoomCredentials Credentials() => new(
        TeamId: 1, AccountId: "acct", ClientId: "id", ClientSecret: "secret", UserId: "zoom-user");

    private static ZoomClient Create(FakeZoomHandler handler) =>
        new(new FixedTimeProvider(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc)),
            NullLogger<ZoomClient>.Instance,
            handler);

    private static (HttpStatusCode, string) Page(string nextToken, params string[] names) =>
        (HttpStatusCode.OK, $$"""{"participants":[{{string.Join(",", names.Select(n => $$"""{"id":"x","name":"{{n}}"}"""))}}],"next_page_token":"{{nextToken}}"}""");

    [Fact]
    public async Task EndedMeeting_ReturnsParticipantNames()
    {
        var handler = new FakeZoomHandler(Page("", "Mike WX0MIK", "Chris K0JCE"));

        var result = await Create(handler).GetPastParticipantsAsync(Credentials(), "81479182462", CancellationToken.None);

        Assert.True(result.MeetingEnded);
        Assert.Equal(["Mike WX0MIK", "Chris K0JCE"], result.Names);
        Assert.Contains("/v2/past_meetings/81479182462/participants", handler.RequestedUrls[0]);
    }

    [Fact]
    public async Task MeetingStillOpen_404_IsNotReadyAndDoesNotThrow()
    {
        var handler = new FakeZoomHandler((HttpStatusCode.NotFound, """{"code":3001,"message":"Meeting does not exist: 81479182462."}"""));

        var result = await Create(handler).GetPastParticipantsAsync(Credentials(), "81479182462", CancellationToken.None);

        Assert.False(result.MeetingEnded);
        Assert.Empty(result.Names);
    }

    [Fact]
    public async Task MissingScope_400_Throws_SoItIsNeverShownAsEmpty()
    {
        var handler = new FakeZoomHandler((HttpStatusCode.BadRequest,
            """{"code":4711,"message":"Invalid access token, does not contain scopes:[meeting:read:list_past_participants:admin]."}"""));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Create(handler).GetPastParticipantsAsync(Credentials(), "81479182462", CancellationToken.None));

        Assert.Contains("list_past_participants", ex.Message);
    }

    [Fact]
    public async Task FollowsNextPageToken()
    {
        var handler = new FakeZoomHandler(Page("page-2", "A"), Page("", "B"));

        var result = await Create(handler).GetPastParticipantsAsync(Credentials(), "1", CancellationToken.None);

        Assert.Equal(["A", "B"], result.Names);
        Assert.DoesNotContain("next_page_token", handler.RequestedUrls[0]);
        Assert.Contains("next_page_token=page-2", handler.RequestedUrls[1]);
    }

    [Fact]
    public async Task EndedMeetingWithNoParticipants_IsEndedAndEmpty()
    {
        var handler = new FakeZoomHandler((HttpStatusCode.OK, """{"participants":[],"next_page_token":""}"""));

        var result = await Create(handler).GetPastParticipantsAsync(Credentials(), "1", CancellationToken.None);

        Assert.True(result.MeetingEnded);
        Assert.Empty(result.Names);
    }
}
