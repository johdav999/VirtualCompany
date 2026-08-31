using System.Net.Http.Json;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<SupplierBillAccountingReferenceDataResponse> GetSupplierBillAccountingReferenceDataAsync(
        Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        GetAsync<SupplierBillAccountingReferenceDataResponse>(companyId,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting/reference-data", false, cancellationToken)!;

    public Task<SupplierBillAccountingPreviewResponse> PreviewSupplierBillAccountingAsync(
        Guid companyId, Guid billId, SupplierBillAccountingApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<SupplierBillAccountingApiRequest, SupplierBillAccountingPreviewResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting/preview", request, cancellationToken);

    public Task<SupplierBillAccountingSubmissionResponse> SubmitSupplierBillAccountingAsync(
        Guid companyId, Guid billId, SubmitSupplierBillAccountingApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<SubmitSupplierBillAccountingApiRequest, SupplierBillAccountingSubmissionResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting/submit", request, cancellationToken);

    public Task<SupplierBillAccountingPostingResponse> PostSupplierBillAccountingAsync(
        Guid companyId, Guid billId, PostSupplierBillAccountingApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<PostSupplierBillAccountingApiRequest, SupplierBillAccountingPostingResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting/post", request, cancellationToken);

    public Task<SupplierBillAccountingStateResponse?> GetSupplierBillAccountingAsync(
        Guid companyId, Guid billId, CancellationToken cancellationToken = default) =>
        GetAsync<SupplierBillAccountingStateResponse>(companyId,
            $"internal/companies/{companyId}/finance/bills/{billId}/accounting", true, cancellationToken);

    public Task<SupplierBillAccountingStateResponse> CreateNativeSupplierCreditNoteAsync(
        Guid companyId, Guid billId, CreateNativeSupplierCreditNoteApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<CreateNativeSupplierCreditNoteApiRequest, SupplierBillAccountingStateResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/bills/{billId}/native-credit-notes", request, cancellationToken);

    public Task<SupplierBillPayablesReconciliationResponse?> GetSupplierBillPayablesReconciliationAsync(
        Guid companyId, DateOnly? throughDate = null, CancellationToken cancellationToken = default)
    {
        var query = throughDate.HasValue ? $"?throughDate={throughDate:yyyy-MM-dd}" : string.Empty;
        return GetAsync<SupplierBillPayablesReconciliationResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/reconciliation/payables{query}", false, cancellationToken);
    }
}

public sealed class SupplierBillAccountingStateResponse
{
    public Guid BillId { get; set; }
    public Guid? ProfileId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StatusLabel { get; set; } = string.Empty;
    public bool CanPreview { get; set; }
    public bool CanSubmit { get; set; }
    public bool CanPost { get; set; }
    public bool CanCreateCreditNote { get; set; }
    public long? SourceVersion { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? RecoverableTaxAmount { get; set; }
    public decimal? NonRecoverableTaxAmount { get; set; }
    public decimal? GrossAmount { get; set; }
    public string? DocumentCurrency { get; set; }
    public decimal? ExchangeRate { get; set; }
    public decimal? GrossBaseAmount { get; set; }
    public string? BaseCurrency { get; set; }
    public string? TaxTreatment { get; set; }
    public string? PolicyPackKey { get; set; }
    public string? PolicyPackVersion { get; set; }
    public string? SourceDocumentHash { get; set; }
    public Guid? LedgerEntryId { get; set; }
    public string? VoucherNumber { get; set; }
    public Guid? OriginalBillId { get; set; }
    public string? BlockingReasonCode { get; set; }
    public string? BlockingReason { get; set; }
    public SupplierBillAccountingApprovalResponse? Approval { get; set; }
    public List<SupplierBillAccountingJournalLineResponse> JournalLines { get; set; } = [];
    public List<SupplierBillDuplicateEvidenceResponse> DuplicateEvidence { get; set; } = [];
    public List<SupplierBillAccountingIssueResponse> Issues { get; set; } = [];
    public DateOnly? ExchangeRateDate { get; set; }
    public Guid? ExchangeRateConversionId { get; set; }
    public string? ExchangeRateIdentity { get; set; }
    public decimal? ConversionRoundingResidual { get; set; }
    public string? CurrencyProvenance { get; set; }
}

public sealed class SupplierBillAccountingApprovalResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public long SourceVersion { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? DecidedUtc { get; set; }
}

public sealed class SupplierBillAccountingJournalLineResponse
{
    public Guid FinanceAccountId { get; set; }
    public string AccountRole { get; set; } = string.Empty;
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? TaxRuleKey { get; set; }
    public string? TaxTreatment { get; set; }
    public decimal? DocumentDebitAmount { get; set; }
    public decimal? DocumentCreditAmount { get; set; }
    public string? DocumentCurrency { get; set; }
}

public sealed class SupplierBillAccountingIssueResponse
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public bool IsBlocking { get; set; }
}

public sealed class SupplierBillDuplicateEvidenceResponse
{
    public Guid MatchedBillId { get; set; }
    public string BillNumber { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateOnly BillDate { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<string> MatchedFields { get; set; } = [];
}

public sealed class SupplierBillAccountingPreviewResponse
{
    public Guid BillId { get; set; }
    public bool IsReady { get; set; }
    public string AccountingStatus { get; set; } = string.Empty;
    public string DocumentKind { get; set; } = string.Empty;
    public decimal NetAmount { get; set; }
    public decimal RecoverableTaxAmount { get; set; }
    public decimal NonRecoverableTaxAmount { get; set; }
    public decimal GrossAmount { get; set; }
    public string DocumentCurrency { get; set; } = string.Empty;
    public decimal ExchangeRate { get; set; }
    public decimal CostBaseAmount { get; set; }
    public decimal RecoverableTaxBaseAmount { get; set; }
    public decimal GrossBaseAmount { get; set; }
    public decimal RoundingBaseAmount { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public long SourceVersion { get; set; }
    public string PayloadHash { get; set; } = string.Empty;
    public string? SourceDocumentHash { get; set; }
    public List<SupplierBillAccountingJournalLineResponse> JournalLines { get; set; } = [];
    public List<SupplierBillDuplicateEvidenceResponse> DuplicateEvidence { get; set; } = [];
    public List<SupplierBillAccountingIssueResponse> Issues { get; set; } = [];
    public DateOnly? ExchangeRateDate { get; set; }
    public string? ExchangeRateIdentity { get; set; }
    public List<ExchangeRateLookupLegResponse> ExchangeRateLegs { get; set; } = [];
}

public sealed class SupplierBillPayablesReconciliationResponse
{
    public Guid CompanyId { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public decimal PostedDocumentPayables { get; set; }
    public decimal PostedJournalPayables { get; set; }
    public decimal AllocatedAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal Difference { get; set; }
    public bool IsReconciled { get; set; }
    public DateTime AsOfUtc { get; set; }
    public List<DocumentCurrencyOpenItemControlResponse> DocumentCurrencyBreakdown { get; set; } = [];
}

public sealed class SupplierBillAccountingReferenceDataResponse
{
    public Guid BillId { get; set; }
    public string DocumentCurrency { get; set; } = string.Empty;
    public string BaseCurrency { get; set; } = string.Empty;
    public decimal GrossAmount { get; set; }
    public List<SupplierBillAccountingTaxRuleOptionResponse> TaxRules { get; set; } = [];
    public List<SupplierBillAccountingPeriodOptionResponse> OpenPeriods { get; set; } = [];
    public List<SupplierBillAccountingVoucherSeriesOptionResponse> VoucherSeries { get; set; } = [];
    public List<SupplierBillAccountingAccountOptionResponse> CostAccounts { get; set; } = [];
    public string? DefaultTaxRuleKey { get; set; }
    public Guid? DefaultPeriodId { get; set; }
    public string? DefaultVoucherSeriesCode { get; set; }
    public Guid? SuggestedCostAccountId { get; set; }
    public string? SuggestedCostAccountEvidence { get; set; }
}

public sealed class SupplierBillAccountingTaxRuleOptionResponse
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public decimal? Rate { get; set; }
    public string AmountMethod { get; set; } = string.Empty;
    public string TaxTreatment { get; set; } = string.Empty;
    public DateOnly EffectiveFrom { get; set; }
}

public sealed class SupplierBillAccountingPeriodOptionResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
}

public sealed class SupplierBillAccountingVoucherSeriesOptionResponse
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class SupplierBillAccountingAccountOptionResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountClass { get; set; } = string.Empty;
}

public class SupplierBillAccountingApiRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = "G";
    public decimal? ExchangeRate { get; set; }
    public List<SupplierBillAccountingLineApiRequest> Lines { get; set; } = [];
}

public sealed class SubmitSupplierBillAccountingApiRequest : SupplierBillAccountingApiRequest
{
    public long? ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class SupplierBillAccountingLineApiRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public Guid CostAccountId { get; set; }
    public string TaxRuleKey { get; set; } = string.Empty;
    public string? LineClassification { get; set; }
    public string? CounterpartyJurisdiction { get; set; }
    public string? CounterpartyVatStatus { get; set; }
    public List<AccountingTaxEvidenceApiRequest> TaxEvidence { get; set; } = [];
}

public sealed class PostSupplierBillAccountingApiRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

public sealed class SupplierBillAccountingSubmissionResponse
{
    public SupplierBillAccountingStateResponse State { get; set; } = new();
    public Guid ApprovalRequestId { get; set; }
    public bool IsIdempotentReplay { get; set; }
}

public sealed class SupplierBillAccountingPostingResponse
{
    public SupplierBillAccountingStateResponse State { get; set; } = new();
    public AccountingJournalResponse Journal { get; set; } = new();
    public bool IsIdempotentReplay { get; set; }
}

public sealed class CreateNativeSupplierCreditNoteApiRequest
{
    public string CreditNoteNumber { get; set; } = string.Empty;
    public DateOnly BillDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public SupplierBillAccountingApiRequest Accounting { get; set; } = new();
}
