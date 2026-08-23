namespace VirtualCompany.Domain.Entities;

public static class AccountingProviderSwitchCutoverStatuses
{
    public const string Queued = "queued";
    public const string Freezing = "freezing";
    public const string Transferring = "transferring";
    public const string Reconciling = "reconciling";
    public const string AwaitingActivationApproval = "awaiting_activation_approval";
    public const string Activating = "activating";
    public const string Activated = "activated";
    public const string Blocked = "blocked";
    public const string Cancelled = "cancelled";
    public const string Recovered = "recovered";
    public const string CorrectiveCutoverRequired = "corrective_cutover_required";

    public static string Normalize(string value) => CutoverText.Token(value, nameof(value), 48) switch
    {
        Queued => Queued,
        Freezing => Freezing,
        Transferring => Transferring,
        Reconciling => Reconciling,
        AwaitingActivationApproval => AwaitingActivationApproval,
        Activating => Activating,
        Activated => Activated,
        Blocked => Blocked,
        Cancelled => Cancelled,
        Recovered => Recovered,
        CorrectiveCutoverRequired => CorrectiveCutoverRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Cutover status is not supported.")
    };
}

public static class AccountingProviderSwitchCutoverCheckResults
{
    public const string Passed = "passed";
    public const string Failed = "failed";

    public static string Normalize(string value) => CutoverText.Token(value, nameof(value), 16) switch
    {
        Passed => Passed,
        Failed => Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Final reconciliation result is not supported.")
    };
}

public sealed class AccountingProviderSwitchCutoverExecution : ICompanyOwnedEntity
{
    private AccountingProviderSwitchCutoverExecution() { }

