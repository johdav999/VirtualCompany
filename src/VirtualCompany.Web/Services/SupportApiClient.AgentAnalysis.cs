namespace VirtualCompany.Web.Services;

public sealed partial class SupportApiClient
{
    public Task<RoleAgentAnalysisViewModel> AnalyzeForAgentAsync(Guid companyId, Guid agentId,
        RoleAgentAnalysisRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<RoleAgentAnalysisRequestViewModel, RoleAgentAnalysisViewModel>(companyId, HttpMethod.Post,
            $"api/support/agents/{agentId:D}/analysis", request, cancellationToken);

    public Task<SupportQueueAnalysisResultViewModel> AnalyzeQueueAsync(Guid companyId, Guid agentId,
        SupportQueueAnalysisRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SupportQueueAnalysisRequestViewModel, SupportQueueAnalysisResultViewModel>(companyId, HttpMethod.Post,
            $"api/support/agents/{agentId:D}/analysis/queue", request, cancellationToken);

    public Task<SupportRiskAssessmentResultViewModel> AnalyzeRiskAsync(Guid companyId, Guid agentId,
        SupportRiskAssessmentRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SupportRiskAssessmentRequestViewModel, SupportRiskAssessmentResultViewModel>(companyId, HttpMethod.Post,
            $"api/support/agents/{agentId:D}/analysis/risk", request, cancellationToken);

    public Task<SupportAnswerabilityResultViewModel> AnalyzeAnswerabilityAsync(Guid companyId, Guid agentId,
        SupportAnswerabilityRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SupportAnswerabilityRequestViewModel, SupportAnswerabilityResultViewModel>(companyId, HttpMethod.Post,
            $"api/support/agents/{agentId:D}/analysis/answerability", request, cancellationToken);

    public Task<SupportRecurringIssueResultViewModel> AnalyzeRecurringIssuesAsync(Guid companyId, Guid agentId,
        SupportRecurringIssueRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SupportRecurringIssueRequestViewModel, SupportRecurringIssueResultViewModel>(companyId, HttpMethod.Post,
            $"api/support/agents/{agentId:D}/analysis/recurring-issues", request, cancellationToken);

    public Task<SupportKnowledgeCoverageResultViewModel> AnalyzeKnowledgeCoverageAsync(Guid companyId, Guid agentId,
        SupportKnowledgeCoverageRequestViewModel request, CancellationToken cancellationToken = default) =>
        SendAsync<SupportKnowledgeCoverageRequestViewModel, SupportKnowledgeCoverageResultViewModel>(companyId, HttpMethod.Post,
            $"api/support/agents/{agentId:D}/analysis/knowledge-coverage", request, cancellationToken);
}
