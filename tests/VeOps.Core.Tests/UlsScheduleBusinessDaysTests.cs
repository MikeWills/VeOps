using VeOps.Core.Uls;
using Xunit;

namespace VeOps.Core.Tests;

/// <summary>
/// <c>UlsSchedule.AddBusinessDays</c> backs <c>{{FccNoticeExpectedBy}}</c> — "ARRL processes sessions
/// in 1-3 business days", counted from the session's Eastern date and skipping weekends and federal holidays.
/// </summary>
public class UlsScheduleBusinessDaysTests
{
    [Theory]
    // A Wednesday session: Thu, Fri, Mon.
    [InlineData("2026-09-16", 3, "2026-09-21")]
    // A Friday session: Mon, Tue, Wed — the weekend is not counted.
    [InlineData("2026-09-18", 3, "2026-09-23")]
    // A Saturday session (the common one): the count starts on Monday.
    [InlineData("2026-09-19", 3, "2026-09-23")]
    [InlineData("2026-09-20", 1, "2026-09-21")]
    [InlineData("2026-09-16", 0, "2026-09-16")]
    // The Friday before Labor Day: Mon the 7th is a holiday, so Tue, Wed, Thu.
    [InlineData("2026-09-04", 3, "2026-09-10")]
    // Wednesday before Thanksgiving: Fri, Mon, Tue — Thursday is skipped.
    [InlineData("2026-11-25", 3, "2026-12-01")]
    // Independence Day 2026 is a Saturday, observed Friday the 3rd: Thu Jul 2 + 1 lands on Mon the 6th.
    [InlineData("2026-07-02", 1, "2026-07-06")]
    public void SkipsWeekendsAndFederalHolidays(string from, int businessDays, string expected)
    {
        var result = UlsSchedule.AddBusinessDays(DateTime.Parse(from), businessDays);
        Assert.Equal(DateTime.Parse(expected), result);
    }
}
