using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Focus;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class TodayWorkspaceQueryServiceTests
{
    [Fact]
    public async Task Missing_source_timestamp_is_unknown_and_never_replaced_by_read_time()
    {
        var id = Guid.NewGuid(); var membership = Guid.NewGuid();
        var access = new TodayWorkspaceLensAccess("sales", "Sales", "Primary", true, false, membership, "Owner", "Alex");
        var service = new CompanyTodayWorkspaceQueryService(new StubResolver(new(id, Guid.NewGuid(), membership,
            CompanyMembershipRole.Manager, "Example", "sales", "sales", "r1", [access])),
            [new FailingContributor()], new UnusedCockpit(), new TimestampMissingFocus(), new NoOpCache(), new EmptyAgentActivity(),
            new ReadyManualReview(), TimeProvider.System, NullLogger<CompanyTodayWorkspaceQueryService>.Instance);
        var workspace = await service.GetAsync(new(id, "sales", true), CancellationToken.None);
        var priority = Assert.Single(workspace.Priorities);
        Assert.Equal(DateTime.MinValue, priority.ObservedAtUtc);
        Assert.Equal("unknown", priority.Freshness);
        Assert.True(workspace.IsPartial);
    }

    [Theory]
    [InlineData(15, false, "fresh")]
    [InlineData(360, false, "current")]
    [InlineData(361, true, "fresh")]
    [InlineData(480, true, "fresh")]
    [InlineData(1440, true, "fresh")]
    [InlineData(1441, true, "fresh")]
    [InlineData(-1, true, "fresh")]
    [InlineData(null, true, "fresh")]
    public async Task Company_refresh_uses_current_priorities_when_saved_briefing_is_stale_or_undated(
        int? ageMinutes, bool fallback, string freshness)
    {
        var clock = new ReadClock();
        var now = clock.Current.UtcDateTime;
        var company = Guid.NewGuid(); var membership = Guid.NewGuid();
        var access = new TodayWorkspaceLensAccess("company", "Company", "Oversight", false, true, membership, "Owner", null);
        var briefing = new ExecutiveCockpitDailyBriefingDto(Guid.NewGuid(), "Saved briefing", "Saved daily copy",
            ageMinutes is int minutes ? now.AddMinutes(-minutes) : DateTime.MinValue, null);
        var service = new CompanyTodayWorkspaceQueryService(
            new StubResolver(new(company, Guid.NewGuid(), membership, CompanyMembershipRole.Owner,
                "North", "company", "company", "r1", [access])), [], new BriefingCockpit(briefing),
            new OldPriorityFocus(now.AddDays(-2)), new NoOpCache(), new EmptyAgentActivity(), new ReadyManualReview(),
            clock, NullLogger<CompanyTodayWorkspaceQueryService>.Instance);
        var result = await service.GetAsync(new(company, "company", true), default);
        Assert.Equal(fallback, result.SituationSummary.IsDeterministicFallback);
        Assert.Equal(freshness, result.SituationSummary.Freshness);
        Assert.Equal(fallback ? now : briefing.GeneratedUtc, result.SituationSummary.AsOfUtc);
        Assert.Equal(fallback ? "Review recorded task" : briefing.Title, result.SituationSummary.Headline);
        Assert.Equal(fallback ? "One priority currently needs your attention." : briefing.Summary, result.SituationSummary.Summary);
        var priority = Assert.Single(result.Priorities);
        Assert.Equal(now.AddDays(-2), priority.ObservedAtUtc);
        Assert.Equal("stale", priority.Freshness);
        Assert.False(result.IsPartial);
    }

    private sealed class OldPriorityFocus(DateTime observed) : IFocusEngine
    {
        public Task<IReadOnlyList<FocusItemDto>> GetFocusAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FocusItemDto>>([
                new("task", "Review recorded task", "Check recorded evidence", "open", 10, "/tasks", "task", ObservedAtUtc: observed)]);
    }

    private sealed class BriefingCockpit(ExecutiveCockpitDailyBriefingDto briefing) : IExecutiveCockpitDashboardService
    {
        public Task<ExecutiveCockpitDashboardDto> GetAsync(GetExecutiveCockpitDashboardQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new ExecutiveCockpitDashboardDto(query.CompanyId, "North", briefing.GeneratedUtc, [], null, [], briefing,
                null, null, new(0, [], "/approvals"), [], [], [], [], new(false, false, false, 0, 0, 0, true),
                new(true, true, true, true, true, true)));
        public Task<ExecutiveCockpitWidgetPayloadDto> GetWidgetAsync(GetExecutiveCockpitWidgetPayloadQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ExecutiveCockpitFinanceAlertDetailDto?> GetFinanceAlertDetailAsync(GetExecutiveCockpitFinanceAlertDetailQuery query, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TimestampMissingFocus : IFocusEngine
    {
        public Task<IReadOnlyList<FocusItemDto>> GetFocusAsync(GetDashboardFocusQuery query, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FocusItemDto>>([new("task", "Review task", "Review its evidence", "open", 10, "/tasks", "task")]);
    }
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

    [Fact]
    public async Task Company_decisions_suppress_task_approval_duplicates_and_projection_logs_exclude_sensitive_content()
    {
        var id=Guid.NewGuid();var membership=Guid.NewGuid();var task=Guid.NewGuid();var approval=Guid.NewGuid();
        var access=new TodayWorkspaceLensAccess("company","Company","Oversight",false,true,membership,"Owner",null);
        var sales=access with {Lens="sales",Label="Sales"};var logger=new SafeLogger();
        var service=new CompanyTodayWorkspaceQueryService(new StubResolver(new(id,Guid.NewGuid(),membership,CompanyMembershipRole.Owner,"North","company","company","r1",[access,sales])),
            [new FailingContributor()],new UnusedCockpit(),new DuplicateFocus(task,approval),new NoOpCache(),new EmptyAgentActivity(),new ReadyManualReview(),TimeProvider.System,logger);
        var result=await service.GetAsync(new(id,"company",true),default);
        Assert.Single(result.Decisions);Assert.Equal(approval,result.Decisions[0].RelatedApprovalId);
        Assert.Single(result.Priorities);Assert.Equal(task,result.Priorities[0].RelatedTaskId);
        Assert.DoesNotContain("Sensitive provider detail",string.Join(" ",logger.Messages));Assert.All(logger.Exceptions,x=>Assert.Null(x));
        Assert.Contains(result.Departments!,x=>x.Lens=="sales" && !x.IsAvailable);
    }
    private sealed class DuplicateFocus(Guid task,Guid approval):IFocusEngine
    {
        public Task<IReadOnlyList<FocusItemDto>> GetFocusAsync(GetDashboardFocusQuery query,CancellationToken token) =>
            Task.FromResult<IReadOnlyList<FocusItemDto>>([
                new("task","Review work","Review recorded proposal","review",100,"/work","task",RelatedTaskId:task),
                new("approval","Review decision","Decide on proposal","review",90,"/work","approval",RelatedTaskId:task,RelatedApprovalId:approval)]);
    }
    private sealed class SafeLogger:ILogger<CompanyTodayWorkspaceQueryService>
    {
        public List<string> Messages=[];public List<Exception?> Exceptions=[];
        public IDisposable? BeginScope<T>(T state) where T:notnull=>null;public bool IsEnabled(LogLevel level)=>true;
        public void Log<T>(LogLevel level,EventId id,T state,Exception? ex,Func<T,Exception?,string> format){Messages.Add(format(state,ex));Exceptions.Add(ex);}
    }

    [Fact]
    public async Task Snapshot_time_follows_department_reads_so_new_observations_are_not_in_the_future()
    {
        var id=Guid.NewGuid();var membership=Guid.NewGuid();var clock=new ReadClock();
        var access=new TodayWorkspaceLensAccess("company","Company","Oversight",false,true,membership,"Owner",null);
        var service=new CompanyTodayWorkspaceQueryService(new StubResolver(new(id,Guid.NewGuid(),membership,CompanyMembershipRole.Owner,"North","company","company","r1",[access,access with {Lens="sales"}])),
            [new ObservedContributor(clock)],new UnusedCockpit(),new EmptyFocus(),new NoOpCache(),new EmptyAgentActivity(),new ReadyManualReview(),clock,NullLogger<CompanyTodayWorkspaceQueryService>.Instance);
        var result=await service.GetAsync(new(id,"company",true),default);
        Assert.NotNull(result.Sales);Assert.Equal(clock.Current.UtcDateTime,result.GeneratedAtUtc);Assert.True(result.Sales!.ObservedAtUtc<=result.GeneratedAtUtc);
    }
    private sealed class ReadClock:TimeProvider
    {
        public DateTimeOffset Current=new(2026,10,1,12,0,0,TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow()=>Current;
    }
    private sealed class ObservedContributor(ReadClock clock):ITodayWorkspaceContributor
    {
        public string Lens=>"sales";
        public Task<TodayWorkspaceFeatureContribution> ContributeAsync(TodayWorkspaceContributorContext context,CancellationToken token)
        { clock.Current=clock.Current.AddSeconds(2);return Task.FromResult(new TodayWorkspaceFeatureContribution("sales",[],[],[],Sales:new(true,"Recorded",clock.Current.UtcDateTime,12000,"SEK",0,0,0,3000,[],"/app/sales"))); }
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
