using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/sales/agents/{agentId:guid}/analysis")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SalesAgentAnalysisController(
    ISalesAgentAnalysisService analysis,
    ISalesAgentDecisionService decisions,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpPost]
    public Task<RoleAgentAnalysisResult> Analyze(Guid agentId,
        [FromBody] RoleAgentAnalysisRequest request, CancellationToken cancellationToken) =>
        analysis.AnalyzeAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("intelligence-brief")]
    public Task<SalesIntelligenceBriefResult> IntelligenceBrief(Guid agentId,
        [FromBody] SalesIntelligenceBriefRequest request, CancellationToken cancellationToken) =>
        decisions.BuildIntelligenceBriefAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("next-actions")]
    public Task<SalesNextBestActionResult> NextActions(Guid agentId,
        [FromBody] SalesNextBestActionRequest request, CancellationToken cancellationToken) =>
        decisions.RecommendNextActionsAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("deal-strategy")]
    public Task<SalesDealStrategyResult> DealStrategy(Guid agentId,
        [FromBody] SalesDealStrategyRequest request, CancellationToken cancellationToken) =>
        decisions.AnalyzeDealStrategyAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("forecast-scenarios")]
    public Task<SalesForecastScenarioResult> ForecastScenarios(Guid agentId,
        [FromBody] SalesForecastScenarioRequest request, CancellationToken cancellationToken) =>
        decisions.AnalyzeForecastAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("campaign-optimization")]
    public Task<SalesCampaignOptimizationResult> CampaignOptimization(Guid agentId,
        [FromBody] SalesCampaignOptimizationRequest request, CancellationToken cancellationToken) =>
        decisions.OptimizeCampaignsAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("proposal-advice")]
    public Task<SalesProposalAdviceResult> ProposalAdvice(Guid agentId,
        [FromBody] SalesProposalAdviceRequest request, CancellationToken cancellationToken) =>
        decisions.AdviseProposalAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved company is required.");
}
