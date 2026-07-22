using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/companies/{companyId:guid}/finance/agents/{agentId:guid}/analysis")]
[Authorize(Policy = CompanyPolicies.FinanceView)]
[RequireCompanyContext]
public sealed class FinanceAgentAnalysisController(
    IFinanceAgentAnalysisService analysis,
    IFinanceAgentDecisionService decisions,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpGet("close-periods")]
    public Task<IReadOnlyList<FinanceClosePeriodOptionDto>> ListClosePeriods(Guid companyId,
        CancellationToken cancellationToken) => decisions.ListClosePeriodsAsync(companyId, cancellationToken);

    [HttpPost]
    public Task<RoleAgentAnalysisResult> Analyze(Guid companyId, Guid agentId,
        [FromBody] RoleAgentAnalysisRequest request, CancellationToken cancellationToken) =>
        analysis.AnalyzeAsync(companyId, agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("cash-scenarios")]
    public Task<FinanceCashScenarioAnalysisResult> AnalyzeCash(Guid companyId, Guid agentId,
        [FromBody] FinanceCashScenarioAnalysisRequest request, CancellationToken cancellationToken) =>
        decisions.AnalyzeCashAsync(companyId, agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("payment-runs")]
    public Task<FinancePaymentRunAnalysisResult> AnalyzePaymentRun(Guid companyId, Guid agentId,
        [FromBody] FinancePaymentRunAnalysisRequest request, CancellationToken cancellationToken) =>
        decisions.AnalyzePaymentRunAsync(companyId, agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("payment-runs/commit")]
    [Authorize(Policy = CompanyPolicies.FinanceEdit)]
    public Task<CommitFinancePaymentRunResult> CommitPaymentRun(Guid companyId, Guid agentId,
        [FromBody] CommitFinancePaymentRunCommand command, CancellationToken cancellationToken) =>
        decisions.CommitPaymentRunAsync(companyId, agentId, companyContext.UserId, command, cancellationToken);

    [HttpPost("collections-plans")]
    public Task<FinanceCollectionsPlanResult> AnalyzeCollections(Guid companyId, Guid agentId,
        [FromBody] FinanceCollectionsPlanRequest request, CancellationToken cancellationToken) =>
        decisions.AnalyzeCollectionsAsync(companyId, agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("accounting-treatment")]
    public Task<FinanceAccountingTreatmentResult> RecommendAccountingTreatment(Guid companyId, Guid agentId,
        [FromBody] FinanceAccountingTreatmentRequest request, CancellationToken cancellationToken) =>
        decisions.RecommendAccountingTreatmentAsync(companyId, agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("close-analysis")]
    public Task<FinanceCloseAnalysisResult> AnalyzeClose(Guid companyId, Guid agentId,
        [FromBody] FinanceCloseAnalysisRequest request, CancellationToken cancellationToken) =>
        decisions.AnalyzeCloseAsync(companyId, agentId, companyContext.UserId, request, cancellationToken);
}
