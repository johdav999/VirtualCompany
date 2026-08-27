namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<CustomerInvoiceDraftListResponse?> GetCustomerInvoiceDraftsAsync(Guid companyId,
        string? status = null, Guid? customerId = null, int skip = 0, int take = 100,
        CancellationToken cancellationToken = default)
    {
        var query = new List<string>
        {
            $"skip={Math.Max(0, skip)}",
            $"take={Math.Clamp(take, 1, 250)}"
        };
        if (!string.IsNullOrWhiteSpace(status)) query.Add($"status={Uri.EscapeDataString(status)}");
        if (customerId.HasValue) query.Add($"customerId={customerId.Value:D}");
        return GetAsync<CustomerInvoiceDraftListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts?{string.Join("&", query)}",
            allowNotFound: false, cancellationToken);
    }

    public Task<CustomerInvoiceDraftResponse?> GetCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        CancellationToken cancellationToken = default) => GetAsync<CustomerInvoiceDraftResponse>(companyId,
        $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}",
        allowNotFound: true, cancellationToken);

    public Task<CustomerInvoiceDraftResponse> CreateCustomerInvoiceDraftAsync(Guid companyId,
        SaveCustomerInvoiceDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveCustomerInvoiceDraftApiRequest, CustomerInvoiceDraftResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftResponse> UpdateCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        SaveCustomerInvoiceDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveCustomerInvoiceDraftApiRequest, CustomerInvoiceDraftResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftResponse> CopyCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        CopyCustomerInvoiceDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CopyCustomerInvoiceDraftApiRequest, CustomerInvoiceDraftResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/copy",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftResponse> DiscardCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        CustomerInvoiceDraftVersionedApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CustomerInvoiceDraftVersionedApiRequest, CustomerInvoiceDraftResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/discard",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftPreviewResponse> PreviewCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        long expectedVersion, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<object, CustomerInvoiceDraftPreviewResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/preview",
            new { expectedVersion }, cancellationToken);

    public Task<CustomerInvoiceDraftReadinessResponse?> GetCustomerInvoiceDraftReadinessAsync(Guid companyId,
        Guid draftId, long expectedVersion, CancellationToken cancellationToken = default) =>
        GetAsync<CustomerInvoiceDraftReadinessResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/readiness?expectedVersion={expectedVersion}",
            allowNotFound: false, cancellationToken);

    public Task<CustomerInvoiceDraftSubmissionResponse> SubmitCustomerInvoiceDraftAsync(Guid companyId,
        Guid draftId, CustomerInvoiceDraftVersionedApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CustomerInvoiceDraftVersionedApiRequest, CustomerInvoiceDraftSubmissionResponse>(
            companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/submit",
            request, cancellationToken);
    }

    public Task<CustomerInvoiceDraftIssueResponse> IssueCustomerInvoiceDraftAsync(Guid companyId, Guid draftId,
        IssueCustomerInvoiceDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<IssueCustomerInvoiceDraftApiRequest, CustomerInvoiceDraftIssueResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/customer-invoice-drafts/{draftId:D}/issue",
            request, cancellationToken);
    }
}

public sealed record SaveCustomerInvoiceDraftApiRequest(long ExpectedVersion, string IdempotencyKey,
    Guid CustomerId, string DocumentType, DateOnly IssueDate, DateOnly SupplyDate, DateOnly DueDate,
    string Currency, string PaymentTermKind, int PaymentTermDays, string? BuyerReference,
    string? SellerReference, string? Notes, string DeliveryIntent, string SourceKind,
    string? SourceReference, IReadOnlyList<CustomerInvoiceDraftLineApiRequest> Lines,
    IReadOnlyList<Guid> EvidenceDocumentIds, Guid? OriginalInvoiceId = null);
public sealed record CustomerInvoiceDraftLineApiRequest(int Sequence, string Description, decimal Quantity,
    string Unit, decimal UnitPrice, decimal DiscountPercent, string TaxRuleKey, string TaxClassification,
    IReadOnlyList<CustomerInvoiceDraftTaxEvidenceApiRequest> TaxEvidence,
    IReadOnlyDictionary<string, string>? DimensionFacts = null, string? RevenueAccountRoleKey = null,
    string? SourceReference = null, string? OrderReference = null);
public sealed record CustomerInvoiceDraftTaxEvidenceApiRequest(string Classification, string? SourceReference = null);
public sealed record CustomerInvoiceDraftVersionedApiRequest(long ExpectedVersion, string IdempotencyKey);
public sealed record CopyCustomerInvoiceDraftApiRequest(long ExpectedVersion, string IdempotencyKey, DateOnly IssueDate);
public sealed record IssueCustomerInvoiceDraftApiRequest(long ExpectedVersion, string IdempotencyKey,
    string ExpectedResultHash, Guid SeriesId, Guid FiscalPeriodId, DateOnly AccountingDate, string VoucherSeriesCode);

