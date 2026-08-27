namespace VirtualCompany.Application.Finance;

public static class NativeReceivablesReadinessStatuses
{
    public const string Healthy = "healthy";
    public const string Attention = "attention";
    public const string Blocking = "blocking";
}

public static class NativeReceivablesReadinessSignalKeys
{
    public const string StaleApprovals = "stale_approvals";
    public const string NumberingGaps = "numbering_gaps";
    public const string RenderFailures = "render_failures";
    public const string DeliveryAmbiguity = "delivery_ambiguity";
    public const string RecurringBlockers = "recurring_blockers";
    public const string ElectronicInvoiceRejections = "electronic_invoice_rejections";
    public const string RefundReconciliation = "refund_reconciliation";
    public const string ReceivablesControl = "receivables_control";
    public const string OverdueCollectionFollowUps = "overdue_collection_follow_ups";
    public const string DocumentArchiveFailures = "document_archive_failures";
}

public sealed record NativeReceivablesReadinessSignalDto(
    string Key,
    string Status,
    int Count,
    decimal? Amount,
    string? Currency,
    string Explanation,
    string OperatorAction,
    IReadOnlyList<Guid> SubjectIds);

public sealed record NativeReceivablesReadinessDto(
    Guid CompanyId,
    string Status,
    bool IsReady,
    DateTime EvaluatedUtc,
    int BlockingCheckCount,
    int AttentionCheckCount,
    int HealthyCheckCount,
    IReadOnlyList<NativeReceivablesReadinessSignalDto> Signals);

public sealed record GetNativeReceivablesReadinessQuery(Guid CompanyId);

public interface INativeReceivablesReadinessService
{
    Task<NativeReceivablesReadinessDto> GetAsync(
        GetNativeReceivablesReadinessQuery query,
        CancellationToken cancellationToken);
}
