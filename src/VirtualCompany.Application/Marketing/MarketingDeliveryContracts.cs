namespace VirtualCompany.Application.Marketing;

public sealed record MarketingChannelConnectionDto(Guid Id, string Provider, string DisplayName, string CapabilitiesJson,
    string Status, string HealthStatus, string? FailureSummary, DateTime? LastCheckedUtc);
public sealed record ConnectMarketingChannelRequest(string Provider, string ExternalAccountReference, string DisplayName,
    string CapabilitiesJson, string SecretReference);
public sealed record StartMarketingChannelOAuthRequest(string Provider, string RedirectUri);
public sealed record MarketingChannelOAuthStartDto(string Provider, Uri AuthorizationUri, DateTime ExpiresUtc);
public sealed record MarketingChannelOAuthState(Guid SessionId, Guid CompanyId, Guid UserId, string Provider,
    string RedirectUri, string CodeVerifier, DateTime ExpiresUtc);
public sealed record CompleteMarketingChannelOAuthRequest(string State, string Code);
public sealed record MarketingChannelDestinationDto(Guid Id, Guid ConnectionId, string ProviderReference,
    string DisplayName, string DestinationType, string CapabilitiesJson, string Status, DateTime LastDiscoveredUtc);
public sealed record MarketingChannelOAuthCompletionDto(MarketingChannelConnectionDto Connection,
    IReadOnlyList<MarketingChannelDestinationDto> Destinations);
