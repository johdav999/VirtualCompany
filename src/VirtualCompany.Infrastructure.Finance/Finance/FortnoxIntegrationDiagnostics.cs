using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxIntegrationDiagnostics : IFortnoxIntegrationDiagnostics
{
    public const string MeterName = "VirtualCompany.Finance.Fortnox";

    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> SyncAttempts = Meter.CreateCounter<long>("fortnox.sync.attempts");
    private static readonly Counter<long> SyncSuccesses = Meter.CreateCounter<long>("fortnox.sync.successes");
    private static readonly Counter<long> SyncFailures = Meter.CreateCounter<long>("fortnox.sync.failures");
    private static readonly Histogram<double> SyncDuration = Meter.CreateHistogram<double>("fortnox.sync.duration", "ms");
    private static readonly Counter<long> TokenRefreshAttempts = Meter.CreateCounter<long>("fortnox.token_refresh.attempts");
    private static readonly Counter<long> TokenRefreshSuccesses = Meter.CreateCounter<long>("fortnox.token_refresh.successes");
    private static readonly Counter<long> TokenRefreshFailures = Meter.CreateCounter<long>("fortnox.token_refresh.failures");
    private static readonly Histogram<double> TokenRefreshDuration = Meter.CreateHistogram<double>("fortnox.token_refresh.duration", "ms");
    private static readonly Counter<long> DuplicateSkippedCounter = Meter.CreateCounter<long>("fortnox.sync.duplicates_skipped");
    private static readonly Counter<long> ApprovalsCreated = Meter.CreateCounter<long>("fortnox.write_approvals.created");

    private readonly ILogger<FortnoxIntegrationDiagnostics> _logger;

    public FortnoxIntegrationDiagnostics(ILogger<FortnoxIntegrationDiagnostics> logger)
    {
        _logger = logger;
    }

    public void SyncStarted(Guid companyId, Guid connectionId, string? correlationId)
    {
        SyncAttempts.Add(1, CommonTags(companyId, connectionId, correlationId));
        _logger.LogInformation(
            "Fortnox sync started. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. CorrelationId: {CorrelationId}.",
            companyId,
            connectionId,
            correlationId);
    }

    public void SyncCompleted(
        Guid companyId,
        Guid connectionId,
        string? correlationId,
        string status,
        int created,
        int updated,
        int skipped,
        int errors,
        TimeSpan duration)
    {
        var tags = CommonTags(companyId, connectionId, correlationId)
            .Append(new KeyValuePair<string, object?>("status", status))
            .ToArray();

        if (errors == 0)
        {
            SyncSuccesses.Add(1, tags);
        }
        else
        {
            SyncFailures.Add(1, tags);
        }

        SyncDuration.Record(duration.TotalMilliseconds, tags);
        _logger.LogInformation(
            "Fortnox sync completed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. CorrelationId: {CorrelationId}. Status: {Status}. Created: {Created}. Updated: {Updated}. Skipped: {Skipped}. Errors: {Errors}. DurationMs: {DurationMs}.",
            companyId,
            connectionId,
            correlationId,
            status,
            created,
            updated,
            skipped,
            errors,
            duration.TotalMilliseconds);
    }

    public void SyncFailed(
        Guid companyId,
        Guid connectionId,
        string? correlationId,
        string safeReason,
        TimeSpan duration)
    {
        var tags = CommonTags(companyId, connectionId, correlationId)
            .Append(new KeyValuePair<string, object?>("status", "failed"))
            .ToArray();

        SyncFailures.Add(1, tags);
        SyncDuration.Record(duration.TotalMilliseconds, tags);
        _logger.LogWarning(
            "Fortnox sync failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. CorrelationId: {CorrelationId}. Reason: {Reason}. DurationMs: {DurationMs}.",
            companyId,
            connectionId,
            correlationId,
            safeReason,
            duration.TotalMilliseconds);
    }

    public void DuplicateSkipped(Guid companyId, Guid connectionId, string entityType)
    {
        DuplicateSkippedCounter.Add(1, EntityTags(companyId, connectionId, entityType));
        _logger.LogInformation(
            "Fortnox sync skipped duplicate record. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. EntityType: {EntityType}.",
            companyId,
            connectionId,
            entityType);
    }

    public void CursorAdvanced(Guid companyId, Guid connectionId, string entityType, string? previousCursor, string? nextCursor)
    {
        _logger.LogInformation(
            "Fortnox sync cursor advanced. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. EntityType: {EntityType}. PreviousCursor: {PreviousCursor}. NextCursor: {NextCursor}.",
            companyId,
            connectionId,
            entityType,
            previousCursor,
            nextCursor);
    }

    public void TokenRefreshStarted(Guid companyId, Guid connectionId)
    {
        TokenRefreshAttempts.Add(1, CommonTags(companyId, connectionId, null));
        _logger.LogInformation(
            "Fortnox token refresh started. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}.",
            companyId,
            connectionId);
    }

    public void TokenRefreshCompleted(Guid companyId, Guid connectionId, bool succeeded, bool needsReconnect, TimeSpan duration)
    {
        var tags = CommonTags(companyId, connectionId, null)
            .Append(new KeyValuePair<string, object?>("needs_reconnect", needsReconnect))
            .ToArray();

        if (succeeded)
        {
            TokenRefreshSuccesses.Add(1, tags);
        }
        else
        {
            TokenRefreshFailures.Add(1, tags);
        }

        TokenRefreshDuration.Record(duration.TotalMilliseconds, tags);
        _logger.LogInformation(
            "Fortnox token refresh completed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Succeeded: {Succeeded}. NeedsReconnect: {NeedsReconnect}. DurationMs: {DurationMs}.",
            companyId,
            connectionId,
            succeeded,
            needsReconnect,
            duration.TotalMilliseconds);
    }

    public void TokenRefreshFailed(Guid companyId, Guid connectionId, string safeReason, bool needsReconnect, TimeSpan duration)
    {
        var tags = CommonTags(companyId, connectionId, null)
            .Append(new KeyValuePair<string, object?>("needs_reconnect", needsReconnect))
            .ToArray();

        TokenRefreshFailures.Add(1, tags);
        TokenRefreshDuration.Record(duration.TotalMilliseconds, tags);
        _logger.LogWarning(
            "Fortnox token refresh failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. NeedsReconnect: {NeedsReconnect}. Reason: {Reason}. DurationMs: {DurationMs}.",
            companyId,
            connectionId,
            needsReconnect,
            safeReason,
            duration.TotalMilliseconds);
    }

    public void ApprovalCreated(Guid companyId, Guid connectionId, Guid approvalId, string entityType, string payloadHash)
    {
        ApprovalsCreated.Add(1, EntityTags(companyId, connectionId, entityType));
        _logger.LogInformation(
            "Fortnox write approval created. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ApprovalId: {ApprovalId}. EntityType: {EntityType}. PayloadHash: {PayloadHash}.",
            companyId,
            connectionId,
            approvalId,
            entityType,
            payloadHash);
    }

    private static KeyValuePair<string, object?>[] CommonTags(Guid companyId, Guid connectionId, string? correlationId) =>
    [
        new("company_id", companyId),
        new("connection_id", connectionId),
        new("correlation_id", correlationId)
    ];

    private static KeyValuePair<string, object?>[] EntityTags(Guid companyId, Guid connectionId, string entityType) =>
    [
        new("company_id", companyId),
        new("connection_id", connectionId),
        new("entity_type", entityType)
    ];
}
