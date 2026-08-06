namespace VirtualCompany.Application.Sales;

public interface ICampaignPlanningService
{
    Task<CampaignInitiativeResponse?> GetInitiativeAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken);
    Task<CampaignInitiativeResponse?> ConfigureInitiativeAsync(Guid companyId, Guid userId, Guid campaignId, ConfigureCampaignInitiativeRequest request, CancellationToken cancellationToken);
    Task<CampaignReadinessResponse?> GetReadinessAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken);
    Task<CampaignInitiativeResponse?> RequestReadinessAsync(Guid companyId, Guid userId, Guid campaignId, long expectedVersion, CancellationToken cancellationToken);
    Task<IReadOnlyList<CampaignSegmentResponse>> ListSegmentsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CampaignSegmentResponse> CreateSegmentAsync(Guid companyId, Guid userId, CreateCampaignSegmentRequest request, CancellationToken cancellationToken);
    Task<CampaignAudiencePreviewResponse> PreviewSegmentAsync(Guid companyId, Guid segmentId, CancellationToken cancellationToken);
    Task<CampaignAudienceSnapshotResponse?> CaptureAudienceAsync(Guid companyId, Guid userId, Guid campaignId, Guid segmentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CampaignActivityResponse>> ListActivitiesAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken);
    Task<CampaignActivityResponse?> AddActivityAsync(Guid companyId, Guid userId, Guid campaignId, CreateCampaignActivityRequest request, CancellationToken cancellationToken);
    Task<CampaignPerformanceResponse?> GetPerformanceAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken);
    Task<CampaignPerformanceResponse?> CapturePerformanceSnapshotAsync(Guid companyId, Guid userId, Guid campaignId, CancellationToken cancellationToken);
}

public interface ICampaignSchedulingCoordinator
{
    Task<CampaignSchedulingResult> RunDueWorkAsync(DateTime utcNow, int batchSize, CancellationToken cancellationToken);
}

public sealed record CampaignSchedulingResult(int CampaignsStarted, int ActivitiesAdvanced, int ActivitiesFailed);

public sealed record ConfigureCampaignInitiativeRequest(
    string CampaignType,
    string? Description,
    Guid OwnerUserId,
    Guid? OwnerAgentId,
    string ObjectiveType,
    decimal ObjectiveTarget,
    string ObjectiveUnit,
    DateTime ObjectiveTargetUtc,
    DateTime PlanningStartsUtc,
    DateTime ScheduledLaunchUtc,
    DateTime EndsUtc,
    string TimeZoneId,
    decimal? PlannedBudget,
    string? BudgetCurrency,
    DateTime? ReviewDueUtc,
    long ExpectedVersion,
    CampaignOfferRequest Offer);

public sealed record CampaignOfferRequest(
    string Name,
    string SourceType,
    string SourceReference,
    Guid? KnowledgeDocumentId,
    bool NoOfferRequired);

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
    IReadOnlyList<string> MissingRequirements);

public sealed record CampaignObjectiveResponse(string Type, decimal Target, string Unit, DateTime TargetUtc);
public sealed record CampaignReadinessResponse(Guid CampaignId, string LifecycleStatus, bool IsReady, long Version, IReadOnlyList<string> MissingRequirements);

public sealed record CreateCampaignSegmentRequest(
    string Name,
    string SegmentKind,
    string? Industry,
    string? Country,
    int? MinEmployees,
    int? MaxEmployees,
    string? BuyingRole,
    string? CustomerLifecycle,
    string? ProductInterest,
    string? PreferredLanguage,
    bool RequireCommunicationPermission = true,
    bool ExcludeOpenCriticalSupportCases = true);

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

public sealed record CampaignAudiencePreviewResponse(
    Guid SegmentId,
    int SegmentVersion,
    int Eligible,
    int Excluded,
    int Suppressed,
    int Ambiguous,
    int MissingData,
    IReadOnlyList<CampaignAudiencePreviewMemberResponse> Members);

public sealed record CampaignAudienceSnapshotResponse(
    Guid Id,
    Guid CampaignId,
    Guid SegmentId,
    int SegmentVersion,
    int SnapshotVersion,
    DateTime CapturedUtc,
    int Eligible,
    int Excluded,
    int Suppressed);

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

public sealed record CampaignCurrencyAmountResponse(decimal Amount, string Currency, string Classification);
public sealed record CampaignMetricResponse(string Key, string Label, decimal? Value, string Unit, decimal? Target,
    int DefinitionVersion, string EvidenceSummary);
public sealed record CampaignAttributionEvidenceResponse(Guid SubjectId, string SubjectType, string Model,
    string Classification, decimal Confidence, int WindowDays, IReadOnlyList<Guid> SourceEventIds);
public sealed record CampaignEventResponse(Guid Id, string EventType, DateTime OccurredUtc, string Summary,
    string SourceType, Guid? ContactId, Guid? DealId, Guid? ActivityId);