public interface IMarketingChannelOAuthStateProtector
{
    string Protect(MarketingChannelOAuthState state);
    MarketingChannelOAuthState Unprotect(string protectedState);
}
public interface IMarketingChannelConnectionService
{
    Task<MarketingChannelOAuthStartDto> StartOAuthAsync(Guid companyId, Guid userId,
        StartMarketingChannelOAuthRequest request, CancellationToken ct);
    Task<MarketingChannelOAuthCompletionDto> CompleteOAuthAsync(CompleteMarketingChannelOAuthRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingChannelDestinationDto>> ListDestinationsAsync(Guid companyId, Guid? connectionId, CancellationToken ct);
    Task<IReadOnlyList<MarketingChannelDestinationDto>> RefreshDestinationsAsync(Guid companyId, Guid connectionId, CancellationToken ct);
    Task<bool> DisconnectAsync(Guid companyId, Guid connectionId, CancellationToken ct);
}
public sealed record MarketingChannelActionDto(Guid Id, Guid ConnectionId, string DestinationReference, string ActionType,
    string PayloadJson, DateTime? ScheduledUtc, string Status, Guid? ApprovalRequestId, int Version, int AttemptCount,
    string? ProviderReference, string? FailureCode, int? ContentBriefVersion);
public sealed record PrepareMarketingChannelActionRequest(Guid ConnectionId, Guid? CampaignId, Guid? ContentBriefId,
    string DestinationReference, string ActionType, string PayloadJson, DateTime? ScheduledUtc, string IdempotencyKey);
public sealed record MarketingJourneyDto(Guid Id, string Name, string AudienceEligibilityJson, string EntryExitCriteriaJson,
    string StepsJson, string GuardrailsJson, int FrequencyCap, DateTime ValidFromUtc, DateTime ValidToUtc,
    string Status, Guid? ApprovalRequestId, int Version, Guid? SupersedesJourneyId, int ConcurrencyVersion,
    Guid? SegmentVersionId);
public sealed record CreateMarketingJourneyRequest(string Name, string AudienceEligibilityJson, string EntryExitCriteriaJson,
    string StepsJson, string GuardrailsJson, int FrequencyCap, DateTime ValidFromUtc, DateTime ValidToUtc, string IdempotencyKey,
    Guid? SegmentVersionId = null);
public sealed record CreateMarketingJourneyVersionRequest(string Name, string AudienceEligibilityJson,
    string EntryExitCriteriaJson, string StepsJson, string GuardrailsJson, int FrequencyCap,
    DateTime ValidFromUtc, DateTime ValidToUtc, string IdempotencyKey, Guid? SegmentVersionId = null);
public sealed record MarketingJourneyValidationDto(bool Valid, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, int StepCount);
public sealed record MarketingJourneyAudiencePreviewDto(int EligibleCount, int SuppressedCount,
    int MissingConsentCount, IReadOnlyList<Guid> SampleContactIds, DateTime EvaluatedUtc);
public sealed record MarketingJourneyEnrollmentDto(Guid Id, Guid JourneyId, Guid ContactId, int JourneyVersion,
    string ConsentEvidenceReference, string Status, int NextStepIndex, DateTime? NextStepUtc,
    int ActionsInWindow, Guid? LastChannelActionId, string? FailureCode, DateTime UpdatedUtc);
public sealed record EnrollMarketingJourneyRequest(Guid ContactId, string ConsentEvidenceReference, string IdempotencyKey);
public sealed record MarketingCreativeAssetDto(Guid Id, Guid AssetFamilyId, int VersionNumber, Guid BriefId, Guid? CampaignId, string Name, string MediaType,
    string Dimensions, string Language, string GenerationSummary, string PromptVersion, string ProviderReference,
    string BrandProfileVersion, string SafetyResult, string AltText, string StorageReference, string Checksum,
    string Status, int Version, DateTime CreatedUtc, DateTime UpdatedUtc, Guid? ContentVariantId,
    string SourceAssetIdsJson, string ProvenanceJson, string AuditReference);
public sealed record RegisterMarketingCreativeAssetRequest(Guid BriefId, Guid? CampaignId, string Name, string MediaType,
    string Dimensions, string Language, string GenerationSummary, string PromptVersion, string ProviderReference,
    string BrandProfileVersion, string SafetyResult, string AltText, string StorageReference, string Checksum,
    string IdempotencyKey, Guid? ContentVariantId = null, IReadOnlyList<Guid>? SourceAssetIds = null,
    string ProvenanceJson = "{}");
public sealed record GenerateMarketingCreativeAssetRequest(Guid BriefId, Guid? CampaignId, string Name,
    string Prompt, string Dimensions, string Language, string BrandProfileVersion, string AltText,
    string IdempotencyKey, string Quality = "medium", string OutputFormat = "png", Guid? RegenerateFromAssetId = null,
    Guid? ContentVariantId = null, IReadOnlyList<Guid>? ReferenceAssetIds = null);
public sealed record UploadMarketingCreativeAssetRequest(Guid BriefId, Guid? CampaignId, string Name,
    string FileName, string ContentType, long Length, Stream Content, string Dimensions, string Language,
    string BrandProfileVersion, string AltText, string IdempotencyKey);
public sealed record MarketingCreativeAssetContentDto(Guid AssetId, string ContentType, Stream Content,
    string Checksum, string ProviderModel, string ProviderRequestId);
public sealed record MarketingCreativeAssetScanDto(Guid Id, Guid AssetId, string Provider, string ProviderReference,
    string ScannerVersion, string Result, string ReasonCode, string EvidenceJson, DateTime ScannedUtc);
public sealed record MarketingAssetScanRequest(Guid CompanyId, Guid AssetId, string FileName, string ContentType,
    byte[] Content, string Checksum);
public sealed record MarketingAssetScanResult(string Provider, string ProviderReference, string ScannerVersion,
    string Result, string ReasonCode, string EvidenceJson, DateTime ScannedUtc);
public interface IMarketingAssetSafetyScanner
{
    Task<MarketingAssetScanResult> ScanAsync(MarketingAssetScanRequest request, CancellationToken ct);
}
public sealed record UpdateMarketingCreativeAssetMetadataRequest(string Name, string Language, string AltText);
public sealed record MarketingAttributionDto(Guid Id, string SubjectType, Guid SubjectId, string Model,
    string Classification, decimal AttributedValue, string Unit, string EvidenceJson, decimal Confidence,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, DateTime CreatedUtc);
public sealed record MarketingMetricDefinitionDto(string Code, string UnitFamily, string Aggregation,
    IReadOnlyList<string> SupportedDimensions, int MaximumFreshnessHours, string MinimumSourceQuality,
    string Explanation);
public sealed record RecordMarketingAttributionRequest(string SubjectType, Guid SubjectId, string Model,
    string Classification, decimal AttributedValue, string Unit, string EvidenceJson, decimal Confidence,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, string IdempotencyKey);
public sealed record MarketingEventTriggerDto(Guid Id, string EventType, string SourceType, string SourceId,
    int SourceVersion, string Severity, string EvidenceJson, string CorrelationId, string Status,
    Guid? OperatingRunId, Guid? RelatedTaskId, string? FailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc);
public sealed record CreateMarketingEventTriggerRequest(string EventType, string SourceType, string SourceId,
    int SourceVersion, string Severity, string EvidenceJson, string IdempotencyKey, string CorrelationId);
public interface IMarketingDeliveryService
{
    Task<IReadOnlyList<MarketingChannelConnectionDto>> ListConnectionsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingChannelConnectionDto> ConnectAsync(Guid companyId, Guid userId, ConnectMarketingChannelRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingChannelActionDto>> ListActionsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingChannelActionDto> PrepareActionAsync(Guid companyId, PrepareMarketingChannelActionRequest request, CancellationToken ct);
    Task<MarketingChannelActionDto?> SubmitActionAsync(Guid companyId, Guid userId, Guid actionId, CancellationToken ct);
    Task<MarketingChannelActionDto?> SynchronizeApprovedActionAsync(Guid companyId, Guid actionId, CancellationToken ct);
    Task<MarketingChannelActionDto?> CancelActionAsync(Guid companyId, Guid actionId, CancellationToken ct);
    Task<IReadOnlyList<MarketingJourneyDto>> ListJourneysAsync(Guid companyId, CancellationToken ct);
    Task<MarketingJourneyDto> CreateJourneyAsync(Guid companyId, Guid userId, CreateMarketingJourneyRequest request, CancellationToken ct);
    Task<MarketingJourneyDto?> CreateJourneyVersionAsync(Guid companyId, Guid userId, Guid journeyId, CreateMarketingJourneyVersionRequest request, CancellationToken ct);
    Task<MarketingJourneyValidationDto?> ValidateJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct);
    Task<MarketingJourneyAudiencePreviewDto?> PreviewJourneyAudienceAsync(Guid companyId, Guid journeyId, int sampleSize, CancellationToken ct);
    Task<MarketingJourneyDto?> SubmitJourneyAsync(Guid companyId, Guid userId, Guid journeyId, CancellationToken ct);
    Task<MarketingJourneyDto?> SynchronizeApprovedJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct);
    Task<MarketingJourneyDto?> PauseJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct);
    Task<MarketingJourneyDto?> ResumeJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct);
    Task<MarketingJourneyDto?> CompleteJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct);
    Task<MarketingJourneyDto?> CancelJourneyAsync(Guid companyId, Guid journeyId, CancellationToken ct);
    Task<IReadOnlyList<MarketingJourneyEnrollmentDto>> ListJourneyEnrollmentsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingJourneyEnrollmentDto> EnrollJourneyAsync(Guid companyId, Guid journeyId, EnrollMarketingJourneyRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingCreativeAssetDto>> ListCreativeAssetsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingCreativeAssetDto> RegisterCreativeAssetAsync(Guid companyId, Guid userId, RegisterMarketingCreativeAssetRequest request, CancellationToken ct);
    Task<MarketingCreativeAssetDto> GenerateCreativeAssetAsync(Guid companyId, Guid userId, GenerateMarketingCreativeAssetRequest request, CancellationToken ct);
    Task<MarketingCreativeAssetDto> UploadCreativeAssetAsync(Guid companyId, Guid userId,
        UploadMarketingCreativeAssetRequest request, CancellationToken ct);
    Task<MarketingCreativeAssetContentDto?> GetCreativeAssetContentAsync(Guid companyId, Guid assetId, CancellationToken ct);
    Task<IReadOnlyList<MarketingCreativeAssetScanDto>> ListCreativeAssetScansAsync(Guid companyId, Guid assetId, CancellationToken ct);
    Task<MarketingCreativeAssetScanDto?> RescanCreativeAssetAsync(Guid companyId, Guid userId, Guid assetId, CancellationToken ct);
    Task<MarketingCreativeAssetDto?> SubmitCreativeAssetAsync(Guid companyId, Guid assetId, CancellationToken ct);
    Task<MarketingCreativeAssetDto?> ReviewCreativeAssetAsync(Guid companyId, Guid assetId, bool approved, CancellationToken ct);
    Task<MarketingCreativeAssetDto?> RequestCreativeAssetChangesAsync(Guid companyId, Guid assetId, CancellationToken ct);
    Task<MarketingCreativeAssetDto?> UpdateCreativeAssetMetadataAsync(Guid companyId, Guid assetId,
        UpdateMarketingCreativeAssetMetadataRequest request, CancellationToken ct);
    Task<MarketingCreativeAssetDto?> RetireCreativeAssetAsync(Guid companyId, Guid assetId, CancellationToken ct);
    Task<IReadOnlyList<MarketingAttributionDto>> ListAttributionAsync(Guid companyId, CancellationToken ct);
    IReadOnlyList<MarketingMetricDefinitionDto> ListMetricCatalog();
    Task<MarketingAttributionDto> RecordAttributionAsync(Guid companyId, RecordMarketingAttributionRequest request, CancellationToken ct);
    Task<IReadOnlyList<MarketingEventTriggerDto>> ListEventsAsync(Guid companyId, CancellationToken ct);
    Task<MarketingEventTriggerDto> CreateEventAsync(Guid companyId, CreateMarketingEventTriggerRequest request, CancellationToken ct);
    Task<MarketingEventTriggerDto?> ProcessEventAsync(Guid companyId, Guid eventId, Guid marketingAgentId, CancellationToken ct);
    Task<MarketingEventTriggerDto?> ResolveEventAsync(Guid companyId, Guid eventId, CancellationToken ct);
}

