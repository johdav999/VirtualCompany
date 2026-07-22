namespace VirtualCompany.Domain.Entities;
public sealed class RevenueForecastSnapshot : ICompanyOwnedEntity
{
    private RevenueForecastSnapshot()
    {
    }

    public RevenueForecastSnapshot(
        Guid id,
        Guid companyId,
        DateTime asOfUtc,
        string currency,
        decimal grossPipeline30Days,
        decimal expectedRevenue30Days,
        int dealCount30Days,
        decimal grossPipeline60Days,
        decimal expectedRevenue60Days,
        int dealCount60Days,
        decimal grossPipeline90Days,
        decimal expectedRevenue90Days,
        int dealCount90Days,
        int unknownRiskDeals,
        int lowRiskDeals,
        int mediumRiskDeals,
        int highRiskDeals,
        DateTime calculatedUtc)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AsOfUtc = SalesEntityText.NormalizeUtc(asOfUtc, nameof(asOfUtc));
        Currency = SalesEntityText.NormalizeRequired(currency, nameof(currency), 3).ToUpperInvariant();
        GrossPipeline30Days = grossPipeline30Days;
        ExpectedRevenue30Days = expectedRevenue30Days;
        DealCount30Days = dealCount30Days;
        GrossPipeline60Days = grossPipeline60Days;
        ExpectedRevenue60Days = expectedRevenue60Days;
        DealCount60Days = dealCount60Days;
        GrossPipeline90Days = grossPipeline90Days;
        ExpectedRevenue90Days = expectedRevenue90Days;
        DealCount90Days = dealCount90Days;
        UnknownRiskDeals = unknownRiskDeals;
        LowRiskDeals = lowRiskDeals;
        MediumRiskDeals = mediumRiskDeals;
        HighRiskDeals = highRiskDeals;
        CalculatedUtc = SalesEntityText.NormalizeUtc(calculatedUtc, nameof(calculatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public DateTime AsOfUtc { get; private set; }
    public string Currency { get; private set; } = null!;
    public decimal GrossPipeline30Days { get; private set; }
    public decimal ExpectedRevenue30Days { get; private set; }
    public int DealCount30Days { get; private set; }
    public decimal GrossPipeline60Days { get; private set; }
    public decimal ExpectedRevenue60Days { get; private set; }
    public int DealCount60Days { get; private set; }
    public decimal GrossPipeline90Days { get; private set; }
    public decimal ExpectedRevenue90Days { get; private set; }
    public int DealCount90Days { get; private set; }
    public int UnknownRiskDeals { get; private set; }
    public int LowRiskDeals { get; private set; }
    public int MediumRiskDeals { get; private set; }
    public int HighRiskDeals { get; private set; }
    public DateTime CalculatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
}

