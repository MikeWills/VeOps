using System.Text.RegularExpressions;
using Xunit;

namespace VeOps.Web.Tests;

/// <summary>
/// <c>EasternTimeFormatter.Format</c> already appends <c>" ET"</c> to everything it returns — that
/// is the whole point of routing every displayed instant through it, so a time can never be
/// mistaken for UTC or the reader's own local time. Writing another <c>ET</c> after the call
/// therefore renders <c>"10:37 PM ET ET."</c>
///
/// <para>Reported by Heather (KM6Z) from a phone, on the ARRL filing page: <i>"Not sure why there
/// are two ETs."</i> It had been live since the page was built.</para>
///
/// <para><b>Why a source scan.</b> Nothing about this fails, throws, or looks wrong to the author:
/// the suffix lives inside the formatter, the prose lives in a Razor file, and the two are only
/// ever seen together by whoever loads the page. There were 43 call sites and exactly one had the
/// duplicate, so reading them by hand once fixes today and guards nothing. Same shape as
/// <c>FormBindingTests</c> and <c>NoNulBytesInSourceTests</c>: the mistake is one stray word, and
/// the compiler has no opinion about it.</para>
/// </summary>
public class EasternTimeSuffixTests
{
    /// <summary>
    /// A <c>Format(...)</c> call followed by a literal ET before any tag or sentence break.
    ///
    /// <para>The window is deliberately short and stops at <c>&lt;</c>: an unbounded search would
    /// match an unrelated "ET" later in the same paragraph, and a test that cries wolf gets
    /// suppressed rather than read. Word-bounded so "ExamTools", "GET" and "SET" do not match.</para>
    /// </summary>
    private static readonly Regex DoubledSuffix = new(
        @"EasternTimeFormatter\.Format\([^)]*\)[^<\n]{0,15}\bET\b",
        RegexOptions.Compiled);

    [Fact]
    public void NoPageAddsItsOwnEtSuffixAfterTheFormatter()
    {
        var root = Path.Combine(FormBindingTests.RepositoryRootPath(), "src", "VeOps.Web");

        var offenders = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)
                           || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            // bin/obj carry generated copies of the Razor files, which would report every hit twice.
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                           && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (path, number: index + 1, line))
                .Where(entry => DoubledSuffix.IsMatch(entry.line)))
            .Select(entry => $"{Path.GetRelativePath(root, entry.path)}:{entry.number}: {entry.line.Trim()}")
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "EasternTimeFormatter.Format already ends in \" ET\". Remove the extra one:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Guards the premise. If the suffix ever moves out of the formatter, the test above starts
    /// passing for the wrong reason and every page silently loses its timezone label — the failure
    /// the formatter exists to prevent.
    /// </summary>
    [Fact]
    public void TheFormatterIsStillWhatAppliesTheSuffix()
    {
        var formatted = EasternTimeFormatter.Format(new DateTime(2026, 9, 10, 2, 37, 0, DateTimeKind.Utc), "MMM d, yyyy h:mm tt");

        Assert.EndsWith(" ET", formatted, StringComparison.Ordinal);
        Assert.DoesNotContain("ET ET", formatted, StringComparison.Ordinal);
    }
}