public static class MarketingEventTypes
{
    public const string ObjectiveRisk = "objective_risk";
    public const string CampaignThreshold = "campaign_threshold";
    public const string ContentDue = "content_due";
    public const string ContentOverdue = "content_overdue";
    public const string StaleObservation = "stale_observation";
    public const string Qualification = "qualification";
    public const string SalesHandoffOutcome = "sales_handoff_outcome";
    public const string CampaignCompleted = "campaign_completed";
    public const string ExperimentThreshold = "experiment_threshold";
    public const string AudienceFatigue = "audience_fatigue";
    public const string ConsentIncident = "consent_incident";
    public const string BrandIncident = "brand_incident";
    public const string ProviderFailure = "provider_failure";
    public const string IntelligenceFreshness = "intelligence_freshness";
    public const string IntelligenceChange = "intelligence_change";
    public const string SegmentMaterialChange = "segment_material_change";
    public const string DownstreamArtifactStale = "downstream_artifact_stale";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ObjectiveRisk, CampaignThreshold, ContentDue, ContentOverdue, StaleObservation, Qualification,
        SalesHandoffOutcome, CampaignCompleted, ExperimentThreshold, AudienceFatigue, ConsentIncident,
        BrandIncident, ProviderFailure, IntelligenceFreshness, IntelligenceChange, SegmentMaterialChange,
        DownstreamArtifactStale
    };
}

