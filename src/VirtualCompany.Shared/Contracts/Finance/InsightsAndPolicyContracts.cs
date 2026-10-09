using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Agents;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;
public sealed record FinanceInsightEntityReferenceDto(string EntityType, string EntityId, string? DisplayName = null, bool IsPrimary = false)
{
    public string EntityType { get; set; } = EntityType;
    public string EntityId { get; set; } = EntityId;
    public string? DisplayName { get; set; } = DisplayName;
    public bool IsPrimary { get; set; } = IsPrimary;

    public FinanceInsightEntityReferenceDto() : this(string.Empty, string.Empty, default !, default !)
    {
    }
}

public sealed record FinanceAnomalyDetailDto(Guid Id, string AnomalyType, string Status, decimal Confidence, string? SupplierName, string Explanation, string RecommendedAction, DateTime DetectedAtUtc, FinanceAnomalyDeduplicationDto? Deduplication, FinanceAnomalyRelatedRecordDto? AffectedRecord, Guid? RelatedInvoiceId, string? RelatedInvoiceReference, Guid? RelatedBillId, string? RelatedBillReference, IReadOnlyList<FinanceAnomalyRecordLinkDto> RelatedRecordLinks, IReadOnlyList<FinanceAnomalyFollowUpTaskDto> FollowUpTasks)
{
    public Guid Id { get; set; } = Id;
    public string AnomalyType { get; set; } = AnomalyType;
    public string Status { get; set; } = Status;
    public decimal Confidence { get; set; } = Confidence;
    public string? SupplierName { get; set; } = SupplierName;
    public string Explanation { get; set; } = Explanation;
    public string RecommendedAction { get; set; } = RecommendedAction;
    public DateTime DetectedAtUtc { get; set; } = DetectedAtUtc;
    public FinanceAnomalyDeduplicationDto? Deduplication { get; set; } = Deduplication;
    public FinanceAnomalyRelatedRecordDto? AffectedRecord { get; set; } = AffectedRecord;
    public Guid? RelatedInvoiceId { get; set; } = RelatedInvoiceId;
    public string? RelatedInvoiceReference { get; set; } = RelatedInvoiceReference;
    public Guid? RelatedBillId { get; set; } = RelatedBillId;
    public string? RelatedBillReference { get; set; } = RelatedBillReference;
    public IReadOnlyList<FinanceAnomalyRecordLinkDto> RelatedRecordLinks { get; set; } = RelatedRecordLinks;
    public IReadOnlyList<FinanceAnomalyFollowUpTaskDto> FollowUpTasks { get; set; } = FollowUpTasks;

    public FinanceAnomalyDetailDto() : this(default !, string.Empty, string.Empty, default !, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !, default !, [], [])
    {
    }
}

public sealed record FinanceAnomalyRelatedRecordDto(Guid Id, string Reference, DateTime OccurredAtUtc, decimal Amount, string Currency, string? SupplierName)
{
    public Guid Id { get; set; } = Id;
    public string Reference { get; set; } = Reference;
    public DateTime OccurredAtUtc { get; set; } = OccurredAtUtc;
    public decimal Amount { get; set; } = Amount;
    public string Currency { get; set; } = Currency;
    public string? SupplierName { get; set; } = SupplierName;

    public FinanceAnomalyRelatedRecordDto() : this(default !, string.Empty, default !, default !, string.Empty, default !)
    {
    }
}

public sealed record FinanceAnomalyRecordLinkDto(Guid? RecordId, string RecordType, string Reference, DateTime? OccurredAtUtc, decimal? Amount, string? Currency)
{
    public Guid? RecordId { get; set; } = RecordId;
    public string RecordType { get; set; } = RecordType;
    public string Reference { get; set; } = Reference;
    public DateTime? OccurredAtUtc { get; set; } = OccurredAtUtc;
    public decimal? Amount { get; set; } = Amount;
    public string? Currency { get; set; } = Currency;

