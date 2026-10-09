

namespace VirtualCompany.Application.Sales;


public sealed record CampaignPresentationRunResponse(
    Guid Id, Guid PresentationRunId, string SubjectType, Guid SubjectId, string Status,
    string? FailureCode, string? FailureSummary, string PreparationStatus, Guid PresenterAgentId);


public sealed record CampaignAudiencePreviewMemberResponse(
    Guid ContactId,
    string ContactName,
    string Email,
    Guid? CustomerCompanyId,
    string? CustomerCompanyName,
    string EligibilityStatus,
    string Reason,
    string ConsentStatus,
    string? CommunicationLanguage);


public sealed record CampaignMarketingContextResponse(Guid PlanId, string PlanName, int PlanVersion,
    Guid? ObjectiveId, string ObjectiveContribution, IReadOnlyList<Guid> SegmentVersionIds,
    IReadOnlyList<string> EvidenceReferences, Guid? PlanApprovalRequestId);


public sealed record CampaignPresentationReadinessBlocker(
    string Code, string Explanation, string Evidence, string CorrectiveAction, bool RequiresReview, bool RequiresApproval);


public sealed record SaveCampaignPresentationActivityRequest(
    Guid PresetVersionId,
    string ExecutionScope,
    string PresenterStrategy,
    Guid? ExplicitPresenterAgentId,
    string WorkStrategy,
    bool AllowOverrides,
    Guid? EventSessionId,
    int PreparationLeadTimeHours,
    int ExpectedVersion);


public sealed record CampaignPerformanceResponse(
    Guid CampaignId,
    string LifecycleStatus,
    CampaignObjectiveResponse? Objective,
    decimal? ObjectiveProgress,
    int Audience,
    int Sent,
    int Delivered,
    int Replied,
    int Bounced,
    int Opportunities,
    int WonDeals,
    IReadOnlyList<CampaignCurrencyAmountResponse> DirectRevenue,
    IReadOnlyList<CampaignCurrencyAmountResponse> PlannedBudget,
    IReadOnlyList<CampaignCurrencyAmountResponse> Costs,
    IReadOnlyList<CampaignMetricResponse> Metrics,
    IReadOnlyList<CampaignAttributionEvidenceResponse> Attribution,
    IReadOnlyList<CampaignEventResponse> Timeline,
    DateTime ObservedUtc);


public sealed record CampaignObjectiveResponse(string Type, decimal Target, string Unit, DateTime TargetUtc);


public sealed record CampaignAudiencePreviewResponse(
    Guid SegmentId,
    int SegmentVersion,
    int Eligible,
    int Excluded,
    int Suppressed,
    int Ambiguous,
    int MissingData,
    IReadOnlyList<CampaignAudiencePreviewMemberResponse> Members);


public sealed record CreateCampaignActivityRequest(
    string Name,
    string ActivityType,
    string Channel,
    string ExecutionMode,
    DateTime PlannedStartUtc,
    DateTime DueUtc,
    string TimeZoneId,
    Guid? OwnerUserId,
    Guid? OwnerAgentId,
    Guid? DependsOnActivityId,
    Guid? MilestoneId,
    Guid? SalesSequenceStepId,
    string? RequiredToolCapability);


public sealed record CampaignInitiativeResponse(
    Guid Id,
    string Name,
    string CampaignType,
    string LifecycleStatus,
    string? Description,
    Guid? OwnerUserId,
    Guid? OwnerAgentId,
    CampaignObjectiveResponse? PrimaryObjective,
    DateTime? PlanningStartsUtc,
    DateTime? ScheduledLaunchUtc,
    DateTime? EndsUtc,
    DateTime? ReviewDueUtc,
    string TimeZoneId,
    decimal? PlannedBudget,
    string? BudgetCurrency,
    bool LegacySetupRequired,
    long Version,
    IReadOnlyList<string> MissingRequirements,
    CampaignMarketingContextResponse? MarketingContext = null);


public sealed record CampaignPresentationRunProjection(
    int EligibleContacts, int DistinctAccounts, int ProjectedRuns, string SubjectType, bool IsBounded);


public sealed record CampaignActivityResponse(
    Guid Id,
    string Name,
    string ActivityType,
    string Channel,
    string ExecutionMode,
    string Status,
    DateTime PlannedStartUtc,
    DateTime DueUtc,
    Guid? OwnerUserId,
    Guid? OwnerAgentId,
    Guid? DependsOnActivityId,
    string? RequiredToolCapability,
    int AttemptCount,
    string? ResultSummary,
    string? FailureReason);


public sealed record CampaignPresentationActivityResponse(
    Guid Id, Guid CampaignId, Guid ActivityId, Guid PresetId, string PresetName, Guid PresetVersionId, int PresetVersionNumber,
    string ExecutionScope, string PresenterStrategy, Guid? ExplicitPresenterAgentId, string WorkStrategy, bool AllowOverrides,
    Guid? EventSessionId, int PreparationLeadTimeHours, int Version, bool IsReady,
    CampaignPresentationRunProjection Projection, IReadOnlyList<CampaignPresentationReadinessBlocker> Blockers,
    IReadOnlyList<CampaignPresentationRunResponse> Runs);


public sealed record CampaignSegmentResponse(
    Guid Id,
    string Name,
    string SegmentKind,
    int Version,
    bool IsActive,
    string? Industry,
    string? Country,
    int? MinEmployees,
    int? MaxEmployees,
    string? BuyingRole,
    string? CustomerLifecycle,
    string? ProductInterest,
    string? PreferredLanguage,
    bool RequireCommunicationPermission,
    bool ExcludeOpenCriticalSupportCases);

public sealed record CampaignAttributionEvidenceResponse(Guid SubjectId, string SubjectType, string Model,
    string Classification, decimal Confidence, int WindowDays, IReadOnlyList<Guid> SourceEventIds);


public sealed record CampaignCurrencyAmountResponse(decimal Amount, string Currency, string Classification);

public sealed record CampaignEventResponse(Guid Id, string EventType, DateTime OccurredUtc, string Summary,
    string SourceType, Guid? ContactId, Guid? DealId, Guid? ActivityId);

public sealed record CampaignMetricResponse(string Key, string Label, decimal? Value, string Unit, decimal? Target,
    int DefinitionVersion, string EvidenceSummary);

public sealed record CampaignReadinessResponse(Guid CampaignId, string LifecycleStatus, bool IsReady, long Version, IReadOnlyList<string> MissingRequirements);
