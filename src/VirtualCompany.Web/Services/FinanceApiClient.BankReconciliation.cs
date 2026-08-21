namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<BankReconciliationWorkspaceResponse?> ListBankReconciliationAsync(
        Guid companyId,
        string? state = null,
        string? search = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var query = $"?limit={Math.Clamp(limit, 1, 500)}";
        if (!string.IsNullOrWhiteSpace(state)) query += $"&state={Uri.EscapeDataString(state)}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        if (fromUtc.HasValue) query += $"&fromUtc={Uri.EscapeDataString(fromUtc.Value.ToString("O"))}";
        if (toUtc.HasValue) query += $"&toUtc={Uri.EscapeDataString(toUtc.Value.ToString("O"))}";
        return GetAsync<BankReconciliationWorkspaceResponse>(companyId,
            $"internal/companies/{companyId}/finance/bank-transactions/reconciliation{query}", false, cancellationToken);
    }

    public Task<BankReconciliationDetailResponse?> GetBankReconciliationDetailAsync(Guid companyId, Guid transactionId,
        CancellationToken cancellationToken = default) =>
        GetAsync<BankReconciliationDetailResponse>(companyId,
            $"internal/companies/{companyId}/finance/bank-transactions/{transactionId}/reconciliation", true, cancellationToken);

    public Task<BankTransactionDetailResponse> ReconcileBankTransactionAsync(Guid companyId, Guid transactionId,
        ReconcileBankTransactionApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ReconcileBankTransactionApiRequest, BankTransactionDetailResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bank-transactions/{transactionId}/reconcile", request, cancellationToken);
    }

    public Task<BankReconciliationDetailResponse> ReclassifyBankSuspenseAsync(Guid companyId, Guid transactionId,
        ReclassifyBankSuspenseApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ReclassifyBankSuspenseApiRequest, BankReconciliationDetailResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bank-transactions/{transactionId}/reclassify-suspense", request, cancellationToken);
    }
}

public sealed class BankReconciliationWorkspaceResponse
{
    public List<BankReconciliationItemResponse> Items { get; set; } = [];
    public Dictionary<string, int> StateCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BankReconciliationItemResponse
{
    public Guid BankTransactionId { get; set; }
    public DateTime BookingDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Counterparty { get; set; } = string.Empty;
    public string ReferenceText { get; set; } = string.Empty;
    public string BankAccountDisplayName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public decimal AllocatedAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public int LinkedPaymentCount { get; set; }
    public long SourceVersion { get; set; }
    public string? ConflictCode { get; set; }
    public string? ConflictExplanation { get; set; }
    public Guid? LedgerEntryId { get; set; }
}

public sealed class BankReconciliationDetailResponse
{
    public BankTransactionDetailResponse Transaction { get; set; } = new();
    public string State { get; set; } = string.Empty;
    public decimal RemainingAmount { get; set; }
    public long SourceVersion { get; set; }
    public string? HandlingMode { get; set; }
    public string? ReviewReason { get; set; }
    public List<BankReconciliationCandidatePaymentResponse> CandidatePayments { get; set; } = [];
    public List<BankReconciliationJournalLinkResponse> Journals { get; set; } = [];
    public BankReconciliationFollowUpResponse? FollowUp { get; set; }
    public bool CanPostToSuspense { get; set; }
    public bool CanReclassify { get; set; }
    public string? BlockingReason { get; set; }
}

public sealed class BankTransactionDetailResponse
{
    public Guid Id { get; set; }
    public DateTime BookingDate { get; set; }
    public DateTime ValueDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string ReferenceText { get; set; } = string.Empty;
    public string Counterparty { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal ReconciledAmount { get; set; }
    public Guid? CashLedgerEntryId { get; set; }
    public List<BankTransactionPaymentLinkResponse> LinkedPayments { get; set; } = [];
}

public sealed class BankTransactionPaymentLinkResponse
{
    public Guid PaymentId { get; set; }
    public decimal AllocatedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string CounterpartyReference { get; set; } = string.Empty;
}

public sealed class BankReconciliationCandidatePaymentResponse
{
    public Guid PaymentId { get; set; }
    public string PaymentType { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal AlreadyLinkedAmount { get; set; }
    public decimal AvailableAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public string CounterpartyReference { get; set; } = string.Empty;
    public Guid? InvoiceId { get; set; }
    public string? InvoiceNumber { get; set; }
    public Guid? BillId { get; set; }
    public string? BillNumber { get; set; }
}

public sealed class BankReconciliationJournalLinkResponse
{
    public Guid LedgerEntryId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public string PostingType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly? PostingDate { get; set; }
    public bool IsOriginalSuspense { get; set; }
    public bool IsCorrection { get; set; }
}

public sealed class BankReconciliationFollowUpResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public Guid LedgerEntryId { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ResolvedUtc { get; set; }
}

public sealed class ReconcileBankTransactionApiRequest
{
    public List<ReconcileBankTransactionPaymentApiRequest> Payments { get; set; } = [];
    public long ExpectedSourceVersion { get; set; } = 1;
    public string HandlingMode { get; set; } = "payment";
    public string? ReviewReason { get; set; }
    public Guid? CategorizationFinanceAccountId { get; set; }
    public List<BankReconciliationAdjustmentApiRequest> Adjustments { get; set; } = [];
    public string? IdempotencyKey { get; set; }
}

public sealed class ReconcileBankTransactionPaymentApiRequest
{
    public Guid PaymentId { get; set; }
    public decimal AllocatedAmount { get; set; }
}

public sealed class BankReconciliationAdjustmentApiRequest
{
    public string Kind { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

public sealed class ReclassifyBankSuspenseApiRequest
{
    public Guid TargetFinanceAccountId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public DateOnly PostingDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public long ExpectedSourceVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}
