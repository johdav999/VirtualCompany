namespace VirtualCompany.Application.Finance;
public sealed record ExchangeRateLookupLeg(Guid ObservationId, string SourceKey, long SourceSetVersion, string FromCurrency, string ToCurrency, decimal SourceRate, decimal Factor, int RatePrecision, DateOnly EffectiveDate, int AgeDays, string QuotationConvention, string EvidenceChecksum)
{
    public Guid ObservationId { get; set; } = ObservationId;
    public string SourceKey { get; set; } = SourceKey;
    public long SourceSetVersion { get; set; } = SourceSetVersion;
    public string FromCurrency { get; set; } = FromCurrency;
    public string ToCurrency { get; set; } = ToCurrency;
    public decimal SourceRate { get; set; } = SourceRate;
    public decimal Factor { get; set; } = Factor;
    public int RatePrecision { get; set; } = RatePrecision;
    public DateOnly EffectiveDate { get; set; } = EffectiveDate;
    public int AgeDays { get; set; } = AgeDays;
    public string QuotationConvention { get; set; } = QuotationConvention;
    public string EvidenceChecksum { get; set; } = EvidenceChecksum;

    public ExchangeRateLookupLeg() : this(default !, string.Empty, default !, string.Empty, string.Empty, default !, default !, default !, default !, default !, string.Empty, string.Empty)
    {
    }
}

public sealed record ExchangeRateReadinessIssue(string ReasonCode, string Explanation, string Severity)
{
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string Severity { get; set; } = Severity;

    public ExchangeRateReadinessIssue() : this(string.Empty, string.Empty, string.Empty)
    {
    }
}

public sealed record ExchangeRateRefreshJobResult(Guid Id, Guid SourceId, string Status, DateOnly RequestedDate, IReadOnlyList<string> RequestedCurrencies, int AttemptCount, DateTime? NextAttemptUtc, string? FailureReasonCode, string? FailureSummary, Guid? RateSetId, long Version)
{
    public Guid Id { get; set; } = Id;
    public Guid SourceId { get; set; } = SourceId;
    public string Status { get; set; } = Status;
    public DateOnly RequestedDate { get; set; } = RequestedDate;
    public IReadOnlyList<string> RequestedCurrencies { get; set; } = RequestedCurrencies;
    public int AttemptCount { get; set; } = AttemptCount;
    public DateTime? NextAttemptUtc { get; set; } = NextAttemptUtc;
    public string? FailureReasonCode { get; set; } = FailureReasonCode;
    public string? FailureSummary { get; set; } = FailureSummary;
    public Guid? RateSetId { get; set; } = RateSetId;
    public long Version { get; set; } = Version;

