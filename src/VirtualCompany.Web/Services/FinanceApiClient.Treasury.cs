namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<TreasurySourceListResponse?> ListTreasurySourcesAsync(Guid companyId, string? status = null,
        Guid? bankTransactionId = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        var query = $"?limit={Math.Clamp(limit, 1, 500)}";
        if (!string.IsNullOrWhiteSpace(status)) query += $"&status={Uri.EscapeDataString(status)}";
        if (bankTransactionId.HasValue) query += $"&bankTransactionId={bankTransactionId.Value:D}";
        return GetAsync<TreasurySourceListResponse>(companyId,
            $"internal/companies/{companyId}/finance/treasury-sources{query}", false, cancellationToken);
    }

    public Task<TreasurySourceDetailResponse?> GetTreasurySourceAsync(Guid companyId, string sourceType,
        Guid sourceId, CancellationToken cancellationToken = default) => GetAsync<TreasurySourceDetailResponse>(
            companyId, TreasuryRoute(companyId, sourceType, sourceId), true, cancellationToken);

    public Task<TreasurySourceDetailResponse> CreateTreasuryTransferAsync(Guid companyId,
        CreateTreasuryTransferApiRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(companyId, "transfers", request, cancellationToken);
    public Task<TreasurySourceDetailResponse> CreateBankAdjustmentAsync(Guid companyId,
        CreateBankAdjustmentApiRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(companyId, "bank-adjustments", request, cancellationToken);
    public Task<TreasurySourceDetailResponse> CreateCardSettlementAsync(Guid companyId,
        CreateCardSettlementApiRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(companyId, "card-settlements", request, cancellationToken);
    public Task<TreasurySourceDetailResponse> CreatePayoutSettlementAsync(Guid companyId,
        CreatePayoutSettlementApiRequest request, CancellationToken cancellationToken = default) =>
        MutateAsync(companyId, "payout-settlements", request, cancellationToken);

    public Task<TreasurySourceDetailResponse> LinkTreasuryBankEvidenceAsync(Guid companyId, string sourceType,
        Guid sourceId, LinkTreasuryBankEvidenceApiRequest request, CancellationToken cancellationToken = default) =>
        MutateSourceAsync(companyId, sourceType, sourceId, "bank-evidence", request, cancellationToken);
    public Task<TreasurySourceDetailResponse> BindTreasuryApprovalAsync(Guid companyId, string sourceType,
        Guid sourceId, BindTreasuryApprovalApiRequest request, CancellationToken cancellationToken = default) =>
        MutateSourceAsync(companyId, sourceType, sourceId, "approval", request, cancellationToken);
    public Task<TreasuryPostingPreviewResponse> PreviewTreasuryPostingAsync(Guid companyId, string sourceType,
        Guid sourceId, PreviewTreasuryPostingApiRequest request, CancellationToken cancellationToken = default) =>
        MutateSourceResultAsync<TreasuryPostingPreviewResponse>(companyId, sourceType, sourceId, "preview", request, cancellationToken);
    public Task<TreasurySourceDetailResponse> PostTreasurySourceAsync(Guid companyId, string sourceType,
        Guid sourceId, PostTreasurySourceApiRequest request, CancellationToken cancellationToken = default) =>
        MutateSourceAsync(companyId, sourceType, sourceId, "post", request, cancellationToken);
    public Task<TreasurySourceDetailResponse> ReverseTreasurySourceAsync(Guid companyId, string sourceType,
        Guid sourceId, ReverseTreasurySourceApiRequest request, CancellationToken cancellationToken = default) =>
        MutateSourceAsync(companyId, sourceType, sourceId, "reverse", request, cancellationToken);

    private Task<TreasurySourceDetailResponse> MutateAsync<T>(Guid companyId, string segment, T request,
        CancellationToken cancellationToken)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<T, TreasurySourceDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/treasury-sources/{segment}", request, cancellationToken); }
    private Task<TreasurySourceDetailResponse> MutateSourceAsync<T>(Guid companyId, string sourceType, Guid sourceId,
        string action, T request, CancellationToken cancellationToken)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<T, TreasurySourceDetailResponse>(companyId, HttpMethod.Post, $"{TreasuryRoute(companyId, sourceType, sourceId)}/{action}", request, cancellationToken); }
    private Task<TResult> MutateSourceResultAsync<TResult>(Guid companyId, string sourceType, Guid sourceId,
        string action, object request, CancellationToken cancellationToken)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<object, TResult>(companyId, HttpMethod.Post, $"{TreasuryRoute(companyId, sourceType, sourceId)}/{action}", request, cancellationToken); }
    private static string TreasuryRoute(Guid companyId, string sourceType, Guid sourceId) =>
        $"internal/companies/{companyId}/finance/treasury-sources/{Uri.EscapeDataString(sourceType)}/{sourceId:D}";
}

