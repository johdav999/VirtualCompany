using VirtualCompany.Application.Finance;

namespace VirtualCompany.Api.Controllers;
[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record PrepareYearEndRunRequest(DateOnly FiscalYearStart, Guid TargetFiscalPeriodId, Guid RetainedEarningsAccountId, Guid OpeningBalanceClearingAccountId, string VoucherSeriesCode, string IdempotencyKey)
{
    public DateOnly FiscalYearStart { get; set; } = FiscalYearStart;
    public Guid TargetFiscalPeriodId { get; set; } = TargetFiscalPeriodId;
    public Guid RetainedEarningsAccountId { get; set; } = RetainedEarningsAccountId;
    public Guid OpeningBalanceClearingAccountId { get; set; } = OpeningBalanceClearingAccountId;
    public string VoucherSeriesCode { get; set; } = VoucherSeriesCode;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public PrepareYearEndRunRequest() : this(default !, default !, default !, default !, "YE", string.Empty)
    {
    }
}

[method: System.Text.Json.Serialization.JsonConstructor]
public sealed record RecordYearEndSubsequentEventRequest(DateOnly EventDate, string Title, string Description, decimal? EstimatedAmount, string Currency, string Decision, Guid OwnerUserId, Guid? EvidenceDocumentId, string IdempotencyKey)
{
    public DateOnly EventDate { get; set; } = EventDate;
    public string Title { get; set; } = Title;
    public string Description { get; set; } = Description;
    public decimal? EstimatedAmount { get; set; } = EstimatedAmount;
    public string Currency { get; set; } = Currency;
    public string Decision { get; set; } = Decision;
    public Guid OwnerUserId { get; set; } = OwnerUserId;
    public Guid? EvidenceDocumentId { get; set; } = EvidenceDocumentId;
    public string IdempotencyKey { get; set; } = IdempotencyKey;

    public RecordYearEndSubsequentEventRequest() : this(default !, string.Empty, string.Empty, default !, "SEK", "disclose", default !, default !, string.Empty)
    {
    }
}
