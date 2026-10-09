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
