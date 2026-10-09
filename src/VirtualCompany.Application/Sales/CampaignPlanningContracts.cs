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
    Task<CampaignPresentationActivityResponse?> GetPresentationActivityAsync(Guid companyId, Guid campaignId, Guid activityId, CancellationToken cancellationToken);
    Task<CampaignPresentationActivityResponse?> SavePresentationActivityAsync(Guid companyId, Guid userId, Guid campaignId, Guid activityId, SaveCampaignPresentationActivityRequest request, CancellationToken cancellationToken);
    Task<bool> RemovePresentationActivityAsync(Guid companyId, Guid userId, Guid campaignId, Guid activityId, int expectedVersion, CancellationToken cancellationToken);
    Task<CampaignPresentationActivityResponse?> RetryPresentationActivityAsync(Guid companyId, Guid userId, Guid campaignId, Guid activityId, CancellationToken cancellationToken);
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

public static class CampaignPresentationBlockerCodes
{
    public const string PresetUnavailable = "campaign.presentation.preset_unavailable";
    public const string AssetUnavailable = "campaign.presentation.asset_unavailable";
    public const string AudienceUnavailable = "campaign.presentation.audience_unavailable";
    public const string AccountMissing = "campaign.presentation.account_missing";
    public const string PresenterUnavailable = "campaign.presentation.presenter_unavailable";
    public const string EventMissing = "campaign.presentation.event_missing";
    public const string StrategyInvalid = "campaign.presentation.strategy_invalid";
    public const string CardinalityExceeded = "campaign.presentation.cardinality_exceeded";
}
