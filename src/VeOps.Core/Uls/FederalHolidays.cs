namespace VeOps.Core.Uls;

/// <summary>
/// The eleven US federal holidays as observed — the days the FCC is closed, and a fair stand-in for
/// the VEC's own closures — computed from OPM's rules rather than a table of dates, so no year has
/// to be added by hand.
///
/// <para><b>What "observed" means here.</b> A fixed-date holiday falling on a Saturday is taken on
/// the Friday before, on a Sunday the Monday after (5 U.S.C. § 6103(b)). That is why
/// <see cref="ObservedIn"/> looks at the neighbouring years too: New Year's Day on a Saturday is
/// observed on December 31 of the year before.</para>
///
/// <para><b>What this deliberately does not know.</b> One-off closures — a day of mourning, a
/// holiday added by statute mid-year — and ARRL's own extra days (the Friday after Thanksgiving,
/// typically). Every-year rules only, which is why a message using
/// <c>{{FccNoticeExpectedBy}}</c> should still say "about".</para>
/// </summary>
public static class FederalHolidays
{
    public static bool IsObserved(DateTime date) => ObservedIn(date.Year).Contains(date.Date);

    /// <summary>Every observed holiday whose observed date falls in <paramref name="year"/>, ascending.</summary>
    public static IReadOnlyList<DateTime> ObservedIn(int year) =>
        [.. Enumerable.Range(year - 1, 3)
            .SelectMany(HolidaysDeclaredIn)
            .Select(Observed)
            .Where(d => d.Year == year)
            .Distinct()
            .Order()];

    /// <summary>The holidays that belong to <paramref name="year"/> by statute, before any weekend shift.</summary>
    private static IEnumerable<DateTime> HolidaysDeclaredIn(int year)
    {
        yield return new DateTime(year, 1, 1);                                   // New Year's Day
        yield return NthWeekday(year, 1, DayOfWeek.Monday, 3);                   // Birthday of Martin Luther King, Jr.
        yield return NthWeekday(year, 2, DayOfWeek.Monday, 3);                   // Washington's Birthday
        yield return LastWeekday(year, 5, DayOfWeek.Monday);                     // Memorial Day
        yield return new DateTime(year, 6, 19);                                  // Juneteenth National Independence Day
        yield return new DateTime(year, 7, 4);                                   // Independence Day
        yield return NthWeekday(year, 9, DayOfWeek.Monday, 1);                   // Labor Day
        yield return NthWeekday(year, 10, DayOfWeek.Monday, 2);                  // Columbus Day
        yield return new DateTime(year, 11, 11);                                 // Veterans Day
        yield return NthWeekday(year, 11, DayOfWeek.Thursday, 4);                // Thanksgiving Day
        yield return new DateTime(year, 12, 25);                                 // Christmas Day
    }

    private static DateTime Observed(DateTime holiday) => holiday.DayOfWeek switch
    {
        DayOfWeek.Saturday => holiday.AddDays(-1),
        DayOfWeek.Sunday => holiday.AddDays(1),
        _ => holiday
    };

    private static DateTime NthWeekday(int year, int month, DayOfWeek dayOfWeek, int n)
    {
        var first = new DateTime(year, month, 1);
        var offset = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(offset + 7 * (n - 1));
    }

    private static DateTime LastWeekday(int year, int month, DayOfWeek dayOfWeek)
    {
        var last = new DateTime(year, month, DateTime.DaysInMonth(year, month));
        var offset = ((int)last.DayOfWeek - (int)dayOfWeek + 7) % 7;
        return last.AddDays(-offset);
    }
}
