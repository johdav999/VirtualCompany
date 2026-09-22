namespace VirtualCompany.Application.Documents;

public sealed record ConfigureDocumentRepositoryConnectionCommand(
    string ProviderKind,
    Guid DirectoryTenantId,
    Guid ApplicationClientId,
    string CredentialReference,
    string DriveId,
    string RootItemId,
    string DisplayName,
    string Audience,
    IReadOnlyCollection<Guid>? AgentIds = null,
    long? ExpectedConcurrencyVersion = null,
    bool EnableWrites = false,
    string? WritableFolderItemId = null);

public sealed record DocumentRepositoryConnectionDto(
    Guid Id,
    Guid CompanyId,
    string ProviderKind,
    Guid DirectoryTenantId,
    Guid ApplicationClientId,
    string DriveId,
    string RootItemId,
    string DisplayName,
    bool IsReadOnly,
    string LifecycleState,
    string Audience,
    IReadOnlyList<Guid> AgentIds,
    string? LastValidationCode,
    string? LastValidationSummary,
    DateTime? LastValidatedUtc,
    DateTime? DisconnectedUtc,
    long ConcurrencyVersion,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? LastSynchronizedUtc = null,
    int IndexedDocumentCount = 0,
    int ProcessingDocumentCount = 0,
    int FailedDocumentCount = 0,
    Guid? LatestImportJobId = null,
    string? LatestImportStatus = null,
    Guid? LatestSynchronizationJobId = null,
    string? LatestSynchronizationStatus = null,
    string? WritableFolderItemId = null,
    Guid? LatestPublicationRequestId = null,
    string? LatestPublicationOperationKind = null,
    string? LatestPublicationStatus = null,
    string? LatestPublicationFailureMessage = null,
    DateTime? RetrievalPausedUtc = null,
    DateTime? SynchronizationPausedUtc = null,
    DateTime? WritesPausedUtc = null,
    double? OldestQueueAgeSeconds = null,
    int StaleLeaseCount = 0,
    int RetryableFailedItemCount = 0,
    int UnresolvedPublicationCount = 0,
    string DependencyHealth = "healthy",
    bool IsThrottled = false,
    bool FeatureEnabled = true,
    string CredentialMode = "customer_managed");

public sealed record BeginMicrosoft365OnboardingCommand(string ReturnPath);
public sealed record Microsoft365OnboardingStartDto(string SessionHandle, string AuthorizationUrl, DateTime ExpiresUtc);
public sealed record Microsoft365OnboardingStatusDto(
    string SessionHandle, string Status, Guid? ProviderTenantId, string ReturnPath,
    string? FailureCode, string? FailureSummary, DateTime CreatedUtc, DateTime ExpiresUtc,
    DateTime? CompletedUtc, long ConcurrencyVersion);
public sealed record Microsoft365AuthorizationCallbackCommand(
    string State, string? Code, string? Error, string? ErrorDescription);
public sealed record Microsoft365SourceKindDto(string Kind, string DisplayName, string Description, bool IsAvailable, string? UnavailableReason);
public sealed record Microsoft365SourceDto(string SelectionHandle, string Kind, string DisplayName, string? Context);
public sealed record Microsoft365SourcePageDto(IReadOnlyList<Microsoft365SourceDto> Items, string? NextPageHandle, bool IsTruncated);
public sealed record Microsoft365FolderBreadcrumbDto(string SelectionHandle, string DisplayName);
public sealed record Microsoft365FolderDto(string SelectionHandle, string DisplayName, DateTime? LastModifiedUtc);
public sealed record Microsoft365FolderPageDto(
    Microsoft365SourceDto Source, IReadOnlyList<Microsoft365FolderBreadcrumbDto> Breadcrumbs,
    IReadOnlyList<Microsoft365FolderDto> Items, string? NextPageHandle, bool IsTruncated);
public sealed record SelectMicrosoft365RepositoryRootCommand(string SourceHandle, string FolderHandle, long ExpectedConcurrencyVersion);
public sealed record Microsoft365RepositorySelectionDto(
    string SourceKind, string SourceDisplayName, string RootDisplayName, string? SourceContext,
    bool CanAssignSelectedApplicationPermission, string ApplicationPermission, string? UnavailableReason,
    long SelectionVersion);
