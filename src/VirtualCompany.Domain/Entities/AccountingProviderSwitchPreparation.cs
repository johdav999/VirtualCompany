using System.Text.Json;

namespace VirtualCompany.Domain.Entities;

public static class AccountingProviderSwitchPreparationStatuses
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public static string Normalize(string value) => PreparationText.Token(value, nameof(value), 24) switch
    {
        Queued => Queued,
        Running => Running,
        Completed => Completed,
        Failed => Failed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Preparation status is not supported.")
    };
}

public static class AccountingProviderSwitchNativeCandidateKinds
{
    public const string OpeningJournal = "opening_journal";
    public const string HistoricalJournal = "historical_journal";
    public const string Customer = "customer";
    public const string Supplier = "supplier";
    public const string CustomerInvoice = "customer_invoice";
    public const string SupplierBill = "supplier_bill";
    public const string Credit = "credit";
    public const string Payment = "payment";
    public const string Allocation = "allocation";
    public const string BankState = "bank_state";
    public const string Document = "document";
    public const string ExternalReference = "external_reference";

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        OpeningJournal, HistoricalJournal, Customer, Supplier, CustomerInvoice, SupplierBill, Credit,
        Payment, Allocation, BankState, Document, ExternalReference
    };

    public static string Normalize(string value)
    {
        var normalized = PreparationText.Token(value, nameof(value), 48);
        return Supported.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), "Native candidate kind is not supported.");
    }
}

public static class AccountingProviderSwitchNativeCandidateStatuses
{
    public const string Valid = "valid";
    public const string Rejected = "rejected";

    public static string Normalize(string value) => PreparationText.Token(value, nameof(value), 24) switch
    {
        Valid => Valid,
        Rejected => Rejected,
        _ => throw new ArgumentOutOfRangeException(nameof(value), "Native candidate status is not supported.")
    };
}

public sealed class AccountingProviderSwitchPreparation : ICompanyOwnedEntity
{
    private AccountingProviderSwitchPreparation() { }

