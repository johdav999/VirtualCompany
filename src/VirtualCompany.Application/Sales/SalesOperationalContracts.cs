namespace VirtualCompany.Application.Sales;

public interface ISalesOperationalReportService
{
    Task<SalesOpportunityReport> GetOpportunitiesAsync(Guid companyId, bool forecast, int days, string? currency, Guid? stageId, CancellationToken cancellationToken);
    Task<SalesActivityReport> GetActivitiesAsync(Guid companyId, string status, Guid? dealId, CancellationToken cancellationToken);
    Task<SalesCommitmentDto?> RecordCommitmentAsync(Guid companyId, Guid userId, Guid dealId, RecordSalesCommitment request, CancellationToken cancellationToken);
    Task<SalesCommitmentDto?> ReviewCommitmentAsync(Guid companyId, Guid userId, Guid activityId, ReviewSalesCommitment request, CancellationToken cancellationToken);
}

public sealed record SalesOpportunityReport(Guid CompanyId, DateTime AsOfUtc, bool IsForecast, int Days,
    string? Currency, Guid? StageId, string CalculationVersion, IReadOnlyList<SalesOpportunityEvidence> Rows,
    IReadOnlyList<SalesCurrencyTotal> Totals, IReadOnlyList<string> AvailableCurrencies,
    IReadOnlyList<SalesForecastCurrencyWindow>? Windows = null);
public sealed record SalesForecastCurrencyWindow(int Days, string Currency, int DealCount, decimal GrossAmount, decimal ExpectedAmount);
public sealed record SalesOpportunityEvidence(Guid DealId, string Title, Guid StageId, string Stage, string Currency,
    decimal Amount, DateTime? ExpectedCloseUtc, DateTime UpdatedUtc, decimal StageProbability, decimal Risk,
    DateTime? RiskCalculatedUtc, decimal ExpectedAmount);
public sealed record SalesCurrencyTotal(string Currency, int DealCount, decimal GrossAmount, decimal ExpectedAmount);
public sealed record SalesActivityReport(Guid CompanyId, DateTime AsOfUtc, string Status, Guid? DealId,
    IReadOnlyList<SalesCommitmentDto> Commitments, IReadOnlyList<SalesMeetingCommitmentDto> Meetings);
public sealed record SalesCommitmentDto(Guid Id, Guid DealId, string DealTitle, string Summary, string Status,
    DateTime DueUtc, DateTime UpdatedUtc);
public sealed record SalesMeetingCommitmentDto(Guid Id, Guid LeadId, Guid? DealId, string Title, DateTime StartsUtc,
    DateTime EndsUtc, string Status, DateTime UpdatedUtc, string? FailureCode);
public sealed record RecordSalesCommitment(Guid CommandId, string Summary, DateTime DueUtc);
public sealed record ReviewSalesCommitment(DateTime ExpectedUpdatedUtc);

/// <summary>The owning revenue forecast formula, shared by stored aggregates and inspectable current reports.</summary>
public static class RevenueForecastCalculation
{
    public const string Version = "stage-risk-v1";
    public static decimal StageProbability(Guid stageId) =>
        stageId == VirtualCompany.Domain.Entities.SalesPipelineStage.ProposalStageId ? 0.70m :
        stageId == VirtualCompany.Domain.Entities.SalesPipelineStage.QualifiedStageId ? 0.45m :
        stageId == VirtualCompany.Domain.Entities.SalesPipelineStage.WonStageId ? 1m : 0.20m;
    public static decimal ExpectedAmount(decimal amount, Guid stageId, decimal risk) =>
        Math.Round(amount * StageProbability(stageId) * (1m - risk * 0.50m), 2);
}
