namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<ManualJournalReferenceDataResponse?> GetManualJournalReferenceDataAsync(Guid companyId,
        CancellationToken cancellationToken = default) => GetAsync<ManualJournalReferenceDataResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/manual-journals/reference-data", false, cancellationToken);

    public Task<ManualJournalDraftListResponse?> ListManualJournalDraftsAsync(Guid companyId, string? status = null,
        int skip = 0, int take = 100, CancellationToken cancellationToken = default)
    {
        var query = $"?skip={Math.Max(0, skip)}&take={Math.Clamp(take, 1, 250)}";
        if (!string.IsNullOrWhiteSpace(status)) query += $"&status={Uri.EscapeDataString(status)}";
        return GetAsync<ManualJournalDraftListResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/manual-journals{query}", false, cancellationToken);
    }

    public Task<ManualJournalDraftResponse?> GetManualJournalDraftAsync(Guid companyId, Guid draftId,
        CancellationToken cancellationToken = default) => GetAsync<ManualJournalDraftResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/manual-journals/{draftId}", true, cancellationToken);

    public Task<ManualJournalDraftResponse> CreateManualJournalDraftAsync(Guid companyId, SaveManualJournalDraftApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveManualJournalDraftApiRequest, ManualJournalDraftResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/manual-journals", request, cancellationToken);
    }

    public Task<ManualJournalDraftResponse> UpdateManualJournalDraftAsync(Guid companyId, Guid draftId,
        SaveManualJournalDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveManualJournalDraftApiRequest, ManualJournalDraftResponse>(companyId, HttpMethod.Put,
            $"internal/companies/{companyId}/finance/accounting/manual-journals/{draftId}", request, cancellationToken);
    }

    public Task<ManualJournalPreviewResponse> PreviewManualJournalDraftAsync(Guid companyId, Guid draftId, long expectedVersion,
        CancellationToken cancellationToken = default) => SendCompanyScopedAsync<ManualJournalPreviewApiRequest, ManualJournalPreviewResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/manual-journals/{draftId}/preview",
            new() { ExpectedVersion = expectedVersion }, cancellationToken);

    public Task<ManualJournalSubmissionResponse> SubmitManualJournalDraftAsync(Guid companyId, Guid draftId, long expectedVersion,
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendManualJournalActionAsync<ManualJournalSubmissionResponse>(companyId, draftId, "submit", expectedVersion, idempotencyKey, cancellationToken);

    public Task<ManualJournalPostingResponse> PostManualJournalDraftAsync(Guid companyId, Guid draftId, long expectedVersion,
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendManualJournalActionAsync<ManualJournalPostingResponse>(companyId, draftId, "post", expectedVersion, idempotencyKey, cancellationToken);

    public Task<ManualJournalDraftResponse> DiscardManualJournalDraftAsync(Guid companyId, Guid draftId, long expectedVersion,
        string idempotencyKey, CancellationToken cancellationToken = default) =>
        SendManualJournalActionAsync<ManualJournalDraftResponse>(companyId, draftId, "discard", expectedVersion, idempotencyKey, cancellationToken);

    public Task<ManualJournalDraftResponse> CreateAdjustingJournalDraftAsync(Guid companyId, Guid journalId,
        SaveManualJournalDraftApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SaveManualJournalDraftApiRequest, ManualJournalDraftResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/journals/{journalId}/adjustments", request, cancellationToken);
    }

    private Task<T> SendManualJournalActionAsync<T>(Guid companyId, Guid draftId, string action, long expectedVersion,
        string idempotencyKey, CancellationToken cancellationToken)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ManualJournalVersionedActionApiRequest, T>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/manual-journals/{draftId}/{action}",
            new() { ExpectedVersion = expectedVersion, IdempotencyKey = idempotencyKey }, cancellationToken);
    }
}

