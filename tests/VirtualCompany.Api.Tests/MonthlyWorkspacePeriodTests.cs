using VirtualCompany.Application.Cockpit;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MonthlyWorkspacePeriodTests
{
    [Fact]
    public void Resolve_uses_company_timezone_and_previous_calendar_month()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("W. Europe Standard Time");
        var period = MonthlyWorkspacePeriod.Resolve(
            new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc), zone);

        Assert.Equal(2026, period.Year);
        Assert.Equal(4, period.Month);
        Assert.Equal(new DateTime(2026, 3, 31, 22, 0, 0, DateTimeKind.Utc), period.StartUtc);
        Assert.Equal(new DateTime(2026, 4, 30, 22, 0, 0, DateTimeKind.Utc), period.EndUtc);
        Assert.Equal(new DateTime(2026, 2, 28, 23, 0, 0, DateTimeKind.Utc), period.ComparisonStartUtc);
        Assert.Equal(period.StartUtc, period.ComparisonEndUtc);
    }

    [Fact]
    public void Resolve_handles_year_transition_and_explicit_reporting_month()
    {
        var period = MonthlyWorkspacePeriod.Resolve(
            new DateTime(2026, 1, 2, 8, 0, 0, DateTimeKind.Utc), TimeZoneInfo.Utc, 2025, 12);

        Assert.Equal(new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc), period.StartUtc);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), period.EndUtc);
        Assert.Equal(new DateTime(2025, 11, 1, 0, 0, 0, DateTimeKind.Utc), period.ComparisonStartUtc);
    }

    [Fact]
    public void Monthly_priority_ordering_prefers_decisions_close_risk_and_material_change()
    {
        var candidates = new[]
        {
            Candidate("change", change: 1000),
            Candidate("risk", risk: true, change: 1),
            Candidate("close", close: true),
            Candidate("decision", decision: true)
        };

        var result = MonthlyWorkspacePriorityOrdering.Select(candidates);

        Assert.Equal(["decision", "close", "risk", "change"], result.Select(x => x.Key));
    }

    [Fact]
    public void Today_and_monthly_cache_scopes_never_collide()
    {
        var company = Guid.NewGuid(); var user = Guid.NewGuid(); var membership = Guid.NewGuid();
        var today = ExecutiveCockpitCacheKeyBuilder.TodayScope(company, user, membership, "owner", "r1", "company", ["company", "sales"]);
        var monthly = ExecutiveCockpitCacheKeyBuilder.MonthlyScope(company, user, membership, "owner", "r1", "company",
            ["company", "sales"], new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotEqual(today.Identity, monthly.Identity);
        Assert.Null(today.StartUtc);
        Assert.NotNull(monthly.StartUtc);
    }

    private static MonthlyWorkspacePriorityCandidate Candidate(string key, bool decision = false, bool close = false,
        bool risk = false, decimal change = 0) => new(key, key, "company", key, "matters", "Owner", null,
        "Review", DateTime.UtcNow, "test", key, "/work", decision, MaterialChange: change,
        UnresolvedRisk: risk, ComplianceOrCloseDeadline: close);
}
