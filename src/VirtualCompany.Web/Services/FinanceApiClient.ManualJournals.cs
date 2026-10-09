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
