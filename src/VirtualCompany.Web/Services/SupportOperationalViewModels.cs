namespace VirtualCompany.Web.Services;

public sealed record SupportOperationalReportQuery(string View = "backlog", string? Status = null,
    string? Priority = null, string? Category = null, string? Search = null, Guid? AssignedUserId = null,
    Guid? AssignedAgentId = null, bool Unassigned = false, Guid? ContactId = null,
    Guid? CustomerCompanyId = null, string? AgeBucket = null, bool FailedReply = false);
public sealed record SupportOperationalCase(SupportCaseListItem Case, string Owner, decimal AgeHours,
    string AgeBucket, DateTime? NextDeadlineUtc, bool Waiting, bool MissingTarget);
public sealed record SupportOperationalReport(Guid CompanyId, string View, DateTime AsOfUtc,
    SupportBusinessCalendarDto Calendar, string Definition, string ClockRules,
    IReadOnlyList<SupportOperationalCase> Cases, int AtRisk, int Breached, int Waiting,
    int Escalated, int AwaitingApproval, int MissingTargets, IReadOnlyList<SupportMetricBucket> AgeBuckets);
public sealed record SupportKnowledgeContext(Guid SupportCaseId, IReadOnlyList<SupportKnowledgeSourceReference> Sources,
    IReadOnlyList<string> CustomerMemorySummaries, IReadOnlyList<string> SimilarCaseSummaries,
    decimal RetrievalConfidence, string RationaleSummary);
public sealed record SupportKnowledgeSourceReference(string Type, string Label, Guid? EntityId,
    string? Excerpt, decimal Relevance, bool IsTrusted = false, Guid? DocumentId = null, string? SourceReference = null);
