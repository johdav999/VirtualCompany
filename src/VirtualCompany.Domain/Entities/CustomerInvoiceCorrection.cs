namespace VirtualCompany.Domain.Entities;

public static class CustomerInvoiceCorrectionTypes
{
    public const string Cancellation = "cancellation";
    public const string FullCredit = "full_credit";
    public const string PartialCredit = "partial_credit";
    public const string PriceCorrection = "price_correction";
    public const string QuantityCorrection = "quantity_correction";
    public const string TaxCorrection = "tax_correction";
    public const string Refund = "refund";
    public const string SmallBalanceWriteOff = "small_balance_write_off";
    public const string BadDebt = "bad_debt";
    public const string BadDebtRecovery = "bad_debt_recovery";

    public static readonly IReadOnlySet<string> CreditTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        FullCredit, PartialCredit, PriceCorrection, QuantityCorrection, TaxCorrection
    };

    public static bool IsSupported(string value) => value is Cancellation or FullCredit or PartialCredit or
        PriceCorrection or QuantityCorrection or TaxCorrection or Refund or SmallBalanceWriteOff or BadDebt or BadDebtRecovery;

    public static string Normalize(string value)
    {
        var normalized = Required(value, nameof(value), 40).ToLowerInvariant();
        return IsSupported(normalized) ? normalized : throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported receivables correction type.");
    }

    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
}

