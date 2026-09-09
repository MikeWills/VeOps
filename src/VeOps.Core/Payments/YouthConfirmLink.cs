namespace VeOps.Core.Payments;

/// <summary>
/// The one place a <c>{{YouthPaymentLinkUrl}}</c> value is built (2026-09-08). It was written out
/// twice — <c>CandidateNotificationService</c> and <c>CandidateRegisteredScanner</c> — and adding it
/// to the two before-the-session reminders would have made four copies of the same three-line rule.
///
/// <para>Deliberately takes plain values rather than a <c>Candidate</c>: the youth flag lives on the
/// session's <c>Vec</c>, and a helper that walked <c>candidate.Session.Vec</c> itself would throw a
/// <see cref="NullReferenceException"/> in any caller whose query forgot the <c>ThenInclude</c> —
/// while passing in every test, because EF's change tracker fixes the navigation up from whatever
/// the test happened to seed in the same context. Taking the bool forces each caller to answer the
/// question in a way its own query can be held to.</para>
/// </summary>
public static class YouthConfirmLink
{
    /// <summary>
    /// The candidate-facing youth-rate confirmation URL, or an empty string when there is nothing
    /// useful to link to — the VEC runs no youth program, or the payment carries no confirmation
    /// token (fee collection was off when it was created, or it is not the initial exam fee).
    ///
    /// <para>Empty rather than null, and rendered as a blank line rather than hidden: there is no
    /// conditional-block templating here, so a team's message body that mentions the youth rate
    /// simply comes out without a link for a session where it does not apply.</para>
    /// </summary>
    public static string For(string publicBaseUrl, bool vecSupportsYouthProgram, Guid? youthConfirmationToken) =>
        vecSupportsYouthProgram && youthConfirmationToken is { } token
            ? $"{publicBaseUrl}/youth-confirm/{token}"
            : "";
}
