namespace VirtualCompany.Domain.Entities;
public sealed class DealRiskScoreSnapshot : ICompanyOwnedEntity
{
    private DealRiskScoreSnapshot()
    {
    }

    public DealRiskScoreSnapshot(
        Guid id,
        Guid companyId,
        Guid dealId,
        DateTime scoreDateUtc,
        decimal score,
        string band,
        string factorsSummary,
        DateTime calculatedUtc)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        DealId = SalesEntityText.NormalizeOptionalId(dealId, nameof(dealId))!.Value;
        ScoreDateUtc = SalesEntityText.NormalizeUtc(scoreDateUtc, nameof(scoreDateUtc)).Date;
        Score = ClampScore(score);
        Band = SalesEntityText.NormalizeRequired(band, nameof(band), 32).ToLowerInvariant();
        FactorsSummary = SalesEntityText.NormalizeRequired(factorsSummary, nameof(factorsSummary), 1000);
        CalculatedUtc = SalesEntityText.NormalizeUtc(calculatedUtc, nameof(calculatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid DealId { get; private set; }
    public DateTime ScoreDateUtc { get; private set; }
    public decimal Score { get; private set; }
    public string Band { get; private set; } = null!;
    public string FactorsSummary { get; private set; } = null!;
    public DateTime CalculatedUtc { get; private set; }
    public Deal Deal { get; private set; } = null!;

    public void Recalculate(decimal score, string band, string factorsSummary, DateTime calculatedUtc)
    {
        Score = ClampScore(score);
        Band = SalesEntityText.NormalizeRequired(band, nameof(band), 32).ToLowerInvariant();
        FactorsSummary = SalesEntityText.NormalizeRequired(factorsSummary, nameof(factorsSummary), 1000);
        CalculatedUtc = SalesEntityText.NormalizeUtc(calculatedUtc, nameof(calculatedUtc));
    }

    private static decimal ClampScore(decimal score) =>
        Math.Round(Math.Clamp(score, 0m, 1m), 4, MidpointRounding.AwayFromZero);
}

