namespace VirtualCompany.Application.Sales;

public static class SalesPresentationPresetProblemCodes
{
    public const string InvalidRequest = "sales.presentation_preset.invalid_request";
    public const string Conflict = "sales.presentation_preset.conflict";
    public const string ImmutableVersion = "sales.presentation_preset.immutable_version";
    public const string NotReady = "sales.presentation_preset.not_ready";
    public const string Archived = "sales.presentation_preset.archived";
}

public static class SalesPresentationPresetBlockerCodes
{
    public const string AssetMissing = "asset_missing"; public const string AssetProcessing = "asset_processing";
    public const string AssetFailed = "asset_failed"; public const string SlidesMissing = "slides_missing";
    public const string PresenterInvalid = "presenter_invalid"; public const string DurationInvalid = "duration_invalid";
    public const string ContextUnsupported = "context_unsupported"; public const string KnowledgeScopeUnavailable = "knowledge_scope_unavailable";
}

public static class SalesPresentationPresetActions
{
    public const string UploadAsset = "upload_asset"; public const string RetryProcessing = "retry_processing";
    public const string ChoosePresenter = "choose_presenter"; public const string UpdateDefaults = "update_defaults";
    public const string Publish = "publish"; public const string CreateDraft = "create_draft"; public const string Archive = "archive";
}

public sealed class SalesPresentationPresetValidationException(IDictionary<string, string[]> errors) : Exception("Presentation preset validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase);
}
public sealed class SalesPresentationPresetConflictException(string code, string message) : Exception(message) { public string Code { get; } = code; }

public sealed record CreateSalesPresentationPresetCommand(string Name, string? Description, Guid OwnerUserId,
    Guid? DefaultPresenterAgentId, string Goal, string Audience, int DurationMinutes, string? DemoScenario,
    string ControlMode, string Language, IReadOnlyCollection<string> AllowedContextTypes, string? RequiredKnowledgeScope,
    string? BehaviorSettingsJson);
public sealed record UpdateSalesPresentationPresetDraftCommand(long ExpectedPresetVersion, long ExpectedDraftVersion,
    string Name, string? Description, Guid OwnerUserId, Guid? DefaultPresenterAgentId, string Goal, string Audience,
    int DurationMinutes, string? DemoScenario, string ControlMode, string Language,
    IReadOnlyCollection<string> AllowedContextTypes, string? RequiredKnowledgeScope, string? BehaviorSettingsJson);
public sealed record ImportSalesPresentationPresetAssetCommand(string OriginalFileName, string? ContentType, long Length, Stream Content);

public sealed record SalesPresentationPresetListItemDto(Guid Id, string Name, string? Description, Guid OwnerUserId,
    string Lifecycle, int? CurrentPublishedVersion, int? CurrentDraftVersion, string? AssetStatus, int SlideCount,
    int WhereUsedCount, DateTime UpdatedUtc, long ConcurrencyVersion, Guid? CoverSlideId = null);
public sealed record SalesPresentationPresetAssetDto(Guid Id, string OriginalFileName, string? ContentType, long FileSizeBytes,
    string ContentHash, string Status, int ProcessingVersion, int ProcessingAttemptCount, int SlideCount,
    string? RendererName, string? RendererVersion, string? AnimationHandling, string? FailureCode,
    string? FailureSummary, bool CanRetry, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ProcessedUtc, long ConcurrencyVersion);
public sealed record SalesPresentationPresetSlideDto(Guid Id, int SlideNumber, string? Title, string ExtractedText,
    string? SpeakerNotes, string ImageStorageKey, string? ImageStorageUrl, int ImageWidthPixels, int ImageHeightPixels,
    long SourceWidthEmus, long SourceHeightEmus, string ContentHash, string Objective, string BaselineTalkingPoints,
    int ExpectedDurationSeconds, string TransitionText, bool AudioNeedsUpdate = false);
