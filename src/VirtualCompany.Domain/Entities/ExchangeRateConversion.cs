namespace VirtualCompany.Domain.Entities;

public sealed class ExchangeRateConversion : ICompanyOwnedEntity
{
    private ExchangeRateConversion() { }

    public ExchangeRateConversion(Guid id, Guid companyId, string idempotencyKey, string requestHash,
        string purpose, DateOnly requestedDate, decimal inputAmount, string inputCurrency,
        string outputCurrency, decimal effectiveRate, decimal unroundedAmount, decimal roundedAmount,
        decimal roundingResidual, int outputPrecision, string roundingMode, DateTime createdUtc)
    {
        Id = ExchangeRateText.Id(id, nameof(id));
        CompanyId = ExchangeRateText.Id(companyId, nameof(companyId));
        IdempotencyKey = ExchangeRateText.Required(idempotencyKey, 200, nameof(idempotencyKey));
        RequestHash = ExchangeRateText.Required(requestHash, 64, nameof(requestHash)).ToLowerInvariant();
        Purpose = ExchangeRateText.Token(purpose, 32, nameof(purpose));
        RequestedDate = requestedDate;
        InputAmount = inputAmount;
        InputCurrency = ExchangeRateText.Currency(inputCurrency, nameof(inputCurrency));
        OutputCurrency = ExchangeRateText.Currency(outputCurrency, nameof(outputCurrency));
        if (effectiveRate <= 0m) throw new ArgumentOutOfRangeException(nameof(effectiveRate));
        if (outputPrecision is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(outputPrecision));
        EffectiveRate = effectiveRate;
        UnroundedAmount = unroundedAmount;
        RoundedAmount = roundedAmount;
        RoundingResidual = roundingResidual;
        OutputPrecision = outputPrecision;
        RoundingMode = ExchangeRateText.Token(roundingMode, 32, nameof(roundingMode));
        CreatedUtc = ExchangeRateText.Utc(createdUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public string Purpose { get; private set; } = null!;
    public DateOnly RequestedDate { get; private set; }
    public decimal InputAmount { get; private set; }
    public string InputCurrency { get; private set; } = null!;
    public string OutputCurrency { get; private set; } = null!;
    public decimal EffectiveRate { get; private set; }
    public decimal UnroundedAmount { get; private set; }
    public decimal RoundedAmount { get; private set; }
    public decimal RoundingResidual { get; private set; }
    public int OutputPrecision { get; private set; }
    public string RoundingMode { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public ICollection<ExchangeRateConversionLeg> Legs { get; } = new List<ExchangeRateConversionLeg>();
}

public sealed class ExchangeRateConversionLeg : ICompanyOwnedEntity
{
    private ExchangeRateConversionLeg() { }

    public ExchangeRateConversionLeg(Guid id, Guid companyId, Guid conversionId, int sequence,
        Guid observationId, string fromCurrency, string toCurrency, decimal factor)
    {
        Id = ExchangeRateText.Id(id, nameof(id));
        CompanyId = ExchangeRateText.Id(companyId, nameof(companyId));
        ConversionId = ExchangeRateText.Id(conversionId, nameof(conversionId));
        ObservationId = ExchangeRateText.Id(observationId, nameof(observationId));
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (factor <= 0m) throw new ArgumentOutOfRangeException(nameof(factor));
        Sequence = sequence;
        FromCurrency = ExchangeRateText.Currency(fromCurrency, nameof(fromCurrency));
        ToCurrency = ExchangeRateText.Currency(toCurrency, nameof(toCurrency));
        Factor = factor;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid ConversionId { get; private set; }
    public int Sequence { get; private set; }
    public Guid ObservationId { get; private set; }
    public string FromCurrency { get; private set; } = null!;
    public string ToCurrency { get; private set; } = null!;
    public decimal Factor { get; private set; }
    public ExchangeRateConversion Conversion { get; private set; } = null!;
    public ExchangeRateObservation Observation { get; private set; } = null!;
}
