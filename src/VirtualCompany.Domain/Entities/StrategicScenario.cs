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

/// <summary>Bounded sensitivity model over retained Finance values, never a posting or forecast writer.</summary>
public static class StrategicScenarioCalculation
{
    public const string Version = "strategic-scenario.v1";
    public const string Formulas = "Demand and capacity grow annually; fulfilled=min(demand,capacity). Price=scaled source revenue/base demand × price growth. Revenue=fulfilled×price. Operating cost=fixed cost×cost growth + fulfilled×variable unit cost×cost growth. Collections=revenue×collection share + opening receivables. Payments=cost×payment share + opening payables. Closing cash=opening cash+collections−payments−additional investment+assumed funding. Remaining receivables/payables settle fully next year. Funding gap=max(0,cash floor−closing cash).";
    public const string Limitations = "Annual sensitivity scenarios in one currency. Explicit source-to-annual multiplier; source coverage may be partial. Incremental investments are cash only and exclude baseline costs. No FX, tax, interest, depreciation, monthly seasonality or defaults. Funding inputs are assumptions and grant no spending or payment authority. Money rounds to two decimals, units to four, away from zero; rounded balances carry forward.";
    private static decimal Money(decimal v) => Bounded(decimal.Round(v, 2, MidpointRounding.AwayFromZero));
    private static decimal Units(decimal v) => Bounded(decimal.Round(v, 4, MidpointRounding.AwayFromZero));
    private static decimal Bounded(decimal v) => Math.Abs(v) <= 1_000_000_000_000m ? v : throw new ArgumentException("Scenario exceeds the supported numeric range.");
    public static IReadOnlyList<ScenarioYearResult> Calculate(decimal sourceRevenue, decimal sourceExpense,
        ScenarioDrivers d, IReadOnlyList<ScenarioCashInput> cash)
    {
        if (d is null || cash is null || d.Years is < 3 or > 10 || string.IsNullOrWhiteSpace(d.CapacityUnit) || d.CapacityUnit.Length > 32 ||
            d.SourceToAnnualScale is <= 0 or > 12 || d.DemandUnits is <= 0 or > 1_000_000_000 || d.CapacityUnits is < 0 or > 1_000_000_000 ||
            sourceRevenue < 0 || sourceExpense < 0 || new[]{d.VariableCostShare,d.CollectionShare,d.PaymentShare}.Any(x=>x is < 0 or > 1) ||
            new[]{d.DemandGrowthPercent,d.PriceGrowthPercent,d.CostGrowthPercent,d.CapacityGrowthPercent}.Any(x=>x is < -100 or > 300) ||
            d.OpeningReceivables < 0 || d.OpeningPayables < 0 || d.CashFloor < 0 || cash.Count != d.Years ||
            cash.Select(x=>x.Year).Distinct().Count()!=d.Years || cash.Any(x=>x.Year<1||x.Year>d.Years||x.Investment<0||x.Funding<0||string.IsNullOrWhiteSpace(x.Rationale)||x.Rationale.Length>1000))
            throw new ArgumentException("Provide all annual cash inputs, rationale, units, a 3–10 year horizon and assumptions within the published limits.");
        foreach(var x in new[]{sourceRevenue,sourceExpense,d.OpeningCash,d.OpeningReceivables,d.OpeningPayables,d.CashFloor}.Concat(cash.SelectMany(x=>new[]{x.Investment,x.Funding}))) Bounded(x);
        if(cash.Any(x=>Money(x.Investment)!=x.Investment||Money(x.Funding)!=x.Funding))throw new ArgumentException("Investments and funding use two-decimal currency amounts.");
        try
        {
            var price=sourceRevenue*d.SourceToAnnualScale/d.DemandUnits;
            var fixedCost=sourceExpense*d.SourceToAnnualScale*(1-d.VariableCostShare);
            var variableCost=sourceExpense*d.SourceToAnnualScale*d.VariableCostShare/d.DemandUnits;
            var demand=d.DemandUnits;var capacity=d.CapacityUnits;
            var opening=Money(d.OpeningCash);var receivables=Money(d.OpeningReceivables);var payables=Money(d.OpeningPayables);
            var result=new List<ScenarioYearResult>();
            for(var year=1;year<=d.Years;year++)
            {
                var fulfilled=Units(Math.Min(demand,capacity));var revenue=Money(fulfilled*price);var cost=Money(fixedCost+fulfilled*variableCost);
                var collections=Money(revenue*d.CollectionShare+receivables);var payments=Money(cost*d.PaymentShare+payables);
                var flow=cash.Single(x=>x.Year==year);var close=Money(opening+collections-payments-flow.Investment+flow.Funding);
                receivables=Money(revenue*(1-d.CollectionShare));payables=Money(cost*(1-d.PaymentShare));
                result.Add(new(year,Units(demand),Units(capacity),fulfilled,Units(Math.Max(0,demand-capacity)),revenue,cost,opening,collections,payments,
                    flow.Investment,flow.Funding,close,receivables,payables,Money(Math.Max(0,d.CashFloor-close))));
                if(year<d.Years){opening=close;demand=Units(demand*(1+d.DemandGrowthPercent/100));capacity=Units(capacity*(1+d.CapacityGrowthPercent/100));
                price*=1+d.PriceGrowthPercent/100;fixedCost*=1+d.CostGrowthPercent/100;variableCost*=1+d.CostGrowthPercent/100;}
            }
            return result;
        }
        catch(OverflowException ex){throw new ArgumentException("Scenario exceeds the supported numeric range.",ex);}
    }
}

