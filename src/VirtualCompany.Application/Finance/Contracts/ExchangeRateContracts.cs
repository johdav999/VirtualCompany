using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Finance;

public sealed record CurrencyDefinitionResult(
    Guid Id,
    string Code,
    string Name,
    int MinorUnitPrecision,
    bool IsEnabled,
    long Version);

public sealed record ExchangeRateSourceResult(
    Guid Id,
    string SourceKey,
    string DisplayName,
    string SourceKind,
    string SourceVersion,
    int Priority,
    bool RequiresApproval,
    int MaxStalenessDays,
    int RefreshIntervalHours,
    string LicenseSummary,
    bool IsEnabled,
    DateTime? LastSuccessfulRefreshUtc,
    DateTime? NextRefreshUtc,
    string? LastFailureReasonCode,
    string? LastFailureSummary,
    long Version);

public sealed record ExchangeRateSetResult(
    Guid Id,
    Guid SourceId,
    string SourceKey,
    long SetVersion,
    string ImportIdentity,
    string ContentHash,
    string Status,
    DateOnly EffectiveFrom,
    DateOnly EffectiveThrough,
    DateTime PublishedUtc,
    Guid? ImportedByUserId,
    Guid? ApprovedByUserId,
    Guid? CorrectsRateSetId,
    DateTime? ApprovedUtc,
    string? ReviewNote,
    long Version,
    int ObservationCount);

public sealed record ExchangeRateObservationResult(
    Guid Id,
    Guid RateSetId,
    string SourceKey,
    long SourceSetVersion,
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    int RatePrecision,
    string QuotationConvention,
    DateOnly EffectiveDate,
    DateTime ObservedUtc,
    Guid? CorrectsObservationId,
    string ApprovalStatus,
    string EvidenceChecksum);

public sealed record ExchangeRateLookupLeg(
    Guid ObservationId,
    string SourceKey,
    long SourceSetVersion,
    string FromCurrency,
    string ToCurrency,
    decimal SourceRate,
    decimal Factor,
    int RatePrecision,
    DateOnly EffectiveDate,
    int AgeDays,
    string QuotationConvention,
    string EvidenceChecksum);

public sealed record ExchangeRateLookupResult(
    string Status,
    string ReasonCode,
    string Explanation,
    string FromCurrency,
    string ToCurrency,
    DateOnly RequestedDate,
    string Purpose,
    decimal? EffectiveRate,
    DateOnly? SelectedRateDate,
    IReadOnlyList<ExchangeRateLookupLeg> Legs)
{
    public bool IsReady => Status == ExchangeRateDecisionStatuses.Ready;
}

public sealed record ExchangeRateConversionResult(
    Guid Id,
    string IdempotencyKey,
    string Purpose,
    DateOnly RequestedDate,
    decimal InputAmount,
    string InputCurrency,
    string OutputCurrency,
    decimal EffectiveRate,
    decimal UnroundedAmount,
    decimal RoundedAmount,
    decimal RoundingResidual,
    int OutputPrecision,
    string RoundingMode,
    DateTime CreatedUtc,
    IReadOnlyList<ExchangeRateLookupLeg> Legs);

public sealed record ExchangeRateReadinessIssue(string ReasonCode, string Explanation, string Severity);

public sealed record ExchangeRateReadinessResult(
    string Status,
    string? FunctionalCurrency,
    int EnabledCurrencyCount,
    int EnabledSourceCount,
    int PendingReviewSetCount,
    int FailedRefreshJobCount,
    DateTime? LatestApprovedObservationUtc,
    IReadOnlyList<ExchangeRateReadinessIssue> Issues,
    IReadOnlyList<ExchangeRateSourceResult> Sources);

public sealed record ConfigureCurrencyCommand(
    Guid CompanyId,
    Guid ActorUserId,
    string Code,
    string Name,
    int MinorUnitPrecision,
    bool IsEnabled,
    long? ExpectedVersion,
    string? CorrelationId);

public sealed record ConfigureExchangeRateSourceCommand(
    Guid CompanyId,
    Guid ActorUserId,
    string SourceKey,
    int Priority,
    bool RequiresApproval,
    int MaxStalenessDays,
    int RefreshIntervalHours,
    bool IsEnabled,
    long? ExpectedVersion,
    string? CorrelationId);

public sealed record ManualExchangeRateObservationInput(
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    int RatePrecision,
    string QuotationConvention,
    DateOnly EffectiveDate,
    Guid? CorrectsObservationId = null);

public sealed record ImportManualExchangeRateSetCommand(
    Guid CompanyId,
    Guid ActorUserId,
    string SourceKey,
    string SourceDisplayName,
    string ImportIdentity,
    DateTime PublishedUtc,
    IReadOnlyList<ManualExchangeRateObservationInput> Observations,
    string EvidenceDescription,
    Guid? CorrectsRateSetId,
    string? CorrelationId);

public sealed record ReviewExchangeRateSetCommand(
    Guid CompanyId,
    Guid ActorUserId,
    Guid RateSetId,
    long ExpectedVersion,
    bool Approve,
    string ReviewNote,
    string? CorrelationId);

