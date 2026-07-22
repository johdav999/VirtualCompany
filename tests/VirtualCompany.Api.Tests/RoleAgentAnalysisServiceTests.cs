using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Support;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;
using VirtualCompany.Infrastructure.Support;

namespace VirtualCompany.Api.Tests;

public sealed class RoleAgentAnalysisServiceTests
{
    [Fact]
    public async Task Finance_cash_analysis_uses_shared_gateway_and_reports_missing_balances()
    {
        await using var db = CreateDb();
        var gateway = new CapturingGateway();
        var service = new FinanceAgentAnalysisService(db, gateway);

        var result = await service.AnalyzeAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new RoleAgentAnalysisRequest(FinanceAgentAnalysisTypes.CashLiquidity), CancellationToken.None);

        Assert.Equal(AgentCapabilityIds.FinanceCashLiquidity, result.CapabilityId);
        Assert.Contains("Current cash balances", result.MissingEvidence);
        Assert.True(result.RequiresReview);
        Assert.Single(gateway.Requests);
        Assert.Equal("finance_state", gateway.Requests[0].Sources.Single().Type);
        Assert.Equal(["recommend"], gateway.Requests[0].AllowedActionTypes);
        Assert.Empty(gateway.Requests[0].AllowedTools);
    }

    [Fact]
    public async Task Sales_forecast_analysis_never_fabricates_a_snapshot()
    {
        await using var db = CreateDb();
        var gateway = new CapturingGateway();
        var service = new SalesAgentAnalysisService(db, new EmptyKnowledgeSearch(), gateway);

        var result = await service.AnalyzeAsync(Guid.NewGuid(), Guid.NewGuid(), null,
            new RoleAgentAnalysisRequest(SalesAgentAnalysisTypes.ForecastAnalysis), CancellationToken.None);

        Assert.Equal(AgentCapabilityIds.SalesForecastAnalysis, result.CapabilityId);
        Assert.Contains("Revenue forecast snapshot", result.MissingEvidence);
        Assert.Empty(result.Metrics);
        Assert.Equal("sales_state", gateway.Requests.Single().Sources.Single().Type);
    }

    [Fact]
    public async Task Support_triage_analysis_is_review_only_when_no_cases_match()
    {
        await using var db = CreateDb();
        var gateway = new CapturingGateway();
        var service = new SupportAgentAnalysisService(db, new EmptyKnowledgeProvider(), new EmptyAnalyticsService(), gateway);

        var result = await service.AnalyzeAsync(Guid.NewGuid(), Guid.NewGuid(), null,
            new RoleAgentAnalysisRequest(SupportAgentAnalysisTypes.TriageAnalysis), CancellationToken.None);

        Assert.Equal(AgentCapabilityIds.SupportTriageAnalysis, result.CapabilityId);
        Assert.Empty(result.Priorities);
        Assert.Equal("support_state", gateway.Requests.Single().Sources.Single().Type);
        Assert.All(result.NextActions, action => Assert.True(action.RequiresApproval));
    }

    [Fact]
    public void Role_capability_ids_are_unique()
    {
        var values = typeof(AgentCapabilityIds).GetFields()
            .Where(x => x.IsLiteral && x.FieldType == typeof(string))
            .Select(x => (string)x.GetRawConstantValue()!).ToArray();

        Assert.Equal(values.Length, values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static VirtualCompanyDbContext CreateDb() => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class CapturingGateway : IAgentReasoningGateway
    {
        public List<AgentReasoningRequest> Requests { get; } = [];

        public Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new AgentReasoningResult(Guid.NewGuid(), AgentAiRunStatuses.Completed, "1.0.0",
                "Review the authoritative evidence and resolve missing information.", [], .8m, [], [],
                [new AgentAiNextAction("Review evidence", "recommend", null, true)], request.Sources.Select(x => x.Id).ToArray()));
        }

        public Task<AgentReasoningResult?> GetRunAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentReasoningResult?>(null);
    }

    private sealed class EmptyKnowledgeProvider : ISupportKnowledgeContextProvider
    {
        public Task<SupportKnowledgeContext> RetrieveAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken) =>
            Task.FromResult(new SupportKnowledgeContext(supportCaseId, [], [], [], 0m, "No knowledge."));
    }

    private sealed class EmptyKnowledgeSearch : ICompanyKnowledgeSearchService
    {
        public Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchAsync(CompanyKnowledgeSemanticSearchQuery query,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CompanyKnowledgeSearchResultDto>>([]);
    }

    private sealed class EmptyAnalyticsService : ISupportAnalyticsService
    {
        public Task<SupportAnalyticsDashboardResponse> GetDashboardAsync(Guid companyId, CancellationToken cancellationToken) =>
            Task.FromResult(new SupportAnalyticsDashboardResponse(
                new SupportCaseSummaryCounts(0, 0, 0, 0, 0, 0), [], [], [],
                new SupportSlaPerformanceSummary(0, 0, 0, 0, 0, 0, 0, "No data."),
                new SupportLearningEffectivenessSummary(0, 0, 0, 0, null, null, 0, 0, 0, 0, "No data."), []));
    }
}
