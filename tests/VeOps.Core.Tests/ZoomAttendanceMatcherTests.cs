using VeOps.Core.Zoom;
using Xunit;

namespace VeOps.Core.Tests;

/// <summary>
/// #576. The matcher only produces hints for a person comparing two lists by eye, so the cases that
/// matter are the ones that would make a hint wrong: a call sign found inside a longer one, and a
/// VE who can never match because their profile has no call sign.
/// </summary>
public class ZoomAttendanceMatcherTests
{
    private static AttendanceRosterEntry Ve(string? callSign, string name = "Some VE") => new(callSign, name);

    [Theory]
    [InlineData("W0MIK")]
    [InlineData("w0mik")]
    [InlineData("Mike Wills - W0MIK")]
    [InlineData("W0MIK/Mike")]
    [InlineData("(W0MIK) Mike")]
    [InlineData("Mike W0MIK")]
    public void CallSignInZoomName_Matches(string zoomName)
    {
        var result = ZoomAttendanceMatcher.Compare([Ve("W0MIK")], [zoomName]);

        Assert.Single(result.Present);
        Assert.Empty(result.NotInZoom);
        Assert.Empty(result.NotOnRoster);
    }

    [Theory]
    [InlineData("KW0MIKE")]
    [InlineData("W0MIK1")]
    [InlineData("AW0MIK")]
    public void CallSignInsideLongerCallSign_DoesNotMatch(string zoomName)
    {
        var result = ZoomAttendanceMatcher.Compare([Ve("W0MIK")], [zoomName]);

        Assert.Empty(result.Present);
        Assert.Single(result.NotInZoom);
        Assert.Equal([zoomName], result.NotOnRoster);
    }

    [Fact]
    public void RosterVeWithNoZoomName_IsNotInZoom()
    {
        var result = ZoomAttendanceMatcher.Compare([Ve("W0MIK"), Ve("K0ABC")], ["Mike - W0MIK"]);

        Assert.Equal(["W0MIK"], result.Present.Select(p => p.Ve.CallSign));
        Assert.Equal(["K0ABC"], result.NotInZoom.Select(v => v.CallSign));
        Assert.Empty(result.NotOnRoster);
    }

    [Fact]
    public void ZoomNameMatchingNoVe_IsNotOnRoster()
    {
        var result = ZoomAttendanceMatcher.Compare([Ve("W0MIK")], ["Mike - W0MIK", "Observer Jane"]);

        Assert.Equal(["Observer Jane"], result.NotOnRoster);
    }

    [Fact]
    public void VeWithNoCallSign_CanNeverMatch()
    {
        // A blank call sign must not behave as "matches everything" (an empty substring is in every string).
        foreach (var blank in new string?[] { null, "", "  " })
        {
            var result = ZoomAttendanceMatcher.Compare([Ve(blank, "No Call")], ["Anyone"]);

            Assert.Empty(result.Present);
            Assert.Single(result.NotInZoom);
            Assert.Equal(["Anyone"], result.NotOnRoster);
        }
    }

    [Fact]
    public void SameVeJoinedTwice_IsOneMatchAndBothNamesAreConsumed()
    {
        // Laptop plus phone: neither device should be reported as an unknown extra.
        var result = ZoomAttendanceMatcher.Compare([Ve("W0MIK")], ["Mike - W0MIK", "W0MIK phone"]);

        var present = Assert.Single(result.Present);
        Assert.Equal(["Mike - W0MIK", "W0MIK phone"], present.ZoomNames);
        Assert.Empty(result.NotOnRoster);
    }

    [Fact]
    public void BlankZoomNames_AreIgnoredAndNamesAreTrimmed()
    {
        var result = ZoomAttendanceMatcher.Compare([Ve("W0MIK")], ["", "   ", "  Observer  "]);

        Assert.Equal(["Observer"], result.NotOnRoster);
    }

    [Fact]
    public void RejoinsAreListedOnce_ByNameIgnoringCaseAndSpacing()
    {
        // From a real 49-row Zoom report: one participant appeared six times because they rejoined.
        var result = ZoomAttendanceMatcher.Compare(
            [Ve("W0MIK")],
            ["Mike W0MIK", "mike  w0mik", "Nate PC", "Nate PC", "NATE PC", "Mike W0MIK"]);

        Assert.Equal(["Mike W0MIK"], Assert.Single(result.Present).ZoomNames);
        Assert.Equal(["Nate PC"], result.NotOnRoster);
    }

    [Fact]
    public void EmptyRosterAndEmptyZoom_IsEmptyResult()
    {
        var result = ZoomAttendanceMatcher.Compare([], []);

        Assert.Empty(result.Present);
        Assert.Empty(result.NotInZoom);
        Assert.Empty(result.NotOnRoster);
    }
}