public sealed record MarketingCreativeImageRequest(string Prompt, string Dimensions, string Quality, string OutputFormat);
public sealed record MarketingCreativeImageResult(byte[] Content, string ContentType, string ProviderModel,
    string ProviderRequestId, string GenerationSummary, string SafetyResult);
public interface IMarketingCreativeImageGenerator
{
    Task<MarketingCreativeImageResult> GenerateAsync(MarketingCreativeImageRequest request, CancellationToken ct);
}

public sealed record MarketingProviderValidationResult(bool Allowed, string ReasonCode, string Explanation,
    IReadOnlyList<string> Warnings);
public interface IMarketingChannelAdapter
{
    string Provider { get; }
    MarketingProviderValidationResult Validate(string actionType, string payloadJson, string capabilitiesJson);
}

public sealed record MarketingChannelDispatchResult(string Outcome, string? ProviderReference, string ReasonCode,
    string SafeExplanation, bool RequiresReauthorization = false);
public interface IMarketingChannelPublisher
{
    string Provider { get; }
    Task<MarketingChannelDispatchResult> PublishAsync(string destinationReference, string actionType,
        string payloadJson, string secretReference, CancellationToken ct);
    Task<MarketingChannelDispatchResult> ReconcileAsync(string destinationReference, string providerReference,
        string secretReference, CancellationToken ct);
}
public interface IMarketingChannelDispatchService
{
    Task<int> DispatchDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct);
    Task<MarketingChannelActionDto?> ReconcileAsync(Guid companyId, Guid actionId, CancellationToken ct);
}
public interface IMarketingJourneyExecutionService
{
    Task<int> ProcessDueAsync(DateTime nowUtc, int batchSize, CancellationToken ct);
}