    public AccountingProviderSwitchPreparation(Guid id, Guid companyId, Guid switchId, Guid planId,
        string planHash, string strategy, Guid requestedByUserId, string idempotencyKey,
        string correlationId, int totalWorkItems, DateTime requestedUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = PreparationText.Required(companyId, nameof(companyId));
        SwitchId = PreparationText.Required(switchId, nameof(switchId));
        PlanId = PreparationText.Required(planId, nameof(planId));
        PlanHash = PreparationText.Hash(planHash, nameof(planHash));
        Strategy = AccountingProviderSwitchStrategies.Normalize(strategy);
        RequestedByUserId = PreparationText.Required(requestedByUserId, nameof(requestedByUserId));
        IdempotencyKey = PreparationText.Required(idempotencyKey, nameof(idempotencyKey), 128);
        CorrelationId = PreparationText.Required(correlationId, nameof(correlationId), 128);
        if (totalWorkItems < 0) throw new ArgumentOutOfRangeException(nameof(totalWorkItems));
        TotalWorkItems = totalWorkItems;
        Status = AccountingProviderSwitchPreparationStatuses.Queued;
        NextAttemptUtc = PreparationText.Utc(requestedUtc, nameof(requestedUtc));
        RequestedUtc = NextAttemptUtc.Value;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid PlanId { get; private set; }
    public string PlanHash { get; private set; } = null!;
    public string Strategy { get; private set; } = null!;
    public Guid RequestedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int CompletedWorkItems { get; private set; }
    public int TotalWorkItems { get; private set; }
    public int CandidateCount { get; private set; }
    public int ValidCandidateCount { get; private set; }
    public int RejectedCandidateCount { get; private set; }
    public int ExistingReferenceCount { get; private set; }
    public int ArchiveDependencyCount { get; private set; }
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
    public AccountingProviderSwitchCutoverPlan Plan { get; private set; } = null!;

    public void Start(string leaseOwner, DateTime leaseExpiresUtc, DateTime startedUtc)
    {
        if (Status is not (AccountingProviderSwitchPreparationStatuses.Queued or AccountingProviderSwitchPreparationStatuses.Running))
            throw new InvalidOperationException("Only queued or lease-expired preparation can start.");
        var now = PreparationText.Utc(startedUtc, nameof(startedUtc));
        var expires = PreparationText.Utc(leaseExpiresUtc, nameof(leaseExpiresUtc));
        if (expires <= now) throw new ArgumentOutOfRangeException(nameof(leaseExpiresUtc));
        Status = AccountingProviderSwitchPreparationStatuses.Running;
        LeaseOwner = PreparationText.Required(leaseOwner, nameof(leaseOwner), 128);
        LeaseExpiresUtc = expires;
        StartedUtc ??= now;
        NextAttemptUtc = null;
        AttemptCount++;
        FailureCode = null;
        FailureSummary = null;
        Version++;
    }

    public void Complete(int completedWorkItems, int candidateCount, int validCandidateCount,
        int rejectedCandidateCount, int existingReferenceCount, int archiveDependencyCount, DateTime completedUtc)
    {
        if (Status != AccountingProviderSwitchPreparationStatuses.Running)
            throw new InvalidOperationException("Preparation must be running before it can complete.");
        if (completedWorkItems < 0 || candidateCount < 0 || validCandidateCount < 0 || rejectedCandidateCount < 0 ||
            existingReferenceCount < 0 || archiveDependencyCount < 0 || validCandidateCount + rejectedCandidateCount != candidateCount)
            throw new ArgumentOutOfRangeException(nameof(candidateCount));
        CompletedWorkItems = Math.Min(completedWorkItems, TotalWorkItems);
        CandidateCount = candidateCount;
        ValidCandidateCount = validCandidateCount;
        RejectedCandidateCount = rejectedCandidateCount;
        ExistingReferenceCount = existingReferenceCount;
        ArchiveDependencyCount = archiveDependencyCount;
        Status = AccountingProviderSwitchPreparationStatuses.Completed;
        CompletedUtc = PreparationText.Utc(completedUtc, nameof(completedUtc));
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        NextAttemptUtc = null;
        Version++;
    }

    public void Retry(string code, string summary, DateTime nextAttemptUtc)
    {
        Status = AccountingProviderSwitchPreparationStatuses.Queued;
        FailureCode = PreparationText.Token(code, nameof(code), 100);
        FailureSummary = PreparationText.Required(summary, nameof(summary), 1000);
        NextAttemptUtc = PreparationText.Utc(nextAttemptUtc, nameof(nextAttemptUtc));
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        Version++;
    }

    public void Fail(string code, string summary, DateTime failedUtc)
    {
        Status = AccountingProviderSwitchPreparationStatuses.Failed;
        FailureCode = PreparationText.Token(code, nameof(code), 100);
        FailureSummary = PreparationText.Required(summary, nameof(summary), 1000);
        CompletedUtc = PreparationText.Utc(failedUtc, nameof(failedUtc));
        NextAttemptUtc = null;
        LeaseOwner = null;
        LeaseExpiresUtc = null;
        Version++;
    }

    public void QueueReplay(string correlationId, DateTime queuedUtc)
    {
        if (Status is not AccountingProviderSwitchPreparationStatuses.Failed)
            throw new InvalidOperationException("Only a failed preparation can be replayed.");
        CorrelationId = PreparationText.Required(correlationId, nameof(correlationId), 128);
        Status = AccountingProviderSwitchPreparationStatuses.Queued;
        NextAttemptUtc = PreparationText.Utc(queuedUtc, nameof(queuedUtc));
        CompletedUtc = null;
        FailureCode = null;
        FailureSummary = null;
        Version++;
    }
}

public sealed class AccountingProviderSwitchReadinessCheck : ICompanyOwnedEntity
{
    private AccountingProviderSwitchReadinessCheck() { }

