using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceIntegrationConnection : ICompanyOwnedEntity
{
    private FinanceIntegrationConnection()
    {
    }

    public FinanceIntegrationConnection(
        Guid id,
        Guid companyId,
        string providerKey,
        string status,
        Guid? connectedByUserId,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        ProviderKey = NormalizeRequired(providerKey, nameof(providerKey), 64).ToLowerInvariant();
        Status = NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        ConnectedByUserId = connectedByUserId == Guid.Empty ? throw new ArgumentException("ConnectedByUserId cannot be empty.", nameof(connectedByUserId)) : connectedByUserId;
        ProviderTenantId = null;
        DisplayName = null;
        Scopes = [];
        Metadata = [];
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid? ConnectedByUserId { get; private set; }
    public string? ProviderTenantId { get; private set; }
    public string? DisplayName { get; private set; }
    public List<string> Scopes { get; private set; } = [];
    public JsonObject Metadata { get; private set; } = [];
    public DateTime? ConnectedUtc { get; private set; }
    public DateTime? LastSyncUtc { get; private set; }
    public DateTime? DisabledUtc { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User? ConnectedByUser { get; private set; }
    public ICollection<FinanceIntegrationToken> Tokens { get; } = new List<FinanceIntegrationToken>();
    public ICollection<FinanceIntegrationSyncState> SyncStates { get; } = new List<FinanceIntegrationSyncState>();
    public ICollection<FinanceExternalReference> ExternalReferences { get; } = new List<FinanceExternalReference>();
    public ICollection<FinanceIntegrationAuditEvent> AuditEvents { get; } = new List<FinanceIntegrationAuditEvent>();

    public void MarkSyncSucceeded(DateTime completedUtc)
    {
        LastSyncUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        LastErrorSummary = null;
        UpdatedUtc = LastSyncUtc.Value;
    }

    public void MarkSyncFailed(string errorSummary, DateTime completedUtc)
    {
        LastErrorSummary = NormalizeOptional(errorSummary, nameof(errorSummary), 1000);
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
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
}

public sealed class FinanceIntegrationToken : ICompanyOwnedEntity
{
    private FinanceIntegrationToken()
    {
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string TokenType { get; private set; } = null!;
    public string EncryptedToken { get; private set; } = null!;
    public DateTime? ExpiresUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceIntegrationConnection Connection { get; private set; } = null!;
}

public sealed class FinanceIntegrationSyncState : ICompanyOwnedEntity
{
    private FinanceIntegrationSyncState()
    {
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public string ScopeKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? Cursor { get; private set; }
    public DateTime? LastStartedUtc { get; private set; }
    public DateTime? LastCompletedUtc { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public int ConsecutiveFailureCount { get; private set; }
    public JsonObject Metadata { get; private set; } = [];
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceIntegrationConnection Connection { get; private set; } = null!;

    public FinanceIntegrationSyncState(
        Guid id,
        Guid companyId,
        Guid connectionId,
        string providerKey,
        string entityType,
        string scopeKey,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        ConnectionId = connectionId == Guid.Empty ? throw new ArgumentException("ConnectionId is required.", nameof(connectionId)) : connectionId;
        ProviderKey = NormalizeRequired(providerKey, nameof(providerKey), 64).ToLowerInvariant();
        EntityType = NormalizeRequired(entityType, nameof(entityType), 64).ToLowerInvariant();
        ScopeKey = string.IsNullOrWhiteSpace(scopeKey) ? "default" : NormalizeRequired(scopeKey, nameof(scopeKey), 128).ToLowerInvariant();
        Status = FinanceIntegrationSyncStatuses.Pending;
        Metadata = [];
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public void MarkStarted(DateTime startedUtc)
    {
        Status = FinanceIntegrationSyncStatuses.Running;
        LastStartedUtc = EntityTimestampNormalizer.NormalizeUtc(startedUtc, nameof(startedUtc));
        LastErrorSummary = null;
        UpdatedUtc = LastStartedUtc.Value;
    }

    public void MarkSucceeded(string? cursor, DateTime completedUtc)
    {
        Status = FinanceIntegrationSyncStatuses.Succeeded;
        Cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor.Trim();
        LastCompletedUtc = EntityTimestampNormalizer.NormalizeUtc(completedUtc, nameof(completedUtc));
        LastErrorSummary = null;
        ConsecutiveFailureCount = 0;
        UpdatedUtc = LastCompletedUtc.Value;
    }

    public void MarkFailed(string errorSummary, DateTime failedUtc)
    {
        Status = FinanceIntegrationSyncStatuses.Failed;
        LastErrorSummary = NormalizeOptional(errorSummary, nameof(errorSummary), 1000);
        ConsecutiveFailureCount++;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(failedUtc, nameof(failedUtc));
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? throw new ArgumentOutOfRangeException(name) : trimmed;
    }
}

public sealed class FinanceExternalReference : ICompanyOwnedEntity
{
    private FinanceExternalReference()
    {
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConnectionId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string EntityType { get; private set; } = null!;
    public Guid InternalRecordId { get; private set; }
    public string ExternalId { get; private set; } = null!;
    public string? ExternalNumber { get; private set; }
    public DateTime? ExternalUpdatedUtc { get; private set; }
    public JsonObject Metadata { get; private set; } = [];
    public DateTime CreatedUtc { get; private set; }
    public DateTime LastSyncedUtc => UpdatedUtc;
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceIntegrationConnection Connection { get; private set; } = null!;

    public FinanceExternalReference(
        Guid id,
        Guid companyId,
        Guid connectionId,
        string providerKey,
        string entityType,
        Guid internalRecordId,
        string externalId,
        string? externalNumber,
        DateTime? externalUpdatedUtc,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        ConnectionId = connectionId == Guid.Empty ? throw new ArgumentException("ConnectionId is required.", nameof(connectionId)) : connectionId;
        ProviderKey = NormalizeRequired(providerKey, nameof(providerKey), 64).ToLowerInvariant();
        EntityType = NormalizeRequired(entityType, nameof(entityType), 64).ToLowerInvariant();
        InternalRecordId = internalRecordId == Guid.Empty ? throw new ArgumentException("InternalRecordId is required.", nameof(internalRecordId)) : internalRecordId;
        ExternalId = NormalizeRequired(externalId, nameof(externalId), 256);
        ExternalNumber = NormalizeOptional(externalNumber, nameof(externalNumber), 128);
        ExternalUpdatedUtc = externalUpdatedUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(externalUpdatedUtc.Value, nameof(externalUpdatedUtc)) : null;
        Metadata = [];
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public void RepointToInternalRecord(Guid internalRecordId, string? externalNumber, DateTime? externalUpdatedUtc, DateTime syncedUtc)
    {
        InternalRecordId = internalRecordId == Guid.Empty
            ? throw new ArgumentException("InternalRecordId is required.", nameof(internalRecordId))
            : internalRecordId;
        Refresh(externalNumber, externalUpdatedUtc, syncedUtc);
    }

    public void Refresh(string? externalNumber, DateTime? externalUpdatedUtc, DateTime updatedUtc)
    {
        ExternalNumber = NormalizeOptional(externalNumber, nameof(externalNumber), 128);
        ExternalUpdatedUtc = externalUpdatedUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(externalUpdatedUtc.Value, nameof(externalUpdatedUtc)) : null;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    public void MarkSynced(DateTime syncedUtc)
    {
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(syncedUtc, nameof(syncedUtc));
    }

    public bool IsCurrent(DateTime? externalUpdatedUtc) =>
        !externalUpdatedUtc.HasValue ||
        (ExternalUpdatedUtc.HasValue &&
         ExternalUpdatedUtc.Value >= EntityTimestampNormalizer.NormalizeUtc(externalUpdatedUtc.Value, nameof(externalUpdatedUtc)));

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
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
}

public sealed class FinanceIntegrationAuditEvent : ICompanyOwnedEntity
{
    private FinanceIntegrationAuditEvent()
    {
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public string? EntityType { get; private set; }
    public Guid? InternalRecordId { get; private set; }
    public string? ExternalId { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? Summary { get; private set; }
    public JsonObject Metadata { get; private set; } = [];
    public int CreatedCount { get; private set; }
    public int UpdatedCount { get; private set; }
    public int SkippedCount { get; private set; }
    public int ErrorCount { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceIntegrationConnection? Connection { get; private set; }

    public FinanceIntegrationAuditEvent(
        Guid id,
        Guid companyId,
        Guid? connectionId,
        string providerKey,
        string eventType,
        string outcome,
        string? entityType,
        Guid? internalRecordId,
        string? externalId,
        string? correlationId,
        string? summary,
        DateTime createdUtc,
        int createdCount = 0,
        int updatedCount = 0,
        int skippedCount = 0,
        int errorCount = 0)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        ConnectionId = connectionId == Guid.Empty ? null : connectionId;
        ProviderKey = NormalizeRequired(providerKey, nameof(providerKey), 64).ToLowerInvariant();
        EventType = NormalizeRequired(eventType, nameof(eventType), 64).ToLowerInvariant();
        Outcome = NormalizeRequired(outcome, nameof(outcome), 32).ToLowerInvariant();
        EntityType = NormalizeOptional(entityType, nameof(entityType), 64)?.ToLowerInvariant();
        InternalRecordId = internalRecordId == Guid.Empty ? null : internalRecordId;
        ExternalId = NormalizeOptional(externalId, nameof(externalId), 256);
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        Summary = NormalizeOptional(summary, nameof(summary), 1000);
        Metadata = [];
        CreatedCount = NormalizeCount(createdCount, nameof(createdCount));
        UpdatedCount = NormalizeCount(updatedCount, nameof(updatedCount));
        SkippedCount = NormalizeCount(skippedCount, nameof(skippedCount));
        ErrorCount = NormalizeCount(errorCount, nameof(errorCount));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }

    private static int NormalizeCount(int value, string name)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} cannot be negative.");
        }

        return value;
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
}

public sealed class FinanceIntegrationWriteCommandRecord : ICompanyOwnedEntity
{
    private FinanceIntegrationWriteCommandRecord()
    {
    }

    public FinanceIntegrationWriteCommandRecord(
        Guid id,
        Guid companyId,
        Guid? connectionId,
        Guid? actorUserId,
        string commandType,
        string httpMethod,
        string path,
        string targetCompany,
        string payloadSummary,
        string payloadHash,
        string sanitizedPayloadJson,
        string? correlationId,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        ConnectionId = connectionId == Guid.Empty ? null : connectionId;
        ActorUserId = actorUserId == Guid.Empty ? null : actorUserId;
        CommandType = NormalizeRequired(commandType, nameof(commandType), 64).Replace('-', '_').ToLowerInvariant();
        HttpMethod = NormalizeRequired(httpMethod, nameof(httpMethod), 16).ToUpperInvariant();
        Path = NormalizeRequired(path, nameof(path), 512);
        TargetCompany = NormalizeRequired(targetCompany, nameof(targetCompany), 160);
        PayloadSummary = NormalizeRequired(payloadSummary, nameof(payloadSummary), 1000);
        PayloadHash = NormalizeRequired(payloadHash, nameof(payloadHash), 128).ToLowerInvariant();
        SanitizedPayloadJson = NormalizeRequired(sanitizedPayloadJson, nameof(sanitizedPayloadJson), 8000);
        CorrelationId = NormalizeOptional(correlationId, nameof(correlationId), 128);
        Status = FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval;
        RetryPolicy = FinanceIntegrationWriteRetryPolicies.ForCommandType(CommandType);
        RetrySupported = RetryPolicy != FinanceIntegrationWriteRetryPolicyValues.None;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? ConnectionId { get; private set; }
    public Guid? ActorUserId { get; private set; }
    public Guid? ApprovalId { get; private set; }
    public Guid? ApprovedByUserId { get; private set; }
    public string CommandType { get; private set; } = null!;
    public string HttpMethod { get; private set; } = null!;
    public string Path { get; private set; } = null!;
    public string TargetCompany { get; private set; } = null!;
    public string PayloadSummary { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public string SanitizedPayloadJson { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? FailureCategory { get; private set; }
    public string? SafeFailureSummary { get; private set; }
    public string? ExternalId { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public string? SafeResponseSummary { get; private set; }
    public bool RetrySupported { get; private set; }
    public string RetryPolicy { get; private set; } = FinanceIntegrationWriteRetryPolicyValues.None;
    public int ExecutionAttemptCount { get; private set; }
    public string? CorrelationId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public DateTime? ExecutionStartedUtc { get; private set; }
    public DateTime? ExecutedUtc { get; private set; }
    public DateTime? FailedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceIntegrationConnection? Connection { get; private set; }

    public void AttachApproval(Guid approvalId, DateTime updatedUtc)
    {
        if (Status != FinanceIntegrationWriteCommandRecordStatuses.AwaitingApproval)
        {
            return;
        }

        if (approvalId == Guid.Empty)
        {
            throw new ArgumentException("ApprovalId is required.", nameof(approvalId));
        }

        ApprovalId = approvalId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    public void MarkApproved(Guid approvalId, Guid? approvedByUserId, DateTime approvedUtc)
    {
        if (Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            throw new InvalidOperationException("Finance integration write command has already executed.");
        }

        ApprovalId = approvalId == Guid.Empty ? ApprovalId : approvalId;
        ApprovedByUserId = approvedByUserId == Guid.Empty ? null : approvedByUserId;
        Status = FinanceIntegrationWriteCommandRecordStatuses.Approved;
        ApprovedUtc = EntityTimestampNormalizer.NormalizeUtc(approvedUtc, nameof(approvedUtc));
        UpdatedUtc = ApprovedUtc.Value;
    }

    public void MarkExecutionStarted(DateTime startedUtc)
    {
        if (Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            throw new InvalidOperationException("Finance integration write command has already executed.");
        }

        Status = FinanceIntegrationWriteCommandRecordStatuses.Executing;
        ExecutionStartedUtc = EntityTimestampNormalizer.NormalizeUtc(startedUtc, nameof(startedUtc));
        ExecutionAttemptCount++;
        UpdatedUtc = ExecutionStartedUtc.Value;
    }

    public void MarkExecuted(string? externalId, int? responseStatusCode, string? safeResponseSummary, DateTime executedUtc)
    {
        Status = FinanceIntegrationWriteCommandRecordStatuses.Executed;
        ExternalId = NormalizeOptional(externalId, nameof(externalId), 256);
        ResponseStatusCode = responseStatusCode;
        SafeResponseSummary = NormalizeOptional(safeResponseSummary, nameof(safeResponseSummary), 1000);
        SafeFailureSummary = null;
        FailureCategory = null;
        ExecutedUtc = EntityTimestampNormalizer.NormalizeUtc(executedUtc, nameof(executedUtc));
        UpdatedUtc = ExecutedUtc.Value;
    }

    public void MarkFailed(string failureCategory, string safeFailureSummary, int? responseStatusCode, DateTime failedUtc)
    {
        Status = FinanceIntegrationWriteCommandRecordStatuses.Failed;
        ResponseStatusCode = responseStatusCode;
        FailureCategory = NormalizeOptional(failureCategory, nameof(failureCategory), 64);
        SafeFailureSummary = NormalizeOptional(safeFailureSummary, nameof(safeFailureSummary), 1000);
        FailedUtc = EntityTimestampNormalizer.NormalizeUtc(failedUtc, nameof(failedUtc));
        UpdatedUtc = FailedUtc.Value;
    }

    public void MarkRejected(DateTime updatedUtc)
    {
        MarkTerminalWithoutExecution(FinanceIntegrationWriteCommandRecordStatuses.Rejected, updatedUtc);
    }

    public void MarkExpired(DateTime updatedUtc)
    {
        MarkTerminalWithoutExecution(FinanceIntegrationWriteCommandRecordStatuses.Expired, updatedUtc);
    }

    public void MarkCancelled(DateTime updatedUtc)
    {
        MarkTerminalWithoutExecution(FinanceIntegrationWriteCommandRecordStatuses.Cancelled, updatedUtc);
    }

    private void MarkTerminalWithoutExecution(string status, DateTime updatedUtc)
    {
        if (Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            return;
        }

        Status = status;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    private static int NormalizeCount(int value, string name)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(name, $"{name} cannot be negative.");
        return value;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length > maxLength ? trimmed[..maxLength] : trimmed;
    }

    private static string NormalizeRequired(string value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{name} is required.", name)
            : value.Trim().Length > maxLength ? throw new ArgumentOutOfRangeException(name) : value.Trim();
}

public static class FinanceRecordSourceTypes
{
    public const string Manual = "manual";
    public const string Simulation = "simulation";
    public const string Fortnox = "fortnox";

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('manual', 'simulation', 'fortnox')";
}

public static class FinanceIntegrationConnectionStatuses
{
    public const string Pending = "pending";
    public const string Connected = "connected";
    public const string NeedsReconnect = "needs_reconnect";
    public const string Disabled = "disabled";
    public const string Error = "error";

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('pending', 'connected', 'needs_reconnect', 'disabled', 'error')";
}

public static class FinanceIntegrationTokenTypes
{
    public const string Access = "access";
    public const string Refresh = "refresh";

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('access', 'refresh')";
}

public static class FinanceIntegrationSyncStatuses
{
    public const string Pending = "pending";
    public const string Running = "running";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Partial = "partial";

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('pending', 'running', 'succeeded', 'failed', 'partial')";
}

public static class FinanceIntegrationAuditOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string Skipped = "skipped";

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('succeeded', 'failed', 'skipped')";
}

public static class FinanceIntegrationWriteCommandRecordStatuses
{
    public const string AwaitingApproval = "awaiting_approval";
    public const string Approved = "approved";
    public const string Executing = "executing";
    public const string Executed = "executed";
    public const string Failed = "failed";
    public const string Rejected = "rejected";
    public const string Expired = "expired";
    public const string Cancelled = "cancelled";

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('awaiting_approval', 'approved', 'executing', 'executed', 'failed', 'rejected', 'expired', 'cancelled')";
}

public static class FinanceIntegrationWriteRetryPolicyValues
{
    public const string None = "none";
    public const string TransientOnly = "transient_only";

    public static string BuildCheckConstraintSql(string columnName) =>
        $"{columnName} IN ('none', 'transient_only')";
}

public static class FinanceIntegrationWriteRetryPolicies
{
    public static string ForCommandType(string commandType) =>
        commandType is "payment" or "invoice_export" or "voucher_create"
            ? FinanceIntegrationWriteRetryPolicyValues.None
            : FinanceIntegrationWriteRetryPolicyValues.TransientOnly;
}