    public AccountingProviderSwitchCutoverExecution(Guid id, Guid companyId, Guid switchId, Guid planId,
        int planVersion, string planHash, Guid? preparationId, Guid? targetTransferBatchId,
        Guid requestedByUserId, string idempotencyKey, string correlationId, DateTime scheduledUtc,
        DateTime requestedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = CutoverText.Required(companyId, nameof(companyId));
        SwitchId = CutoverText.Required(switchId, nameof(switchId));
        PlanId = CutoverText.Required(planId, nameof(planId));
        PlanVersion = planVersion > 0 ? planVersion : throw new ArgumentOutOfRangeException(nameof(planVersion));
        PlanHash = CutoverText.Hash(planHash, nameof(planHash));
        if (preparationId == Guid.Empty) throw new ArgumentException("PreparationId cannot be empty.", nameof(preparationId));
        if (targetTransferBatchId == Guid.Empty) throw new ArgumentException("TargetTransferBatchId cannot be empty.", nameof(targetTransferBatchId));
        PreparationId = preparationId;
        TargetTransferBatchId = targetTransferBatchId;
        RequestedByUserId = CutoverText.Required(requestedByUserId, nameof(requestedByUserId));
        IdempotencyKey = CutoverText.Required(idempotencyKey, nameof(idempotencyKey), 128);
        CorrelationId = CutoverText.Required(correlationId, nameof(correlationId), 128);
        ScheduledUtc = CutoverText.Utc(scheduledUtc, nameof(scheduledUtc));
        RequestedUtc = CutoverText.Utc(requestedUtc, nameof(requestedUtc));
        Status = AccountingProviderSwitchCutoverStatuses.Queued;
        CurrentStep = "waiting_for_freeze";
        NextAttemptUtc = ScheduledUtc;
        RetryIsSafe = true;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid PlanId { get; private set; }
    public int PlanVersion { get; private set; }
    public string PlanHash { get; private set; } = null!;
    public Guid? PreparationId { get; private set; }
    public Guid? TargetTransferBatchId { get; private set; }
    public Guid? FinalSnapshotId { get; private set; }
    public Guid? AuthorityPeriodId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string CurrentStep { get; private set; } = null!;
    public bool TargetActivityRecorded { get; private set; }
    public bool RetryIsSafe { get; private set; }
    public bool ProviderReconciliationRequired { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public string? NextAction { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public DateTime ScheduledUtc { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? FreezeStartedUtc { get; private set; }
    public DateTime? ReconciledUtc { get; private set; }
    public DateTime? ActivatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; }
    public AccountingProviderSwitch Switch { get; private set; } = null!;
    public AccountingProviderSwitchCutoverPlan Plan { get; private set; } = null!;

    public void Claim(string owner, DateTime leaseExpiresUtc, DateTime nowUtc)
    {
        if (Status is AccountingProviderSwitchCutoverStatuses.Activated or AccountingProviderSwitchCutoverStatuses.Cancelled or
            AccountingProviderSwitchCutoverStatuses.Recovered or AccountingProviderSwitchCutoverStatuses.CorrectiveCutoverRequired)
            throw new InvalidOperationException("A terminal cutover execution cannot be claimed.");
        LeaseOwner = CutoverText.Required(owner, nameof(owner), 128);
        LeaseExpiresUtc = CutoverText.Utc(leaseExpiresUtc, nameof(leaseExpiresUtc));
        if (LeaseExpiresUtc <= CutoverText.Utc(nowUtc, nameof(nowUtc))) throw new ArgumentOutOfRangeException(nameof(leaseExpiresUtc));
        AttemptCount++;
        NextAttemptUtc = null;
        Version++;
    }

    public void BeginFreeze(DateTime nowUtc)
    {
        RequireStatus(AccountingProviderSwitchCutoverStatuses.Queued, AccountingProviderSwitchCutoverStatuses.Blocked);
        Status = AccountingProviderSwitchCutoverStatuses.Freezing;
        CurrentStep = "final_source_freeze";
        FreezeStartedUtc ??= CutoverText.Utc(nowUtc, nameof(nowUtc));
        ClearFailure();
        Version++;
    }

    public void RecordFrozen(Guid snapshotId, Guid authorityPeriodId, DateTime nowUtc)
    {
        RequireStatus(AccountingProviderSwitchCutoverStatuses.Freezing);
        FinalSnapshotId = CutoverText.Required(snapshotId, nameof(snapshotId));
        AuthorityPeriodId = CutoverText.Required(authorityPeriodId, nameof(authorityPeriodId));
        Status = AccountingProviderSwitchCutoverStatuses.Transferring;
        CurrentStep = "approved_final_transfer";
        RetryIsSafe = true;
        NextAttemptUtc = CutoverText.Utc(nowUtc, nameof(nowUtc));
        ReleaseLease();
        Version++;
    }

    public void WaitForTransfer(DateTime nextAttemptUtc, string explanation)
    {
        RequireStatus(AccountingProviderSwitchCutoverStatuses.Transferring);
        CurrentStep = "approved_final_transfer";
        NextAttemptUtc = CutoverText.Utc(nextAttemptUtc, nameof(nextAttemptUtc));
        NextAction = CutoverText.Required(explanation, nameof(explanation), 1000);
        ReleaseLease();
        Version++;
    }

    public void RecordTargetActivity() { TargetActivityRecorded = true; RetryIsSafe = false; Version++; }

    public void BeginReconciliation(DateTime nowUtc)
    {
        RequireStatus(AccountingProviderSwitchCutoverStatuses.Transferring);
        Status = AccountingProviderSwitchCutoverStatuses.Reconciling;
        CurrentStep = "final_reconciliation";
        NextAttemptUtc = CutoverText.Utc(nowUtc, nameof(nowUtc));
        ReleaseLease();
        Version++;
    }

    public void AwaitActivationApproval(DateTime nowUtc)
    {
        RequireStatus(AccountingProviderSwitchCutoverStatuses.Reconciling);
        Status = AccountingProviderSwitchCutoverStatuses.AwaitingActivationApproval;
        CurrentStep = "activation_approval";
        ReconciledUtc = CutoverText.Utc(nowUtc, nameof(nowUtc));
        RetryIsSafe = false;
        NextAttemptUtc = null;
        ReleaseLease();
        Version++;
    }

    public void BeginActivation()
    {
        RequireStatus(AccountingProviderSwitchCutoverStatuses.AwaitingActivationApproval);
        Status = AccountingProviderSwitchCutoverStatuses.Activating;
        CurrentStep = "atomic_authority_activation";
        Version++;
    }

    public void CompleteActivation(DateTime nowUtc)
    {
        RequireStatus(AccountingProviderSwitchCutoverStatuses.Activating);
        Status = AccountingProviderSwitchCutoverStatuses.Activated;
        CurrentStep = "target_authoritative";
        ActivatedUtc = CutoverText.Utc(nowUtc, nameof(nowUtc));
        CompletedUtc = ActivatedUtc;
        NextAction = "Monitor the target accounting system and reconcile post-activation activity.";
        ReleaseLease();
        Version++;
    }

    public void Block(string code, string summary, bool retryIsSafe, bool providerReconciliationRequired,
        string nextAction, DateTime nowUtc)
    {
        Status = AccountingProviderSwitchCutoverStatuses.Blocked;
        CurrentStep = providerReconciliationRequired ? "provider_reconciliation" : CurrentStep;
        FailureCode = CutoverText.Token(code, nameof(code), 100);
        FailureSummary = CutoverText.Required(summary, nameof(summary), 1000);
        RetryIsSafe = retryIsSafe;
        ProviderReconciliationRequired = providerReconciliationRequired;
        NextAction = CutoverText.Required(nextAction, nameof(nextAction), 1000);
        NextAttemptUtc = null;
        CompletedUtc = CutoverText.Utc(nowUtc, nameof(nowUtc));
        ReleaseLease();
        Version++;
    }

    public void Resume(DateTime nowUtc)
    {
        RequireStatus(AccountingProviderSwitchCutoverStatuses.Blocked);
        if (!RetryIsSafe || ProviderReconciliationRequired)
            throw new InvalidOperationException("This cutover cannot be retried until provider reconciliation is complete.");
        Status = FinalSnapshotId.HasValue ? AccountingProviderSwitchCutoverStatuses.Transferring : AccountingProviderSwitchCutoverStatuses.Queued;
        CurrentStep = FinalSnapshotId.HasValue ? "approved_final_transfer" : "waiting_for_freeze";
        NextAttemptUtc = CutoverText.Utc(nowUtc, nameof(nowUtc));
        CompletedUtc = null;
        ClearFailure();
        Version++;
    }

    public void Cancel(DateTime nowUtc)
    {
        RequireStatus(AccountingProviderSwitchCutoverStatuses.Queued);
        Status = AccountingProviderSwitchCutoverStatuses.Cancelled;
        CurrentStep = "cancelled_before_freeze";
        CompletedUtc = CutoverText.Utc(nowUtc, nameof(nowUtc));
        NextAttemptUtc = null;
        ReleaseLease();
        Version++;
    }

    public void RecordRecovery(bool correctiveCutoverRequired, string summary, DateTime nowUtc)
    {
        Status = correctiveCutoverRequired
            ? AccountingProviderSwitchCutoverStatuses.CorrectiveCutoverRequired
            : AccountingProviderSwitchCutoverStatuses.Recovered;
        CurrentStep = correctiveCutoverRequired ? "corrective_cutover_required" : "source_authority_restored";
        FailureSummary = CutoverText.Required(summary, nameof(summary), 1000);
        RetryIsSafe = false;
        NextAction = correctiveCutoverRequired
            ? "Reconcile target activity and schedule a new controlled cutover at a valid period boundary."
            : "Review the recovered source authority before planning another cutover.";
        CompletedUtc = CutoverText.Utc(nowUtc, nameof(nowUtc));
        NextAttemptUtc = null;
        ReleaseLease();
        Version++;
    }

    private void RequireStatus(params string[] allowed)
    {
        if (!allowed.Contains(Status, StringComparer.Ordinal))
            throw new InvalidOperationException($"Cutover cannot continue from '{Status}'.");
    }

    private void ClearFailure()
    {
        FailureCode = null;
        FailureSummary = null;
        ProviderReconciliationRequired = false;
        NextAction = null;
    }

    private void ReleaseLease() { LeaseOwner = null; LeaseExpiresUtc = null; }
}

public sealed class AccountingProviderSwitchFinalSnapshot : ICompanyOwnedEntity
{
    private AccountingProviderSwitchFinalSnapshot() { }

    public AccountingProviderSwitchFinalSnapshot(Guid companyId, Guid switchId, Guid executionId,
        string approvedSourceSnapshotHash, string finalSourceSnapshotHash, string stagingHash,
        string mappingHash, string gapHash, long recordCount, decimal financialTotal, long deltaRecordCount,
        decimal deltaFinancialTotal, string snapshotJson, DateTime extractionStartedUtc,
        DateTime extractionCompletedUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = CutoverText.Required(companyId, nameof(companyId));
        SwitchId = CutoverText.Required(switchId, nameof(switchId));
        ExecutionId = CutoverText.Required(executionId, nameof(executionId));
        ApprovedSourceSnapshotHash = CutoverText.Hash(approvedSourceSnapshotHash, nameof(approvedSourceSnapshotHash));
        FinalSourceSnapshotHash = CutoverText.Hash(finalSourceSnapshotHash, nameof(finalSourceSnapshotHash));
        StagingHash = CutoverText.Hash(stagingHash, nameof(stagingHash));
        MappingHash = CutoverText.Hash(mappingHash, nameof(mappingHash));
        GapHash = CutoverText.Hash(gapHash, nameof(gapHash));
        if (recordCount < 0 || deltaRecordCount < 0) throw new ArgumentOutOfRangeException(nameof(recordCount));
        RecordCount = recordCount;
        FinancialTotal = financialTotal;
        DeltaRecordCount = deltaRecordCount;
        DeltaFinancialTotal = deltaFinancialTotal;
        SnapshotJson = CutoverText.Required(snapshotJson, nameof(snapshotJson), 64000);
        ExtractionStartedUtc = CutoverText.Utc(extractionStartedUtc, nameof(extractionStartedUtc));
        ExtractionCompletedUtc = CutoverText.Utc(extractionCompletedUtc, nameof(extractionCompletedUtc));
        if (ExtractionCompletedUtc < ExtractionStartedUtc) throw new ArgumentOutOfRangeException(nameof(extractionCompletedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid ExecutionId { get; private set; }
    public string ApprovedSourceSnapshotHash { get; private set; } = null!;
    public string FinalSourceSnapshotHash { get; private set; } = null!;
    public string StagingHash { get; private set; } = null!;
    public string MappingHash { get; private set; } = null!;
    public string GapHash { get; private set; } = null!;
    public long RecordCount { get; private set; }
    public decimal FinancialTotal { get; private set; }
    public long DeltaRecordCount { get; private set; }
    public decimal DeltaFinancialTotal { get; private set; }
    public string SnapshotJson { get; private set; } = null!;
    public DateTime ExtractionStartedUtc { get; private set; }
    public DateTime ExtractionCompletedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchFinalCheck : ICompanyOwnedEntity
{
    private AccountingProviderSwitchFinalCheck() { }
    public AccountingProviderSwitchFinalCheck(Guid companyId, Guid switchId, Guid executionId, string checkKey,
        string result, string reasonCode, string explanation, string evidenceJson, DateTime calculatedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = CutoverText.Required(companyId, nameof(companyId));
        SwitchId = CutoverText.Required(switchId, nameof(switchId)); ExecutionId = CutoverText.Required(executionId, nameof(executionId));
        CheckKey = CutoverText.Token(checkKey, nameof(checkKey), 80);
        Result = AccountingProviderSwitchCutoverCheckResults.Normalize(result);
        ReasonCode = CutoverText.Token(reasonCode, nameof(reasonCode), 100);
        Explanation = CutoverText.Required(explanation, nameof(explanation), 1000);
        EvidenceJson = CutoverText.Required(evidenceJson, nameof(evidenceJson), 16000);
        CalculatedUtc = CutoverText.Utc(calculatedUtc, nameof(calculatedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid ExecutionId { get; private set; } public string CheckKey { get; private set; } = null!;
    public string Result { get; private set; } = null!; public string ReasonCode { get; private set; } = null!;
    public string Explanation { get; private set; } = null!; public string EvidenceJson { get; private set; } = null!;
    public DateTime CalculatedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchActivationApproval : ICompanyOwnedEntity
{
    private AccountingProviderSwitchActivationApproval() { }
    public AccountingProviderSwitchActivationApproval(Guid companyId, Guid switchId, Guid executionId,
        Guid finalSnapshotId, string finalSnapshotHash, string reconciliationHash, long switchVersion,
        Guid approvalRequestId, Guid requestedByUserId, DateTime requestedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = CutoverText.Required(companyId, nameof(companyId));
        SwitchId = CutoverText.Required(switchId, nameof(switchId)); ExecutionId = CutoverText.Required(executionId, nameof(executionId));
        FinalSnapshotId = CutoverText.Required(finalSnapshotId, nameof(finalSnapshotId));
        FinalSnapshotHash = CutoverText.Hash(finalSnapshotHash, nameof(finalSnapshotHash));
        ReconciliationHash = CutoverText.Hash(reconciliationHash, nameof(reconciliationHash));
        SwitchVersion = switchVersion > 0 ? switchVersion : throw new ArgumentOutOfRangeException(nameof(switchVersion));
        ApprovalRequestId = CutoverText.Required(approvalRequestId, nameof(approvalRequestId));
        RequestedByUserId = CutoverText.Required(requestedByUserId, nameof(requestedByUserId));
        RequestedUtc = CutoverText.Utc(requestedUtc, nameof(requestedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid ExecutionId { get; private set; } public Guid FinalSnapshotId { get; private set; }
    public string FinalSnapshotHash { get; private set; } = null!; public string ReconciliationHash { get; private set; } = null!;
    public long SwitchVersion { get; private set; } public Guid ApprovalRequestId { get; private set; }
    public Guid RequestedByUserId { get; private set; } public DateTime RequestedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchNativeMaterialization : ICompanyOwnedEntity
{
    private AccountingProviderSwitchNativeMaterialization() { }
    public AccountingProviderSwitchNativeMaterialization(Guid companyId, Guid switchId, Guid executionId,
        Guid candidateId, string candidateHash, Guid targetRecordId, string targetRecordType, DateTime materializedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = CutoverText.Required(companyId, nameof(companyId));
        SwitchId = CutoverText.Required(switchId, nameof(switchId)); ExecutionId = CutoverText.Required(executionId, nameof(executionId));
        CandidateId = CutoverText.Required(candidateId, nameof(candidateId)); CandidateHash = CutoverText.Hash(candidateHash, nameof(candidateHash));
        TargetRecordId = CutoverText.Required(targetRecordId, nameof(targetRecordId));
        TargetRecordType = CutoverText.Token(targetRecordType, nameof(targetRecordType), 64);
        MaterializedUtc = CutoverText.Utc(materializedUtc, nameof(materializedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid ExecutionId { get; private set; } public Guid CandidateId { get; private set; }
    public string CandidateHash { get; private set; } = null!; public Guid TargetRecordId { get; private set; }
    public string TargetRecordType { get; private set; } = null!; public DateTime MaterializedUtc { get; private set; }
}

file static class CutoverText
{
    public static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Required(string? value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    public static string Token(string? value, string name, int max) => Required(value, name, max).Replace('-', '_').ToLowerInvariant();
    public static string Hash(string? value, string name)
    {
        var normalized = Required(value, name, 64).ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit) ? normalized : throw new ArgumentException($"{name} must be a SHA-256 hash.", name);
    }
    public static DateTime Utc(DateTime value, string name) => value == default ? throw new ArgumentException($"{name} is required.", name) : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
