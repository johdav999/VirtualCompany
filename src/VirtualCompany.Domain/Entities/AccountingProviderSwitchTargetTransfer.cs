namespace VirtualCompany.Domain.Entities;

public static class AccountingProviderSwitchTargetTransferBatchStatuses
{
    public const string Queued = "queued";
    public const string Building = "building";
    public const string AwaitingApproval = "awaiting_approval";
    public const string ReadyForCutover = "ready_for_cutover";
    public const string Failed = "failed";
    public const string ReconciliationRequired = "reconciliation_required";

    public static string Normalize(string value) => TargetTransferText.Token(value, nameof(value), 32) switch
    {
        Queued => Queued, Building => Building, AwaitingApproval => AwaitingApproval,
        ReadyForCutover => ReadyForCutover, Failed => Failed, ReconciliationRequired => ReconciliationRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Target transfer batch status is not supported.")
    };
}

public static class AccountingProviderSwitchTargetTransferItemStatuses
{
    public const string Planned = "planned";
    public const string PreviewValidated = "preview_validated";
    public const string AwaitingApproval = "awaiting_approval";
    public const string Executing = "executing";
    public const string Succeeded = "succeeded";
    public const string HeldForCutover = "held_for_cutover";
    public const string Failed = "failed";
    public const string ReconciliationRequired = "reconciliation_required";

    public static string Normalize(string value) => TargetTransferText.Token(value, nameof(value), 32) switch
    {
        Planned => Planned, PreviewValidated => PreviewValidated, AwaitingApproval => AwaitingApproval,
        Executing => Executing, Succeeded => Succeeded, HeldForCutover => HeldForCutover,
        Failed => Failed, ReconciliationRequired => ReconciliationRequired,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Target transfer item status is not supported.")
    };
}

public sealed class AccountingProviderSwitchTargetTransferBatch : ICompanyOwnedEntity
{
    private AccountingProviderSwitchTargetTransferBatch() { }

