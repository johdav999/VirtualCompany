using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class OperatingCycleRequest : ICompanyOwnedEntity
{
    private OperatingCycleRequest() { }
    public OperatingCycleRequest(Guid id, Guid companyId, string triggerType, string? triggerReference,
        string deduplicationKey, string correlationId, DateTime notBeforeUtc, Guid? operatingEventId = null,
        int maxAttempts = 3)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId));
        if (operatingEventId == Guid.Empty || maxAttempts is < 1 or > 10) throw new ArgumentException("Cycle request values are invalid.");
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        TriggerType = OperatingCycle.Text(triggerType, nameof(triggerType), 64);
        TriggerReference = OperatingCycle.Optional(triggerReference, 256);
        DeduplicationKey = OperatingCycle.Text(deduplicationKey, nameof(deduplicationKey), 200);
        CorrelationId = OperatingCycle.Text(correlationId, nameof(correlationId), 128);
        NotBeforeUtc = notBeforeUtc.ToUniversalTime();
        OperatingEventId = operatingEventId;
        MaxAttempts = maxAttempts;
        Status = OperatingCycleRequestStatus.Pending;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
        Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? OperatingEventId { get; private set; }
    public Guid? OperatingCycleId { get; private set; }
    public string TriggerType { get; private set; } = null!;
    public string? TriggerReference { get; private set; }
    public string DeduplicationKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public OperatingCycleRequestStatus Status { get; private set; }
    public DateTime NotBeforeUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public int Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public OperatingEvent? OperatingEvent { get; private set; }
    public OperatingCycle? OperatingCycle { get; private set; }
    public bool TryClaim(string owner, DateTime nowUtc, TimeSpan duration)
    {
        var eligible = Status is OperatingCycleRequestStatus.Pending or OperatingCycleRequestStatus.RetryScheduled ||
            Status is OperatingCycleRequestStatus.Claimed or OperatingCycleRequestStatus.Processing && LeaseExpiresUtc <= nowUtc;
        if (!eligible || NotBeforeUtc > nowUtc) return false;
        LeaseOwner = OperatingCycle.Text(owner, nameof(owner), 128); LeaseExpiresUtc = nowUtc.Add(duration);
        Status = OperatingCycleRequestStatus.Claimed; Touch(nowUtc); return true;
    }
    public void Start(string owner, DateTime nowUtc) { if (Status != OperatingCycleRequestStatus.Claimed || LeaseOwner != owner || LeaseExpiresUtc <= nowUtc) throw new InvalidOperationException("A current cycle-request lease is required."); Status = OperatingCycleRequestStatus.Processing; AttemptCount++; Touch(nowUtc); }
    public void Complete(Guid cycleId, DateTime nowUtc) { if (Status != OperatingCycleRequestStatus.Processing) throw new InvalidOperationException(); OperatingCycleId = OperatingCycle.RequiredId(cycleId, nameof(cycleId)); Status = OperatingCycleRequestStatus.Completed; CompletedUtc = nowUtc; ClearLease(); Touch(nowUtc); }
    public void Suppress(string code, string summary, DateTime nowUtc) { if (Status is not (OperatingCycleRequestStatus.Claimed or OperatingCycleRequestStatus.Processing)) throw new InvalidOperationException(); FailureCode = Token(code); FailureSummary = Text(summary); Status = OperatingCycleRequestStatus.Suppressed; CompletedUtc = nowUtc; ClearLease(); Touch(nowUtc); }
    public void Retry(string code, string summary, DateTime retryUtc, DateTime nowUtc) { if (Status != OperatingCycleRequestStatus.Processing) throw new InvalidOperationException(); FailureCode = Token(code); FailureSummary = Text(summary); ClearLease(); if (AttemptCount >= MaxAttempts) { Status = OperatingCycleRequestStatus.DeadLettered; CompletedUtc = nowUtc; } else { Status = OperatingCycleRequestStatus.RetryScheduled; NotBeforeUtc = retryUtc; } Touch(nowUtc); }
    private void ClearLease() { LeaseOwner = null; LeaseExpiresUtc = null; }
    private void Touch(DateTime now) { UpdatedUtc = now; Version++; }
    private static string Token(string value) => OperatingCycle.Text(value, nameof(value), 100);
    private static string Text(string value) => OperatingCycle.Text(value, nameof(value), 2000);
}
