using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinanceAutonomyTriggerCursor : ICompanyOwnedEntity
{
    private FinanceAutonomyTriggerCursor() { }

    public FinanceAutonomyTriggerCursor(Guid id, Guid companyId, Guid grantId, Guid grantVersionId,
        Guid agentId, string capabilityId, string triggerKind, string triggerKey, DateTime createdUtc)
    {
        Id = TriggerValue.Id(id);
        CompanyId = TriggerValue.Required(companyId, nameof(companyId));
        GrantId = TriggerValue.Required(grantId, nameof(grantId));
        GrantVersionId = TriggerValue.Required(grantVersionId, nameof(grantVersionId));
        AgentId = TriggerValue.Required(agentId, nameof(agentId));
        CapabilityId = TriggerValue.Text(capabilityId, nameof(capabilityId), 160);
        TriggerKind = TriggerValue.Text(triggerKind, nameof(triggerKind), 32);
        TriggerKey = TriggerValue.Text(triggerKey, nameof(triggerKey), 200);
        Status = FinanceAutonomyTriggerCursorStatus.Idle;
        CreatedUtc = TriggerValue.Utc(createdUtc);
        UpdatedUtc = CreatedUtc;
        RowVersion = TriggerValue.Token();
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid GrantId { get; private set; }
    public Guid GrantVersionId { get; private set; }
    public Guid AgentId { get; private set; }
    public string CapabilityId { get; private set; } = null!;
    public string TriggerKind { get; private set; } = null!;
    public string TriggerKey { get; private set; } = null!;
    public FinanceAutonomyTriggerCursorStatus Status { get; private set; }
    public DateTime? CursorUtc { get; private set; }
    public string? LastEventVersion { get; private set; }
    public DateTime? CurrentWindowStartUtc { get; private set; }
    public DateTime? CurrentWindowEndUtc { get; private set; }
    public DateTime? QuotaWindowStartUtc { get; private set; }
    public DateTime? QuotaWindowEndUtc { get; private set; }
    public int RunsInWindow { get; private set; }
    public Guid? LastRunId { get; private set; }
    public DateTime? LastRunUtc { get; private set; }
    public DateTime? NextEligibleUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LeaseOwner { get; private set; }
    public string? LeaseToken { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public FinanceAutonomyGrant Grant { get; private set; } = null!;
    public FinanceAutonomyGrantVersion GrantVersion { get; private set; } = null!;
    public FinanceAutonomyRun? LastRun { get; private set; }
    public ICollection<FinanceAutonomyTriggerEvent> Events { get; } = new List<FinanceAutonomyTriggerEvent>();

    public bool TryClaim(string owner, string token, DateTime nowUtc, TimeSpan leaseDuration)
    {
        var now = TriggerValue.Utc(nowUtc);
        if (Status == FinanceAutonomyTriggerCursorStatus.DeadLettered ||
            (LeaseExpiresUtc.HasValue && LeaseExpiresUtc > now)) return false;
        LeaseOwner = TriggerValue.Text(owner, nameof(owner), 160);
        LeaseToken = TriggerValue.Text(token, nameof(token), 160);
        LeaseExpiresUtc = now.Add(leaseDuration);
        Status = FinanceAutonomyTriggerCursorStatus.Claimed;
        AttemptCount++;
        Touch(now);
        return true;
    }

    public void RecordRun(string leaseToken, Guid runId, DateTime cursorUtc, string? eventVersion,
        DateTime runWindowStartUtc, DateTime runWindowEndUtc, DateTime quotaWindowStartUtc,
        DateTime quotaWindowEndUtc, bool incrementQuota, DateTime nextEligibleUtc, bool coalesced, DateTime nowUtc)
    {
        EnsureLease(leaseToken, nowUtc);
        var incomingCursor = TriggerValue.Utc(cursorUtc);
        if (!CursorUtc.HasValue || incomingCursor >= CursorUtc.Value)
        {
            CursorUtc = incomingCursor;
            LastEventVersion = TriggerValue.Optional(eventVersion, 100);
        }
        CurrentWindowStartUtc = TriggerValue.Utc(runWindowStartUtc);
        CurrentWindowEndUtc = TriggerValue.Utc(runWindowEndUtc);
        var quotaStart = TriggerValue.Utc(quotaWindowStartUtc);
        var quotaEnd = TriggerValue.Utc(quotaWindowEndUtc);
        if (QuotaWindowStartUtc != quotaStart || QuotaWindowEndUtc != quotaEnd)
        {
            QuotaWindowStartUtc = quotaStart;
            QuotaWindowEndUtc = quotaEnd;
            RunsInWindow = 0;
        }
        if (incrementQuota) RunsInWindow++;
        LastRunId = TriggerValue.Required(runId, nameof(runId));
        // Coalesced receipts retain the first run's fixed eligibility boundary. This prevents
        // a steady event burst from extending the debounce/minimum-interval window forever.
        if (incrementQuota || !LastRunUtc.HasValue)
        {
            LastRunUtc = TriggerValue.Utc(nowUtc);
            NextEligibleUtc = TriggerValue.Utc(nextEligibleUtc);
        }
        AttemptCount = 0;
        Status = coalesced ? FinanceAutonomyTriggerCursorStatus.Coalesced : FinanceAutonomyTriggerCursorStatus.Processed;
        FailureCode = null;
        FailureSummary = null;
        ClearLease();
        Touch(nowUtc);
    }

    public void Suppress(string leaseToken, DateTime cursorUtc, string reasonCode, string summary,
        DateTime nextEligibleUtc, DateTime nowUtc)
    {
        EnsureLease(leaseToken, nowUtc);
        CursorUtc = TriggerValue.Utc(cursorUtc);
        NextEligibleUtc = TriggerValue.Utc(nextEligibleUtc);
        FailureCode = TriggerValue.Text(reasonCode, nameof(reasonCode), 100);
        FailureSummary = TriggerValue.Text(summary, nameof(summary), 1000);
        Status = FinanceAutonomyTriggerCursorStatus.Suppressed;
        AttemptCount = 0;
        ClearLease();
        Touch(nowUtc);
    }

    public void Fail(string leaseToken, string reasonCode, string summary, int maximumAttempts,
        DateTime retryUtc, DateTime nowUtc)
    {
        EnsureLease(leaseToken, nowUtc, allowExpired: true);
        FailureCode = TriggerValue.Text(reasonCode, nameof(reasonCode), 100);
        FailureSummary = TriggerValue.Text(summary, nameof(summary), 1000);
        NextEligibleUtc = TriggerValue.Utc(retryUtc);
        Status = AttemptCount >= maximumAttempts
            ? FinanceAutonomyTriggerCursorStatus.DeadLettered
            : FinanceAutonomyTriggerCursorStatus.RetryScheduled;
        ClearLease();
        Touch(nowUtc);
    }

    public void Reset(DateTime nowUtc)
    {
        if (Status != FinanceAutonomyTriggerCursorStatus.DeadLettered)
            throw new InvalidOperationException("Only a dead-lettered Finance trigger can be retried.");
        Status = FinanceAutonomyTriggerCursorStatus.Idle;
        AttemptCount = 0;
        FailureCode = null;
        FailureSummary = null;
        NextEligibleUtc = TriggerValue.Utc(nowUtc);
        ClearLease();
        Touch(nowUtc);
    }

    private void EnsureLease(string token, DateTime nowUtc, bool allowExpired = false)
    {
        var now = TriggerValue.Utc(nowUtc);
        if (Status != FinanceAutonomyTriggerCursorStatus.Claimed ||
            !string.Equals(LeaseToken, token, StringComparison.Ordinal) ||
            (!allowExpired && LeaseExpiresUtc <= now))
            throw new InvalidOperationException("The Finance autonomy trigger lease is not owned by this worker.");
    }

    private void ClearLease() { LeaseOwner = null; LeaseToken = null; LeaseExpiresUtc = null; }
    private void Touch(DateTime nowUtc) { UpdatedUtc = TriggerValue.Utc(nowUtc); Version++; RowVersion = TriggerValue.Token(); }
}

public sealed class FinanceAutonomyTriggerEvent : ICompanyOwnedEntity
{
    private FinanceAutonomyTriggerEvent() { }

    public FinanceAutonomyTriggerEvent(Guid id, Guid companyId, Guid cursorId, string eventType,
        string sourceEventId, string sourceEventVersion, string sourceEntityType, string sourceEntityId,
        DateTime occurredUtc, DateTime evidenceObservedUtc, string coalescingKey, string contentHash,
        string? safeLabel, string correlationId, DateTime createdUtc)
    {
        Id = TriggerValue.Id(id);
        CompanyId = TriggerValue.Required(companyId, nameof(companyId));
        CursorId = TriggerValue.Required(cursorId, nameof(cursorId));
        EventType = TriggerValue.Text(eventType, nameof(eventType), 100);
        SourceEventId = TriggerValue.Text(sourceEventId, nameof(sourceEventId), 240);
        SourceEventVersion = TriggerValue.Text(sourceEventVersion, nameof(sourceEventVersion), 100);
        SourceEntityType = TriggerValue.Text(sourceEntityType, nameof(sourceEntityType), 100);
        SourceEntityId = TriggerValue.Text(sourceEntityId, nameof(sourceEntityId), 240);
        OccurredUtc = TriggerValue.Utc(occurredUtc);
        EvidenceObservedUtc = TriggerValue.Utc(evidenceObservedUtc);
        CoalescingKey = TriggerValue.Text(coalescingKey, nameof(coalescingKey), 200);
        ContentHash = TriggerValue.Hash(contentHash, nameof(contentHash));
        SafeLabel = TriggerValue.Optional(safeLabel, 300);
        CorrelationId = TriggerValue.Text(correlationId, nameof(correlationId), 128);
        Status = FinanceAutonomyTriggerEventStatus.Received;
        CreatedUtc = TriggerValue.Utc(createdUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CursorId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string SourceEventId { get; private set; } = null!;
    public string SourceEventVersion { get; private set; } = null!;
    public string SourceEntityType { get; private set; } = null!;
    public string SourceEntityId { get; private set; } = null!;
    public DateTime OccurredUtc { get; private set; }
    public DateTime EvidenceObservedUtc { get; private set; }
    public string CoalescingKey { get; private set; } = null!;
    public string ContentHash { get; private set; } = null!;
    public string? SafeLabel { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public FinanceAutonomyTriggerEventStatus Status { get; private set; }
    public Guid? RunId { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime? ProcessedUtc { get; private set; }
    public FinanceAutonomyTriggerCursor Cursor { get; private set; } = null!;
    public FinanceAutonomyRun? Run { get; private set; }

    public void Complete(Guid runId, bool coalesced, DateTime nowUtc)
    {
        RunId = TriggerValue.Required(runId, nameof(runId));
        Status = coalesced ? FinanceAutonomyTriggerEventStatus.Coalesced : FinanceAutonomyTriggerEventStatus.Processed;
        FailureCode = null;
        FailureSummary = null;
        ProcessedUtc = TriggerValue.Utc(nowUtc);
    }

    public void Suppress(string reasonCode, string summary, DateTime nowUtc)
    {
        Status = FinanceAutonomyTriggerEventStatus.Suppressed;
        FailureCode = TriggerValue.Text(reasonCode, nameof(reasonCode), 100);
        FailureSummary = TriggerValue.Text(summary, nameof(summary), 1000);
        ProcessedUtc = TriggerValue.Utc(nowUtc);
    }

    public void DeadLetter(string reasonCode, string summary, DateTime nowUtc)
    {
        Status = FinanceAutonomyTriggerEventStatus.DeadLettered;
        FailureCode = TriggerValue.Text(reasonCode, nameof(reasonCode), 100);
        FailureSummary = TriggerValue.Text(summary, nameof(summary), 1000);
        ProcessedUtc = TriggerValue.Utc(nowUtc);
    }

    public void ResetForRetry()
    {
        if (Status != FinanceAutonomyTriggerEventStatus.DeadLettered)
            throw new InvalidOperationException("Only a dead-lettered Finance event can be retried.");
        Status = FinanceAutonomyTriggerEventStatus.Received;
        FailureCode = null;
        FailureSummary = null;
        ProcessedUtc = null;
    }
}

internal static class TriggerValue
{
    public static Guid Id(Guid value) => value == Guid.Empty ? Guid.NewGuid() : value;
    public static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Text(string? value, string name, int maximum) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length > maximum ? throw new ArgumentOutOfRangeException(name) : value.Trim();
    public static string? Optional(string? value, int maximum) => string.IsNullOrWhiteSpace(value) ? null
        : value.Trim().Length > maximum ? throw new ArgumentOutOfRangeException(nameof(value)) : value.Trim();
    public static string Hash(string value, string name)
    {
        var result = Text(value, name, 64).ToLowerInvariant();
        return result.Length == 64 && result.All(Uri.IsHexDigit) ? result : throw new ArgumentException("A SHA-256 hash is required.", name);
    }
    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    public static byte[] Token() => Guid.NewGuid().ToByteArray();
}
