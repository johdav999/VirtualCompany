using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public interface IDocumentRepositoryApiClient
{
    Task<IReadOnlyList<DocumentRepositoryConnectionViewModel>?> ListAsync(Guid companyId, CancellationToken cancellationToken = default);
    Task<Microsoft365OnboardingStartViewModel> BeginMicrosoftOnboardingAsync(Guid companyId, string returnPath, CancellationToken cancellationToken = default);
    Task<Microsoft365OnboardingStatusViewModel> GetMicrosoftOnboardingStatusAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Microsoft365SourceKindViewModel>> GetMicrosoftSourceKindsAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default);
    Task<Microsoft365SourcePageViewModel> GetMicrosoftOneDriveSourcesAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default);
    Task<Microsoft365SourcePageViewModel> SearchMicrosoftSharePointSitesAsync(Guid companyId, string sessionHandle, string query, string? pageHandle = null, int maxItems = 25, CancellationToken cancellationToken = default);
    Task<Microsoft365SourcePageViewModel> GetMicrosoftSharePointLibrariesAsync(Guid companyId, string sessionHandle, string siteHandle, string? pageHandle = null, int maxItems = 25, CancellationToken cancellationToken = default);
    Task<Microsoft365FolderPageViewModel> BrowseMicrosoftFoldersAsync(Guid companyId, string sessionHandle, string sourceHandle, string? folderHandle = null, string? pageHandle = null, int maxItems = 50, CancellationToken cancellationToken = default);
    Task<Microsoft365RepositorySelectionViewModel> SelectMicrosoftRootAsync(Guid companyId, string sessionHandle, string sourceHandle, string folderHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken = default);
    Task CancelMicrosoftOnboardingAsync(Guid companyId, string sessionHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken = default);
    Task<Microsoft365RepositoryAccessDraftViewModel> ConfigureMicrosoftAccessAsync(Guid companyId, string sessionHandle, bool enableWrites, string? outputFolderHandle, IReadOnlyCollection<Guid> agentIds, long expectedConcurrencyVersion, CancellationToken cancellationToken = default);
    Task<Microsoft365RepositoryReviewViewModel> GetMicrosoftReviewAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default);
    Task<Microsoft365RepositoryProvisioningViewModel> FinalizeMicrosoftRepositoryAsync(Guid companyId, string sessionHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken = default);
    Task<Microsoft365RepositoryProvisioningViewModel?> GetMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default);
    Task<Microsoft365RepositoryProvisioningViewModel> RetryMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default);
    Task<Microsoft365RepositoryProvisioningViewModel> CleanupMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default);
    Task<DocumentRepositoryConnectionViewModel> SaveAsync(Guid companyId, Guid? connectionId, ConfigureDocumentRepositoryRequest request, CancellationToken cancellationToken = default);
    Task<DocumentRepositoryValidationViewModel> ValidateAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken = default);
    Task<DocumentRepositoryBrowseViewModel> BrowseAsync(Guid companyId, Guid connectionId, string? parentItemId, CancellationToken cancellationToken = default);
    Task<DocumentRepositoryImportJobViewModel> StartImportAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken = default);
    Task<DocumentRepositorySynchronizationJobViewModel> StartSynchronizationAsync(Guid companyId, Guid connectionId, bool forceFullReconciliation, CancellationToken cancellationToken = default);
    Task<DocumentRepositoryConnectionViewModel> SetPauseAsync(Guid companyId, Guid connectionId, string scope, bool paused, long expectedConcurrencyVersion, CancellationToken cancellationToken = default);
    Task<DocumentRepositorySynchronizationJobViewModel> RetryFailedItemsAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken = default);
    Task ReconcilePublicationAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(Guid companyId, Guid connectionId, long expectedConcurrencyVersion, CancellationToken cancellationToken = default);
}

