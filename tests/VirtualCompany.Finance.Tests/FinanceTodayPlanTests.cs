using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Finance.Tests;
public sealed class FinanceTodayPlanTests
{
    private static readonly Guid Company = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    private static TodayWorkspaceContributorContext Context()
    {
        var finance = new ExecutiveCockpitFinanceDto(new(12000m, "SEK", "12,000 SEK", "unknown", null, "", Now.AddHours(-1), "/finance"),
            new(30, 1, "30 days", default, "", "/finance"), null, new("attention", "", "", 1, 0, 1, Now), [], [], [], []);
        var cockpit = new ExecutiveCockpitDashboardDto(Company, "North", Now, [], null, [], null, null, finance,
            new(0, [], "/approvals"), [], [], [], [], new(false, false, false, 0, 0, 0, true), new(true, true, true, true, true, true));
        return new(Company, Now, "company", new("finance", "Finance", "Oversight", false, true, null, "Treasurer", null), cockpit);
    }
    private static FinanceBudgetDto Budget(string version, decimal amount = 100) => new(Guid.NewGuid(), Company,
        Guid.NewGuid(), "3000", "Revenue", Now.Date, version, null, amount, "SEK", Now, Now);
    private static (FinanceTodayWorkspaceContributor, ReadProxy) Contributor()
    {
        var read = DispatchProxy.Create<IFinanceReadService, ReadProxy>(); var proxy = (ReadProxy)(object)read;
        return (new(read, NullLogger<FinanceTodayWorkspaceContributor>.Instance), proxy);
    }
    [Fact] public async Task Absent_budget_is_a_gap_not_zero_or_favorable_variance()
    {
        var (contributor, proxy) = Contributor();
        var result = await contributor.ContributeAsync(Context(), default);
        var plan = result.Finance!.PlanComparison!;
        Assert.Equal("no_baseline", plan.State); Assert.Null(plan.RecordedComparison); Assert.Null(plan.SourceUpdatedUtc);
        Assert.Equal(new DateTime(2026,10,1,0,0,0,DateTimeKind.Utc), plan.PeriodStartUtc);
        Assert.Null(proxy.Query); Assert.Equal(12000m, result.Finance.CashBalance);
    }
    [Fact] public async Task One_recorded_version_uses_authoritative_variance_and_never_claims_approval()
    {
        var (contributor, proxy) = Contributor(); proxy.Budgets = [Budget("v2")];
        proxy.Variance = new(Company, "actual_vs_budget", Now.Date, Now.Date, "v2", false,
            [new(Now.Date, Guid.NewGuid(), "3000", "Revenue", "income", "Income", null, null, null, 80, 100, -20, -20, "SEK")]);
        var result = await contributor.ContributeAsync(Context(), default); var plan = result.Finance!.PlanComparison!;
        Assert.Equal("recorded_unapproved", plan.State); Assert.Same(proxy.Variance, plan.RecordedComparison);
        Assert.Equal("v2", proxy.Query!.Version); Assert.Equal(Company, proxy.Query.CompanyId);
        Assert.Equal(FinanceVarianceComparisonTypes.ActualVsBudget, proxy.Query.ComparisonType);
        Assert.Equal(-20m, Assert.Single(plan.RecordedComparison!.Rows).VarianceAmount);
    }
    [Fact] public async Task Multiple_versions_are_not_combined_into_an_arbitrary_baseline()
    {
        var (contributor, proxy) = Contributor(); proxy.Budgets = [Budget("v1"), Budget("v2")];
        var result = await contributor.ContributeAsync(Context(), default);
        Assert.Equal("selection_required", result.Finance!.PlanComparison!.State); Assert.Null(proxy.Query);
        Assert.Null(result.Finance.PlanComparison.RecordedComparison);
    }
    [Fact] public async Task Failed_planning_keeps_cash_and_logs_only_error_type()
    {
        var read = DispatchProxy.Create<IFinanceReadService, ReadProxy>(); ((ReadProxy)(object)read).Fail = true;
        var logger = new CaptureLogger();
        var result = await new FinanceTodayWorkspaceContributor(read, logger).ContributeAsync(Context(), default);
        Assert.Equal("unavailable", result.Finance!.PlanComparison!.State); Assert.Equal(12000m, result.Finance.CashBalance);
        Assert.Contains("InvalidOperationException", logger.Message); Assert.DoesNotContain("CONFIDENTIAL", logger.Message); Assert.Null(logger.Exception);
    }
    [Fact] public async Task Revoked_finance_access_is_not_downgraded_to_a_planning_gap()
    {
        var (contributor, proxy) = Contributor(); proxy.Denied = true;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => contributor.ContributeAsync(Context(), default));
    }
    public class ReadProxy : DispatchProxy
    {
        public IReadOnlyList<FinanceBudgetDto> Budgets = []; public FinanceVarianceResultDto? Variance;
        public GetFinanceVarianceQuery? Query; public bool Fail, Denied;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (Denied) throw new UnauthorizedAccessException("CONFIDENTIAL");
            if (Fail) throw new InvalidOperationException("CONFIDENTIAL provider failure");
            if (method!.Name == nameof(IFinanceReadService.GetBudgetsAsync)) return Task.FromResult(Budgets);
            if (method.Name == nameof(IFinanceReadService.GetVarianceAsync)) { Query = (GetFinanceVarianceQuery)args![0]!; return Task.FromResult(Variance!); }
            throw new NotSupportedException(method.Name);
        }
    }
    private sealed class CaptureLogger : ILogger<FinanceTodayWorkspaceContributor>
    {
        public string Message = ""; public Exception? Exception;
        public IDisposable? BeginScope<T>(T state) where T:notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<T>(LogLevel level, EventId id, T state, Exception? ex, Func<T,Exception?,string> formatter) { Message = formatter(state,ex); Exception=ex; }
    }
}
