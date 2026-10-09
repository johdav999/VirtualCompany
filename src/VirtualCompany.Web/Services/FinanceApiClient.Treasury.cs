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