public sealed class StrategicScenarioVersion : ICompanyOwnedEntity
{
    public Guid Id { get; init; } public Guid CompanyId { get; init; } public Guid SeriesId { get; init; }
    public int Revision { get; init; } public Guid? PreviousId { get; init; } public Guid? DerivedFromId { get; init; }
    public Guid RequestId { get; init; } public Guid AuthorId { get; init; } public Guid OwnerId { get; init; }
    public string OwnerName { get; init; } = ""; public string Name { get; init; } = ""; public string Notes { get; init; } = "";
    public DateTime SavedUtc { get; init; } public Guid AnnualPlanId { get; init; } public Guid ForecastRevisionId { get; init; }
    public int FiscalYear { get; init; } public string Currency { get; init; } = "";
    public string CalculationVersion { get; init; } = ""; public string SourceJson { get; init; } = "";
    public decimal SourceRevenue { get; init; } public decimal SourceExpense { get; init; }
    public string Checksum { get; init; } = ""; public string CommandHash { get; init; } = "";
    public ScenarioDrivers Drivers { get; init; } = null!;
    public List<StrategicScenarioCash> Cash { get; init; } = [];
    public List<StrategicScenarioOutput> Outputs { get; init; } = [];
    public List<StrategicScenarioCheckpoint> Checkpoints { get; init; } = [];
}
public sealed class StrategicScenarioCash : ICompanyOwnedEntity
{
    public Guid Id { get; init; } public Guid CompanyId { get; init; } public Guid ScenarioId { get; init; }
    public int Year { get; init; } public decimal Investment { get; init; } public decimal Funding { get; init; }
    public string Rationale { get; init; } = "";
}
public sealed class StrategicScenarioOutput : ICompanyOwnedEntity
{
    public Guid Id { get; init; } public Guid CompanyId { get; init; } public Guid ScenarioId { get; init; }
    public ScenarioYearResult Result { get; init; } = null!;
}
public sealed class StrategicScenarioCheckpoint : ICompanyOwnedEntity
{
    public Guid Id { get; init; } public Guid CompanyId { get; init; } public Guid ScenarioId { get; init; }
    public int Year { get; init; } public string Title { get; init; } = ""; public Guid OwnerId { get; init; }
    public string OwnerName { get; init; } = ""; public Guid InitiativeId { get; init; }
}