public sealed record ConfigureMicrosoft365RepositoryAccessCommand(
    bool EnableWrites,
    string? OutputFolderHandle,
    IReadOnlyCollection<Guid>? AgentIds,
    long ExpectedConcurrencyVersion);
public sealed record Microsoft365RepositoryAccessDraftDto(
    bool EnableWrites,
    string? OutputFolderName,
    IReadOnlyList<Guid> AgentIds,
    long DraftVersion);
public sealed record Microsoft365RepositoryReviewAgentDto(Guid Id, string DisplayName);
public sealed record Microsoft365RepositoryReviewDto(
    string TenantDisplay,
    string SourceKind,
    string SourceName,
    string RootName,
    string AccessMode,
    string? OutputFolderName,
    IReadOnlyList<Microsoft365RepositoryReviewAgentDto> Agents,
    string Audience,
    string ImportBehavior,
    IReadOnlyList<string> PermissionChanges,
    long DraftVersion);
public sealed record FinalizeMicrosoft365RepositoryCommand(long ExpectedConcurrencyVersion, bool Confirmed);
public sealed record Microsoft365RepositoryProvisioningDto(
    Guid Id, string Status, Guid? ConnectionId, string? FailureCode, string? FailureSummary,
    bool CanRetry, bool CanCleanup, int AttemptCount, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);
public sealed record CleanupMicrosoft365RepositoryProvisioningCommand(bool Confirmed);

public interface IDocumentRepositoryMicrosoftOnboardingService
{
    Task<Microsoft365OnboardingStartDto> BeginAsync(Guid companyId, BeginMicrosoft365OnboardingCommand command, CancellationToken cancellationToken);
    Task<Microsoft365OnboardingStatusDto> CompleteCallbackAsync(Microsoft365AuthorizationCallbackCommand command, CancellationToken cancellationToken);
    Task<Microsoft365OnboardingStatusDto?> GetStatusAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken);
    Task<IReadOnlyList<Microsoft365SourceKindDto>> GetSourceKindsAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken);
    Task<Microsoft365SourcePageDto> GetOneDriveSourcesAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken);
    Task<Microsoft365SourcePageDto> SearchSharePointSitesAsync(Guid companyId, string sessionHandle, string query, string? pageHandle, int maxItems, CancellationToken cancellationToken);
    Task<Microsoft365SourcePageDto> GetSharePointLibrariesAsync(Guid companyId, string sessionHandle, string siteHandle, string? pageHandle, int maxItems, CancellationToken cancellationToken);
    Task<Microsoft365FolderPageDto> BrowseFoldersAsync(Guid companyId, string sessionHandle, string sourceHandle, string? folderHandle, string? pageHandle, int maxItems, CancellationToken cancellationToken);
    Task<Microsoft365RepositorySelectionDto> SelectRootAsync(Guid companyId, string sessionHandle, SelectMicrosoft365RepositoryRootCommand command, CancellationToken cancellationToken);
    Task<Microsoft365RepositoryAccessDraftDto> ConfigureAccessAsync(Guid companyId, string sessionHandle, ConfigureMicrosoft365RepositoryAccessCommand command, CancellationToken cancellationToken);
    Task<Microsoft365RepositoryReviewDto> GetReviewAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken);
    Task<Microsoft365RepositoryProvisioningDto> FinalizeAsync(Guid companyId, string sessionHandle, FinalizeMicrosoft365RepositoryCommand command, CancellationToken cancellationToken);
    Task<Microsoft365RepositoryProvisioningDto?> GetProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken);
    Task<Microsoft365RepositoryProvisioningDto> RetryProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken);
    Task<Microsoft365RepositoryProvisioningDto> CleanupProvisioningAsync(Guid companyId, string sessionHandle, CleanupMicrosoft365RepositoryProvisioningCommand command, CancellationToken cancellationToken);
    Task CancelAsync(Guid companyId, string sessionHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken);
    Task ExpirePendingAsync(CancellationToken cancellationToken);
}

public interface IDocumentRepositoryProvisioningProcessor
{
    Task ProcessPendingAsync(CancellationToken cancellationToken);
}

public sealed class DocumentRepositoryOnboardingException(string code, string safeMessage, bool retryable = false) : InvalidOperationException(safeMessage)
{
    public string Code { get; } = code;
    public string SafeMessage { get; } = safeMessage;
    public bool Retryable { get; } = retryable;
}