public sealed record QueueExchangeRateRefreshCommand(
    Guid CompanyId,
    Guid ActorUserId,
    string ProviderKey,
    DateOnly RequestedDate,
    IReadOnlyCollection<string> Currencies,
    string IdempotencyKey,
    string? CorrelationId);

public sealed record ExchangeRateRefreshJobResult(
    Guid Id,
    Guid SourceId,
    string Status,
    DateOnly RequestedDate,
    IReadOnlyList<string> RequestedCurrencies,
    int AttemptCount,
    DateTime? NextAttemptUtc,
    string? FailureReasonCode,
    string? FailureSummary,
    Guid? RateSetId,
    long Version);

public sealed record ExchangeRateLookupQuery(
    Guid CompanyId,
    string FromCurrency,
    string ToCurrency,
    DateOnly Date,
    string Purpose);

public sealed record ConvertCurrencyCommand(
    Guid CompanyId,
    Guid ActorUserId,
    decimal Amount,
    string FromCurrency,
    string ToCurrency,
    DateOnly Date,
    string Purpose,
    string IdempotencyKey,
    string? CorrelationId);

public interface IExchangeRateService
{
    Task<IReadOnlyList<CurrencyDefinitionResult>> GetCurrenciesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExchangeRateSourceResult>> GetSourcesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ExchangeRateSetResult>> GetSetsAsync(Guid companyId, int skip, int take, CancellationToken cancellationToken);
    Task<ExchangeRateObservationResult> GetObservationAsync(Guid companyId, Guid observationId, CancellationToken cancellationToken);
    Task<ExchangeRateLookupResult> LookupAsync(ExchangeRateLookupQuery query, CancellationToken cancellationToken);
    Task<ExchangeRateReadinessResult> GetReadinessAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CurrencyDefinitionResult> ConfigureCurrencyAsync(ConfigureCurrencyCommand command, CancellationToken cancellationToken);
    Task<ExchangeRateSourceResult> ConfigureSourceAsync(ConfigureExchangeRateSourceCommand command, CancellationToken cancellationToken);
    Task<ExchangeRateSetResult> ImportManualAsync(ImportManualExchangeRateSetCommand command, CancellationToken cancellationToken);
    Task<ExchangeRateSetResult> ReviewSetAsync(ReviewExchangeRateSetCommand command, CancellationToken cancellationToken);
    Task<ExchangeRateRefreshJobResult> QueueRefreshAsync(QueueExchangeRateRefreshCommand command, CancellationToken cancellationToken);
    Task<ExchangeRateConversionResult> ConvertAsync(ConvertCurrencyCommand command, CancellationToken cancellationToken);
}

public sealed record ExchangeRateProviderDescriptor(
    string ProviderKey,
    string DisplayName,
    string AdapterVersion,
    string BaseCurrency,
    int DefaultPriority,
    bool RequiresApproval,
    int DefaultMaxStalenessDays,
    int DefaultRefreshIntervalHours,
    string LicenseSummary,
    IReadOnlyCollection<string> DefaultCurrencies);

public sealed record ExchangeRateProviderRequest(
    Guid CompanyId,
    DateOnly RequestedDate,
    IReadOnlyCollection<string> Currencies,
    string CorrelationId);

public sealed record ExchangeRateProviderObservation(
    string ProviderObservationId,
    string BaseCurrency,
    string QuoteCurrency,
    decimal Rate,
    int RatePrecision,
    string QuotationConvention,
    DateOnly EffectiveDate,
    DateTime ObservedUtc);

public sealed record ExchangeRateProviderResponse(
    string ImportIdentity,
    DateTime PublishedUtc,
    IReadOnlyList<ExchangeRateProviderObservation> Observations,
    string RawEvidence,
    string ContentType);

public interface IExchangeRateProvider
{
    ExchangeRateProviderDescriptor Descriptor { get; }
    Task<ExchangeRateProviderResponse> FetchAsync(ExchangeRateProviderRequest request, CancellationToken cancellationToken);
}

public interface IExchangeRateProviderRegistry
{
    IReadOnlyList<ExchangeRateProviderDescriptor> GetAll();
    IExchangeRateProvider GetRequired(string providerKey);
}

public interface IExchangeRateRefreshRunner
{
    Task<int> RunDueAsync(CancellationToken cancellationToken);
}

public sealed class ExchangeRateOperationException : Exception
{
    public ExchangeRateOperationException(string reasonCode, string safeMessage, bool conflict = false)
        : base(safeMessage)
    {
        ReasonCode = reasonCode;
        SafeMessage = safeMessage;
        IsConflict = conflict;
    }

    public string ReasonCode { get; }
    public string SafeMessage { get; }
    public bool IsConflict { get; }
}

public sealed class ExchangeRateProviderException : Exception
{
    public ExchangeRateProviderException(string reasonCode, string safeMessage, bool isTransient,
        TimeSpan? retryAfter = null, Exception? innerException = null) : base(safeMessage, innerException)
    {
        ReasonCode = reasonCode;
        SafeMessage = safeMessage;
        IsTransient = isTransient;
        RetryAfter = retryAfter;
    }

    public string ReasonCode { get; }
    public string SafeMessage { get; }
    public bool IsTransient { get; }
    public TimeSpan? RetryAfter { get; }
}
