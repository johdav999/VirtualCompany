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
