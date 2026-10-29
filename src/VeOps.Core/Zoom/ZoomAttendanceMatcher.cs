namespace VeOps.Core.Zoom;

/// <summary>One VE on a session's roster, reduced to what an attendance comparison can use.</summary>
public sealed record AttendanceRosterEntry(string? CallSign, string Name);

/// <summary>A roster VE and the Zoom display name(s) that carried their call sign.</summary>
public sealed record AttendancePresent(AttendanceRosterEntry Ve, IReadOnlyList<string> ZoomNames);

/// <param name="Present">Roster VEs whose call sign appeared in a Zoom name.</param>
/// <param name="NotInZoom">Roster VEs with no matching Zoom name — absent, or joined under a name without their call sign.</param>
/// <param name="NotOnRoster">Zoom names matching no roster VE — candidates, observers, or a VE whose name lacks a call sign.</param>
public sealed record AttendanceComparison(
    IReadOnlyList<AttendancePresent> Present,
    IReadOnlyList<AttendanceRosterEntry> NotInZoom,
    IReadOnlyList<string> NotOnRoster);

/// <summary>
/// The call-sign hint behind the VE attendance audit (#576). It produces hints for a person who is
/// comparing the two lists by eye, never a verdict: nothing here blocks or records anything.
///
/// <para>A call sign matches when it appears in a Zoom name, ignoring case, <b>and is not touching another
/// letter or digit</b> — so <c>W0MIK</c> is not found inside <c>KW0MIKE</c> or <c>W0MIK1</c>. A VE with no
/// call sign can never match; an empty string is a substring of every name, so it is excluded explicitly.</para>
/// </summary>
public static class ZoomAttendanceMatcher
{
    public static AttendanceComparison Compare(IEnumerable<AttendanceRosterEntry> roster, IEnumerable<string> zoomNames)
    {
        // A rejoin is a new row in Zoom's report (one real name appeared six times), so names are listed
        // once, keyed ignoring case and runs of spaces ("Grant  NP2U"), keeping the first spelling seen.
        var names = zoomNames
            .Select(n => n.Trim())
            .Where(n => n.Length > 0)
            .DistinctBy(n => string.Join(' ', n.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant())
            .ToList();
        var consumed = new HashSet<int>();
        var present = new List<AttendancePresent>();
        var notInZoom = new List<AttendanceRosterEntry>();

        foreach (var ve in roster)
        {
            var callSign = ve.CallSign?.Trim();
            var hits = string.IsNullOrEmpty(callSign)
                ? []
                : Enumerable.Range(0, names.Count)
                    .Where(i => !consumed.Contains(i) && ContainsCallSign(names[i], callSign))
                    .ToList();

            if (hits.Count == 0)
            {
                notInZoom.Add(ve);
                continue;
            }

            consumed.UnionWith(hits);
            present.Add(new AttendancePresent(ve, [.. hits.Select(i => names[i])]));
        }

        var notOnRoster = Enumerable.Range(0, names.Count).Where(i => !consumed.Contains(i)).Select(i => names[i]).ToList();
        return new AttendanceComparison(present, notInZoom, notOnRoster);
    }

    private static bool ContainsCallSign(string name, string callSign)
    {
        var from = 0;
        while (from <= name.Length - callSign.Length)
        {
            var at = name.IndexOf(callSign, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return false;

            var before = at == 0 || !char.IsLetterOrDigit(name[at - 1]);
            var afterIndex = at + callSign.Length;
            var after = afterIndex >= name.Length || !char.IsLetterOrDigit(name[afterIndex]);
            if (before && after) return true;

            from = at + 1;
        }

        return false;
    }
}
