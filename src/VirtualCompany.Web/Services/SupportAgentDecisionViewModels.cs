namespace VirtualCompany.Web.Services;

public sealed class SupportQueueAnalysisRequestViewModel
{
    public Guid? SupportCaseId { get; set; }
    public int Limit { get; set; } = 50;
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SupportQueueAnalysisResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public List<SupportQueueItemViewModel> Items { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SupportQueueItemViewModel
{
    public Guid SupportCaseId { get; set; }
    public string CaseNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int PriorityScore { get; set; }
    public string PriorityBand { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? ResolutionDueUtc { get; set; }
    public decimal? MinutesToResolutionDue { get; set; }
    public bool MandatoryEscalation { get; set; }
    public bool RequiresReview { get; set; }
    public List<string> ReasonCodes { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
}

public sealed class SupportRiskAssessmentRequestViewModel
{
    public Guid? SupportCaseId { get; set; }
    public int Limit { get; set; } = 50;
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SupportAnswerabilityRequestViewModel
{
    public Guid SupportCaseId { get; set; }
    public string? DraftBody { get; set; }
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SupportAnswerabilityResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public Guid SupportCaseId { get; set; }
    public decimal Score { get; set; }
    public string State { get; set; } = string.Empty;
    public List<SupportAnswerabilityClaimViewModel> ConfirmedContext { get; set; } = [];
    public List<string> MissingInformation { get; set; } = [];
    public List<string> QuestionsToAsk { get; set; } = [];
    public List<string> TrustedSourceIds { get; set; } = [];
    public SupportReplySafetyDecisionViewModel? DraftSafety { get; set; }
    public bool CanDraft { get; set; }
    public bool RequiresReview { get; set; }
}

public sealed class SupportAnswerabilityClaimViewModel
{
    public string ClaimType { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public List<string> SourceIds { get; set; } = [];
}

public sealed class SupportReplySafetyDecisionViewModel
{
    public string Decision { get; set; } = string.Empty;
    public List<string> ReasonCodes { get; set; } = [];
    public List<string> Explanations { get; set; } = [];
    public string PolicyVersion { get; set; } = string.Empty;
}

public sealed class SupportRiskAssessmentResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public List<SupportRiskItemViewModel> Risks { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SupportRiskItemViewModel
{
    public Guid SupportCaseId { get; set; }
    public string CaseNumber { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public List<string> ConfirmedRiskTypes { get; set; } = [];
    public List<string> Hypotheses { get; set; } = [];
    public DateTime? ResolutionDueUtc { get; set; }
    public decimal? MinutesToResolutionDue { get; set; }
    public string RequiredRole { get; set; } = string.Empty;
    public bool MandatoryEscalation { get; set; }
    public List<string> AllowedActions { get; set; } = [];
    public List<string> SourceIds { get; set; } = [];
}

public sealed class SupportRecurringIssueRequestViewModel
{
    public int WindowDays { get; set; } = 90;
    public int MinimumCases { get; set; } = 2;
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SupportRecurringIssueResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public List<SupportRecurringIssueViewModel> Clusters { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SupportRecurringIssueViewModel
{
    public string ClusterId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int CaseCount { get; set; }
    public List<Guid> CaseIds { get; set; } = [];
    public List<string> SharedConfirmedFacts { get; set; } = [];
    public List<string> Differences { get; set; } = [];
    public List<string> RootCauseHypotheses { get; set; } = [];
    public string RecommendedInvestigation { get; set; } = string.Empty;
    public List<string> SourceIds { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SupportKnowledgeCoverageRequestViewModel
{
    public int Limit { get; set; } = 50;
    public DateTime? AsOfUtc { get; set; }
    public string? Objective { get; set; }
}

public sealed class SupportKnowledgeCoverageResultViewModel
{
    public RoleAgentAnalysisViewModel Advice { get; set; } = new();
    public int OpenGapCount { get; set; }
    public int RepeatedGapCount { get; set; }
    public List<SupportDocumentationRecommendationViewModel> Recommendations { get; set; } = [];
    public List<string> MissingEvidence { get; set; } = [];
    public bool RequiresReview { get; set; }
}

public sealed class SupportDocumentationRecommendationViewModel
{
    public Guid KnowledgeGapId { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public int Frequency { get; set; }
    public string MissingInformation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public Guid? LinkedDocumentId { get; set; }
    public bool ResolutionVerified { get; set; }
    public string RecommendedOwner { get; set; } = string.Empty;
    public List<string> SourceIds { get; set; } = [];
}