public sealed class TreasurySourceListResponse
{
    public List<TreasurySourceSummaryResponse> Items { get; set; } = [];
    public int AttentionCount { get; set; } public int InTransitCount { get; set; }
    public int ReadyCount { get; set; } public int PostedCount { get; set; }
}
public sealed class TreasurySourceSummaryResponse
{
    public Guid Id { get; set; } public string SourceType { get; set; } = string.Empty;
    public string SourceIdentity { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; public string? ReasonCode { get; set; }
    public string Currency { get; set; } = string.Empty; public decimal GrossAmount { get; set; }
    public decimal FeeAmount { get; set; } public decimal NetAmount { get; set; }
    public bool RequiresApproval { get; set; } public Guid? ApprovalRequestId { get; set; }
    public long Version { get; set; } public DateTime UpdatedUtc { get; set; }
}
public sealed class TreasurySourceDetailResponse
{
    public TreasurySourceSummaryResponse Summary { get; set; } = new();
    public Guid? FromBankAccountId { get; set; } public Guid? ToBankAccountId { get; set; }
    public Guid? BankAccountId { get; set; } public Guid? CounterpartFinanceAccountId { get; set; }
    public Guid? CorrectionOfSourceId { get; set; } public List<TreasuryBankEvidenceResponse> BankEvidence { get; set; } = [];
    public List<TreasuryEvidenceResponse> Evidence { get; set; } = []; public List<TreasuryLedgerLinkResponse> Journals { get; set; } = [];
    public List<TreasurySourceEventResponse> History { get; set; } = []; public TreasuryAllowedActionsResponse AllowedActions { get; set; } = new();
    public TreasuryPostingPreviewResponse? PostingPreview { get; set; }
}
public sealed class TreasuryBankEvidenceResponse
{ public Guid BankTransactionId { get; set; } public string LegRole { get; set; } = string.Empty; public DateTime BookingDate { get; set; } public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty; public string Reference { get; set; } = string.Empty; public string Counterparty { get; set; } = string.Empty; }
public sealed class TreasuryEvidenceResponse
{ public Guid Id { get; set; } public string EvidenceType { get; set; } = string.Empty; public string Reference { get; set; } = string.Empty; public string ContentHash { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public DateTime CreatedUtc { get; set; } }
public sealed class TreasuryLedgerLinkResponse
{ public Guid LedgerEntryId { get; set; } public string EntryNumber { get; set; } = string.Empty; public string LinkRole { get; set; } = string.Empty; public DateTime CreatedUtc { get; set; } }
public sealed class TreasurySourceEventResponse
{ public Guid Id { get; set; } public string Action { get; set; } = string.Empty; public Guid ActorUserId { get; set; } public string? ReasonCode { get; set; } public DateTime CreatedUtc { get; set; } }
public sealed class TreasuryAllowedActionsResponse
{ public bool CanLinkBankEvidence { get; set; } public bool CanBindApproval { get; set; } public bool CanPreview { get; set; } public bool CanPost { get; set; } public bool CanReverse { get; set; } public string? BlockingReasonCode { get; set; } public string? Explanation { get; set; } }
public sealed class TreasuryPostingPreviewResponse
{ public bool CanPost { get; set; } public string? BlockingReasonCode { get; set; } public string? BlockingReason { get; set; } public AccountingPostingPreviewResponse? Accounting { get; set; } public List<TreasuryPostingLineResponse> Lines { get; set; } = []; }
public sealed class TreasuryPostingLineResponse
{ public Guid FinanceAccountId { get; set; } public string AccountCode { get; set; } = string.Empty; public string AccountName { get; set; } = string.Empty; public decimal DebitAmount { get; set; } public decimal CreditAmount { get; set; } public string Currency { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; }

public sealed class TreasuryEvidenceApiRequest
{ public string EvidenceType { get; set; } = string.Empty; public string Reference { get; set; } = string.Empty; public string ContentHash { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; }
public sealed class CreateTreasuryTransferApiRequest
{ public string SourceIdentity { get; set; } = string.Empty; public Guid FromBankAccountId { get; set; } public Guid ToBankAccountId { get; set; } public decimal Amount { get; set; } public decimal FeeAmount { get; set; } public string Currency { get; set; } = string.Empty; public Guid? FeeFinanceAccountId { get; set; } public decimal MaterialityThreshold { get; set; } public Guid? CorrectionOfTransferId { get; set; } public Guid? OutboundBankTransactionId { get; set; } public Guid? InboundBankTransactionId { get; set; } public List<TreasuryEvidenceApiRequest> Evidence { get; set; } = []; }
public sealed class CreateBankAdjustmentApiRequest
{ public string SourceIdentity { get; set; } = string.Empty; public string AdjustmentKind { get; set; } = string.Empty; public Guid BankAccountId { get; set; } public Guid BankTransactionId { get; set; } public Guid CounterpartFinanceAccountId { get; set; } public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty; public string Description { get; set; } = string.Empty; public decimal MaterialityThreshold { get; set; } public Guid? CorrectionOfAdjustmentId { get; set; } public List<TreasuryEvidenceApiRequest> Evidence { get; set; } = []; }
public sealed class CreateCardSettlementApiRequest
{ public string SourceIdentity { get; set; } = string.Empty; public string ProviderBatchReference { get; set; } = string.Empty; public Guid BankAccountId { get; set; } public Guid ReceivableFinanceAccountId { get; set; } public decimal GrossAmount { get; set; } public decimal FeeAmount { get; set; } public decimal NetAmount { get; set; } public string Currency { get; set; } = string.Empty; public decimal MaterialityThreshold { get; set; } public Guid? CorrectionOfSettlementId { get; set; } public Guid? BankTransactionId { get; set; } public List<TreasuryEvidenceApiRequest> Evidence { get; set; } = []; }
public sealed class CreatePayoutSettlementApiRequest
{ public string SourceIdentity { get; set; } = string.Empty; public string ProviderBatchReference { get; set; } = string.Empty; public Guid BankAccountId { get; set; } public Guid PayoutClearingFinanceAccountId { get; set; } public decimal GrossAmount { get; set; } public decimal FeeAmount { get; set; } public decimal NetAmount { get; set; } public string Currency { get; set; } = string.Empty; public decimal MaterialityThreshold { get; set; } public Guid? CorrectionOfSettlementId { get; set; } public Guid? BankTransactionId { get; set; } public List<TreasuryEvidenceApiRequest> Evidence { get; set; } = []; }
public sealed class LinkTreasuryBankEvidenceApiRequest
{ public Guid BankTransactionId { get; set; } public string? TransferLegRole { get; set; } public long ExpectedVersion { get; set; } }
public sealed class BindTreasuryApprovalApiRequest
{ public Guid ApprovalRequestId { get; set; } public long ExpectedVersion { get; set; } }
public class PreviewTreasuryPostingApiRequest
{ public Guid FiscalPeriodId { get; set; } public DateOnly PostingDate { get; set; } }
public class PostTreasurySourceApiRequest : PreviewTreasuryPostingApiRequest
{ public long ExpectedVersion { get; set; } }
public sealed class ReverseTreasurySourceApiRequest : PostTreasurySourceApiRequest
{ public string Reason { get; set; } = string.Empty; }