    public FinanceAnomalyRecordLinkDto() : this(default !, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record FinanceAnomalyFollowUpTaskDto(Guid Id, string Title, string Status, DateTime CreatedUtc, DateTime? DueUtc, DateTime UpdatedUtc)
{
    public Guid Id { get; set; } = Id;
    public string Title { get; set; } = Title;
    public string Status { get; set; } = Status;
    public DateTime CreatedUtc { get; set; } = CreatedUtc;
    public DateTime? DueUtc { get; set; } = DueUtc;
    public DateTime UpdatedUtc { get; set; } = UpdatedUtc;

    public FinanceAnomalyFollowUpTaskDto() : this(default !, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}

public sealed record NormalizedFinanceInsightDto(Guid Id, string Severity, string Message, string Recommendation, FinanceInsightEntityReferenceDto EntityReference, string Status, DateTime CreatedAt, DateTime UpdatedAt, string CheckCode, string CheckName, string ConditionKey, IReadOnlyList<FinanceInsightEntityReferenceDto> AffectedEntities, DateTime ObservedAt, DateTime? ResolvedAt)
{
    public Guid Id { get; set; } = Id;
    public string Severity { get; set; } = Severity;
    public string Message { get; set; } = Message;
    public string Recommendation { get; set; } = Recommendation;
    public FinanceInsightEntityReferenceDto EntityReference { get; set; } = EntityReference;
    public string Status { get; set; } = Status;
    public DateTime CreatedAt { get; set; } = CreatedAt;
    public DateTime UpdatedAt { get; set; } = UpdatedAt;
    public string CheckCode { get; set; } = CheckCode;
    public string CheckName { get; set; } = CheckName;
    public string ConditionKey { get; set; } = ConditionKey;
    public IReadOnlyList<FinanceInsightEntityReferenceDto> AffectedEntities { get; set; } = AffectedEntities;
    public DateTime ObservedAt { get; set; } = ObservedAt;
    public DateTime? ResolvedAt { get; set; } = ResolvedAt;

    public NormalizedFinanceInsightDto() : this(default !, string.Empty, string.Empty, string.Empty, new(), string.Empty, default !, default !, string.Empty, string.Empty, string.Empty, [], default !, default !)
    {
    }
}

public sealed record FinanceAnomalyDeduplicationDto(string? Key, DateTime? WindowStartUtc, DateTime? WindowEndUtc)
{
    public string? Key { get; set; } = Key;
    public DateTime? WindowStartUtc { get; set; } = WindowStartUtc;
    public DateTime? WindowEndUtc { get; set; } = WindowEndUtc;

    public FinanceAnomalyDeduplicationDto() : this(default !, default !, default !)
    {
    }
}

public sealed record FinanceCashPositionAlertStateDto(bool IsLowCash, string RiskLevel, bool AlertCreated, bool AlertDeduplicated, Guid? AlertId, string? AlertStatus, string Rationale)
{
    public bool IsLowCash { get; set; } = IsLowCash;
    public string RiskLevel { get; set; } = RiskLevel;
    public bool AlertCreated { get; set; } = AlertCreated;
    public bool AlertDeduplicated { get; set; } = AlertDeduplicated;
    public Guid? AlertId { get; set; } = AlertId;
    public string? AlertStatus { get; set; } = AlertStatus;
    public string Rationale { get; set; } = Rationale;

    public FinanceCashPositionAlertStateDto() : this(default !, string.Empty, default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record FinanceAnomalyWorkbenchItemDto(Guid Id, string AnomalyType, string Status, decimal Confidence, string? SupplierName, Guid? AffectedRecordId, string AffectedRecordReference, string ExplanationSummary, string RecommendedAction, DateTime DetectedAtUtc, FinanceAnomalyDeduplicationDto? Deduplication, Guid? FollowUpTaskId, string? FollowUpTaskStatus, Guid? RelatedInvoiceId, Guid? RelatedBillId)
{
    public Guid Id { get; set; } = Id;
    public string AnomalyType { get; set; } = AnomalyType;
    public string Status { get; set; } = Status;
    public decimal Confidence { get; set; } = Confidence;
    public string? SupplierName { get; set; } = SupplierName;
    public Guid? AffectedRecordId { get; set; } = AffectedRecordId;
    public string AffectedRecordReference { get; set; } = AffectedRecordReference;
    public string ExplanationSummary { get; set; } = ExplanationSummary;
    public string RecommendedAction { get; set; } = RecommendedAction;
    public DateTime DetectedAtUtc { get; set; } = DetectedAtUtc;
    public FinanceAnomalyDeduplicationDto? Deduplication { get; set; } = Deduplication;
    public Guid? FollowUpTaskId { get; set; } = FollowUpTaskId;
    public string? FollowUpTaskStatus { get; set; } = FollowUpTaskStatus;
    public Guid? RelatedInvoiceId { get; set; } = RelatedInvoiceId;
    public Guid? RelatedBillId { get; set; } = RelatedBillId;

    public FinanceAnomalyWorkbenchItemDto() : this(default !, string.Empty, string.Empty, default !, default !, default !, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record FinanceCashPositionThresholdsDto(int WarningRunwayDays, int CriticalRunwayDays, decimal? WarningCashAmount, decimal? CriticalCashAmount, string Currency)
{
    public int WarningRunwayDays { get; set; } = WarningRunwayDays;
    public int CriticalRunwayDays { get; set; } = CriticalRunwayDays;
    public decimal? WarningCashAmount { get; set; } = WarningCashAmount;
    public decimal? CriticalCashAmount { get; set; } = CriticalCashAmount;
    public string Currency { get; set; } = Currency;

    public FinanceCashPositionThresholdsDto() : this(default !, default !, default !, default !, string.Empty)
    {
    }
}

public sealed record FinanceAnomalyWorkbenchResultDto(int TotalCount, int Page, int PageSize, IReadOnlyList<FinanceAnomalyWorkbenchItemDto> Items)
{
    public int TotalCount { get; set; } = TotalCount;
    public int Page { get; set; } = Page;
    public int PageSize { get; set; } = PageSize;
    public IReadOnlyList<FinanceAnomalyWorkbenchItemDto> Items { get; set; } = Items;

    public FinanceAnomalyWorkbenchResultDto() : this(default !, default !, default !, [])
    {
    }
}
