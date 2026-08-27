namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CustomerInvoiceCorrectionPolicyResponse?> EvaluateCustomerInvoiceCorrectionAsync(Guid companyId,
        Guid invoiceId, string correctionType, decimal amount, string currency, string? providerKey = null,
        CancellationToken cancellationToken = default)
    {
        var path = $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/corrections/policy" +
            $"?correctionType={Uri.EscapeDataString(correctionType)}&amount={Uri.EscapeDataString(amount.ToString(System.Globalization.CultureInfo.InvariantCulture))}" +
            $"&currency={Uri.EscapeDataString(currency)}" +
            (string.IsNullOrWhiteSpace(providerKey) ? string.Empty : $"&providerKey={Uri.EscapeDataString(providerKey)}");
        return GetAsync<CustomerInvoiceCorrectionPolicyResponse>(companyId, path, false, cancellationToken);
    }

    public Task<CustomerInvoiceCorrectionResponse> ProposeCustomerInvoiceCorrectionAsync(Guid companyId,
        Guid invoiceId, ProposeCustomerInvoiceCorrectionApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ProposeCustomerInvoiceCorrectionApiRequest, CustomerInvoiceCorrectionResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoices/{invoiceId:D}/corrections",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceCorrectionListResponse?> GetCustomerInvoiceCorrectionsAsync(Guid companyId,
        Guid? invoiceId = null, string? status = null, int skip = 0, int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"skip={Math.Max(0, skip)}", $"take={Math.Clamp(take, 1, 250)}" };
        if (invoiceId.HasValue) query.Add($"invoiceId={invoiceId:D}");
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        return GetAsync<CustomerInvoiceCorrectionListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-corrections?{string.Join("&", query)}",
            false, cancellationToken);
    }

    public Task<CustomerInvoiceCorrectionResponse?> GetCustomerInvoiceCorrectionAsync(Guid companyId,
        Guid correctionId, CancellationToken cancellationToken = default) =>
        GetAsync<CustomerInvoiceCorrectionResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-corrections/{correctionId:D}",
            true, cancellationToken);

    public Task<CustomerInvoiceCorrectionResponse> ExecuteCustomerInvoiceCorrectionAsync(Guid companyId,
        Guid correctionId, ExecuteCustomerInvoiceCorrectionApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ExecuteCustomerInvoiceCorrectionApiRequest, CustomerInvoiceCorrectionResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-corrections/{correctionId:D}/execute",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceCorrectionResponse> ReconcileCustomerInvoiceRefundAsync(Guid companyId,
        Guid correctionId, ReconcileCustomerInvoiceRefundApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ReconcileCustomerInvoiceRefundApiRequest, CustomerInvoiceCorrectionResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-corrections/{correctionId:D}/refund-reconciliation",
            request, cancellationToken);
    }
}

public sealed record ProposeCustomerInvoiceCorrectionApiRequest(string CorrectionType, decimal Amount,
    string Currency, string Reason, string EvidenceReference, string IdempotencyKey,
    string? BeneficiaryReference = null, string? PaymentEvidenceReference = null,
    string? ProviderKey = null, SaveCustomerInvoiceDraftApiRequest? CreditDraft = null);
public sealed record ExecuteCustomerInvoiceCorrectionApiRequest(long ExpectedVersion,
    string ExpectedSourceHash, string IdempotencyKey, Guid? SeriesId = null,
    Guid? FiscalPeriodId = null, DateOnly? AccountingDate = null, string? VoucherSeriesCode = null,
    Guid? ExpenseAccountId = null);
public sealed record ReconcileCustomerInvoiceRefundApiRequest(long ExpectedVersion,
    bool ProviderConfirmedSucceeded, bool ProviderConfirmedAbsent, string EvidenceReference,
    string? ProviderReference = null);
public sealed record CustomerInvoiceCorrectionEvidenceResponse(string Key, string Value);
public sealed record CustomerInvoiceCorrectionPolicyResponse(bool IsAllowed, string ReasonCode,
    string Explanation, bool RequiresApproval, decimal InvoiceAmount, decimal AllocatedPaidAmount,
    decimal PriorCreditAmount, decimal PriorRefundAmount, decimal PriorWriteOffAmount,
    decimal RemainingEconomicBalance, decimal MaximumAllowedAmount, string Currency,
    string SourceVersion, string SourceHash, bool RequiresCurrentPeriodPosting,
    bool RequiresVatCorrectionReturn, Guid? OriginalVatReturnId,
    IReadOnlyList<CustomerInvoiceCorrectionEvidenceResponse> Evidence);
public sealed record CustomerInvoiceRefundExecutionResponse(Guid Id, string? ProviderKey, string Status,
    int AttemptCount, DateTime AvailableUtc, string? ProviderReference, string? FailureCategory,
    string? SafeFailureSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);
public sealed record CustomerInvoiceCorrectionResponse(Guid Id, Guid CompanyId, Guid InvoiceId,
    string InvoiceNumber, string CorrectionType, decimal Amount, string Currency, string Reason,
    string Status, long Version, string SourceVersion, string SourceHash, string EvidenceReference,
    Guid? ApprovalRequestId, string? ApprovalStatus, Guid? TaskId, Guid? CreditDraftId,
    Guid? CorrectingInvoiceId, Guid? LedgerEntryId, Guid? OriginalVatReturnId,
    Guid? CorrectionVatReturnId, Guid? ExpenseAccountId, string? ProviderKey,
    string? BeneficiaryReference, string? PaymentEvidenceReference, Guid CreatedByUserId,
    Guid? ExecutedByUserId, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ExecutedUtc,
    string? FailureReasonCode, string? FailureSummary,
    CustomerInvoiceRefundExecutionResponse? RefundExecution, IReadOnlyList<string> AllowedActions,
    bool IsIdempotentReplay = false);
public sealed record CustomerInvoiceCorrectionListResponse(IReadOnlyList<CustomerInvoiceCorrectionResponse> Items,
    int TotalCount, int Skip, int Take);
