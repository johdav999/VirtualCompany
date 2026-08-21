namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AccountingPostingPreviewResponse> PreviewAccountingJournalAsync(Guid companyId, ProposedAccountingEntryApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ProposedAccountingEntryApiRequest, AccountingPostingPreviewResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/journals/preview", request, cancellationToken);
    }

    public Task<PostedAccountingJournalResponse> PostAccountingJournalAsync(Guid companyId, ProposedAccountingEntryApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ProposedAccountingEntryApiRequest, PostedAccountingJournalResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/journals", request, cancellationToken);
    }

    public Task<PostedAccountingJournalResponse> ReverseAccountingJournalAsync(Guid companyId, Guid journalId, ReverseAccountingEntryApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ReverseAccountingEntryApiRequest, PostedAccountingJournalResponse>(companyId, HttpMethod.Post,
            $"internal/companies/{companyId}/finance/accounting/journals/{journalId}/reversal", request, cancellationToken);
    }

    public Task<AccountingJournalListResponse?> ListAccountingJournalsAsync(Guid companyId, DateOnly? from = null, DateOnly? to = null, int skip = 0, int take = 100,
        string? search = null, string? sourceType = null, string? postingType = null, string? voucherSeriesCode = null,
        CancellationToken cancellationToken = default)
    {
        var query = $"?skip={Math.Max(0, skip)}&take={Math.Clamp(take, 1, 250)}";
        if (from.HasValue) query += $"&from={from:yyyy-MM-dd}";
        if (to.HasValue) query += $"&to={to:yyyy-MM-dd}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        if (!string.IsNullOrWhiteSpace(sourceType)) query += $"&sourceType={Uri.EscapeDataString(sourceType)}";
        if (!string.IsNullOrWhiteSpace(postingType)) query += $"&postingType={Uri.EscapeDataString(postingType)}";
        if (!string.IsNullOrWhiteSpace(voucherSeriesCode)) query += $"&voucherSeriesCode={Uri.EscapeDataString(voucherSeriesCode)}";
        return GetAsync<AccountingJournalListResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/journals{query}", false, cancellationToken);
    }

    public Task<AccountingJournalResponse?> GetAccountingJournalAsync(Guid companyId, Guid journalId, CancellationToken cancellationToken = default) =>
        GetAsync<AccountingJournalResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/journals/{journalId}", true, cancellationToken);

    public Task<AccountingJournalResponse?> GetAccountingJournalBySourceAsync(Guid companyId, string sourceType, string sourceId, string? sourceVersion = null, CancellationToken cancellationToken = default)
    {
        var query = $"?sourceType={Uri.EscapeDataString(sourceType)}&sourceId={Uri.EscapeDataString(sourceId)}";
        if (!string.IsNullOrWhiteSpace(sourceVersion)) query += $"&sourceVersion={Uri.EscapeDataString(sourceVersion)}";
        return GetAsync<AccountingJournalResponse>(companyId, $"internal/companies/{companyId}/finance/accounting/journals/by-source{query}", true, cancellationToken);
    }
}

public sealed class ProposedAccountingEntryApiRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public DateOnly DocumentDate { get; set; }
    public DateOnly PostingDate { get; set; }
    public string PostingType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public List<ProposedAccountingLineApiRequest> Lines { get; set; } = [];
    public Guid? ApprovalRequestId { get; set; }
    public bool RequiresApproval { get; set; }
    public Dictionary<string, string>? PolicyFacts { get; set; }
    public string Action { get; set; } = "post";
}

public sealed class ProposedAccountingLineApiRequest
{
    public Guid FinanceAccountId { get; set; }
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Guid? CostCenterId { get; set; }
    public Dictionary<string, string>? TaxFacts { get; set; }
    public Dictionary<string, string>? DimensionFacts { get; set; }
}

public sealed class ReverseAccountingEntryApiRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public DateOnly PostingDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ApprovalRequestId { get; set; }
}

public sealed class AccountingPostingPreviewResponse
{
    public bool IsValid { get; set; }
    public decimal DebitTotal { get; set; }
    public decimal CreditTotal { get; set; }
    public decimal Difference { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public int RoundingPrecision { get; set; }
    public List<AccountingPostingIssueResponse> Issues { get; set; } = [];
}

public sealed class AccountingPostingIssueResponse
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public Guid? SubjectId { get; set; }
}

public sealed class PostedAccountingJournalResponse
{
    public AccountingJournalResponse Journal { get; set; } = new();
    public bool IsIdempotentReplay { get; set; }
}

public sealed class AccountingJournalListResponse
{
    public List<AccountingJournalResponse> Items { get; set; } = [];
    public int TotalCount { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public sealed class AccountingJournalResponse
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid FiscalPeriodId { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string VoucherSeriesCode { get; set; } = string.Empty;
    public long? VoucherSequenceNumber { get; set; }
    public int? VoucherFiscalYear { get; set; }
    public DateOnly? DocumentDate { get; set; }
    public DateOnly? PostingDate { get; set; }
    public string BaseCurrency { get; set; } = string.Empty;
    public string? PostingType { get; set; }
    public string? Description { get; set; }
    public string? SourceType { get; set; }
    public string? SourceId { get; set; }
    public string? SourceVersion { get; set; }
    public string? PolicyPackKey { get; set; }
    public string? PolicyPackVersion { get; set; }
    public Guid? PostedByUserId { get; set; }
    public Guid? ApprovalRequestId { get; set; }
    public Guid? OriginalLedgerEntryId { get; set; }
    public string? CorrectionReason { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public decimal DebitTotal { get; set; }
    public decimal CreditTotal { get; set; }
    public List<AccountingJournalLineResponse> Lines { get; set; } = [];
    public List<AccountingJournalEvidenceResponse> Evidence { get; set; } = [];
    public AccountingJournalApprovalResponse? Approval { get; set; }
    public List<AccountingJournalCorrectionResponse> Corrections { get; set; } = [];
    public List<AccountingJournalAuditEventResponse> AuditTimeline { get; set; } = [];
}

public sealed class AccountingJournalLineResponse
{
    public Guid Id { get; set; }
    public Guid FinanceAccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal DebitAmount { get; set; }
    public decimal CreditAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public Guid? CostCenterId { get; set; }
    public string? Description { get; set; }
    public Dictionary<string, string> TaxFacts { get; set; } = [];
    public Dictionary<string, string> DimensionFacts { get; set; } = [];
}

public sealed class AccountingJournalEvidenceResponse
{
    public Guid DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
}

public sealed class AccountingJournalApprovalResponse
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public string ApprovalType { get; set; } = string.Empty;
    public string? DecisionSummary { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? DecidedUtc { get; set; }
}

public sealed class AccountingJournalCorrectionResponse
{
    public Guid Id { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public string PostingType { get; set; } = string.Empty;
    public DateOnly? PostingDate { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class AccountingJournalAuditEventResponse
{
    public Guid Id { get; set; }
    public string ActorType { get; set; } = string.Empty;
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public DateTime OccurredUtc { get; set; }
}
