namespace VirtualCompany.Domain.Entities;

public static class AccountingProviderSwitchRehearsalStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class AccountingProviderSwitchReconciliationResults
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string ManualEvidenceRequired = "manual_evidence_required";
    public const string NotApplicable = "not_applicable";

    public static string Normalize(string value) => value?.Trim().ToLowerInvariant() switch
    {
        Passed => Passed,
        Failed => Failed,
        ManualEvidenceRequired => ManualEvidenceRequired,
        NotApplicable => NotApplicable,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Reconciliation result is not supported.")
    };
}

public sealed class AccountingProviderSwitchRehearsal : ICompanyOwnedEntity
{
    private AccountingProviderSwitchRehearsal() { }

    public AccountingProviderSwitchRehearsal(Guid id, Guid companyId, Guid switchId, Guid requestedByUserId,
        string idempotencyKey, string correlationId, DateTime requestedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = AccountingProviderSwitchRehearsalText.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchRehearsalText.Required(switchId, nameof(switchId));
        RequestedByUserId = AccountingProviderSwitchRehearsalText.Required(requestedByUserId, nameof(requestedByUserId));
        IdempotencyKey = AccountingProviderSwitchRehearsalText.Required(idempotencyKey, nameof(idempotencyKey), 128);
        CorrelationId = AccountingProviderSwitchRehearsalText.Required(correlationId, nameof(correlationId), 128);
        RequestedUtc = AccountingProviderSwitchRehearsalText.Utc(requestedUtc, nameof(requestedUtc));
        NextAttemptUtc = RequestedUtc;
        Status = AccountingProviderSwitchRehearsalStatuses.Queued;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? SimulationKind { get; private set; }
    public bool ProviderAcceptanceProven { get; private set; }
    public string? Disclosure { get; private set; }
    public int CompletedWorkItems { get; private set; }
    public int TotalWorkItems { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitch Switch { get; private set; } = null!;

    public void Claim(string leaseOwner, DateTime leaseExpiresUtc, DateTime nowUtc)
    {
        if (Status is not (AccountingProviderSwitchRehearsalStatuses.Queued or AccountingProviderSwitchRehearsalStatuses.Running))
            throw new InvalidOperationException("Only queued or interrupted rehearsals can be claimed.");
        LeaseOwner = AccountingProviderSwitchRehearsalText.Required(leaseOwner, nameof(leaseOwner), 128);
        LeaseExpiresUtc = AccountingProviderSwitchRehearsalText.Utc(leaseExpiresUtc, nameof(leaseExpiresUtc));
        var now = AccountingProviderSwitchRehearsalText.Utc(nowUtc, nameof(nowUtc));
        Status = AccountingProviderSwitchRehearsalStatuses.Running;
        StartedUtc ??= now;
        AttemptCount++;
        NextAttemptUtc = null;
        Version++;
    }

    public void SetProgress(int completed, int total)
    {
        if (completed < 0 || total < 0 || completed > total) throw new ArgumentOutOfRangeException(nameof(completed));
        CompletedWorkItems = completed;
        TotalWorkItems = total;
        Version++;
    }

    public void Complete(string simulationKind, bool providerAcceptanceProven, string disclosure, DateTime nowUtc)
    {
        SimulationKind = AccountingProviderSwitchRehearsalText.Required(simulationKind, nameof(simulationKind), 32);
        ProviderAcceptanceProven = providerAcceptanceProven;
        Disclosure = AccountingProviderSwitchRehearsalText.Required(disclosure, nameof(disclosure), 1000);
        Status = AccountingProviderSwitchRehearsalStatuses.Completed;
        CompletedWorkItems = TotalWorkItems;
        CompletedUtc = AccountingProviderSwitchRehearsalText.Utc(nowUtc, nameof(nowUtc));
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        FailureCode = null;
        FailureSummary = null;
        Version++;
    }

    public void Retry(string code, string summary, DateTime nextAttemptUtc)
    {
        Status = AccountingProviderSwitchRehearsalStatuses.Queued;
        FailureCode = AccountingProviderSwitchRehearsalText.Required(code, nameof(code), 100);
        FailureSummary = AccountingProviderSwitchRehearsalText.Required(summary, nameof(summary), 1000);
        NextAttemptUtc = AccountingProviderSwitchRehearsalText.Utc(nextAttemptUtc, nameof(nextAttemptUtc));
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        Version++;
    }

    public void Fail(string code, string summary, DateTime nowUtc)
    {
        Status = AccountingProviderSwitchRehearsalStatuses.Failed;
        FailureCode = AccountingProviderSwitchRehearsalText.Required(code, nameof(code), 100);
        FailureSummary = AccountingProviderSwitchRehearsalText.Required(summary, nameof(summary), 1000);
        CompletedUtc = AccountingProviderSwitchRehearsalText.Utc(nowUtc, nameof(nowUtc));
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        NextAttemptUtc = null;
        Version++;
    }
}

public sealed class AccountingProviderSwitchRehearsalInput : ICompanyOwnedEntity
{
    private AccountingProviderSwitchRehearsalInput() { }
    public AccountingProviderSwitchRehearsalInput(Guid id, Guid companyId, Guid switchId, Guid rehearsalId,
        long switchVersion, string strategy, string sourceSnapshotHash, string stagingHash, string mappingHash,
        string gapHash, long stagedRecordCount, decimal financialTotal, string datasetSummaryJson, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = AccountingProviderSwitchRehearsalText.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchRehearsalText.Required(switchId, nameof(switchId));
        RehearsalId = AccountingProviderSwitchRehearsalText.Required(rehearsalId, nameof(rehearsalId));
        SwitchVersion = switchVersion > 0 ? switchVersion : throw new ArgumentOutOfRangeException(nameof(switchVersion));
        Strategy = AccountingProviderSwitchStrategies.Normalize(strategy);
        SourceSnapshotHash = AccountingProviderSwitchRehearsalText.Hash(sourceSnapshotHash, nameof(sourceSnapshotHash));
        StagingHash = AccountingProviderSwitchRehearsalText.Hash(stagingHash, nameof(stagingHash));
        MappingHash = AccountingProviderSwitchRehearsalText.Hash(mappingHash, nameof(mappingHash));
        GapHash = AccountingProviderSwitchRehearsalText.Hash(gapHash, nameof(gapHash));
        StagedRecordCount = stagedRecordCount;
        FinancialTotal = financialTotal;
        DatasetSummaryJson = AccountingProviderSwitchRehearsalText.Json(datasetSummaryJson, nameof(datasetSummaryJson), 16000);
        CreatedUtc = AccountingProviderSwitchRehearsalText.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid RehearsalId { get; private set; }
    public long SwitchVersion { get; private set; }
    public string Strategy { get; private set; } = null!;
    public string SourceSnapshotHash { get; private set; } = null!;
    public string StagingHash { get; private set; } = null!;
    public string MappingHash { get; private set; } = null!;
    public string GapHash { get; private set; } = null!;
    public long StagedRecordCount { get; private set; }
    public decimal FinancialTotal { get; private set; }
    public string DatasetSummaryJson { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchRehearsalDatasetResult : ICompanyOwnedEntity
{
    private AccountingProviderSwitchRehearsalDatasetResult() { }
    public AccountingProviderSwitchRehearsalDatasetResult(Guid companyId, Guid switchId, Guid rehearsalId,
        string dataset, long expectedCount, long observedCount, decimal expectedTotal, decimal observedTotal,
        string? currency, string result, string reasonCode, string evidenceJson, DateTime calculatedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = companyId; SwitchId = switchId; RehearsalId = rehearsalId;
        Dataset = AccountingProviderSwitchRehearsalText.Required(dataset, nameof(dataset), 64);
        ExpectedCount = expectedCount; ObservedCount = observedCount; ExpectedTotal = expectedTotal; ObservedTotal = observedTotal;
        Currency = AccountingProviderSwitchRehearsalText.Optional(currency, 16)?.ToUpperInvariant();
        CurrencyKey = Currency ?? "__none__";
        Result = AccountingProviderSwitchReconciliationResults.Normalize(result);
        ReasonCode = AccountingProviderSwitchRehearsalText.Required(reasonCode, nameof(reasonCode), 100);
        EvidenceJson = AccountingProviderSwitchRehearsalText.Json(evidenceJson, nameof(evidenceJson), 16000);
        CalculatedUtc = AccountingProviderSwitchRehearsalText.Utc(calculatedUtc, nameof(calculatedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid RehearsalId { get; private set; } public string Dataset { get; private set; } = null!;
    public long ExpectedCount { get; private set; } public long ObservedCount { get; private set; }
    public decimal ExpectedTotal { get; private set; } public decimal ObservedTotal { get; private set; }
    public string? Currency { get; private set; } public string CurrencyKey { get; private set; } = null!; public string Result { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!; public string EvidenceJson { get; private set; } = null!;
    public DateTime CalculatedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchReconciliationCheck : ICompanyOwnedEntity
{
    private AccountingProviderSwitchReconciliationCheck() { }
    public AccountingProviderSwitchReconciliationCheck(Guid companyId, Guid switchId, Guid rehearsalId,
        string checkKey, string expectedValue, string observedValue, decimal tolerance, string? currency,
        string result, string reasonCode, string dataSourcesJson, string calculationVersion,
        bool manualEvidenceAllowed, DateTime calculatedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = companyId; SwitchId = switchId; RehearsalId = rehearsalId;
        CheckKey = AccountingProviderSwitchRehearsalText.Required(checkKey, nameof(checkKey), 80);
        ExpectedValue = AccountingProviderSwitchRehearsalText.Required(expectedValue, nameof(expectedValue), 1000);
        ObservedValue = AccountingProviderSwitchRehearsalText.Required(observedValue, nameof(observedValue), 1000);
        Tolerance = tolerance >= 0 ? tolerance : throw new ArgumentOutOfRangeException(nameof(tolerance));
        Currency = AccountingProviderSwitchRehearsalText.Optional(currency, 16)?.ToUpperInvariant();
        CurrencyKey = Currency ?? "__none__";
        Result = AccountingProviderSwitchReconciliationResults.Normalize(result);
        ReasonCode = AccountingProviderSwitchRehearsalText.Required(reasonCode, nameof(reasonCode), 100);
        DataSourcesJson = AccountingProviderSwitchRehearsalText.Json(dataSourcesJson, nameof(dataSourcesJson), 16000);
        CalculationVersion = AccountingProviderSwitchRehearsalText.Required(calculationVersion, nameof(calculationVersion), 32);
        ManualEvidenceAllowed = manualEvidenceAllowed;
        CalculatedUtc = AccountingProviderSwitchRehearsalText.Utc(calculatedUtc, nameof(calculatedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid RehearsalId { get; private set; } public string CheckKey { get; private set; } = null!;
    public string ExpectedValue { get; private set; } = null!; public string ObservedValue { get; private set; } = null!;
    public decimal Tolerance { get; private set; } public string? Currency { get; private set; } public string CurrencyKey { get; private set; } = null!;
    public string Result { get; private set; } = null!; public string ReasonCode { get; private set; } = null!;
    public string DataSourcesJson { get; private set; } = null!; public string CalculationVersion { get; private set; } = null!;
    public bool ManualEvidenceAllowed { get; private set; } public DateTime CalculatedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchManualEvidence : ICompanyOwnedEntity
{
    private AccountingProviderSwitchManualEvidence() { }
    public AccountingProviderSwitchManualEvidence(Guid companyId, Guid switchId, Guid rehearsalId, Guid checkId,
        string inputHash, string explanation, string evidenceReference, Guid recordedByUserId,
        DateTime recordedUtc, DateTime? expiresUtc)
    {
        Id = Guid.NewGuid(); CompanyId = AccountingProviderSwitchRehearsalText.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchRehearsalText.Required(switchId, nameof(switchId));
        RehearsalId = AccountingProviderSwitchRehearsalText.Required(rehearsalId, nameof(rehearsalId));
        CheckId = AccountingProviderSwitchRehearsalText.Required(checkId, nameof(checkId));
        InputHash = AccountingProviderSwitchRehearsalText.Hash(inputHash, nameof(inputHash));
        Explanation = AccountingProviderSwitchRehearsalText.Required(explanation, nameof(explanation), 1000);
        EvidenceReference = AccountingProviderSwitchRehearsalText.Required(evidenceReference, nameof(evidenceReference), 1000);
        RecordedByUserId = AccountingProviderSwitchRehearsalText.Required(recordedByUserId, nameof(recordedByUserId));
        RecordedUtc = AccountingProviderSwitchRehearsalText.Utc(recordedUtc, nameof(recordedUtc));
        ExpiresUtc = expiresUtc.HasValue ? AccountingProviderSwitchRehearsalText.Utc(expiresUtc.Value, nameof(expiresUtc)) : null;
        if (ExpiresUtc <= RecordedUtc) throw new ArgumentException("Evidence expiry must be after it is recorded.", nameof(expiresUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid RehearsalId { get; private set; } public Guid CheckId { get; private set; }
    public string InputHash { get; private set; } = null!; public string Explanation { get; private set; } = null!;
    public string EvidenceReference { get; private set; } = null!; public Guid RecordedByUserId { get; private set; }
    public DateTime RecordedUtc { get; private set; } public DateTime? ExpiresUtc { get; private set; }
}

public sealed class AccountingProviderSwitchCutoverPlan : ICompanyOwnedEntity
{
    private AccountingProviderSwitchCutoverPlan() { }
    public AccountingProviderSwitchCutoverPlan(Guid companyId, Guid switchId, Guid rehearsalId, int planVersion,
        string planHash, string sourceSnapshotHash, string strategy, DateTime freezeStartsUtc, DateTime freezeEndsUtc,
        string recoveryBoundary, string participantsJson, string snapshotJson, Guid generatedByUserId, DateTime generatedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = AccountingProviderSwitchRehearsalText.Required(companyId, nameof(companyId));
        SwitchId = AccountingProviderSwitchRehearsalText.Required(switchId, nameof(switchId));
        RehearsalId = AccountingProviderSwitchRehearsalText.Required(rehearsalId, nameof(rehearsalId));
        PlanVersion = planVersion > 0 ? planVersion : throw new ArgumentOutOfRangeException(nameof(planVersion));
        PlanHash = AccountingProviderSwitchRehearsalText.Hash(planHash, nameof(planHash));
        SourceSnapshotHash = AccountingProviderSwitchRehearsalText.Hash(sourceSnapshotHash, nameof(sourceSnapshotHash));
        Strategy = AccountingProviderSwitchStrategies.Normalize(strategy);
        FreezeStartsUtc = AccountingProviderSwitchRehearsalText.Utc(freezeStartsUtc, nameof(freezeStartsUtc));
        FreezeEndsUtc = AccountingProviderSwitchRehearsalText.Utc(freezeEndsUtc, nameof(freezeEndsUtc));
        if (FreezeEndsUtc <= FreezeStartsUtc) throw new ArgumentException("Freeze window end must be after its start.");
        RecoveryBoundary = AccountingProviderSwitchRehearsalText.Required(recoveryBoundary, nameof(recoveryBoundary), 1000);
        ParticipantsJson = AccountingProviderSwitchRehearsalText.Json(participantsJson, nameof(participantsJson), 8000);
        SnapshotJson = AccountingProviderSwitchRehearsalText.Json(snapshotJson, nameof(snapshotJson), 32000);
        GeneratedByUserId = AccountingProviderSwitchRehearsalText.Required(generatedByUserId, nameof(generatedByUserId));
        GeneratedUtc = AccountingProviderSwitchRehearsalText.Utc(generatedUtc, nameof(generatedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid RehearsalId { get; private set; } public int PlanVersion { get; private set; }
    public string PlanHash { get; private set; } = null!; public string SourceSnapshotHash { get; private set; } = null!;
    public string Strategy { get; private set; } = null!; public DateTime FreezeStartsUtc { get; private set; }
    public DateTime FreezeEndsUtc { get; private set; } public string RecoveryBoundary { get; private set; } = null!;
    public string ParticipantsJson { get; private set; } = null!; public string SnapshotJson { get; private set; } = null!;
    public Guid GeneratedByUserId { get; private set; } public DateTime GeneratedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchPlanApproval : ICompanyOwnedEntity
{
    private AccountingProviderSwitchPlanApproval() { }
    public AccountingProviderSwitchPlanApproval(Guid companyId, Guid switchId, Guid planId, string planHash,
        Guid approvalRequestId, Guid requestedByUserId, DateTime requestedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = companyId; SwitchId = switchId; PlanId = planId;
        PlanHash = AccountingProviderSwitchRehearsalText.Hash(planHash, nameof(planHash));
        ApprovalRequestId = approvalRequestId; RequestedByUserId = requestedByUserId; RequestedUtc = requestedUtc;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid PlanId { get; private set; } public string PlanHash { get; private set; } = null!;
    public Guid ApprovalRequestId { get; private set; } public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedUtc { get; private set; }
}

internal static class AccountingProviderSwitchRehearsalText
{
    public static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Required(string? value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    public static string? Optional(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value));
    public static string Hash(string value, string name) => Required(value, name, 64).Length == 64 ? value.ToLowerInvariant() : throw new ArgumentException($"{name} must be a SHA-256 hash.", name);
    public static string Json(string value, string name, int max) => Required(value, name, max);
    public static DateTime Utc(DateTime value, string name) => value == default ? throw new ArgumentException($"{name} is required.", name) : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
