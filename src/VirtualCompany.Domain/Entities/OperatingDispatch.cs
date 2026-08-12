using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class OperatingDispatch : ICompanyOwnedEntity
{
    private OperatingDispatch() { }

    public OperatingDispatch(Guid id, Guid companyId, Guid initiativeId, Guid taskId,
        OperatingDispatchKind kind, string correlationId, int maxAttempts = 3)
    {
        CompanyId = OperatingCycle.RequiredId(companyId, nameof(companyId));
        InitiativeId = OperatingCycle.RequiredId(initiativeId, nameof(initiativeId));
        TaskId = OperatingCycle.RequiredId(taskId, nameof(taskId));
        if (maxAttempts is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        Kind = kind;
        CorrelationId = OperatingCycle.Text(correlationId, nameof(correlationId), 128);
        MaxAttempts = maxAttempts;
        Status = OperatingDispatchStatus.Pending;
        NextAttemptUtc = CreatedUtc = UpdatedUtc = DateTime.UtcNow;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InitiativeId { get; private set; }
    public Guid TaskId { get; private set; }
    public OperatingDispatchKind Kind { get; private set; }
    public OperatingDispatchStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public int MaxAttempts { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public Guid? OrchestrationRunId { get; private set; }
    public Guid? CollaborationPlanId { get; private set; }
    public string CorrelationId { get; private set; } = null!;
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public int Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public OperatingInitiative Initiative { get; private set; } = null!;
    public WorkTask Task { get; private set; } = null!;

    public bool TryClaim(string leaseOwner, DateTime nowUtc, TimeSpan leaseDuration)
    {
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        var eligible = Status is OperatingDispatchStatus.Pending or OperatingDispatchStatus.RetryScheduled ||
            Status is OperatingDispatchStatus.Claimed or OperatingDispatchStatus.Running && LeaseExpiresUtc <= nowUtc;
        if (!eligible || NextAttemptUtc > nowUtc) return false;
        LeaseOwner = OperatingCycle.Text(leaseOwner, nameof(leaseOwner), 128);
        LeaseExpiresUtc = nowUtc.Add(leaseDuration);
        Status = OperatingDispatchStatus.Claimed;
        Touch(nowUtc);
        return true;
    }

    public void Start(string leaseOwner, DateTime nowUtc)
    {
        if (Status != OperatingDispatchStatus.Claimed || !string.Equals(LeaseOwner, leaseOwner, StringComparison.Ordinal) || LeaseExpiresUtc <= nowUtc)
            throw new InvalidOperationException("A current dispatch lease is required before execution.");
        Status = OperatingDispatchStatus.Running;
        AttemptCount++;
        Touch(nowUtc);
    }

    public void Complete(Guid? orchestrationRunId, Guid? collaborationPlanId, DateTime nowUtc)
    {
        RequireRunning();
        OrchestrationRunId = orchestrationRunId;
        CollaborationPlanId = collaborationPlanId;
        Status = OperatingDispatchStatus.Completed;
        CompletedUtc = nowUtc;
        ClearLease();
        Touch(nowUtc);
    }

    public void AwaitApproval(string summary, DateTime nowUtc)
    {
        RequireRunning();
        Status = OperatingDispatchStatus.AwaitingApproval;
        FailureCode = "approval_required";
        FailureSummary = Text(summary);
        ClearLease();
        Touch(nowUtc);
    }

    public void Retry(string code, string summary, DateTime nextAttemptUtc, DateTime nowUtc)
    {
        RequireRunning();
        FailureCode = Token(code);
        FailureSummary = Text(summary);
        ClearLease();
        if (AttemptCount >= MaxAttempts)
        {
            Status = OperatingDispatchStatus.DeadLettered;
            NextAttemptUtc = null;
        }
        else
        {
            Status = OperatingDispatchStatus.RetryScheduled;
            NextAttemptUtc = nextAttemptUtc > nowUtc ? nextAttemptUtc : nowUtc.AddMinutes(1);
        }
        Touch(nowUtc);
    }

    public void Block(string code, string summary, DateTime nowUtc)
    {
        RequireRunning();
        Status = OperatingDispatchStatus.Blocked;
        FailureCode = Token(code);
        FailureSummary = Text(summary);
        ClearLease();
        Touch(nowUtc);
    }

    private void RequireRunning()
    {
        if (Status != OperatingDispatchStatus.Running) throw new InvalidOperationException("Dispatch is not running.");
    }
    private void ClearLease() { LeaseOwner = null; LeaseExpiresUtc = null; }
    private void Touch(DateTime nowUtc) { UpdatedUtc = nowUtc; Version++; }
    private static string Token(string value) => OperatingCycle.Text(value, nameof(value), 100);
    private static string Text(string value) => OperatingCycle.Text(value, nameof(value), 2000);
}
