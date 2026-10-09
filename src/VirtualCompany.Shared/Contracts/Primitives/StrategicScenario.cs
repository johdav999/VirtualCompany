using System.Text.Json.Serialization;

namespace VirtualCompany.Domain.Entities;

public sealed record ScenarioDrivers([property: JsonRequired] int Years, [property: JsonRequired] string CapacityUnit, [property: JsonRequired] decimal SourceToAnnualScale,
    [property: JsonRequired] decimal DemandUnits, [property: JsonRequired] decimal CapacityUnits, [property: JsonRequired] decimal DemandGrowthPercent, [property: JsonRequired] decimal PriceGrowthPercent,
    [property: JsonRequired] decimal CostGrowthPercent, [property: JsonRequired] decimal CapacityGrowthPercent, [property: JsonRequired] decimal VariableCostShare,
    [property: JsonRequired] decimal OpeningCash, [property: JsonRequired] decimal OpeningReceivables, [property: JsonRequired] decimal OpeningPayables,
    [property: JsonRequired] decimal CollectionShare, [property: JsonRequired] decimal PaymentShare, [property: JsonRequired] decimal CashFloor);
public sealed record ScenarioCashInput([property: JsonRequired] int Year, [property: JsonRequired] decimal Investment, [property: JsonRequired] decimal Funding, [property: JsonRequired] string Rationale);
public sealed record ScenarioYearResult(int Year, decimal Demand, decimal Capacity, decimal Fulfilled,
    decimal CapacityShortfall, decimal Revenue, decimal OperatingCost, decimal OpeningCash,
    decimal Collections, decimal Payments, decimal Investment, decimal Funding, decimal ClosingCash,
    decimal Receivables, decimal Payables, decimal FundingGap);