public sealed record SalesPresentationPresetVersionDto(Guid Id, int VersionNumber, string Lifecycle,
    Guid? DefaultPresenterAgentId, string? BehaviorSettingsJson, string Goal, string Audience, int DurationMinutes,
    string? DemoScenario, string ControlMode, string Language, IReadOnlyList<string> AllowedContextTypes,
    string? RequiredKnowledgeScope, Guid? PublishedByUserId, DateTime? PublishedUtc, DateTime CreatedUtc,
    DateTime UpdatedUtc, long ConcurrencyVersion, SalesPresentationPresetAssetDto? Asset);
public sealed record SalesPresentationPresetDto(Guid Id, Guid CompanyId, string Name, string? Description,
    Guid OwnerUserId, string Lifecycle, Guid? CurrentPublishedVersionId, DateTime CreatedUtc, DateTime UpdatedUtc,
    DateTime? ArchivedUtc, long ConcurrencyVersion, int WhereUsedCount, SalesPresentationPresetVersionDto? CurrentDraft,
    SalesPresentationPresetVersionDto? CurrentPublished, IReadOnlyList<SalesPresentationPresetVersionDto> Versions,
    IReadOnlyList<string> AllowedActions);
public sealed record SalesPresentationPresetReadinessBlockerDto(string Code, string Explanation,
    IReadOnlyList<string> CorrectiveActions, bool RequiresReview);
public sealed record SalesPresentationPresetReadinessDto(string State, bool IsReady,
    IReadOnlyList<SalesPresentationPresetReadinessBlockerDto> Blockers, IReadOnlyList<string> AllowedActions,
    bool RequiresReview);

public sealed record SalesPresentationPresetCover(byte[] Content, string ContentType);

public interface ISalesPresentationPresetService
{
    Task<SalesPresentationPresetDto?> UpdateSpeakerNotesAsync(Guid companyId, Guid actorUserId, Guid presetId, Guid versionId, Guid slideId, long expectedVersion, string? notes, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetCover?> GetCoverAsync(Guid companyId, Guid actorUserId, Guid presetId, Guid slideId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetCover?> GetSlideImageAsync(Guid companyId, Guid actorUserId, Guid presetId, Guid versionId, Guid slideId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesPresentationPresetListItemDto>> ListAsync(Guid companyId, Guid actorUserId, string? search, bool includeArchived, CancellationToken cancellationToken);
    Task<SalesPresentationPresetDto?> GetAsync(Guid companyId, Guid actorUserId, Guid presetId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetDto> CreateAsync(Guid companyId, Guid actorUserId, CreateSalesPresentationPresetCommand command, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetDto?> UpdateDraftAsync(Guid companyId, Guid actorUserId, Guid presetId, UpdateSalesPresentationPresetDraftCommand command, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetAssetDto?> ImportAssetAsync(Guid companyId, Guid actorUserId, Guid presetId, Guid versionId, ImportSalesPresentationPresetAssetCommand command, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetAssetDto?> RetryAssetAsync(Guid companyId, Guid actorUserId, Guid presetId, Guid versionId, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetReadinessDto?> EvaluateReadinessAsync(Guid companyId, Guid actorUserId, Guid presetId, Guid versionId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetDto?> PublishAsync(Guid companyId, Guid actorUserId, Guid presetId, Guid versionId, long expectedPresetVersion, long expectedVersion, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetDto?> CreateNextDraftAsync(Guid companyId, Guid actorUserId, Guid presetId, long expectedPresetVersion, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationPresetDto?> ArchiveAsync(Guid companyId, Guid actorUserId, Guid presetId, long expectedPresetVersion, string? rationale, string? correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesPresentationPresetSlideDto>?> GetSlidesAsync(Guid companyId, Guid actorUserId, Guid presetId, Guid versionId, CancellationToken cancellationToken);
}

public interface ISalesPresentationPresetAssetProcessor
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
    Task ProcessAsync(Guid companyId, Guid assetId, CancellationToken cancellationToken);
}