public sealed record SetDocumentRepositoryPauseCommand(string Scope, bool Paused, long ExpectedConcurrencyVersion);
public sealed record RetryDocumentRepositoryFailuresCommand(string IdempotencyKey);

public static class DocumentPublicationToolNames
{
    public const string PrepareCreate = "documents.prepare_create";
    public const string PrepareUpdate = "documents.prepare_update";
    public const string Create = "documents.create";
    public const string Update = "documents.update";
}

public sealed record PrepareDocumentPublicationCommand(
    Guid ConnectionId,
    string FileName,
    string? ContentType,
    string ContentBase64,
    string IdempotencyKey);

public sealed record PrepareDocumentUpdateCommand(
    Guid ConnectionId,
    string ItemId,
    string ExpectedRemoteVersion,
    string OriginalEvidenceVersion,
    string FileName,
    string? ContentType,
    string ContentBase64,
    string IdempotencyKey,
    Guid? StalePublicationRequestId = null);

public sealed record DocumentPublicationRequestDto(
    Guid Id, Guid CompanyId, Guid ConnectionId, string RepositoryName, string TargetFolderItemId,
    string FileName, string? ContentType, long SizeBytes, string ContentSha256, string Status,
    Guid RequestingAgentId, Guid? ApprovalRequestId, DateTime? ApprovalVersionUtc, string? ProviderItemId, string? ProviderVersion,
    string? SourceWebUrl, int AttemptCount, string? FailureCode, string? FailureMessage,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc,
    string OperationKind = "create", string? TargetItemId = null, string? ExpectedRemoteVersion = null,
    string? OriginalEvidenceVersion = null, string? OriginalContentSha256 = null, long? OriginalSizeBytes = null,
    Guid? StalePublicationRequestId = null, string? ConflictRemoteVersion = null);

public sealed record DocumentUpdateReviewDto(
    Guid PublicationRequestId,
    string FileName,
    string ExpectedRemoteVersion,
    string OriginalEvidenceVersion,
    string ReplacementSha256,
    bool HasTextDiff,
    string? UnifiedDiff,
    string Explanation);

public interface ICompanyDocumentPublicationService
{
    Task<DocumentPublicationRequestDto> PrepareAsync(Guid companyId, Guid agentId, string actorType, Guid actorId, PrepareDocumentPublicationCommand command, CancellationToken cancellationToken);
    Task<DocumentPublicationRequestDto> PrepareUpdateAsync(Guid companyId, Guid agentId, string actorType, Guid actorId, PrepareDocumentUpdateCommand command, CancellationToken cancellationToken);
    Task<DocumentPublicationRequestDto> QueueApprovedAsync(Guid companyId, Guid agentId, Guid executionId, Guid publicationRequestId, string expectedSha256, CancellationToken cancellationToken);
    Task<DocumentPublicationRequestDto?> GetAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken);
    Task<Stream> OpenStagedAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken);
    Task<Stream> OpenOriginalAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken);
    Task<DocumentUpdateReviewDto> GetUpdateReviewAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken);
    Task DispatchAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken);
    Task<DocumentPublicationRequestDto> RequestReconciliationAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken);
}

public sealed record DocumentPublicationDeliveryRequestedMessage(Guid CompanyId, Guid PublicationRequestId, string ContentSha256, string? CorrelationId);

public sealed record DocumentRepositoryValidationResult(
    bool IsAccessible,
    string Code,
    string RepositoryName,
    string RootName,
    string? SafeSummary);

public sealed record DocumentRepositoryBrowseItem(
    string ItemId,
    string Name,
    bool IsFolder,
    long? SizeBytes,
    DateTime? LastModifiedUtc);

public sealed record DocumentRepositoryBrowseResult(
    string ParentItemId,
    IReadOnlyList<DocumentRepositoryBrowseItem> Items,
    bool IsTruncated);

public sealed record StartDocumentRepositoryImportCommand(string IdempotencyKey);
public sealed record DocumentRepositoryImportItemDto(Guid Id, Guid? DocumentId, string Name, string Status, string? FailureCode, string? FailureMessage, bool CanRetry);
public sealed record DocumentRepositoryImportJobDto(Guid Id, Guid ConnectionId, string Status, int DiscoveredCount, int ProcessedCount, int FailedCount, string? FailureCode, string? FailureMessage, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc, IReadOnlyList<DocumentRepositoryImportItemDto> Items);