    public AccountingProviderSwitchTargetTransferBatch(Guid id, Guid companyId, Guid switchId, Guid planId,
        int planVersion, string planHash, string targetProviderKey, string packageHash, Guid requestedByUserId,
        string idempotencyKey, string correlationId, DateTime requestedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = TargetTransferText.Guid(companyId, nameof(companyId));
        SwitchId = TargetTransferText.Guid(switchId, nameof(switchId));
        PlanId = TargetTransferText.Guid(planId, nameof(planId));
        if (planVersion <= 0) throw new ArgumentOutOfRangeException(nameof(planVersion));
        PlanVersion = planVersion;
        PlanHash = TargetTransferText.Hash(planHash, nameof(planHash));
        TargetProviderKey = TargetTransferText.Token(targetProviderKey, nameof(targetProviderKey), 64);
        PackageHash = TargetTransferText.Hash(packageHash, nameof(packageHash));
        RequestedByUserId = TargetTransferText.Guid(requestedByUserId, nameof(requestedByUserId));
        IdempotencyKey = TargetTransferText.Required(idempotencyKey, nameof(idempotencyKey), 128);
        CorrelationId = TargetTransferText.Required(correlationId, nameof(correlationId), 128);
        RequestedUtc = TargetTransferText.Utc(requestedUtc, nameof(requestedUtc));
        NextAttemptUtc = RequestedUtc;
        Status = AccountingProviderSwitchTargetTransferBatchStatuses.Queued;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid PlanId { get; private set; }
    public int PlanVersion { get; private set; }
    public string PlanHash { get; private set; } = null!;
    public string TargetProviderKey { get; private set; } = null!;
    public string PackageHash { get; private set; } = null!;
    public Guid RequestedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; set; } = null!;
    public int TotalItemCount { get; private set; }
    public int PreviewItemCount { get; private set; }
    public int PreparatoryItemCount { get; private set; }
    public int FinalItemCount { get; private set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptUtc { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseExpiresUtc { get; set; }
    public string? FailureCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public DateTime RequestedUtc { get; private set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; private set; }
    public long Version { get; set; }
    public Company Company { get; private set; } = null!;
    public AccountingProviderSwitch Switch { get; private set; } = null!;
    public AccountingProviderSwitchCutoverPlan Plan { get; private set; } = null!;

    public void CompleteBuild(int total, int preview, int preparatory, int final, DateTime completedUtc)
    {
        if (Status != AccountingProviderSwitchTargetTransferBatchStatuses.Building)
            throw new InvalidOperationException("The target transfer batch must be building before it can complete.");
        if (total < 0 || preview < 0 || preparatory < 0 || final < 0 || preview + preparatory + final != total)
            throw new ArgumentOutOfRangeException(nameof(total));
        TotalItemCount = total; PreviewItemCount = preview; PreparatoryItemCount = preparatory; FinalItemCount = final;
        Status = preparatory > 0
            ? AccountingProviderSwitchTargetTransferBatchStatuses.AwaitingApproval
            : AccountingProviderSwitchTargetTransferBatchStatuses.ReadyForCutover;
        CompletedUtc = TargetTransferText.Utc(completedUtc, nameof(completedUtc));
        LeaseOwner = null; LeaseExpiresUtc = null; NextAttemptUtc = null; FailureCode = null; FailureSummary = null;
        Version++;
    }

    public void RequireReconciliation(string code, string summary, DateTime utc)
    {
        Status = AccountingProviderSwitchTargetTransferBatchStatuses.ReconciliationRequired;
        FailureCode = TargetTransferText.Token(code, nameof(code), 100);
        FailureSummary = TargetTransferText.Required(summary, nameof(summary), 1000);
        CompletedUtc = TargetTransferText.Utc(utc, nameof(utc));
        LeaseOwner = null; LeaseExpiresUtc = null; NextAttemptUtc = null; Version++;
    }

    public void Fail(string code, string summary, DateTime utc)
    {
        Status = AccountingProviderSwitchTargetTransferBatchStatuses.Failed;
        FailureCode = TargetTransferText.Token(code, nameof(code), 100);
        FailureSummary = TargetTransferText.Required(summary, nameof(summary), 1000);
        CompletedUtc = TargetTransferText.Utc(utc, nameof(utc));
        LeaseOwner = null; LeaseExpiresUtc = null; NextAttemptUtc = null; Version++;
    }

    public void Retry(string code, string summary, DateTime nextUtc)
    {
        Status = AccountingProviderSwitchTargetTransferBatchStatuses.Queued;
        FailureCode = TargetTransferText.Token(code, nameof(code), 100);
        FailureSummary = TargetTransferText.Required(summary, nameof(summary), 1000);
        NextAttemptUtc = TargetTransferText.Utc(nextUtc, nameof(nextUtc));
        LeaseOwner = null; LeaseExpiresUtc = null; CompletedUtc = null; Version++;
    }

    public void QueueReplay(string correlationId, DateTime utc)
    {
        if (Status is not AccountingProviderSwitchTargetTransferBatchStatuses.Failed)
            throw new InvalidOperationException("Only a failed target transfer batch can be replayed.");
        CorrelationId = TargetTransferText.Required(correlationId, nameof(correlationId), 128);
        Status = AccountingProviderSwitchTargetTransferBatchStatuses.Queued;
        NextAttemptUtc = TargetTransferText.Utc(utc, nameof(utc));
        FailureCode = null; FailureSummary = null; CompletedUtc = null; Version++;
    }
}

public sealed class AccountingProviderSwitchTargetTransferItem : ICompanyOwnedEntity
{
    private AccountingProviderSwitchTargetTransferItem() { }

    public AccountingProviderSwitchTargetTransferItem(Guid id, Guid companyId, Guid switchId, Guid batchId,
        Guid stagedRecordId, string dataset, string sourceIdentity, string sourceVersion, string sourceHash,
        string normalizedHash, int? mappingVersion, string operationMode, string action, string stableIdentity,
        string payloadHash, string safePayloadSummary, DateTime createdUtc)
        : this(id, companyId, switchId, batchId, stagedRecordId, dataset, sourceIdentity, sourceVersion,
            sourceHash, normalizedHash, mappingVersion, operationMode, action, stableIdentity, payloadHash,
            safePayloadSummary, null, null, null, null, null, createdUtc)
    {
    }

    public AccountingProviderSwitchTargetTransferItem(Guid id, Guid companyId, Guid switchId, Guid batchId,
        Guid stagedRecordId, string dataset, string sourceIdentity, string sourceVersion, string sourceHash,
        string normalizedHash, int? mappingVersion, string operationMode, string action, string stableIdentity,
        string payloadHash, string safePayloadSummary, string? commandType, string? httpMethod, string? path,
        string? sanitizedPayloadJson, string? providerPayloadType, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = TargetTransferText.Guid(companyId, nameof(companyId));
        SwitchId = TargetTransferText.Guid(switchId, nameof(switchId)); BatchId = TargetTransferText.Guid(batchId, nameof(batchId));
        StagedRecordId = TargetTransferText.Guid(stagedRecordId, nameof(stagedRecordId));
        Dataset = AccountingProviderSwitchStagingDatasets.Normalize(dataset);
        SourceIdentity = TargetTransferText.Required(sourceIdentity, nameof(sourceIdentity), 256);
        SourceVersion = TargetTransferText.Required(sourceVersion, nameof(sourceVersion), 128);
        SourceHash = TargetTransferText.Hash(sourceHash, nameof(sourceHash)); NormalizedHash = TargetTransferText.Hash(normalizedHash, nameof(normalizedHash));
        if (mappingVersion <= 0) throw new ArgumentOutOfRangeException(nameof(mappingVersion));
        MappingVersion = mappingVersion; OperationMode = NormalizeMode(operationMode); Action = TargetTransferText.Token(action, nameof(action), 80);
        StableIdentity = TargetTransferText.Hash(stableIdentity, nameof(stableIdentity)); PayloadHash = TargetTransferText.Hash(payloadHash, nameof(payloadHash));
        SafePayloadSummary = TargetTransferText.Required(safePayloadSummary, nameof(safePayloadSummary), 1000);
        CommandType = TargetTransferText.Optional(commandType, nameof(commandType), 80);
        HttpMethod = TargetTransferText.Optional(httpMethod, nameof(httpMethod), 16)?.ToUpperInvariant();
        Path = TargetTransferText.Optional(path, nameof(path), 512);
        SanitizedPayloadJson = TargetTransferText.Optional(sanitizedPayloadJson, nameof(sanitizedPayloadJson), 64000);
        ProviderPayloadType = TargetTransferText.Optional(providerPayloadType, nameof(providerPayloadType), 128);
        Status = AccountingProviderSwitchTargetTransferItemStatuses.Planned;
        CreatedUtc = TargetTransferText.Utc(createdUtc, nameof(createdUtc)); UpdatedUtc = CreatedUtc; Version = 1;
    }

    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid BatchId { get; private set; } public Guid StagedRecordId { get; private set; }
    public string Dataset { get; private set; } = null!; public string SourceIdentity { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!; public string SourceHash { get; private set; } = null!;
    public string NormalizedHash { get; private set; } = null!; public int? MappingVersion { get; private set; }
    public string OperationMode { get; private set; } = null!; public string Action { get; private set; } = null!;
    public string StableIdentity { get; private set; } = null!; public string PayloadHash { get; private set; } = null!;
    public string SafePayloadSummary { get; private set; } = null!; public string Status { get; private set; } = null!;
    public string? CommandType { get; private set; } public string? HttpMethod { get; private set; }
    public string? Path { get; private set; } public string? SanitizedPayloadJson { get; private set; }
    public string? ProviderPayloadType { get; private set; }
    public Guid? WriteRequestId { get; private set; } public Guid? ApprovalRequestId { get; private set; }
    public string? ProviderExternalId { get; private set; } public string? FailureCategory { get; private set; }
    public string? SafeSummary { get; private set; } public bool ReconciliationNeeded { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public long Version { get; private set; }

    public void MarkPreviewValidated(string summary, DateTime utc) => Set(AccountingProviderSwitchTargetTransferItemStatuses.PreviewValidated, summary, utc);
    public void HoldForCutover(string summary, DateTime utc) => Set(AccountingProviderSwitchTargetTransferItemStatuses.HeldForCutover, summary, utc);
    public void AttachApproval(Guid writeRequestId, Guid approvalRequestId, DateTime utc)
    {
        WriteRequestId = TargetTransferText.Guid(writeRequestId, nameof(writeRequestId)); ApprovalRequestId = TargetTransferText.Guid(approvalRequestId, nameof(approvalRequestId));
        Set(AccountingProviderSwitchTargetTransferItemStatuses.AwaitingApproval, "This preparatory provider write is waiting for separate approval.", utc);
    }
    public void AttachFinalApproval(Guid writeRequestId, Guid approvalRequestId, DateTime utc)
    {
        if (OperationMode != "final_authoritative" ||
            Status != AccountingProviderSwitchTargetTransferItemStatuses.HeldForCutover)
            throw new InvalidOperationException("Only a final operation held for cutover can receive its execution approval.");
        WriteRequestId = TargetTransferText.Guid(writeRequestId, nameof(writeRequestId));
        ApprovalRequestId = TargetTransferText.Guid(approvalRequestId, nameof(approvalRequestId));
        Set(AccountingProviderSwitchTargetTransferItemStatuses.AwaitingApproval,
            "The final provider operation is waiting for its bound approval.", utc);
    }
    public void StartAttempt(DateTime utc) => Set(AccountingProviderSwitchTargetTransferItemStatuses.Executing, "The approved preparatory provider write is executing.", utc);
    public void Succeed(string? externalId, string summary, DateTime utc)
    {
        ProviderExternalId = TargetTransferText.Optional(externalId, nameof(externalId), 256);
        FailureCategory = null; ReconciliationNeeded = false; Set(AccountingProviderSwitchTargetTransferItemStatuses.Succeeded, summary, utc);
    }
    public void Fail(string category, string summary, bool ambiguous, DateTime utc)
    {
        FailureCategory = TargetTransferText.Token(category, nameof(category), 100); ReconciliationNeeded = ambiguous;
        Set(ambiguous ? AccountingProviderSwitchTargetTransferItemStatuses.ReconciliationRequired : AccountingProviderSwitchTargetTransferItemStatuses.Failed, summary, utc);
    }
    public void Reconcile(bool succeeded, string? externalId, string summary, DateTime utc)
    {
        if (!ReconciliationNeeded) throw new InvalidOperationException("This target transfer item does not require reconciliation.");
        if (succeeded) Succeed(externalId, summary, utc);
        else { ReconciliationNeeded = false; FailureCategory = "provider_confirmed_not_applied"; Set(AccountingProviderSwitchTargetTransferItemStatuses.Failed, summary, utc); }
    }
    private void Set(string status, string summary, DateTime utc)
    {
        Status = AccountingProviderSwitchTargetTransferItemStatuses.Normalize(status);
        SafeSummary = TargetTransferText.Required(summary, nameof(summary), 1000); UpdatedUtc = TargetTransferText.Utc(utc, nameof(utc)); Version++;
    }
    private static string NormalizeMode(string value) => TargetTransferText.Token(value, nameof(value), 32) switch
    {
        "preview_only" => "preview_only", "preparatory_non_posting" => "preparatory_non_posting", "final_authoritative" => "final_authoritative",
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Target operation mode is not supported.")
    };
}

public sealed class AccountingProviderSwitchTargetTransferAttempt : ICompanyOwnedEntity
{
    private AccountingProviderSwitchTargetTransferAttempt() { }
    public AccountingProviderSwitchTargetTransferAttempt(Guid companyId, Guid switchId, Guid batchId, Guid itemId,
        int attemptNumber, string outcome, string? failureCategory, string? safeSummary, bool providerAcceptedRequest,
        DateTime startedUtc, DateTime? completedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = TargetTransferText.Guid(companyId, nameof(companyId)); SwitchId = TargetTransferText.Guid(switchId, nameof(switchId));
        BatchId = TargetTransferText.Guid(batchId, nameof(batchId)); ItemId = TargetTransferText.Guid(itemId, nameof(itemId));
        if (attemptNumber <= 0) throw new ArgumentOutOfRangeException(nameof(attemptNumber)); AttemptNumber = attemptNumber;
        Outcome = TargetTransferText.Token(outcome, nameof(outcome), 32); FailureCategory = TargetTransferText.Optional(failureCategory, nameof(failureCategory), 100);
        SafeSummary = TargetTransferText.Optional(safeSummary, nameof(safeSummary), 1000); ProviderAcceptedRequest = providerAcceptedRequest;
        StartedUtc = TargetTransferText.Utc(startedUtc, nameof(startedUtc)); CompletedUtc = completedUtc.HasValue ? TargetTransferText.Utc(completedUtc.Value, nameof(completedUtc)) : null;
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid BatchId { get; private set; } public Guid ItemId { get; private set; } public int AttemptNumber { get; private set; }
    public string Outcome { get; private set; } = null!; public string? FailureCategory { get; private set; }
    public string? SafeSummary { get; private set; } public bool ProviderAcceptedRequest { get; private set; }
    public DateTime StartedUtc { get; private set; } public DateTime? CompletedUtc { get; private set; }
    public void Complete(string outcome, string? failureCategory, string? safeSummary,
        bool providerAcceptedRequest, DateTime completedUtc)
    {
        if (CompletedUtc.HasValue) return;
        Outcome = TargetTransferText.Token(outcome, nameof(outcome), 32);
        FailureCategory = TargetTransferText.Optional(failureCategory, nameof(failureCategory), 100);
        SafeSummary = TargetTransferText.Optional(safeSummary, nameof(safeSummary), 1000);
        ProviderAcceptedRequest = providerAcceptedRequest;
        CompletedUtc = TargetTransferText.Utc(completedUtc, nameof(completedUtc));
    }
}

public sealed class AccountingProviderSwitchTargetAcknowledgement : ICompanyOwnedEntity
{
    private AccountingProviderSwitchTargetAcknowledgement() { }
    public AccountingProviderSwitchTargetAcknowledgement(Guid companyId, Guid switchId, Guid batchId, Guid itemId,
        string providerKey, string? externalId, string acknowledgementHash, string safeSummary, DateTime receivedUtc)
    {
        Id = Guid.NewGuid(); CompanyId = TargetTransferText.Guid(companyId, nameof(companyId)); SwitchId = TargetTransferText.Guid(switchId, nameof(switchId));
        BatchId = TargetTransferText.Guid(batchId, nameof(batchId)); ItemId = TargetTransferText.Guid(itemId, nameof(itemId));
        ProviderKey = TargetTransferText.Token(providerKey, nameof(providerKey), 64); ExternalId = TargetTransferText.Optional(externalId, nameof(externalId), 256);
        AcknowledgementHash = TargetTransferText.Hash(acknowledgementHash, nameof(acknowledgementHash)); SafeSummary = TargetTransferText.Required(safeSummary, nameof(safeSummary), 1000);
        ReceivedUtc = TargetTransferText.Utc(receivedUtc, nameof(receivedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid SwitchId { get; private set; }
    public Guid BatchId { get; private set; } public Guid ItemId { get; private set; } public string ProviderKey { get; private set; } = null!;
    public string? ExternalId { get; private set; } public string AcknowledgementHash { get; private set; } = null!;
    public string SafeSummary { get; private set; } = null!; public DateTime ReceivedUtc { get; private set; }
}

file static class TargetTransferText
{
    public static Guid Guid(Guid value, string name) => value == System.Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Required(string? value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    public static string Token(string? value, string name, int max) => Required(value, name, max).Replace('-', '_').ToLowerInvariant();
    public static string Hash(string? value, string name) { var hash = Required(value, name, 64).ToLowerInvariant(); return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? hash : throw new ArgumentException($"{name} must be a SHA-256 hash.", name); }
    public static string? Optional(string? value, string name, int max) => string.IsNullOrWhiteSpace(value) ? null : Required(value, name, max);
    public static DateTime Utc(DateTime value, string name) => value == default ? throw new ArgumentException($"{name} is required.", name) : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