public sealed record CustomerInvoiceDraftIssueItemResponse(string ReasonCode, string Explanation, Guid? RelatedEntityId = null);
public sealed record CustomerInvoiceDraftEvidenceResponse(Guid DocumentId, string Title, string ContentHash, string OriginalFileName);
public sealed record CustomerInvoiceDraftTaxEvidenceResponse(string Classification, string? SourceReference = null);
public sealed record CustomerInvoiceDraftApprovalResponse(Guid Id, string Status, string? DecisionSummary,
    long DraftVersion, string ResultHash, DateTime CreatedUtc, DateTime? DecidedUtc, bool IsCurrent);
public sealed record CustomerInvoiceDraftLineResponse(Guid Id, int Sequence, string Description,
    decimal Quantity, string Unit, decimal UnitPrice, decimal DiscountPercent, decimal DiscountAmount,
    decimal NetAmount, string TaxRuleKey, string TaxRuleVersion, string TaxClassification, decimal TaxRate,
    decimal TaxAmount, decimal GrossAmount, string? RevenueAccountRoleKey, string? TaxAccountRoleKey,
    IReadOnlyList<string> VatBoxMappings, IReadOnlyList<CustomerInvoiceDraftTaxEvidenceResponse> TaxEvidence,
    IReadOnlyDictionary<string, string> DimensionFacts, string? SourceReference, string? OrderReference);
public sealed record CustomerInvoiceDraftTotalsResponse(decimal NetTotal, decimal DiscountTotal,
    decimal TaxTotal, decimal GrossTotal, decimal RoundingAmount, int RoundingPrecision, string RoundingMode);
public sealed record CustomerInvoiceDraftResponse(Guid Id, Guid CompanyId, Guid CustomerId,
    string CustomerName, string Status, string DocumentType, DateOnly IssueDate, DateOnly SupplyDate,
    DateOnly DueDate, string Currency, string PaymentTermKind, int PaymentTermDays, string? BuyerReference,
    string? SellerReference, string? Notes, string DeliveryIntent, string SourceKind, string? SourceReference,
    long Version, string InputHash, string ResultHash, string PolicyPackKey, string PolicyPackVersion,
    string PolicyDefinitionHash, Guid CreatedByUserId, Guid UpdatedByUserId, DateTime CreatedUtc,
    DateTime UpdatedUtc, DateTime? DiscardedUtc, CustomerInvoiceDraftTotalsResponse Totals,
    IReadOnlyList<CustomerInvoiceDraftLineResponse> Lines, IReadOnlyList<CustomerInvoiceDraftEvidenceResponse> Evidence,
    IReadOnlyList<CustomerInvoiceDraftIssueItemResponse> Warnings, IReadOnlyList<CustomerInvoiceDraftIssueItemResponse> Blockers,
    CustomerInvoiceDraftApprovalResponse? Approval, Guid? OriginalInvoiceId = null);
public sealed record CustomerInvoiceDraftPreviewResponse(CustomerInvoiceDraftResponse Draft, bool IsDeterministicReplay);
public sealed record CustomerInvoiceDraftReadinessResponse(bool IsAllowed, string ReasonCode, string Explanation,
    bool RequiresApproval, decimal ApprovalThreshold, string ApprovalCurrency,
    IReadOnlyList<CustomerInvoiceDraftIssueItemResponse> Blockers, IReadOnlyList<CustomerInvoiceDraftIssueItemResponse> Warnings,
    IReadOnlyDictionary<string, string> Evidence);
public sealed record CustomerInvoiceDraftListResponse(IReadOnlyList<CustomerInvoiceDraftResponse> Items,
    int TotalCount, int Skip, int Take);
public sealed record CustomerInvoiceDraftSubmissionResponse(CustomerInvoiceDraftResponse Draft,
    CustomerInvoiceDraftReadinessResponse Readiness, Guid ApprovalRequestId, bool IsIdempotentReplay);
public sealed record CustomerInvoiceDraftIssueResponse(Guid InvoiceId, Guid IssuedDocumentId, Guid LedgerEntryId,
    string DocumentNumber, string DeliveryState, string SnapshotHash, decimal NetTotal, decimal TaxTotal,
    decimal GrossTotal, string Currency, IReadOnlyList<string> AllowedNextActions, bool IsIdempotentReplay);
