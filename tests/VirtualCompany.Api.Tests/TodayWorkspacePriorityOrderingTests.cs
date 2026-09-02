using VirtualCompany.Application.Cockpit;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TodayWorkspacePriorityOrderingTests
{
    private static readonly DateTime Now = new(2026, 9, 2, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Decision_required_precedes_deadline_and_impact()
    {
        var urgent = Candidate("urgent", due: Now.AddMinutes(-1), impact: 10_000);
        var decision = Candidate("decision", decision: true, impact: 1);

        var result = TodayWorkspacePriorityOrdering.Select([urgent, decision], Now);

        Assert.Equal("decision", result[0].Key);
        Assert.Equal("urgent", result[1].Key);
    }

    [Fact]
    public void Selection_deduplicates_underlying_records_limits_to_five_and_is_stable()
    {
        var candidates = new[]
        {
            Candidate("approval-copy", dedup: "task:1", decision: true),
            Candidate("task-copy", dedup: "task:1", impact: 100),
            Candidate("b", impact: 90), Candidate("c", impact: 80), Candidate("d", impact: 70),
            Candidate("e", impact: 60), Candidate("f", impact: 50), Candidate("g", impact: 40)
        };

        var first = TodayWorkspacePriorityOrdering.Select(candidates, Now);
        var second = TodayWorkspacePriorityOrdering.Select(candidates.Reverse(), Now);

        Assert.Equal(5, first.Count);
        Assert.Single(first.Where(x => x.DeduplicationKey == "task:1"));
        Assert.Equal(first.Select(x => x.Key), second.Select(x => x.Key));
    }

    [Fact]
    public void Lexicographic_ranking_uses_proximity_impact_ownership_blocking_severity_and_freshness()
    {
        var candidates = new[]
        {
            Candidate("low-impact", due: Now.AddHours(2), impact: 1),
            Candidate("high-impact", due: Now.AddHours(2), impact: 2),
            Candidate("later", due: Now.AddDays(2), impact: 1_000),
            Candidate("no-deadline", impact: 10_000)
        };

        var result = TodayWorkspacePriorityOrdering.Select(candidates, Now);

        Assert.Equal(["high-impact", "low-impact", "later", "no-deadline"], result.Select(x => x.Key));
    }

    private static TodayWorkspacePriorityCandidate Candidate(
        string key,
        string? dedup = null,
        bool decision = false,
        DateTime? due = null,
        decimal impact = 0) => new(
        key,
        dedup ?? key,
        TodayWorkspaceLenses.Company,
        $"{key} happened",
        $"{key} matters",
        "Owner",
        null,
        "Review it",
        Now,
        "test",
        key,
        $"/work?itemId={key}",
        decision,
        due,
        Impact: impact);
}
