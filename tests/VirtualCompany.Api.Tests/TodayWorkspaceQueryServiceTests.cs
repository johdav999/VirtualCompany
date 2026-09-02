using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Focus;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TodayWorkspaceQueryServiceTests
{
    [Fact]
    public async Task Noncritical_contributor_failure_returns_typed_unavailable_section_and_fallback_summary()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var access = new TodayWorkspaceLensAccess("sales", "Sales", "Primary responsibility", true, false,
            membershipId, "Sales Manager", "Alex");
        var resolver = new StubResolver(new TodayWorkspaceLensResolution(
            companyId, userId, membershipId, CompanyMembershipRole.Manager, "Example", "sales", "sales", "r1", [access]));
        var service = new CompanyTodayWorkspaceQueryService(
            resolver,
            [new FailingContributor()],
            new UnusedCockpit(),
            new EmptyFocus(),
            new NoOpCache(),
            new EmptyAgentActivity(),
            new ReadyManualReview(),
            TimeProvider.System,
            NullLogger<CompanyTodayWorkspaceQueryService>.Instance);

        var result = await service.GetAsync(new GetTodayWorkspaceQuery(companyId), CancellationToken.None);

        Assert.True(result.IsPartial);
        Assert.True(result.SituationSummary.IsDeterministicFallback);
        Assert.NotNull(result.Sales);
        Assert.False(result.Sales!.IsAvailable);
        Assert.Contains(result.Diagnostics, x => x.Section == "sales" && x.Code == "contributor_failed");
    }

    private sealed class StubResolver(TodayWorkspaceLensResolution resolution) : ITodayWorkspaceLensResolver
    {
        public Task<TodayWorkspaceLensResolution> ResolveAsync(Guid companyId, string? requestedLens, CancellationToken cancellationToken) =>
            Task.FromResult(resolution);
    }

    private sealed class FailingContributor : ITodayWorkspaceContributor
    {
        public string Lens => TodayWorkspaceLenses.Sales;
        public Task<TodayWorkspaceFeatureContribution> ContributeAsync(TodayWorkspaceContributorContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Sensitive provider detail that must not reach the response.");
    }

    private sealed class EmptyFocus : IFocusEngine
    {
        public Task<IReadOnlyList<FocusItemDto>> GetFocusAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FocusItemDto>>([]);
    }

    private sealed class EmptyAgentActivity : ITodayAgentActivityQueryService
    {
        public Task<IReadOnlyList<TodayWorkspaceAgentUpdateDto>> GetAsync(
            TodayWorkspaceLensResolution resolution, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TodayWorkspaceAgentUpdateDto>>([]);
    }

    private sealed class ReadyManualReview : ICompanyManualReviewService
    {
        public Task<TodayWorkspaceManualReviewDto> GetStatusAsync(Guid companyId, bool canRequest, CancellationToken cancellationToken) =>
            Task.FromResult(new TodayWorkspaceManualReviewDto(canRequest, null, null, null, null, "idle", "Ready.", null));
        public Task<TodayWorkspaceManualReviewDto> RequestAsync(Guid companyId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class UnusedCockpit : IExecutiveCockpitDashboardService
    {
        public Task<ExecutiveCockpitDashboardDto> GetAsync(GetExecutiveCockpitDashboardQuery query, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected for a Sales-only lens.");
        public Task<ExecutiveCockpitWidgetPayloadDto> GetWidgetAsync(GetExecutiveCockpitWidgetPayloadQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ExecutiveCockpitFinanceAlertDetailDto?> GetFinanceAlertDetailAsync(GetExecutiveCockpitFinanceAlertDetailQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpCache : IExecutiveCockpitDashboardCache
    {
        public Task<CachedExecutiveCockpitDashboardDto?> TryGetAsync(Guid companyId, CancellationToken cancellationToken) => Task.FromResult<CachedExecutiveCockpitDashboardDto?>(null);
        public Task<CachedExecutiveCockpitDashboardDto?> TryGetDashboardAsync(ExecutiveCockpitCacheScope scope, CancellationToken cancellationToken) => Task.FromResult<CachedExecutiveCockpitDashboardDto?>(null);
        public Task SetAsync(CachedExecutiveCockpitDashboardDto snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SetDashboardAsync(ExecutiveCockpitCacheScope scope, CachedExecutiveCockpitDashboardDto snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CachedExecutiveCockpitKpiDashboardDto?> TryGetKpiDashboardAsync(ExecutiveCockpitCacheScope scope, CancellationToken cancellationToken) => Task.FromResult<CachedExecutiveCockpitKpiDashboardDto?>(null);
        public Task SetKpiDashboardAsync(ExecutiveCockpitCacheScope scope, CachedExecutiveCockpitKpiDashboardDto snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CachedExecutiveCockpitWidgetDto<TPayload>?> TryGetWidgetAsync<TPayload>(ExecutiveCockpitCacheScope scope, CancellationToken cancellationToken) => Task.FromResult<CachedExecutiveCockpitWidgetDto<TPayload>?>(null);
        public Task SetWidgetAsync<TPayload>(ExecutiveCockpitCacheScope scope, CachedExecutiveCockpitWidgetDto<TPayload> snapshot, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(Guid companyId, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task InvalidateAsync(ExecutiveCockpitCacheInvalidationEvent invalidationEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