    public ExchangeRateRefreshJobResult() : this(default !, default !, string.Empty, default !, [], default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record ExchangeRateLookupResult(string Status, string ReasonCode, string Explanation, string FromCurrency, string ToCurrency, DateOnly RequestedDate, string Purpose, decimal? EffectiveRate, DateOnly? SelectedRateDate, IReadOnlyList<ExchangeRateLookupLeg> Legs)
{
    public string Status { get; set; } = Status;
    public string ReasonCode { get; set; } = ReasonCode;
    public string Explanation { get; set; } = Explanation;
    public string FromCurrency { get; set; } = FromCurrency;
    public string ToCurrency { get; set; } = ToCurrency;
    public DateOnly RequestedDate { get; set; } = RequestedDate;
    public string Purpose { get; set; } = Purpose;
    public decimal? EffectiveRate { get; set; } = EffectiveRate;
    public DateOnly? SelectedRateDate { get; set; } = SelectedRateDate;
    public IReadOnlyList<ExchangeRateLookupLeg> Legs { get; set; } = Legs;
    public bool IsReady => Status == "ready";

    public ExchangeRateLookupResult() : this(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, default !, string.Empty, default !, default !, [])
    {
    }
}

public sealed record ExchangeRateObservationResult(Guid Id, Guid RateSetId, string SourceKey, long SourceSetVersion, string BaseCurrency, string QuoteCurrency, decimal Rate, int RatePrecision, string QuotationConvention, DateOnly EffectiveDate, DateTime ObservedUtc, Guid? CorrectsObservationId, string ApprovalStatus, string EvidenceChecksum)
{
    public Guid Id { get; set; } = Id;
    public Guid RateSetId { get; set; } = RateSetId;
    public string SourceKey { get; set; } = SourceKey;
    public long SourceSetVersion { get; set; } = SourceSetVersion;
    public string BaseCurrency { get; set; } = BaseCurrency;
    public string QuoteCurrency { get; set; } = QuoteCurrency;
    public decimal Rate { get; set; } = Rate;
    public int RatePrecision { get; set; } = RatePrecision;
    public string QuotationConvention { get; set; } = QuotationConvention;
    public DateOnly EffectiveDate { get; set; } = EffectiveDate;
    public DateTime ObservedUtc { get; set; } = ObservedUtc;
    public Guid? CorrectsObservationId { get; set; } = CorrectsObservationId;
    public string ApprovalStatus { get; set; } = ApprovalStatus;
    public string EvidenceChecksum { get; set; } = EvidenceChecksum;

    public ExchangeRateObservationResult() : this(default !, default !, string.Empty, default !, string.Empty, string.Empty, default !, default !, string.Empty, default !, default !, default !, string.Empty, string.Empty)
    {
    }
}

public sealed record ExchangeRateReadinessResult(string Status, string? FunctionalCurrency, int EnabledCurrencyCount, int EnabledSourceCount, int PendingReviewSetCount, int FailedRefreshJobCount, DateTime? LatestApprovedObservationUtc, IReadOnlyList<ExchangeRateReadinessIssue> Issues, IReadOnlyList<ExchangeRateSourceResult> Sources)
{
    public string Status { get; set; } = Status;
    public string? FunctionalCurrency { get; set; } = FunctionalCurrency;
    public int EnabledCurrencyCount { get; set; } = EnabledCurrencyCount;
    public int EnabledSourceCount { get; set; } = EnabledSourceCount;
    public int PendingReviewSetCount { get; set; } = PendingReviewSetCount;
    public int FailedRefreshJobCount { get; set; } = FailedRefreshJobCount;
    public DateTime? LatestApprovedObservationUtc { get; set; } = LatestApprovedObservationUtc;
    public IReadOnlyList<ExchangeRateReadinessIssue> Issues { get; set; } = Issues;
    public IReadOnlyList<ExchangeRateSourceResult> Sources { get; set; } = Sources;

    public ExchangeRateReadinessResult() : this(string.Empty, default !, default !, default !, default !, default !, default !, [], [])
    {
    }
}

public sealed record ExchangeRateSourceResult(Guid Id, string SourceKey, string DisplayName, string SourceKind, string SourceVersion, int Priority, bool RequiresApproval, int MaxStalenessDays, int RefreshIntervalHours, string LicenseSummary, bool IsEnabled, DateTime? LastSuccessfulRefreshUtc, DateTime? NextRefreshUtc, string? LastFailureReasonCode, string? LastFailureSummary, long Version)
{
    public Guid Id { get; set; } = Id;
    public string SourceKey { get; set; } = SourceKey;
    public string DisplayName { get; set; } = DisplayName;
    public string SourceKind { get; set; } = SourceKind;
    public string SourceVersion { get; set; } = SourceVersion;
    public int Priority { get; set; } = Priority;
    public bool RequiresApproval { get; set; } = RequiresApproval;
    public int MaxStalenessDays { get; set; } = MaxStalenessDays;
    public int RefreshIntervalHours { get; set; } = RefreshIntervalHours;
    public string LicenseSummary { get; set; } = LicenseSummary;
    public bool IsEnabled { get; set; } = IsEnabled;
    public DateTime? LastSuccessfulRefreshUtc { get; set; } = LastSuccessfulRefreshUtc;
    public DateTime? NextRefreshUtc { get; set; } = NextRefreshUtc;
    public string? LastFailureReasonCode { get; set; } = LastFailureReasonCode;
    public string? LastFailureSummary { get; set; } = LastFailureSummary;
    public long Version { get; set; } = Version;

    public ExchangeRateSourceResult() : this(default !, string.Empty, string.Empty, string.Empty, string.Empty, default !, default !, default !, default !, string.Empty, default !, default !, default !, default !, default !, default !)
    {
    }
}

public sealed record CurrencyDefinitionResult(Guid Id, string Code, string Name, int MinorUnitPrecision, bool IsEnabled, long Version)
{
    public Guid Id { get; set; } = Id;
    public string Code { get; set; } = Code;
    public string Name { get; set; } = Name;
    public int MinorUnitPrecision { get; set; } = MinorUnitPrecision;
    public bool IsEnabled { get; set; } = IsEnabled;
    public long Version { get; set; } = Version;

    public CurrencyDefinitionResult() : this(default !, string.Empty, string.Empty, default !, default !, default !)
    {
    }
}