public static class CustomerInvoiceCorrectionStatuses
{
    public const string AwaitingApproval = "awaiting_approval";
    public const string Approved = "approved";
    public const string DraftCreated = "draft_created";
    public const string Queued = "queued";
    public const string ManualInstruction = "manual_instruction";
    public const string Executed = "executed";
    public const string ReconciliationRequired = "reconciliation_required";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed class CustomerInvoiceCorrection : ICompanyOwnedEntity
{
    private CustomerInvoiceCorrection() { }

    public CustomerInvoiceCorrection(Guid id, Guid companyId, Guid invoiceId, string correctionType,
        decimal amount, string currency, string reason, string sourceVersion, string sourceHash,
        string payloadHash, string idempotencyKey, string evidenceReference, Guid actorUserId,
        DateTime createdUtc, string? beneficiaryReference = null, string? paymentEvidenceReference = null,
        string? providerKey = null, Guid? originalVatReturnId = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = Required(companyId, nameof(companyId));
        InvoiceId = Required(invoiceId, nameof(invoiceId));
        CorrectionType = CustomerInvoiceCorrectionTypes.Normalize(correctionType);
        Amount = Positive(amount, nameof(amount));
        Currency = Text(currency, nameof(currency), 3).ToUpperInvariant();
        Reason = Text(reason, nameof(reason), 1000);
        SourceVersion = Text(sourceVersion, nameof(sourceVersion), 128);
        SourceHash = Hash(sourceHash, nameof(sourceHash));
        PayloadHash = Hash(payloadHash, nameof(payloadHash));
        IdempotencyKey = Text(idempotencyKey, nameof(idempotencyKey), 200);
        EvidenceReference = Text(evidenceReference, nameof(evidenceReference), 500);
        BeneficiaryReference = Optional(beneficiaryReference, nameof(beneficiaryReference), 300);
        PaymentEvidenceReference = Optional(paymentEvidenceReference, nameof(paymentEvidenceReference), 500);
        ProviderKey = Optional(providerKey, nameof(providerKey), 64)?.ToLowerInvariant();
        OriginalVatReturnId = originalVatReturnId == Guid.Empty ? null : originalVatReturnId;
        CreatedByUserId = Required(actorUserId, nameof(actorUserId));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
        Status = CustomerInvoiceCorrectionStatuses.AwaitingApproval;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public string CorrectionType { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public string Reason { get; private set; } = null!;
    public string SourceVersion { get; private set; } = null!;
    public string SourceHash { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string EvidenceReference { get; private set; } = null!;
    public string? BeneficiaryReference { get; private set; }
    public string? PaymentEvidenceReference { get; private set; }
    public string? ProviderKey { get; private set; }
    public string Status { get; private set; } = null!;
    public long Version { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public Guid? TaskId { get; private set; }
    public Guid? CreditDraftId { get; private set; }
    public Guid? CorrectingInvoiceId { get; private set; }
    public Guid? LedgerEntryId { get; private set; }
    public Guid? OriginalVatReturnId { get; private set; }
    public Guid? CorrectionVatReturnId { get; private set; }
    public Guid? ExpenseAccountId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid? ExecutedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? ExecutedUtc { get; private set; }
    public string? FailureReasonCode { get; private set; }
    public string? FailureSummary { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public FinanceInvoice Invoice { get; private set; } = null!;
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public WorkTask? Task { get; private set; }
    public CustomerInvoiceDraft? CreditDraft { get; private set; }
    public FinanceInvoice? CorrectingInvoice { get; private set; }
    public LedgerEntry? LedgerEntry { get; private set; }
    public CustomerInvoiceRefundExecution? RefundExecution { get; private set; }

    public void BindApproval(Guid approvalRequestId, Guid taskId, DateTime utcNow)
    {
        ApprovalRequestId = Required(approvalRequestId, nameof(approvalRequestId));
        TaskId = Required(taskId, nameof(taskId));
        Touch(utcNow);
    }

    public void BindCreditDraft(Guid draftId, DateTime utcNow)
    {
        CreditDraftId = Required(draftId, nameof(draftId));
        Status = CustomerInvoiceCorrectionStatuses.DraftCreated;
        Touch(utcNow);
    }

    public void MarkApproved(DateTime utcNow)
    {
        Status = CustomerInvoiceCorrectionStatuses.Approved;
        Touch(utcNow);
    }

    public void BindVatCorrection(Guid vatReturnId, DateTime utcNow)
    {
        CorrectionVatReturnId = Required(vatReturnId, nameof(vatReturnId));
        Touch(utcNow);
    }

    public void MarkQueued(DateTime utcNow)
    {
        Status = CustomerInvoiceCorrectionStatuses.Queued;
        Touch(utcNow);
    }

    public void MarkManualInstruction(DateTime utcNow)
    {
        Status = CustomerInvoiceCorrectionStatuses.ManualInstruction;
        Touch(utcNow);
    }

    public void MarkExecuted(Guid actorUserId, DateTime utcNow, Guid? correctingInvoiceId = null,
        Guid? ledgerEntryId = null, Guid? expenseAccountId = null)
    {
        CorrectingInvoiceId = correctingInvoiceId == Guid.Empty ? null : correctingInvoiceId;
        LedgerEntryId = ledgerEntryId == Guid.Empty ? null : ledgerEntryId;
        ExpenseAccountId = expenseAccountId == Guid.Empty ? null : expenseAccountId;
        ExecutedByUserId = Required(actorUserId, nameof(actorUserId));
        ExecutedUtc = EntityTimestampNormalizer.NormalizeUtc(utcNow, nameof(utcNow));
        UpdatedUtc = ExecutedUtc.Value;
        Status = CustomerInvoiceCorrectionStatuses.Executed;
        FailureReasonCode = null;
        FailureSummary = null;
        Version++;
    }

    public void MarkReconciliationRequired(string reasonCode, string summary, DateTime utcNow)
    {
        FailureReasonCode = Text(reasonCode, nameof(reasonCode), 100);
        FailureSummary = Text(summary, nameof(summary), 1000);
        Status = CustomerInvoiceCorrectionStatuses.ReconciliationRequired;
        Touch(utcNow);
    }

    public void MarkFailed(string reasonCode, string summary, DateTime utcNow)
    {
        FailureReasonCode = Text(reasonCode, nameof(reasonCode), 100);
        FailureSummary = Text(summary, nameof(summary), 1000);
        Status = CustomerInvoiceCorrectionStatuses.Failed;
        Touch(utcNow);
    }

    private void Touch(DateTime utcNow)
    {
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(utcNow, nameof(utcNow));
        Version++;
    }

    private static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new ArgumentException($"{name} is required.", name) : value;
    private static decimal Positive(decimal value, string name) => value > 0m ? decimal.Round(value, 2, MidpointRounding.AwayFromZero) : throw new ArgumentOutOfRangeException(name);
    private static string Text(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static string Hash(string value, string name) { var result = Text(value, name, 64).ToLowerInvariant(); return result.Length == 64 ? result : throw new ArgumentOutOfRangeException(name); }
    private static string? Optional(string? value, string name, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
}

public static class CustomerInvoiceRefundExecutionStatuses
{
    public const string Queued = "queued";
    public const string Executing = "executing";
    public const string RetryScheduled = "retry_scheduled";
    public const string ManualInstruction = "manual_instruction";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string ReconciliationRequired = "reconciliation_required";
}

public sealed class CustomerInvoiceRefundExecution : ICompanyOwnedEntity
{
    private CustomerInvoiceRefundExecution() { }

    public CustomerInvoiceRefundExecution(Guid id, Guid companyId, Guid correctionId, string? providerKey,
        string idempotencyKey, string beneficiaryReference, string paymentEvidenceReference,
        bool manualInstruction, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CorrectionId = correctionId;
        ProviderKey = string.IsNullOrWhiteSpace(providerKey) ? null : providerKey.Trim().ToLowerInvariant();
        IdempotencyKey = idempotencyKey.Trim();
        BeneficiaryReference = beneficiaryReference.Trim();
        PaymentEvidenceReference = paymentEvidenceReference.Trim();
        Status = manualInstruction ? CustomerInvoiceRefundExecutionStatuses.ManualInstruction : CustomerInvoiceRefundExecutionStatuses.Queued;
        AvailableUtc = CreatedUtc = UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CorrectionId { get; private set; }
    public string? ProviderKey { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string BeneficiaryReference { get; private set; } = null!;
    public string PaymentEvidenceReference { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public int AttemptCount { get; private set; }
    public DateTime AvailableUtc { get; private set; }
    public DateTime? ClaimedUtc { get; private set; }
    public string? ClaimToken { get; private set; }
    public string? ProviderReference { get; private set; }
    public string? FailureCategory { get; private set; }
    public string? SafeFailureSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public CustomerInvoiceCorrection Correction { get; private set; } = null!;

    public bool TryClaim(string claimToken, DateTime utcNow, TimeSpan lease)
    {
        if (Status is not (CustomerInvoiceRefundExecutionStatuses.Queued or CustomerInvoiceRefundExecutionStatuses.RetryScheduled) || AvailableUtc > utcNow)
            return false;
        if (ClaimedUtc.HasValue && ClaimedUtc > utcNow.Subtract(lease)) return false;
        ClaimToken = claimToken;
        ClaimedUtc = utcNow;
        Status = CustomerInvoiceRefundExecutionStatuses.Executing;
        UpdatedUtc = utcNow;
        AttemptCount++;
        return true;
    }

    public void MarkSucceeded(string providerReference, DateTime utcNow)
    {
        ProviderReference = providerReference.Trim();
        Status = CustomerInvoiceRefundExecutionStatuses.Succeeded;
        CompletedUtc = UpdatedUtc = utcNow;
        ClaimedUtc = null; ClaimToken = null; FailureCategory = null; SafeFailureSummary = null;
    }

    public void ScheduleRetry(string category, string summary, DateTime availableUtc)
    {
        FailureCategory = category; SafeFailureSummary = summary;
        Status = CustomerInvoiceRefundExecutionStatuses.RetryScheduled;
        AvailableUtc = UpdatedUtc = availableUtc;
        ClaimedUtc = null; ClaimToken = null;
    }

    public void MarkFailed(string category, string summary, DateTime utcNow)
    {
        FailureCategory = category; SafeFailureSummary = summary;
        Status = CustomerInvoiceRefundExecutionStatuses.Failed;
        CompletedUtc = UpdatedUtc = utcNow;
        ClaimedUtc = null; ClaimToken = null;
    }

    public void MarkReconciliationRequired(string category, string summary, string? providerReference, DateTime utcNow)
    {
        FailureCategory = category; SafeFailureSummary = summary; ProviderReference = providerReference;
        Status = CustomerInvoiceRefundExecutionStatuses.ReconciliationRequired;
        CompletedUtc = UpdatedUtc = utcNow;
        ClaimedUtc = null; ClaimToken = null;
    }
}

public sealed class CustomerInvoiceCorrectionAllocationAdjustment : ICompanyOwnedEntity
{
    private CustomerInvoiceCorrectionAllocationAdjustment() { }
    public CustomerInvoiceCorrectionAllocationAdjustment(Guid id, Guid companyId, Guid correctionId,
        Guid paymentAllocationId, decimal releasedAmount, string currency, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId; CorrectionId = correctionId; PaymentAllocationId = paymentAllocationId;
        ReleasedAmount = decimal.Round(releasedAmount, 2, MidpointRounding.AwayFromZero);
        if (ReleasedAmount <= 0m) throw new ArgumentOutOfRangeException(nameof(releasedAmount));
        Currency = currency.Trim().ToUpperInvariant();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CorrectionId { get; private set; }
    public Guid PaymentAllocationId { get; private set; }
    public decimal ReleasedAmount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public CustomerInvoiceCorrection Correction { get; private set; } = null!;
    public PaymentAllocation PaymentAllocation { get; private set; } = null!;
}
