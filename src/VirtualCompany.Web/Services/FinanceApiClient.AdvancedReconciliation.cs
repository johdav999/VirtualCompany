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

public sealed class AdvancedReconciliationWorkspaceResponse
{
    public List<AdvancedReconciliationGroupSummaryResponse> Groups { get; set; } = [];
    public AdvancedReconciliationQualityMetricsResponse Metrics { get; set; } = new();
    public AdvancedReconciliationRuleResponse? CurrentRule { get; set; }
}

public sealed class AdvancedReconciliationQualityMetricsResponse
{
    public int NeedsReviewCount { get; set; } public int LowConfidenceCount { get; set; } public int ConflictCount { get; set; }
    public int StaleCount { get; set; } public decimal AverageConfidence { get; set; } public decimal AcceptedValue { get; set; }
}

public sealed class AdvancedReconciliationRuleResponse
{
    public Guid Id { get; set; } public int Version { get; set; } public string Name { get; set; } = string.Empty;
    public decimal LowConfidenceThreshold { get; set; } public decimal MaterialityThreshold { get; set; }
}

public sealed class AdvancedReconciliationGroupSummaryResponse
{
    public Guid Id { get; set; } public string Reference { get; set; } = string.Empty; public string Counterparty { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty; public decimal ExpectedBankTotal { get; set; } public decimal ConfidenceScore { get; set; }
    public string Status { get; set; } = string.Empty; public string Cardinality { get; set; } = string.Empty;
    public int BankRowCount { get; set; } public int PaymentCount { get; set; } public int DocumentCount { get; set; }
    public int RuleVersion { get; set; } public long Version { get; set; } public bool RequiresApproval { get; set; }
    public bool IsStale { get; set; } public DateTime UpdatedUtc { get; set; }
}

public sealed class AdvancedReconciliationGroupDetailResponse
{
    public AdvancedReconciliationGroupSummaryResponse Summary { get; set; } = new();
    public decimal AllocatedAmount { get; set; } public decimal FeeAmount { get; set; } public decimal RoundingAmount { get; set; }
    public decimal ResidualAmount { get; set; } public decimal Variance { get; set; } public bool IsBalanced { get; set; }
    public string? BlockingReason { get; set; } public List<AdvancedReconciliationNodeResponse> Nodes { get; set; } = [];
    public List<AdvancedReconciliationEdgeResponse> Edges { get; set; } = [];
    public List<AdvancedReconciliationReasonContributionResponse> ReasonContributions { get; set; } = [];
    public List<AdvancedReconciliationResultResponse> Results { get; set; } = [];
    public List<AdvancedReconciliationEventResponse> History { get; set; } = [];
}

public sealed class AdvancedReconciliationNodeResponse
{
    public Guid Id { get; set; } public string NodeType { get; set; } = string.Empty; public Guid? RecordId { get; set; }
    public string Label { get; set; } = string.Empty; public string Reference { get; set; } = string.Empty; public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; } public string? Direction { get; set; } public string? AdjustmentKind { get; set; }
    public decimal DebitAmount { get; set; } public decimal CreditAmount { get; set; } public int Sequence { get; set; }
}

public sealed class AdvancedReconciliationEdgeResponse
{ public Guid Id { get; set; } public Guid SourceNodeId { get; set; } public Guid TargetNodeId { get; set; } public string EdgeType { get; set; } = string.Empty; public decimal Amount { get; set; } }
public sealed class AdvancedReconciliationReasonContributionResponse
{ public string FeatureKey { get; set; } = string.Empty; public decimal Contribution { get; set; } public string Explanation { get; set; } = string.Empty; public string Evidence { get; set; } = string.Empty; }
public sealed class AdvancedReconciliationResultResponse
{ public Guid Id { get; set; } public Guid? ParentResultId { get; set; } public string Outcome { get; set; } = string.Empty; public long GroupVersion { get; set; } public int RuleVersion { get; set; } public List<Guid> LedgerEntryIds { get; set; } = []; public DateTime CreatedUtc { get; set; } }
public sealed class AdvancedReconciliationEventResponse
{ public Guid Id { get; set; } public string Action { get; set; } = string.Empty; public DateTime CreatedUtc { get; set; } }

public sealed class AcceptAdvancedReconciliationApiRequest
{ public long ExpectedVersion { get; set; } public int ExpectedRuleVersion { get; set; } public string DecisionReason { get; set; } = string.Empty; }
public sealed class RejectAdvancedReconciliationApiRequest
{ public long ExpectedVersion { get; set; } public string DecisionReason { get; set; } = string.Empty; }
public sealed class ReverseAdvancedReconciliationApiRequest
{ public long ExpectedVersion { get; set; } public Guid FiscalPeriodId { get; set; } public DateOnly PostingDate { get; set; } public string Reason { get; set; } = string.Empty; }

