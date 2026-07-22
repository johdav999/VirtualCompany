using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Support;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Controllers;

[ApiController]
[Route("api/support/agents/{agentId:guid}/analysis")]
[Authorize(Policy = CompanyPolicies.CompanyMember)]
[RequireCompanyContext]
public sealed class SupportAgentAnalysisController(
    ISupportAgentAnalysisService analysis,
    ISupportAgentDecisionService decisions,
    ICompanyContextAccessor companyContext) : ControllerBase
{
    [HttpPost]
    public Task<RoleAgentAnalysisResult> Analyze(Guid agentId,
        [FromBody] RoleAgentAnalysisRequest request, CancellationToken cancellationToken) =>
        analysis.AnalyzeAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("queue")]
    public Task<SupportQueueAnalysisResult> Queue(Guid agentId, [FromBody] SupportQueueAnalysisRequest request,
        CancellationToken cancellationToken) =>
        decisions.AnalyzeQueueAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("answerability")]
    public Task<SupportAnswerabilityResult> Answerability(Guid agentId,
        [FromBody] SupportAnswerabilityRequest request, CancellationToken cancellationToken) =>
        decisions.AnalyzeAnswerabilityAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("risk")]
    public Task<SupportRiskAssessmentResult> Risk(Guid agentId, [FromBody] SupportRiskAssessmentRequest request,
        CancellationToken cancellationToken) =>
        decisions.AnalyzeRiskAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("recurring-issues")]
    public Task<SupportRecurringIssueResult> RecurringIssues(Guid agentId,
        [FromBody] SupportRecurringIssueRequest request, CancellationToken cancellationToken) =>
        decisions.AnalyzeRecurringIssuesAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    [HttpPost("knowledge-coverage")]
    public Task<SupportKnowledgeCoverageResult> KnowledgeCoverage(Guid agentId,
        [FromBody] SupportKnowledgeCoverageRequest request, CancellationToken cancellationToken) =>
        decisions.AnalyzeKnowledgeCoverageAsync(CompanyId(), agentId, companyContext.UserId, request, cancellationToken);

    private Guid CompanyId() => companyContext.CompanyId is { } id && id != Guid.Empty
        ? id : throw new UnauthorizedAccessException("A resolved company is required.");
}