public interface ICompanyDocumentRepositoryImportService
{
    Task<DocumentRepositoryImportJobDto> StartAsync(Guid companyId, Guid connectionId, StartDocumentRepositoryImportCommand command, CancellationToken cancellationToken);
    Task<DocumentRepositoryImportJobDto?> GetAsync(Guid companyId, Guid connectionId, Guid jobId, CancellationToken cancellationToken);
    Task ProcessPendingAsync(CancellationToken cancellationToken);
}

public sealed record StartDocumentRepositorySynchronizationCommand(string IdempotencyKey, bool ForceFullReconciliation = false);

public sealed record DocumentRepositorySynchronizationJobDto(
    Guid Id,
    Guid ConnectionId,
    string Status,
    string Mode,
    int AttemptCount,
    int ObservedCount,
    int ChangedCount,
    int RemovedCount,
    int FailedCount,
    DateTime? NextRetryUtc,
    string? FailureCode,
    string? FailureMessage,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? CompletedUtc);

public interface ICompanyDocumentRepositorySynchronizationService
{
    Task<DocumentRepositorySynchronizationJobDto> StartAsync(Guid companyId, Guid connectionId, StartDocumentRepositorySynchronizationCommand command, CancellationToken cancellationToken);
    Task<DocumentRepositorySynchronizationJobDto?> GetAsync(Guid companyId, Guid connectionId, Guid jobId, CancellationToken cancellationToken);
    Task CancelAsync(Guid companyId, Guid connectionId, Guid jobId, CancellationToken cancellationToken);
    Task ProcessPendingAsync(CancellationToken cancellationToken);
    Task<DocumentRepositorySynchronizationJobDto> RetryFailedItemsAsync(Guid companyId, Guid connectionId, RetryDocumentRepositoryFailuresCommand command, CancellationToken cancellationToken);
}

public sealed record RemoteKnowledgeSourceAvailability(
    bool IsAvailable,
    string ReasonCode,
    string? CurrentRemoteVersion = null);

/// <summary>
/// The asynchronous, fail-closed boundary for exposing Microsoft 365 sourced knowledge.
/// It intentionally remains separate from the synchronous local access policy evaluator.
/// </summary>
public interface IRemoteKnowledgeSourceAvailabilityGate
{
    Task<RemoteKnowledgeSourceAvailability> CheckAsync(
        Guid companyId,
        Guid documentId,
        CompanyKnowledgeAccessContext accessContext,
        CancellationToken cancellationToken);
}

public interface ICompanyDocumentRepositoryService
{
    Task<DocumentRepositoryConnectionDto> CreateAsync(Guid companyId, ConfigureDocumentRepositoryConnectionCommand command, CancellationToken cancellationToken);
    Task<DocumentRepositoryConnectionDto> UpdateAsync(Guid companyId, Guid connectionId, ConfigureDocumentRepositoryConnectionCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<DocumentRepositoryConnectionDto>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    Task<DocumentRepositoryConnectionDto?> GetAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken);
    Task<DocumentRepositoryValidationResult> ValidateAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken);
    Task<DocumentRepositoryBrowseResult> BrowseAsync(Guid companyId, Guid connectionId, string? parentItemId, int maxItems, CancellationToken cancellationToken);
    Task DisconnectAsync(Guid companyId, Guid connectionId, long expectedConcurrencyVersion, CancellationToken cancellationToken);
    Task<DocumentRepositoryConnectionDto> SetPauseAsync(Guid companyId, Guid connectionId, SetDocumentRepositoryPauseCommand command, CancellationToken cancellationToken);
}

public sealed class DocumentRepositoryValidationException(string message) : ArgumentException(message);
public sealed class DocumentRepositoryNotFoundException() : KeyNotFoundException("The document repository connection was not found.");
public sealed class DocumentRepositoryConflictException(string message, string? currentRemoteVersion = null) : InvalidOperationException(message)
{
    public string? CurrentRemoteVersion { get; } = currentRemoteVersion;
}
public sealed class DocumentRepositoryAmbiguousWriteException(string message) : IOException(message);
public sealed class DocumentRepositoryUnavailableException(string code, string safeMessage) : InvalidOperationException(safeMessage)
{
    public string Code { get; } = code;
    public string SafeMessage { get; } = safeMessage;
    public TimeSpan? RetryAfter { get; set; }
}