public sealed class SaveManualJournalDraftApiRequest
{
    public long ExpectedVersion { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public string Explanation { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public List<ManualJournalLineApiRequest> Lines { get; set; } = [];
    public List<Guid> EvidenceDocumentIds { get; set; } = [];
    public Guid? OriginalLedgerEntryId { get; set; }
    public string? CorrectionReason { get; set; }
    public List<ManualJournalSourceReferenceApiRequest> SourceRecords { get; set; } = [];
}

public sealed class ManualJournalSourceReferenceApiRequest
{
    public string SourceType { get; set; } = string.Empty;
    public Guid RecordId { get; set; }
    public string SourceVersion { get; set; } = string.Empty;
}

public sealed class ManualJournalLineApiRequest
{
    public Guid FinanceAccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string? Description { get; set; }
    public Guid? CostCenterId { get; set; }
    public Dictionary<string, string>? TaxFacts { get; set; }
    public Dictionary<string, string>? DimensionFacts { get; set; }
}

public sealed class ManualJournalVersionedActionApiRequest { public long ExpectedVersion { get; set; } public string IdempotencyKey { get; set; } = string.Empty; }
public sealed class ManualJournalPreviewApiRequest { public long ExpectedVersion { get; set; } }
public sealed class ManualJournalDraftListResponse { public List<ManualJournalDraftResponse> Items { get; set; } = []; public int TotalCount { get; set; } public int Skip { get; set; } public int Take { get; set; } }
public sealed class ManualJournalDraftResponse
{
    public Guid Id { get; set; } public Guid CompanyId { get; set; } public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty; public DateOnly DocumentDate { get; set; }
    public DateOnly PostingDate { get; set; } public string Explanation { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty; public string Status { get; set; } = string.Empty;
    public long Version { get; set; } public string PayloadHash { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; } public Guid UpdatedByUserId { get; set; }
    public Guid? ApprovalRequestId { get; set; } public Guid? LedgerEntryId { get; set; }
    public Guid? OriginalLedgerEntryId { get; set; } public string? CorrectionReason { get; set; }
    public DateTime CreatedUtc { get; set; } public DateTime UpdatedUtc { get; set; } public DateTime? PostedUtc { get; set; }
    public decimal DebitTotal { get; set; } public decimal CreditTotal { get; set; } public decimal Difference { get; set; }
    public List<ManualJournalLineResponse> Lines { get; set; } = []; public List<ManualJournalEvidenceResponse> Evidence { get; set; } = [];
    public List<ManualJournalSourceReferenceResponse> SourceRecords { get; set; } = [];
    public ManualJournalApprovalResponse? Approval { get; set; }
}
public sealed class ManualJournalSourceReferenceResponse { public string SourceType { get; set; } = string.Empty; public Guid RecordId { get; set; } public string SourceVersion { get; set; } = string.Empty; }
public sealed class ManualJournalLineResponse { public Guid Id { get; set; } public int LineNumber { get; set; } public Guid FinanceAccountId { get; set; } public string AccountCode { get; set; } = string.Empty; public string AccountName { get; set; } = string.Empty; public decimal DebitAmount { get; set; } public decimal CreditAmount { get; set; } public string Currency { get; set; } = string.Empty; public string? Description { get; set; } public Guid? CostCenterId { get; set; } public Dictionary<string,string> TaxFacts { get; set; } = []; public Dictionary<string,string> DimensionFacts { get; set; } = []; }
public sealed class ManualJournalEvidenceResponse { public Guid DocumentId { get; set; } public string Title { get; set; } = string.Empty; public string ContentHash { get; set; } = string.Empty; public string OriginalFileName { get; set; } = string.Empty; }
public sealed class ManualJournalApprovalResponse { public Guid Id { get; set; } public string Status { get; set; } = string.Empty; public string? DecisionSummary { get; set; } public long DraftVersion { get; set; } public string PayloadHash { get; set; } = string.Empty; public DateTime CreatedUtc { get; set; } public DateTime? DecidedUtc { get; set; } }
public sealed class ManualJournalPreviewResponse { public ManualJournalDraftResponse Draft { get; set; } = new(); public AccountingPostingPreviewResponse PostingPreview { get; set; } = new(); public ManualJournalPolicyDecisionResponse Policy { get; set; } = new(); }
public sealed class ManualJournalPolicyDecisionResponse { public bool IsAllowed { get; set; } public bool RequiresApproval { get; set; } public decimal ApprovalThreshold { get; set; } public string ApprovalCurrency { get; set; } = string.Empty; public List<AccountingPostingIssueResponse> Issues { get; set; } = []; public List<AccountingPostingIssueResponse> Warnings { get; set; } = []; }
public sealed class ManualJournalSubmissionResponse { public ManualJournalDraftResponse Draft { get; set; } = new(); public Guid ApprovalRequestId { get; set; } public bool IsIdempotentReplay { get; set; } }
public sealed class ManualJournalPostingResponse { public ManualJournalDraftResponse Draft { get; set; } = new(); public AccountingJournalResponse Journal { get; set; } = new(); public bool IsIdempotentReplay { get; set; } }
public sealed class ManualJournalReferenceDataResponse { public List<ManualJournalVoucherSeriesResponse> VoucherSeries { get; set; } = []; public List<ManualJournalEvidenceOptionResponse> EvidenceDocuments { get; set; } = []; }
public sealed class ManualJournalVoucherSeriesResponse { public string Code { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public string NumberPrefix { get; set; } = string.Empty; }
public sealed class ManualJournalEvidenceOptionResponse { public Guid DocumentId { get; set; } public string Title { get; set; } = string.Empty; public string OriginalFileName { get; set; } = string.Empty; public DateTime UploadedUtc { get; set; } }
