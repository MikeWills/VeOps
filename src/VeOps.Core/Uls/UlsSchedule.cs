namespace VeOps.Core.Uls;

public static class UlsSchedule
{
    /// <summary>
    /// US Eastern, the timezone every FCC-side process is anchored to — licenses are issued at
    /// 02:00 ET Tue-Sat and fee payments processed at 18:00 ET Mon-Fri. IANA id resolves
    /// cross-platform since .NET 6 (verified on both Windows and the Linux deploy target).
    ///
    /// <para>Reuse this rather than re-resolving the id. Note that a UTC-based "what day is it"
    /// check is wrong for an ET evening slot: EDT is UTC-4, so anything from ~20:00 ET onward is
    /// already tomorrow in raw UTC.</para>
    /// </summary>
    public static readonly TimeZoneInfo EasternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    /// <summary>
    /// The calendar date a UTC instant falls on <b>in Eastern time</b> — the only correct left-hand
    /// side when comparing one of this app's timestamps against an FCC date.
    ///
    /// <para><b>Why this exists rather than <c>.Date</c>.</b> Every FCC date arrives date-only and is
    /// stamped at UTC midnight by <c>ExamToolsUlsLookupClient.AsUtcDate</c>, so it already *is* a
    /// wall-clock date. This app's own timestamps are real instants. Taking <c>.Date</c> on one of
    /// those answers "what day is it in London", and for any session at or after ~20:00 ET that is
    /// tomorrow — the trap the remarks above warn about, which <c>UlsWatcherService</c> then fell
    /// into anyway (issue #248): an evening session's candidates could never match an application
    /// FCC received the same evening, so they stayed <c>Unmatched</c> permanently.</para>
    ///
    /// <para><c>SpecifyKind</c> first because EF Core/SQLite returns <c>DateTimeKind.Unspecified</c>,
    /// and <c>ConvertTimeFromUtc</c> throws for a value it does not believe is UTC.</para>
    /// </summary>
    public static DateTime ToEasternDate(DateTime utc) => ToEastern(utc).Date;

    /// <summary>
    /// The same conversion keeping the time of day, for callers that need the hour rather than the
    /// calendar date — the daily slot schedule, the year boundary on the VE report, and every
    /// user-facing "… ET" timestamp (#309, DUP-14).
    ///
    /// <para>Five call sites spelled out
    /// <c>ConvertTimeFromUtc(SpecifyKind(x, Utc), UlsSchedule.EasternTimeZone)</c> in full. All five
    /// were correct — they already shared the zone, which is the part that matters — so this is
    /// removing an incantation rather than fixing a bug. The <c>SpecifyKind</c> is the incantation's
    /// load-bearing half: EF Core/SQLite returns <c>DateTimeKind.Unspecified</c> and
    /// <c>ConvertTimeFromUtc</c> throws for a value it does not believe is UTC, so a site that
    /// forgets it works on a freshly-computed timestamp and throws on one read from the database.</para>
    /// </summary>
    public static DateTime ToEastern(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), EasternTimeZone);

    /// <summary>
    /// <paramref name="date"/> plus <paramref name="businessDays"/> weekdays, weekends and observed
    /// federal holidays skipped (<see cref="FederalHolidays"/>) — still "about", never "on", since
    /// one-off closures and the VEC's own extra days are unknowable. The input is a calendar date (pass
    /// <see cref="ToEasternDate"/>'s result, not a raw UTC instant: a Friday-evening session is
    /// already Saturday in UTC and would lose a day). A weekend start counts from the Monday.
    /// </summary>
    public static DateTime AddBusinessDays(DateTime date, int businessDays)
    {
        var result = date.Date;
        for (var remaining = businessDays; remaining > 0; remaining--)
        {
            do
            {
                result = result.AddDays(1);
            }
            while (result.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || FederalHolidays.IsObserved(result));
        }

        return result;
    }
}