    public AccountingProviderSwitchReadinessCheck(Guid companyId, Guid switchId, Guid preparationId,
        string checkKey, bool isReady, bool isBlocking, string? reasonCode, string explanation,
        string evidenceJson, DateTime calculatedUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = PreparationText.Required(companyId, nameof(companyId));
        SwitchId = PreparationText.Required(switchId, nameof(switchId));
        PreparationId = PreparationText.Required(preparationId, nameof(preparationId));
        CheckKey = PreparationText.Token(checkKey, nameof(checkKey), 80);
        IsReady = isReady;
        IsBlocking = isBlocking;
        ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : PreparationText.Token(reasonCode, nameof(reasonCode), 100);
        Explanation = PreparationText.Required(explanation, nameof(explanation), 1000);
        EvidenceJson = PreparationText.Json(evidenceJson, nameof(evidenceJson), 16000);
        CalculatedUtc = PreparationText.Utc(calculatedUtc, nameof(calculatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid PreparationId { get; private set; }
    public string CheckKey { get; private set; } = null!;
    public bool IsReady { get; private set; }
    public bool IsBlocking { get; private set; }
    public string? ReasonCode { get; private set; }
    public string Explanation { get; private set; } = null!;
    public string EvidenceJson { get; private set; } = null!;
    public DateTime CalculatedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchNativeCandidate : ICompanyOwnedEntity
{
    private AccountingProviderSwitchNativeCandidate() { }

    public AccountingProviderSwitchNativeCandidate(Guid id, Guid companyId, Guid switchId, Guid preparedByRunId,
        Guid stagedRecordId, string candidateKind, string sourceDataset, string sourceIdentity,
        string sourceVersion, string sourceHash, string idempotencyKey, Guid? fiscalPeriodId,
        DateOnly? documentDate, DateOnly? postingDate, decimal financialAmount, string? currency,
        string status, string payloadJson, string evidenceHash, Guid? externalReferenceId, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = PreparationText.Required(companyId, nameof(companyId));
        SwitchId = PreparationText.Required(switchId, nameof(switchId));
        PreparedByRunId = PreparationText.Required(preparedByRunId, nameof(preparedByRunId));
        StagedRecordId = PreparationText.Required(stagedRecordId, nameof(stagedRecordId));
        CandidateKind = AccountingProviderSwitchNativeCandidateKinds.Normalize(candidateKind);
        SourceDataset = PreparationText.Token(sourceDataset, nameof(sourceDataset), 64);
        SourceIdentity = PreparationText.Required(sourceIdentity, nameof(sourceIdentity), 256);
        SourceVersion = PreparationText.Required(sourceVersion, nameof(sourceVersion), 128);
        SourceHash = PreparationText.Hash(sourceHash, nameof(sourceHash));
        IdempotencyKey = PreparationText.Required(idempotencyKey, nameof(idempotencyKey), 200);
        if (fiscalPeriodId == Guid.Empty) throw new ArgumentException("FiscalPeriodId cannot be empty.", nameof(fiscalPeriodId));
        FiscalPeriodId = fiscalPeriodId;
        DocumentDate = documentDate;
        PostingDate = postingDate;
        FinancialAmount = financialAmount;
        Currency = string.IsNullOrWhiteSpace(currency) ? null : PreparationText.Required(currency, nameof(currency), 3).ToUpperInvariant();
        Status = AccountingProviderSwitchNativeCandidateStatuses.Normalize(status);
        PayloadJson = PreparationText.Json(payloadJson, nameof(payloadJson), 64000);
        EvidenceHash = PreparationText.Hash(evidenceHash, nameof(evidenceHash));
        if (externalReferenceId == Guid.Empty) throw new ArgumentException("ExternalReferenceId cannot be empty.", nameof(externalReferenceId));
        ExternalReferenceId = externalReferenceId;
        CreatedUtc = PreparationText.Utc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid PreparedByRunId { get; private set; }
    public Guid StagedRecordId { get; private set; }
    public string CandidateKind { get; private set; } = null!;
    public string SourceDataset { get; private set; } = null!;
    public string SourceIdentity { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!;
    public string SourceHash { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public Guid? FiscalPeriodId { get; private set; }
    public DateOnly? DocumentDate { get; private set; }
    public DateOnly? PostingDate { get; private set; }
    public decimal FinancialAmount { get; private set; }
    public string? Currency { get; private set; }
    public string Status { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!;
    public Guid? ExternalReferenceId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchCandidateValidation : ICompanyOwnedEntity
{
    private AccountingProviderSwitchCandidateValidation() { }

    public AccountingProviderSwitchCandidateValidation(Guid companyId, Guid switchId, Guid candidateId,
        string reasonCode, bool isBlocking, string explanation, string evidenceJson, DateTime validatedUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = PreparationText.Required(companyId, nameof(companyId));
        SwitchId = PreparationText.Required(switchId, nameof(switchId));
        CandidateId = PreparationText.Required(candidateId, nameof(candidateId));
        ReasonCode = PreparationText.Token(reasonCode, nameof(reasonCode), 100);
        IsBlocking = isBlocking;
        Explanation = PreparationText.Required(explanation, nameof(explanation), 1000);
        EvidenceJson = PreparationText.Json(evidenceJson, nameof(evidenceJson), 16000);
        ValidatedUtc = PreparationText.Utc(validatedUtc, nameof(validatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid CandidateId { get; private set; }
    public string ReasonCode { get; private set; } = null!;
    public bool IsBlocking { get; private set; }
    public string Explanation { get; private set; } = null!;
    public string EvidenceJson { get; private set; } = null!;
    public DateTime ValidatedUtc { get; private set; }
}

public sealed class AccountingProviderSwitchArchiveDependency : ICompanyOwnedEntity
{
    private AccountingProviderSwitchArchiveDependency() { }

    public AccountingProviderSwitchArchiveDependency(Guid companyId, Guid switchId, Guid preparedByRunId,
        Guid? stagedRecordId, string dataset, string sourceIdentity, string reasonCode, string explanation,
        string evidenceHash, Guid approvedPlanId, string approvedPlanHash, DateTime createdUtc)
    {
        Id = Guid.NewGuid();
        CompanyId = PreparationText.Required(companyId, nameof(companyId));
        SwitchId = PreparationText.Required(switchId, nameof(switchId));
        PreparedByRunId = PreparationText.Required(preparedByRunId, nameof(preparedByRunId));
        if (stagedRecordId == Guid.Empty) throw new ArgumentException("StagedRecordId cannot be empty.", nameof(stagedRecordId));
        StagedRecordId = stagedRecordId;
        Dataset = PreparationText.Token(dataset, nameof(dataset), 64);
        SourceIdentity = PreparationText.Required(sourceIdentity, nameof(sourceIdentity), 256);
        ReasonCode = PreparationText.Token(reasonCode, nameof(reasonCode), 100);
        Explanation = PreparationText.Required(explanation, nameof(explanation), 1000);
        EvidenceHash = PreparationText.Hash(evidenceHash, nameof(evidenceHash));
        ApprovedPlanId = PreparationText.Required(approvedPlanId, nameof(approvedPlanId));
        ApprovedPlanHash = PreparationText.Hash(approvedPlanHash, nameof(approvedPlanHash));
        CreatedUtc = PreparationText.Utc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SwitchId { get; private set; }
    public Guid PreparedByRunId { get; private set; }
    public Guid? StagedRecordId { get; private set; }
    public string Dataset { get; private set; } = null!;
    public string SourceIdentity { get; private set; } = null!;
    public string ReasonCode { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public string EvidenceHash { get; private set; } = null!;
    public Guid ApprovedPlanId { get; private set; }
    public string ApprovedPlanHash { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
}

internal static class PreparationText
{
    public static Guid Required(Guid value, string name) => value == Guid.Empty
        ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Required(string? value, string name, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        return normalized.Length <= max ? normalized : throw new ArgumentOutOfRangeException(name);
    }
    public static string Token(string value, string name, int max) => Required(value, name, max).Replace('-', '_').ToLowerInvariant();
    public static string Hash(string value, string name)
    {
        var normalized = Required(value, name, 64).ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? normalized : throw new ArgumentException($"{name} must be a SHA-256 hash.", name);
    }
    public static string Json(string value, string name, int max)
    {
        var normalized = Required(value, name, max);
        try { using var _ = JsonDocument.Parse(normalized); }
        catch (JsonException exception) { throw new ArgumentException($"{name} must be valid JSON.", name, exception); }
        return normalized;
    }
    public static DateTime Utc(DateTime value, string name) => value == default
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
