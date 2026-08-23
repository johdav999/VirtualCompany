namespace VirtualCompany.Domain.Entities;

public static class AccountingProviderSwitchMonitoringStatuses
{
    public const string Active = "active";
    public const string AttentionRequired = "attention_required";
    public const string ClosureAwaitingApproval = "closure_awaiting_approval";
    public const string Closed = "closed";
    public const string Failed = "failed";
}

public static class AccountingProviderSwitchMonitoringCheckStatuses
{
    public const string Healthy = "healthy";
    public const string Attention = "attention";
    public const string Critical = "critical";
    public const string Unavailable = "unavailable";
}

public static class AccountingProviderSwitchMonitoringIncidentStatuses
{
    public const string Open = "open";
    public const string Resolved = "resolved";
    public const string AcceptedException = "accepted_exception";
}

/// <summary>Owns the durable post-activation observation window for one accounting-system switch.</summary>
/// <remarks>
/// A leased background worker advances <see cref="CheckSequence"/> once per successful pass. Check history,
/// incidents, and accepted exceptions are accounting evidence and must be retained after closure.
/// </remarks>
public sealed class AccountingProviderSwitchMonitoringRun : ICompanyOwnedEntity
{
    private AccountingProviderSwitchMonitoringRun() { }

    public AccountingProviderSwitchMonitoringRun(Guid companyId, Guid switchId, Guid activationExecutionId,
        int windowDays, Guid assignedOwnerUserId, Guid? assignedOwnerAgentId, string correlationId,
        DateTime startedUtc, DateTime nextRunUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = MonitoringText.Required(companyId, nameof(companyId));
        SwitchId = MonitoringText.Required(switchId, nameof(switchId));
        ActivationExecutionId = MonitoringText.Required(activationExecutionId, nameof(activationExecutionId));
        WindowDays = windowDays is >= 7 and <= 30 ? windowDays : throw new ArgumentOutOfRangeException(nameof(windowDays));
        AssignedOwnerUserId = MonitoringText.Required(assignedOwnerUserId, nameof(assignedOwnerUserId));
        if (assignedOwnerAgentId == Guid.Empty) throw new ArgumentException("AssignedOwnerAgentId cannot be empty.", nameof(assignedOwnerAgentId));
        AssignedOwnerAgentId = assignedOwnerAgentId;
        CorrelationId = MonitoringText.Required(correlationId, nameof(correlationId), 128);
        StartedUtc = MonitoringText.Utc(startedUtc, nameof(startedUtc));
        WindowEndsUtc = StartedUtc.AddDays(WindowDays);
        NextRunUtc = MonitoringText.Utc(nextRunUtc, nameof(nextRunUtc));
        Status = AccountingProviderSwitchMonitoringStatuses.Active;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid ActivationExecutionId { get; private set; }
    public int WindowDays { get; private set; }
    public Guid AssignedOwnerUserId { get; private set; }
    public Guid? AssignedOwnerAgentId { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int CheckSequence { get; private set; }
    public int AttemptCount { get; private set; }
    public int ConsecutiveFailureCount { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime WindowEndsUtc { get; private set; }
    public DateTime? LastCheckStartedUtc { get; private set; }
    public DateTime? LastSuccessfulCheckUtc { get; private set; }
    public DateTime? NextRunUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public Guid? ClosureApprovalRequestId { get; private set; }
    public string? ClosureEvidenceHash { get; private set; }
    public Guid? ClosedByUserId { get; private set; }
    public string? ClosureDecision { get; private set; }
    public string? ClosureSummary { get; private set; }
    public Guid? CorrectiveSwitchId { get; private set; }
    public DateTime? ClosedUtc { get; private set; }
    public long Version { get; private set; }
    public AccountingProviderSwitch Switch { get; private set; } = null!;

    public void Claim(string owner, DateTime nowUtc, DateTime leaseExpiresUtc)
    {
        if (Status is AccountingProviderSwitchMonitoringStatuses.Closed or AccountingProviderSwitchMonitoringStatuses.Failed)
            throw new InvalidOperationException("A closed or failed monitoring run cannot be claimed.");
        var now = MonitoringText.Utc(nowUtc, nameof(nowUtc));
        LeaseOwner = MonitoringText.Required(owner, nameof(owner), 128);
        LeaseExpiresUtc = MonitoringText.Utc(leaseExpiresUtc, nameof(leaseExpiresUtc));
        if (LeaseExpiresUtc <= now) throw new ArgumentOutOfRangeException(nameof(leaseExpiresUtc));
        LastCheckStartedUtc = now;
        NextRunUtc = null;
        AttemptCount++;
        Version++;
    }

    public int CompletePass(bool hasBlockingIncident, DateTime nowUtc, DateTime nextRunUtc)
    {
        RequireLease();
        CheckSequence++;
        Status = hasBlockingIncident ? AccountingProviderSwitchMonitoringStatuses.AttentionRequired : AccountingProviderSwitchMonitoringStatuses.Active;
        LastSuccessfulCheckUtc = MonitoringText.Utc(nowUtc, nameof(nowUtc));
        NextRunUtc = MonitoringText.Utc(nextRunUtc, nameof(nextRunUtc));
        ConsecutiveFailureCount = 0;
        FailureCode = null;
        FailureSummary = null;
        ReleaseLease();
        Version++;
        return CheckSequence;
    }

    public void Fail(string failureCode, string failureSummary, bool exhausted, DateTime nowUtc, DateTime? nextRunUtc)
    {
        FailureCode = MonitoringText.Token(failureCode, nameof(failureCode), 100);
        FailureSummary = MonitoringText.Required(failureSummary, nameof(failureSummary), 1000);
        ConsecutiveFailureCount++;
        Status = exhausted ? AccountingProviderSwitchMonitoringStatuses.Failed : AccountingProviderSwitchMonitoringStatuses.AttentionRequired;
        NextRunUtc = exhausted || !nextRunUtc.HasValue ? null : MonitoringText.Utc(nextRunUtc.Value, nameof(nextRunUtc));
        LastCheckStartedUtc ??= MonitoringText.Utc(nowUtc, nameof(nowUtc));
        ReleaseLease();
        Version++;
    }

    public void Retry(DateTime nowUtc)
    {
        if (Status is not (AccountingProviderSwitchMonitoringStatuses.Failed or AccountingProviderSwitchMonitoringStatuses.AttentionRequired))
            throw new InvalidOperationException("Monitoring is not waiting for a retry.");
        Status = AccountingProviderSwitchMonitoringStatuses.Active;
        NextRunUtc = MonitoringText.Utc(nowUtc, nameof(nowUtc));
        FailureCode = null;
        FailureSummary = null;
        Version++;
    }

    public void QueueNow(DateTime nowUtc)
    {
        if (Status is not (AccountingProviderSwitchMonitoringStatuses.Active or
            AccountingProviderSwitchMonitoringStatuses.AttentionRequired or
            AccountingProviderSwitchMonitoringStatuses.ClosureAwaitingApproval))
            throw new InvalidOperationException("Monitoring cannot run from its current state.");
        NextRunUtc = MonitoringText.Utc(nowUtc, nameof(nowUtc));
        Version++;
    }

    public void AwaitClosureApproval(Guid approvalRequestId, string evidenceHash)
    {
        if (Status == AccountingProviderSwitchMonitoringStatuses.Closed) throw new InvalidOperationException("Monitoring is already closed.");
        ClosureApprovalRequestId = MonitoringText.Required(approvalRequestId, nameof(approvalRequestId));
        ClosureEvidenceHash = MonitoringText.Hash(evidenceHash, nameof(evidenceHash));
        Status = AccountingProviderSwitchMonitoringStatuses.ClosureAwaitingApproval;
        Version++;
    }

    public void Close(Guid actorUserId, string decision, string summary, DateTime closedUtc, Guid? correctiveSwitchId = null)
    {
        if (correctiveSwitchId == Guid.Empty) throw new ArgumentException("CorrectiveSwitchId cannot be empty.", nameof(correctiveSwitchId));
        ClosedByUserId = MonitoringText.Required(actorUserId, nameof(actorUserId));
        ClosureDecision = MonitoringText.Token(decision, nameof(decision), 64);
        ClosureSummary = MonitoringText.Required(summary, nameof(summary), 2000);
        CorrectiveSwitchId = correctiveSwitchId;
        ClosedUtc = MonitoringText.Utc(closedUtc, nameof(closedUtc));
        Status = AccountingProviderSwitchMonitoringStatuses.Closed;
        NextRunUtc = null;
        ReleaseLease();
        Version++;
    }

    private void RequireLease() { if (LeaseOwner is null) throw new InvalidOperationException("The monitoring pass must hold a lease."); }
    private void ReleaseLease() { LeaseOwner = null; LeaseExpiresUtc = null; }
}

public sealed class AccountingProviderSwitchMonitoringCheck : ICompanyOwnedEntity
{
    private AccountingProviderSwitchMonitoringCheck() { }
    public AccountingProviderSwitchMonitoringCheck(Guid companyId, Guid switchId, Guid monitoringRunId,
        int checkSequence, string checkKey, string status, string severity, bool isBlocking, string reasonCode,
        string explanation, string evidenceJson, string fingerprint, DateTime observedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = MonitoringText.Required(companyId, nameof(companyId));
        SwitchId = MonitoringText.Required(switchId, nameof(switchId)); MonitoringRunId = MonitoringText.Required(monitoringRunId, nameof(monitoringRunId));
        CheckSequence = checkSequence > 0 ? checkSequence : throw new ArgumentOutOfRangeException(nameof(checkSequence));
        CheckKey = MonitoringText.Token(checkKey, nameof(checkKey), 80); Status = MonitoringText.Token(status, nameof(status), 32);
        Severity = MonitoringText.Token(severity, nameof(severity), 24); IsBlocking = isBlocking;
        ReasonCode = MonitoringText.Token(reasonCode, nameof(reasonCode), 100); Explanation = MonitoringText.Required(explanation, nameof(explanation), 1000);
        EvidenceJson = MonitoringText.Required(evidenceJson, nameof(evidenceJson), 16000);
        Fingerprint = MonitoringText.Hash(fingerprint, nameof(fingerprint)); ObservedUtc = MonitoringText.Utc(observedUtc, nameof(observedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid MonitoringRunId { get; private set; } public int CheckSequence { get; private set; }
    public string CheckKey { get; private set; } = null!; public string Status { get; private set; } = null!;
    public string Severity { get; private set; } = null!; public bool IsBlocking { get; private set; }
    public string ReasonCode { get; private set; } = null!; public string Explanation { get; private set; } = null!;
    public string EvidenceJson { get; private set; } = null!; public string Fingerprint { get; private set; } = null!;
    public DateTime ObservedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchMonitoringIncident : ICompanyOwnedEntity
{
    private AccountingProviderSwitchMonitoringIncident() { }
    public AccountingProviderSwitchMonitoringIncident(Guid companyId, Guid switchId, Guid monitoringRunId,
        string fingerprint, string checkKey, string severity, bool isBlocking, string explanation,
        Guid? taskId, DateTime observedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = MonitoringText.Required(companyId, nameof(companyId)); SwitchId = MonitoringText.Required(switchId, nameof(switchId));
        MonitoringRunId = MonitoringText.Required(monitoringRunId, nameof(monitoringRunId)); Fingerprint = MonitoringText.Hash(fingerprint, nameof(fingerprint));
        CheckKey = MonitoringText.Token(checkKey, nameof(checkKey), 80); Severity = MonitoringText.Token(severity, nameof(severity), 24);
        IsBlocking = isBlocking; Explanation = MonitoringText.Required(explanation, nameof(explanation), 1000);
        if (taskId == Guid.Empty) throw new ArgumentException("TaskId cannot be empty.", nameof(taskId)); TaskId = taskId;
        Status = AccountingProviderSwitchMonitoringIncidentStatuses.Open; OccurrenceCount = 1;
        FirstObservedUtc = LastObservedUtc = MonitoringText.Utc(observedUtc, nameof(observedUtc)); Version = 1;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid MonitoringRunId { get; private set; } public string Fingerprint { get; private set; } = null!;
    public string CheckKey { get; private set; } = null!; public string Severity { get; private set; } = null!;
    public bool IsBlocking { get; private set; } public string Explanation { get; private set; } = null!;
    public string Status { get; private set; } = null!; public Guid? TaskId { get; private set; }
    public int OccurrenceCount { get; private set; } public DateTime FirstObservedUtc { get; private set; }
    public DateTime LastObservedUtc { get; private set; } public DateTime? ResolvedUtc { get; private set; }
    public Guid? AcceptedByUserId { get; private set; } public string? ExceptionExplanation { get; private set; }
    public string? ExceptionScope { get; private set; } public decimal? FinancialImpact { get; private set; }
    public string? EvidenceReference { get; private set; } public DateTime? AcceptedUtc { get; private set; }
    public long Version { get; private set; }

    public void ObserveAgain(string severity, bool isBlocking, string explanation, DateTime observedUtc)
    {
        Severity = MonitoringText.Token(severity, nameof(severity), 24); IsBlocking = isBlocking;
        Explanation = MonitoringText.Required(explanation, nameof(explanation), 1000);
        Status = AccountingProviderSwitchMonitoringIncidentStatuses.Open; ResolvedUtc = null;
        LastObservedUtc = MonitoringText.Utc(observedUtc, nameof(observedUtc)); OccurrenceCount++; Version++;
    }
    public void Resolve(DateTime resolvedUtc) { if (Status == AccountingProviderSwitchMonitoringIncidentStatuses.Resolved) return;
        Status = AccountingProviderSwitchMonitoringIncidentStatuses.Resolved; ResolvedUtc = MonitoringText.Utc(resolvedUtc, nameof(resolvedUtc)); Version++; }
    public void AttachTask(Guid taskId) { if (taskId == Guid.Empty) throw new ArgumentException("TaskId is required.", nameof(taskId)); TaskId ??= taskId; Version++; }
    public void AcceptException(Guid actorUserId, string explanation, string scope, decimal financialImpact,
        string evidenceReference, DateTime acceptedUtc)
    {
        if (IsBlocking) throw new InvalidOperationException("A blocking monitoring incident cannot be accepted as a closure exception.");
        AcceptedByUserId = MonitoringText.Required(actorUserId, nameof(actorUserId));
        ExceptionExplanation = MonitoringText.Required(explanation, nameof(explanation), 2000);
        ExceptionScope = MonitoringText.Required(scope, nameof(scope), 500);
        FinancialImpact = financialImpact; EvidenceReference = MonitoringText.Required(evidenceReference, nameof(evidenceReference), 1000);
        AcceptedUtc = MonitoringText.Utc(acceptedUtc, nameof(acceptedUtc)); Status = AccountingProviderSwitchMonitoringIncidentStatuses.AcceptedException; Version++;
    }
}

internal static class MonitoringText
{
    public static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Required(string? value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    public static string Token(string? value, string name, int max) => Required(value, name, max).Replace('-', '_').ToLowerInvariant();
    public static string Hash(string? value, string name) { var text = Required(value, name, 64).ToLowerInvariant();
        return text.Length == 64 && text.All(Uri.IsHexDigit) ? text : throw new ArgumentException($"{name} must be a SHA-256 hash.", name); }
    public static DateTime Utc(DateTime value, string name) => value == default ? throw new ArgumentException($"{name} is required.", name) : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
