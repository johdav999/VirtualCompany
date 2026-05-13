using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class FortnoxOAuthState : ICompanyOwnedEntity
{
    private FortnoxOAuthState()
    {
    }

    public FortnoxOAuthState(
        Guid id,
        Guid companyId,
        Guid userId,
        string stateHash,
        DateTime createdUtc,
        DateTime expiresUtc,
        string? redirectUri = null,
        string? codeVerifierCiphertext = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        UserId = userId == Guid.Empty ? throw new ArgumentException("UserId is required.", nameof(userId)) : userId;
        StateHash = NormalizeRequired(stateHash, nameof(stateHash), 128);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        ExpiresUtc = EntityTimestampNormalizer.NormalizeUtc(expiresUtc, nameof(expiresUtc));
        if (ExpiresUtc <= CreatedUtc)
        {
            throw new ArgumentException("OAuth state expiry must be after creation.", nameof(expiresUtc));
        }

        RedirectUri = NormalizeOptional(redirectUri, nameof(redirectUri), 2048);
        CodeVerifierCiphertext = NormalizeOptional(codeVerifierCiphertext, nameof(codeVerifierCiphertext), 4096);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public string StateHash { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? ConsumedUtc { get; private set; }
    public DateTime? CallbackReceivedUtc { get; private set; }
    public string? RedirectUri { get; private set; }
    public string? CodeVerifierCiphertext { get; private set; }
    public string? FailureReason { get; private set; }
    public Company Company { get; private set; } = null!;
    public User User { get; private set; } = null!;
    public FortnoxConnection? Connection { get; private set; }

    public void MarkCallbackReceived(DateTime receivedUtc)
    {
        CallbackReceivedUtc = EntityTimestampNormalizer.NormalizeUtc(receivedUtc, nameof(receivedUtc));
    }

    public void MarkConsumed(Guid? connectionId, DateTime consumedUtc)
    {
        if (ConsumedUtc.HasValue)
        {
            throw new InvalidOperationException("Fortnox OAuth state has already been consumed.");
        }

        ConnectionId = connectionId == Guid.Empty ? null : connectionId;
        ConsumedUtc = EntityTimestampNormalizer.NormalizeUtc(consumedUtc, nameof(consumedUtc));
        CallbackReceivedUtc ??= ConsumedUtc;
        FailureReason = null;
    }

    public void MarkFailed(string failureReason, DateTime receivedUtc)
    {
        FailureReason = NormalizeOptional(failureReason, nameof(failureReason), 1000);
        CallbackReceivedUtc = EntityTimestampNormalizer.NormalizeUtc(receivedUtc, nameof(receivedUtc));
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

public sealed class FortnoxSyncHistory : ICompanyOwnedEntity
{
    private FortnoxSyncHistory()
    {
    }

    public FortnoxSyncHistory(
        Guid id,
        Guid companyId,
        Guid fortnoxConnectionId,
        string syncType,
        string direction,
        DateTime startedUtc,
        Guid? triggeredByUserId = null,
        string? correlationId = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        FortnoxConnectionId = fortnoxConnectionId == Guid.Empty ? throw new ArgumentException("FortnoxConnectionId is required.", nameof(fortnoxConnectionId)) : fortnoxConnectionId;
        SyncType = NormalizeRequired(syncType, nameof(syncType), 64).ToLowerInvariant();
        Direction = NormalizeRequired(direction, nameof(direction), 32).ToLowerInvariant();
        Status = FortnoxSyncStatuses.Running;
        StartedUtc = EntityTimestampNormalizer.NormalizeUtc(startedUtc, nameof(startedUtc));
        TriggeredByUserId = triggeredByUserId == Guid.Empty ? null : triggeredByUserId;
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        Metadata = [];
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FortnoxConnectionId { get; private set; }
    public string SyncType { get; private set; } = null!;
    public string Direction { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public DateTime StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public Guid? TriggeredByUserId { get; private set; }
    public int RecordsProcessed { get; private set; }
    public int RecordsSucceeded { get; private set; }
    public int RecordsFailed { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? ErrorSummary { get; private set; }
    public JsonObject Metadata { get; private set; } = [];
    public Company Company { get; private set; } = null!;
    public FortnoxConnection FortnoxConnection { get; private set; } = null!;
    public User? TriggeredByUser { get; private set; }

    public void MarkCompleted(int processed, int succeeded, int failed, DateTime completedUtc, string? errorSummary = null)
    {
        RecordsProcessed = NormalizeCount(processed, nameof(processed));
        RecordsSucceeded = NormalizeCount(succeeded, nameof(succeeded));
        RecordsFailed = NormalizeCount(failed, nameof(failed));
        Status = failed == 0
            ? FortnoxSyncStatuses.Succeeded
            : succeeded > 0 ? FortnoxSyncStatuses.Partial : FortnoxSyncStatuses.Failed;
        CompletedUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        ErrorSummary = NormalizeOptional(errorSummary, nameof(errorSummary), 1000);
    }

    private static int NormalizeCount(int value, string name) =>
        value < 0 ? throw new ArgumentOutOfRangeException(name, $"{name} cannot be negative.") : value;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

public sealed class FortnoxExternalReference : ICompanyOwnedEntity
{
    private FortnoxExternalReference()
    {
    }

    public FortnoxExternalReference(
        Guid id,
        Guid companyId,
        Guid? fortnoxConnectionId,
        string entityType,
        Guid internalEntityId,
        string externalEntityType,
        string externalId,
        DateTime createdUtc,
        string? externalDisplayReference = null,
        DateTime? lastSyncedUtc = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        FortnoxConnectionId = fortnoxConnectionId == Guid.Empty ? null : fortnoxConnectionId;
        EntityType = NormalizeRequired(entityType, nameof(entityType), 64).ToLowerInvariant();
        InternalEntityId = internalEntityId == Guid.Empty ? throw new ArgumentException("InternalEntityId is required.", nameof(internalEntityId)) : internalEntityId;
        ExternalEntityType = NormalizeRequired(externalEntityType, nameof(externalEntityType), 64).ToLowerInvariant();
        ExternalId = NormalizeRequired(externalId, nameof(externalId), 256);
        ExternalDisplayReference = NormalizeOptional(externalDisplayReference, nameof(externalDisplayReference), 128);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        LastSyncedUtc = lastSyncedUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(lastSyncedUtc.Value, nameof(lastSyncedUtc))
            : null;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? FortnoxConnectionId { get; private set; }
    public string EntityType { get; private set; } = null!;
    public Guid InternalEntityId { get; private set; }
    public string ExternalEntityType { get; private set; } = null!;
    public string ExternalId { get; private set; } = null!;
    public string? ExternalDisplayReference { get; private set; }
    public DateTime? LastSyncedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FortnoxConnection? FortnoxConnection { get; private set; }

    public void Refresh(string? externalDisplayReference, DateTime syncedUtc)
    {
        ExternalDisplayReference = NormalizeOptional(externalDisplayReference, nameof(externalDisplayReference), 128);
        LastSyncedUtc = EntityTimestampNormalizer.NormalizeUtc(syncedUtc, nameof(syncedUtc));
        UpdatedUtc = LastSyncedUtc.Value;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }
}

public static class FortnoxSyncTypes
{
    public const string Full = "full";
    public const string Incremental = "incremental";
    public const string Manual = "manual";
}

public static class FortnoxSyncDirections
{
    public const string Import = "import";
    public const string Export = "export";
    public const string Bidirectional = "bidirectional";

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('import', 'export', 'bidirectional')";
}

public static class FortnoxSyncStatuses
{
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Partial = "partial";

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('running', 'succeeded', 'failed', 'partial')";
}
