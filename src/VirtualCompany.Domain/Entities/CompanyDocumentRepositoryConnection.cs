namespace VirtualCompany.Domain.Entities;

public sealed class CompanyDocumentRepositoryConnection : ICompanyOwnedEntity
{
    private CompanyDocumentRepositoryConnection() { }

    public CompanyDocumentRepositoryConnection(
        Guid companyId,
        string providerKind,
        Guid directoryTenantId,
        Guid applicationClientId,
        string credentialReference,
        string driveId,
        string rootItemId,
        string displayName,
        string audience,
        DateTime createdUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = Require(companyId, nameof(companyId));
        ProviderKind = DocumentRepositoryProviderKinds.Normalize(providerKind);
        DirectoryTenantId = Require(directoryTenantId, nameof(directoryTenantId));
        ApplicationClientId = Require(applicationClientId, nameof(applicationClientId));
        CredentialReference = Normalize(credentialReference, nameof(credentialReference), 256);
        CredentialMode = DocumentRepositoryCredentialModes.CustomerManaged;
        DriveId = Normalize(driveId, nameof(driveId), 160);
        RootItemId = Normalize(rootItemId, nameof(rootItemId), 160);
        DisplayName = Normalize(displayName, nameof(displayName), 200);
        Audience = DocumentRepositoryAudiences.Normalize(audience);
        IsReadOnly = true;
        LifecycleState = DocumentRepositoryLifecycleStates.PendingValidation;
        ConcurrencyVersion = 1;
        CreatedUtc = NormalizeUtc(createdUtc);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ProviderKind { get; private set; } = null!;
    public Guid DirectoryTenantId { get; private set; }
    public Guid ApplicationClientId { get; private set; }
    public string CredentialReference { get; private set; } = null!;
    public string CredentialMode { get; private set; } = DocumentRepositoryCredentialModes.CustomerManaged;
    public string DriveId { get; private set; } = null!;
    public string RootItemId { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public bool IsReadOnly { get; private set; }
    public string? WritableFolderItemId { get; private set; }
    public string LifecycleState { get; private set; } = null!;
    public string Audience { get; private set; } = null!;
    public string? LastValidationCode { get; private set; }
    public string? LastValidationSummary { get; private set; }
    public DateTime? LastValidatedUtc { get; private set; }
    public DateTime? DisconnectedUtc { get; private set; }
    public string? SynchronizationCursor { get; private set; }
    public string? SynchronizationMode { get; private set; }
    public DateTime? LastSynchronizedUtc { get; private set; }
    public DateTime? RetrievalPausedUtc { get; private set; }
    public DateTime? SynchronizationPausedUtc { get; private set; }
    public DateTime? WritesPausedUtc { get; private set; }
    public long ConcurrencyVersion { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<CompanyDocumentRepositoryAgentGrant> AgentGrants { get; } = new List<CompanyDocumentRepositoryAgentGrant>();

    public void Reconfigure(
        string providerKind,
        Guid directoryTenantId,
        Guid applicationClientId,
        string credentialReference,
        string driveId,
        string rootItemId,
        string displayName,
        string audience,
        DateTime updatedUtc)
    {
        EnsureConnected();
        ProviderKind = DocumentRepositoryProviderKinds.Normalize(providerKind);
        DirectoryTenantId = Require(directoryTenantId, nameof(directoryTenantId));
        ApplicationClientId = Require(applicationClientId, nameof(applicationClientId));
        CredentialReference = Normalize(credentialReference, nameof(credentialReference), 256);
        DriveId = Normalize(driveId, nameof(driveId), 160);
        RootItemId = Normalize(rootItemId, nameof(rootItemId), 160);
        DisplayName = Normalize(displayName, nameof(displayName), 200);
        Audience = DocumentRepositoryAudiences.Normalize(audience);
        LifecycleState = DocumentRepositoryLifecycleStates.PendingValidation;
        LastValidationCode = null;
        LastValidationSummary = null;
        LastValidatedUtc = null;
        SynchronizationCursor = null;
        SynchronizationMode = null;
        LastSynchronizedUtc = null;
        Touch(updatedUtc);
    }

    public void ConfigureWrites(bool enableWrites, string? writableFolderItemId, DateTime updatedUtc)
    {
        EnsureConnected();
        var normalizedFolderId = enableWrites
            ? Normalize(writableFolderItemId ?? string.Empty, nameof(writableFolderItemId), 160)
            : null;
        if (IsReadOnly == !enableWrites && string.Equals(WritableFolderItemId, normalizedFolderId, StringComparison.Ordinal))
            return;

        if (enableWrites)
        {
            WritableFolderItemId = normalizedFolderId;
            IsReadOnly = false;
        }
        else
        {
            WritableFolderItemId = null;
            IsReadOnly = true;
        }

        Touch(updatedUtc);
    }

    public void UsePlatformManagedCredential(Guid applicationClientId, DateTime updatedUtc)
    {
        EnsureConnected();
        ApplicationClientId = Require(applicationClientId, nameof(applicationClientId));
        CredentialMode = DocumentRepositoryCredentialModes.PlatformManaged;
        CredentialReference = DocumentRepositoryCredentialModes.PlatformManagedReference;
        Touch(updatedUtc);
    }

    public void MarkValidated(string repositoryName, DateTime validatedUtc)
    {

        EnsureConnected();
        DisplayName = Normalize(repositoryName, nameof(repositoryName), 200);
        LifecycleState = DocumentRepositoryLifecycleStates.Active;
        LastValidationCode = DocumentRepositoryValidationCodes.Succeeded;
        LastValidationSummary = null;
        LastValidatedUtc = NormalizeUtc(validatedUtc);
        Touch(validatedUtc);
    }

    public void MarkValidationFailed(string code, string safeSummary, DateTime validatedUtc)
    {
        EnsureConnected();
        LifecycleState = DocumentRepositoryLifecycleStates.Unavailable;
        LastValidationCode = Normalize(code, nameof(code), 64).ToLowerInvariant();
        LastValidationSummary = Normalize(safeSummary, nameof(safeSummary), 500);
        LastValidatedUtc = NormalizeUtc(validatedUtc);
        Touch(validatedUtc);
    }

    public void Disconnect(DateTime disconnectedUtc)
    {
        if (LifecycleState == DocumentRepositoryLifecycleStates.Disconnected) return;
        LifecycleState = DocumentRepositoryLifecycleStates.Disconnected;
        DisconnectedUtc = NormalizeUtc(disconnectedUtc);
        Touch(disconnectedUtc);
    }

    public void RecordSynchronization(string mode, string? cursor, DateTime synchronizedUtc)
    {
        EnsureConnected();
        SynchronizationMode = Normalize(mode, nameof(mode), 32);
        SynchronizationCursor = string.IsNullOrWhiteSpace(cursor) ? null : Normalize(cursor, nameof(cursor), 4096);
        LastSynchronizedUtc = NormalizeUtc(synchronizedUtc);
        Touch(synchronizedUtc);
    }

    public void ResetSynchronization(DateTime updatedUtc)
    {
        SynchronizationCursor = null;
        SynchronizationMode = null;
        LastSynchronizedUtc = null;
        Touch(updatedUtc);
    }

    public void SetOperationalPause(string scope, bool paused, DateTime updatedUtc)
    {
        EnsureConnected();
        var value = paused ? NormalizeUtc(updatedUtc) : (DateTime?)null;
        switch (DocumentRepositoryPauseScopes.Normalize(scope))
        {
            case DocumentRepositoryPauseScopes.Retrieval: RetrievalPausedUtc = value; break;
            case DocumentRepositoryPauseScopes.Synchronization: SynchronizationPausedUtc = value; break;
            case DocumentRepositoryPauseScopes.Writes: WritesPausedUtc = value; break;
        }
        Touch(updatedUtc);
    }

    private void EnsureConnected()
    {
        if (LifecycleState == DocumentRepositoryLifecycleStates.Disconnected)
            throw new InvalidOperationException("A disconnected document repository connection cannot be changed or used.");
    }

    private void Touch(DateTime value)
    {
        UpdatedUtc = NormalizeUtc(value);
        ConcurrencyVersion++;
    }

    private static Guid Require(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Normalize(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        return normalized;
    }
}

public static class DocumentRepositoryCredentialModes
{
    public const string CustomerManaged = "customer_managed";
    public const string PlatformManaged = "platform_managed";
    public const string PlatformManagedReference = "platform-managed";
    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        CustomerManaged => CustomerManaged,
        PlatformManaged => PlatformManaged,
        _ => throw new ArgumentException("Credential mode must be customer_managed or platform_managed.", nameof(value))
    };
}

public static class DocumentRepositoryPauseScopes
{
    public const string Retrieval = "retrieval";
    public const string Synchronization = "synchronization";
    public const string Writes = "writes";

    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        Retrieval => Retrieval,
        Synchronization => Synchronization,
        Writes => Writes,
        _ => throw new ArgumentException("Pause scope must be retrieval, synchronization, or writes.", nameof(value))
    };
}

public sealed class CompanyDocumentRepositoryAgentGrant : ICompanyOwnedEntity
{
    private CompanyDocumentRepositoryAgentGrant() { }

    public CompanyDocumentRepositoryAgentGrant(Guid companyId, Guid connectionId, Guid agentId, DateTime createdUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        ConnectionId = connectionId == Guid.Empty ? throw new ArgumentException("ConnectionId is required.", nameof(connectionId)) : connectionId;
        AgentId = agentId == Guid.Empty ? throw new ArgumentException("AgentId is required.", nameof(agentId)) : agentId;
        CreatedUtc = createdUtc.Kind == DateTimeKind.Utc ? createdUtc : createdUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public Guid AgentId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public CompanyDocumentRepositoryConnection Connection { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;
}

public static class DocumentRepositoryProviderKinds
{
    public const string OneDriveForBusiness = "onedrive_business";
    public const string SharePointLibrary = "sharepoint_library";
    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        OneDriveForBusiness => OneDriveForBusiness,
        SharePointLibrary => SharePointLibrary,
        _ => throw new ArgumentException("ProviderKind must be 'onedrive_business' or 'sharepoint_library'.", nameof(value))
    };
}

public static class DocumentRepositoryLifecycleStates
{
    public const string PendingValidation = "pending_validation";
    public const string Active = "active";
    public const string Unavailable = "unavailable";
    public const string Disconnected = "disconnected";
}

public static class DocumentRepositoryAudiences
{
    public const string Company = "company";
    public static string Normalize(string value) => string.Equals(value?.Trim(), Company, StringComparison.OrdinalIgnoreCase)
        ? Company
        : throw new ArgumentException("Audience must explicitly be 'company'.", nameof(value));
}

public static class DocumentRepositoryValidationCodes
{
    public const string Succeeded = "succeeded";
    public const string MissingAccess = "missing_resource_grant";
    public const string InvalidCredentials = "invalid_credentials";
    public const string NotFound = "resource_not_found";
    public const string Throttled = "provider_throttled";
    public const string Unavailable = "provider_unavailable";
    public const string BoundaryViolation = "boundary_violation";
}
