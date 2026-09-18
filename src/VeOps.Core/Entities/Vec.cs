namespace VeOps.Core.Entities;

public class Vec
{
    public int Id { get; set; }

    /// <summary>The human-readable name shown throughout the UI, e.g. "ARRL", "GLAARG".</summary>
    public required string Name { get; set; }

    /// <summary>
    /// ExamTools' own per-session <c>vec</c> code, when it differs from <see cref="Name"/> — GLAARG
    /// reports "lagroup", for instance. Null means "the code is the same as the name" (ARRL reports
    /// "arrl"), which is the common case and keeps every pre-existing row working untouched.
    /// Never match ingestion against <see cref="Name"/> directly — use <see cref="MatchCode"/>.
    /// </summary>
    public string? ExamToolsCode { get; set; }

    /// <summary>
    /// The value <see cref="Ingestion.SessionIngestionService"/> matches ExamTools' session <c>vec</c>
    /// field against, case-insensitively. In-memory convenience only — an EF Core query must spell
    /// out the same coalesce itself so it translates to SQL.
    /// </summary>
    public string MatchCode => ExamToolsCode ?? Name;

    /// <summary>ARRL's youth discount/FCC-fee-reimbursement scholarship program.</summary>
    public bool SupportsYouthProgram { get; set; }

    public string? Notes { get; set; }

    /// <summary>
    /// How many business days this VEC takes to file a session with the FCC — ARRL publishes 1-3, so
    /// the default is the outer figure. Backs <c>{{FccNoticeExpectedBy}}</c> on the "When a candidate
    /// passes" trigger: the FCC's fee notice cannot arrive before the VEC files, and this is the only
    /// date the app can promise a candidate on the night. A real default rather than null, like
    /// <c>Team.PurgeUnpaidLinkDays</c>: it is a business setting, not something unset until entered.
    /// </summary>
    public int FccProcessingBusinessDays { get; set; } = 3;


    public List<FeeConfiguration> FeeConfigurations { get; } = [];
    public List<Session> Sessions { get; } = [];
}
