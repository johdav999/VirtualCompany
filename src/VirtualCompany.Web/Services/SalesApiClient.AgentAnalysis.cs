namespace VirtualCompany.Web.Services;

public sealed partial class SalesApiClient
{
    public Task<SalesIntelligenceBriefResultViewModel> BuildIntelligenceBriefAsync(Guid companyId, Guid agentId,
        SalesIntelligenceBriefRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesIntelligenceBriefRequestViewModel, SalesIntelligenceBriefResultViewModel>(companyId, HttpMethod.Post,
            $"api/sales/agents/{agentId:D}/analysis/intelligence-brief", request, cancellationToken);

    public Task<RoleAgentAnalysisViewModel> AnalyzeForAgentAsync(Guid companyId, Guid agentId,
        RoleAgentAnalysisRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<RoleAgentAnalysisRequestViewModel, RoleAgentAnalysisViewModel>(companyId, HttpMethod.Post,
            $"api/sales/agents/{agentId:D}/analysis", request, cancellationToken);

    public Task<SalesNextBestActionViewModel> AnalyzeNextActionsAsync(Guid companyId, Guid agentId,
        SalesNextBestActionRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesNextBestActionRequestViewModel, SalesNextBestActionViewModel>(companyId, HttpMethod.Post,
            $"api/sales/agents/{agentId:D}/analysis/next-actions", request, cancellationToken);

    public Task<SalesForecastScenarioResultViewModel> AnalyzeForecastScenariosAsync(Guid companyId, Guid agentId,
        SalesForecastScenarioRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesForecastScenarioRequestViewModel, SalesForecastScenarioResultViewModel>(companyId, HttpMethod.Post,
            $"api/sales/agents/{agentId:D}/analysis/forecast-scenarios", request, cancellationToken);

    public Task<SalesCampaignOptimizationResultViewModel> AnalyzeCampaignsAsync(Guid companyId, Guid agentId,
        SalesCampaignOptimizationRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesCampaignOptimizationRequestViewModel, SalesCampaignOptimizationResultViewModel>(companyId, HttpMethod.Post,
            $"api/sales/agents/{agentId:D}/analysis/campaign-optimization", request, cancellationToken);

    public Task<SalesDealStrategyResultViewModel> AnalyzeDealStrategyAsync(Guid companyId, Guid agentId,
        SalesDealStrategyRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesDealStrategyRequestViewModel, SalesDealStrategyResultViewModel>(companyId, HttpMethod.Post,
            $"api/sales/agents/{agentId:D}/analysis/deal-strategy", request, cancellationToken);

    public Task<SalesProposalAdviceResultViewModel> AnalyzeProposalAsync(Guid companyId, Guid agentId,
        SalesProposalAdviceRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SalesProposalAdviceRequestViewModel, SalesProposalAdviceResultViewModel>(companyId, HttpMethod.Post,
            $"api/sales/agents/{agentId:D}/analysis/proposal-advice", request, cancellationToken);
}
