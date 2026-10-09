namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<AdvancedReconciliationWorkspaceResponse?> ListAdvancedReconciliationAsync(Guid companyId,
        string? status = null, string? search = null, decimal? maximumConfidence = null, int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var query = $"?limit={Math.Clamp(limit, 1, 500)}";
        if (!string.IsNullOrWhiteSpace(status)) query += $"&status={Uri.EscapeDataString(status)}";
        if (!string.IsNullOrWhiteSpace(search)) query += $"&search={Uri.EscapeDataString(search)}";
        if (maximumConfidence.HasValue) query += $"&maximumConfidence={maximumConfidence.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return GetAsync<AdvancedReconciliationWorkspaceResponse>(companyId,
            $"internal/companies/{companyId}/finance/advanced-reconciliation{query}", false, cancellationToken);
    }

    public Task<AdvancedReconciliationGroupDetailResponse?> GetAdvancedReconciliationAsync(Guid companyId, Guid groupId,
        CancellationToken cancellationToken = default) => GetAsync<AdvancedReconciliationGroupDetailResponse>(companyId,
            $"internal/companies/{companyId}/finance/advanced-reconciliation/{groupId}", true, cancellationToken);

    public Task<AdvancedReconciliationGroupDetailResponse> AcceptAdvancedReconciliationAsync(Guid companyId, Guid groupId,
        AcceptAdvancedReconciliationApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<AcceptAdvancedReconciliationApiRequest, AdvancedReconciliationGroupDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/advanced-reconciliation/{groupId}/accept", request, cancellationToken); }

    public Task<AdvancedReconciliationGroupDetailResponse> RejectAdvancedReconciliationAsync(Guid companyId, Guid groupId,
        RejectAdvancedReconciliationApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<RejectAdvancedReconciliationApiRequest, AdvancedReconciliationGroupDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/advanced-reconciliation/{groupId}/reject", request, cancellationToken); }

    public Task<AdvancedReconciliationGroupDetailResponse> ReverseAdvancedReconciliationAsync(Guid companyId, Guid groupId,
        ReverseAdvancedReconciliationApiRequest request, CancellationToken cancellationToken = default)
    { EnsureOnlineMutation(); return SendCompanyScopedAsync<ReverseAdvancedReconciliationApiRequest, AdvancedReconciliationGroupDetailResponse>(companyId, HttpMethod.Post, $"internal/companies/{companyId}/finance/advanced-reconciliation/{groupId}/reverse", request, cancellationToken); }
}

public sealed class AcceptAdvancedReconciliationApiRequest
{ public long ExpectedVersion { get; set; } public int ExpectedRuleVersion { get; set; } public string DecisionReason { get; set; } = string.Empty; }
public sealed class RejectAdvancedReconciliationApiRequest
{ public long ExpectedVersion { get; set; } public string DecisionReason { get; set; } = string.Empty; }
public sealed class ReverseAdvancedReconciliationApiRequest
{ public long ExpectedVersion { get; set; } public Guid FiscalPeriodId { get; set; } public DateOnly PostingDate { get; set; } public string Reason { get; set; } = string.Empty; }

