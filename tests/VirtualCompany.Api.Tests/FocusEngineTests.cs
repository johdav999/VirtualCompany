using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Focus;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FocusEngineTests
{
    [Fact]
    public async Task Priority_workspace_receives_all_authorized_candidates_before_deadline_ranking()
    {
        var company = Guid.NewGuid(); var user = Guid.NewGuid();
        var candidates = Enumerable.Range(1, 7).Select(x => Candidate($"approval{x}", FocusSourceType.Approval, 100 - x)).Append(
            Candidate("old-overdue", FocusSourceType.Task, 1) with { DueUtc = DateTime.UtcNow.AddDays(-3), ObservedAtUtc = DateTime.UtcNow.AddDays(-4) }).ToArray();
        var engine = CreateEngine(company, user, new StubSource(candidates));
        var items = await engine.GetFocusAsync(new(company, user, ForPriorityWorkspace: true), CancellationToken.None);
        Assert.Equal(8, items.Count);
        var overdue = Assert.Single(items, x => x.Id == "old-overdue");
        Assert.Equal(candidates.Last().ObservedAtUtc, overdue.ObservedAtUtc);
        Assert.Equal(candidates.Last().DueUtc, overdue.DueUtc);
        var ordinary = await engine.GetFocusAsync(new(company, user), CancellationToken.None);
        Assert.Equal(5, ordinary.Count);
    }
    [Fact]
    public async Task Engine_normalizes_scores_orders_descending_and_caps_to_five()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var engine = CreateEngine(
            companyId,
            userId,
            new StubSource(
            [
                Candidate("approval-1", FocusSourceType.Approval, 99),
                Candidate("task-1", FocusSourceType.Task, 88),
                Candidate("anomaly-1", FocusSourceType.Anomaly, 77),
                Candidate("finance-1", FocusSourceType.FinanceAlert, 66),
                Candidate("task-2", FocusSourceType.Task, 55),
                Candidate("task-3", FocusSourceType.Task, 44)
            ]));

        var items = await engine.GetFocusAsync(new GetDashboardFocusQuery(companyId, userId), CancellationToken.None);

        Assert.Equal(5, items.Count);
        Assert.Equal(items.OrderByDescending(item => item.PriorityScore).Select(item => item.Id), items.Select(item => item.Id));
        Assert.All(items, item => Assert.InRange(item.PriorityScore, 0, 100));
        Assert.Contains(items, item => item.SourceType == "approval");
        Assert.Contains(items, item => item.SourceType == "task");
        Assert.Contains(items, item => item.SourceType == "anomaly");
        Assert.Contains(items, item => item.SourceType == "finance_alert");
    }

    [Fact]
    public async Task Engine_preserves_deterministic_order_for_equal_scores()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var engine = CreateEngine(
            companyId,
            userId,
            new StubSource(
            [
                Candidate("alpha", FocusSourceType.Task, 10, stableSortKey: "a"),
                Candidate("bravo", FocusSourceType.Task, 10, stableSortKey: "b"),
                Candidate("charlie", FocusSourceType.Task, 10, stableSortKey: "c")
            ]));

        var items = await engine.GetFocusAsync(new GetDashboardFocusQuery(companyId, userId), CancellationToken.None);

        Assert.Equal(["alpha", "bravo", "charlie"], items.Select(item => item.Id).ToArray());
        Assert.All(items, item => Assert.Equal(100, item.PriorityScore));
    }

    [Fact]
    public async Task Engine_returns_empty_when_no_candidates_exist()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var engine = CreateEngine(companyId, userId, new StubSource([]));

        var items = await engine.GetFocusAsync(new GetDashboardFocusQuery(companyId, userId), CancellationToken.None);

        Assert.Empty(items);
    }

    private static CompanyFocusEngine CreateEngine(Guid companyId, Guid userId, params IFocusCandidateSource[] sources) =>
        new(
            sources,
            new StubMembershipResolver(companyId, userId));

    private static FocusCandidate Candidate(
        string id,
        FocusSourceType sourceType,
        double rawScore,
        string? stableSortKey = null) =>
        new(
            id,
            $"Title {id}",
            $"Description {id}",
            "open",
            $"/work/{id}",
            sourceType,
            rawScore,
            new DateTime(2026, 4, 19, 12, 0, 0, DateTimeKind.Utc),
            stableSortKey ?? id);

    private sealed class StubSource : IFocusCandidateSource
    {
        private readonly IReadOnlyList<FocusCandidate> _candidates;

        public StubSource(IReadOnlyList<FocusCandidate> candidates)
        {
            _candidates = candidates;
        }

        public Task<IReadOnlyList<FocusCandidate>> GetCandidatesAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(_candidates);
    }

    private sealed class StubMembershipResolver : ICompanyMembershipContextResolver
    {
        private readonly ResolvedCompanyMembershipContext _membership;

        public StubMembershipResolver(Guid companyId, Guid userId)
        {
            _membership = new ResolvedCompanyMembershipContext(
                Guid.NewGuid(),
                companyId,
                userId,
                "Focus Company",
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active);
        }

        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedCompanyMembershipContext?>(_membership);

        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult<ResolvedCompanyMembershipContext?>(_membership.CompanyId == companyId ? _membership : null);
    }
}