public sealed class DocumentRepositoryApiClient(
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver problemResolver) : IDocumentRepositoryApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task<Microsoft365OnboardingStartViewModel> BeginMicrosoftOnboardingAsync(Guid companyId, string returnPath, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365OnboardingStartViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding", new { returnPath }, cancellationToken);

    public Task<Microsoft365OnboardingStatusViewModel> GetMicrosoftOnboardingStatusAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365OnboardingStatusViewModel>(companyId, HttpMethod.Get, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}", null, cancellationToken);

    public async Task<IReadOnlyList<Microsoft365SourceKindViewModel>> GetMicrosoftSourceKindsAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) =>
        await SendRequiredAsync<List<Microsoft365SourceKindViewModel>>(companyId, HttpMethod.Get, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/source-kinds", null, cancellationToken);

    public Task<Microsoft365SourcePageViewModel> GetMicrosoftOneDriveSourcesAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365SourcePageViewModel>(companyId, HttpMethod.Get, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/sources/onedrive", null, cancellationToken);

    public Task<Microsoft365SourcePageViewModel> SearchMicrosoftSharePointSitesAsync(Guid companyId, string sessionHandle, string query, string? pageHandle = null, int maxItems = 25, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365SourcePageViewModel>(companyId, HttpMethod.Get, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/sources/sharepoint/sites?query={Uri.EscapeDataString(query)}&maxItems={maxItems}{Optional("pageHandle", pageHandle)}", null, cancellationToken);

    public Task<Microsoft365SourcePageViewModel> GetMicrosoftSharePointLibrariesAsync(Guid companyId, string sessionHandle, string siteHandle, string? pageHandle = null, int maxItems = 25, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365SourcePageViewModel>(companyId, HttpMethod.Get, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/sources/sharepoint/libraries?siteHandle={Uri.EscapeDataString(siteHandle)}&maxItems={maxItems}{Optional("pageHandle", pageHandle)}", null, cancellationToken);

    public Task<Microsoft365FolderPageViewModel> BrowseMicrosoftFoldersAsync(Guid companyId, string sessionHandle, string sourceHandle, string? folderHandle = null, string? pageHandle = null, int maxItems = 50, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365FolderPageViewModel>(companyId, HttpMethod.Get, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/folders?sourceHandle={Uri.EscapeDataString(sourceHandle)}&maxItems={maxItems}{Optional("folderHandle", folderHandle)}{Optional("pageHandle", pageHandle)}", null, cancellationToken);

    public Task<Microsoft365RepositorySelectionViewModel> SelectMicrosoftRootAsync(Guid companyId, string sessionHandle, string sourceHandle, string folderHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365RepositorySelectionViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/selection", new { sourceHandle, folderHandle, expectedConcurrencyVersion }, cancellationToken);

    public async Task CancelMicrosoftOnboardingAsync(Guid companyId, string sessionHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken = default)
    {
        EnsureAvailable(companyId);
        using var content = JsonContent.Create(new { expectedConcurrencyVersion }, options: JsonOptions);
        using var response = await transport.SendAsync(companyId, HttpMethod.Post, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/cancel", content, cancellationToken);
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
    }

    public Task<Microsoft365RepositoryAccessDraftViewModel> ConfigureMicrosoftAccessAsync(Guid companyId, string sessionHandle, bool enableWrites, string? outputFolderHandle, IReadOnlyCollection<Guid> agentIds, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365RepositoryAccessDraftViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/access", new { enableWrites, outputFolderHandle, agentIds, expectedConcurrencyVersion }, cancellationToken);

    public Task<Microsoft365RepositoryReviewViewModel> GetMicrosoftReviewAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365RepositoryReviewViewModel>(companyId, HttpMethod.Get, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/review", null, cancellationToken);

    public Task<Microsoft365RepositoryProvisioningViewModel> FinalizeMicrosoftRepositoryAsync(Guid companyId, string sessionHandle, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365RepositoryProvisioningViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/finalize", new { expectedConcurrencyVersion, confirmed = true }, cancellationToken);

    public Task<Microsoft365RepositoryProvisioningViewModel?> GetMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) =>
        SendAsync<Microsoft365RepositoryProvisioningViewModel>(companyId, HttpMethod.Get, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/provisioning", null, false, cancellationToken);

    public Task<Microsoft365RepositoryProvisioningViewModel> RetryMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365RepositoryProvisioningViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/provisioning/retry", new { }, cancellationToken);

    public Task<Microsoft365RepositoryProvisioningViewModel> CleanupMicrosoftProvisioningAsync(Guid companyId, string sessionHandle, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<Microsoft365RepositoryProvisioningViewModel>(companyId, HttpMethod.Post, $"api/companies/{companyId:D}/document-repositories/microsoft/onboarding/{Uri.EscapeDataString(sessionHandle)}/provisioning/cleanup", new { confirmed = true }, cancellationToken);

    private static string Optional(string name, string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : $"&{name}={Uri.EscapeDataString(value)}";
    public Task<IReadOnlyList<DocumentRepositoryConnectionViewModel>?> ListAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<DocumentRepositoryConnectionViewModel>>(companyId, HttpMethod.Get,
            $"api/companies/{companyId:D}/document-repositories", null, allowForbidden: true, cancellationToken);

    public Task<DocumentRepositoryConnectionViewModel> SaveAsync(Guid companyId, Guid? connectionId,
        ConfigureDocumentRepositoryRequest request, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<DocumentRepositoryConnectionViewModel>(companyId,
            connectionId.HasValue ? HttpMethod.Put : HttpMethod.Post,
            connectionId.HasValue
                ? $"api/companies/{companyId:D}/document-repositories/{connectionId:D}"
                : $"api/companies/{companyId:D}/document-repositories",
            request, cancellationToken);

    public Task<DocumentRepositoryValidationViewModel> ValidateAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<DocumentRepositoryValidationViewModel>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/document-repositories/{connectionId:D}/validate", new { }, cancellationToken);

    public Task<DocumentRepositoryBrowseViewModel> BrowseAsync(Guid companyId, Guid connectionId, string? parentItemId, CancellationToken cancellationToken = default)
    {
        var query = string.IsNullOrWhiteSpace(parentItemId) ? string.Empty : $"?parentItemId={Uri.EscapeDataString(parentItemId)}";
        return SendRequiredAsync<DocumentRepositoryBrowseViewModel>(companyId, HttpMethod.Get,
            $"api/companies/{companyId:D}/document-repositories/{connectionId:D}/browse{query}", null, cancellationToken);
    }

    public Task<DocumentRepositoryImportJobViewModel> StartImportAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<DocumentRepositoryImportJobViewModel>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/document-repositories/{connectionId:D}/imports",
            new { idempotencyKey = $"ui-import-{connectionId:N}-{DateTime.UtcNow:yyyyMMddHHmm}" }, cancellationToken);

    public Task<DocumentRepositorySynchronizationJobViewModel> StartSynchronizationAsync(Guid companyId, Guid connectionId,
        bool forceFullReconciliation, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<DocumentRepositorySynchronizationJobViewModel>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/document-repositories/{connectionId:D}/synchronizations",
            new { idempotencyKey = $"ui-sync-{connectionId:N}-{DateTime.UtcNow:yyyyMMddHHmm}", forceFullReconciliation }, cancellationToken);

    public Task<DocumentRepositoryConnectionViewModel> SetPauseAsync(Guid companyId, Guid connectionId, string scope,
        bool paused, long expectedConcurrencyVersion, CancellationToken cancellationToken = default) =>
        SendRequiredAsync<DocumentRepositoryConnectionViewModel>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/document-repositories/{connectionId:D}/pause",
            new { scope, paused, expectedConcurrencyVersion }, cancellationToken);

    public Task<DocumentRepositorySynchronizationJobViewModel> RetryFailedItemsAsync(Guid companyId, Guid connectionId,
        CancellationToken cancellationToken = default) =>
        SendRequiredAsync<DocumentRepositorySynchronizationJobViewModel>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/document-repositories/{connectionId:D}/recovery/retry-failed-items",
            new { idempotencyKey = $"ui-retry-failed-{connectionId:N}-{DateTime.UtcNow:yyyyMMddHHmm}" }, cancellationToken);

    public async Task ReconcilePublicationAsync(Guid companyId, Guid publicationRequestId, CancellationToken cancellationToken = default)
    {
        _ = await SendRequiredAsync<DocumentPublicationRecoveryViewModel>(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/document-repositories/publications/{publicationRequestId:D}/reconcile",
            new { }, cancellationToken);
    }

    public async Task DisconnectAsync(Guid companyId, Guid connectionId, long expectedConcurrencyVersion, CancellationToken cancellationToken = default)
    {
        EnsureAvailable(companyId);
        using var content = JsonContent.Create(new { expectedConcurrencyVersion }, options: JsonOptions);
        using var response = await transport.SendAsync(companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/document-repositories/{connectionId:D}/disconnect", content, cancellationToken);
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
    }

    private async Task<T> SendRequiredAsync<T>(Guid companyId, HttpMethod method, string uri, object? payload,
        CancellationToken cancellationToken) where T : class =>
        await SendAsync<T>(companyId, method, uri, payload, false, cancellationToken)
        ?? throw new DocumentRepositoryApiException("The server returned an empty document repository response.");

    private async Task<T?> SendAsync<T>(Guid companyId, HttpMethod method, string uri, object? payload,
        bool allowForbidden, CancellationToken cancellationToken) where T : class
    {
        EnsureAvailable(companyId);
        try
        {
            using var content = payload is null ? null : JsonContent.Create(payload, options: JsonOptions);
            using var response = await transport.SendAsync(companyId, method, uri, content, cancellationToken);
            if (allowForbidden && response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) return null;
            if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
            return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        }
        catch (HttpRequestException)
        {
            var endpoint = transport.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
            throw new DocumentRepositoryApiException($"The web app could not reach the backend API at {endpoint}.");
        }
    }

    private void EnsureAvailable(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("A company is required.", nameof(companyId));
        if (useOfflineMode) throw new DocumentRepositoryApiException("Document repositories require the backend API.");
    }

    private async Task<DocumentRepositoryApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ApiProblemResponse? problem = null;
        try { problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(JsonOptions, cancellationToken); }
        catch (JsonException) { }
        var fallback = response.StatusCode switch
        {
            HttpStatusCode.Conflict => "This repository changed. Refresh and try again.",
            HttpStatusCode.Forbidden => "You do not have permission to manage document repositories.",
            _ => "The document repository request could not be completed."
        };
        return new DocumentRepositoryApiException(problemResolver.Resolve(problem, fallback), problem?.Errors, response.StatusCode);
    }
}

public sealed class DocumentRepositoryApiException : Exception
{
    public DocumentRepositoryApiException(string message, IReadOnlyDictionary<string, string[]>? errors = null, HttpStatusCode? statusCode = null) : base(message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
        StatusCode = statusCode;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public HttpStatusCode? StatusCode { get; }
}

public sealed class ConfigureDocumentRepositoryRequest
{
    public string ProviderKind { get; set; } = "sharepoint_library";
    public Guid DirectoryTenantId { get; set; }
    public Guid ApplicationClientId { get; set; }
    public string CredentialReference { get; set; } = string.Empty;
    public string DriveId { get; set; } = string.Empty;
    public string RootItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Audience { get; set; } = "company";
    public List<Guid> AgentIds { get; set; } = [];
    public long? ExpectedConcurrencyVersion { get; set; }
    public bool EnableWrites { get; set; }
    public string? WritableFolderItemId { get; set; }
}

public sealed class DocumentRepositoryConnectionViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string ProviderKind { get; set; } = string.Empty;
    public Guid DirectoryTenantId { get; set; }
    public Guid ApplicationClientId { get; set; }
    public string DriveId { get; set; } = string.Empty;
    public string RootItemId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsReadOnly { get; set; }
    public string? WritableFolderItemId { get; set; }
    public string LifecycleState { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public List<Guid> AgentIds { get; set; } = [];
    public string? LastValidationCode { get; set; }
    public string? LastValidationSummary { get; set; }
    public DateTime? LastValidatedUtc { get; set; }
    public DateTime? DisconnectedUtc { get; set; }
    public long ConcurrencyVersion { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public DateTime? LastSynchronizedUtc { get; set; }
    public int IndexedDocumentCount { get; set; }
    public int ProcessingDocumentCount { get; set; }
    public int FailedDocumentCount { get; set; }
    public Guid? LatestImportJobId { get; set; }
    public string? LatestImportStatus { get; set; }
    public Guid? LatestSynchronizationJobId { get; set; }
    public string? LatestSynchronizationStatus { get; set; }
    public Guid? LatestPublicationRequestId { get; set; }
    public string? LatestPublicationOperationKind { get; set; }
    public string? LatestPublicationStatus { get; set; }
    public string? LatestPublicationFailureMessage { get; set; }
    public DateTime? RetrievalPausedUtc { get; set; }
    public DateTime? SynchronizationPausedUtc { get; set; }
    public DateTime? WritesPausedUtc { get; set; }
    public double? OldestQueueAgeSeconds { get; set; }
    public int StaleLeaseCount { get; set; }
    public int RetryableFailedItemCount { get; set; }
    public int UnresolvedPublicationCount { get; set; }
    public string DependencyHealth { get; set; } = "healthy";
    public bool IsThrottled { get; set; }
    public bool FeatureEnabled { get; set; } = true;
    public string CredentialMode { get; set; } = "customer_managed";
}

public sealed record Microsoft365OnboardingStartViewModel(string SessionHandle, string AuthorizationUrl, DateTime ExpiresUtc);
public sealed record Microsoft365OnboardingStatusViewModel(string SessionHandle, string Status, Guid? ProviderTenantId, string ReturnPath, string? FailureCode, string? FailureSummary, DateTime CreatedUtc, DateTime ExpiresUtc, DateTime? CompletedUtc, long ConcurrencyVersion);
public sealed record Microsoft365SourceKindViewModel(string Kind, string DisplayName, string Description, bool IsAvailable, string? UnavailableReason);
public sealed record Microsoft365SourceViewModel(string SelectionHandle, string Kind, string DisplayName, string? Context);
public sealed record Microsoft365SourcePageViewModel(IReadOnlyList<Microsoft365SourceViewModel> Items, string? NextPageHandle, bool IsTruncated);
public sealed record Microsoft365FolderBreadcrumbViewModel(string SelectionHandle, string DisplayName);
public sealed record Microsoft365FolderViewModel(string SelectionHandle, string DisplayName, DateTime? LastModifiedUtc);
public sealed record Microsoft365FolderPageViewModel(Microsoft365SourceViewModel Source, IReadOnlyList<Microsoft365FolderBreadcrumbViewModel> Breadcrumbs, IReadOnlyList<Microsoft365FolderViewModel> Items, string? NextPageHandle, bool IsTruncated);
public sealed record Microsoft365RepositorySelectionViewModel(string SourceKind, string SourceDisplayName, string RootDisplayName, string? SourceContext, bool CanAssignSelectedApplicationPermission, string ApplicationPermission, string? UnavailableReason, long SelectionVersion);
public sealed record Microsoft365RepositoryAccessDraftViewModel(bool EnableWrites, string? OutputFolderName, IReadOnlyList<Guid> AgentIds, long DraftVersion);
public sealed record Microsoft365RepositoryReviewAgentViewModel(Guid Id, string DisplayName);
public sealed record Microsoft365RepositoryReviewViewModel(string TenantDisplay, string SourceKind, string SourceName, string RootName, string AccessMode, string? OutputFolderName, IReadOnlyList<Microsoft365RepositoryReviewAgentViewModel> Agents, string Audience, string ImportBehavior, IReadOnlyList<string> PermissionChanges, long DraftVersion);
public sealed record Microsoft365RepositoryProvisioningViewModel(Guid Id, string Status, Guid? ConnectionId, string? FailureCode, string? FailureSummary, bool CanRetry, bool CanCleanup, int AttemptCount, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);

public sealed record DocumentPublicationRecoveryViewModel(Guid Id, string Status);

public sealed record DocumentRepositoryValidationViewModel(bool IsAccessible, string Code, string RepositoryName, string RootName, string? SafeSummary);
public sealed record DocumentRepositoryBrowseItemViewModel(string ItemId, string Name, bool IsFolder, long? SizeBytes, DateTime? LastModifiedUtc);
public sealed record DocumentRepositoryBrowseViewModel(string ParentItemId, IReadOnlyList<DocumentRepositoryBrowseItemViewModel> Items, bool IsTruncated);
public sealed record DocumentRepositoryImportItemViewModel(Guid Id, Guid? DocumentId, string Name, string Status, string? FailureCode, string? FailureMessage, bool CanRetry);
public sealed record DocumentRepositoryImportJobViewModel(Guid Id, Guid ConnectionId, string Status, int DiscoveredCount, int ProcessedCount, int FailedCount, string? FailureCode, string? FailureMessage, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc, IReadOnlyList<DocumentRepositoryImportItemViewModel> Items);
public sealed record DocumentRepositorySynchronizationJobViewModel(Guid Id, Guid ConnectionId, string Status, string Mode, int AttemptCount, int ObservedCount, int ChangedCount, int RemovedCount, int FailedCount, DateTime? NextRetryUtc, string? FailureCode, string? FailureMessage, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);
