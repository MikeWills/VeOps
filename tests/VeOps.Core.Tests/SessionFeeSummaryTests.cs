using VeOps.Core.Entities;
using Xunit;

namespace VeOps.Core.Tests;

/// <summary>Pure in-memory tests for Session.GetFeeSummary — no DB needed, same style as FeeConfigurationTests.</summary>
public class SessionFeeSummaryTests
{
    private static Session BuildSession(decimal retainedAmount, decimal? remitToVecOverride, params decimal[] paidAmounts)
    {
        var feeConfiguration = new FeeConfiguration { RetainedAmount = retainedAmount };
        var session = new Session
        {
            ExamToolsSessionId = "session-1",
            Title = "Test Session",
            FeeConfiguration = feeConfiguration,
            RemitToVecOverride = remitToVecOverride
        };

        var candidate = new Candidate { Name = "Test Candidate", DateRegisteredUtc = DateTime.UtcNow, Session = session };
        foreach (var amount in paidAmounts)
        {
            candidate.Payments.Add(new Payment { Amount = amount, Status = PaymentStatus.Paid });
        }

        session.Candidates.Add(candidate);
        return session;
    }

    [Fact]
    public void NoOverride_SumsPerCandidateDefaultAcrossAllPaidPayments()
    {
        // Two candidates, standard $15 fee, $7 retained cap each — matches the real per-candidate default.
        var session = BuildSession(retainedAmount: 7m, remitToVecOverride: null, paidAmounts: [15m, 15m]);

        var summary = session.GetFeeSummary();

        Assert.Equal(30m, summary.TotalCollected);
        Assert.Equal(14m, summary.TotalRetained); // $7 + $7
        Assert.Equal(16m, summary.TotalRemitToVec); // $8 + $8
    }

    [Fact]
    public void NoOverride_YouthFeeUnderRetainedCap_KeepsWholeFeeAcrossTheSum()
    {
        var session = BuildSession(retainedAmount: 7m, remitToVecOverride: null, paidAmounts: [15m, 5m]); // regular + youth

        var summary = session.GetFeeSummary();

        Assert.Equal(20m, summary.TotalCollected);
        Assert.Equal(12m, summary.TotalRetained); // $7 regular + $5 youth (clamped, not $7)
        Assert.Equal(8m, summary.TotalRemitToVec); // $8 regular + $0 youth
    }

    /// <summary>
    /// The override states what the VEC is owed. Everything left of what was collected is retained —
    /// the reverse of how this worked until 2026-09-10.
    ///
    /// <para>Mike: <i>"I read that button as adjusting what we send to the VEC, not the other way
    /// around. To me, that makes more sense."</i> The remit is the number with an external
    /// consequence — it is what gets typed into ARRL's form and what money actually moves — and it
    /// is the number an operator independently knows. Nobody arrives knowing what their team is
    /// keeping.</para>
    /// </summary>
    [Fact]
    public void WithOverride_StatesWhatTheVecIsOwed_AndRetainedIsTheRemainder()
    {
        // 50 candidates at $15. The team owes the VEC a flat $730 for the session rather than a
        // figure computed across every payment.
        var paidAmounts = Enumerable.Repeat(15m, 50).ToArray();
        var session = BuildSession(retainedAmount: 7m, remitToVecOverride: 730m, paidAmounts: paidAmounts);

        var summary = session.GetFeeSummary();

        Assert.Equal(750m, summary.TotalCollected);
        Assert.Equal(730m, summary.TotalRemitToVec);
        Assert.Equal(20m, summary.TotalRetained);
    }

    /// <summary>
    /// ⚠️ <b>The case the change exists for.</b> A session whose payments the app never saw still
    /// owes the VEC — two candidates at $15 with $7 retained is $16 owed. Overriding now states
    /// that directly, so the ARRL form prefills it.
    ///
    /// <para>Under the old meaning this was unreachable: the override was subtracted from what had
    /// been collected, so <c>max(0, 0 − 16)</c> was zero and every figure on the page stayed
    /// <c>$0.00</c> no matter what was typed. Mike hand-typed 16.00 into ARRL's form to get a real
    /// filing through (#544).</para>
    /// </summary>
    [Fact]
    public void WithOverride_AndNothingCollected_StillOwesTheVec()
    {
        var session = BuildSession(retainedAmount: 7m, remitToVecOverride: 16m);

        var summary = session.GetFeeSummary();

        Assert.Equal(0m, summary.TotalCollected);
        Assert.Equal(16m, summary.TotalRemitToVec);
        Assert.Equal(0m, summary.TotalRetained); // nothing was collected, so nothing can be kept
    }

    /// <summary>
    /// Retained is clamped at zero rather than going negative when the VEC is owed more than was
    /// collected — which is the normal state of the case above, not an error.
    /// </summary>
    [Fact]
    public void WithOverride_ExceedingWhatWasCollected_ClampsRetainedToZero()
    {
        var session = BuildSession(retainedAmount: 7m, remitToVecOverride: 100m, paidAmounts: [15m, 15m]);

        var summary = session.GetFeeSummary();

        Assert.Equal(30m, summary.TotalCollected);
        Assert.Equal(100m, summary.TotalRemitToVec); // stated, not derived -- it is what is owed
        Assert.Equal(0m, summary.TotalRetained);
    }

    [Fact]
    public void UnpaidPayments_AreExcludedFromTotals()
    {
        var session = BuildSession(retainedAmount: 7m, remitToVecOverride: null, paidAmounts: [15m]);
        session.Candidates[0].Payments.Add(new Payment { Amount = 15m, Status = PaymentStatus.Unpaid });

        var summary = session.GetFeeSummary();

        Assert.Equal(15m, summary.TotalCollected); // the Unpaid payment doesn't count
    }
}
