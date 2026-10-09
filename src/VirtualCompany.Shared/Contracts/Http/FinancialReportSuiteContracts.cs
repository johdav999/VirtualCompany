using VirtualCompany.Application.Finance;
namespace VirtualCompany.Api.Controllers;

public sealed class CaptureFinancialReportSnapshotRequest
{
    public Guid FiscalPeriodId { get; set; }
    public string ReportKind { get; set; } = string.Empty;
    public string CashFlowMethod { get; set; } = "indirect";
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? ComparisonFiscalPeriodId { get; set; }
    public int RollingPeriodCount { get; set; } = 12;
    public DateOnly? AsOfDate { get; set; }
    public Guid? DimensionTypeId { get; set; }
    public Guid? DimensionMemberId { get; set; }
}
