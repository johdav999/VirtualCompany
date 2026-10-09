

namespace VirtualCompany.Application.Sales;

public sealed record SalesForecastCurrencyWindow(int Days, string Currency, int DealCount, decimal GrossAmount, decimal ExpectedAmount);

public sealed record SalesOpportunityEvidence(Guid DealId, string Title, Guid StageId, string Stage, string Currency,
    decimal Amount, DateTime? ExpectedCloseUtc, DateTime UpdatedUtc, decimal StageProbability, decimal Risk,
    DateTime? RiskCalculatedUtc, decimal ExpectedAmount);
