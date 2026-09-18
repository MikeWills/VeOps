using VeOps.Core.Uls;
using Xunit;

namespace VeOps.Core.Tests;

/// <summary>
/// <c>FederalHolidays</c> feeds <c>UlsSchedule.AddBusinessDays</c>. The rules are the OPM ones:
/// fixed dates observed on Friday/Monday when they fall on a weekend, plus four "Nth Monday" days and
/// Thanksgiving. Pinned against a known year so a rule bug shows up as a date, not a boolean.
/// </summary>
public class FederalHolidaysTests
{
    [Fact]
    public void ObservedDatesFor2026MatchOpm()
    {
        var expected = new[]
        {
            new DateTime(2026, 1, 1),   // New Year's Day (Thu)
            new DateTime(2026, 1, 19),  // MLK — 3rd Mon Jan
            new DateTime(2026, 2, 16),  // Presidents' Day — 3rd Mon Feb
            new DateTime(2026, 5, 25),  // Memorial Day — last Mon May
            new DateTime(2026, 6, 19),  // Juneteenth (Fri)
            new DateTime(2026, 7, 3),   // Independence Day: Jul 4 is Sat, observed Fri
            new DateTime(2026, 9, 7),   // Labor Day — 1st Mon Sep
            new DateTime(2026, 10, 12), // Columbus Day — 2nd Mon Oct
            new DateTime(2026, 11, 11), // Veterans Day (Wed)
            new DateTime(2026, 11, 26), // Thanksgiving — 4th Thu Nov
            new DateTime(2026, 12, 25), // Christmas (Fri)
        };

        Assert.Equal(expected, FederalHolidays.ObservedIn(2026));
    }

    [Theory]
    // Sunday holiday is observed Monday: Jul 4, 2027 is a Sunday.
    [InlineData("2027-07-05", true)]
    [InlineData("2027-07-04", false)]
    // Saturday holiday is observed Friday: Christmas 2027 is a Saturday.
    [InlineData("2027-12-24", true)]
    // Dec 31, 2027 is a Friday, observed for New Year's Day 2028 (a Saturday) — the one that crosses a year.
    [InlineData("2027-12-31", true)]
    [InlineData("2026-09-08", false)]
    public void IsObservedHoliday(string date, bool expected) =>
        Assert.Equal(expected, FederalHolidays.IsObserved(DateTime.Parse(date)));
}
