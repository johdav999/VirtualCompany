using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Support;

public sealed record SupportQueueAnalysisRequest(Guid? SupportCaseId = null, int Limit = 50,
    DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SupportQueueItemDto(Guid SupportCaseId, string CaseNumber, string Subject, int PriorityScore,
    string PriorityBand, string Category, string Status, DateTime? ResolutionDueUtc, decimal? MinutesToResolutionDue,
    bool MandatoryEscalation, bool RequiresReview, IReadOnlyList<string> ReasonCodes, IReadOnlyList<string> SourceIds);
public sealed record SupportQueueAnalysisResult(RoleAgentAnalysisResult Advice,
    IReadOnlyList<SupportQueueItemDto> Items, IReadOnlyList<string> MissingEvidence, bool RequiresReview);

public sealed record SupportAnswerabilityRequest(Guid SupportCaseId, string? DraftBody = null,
    DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SupportAnswerabilityClaimDto(string ClaimType, string Text, string Status,
    IReadOnlyList<string> SourceIds);
public sealed record SupportAnswerabilityResult(RoleAgentAnalysisResult Advice, Guid SupportCaseId,
    decimal Score, string State, IReadOnlyList<SupportAnswerabilityClaimDto> ConfirmedContext,
    IReadOnlyList<string> MissingInformation, IReadOnlyList<string> QuestionsToAsk,
    IReadOnlyList<string> TrustedSourceIds, SupportReplySafetyDecision? DraftSafety, bool CanDraft, bool RequiresReview);

public sealed record SupportRiskAssessmentRequest(Guid? SupportCaseId = null, int Limit = 50,
    DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SupportRiskItemDto(Guid SupportCaseId, string CaseNumber, string Subject, string Severity,
    IReadOnlyList<string> ConfirmedRiskTypes, IReadOnlyList<string> Hypotheses, DateTime? ResolutionDueUtc,
    decimal? MinutesToResolutionDue, string RequiredRole, bool MandatoryEscalation,
    IReadOnlyList<string> AllowedActions, IReadOnlyList<string> SourceIds);
public sealed record SupportRiskAssessmentResult(RoleAgentAnalysisResult Advice,
    IReadOnlyList<SupportRiskItemDto> Risks, IReadOnlyList<string> MissingEvidence, bool RequiresReview);

public sealed record SupportRecurringIssueRequest(int WindowDays = 90, int MinimumCases = 2,
    DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SupportRecurringIssueDto(string ClusterId, string Version, string Category, int CaseCount,
    IReadOnlyList<Guid> CaseIds, IReadOnlyList<string> SharedConfirmedFacts, IReadOnlyList<string> Differences,
    IReadOnlyList<string> RootCauseHypotheses, string RecommendedInvestigation, IReadOnlyList<string> SourceIds,
    bool RequiresReview);
public sealed record SupportRecurringIssueResult(RoleAgentAnalysisResult Advice,
    IReadOnlyList<SupportRecurringIssueDto> Clusters, IReadOnlyList<string> MissingEvidence, bool RequiresReview);

public sealed record SupportKnowledgeCoverageRequest(int Limit = 50, DateTime? AsOfUtc = null, string? Objective = null);
public sealed record SupportDocumentationRecommendationDto(Guid KnowledgeGapId, string Topic, string Category,
    int Frequency, string MissingInformation, string Status, Guid? LinkedDocumentId, bool ResolutionVerified,
    string RecommendedOwner, IReadOnlyList<string> SourceIds);
public sealed record SupportKnowledgeCoverageResult(RoleAgentAnalysisResult Advice, int OpenGapCount,
    int RepeatedGapCount, IReadOnlyList<SupportDocumentationRecommendationDto> Recommendations,
    IReadOnlyList<string> MissingEvidence, bool RequiresReview);

public interface ISupportAgentDecisionService
{
    Task<SupportQueueAnalysisResult> AnalyzeQueueAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SupportQueueAnalysisRequest request, CancellationToken cancellationToken);
    Task<SupportAnswerabilityResult> AnalyzeAnswerabilityAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SupportAnswerabilityRequest request, CancellationToken cancellationToken);
    Task<SupportRiskAssessmentResult> AnalyzeRiskAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SupportRiskAssessmentRequest request, CancellationToken cancellationToken);
    Task<SupportRecurringIssueResult> AnalyzeRecurringIssuesAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SupportRecurringIssueRequest request, CancellationToken cancellationToken);
    Task<SupportKnowledgeCoverageResult> AnalyzeKnowledgeCoverageAsync(Guid companyId, Guid agentId, Guid? actorUserId,
        SupportKnowledgeCoverageRequest request, CancellationToken cancellationToken);
}
