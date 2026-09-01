using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ExchangeRateAuthorityOptions
{
    public const string SectionName = "ExchangeRates:Authority";
    public int RawEvidenceRetentionDays { get; set; } = 2555;
    public int MaximumManualObservations { get; set; } = 1000;
    public int MaximumLookupCandidates { get; set; } = 2000;
}

public sealed class ExchangeRateService : IExchangeRateService
{
    private const string EvidencePurposePrefix = "exchange-rate-evidence";
    private readonly VirtualCompanyDbContext _db;
    private readonly IExchangeRateProviderRegistry _providers;
    private readonly IFieldEncryptionService _encryption;
    private readonly IAuditEventWriter _audit;
    private readonly ExchangeRateAuthorityOptions _options;
    private readonly ExchangeRateTelemetry _telemetry;
    private readonly TimeProvider _time;

    public ExchangeRateService(VirtualCompanyDbContext db, IExchangeRateProviderRegistry providers,
        IFieldEncryptionService encryption, IAuditEventWriter audit,
        IOptions<ExchangeRateAuthorityOptions> options, ExchangeRateTelemetry telemetry,
        TimeProvider time)
    {
        _db = db;
        _providers = providers;
        _encryption = encryption;
        _audit = audit;
        _options = options.Value;
        _telemetry = telemetry;
        _time = time;
    }

    public async Task<IReadOnlyList<CurrencyDefinitionResult>> GetCurrenciesAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId));
        var currencies = await _db.CompanyCurrencyDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Code)
            .Take(200)
            .ToArrayAsync(cancellationToken);
        if (currencies.Length > 0) return currencies.Select(MapCurrency).ToArray();

        var baseCurrency = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).Select(x => x.BaseCurrency)
            .SingleOrDefaultAsync(cancellationToken);
        return baseCurrency is null
            ? []
            : [new CurrencyDefinitionResult(Guid.Empty, baseCurrency, baseCurrency, MinorUnits(baseCurrency), true, 0)];
    }

    public async Task<IReadOnlyList<ExchangeRateSourceResult>> GetSourcesAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId));
        return (await _db.ExchangeRateSources.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.Priority).ThenBy(x => x.SourceKey)
            .Take(100).ToArrayAsync(cancellationToken)).Select(MapSource).ToArray();
    }

    public async Task<IReadOnlyList<ExchangeRateSetResult>> GetSetsAsync(
        Guid companyId, int skip, int take, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId));
        if (skip < 0) throw new ArgumentOutOfRangeException(nameof(skip));
        if (take is < 1 or > FinanceAdvancedAccountingAgentContract.MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(take));

        var sets = await _db.ExchangeRateSets.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Source)
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.PublishedUtc)
            .ThenByDescending(x => x.SetVersion)
            .Skip(skip)
            .Take(take)
            .ToArrayAsync(cancellationToken);
        var results = new List<ExchangeRateSetResult>(sets.Length);
        foreach (var set in sets)
            results.Add(await MapSetAsync(set, cancellationToken));
        return results;
    }

    public async Task<ExchangeRateObservationResult> GetObservationAsync(
        Guid companyId, Guid observationId, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId)); Require(observationId, nameof(observationId));
        var observation = await _db.ExchangeRateObservations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.RateSet).ThenInclude(x => x.Source)
            .Include(x => x.RateSet).ThenInclude(x => x.Evidence)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == observationId, cancellationToken)
            ?? throw new KeyNotFoundException("The exchange-rate observation was not found.");
        return MapObservation(observation);
    }

    public async Task<ExchangeRateLookupResult> LookupAsync(
        ExchangeRateLookupQuery query, CancellationToken cancellationToken)
    {
        var result = await LookupCoreAsync(query, cancellationToken);
        _telemetry.Lookup(result.Purpose, result.Status, result.Legs.Count, result.ReasonCode);
        return result;
    }

    public async Task<ExchangeRateReadinessResult> GetReadinessAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId));
        var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        var currencies = await _db.CompanyCurrencyDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsEnabled).OrderBy(x => x.Code)
            .Take(200).ToArrayAsync(cancellationToken);
        var sources = await _db.ExchangeRateSources.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).OrderBy(x => x.Priority).Take(100)
            .ToArrayAsync(cancellationToken);
        var pending = await _db.ExchangeRateSets.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.Status == ExchangeRateSetStatuses.PendingReview, cancellationToken);
        var failed = await _db.ExchangeRateRefreshJobs.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.Status == ExchangeRateRefreshJobStatuses.Failed, cancellationToken);
        var latest = await (from observation in _db.ExchangeRateObservations.IgnoreQueryFilters().AsNoTracking()
                            join set in _db.ExchangeRateSets.IgnoreQueryFilters().AsNoTracking()
                                on new { observation.CompanyId, Id = observation.RateSetId } equals new { set.CompanyId, set.Id }
                            where observation.CompanyId == companyId && set.Status == ExchangeRateSetStatuses.Approved
                            select (DateTime?)observation.ObservedUtc).MaxAsync(cancellationToken);
        var observedCurrencies = await (from observation in _db.ExchangeRateObservations.IgnoreQueryFilters().AsNoTracking()
                                        join set in _db.ExchangeRateSets.IgnoreQueryFilters().AsNoTracking()
                                            on new { observation.CompanyId, Id = observation.RateSetId } equals new { set.CompanyId, set.Id }
                                        join source in _db.ExchangeRateSources.IgnoreQueryFilters().AsNoTracking()
                                            on new { set.CompanyId, Id = set.SourceId } equals new { source.CompanyId, source.Id }
                                        where observation.CompanyId == companyId && set.Status == ExchangeRateSetStatuses.Approved && source.IsEnabled
                                        select new { observation.BaseCurrency, observation.QuoteCurrency })
            .Take(5000).ToArrayAsync(cancellationToken);

        var issues = new List<ExchangeRateReadinessIssue>();
        if (configuration is null)
            issues.Add(new(ExchangeRateReasonCodes.MissingAccountingConfiguration,
                "Complete accounting configuration before exchange rates can become accounting authority.", "blocking"));
        if (pending > 0)
            issues.Add(new(ExchangeRateReasonCodes.PendingApproval,
                $"{pending} exchange-rate set(s) require review before they can be selected.", "warning"));
        if (failed > 0)
            issues.Add(new(ExchangeRateReasonCodes.ProviderFailure,
                $"{failed} provider refresh job(s) require operator attention.", "blocking"));
        foreach (var currency in currencies.Where(x => !string.Equals(x.Code, configuration?.BaseCurrency, StringComparison.OrdinalIgnoreCase)))
        {
            if (!observedCurrencies.Any(x => x.BaseCurrency == currency.Code || x.QuoteCurrency == currency.Code))
                issues.Add(new(ExchangeRateReasonCodes.MissingRate,
                    $"No approved observation is available for enabled currency {currency.Code}.", "blocking"));
        }
        if (currencies.Count(x => x.IsEnabled) > 1 && sources.All(x => !x.IsEnabled))
            issues.Add(new(ExchangeRateReasonCodes.ProviderUnavailable,
                "Enable an exchange-rate source before using foreign-currency accounting.", "blocking"));

        var status = issues.Any(x => x.Severity == "blocking")
            ? ExchangeRateDecisionStatuses.Blocked
            : issues.Count > 0 ? ExchangeRateDecisionStatuses.ReviewRequired : ExchangeRateDecisionStatuses.Ready;
        return new ExchangeRateReadinessResult(status, configuration?.BaseCurrency, currencies.Length,
            sources.Count(x => x.IsEnabled), pending, failed, latest, issues, sources.Select(MapSource).ToArray());
    }

    public async Task<CurrencyDefinitionResult> ConfigureCurrencyAsync(
        ConfigureCurrencyCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var now = Now();
        var code = Currency(command.Code);
        var existing = await _db.CompanyCurrencyDefinitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Code == code, cancellationToken);
        if (existing is null)
        {
            if (command.ExpectedVersion is > 0)
                throw Conflict("The currency definition no longer exists. Reload before retrying.");
            existing = new CompanyCurrencyDefinition(Guid.NewGuid(), command.CompanyId, code,
                command.Name, command.MinorUnitPrecision, command.IsEnabled, now);
            _db.CompanyCurrencyDefinitions.Add(existing);
        }
        else
        {
            if (!command.ExpectedVersion.HasValue) throw Conflict("An expected version is required when changing a currency definition.");
            try { existing.Configure(command.Name, command.MinorUnitPrecision, command.IsEnabled, command.ExpectedVersion.Value, now); }
            catch (InvalidOperationException) { throw Conflict("The currency definition changed. Reload before retrying."); }
        }
        await AuditAsync(command.CompanyId, command.ActorUserId, "exchange_rate.currency_configured",
            "currency_definition", existing.Id, "Currency support was configured.", command.CorrelationId,
            new Dictionary<string, string?> { ["currency"] = code, ["enabled"] = command.IsEnabled.ToString() }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return MapCurrency(existing);
    }

    public async Task<ExchangeRateSourceResult> ConfigureSourceAsync(
        ConfigureExchangeRateSourceCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var provider = _providers.GetRequired(command.SourceKey);
        var now = Now();
        var source = await _db.ExchangeRateSources.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SourceKey == provider.Descriptor.ProviderKey,
                cancellationToken);
        if (source is null)
        {
            if (command.ExpectedVersion is > 0) throw Conflict("The exchange-rate source no longer exists. Reload before retrying.");
            source = CreateProviderSource(command.CompanyId, provider.Descriptor, command.Priority,
                command.RequiresApproval, command.MaxStalenessDays, command.RefreshIntervalHours,
                command.IsEnabled, now);
            _db.ExchangeRateSources.Add(source);
        }
        else
        {
            if (source.SourceKind != ExchangeRateSourceKinds.Provider)
                throw new ExchangeRateOperationException(ExchangeRateReasonCodes.ImportConflict,
                    "The source key is already owned by a different source kind.", true);
            if (!command.ExpectedVersion.HasValue) throw Conflict("An expected version is required when changing an exchange-rate source.");
            try { source.Configure(command.Priority, command.RequiresApproval, command.MaxStalenessDays,
                command.RefreshIntervalHours, command.IsEnabled, command.ExpectedVersion.Value, now); }
            catch (InvalidOperationException) { throw Conflict("The exchange-rate source changed. Reload before retrying."); }
        }
        await EnsureCurrencyAsync(command.CompanyId, provider.Descriptor.BaseCurrency, true, now, cancellationToken);
        foreach (var currency in provider.Descriptor.DefaultCurrencies)
            await EnsureCurrencyAsync(command.CompanyId, currency, true, now, cancellationToken);
        await AuditAsync(command.CompanyId, command.ActorUserId, "exchange_rate.source_configured",
            "exchange_rate_source", source.Id, "Exchange-rate source policy was configured.", command.CorrelationId,
            new Dictionary<string, string?> { ["sourceKey"] = source.SourceKey, ["enabled"] = source.IsEnabled.ToString() }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return MapSource(source);
    }

    public async Task<ExchangeRateSetResult> ImportManualAsync(
        ImportManualExchangeRateSetCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        if (command.Observations.Count is < 1 || command.Observations.Count > Math.Clamp(_options.MaximumManualObservations, 1, 5000))
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.InvalidRate,
                $"A manual rate set must contain between 1 and {Math.Clamp(_options.MaximumManualObservations, 1, 5000)} observations.");
        var now = Now();
        var sourceKey = Token(command.SourceKey, 64);
        var normalized = command.Observations.Select(NormalizeManual).OrderBy(x => x.EffectiveDate)
            .ThenBy(x => x.BaseCurrency).ThenBy(x => x.QuoteCurrency).ToArray();
        if (normalized.GroupBy(x => new { x.BaseCurrency, x.QuoteCurrency, x.EffectiveDate }).Any(x => x.Count() > 1))
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.AmbiguousRate,
                "A rate set cannot contain multiple observations for the same pair and effective date.");
        var canonical = JsonSerializer.Serialize(new
        {
            SourceKey = sourceKey,
            ImportIdentity = Required(command.ImportIdentity, 200),
            PublishedUtc = Utc(command.PublishedUtc).ToString("O", CultureInfo.InvariantCulture),
            EvidenceDescription = Required(command.EvidenceDescription, 2000),
            CorrectsRateSetId = command.CorrectsRateSetId,
            Observations = normalized
        });
        var contentHash = Hash(canonical);

        var source = await _db.ExchangeRateSources.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SourceKey == sourceKey, cancellationToken);
        if (source is null)
        {
            source = new ExchangeRateSource(Guid.NewGuid(), command.CompanyId, sourceKey,
                command.SourceDisplayName, ExchangeRateSourceKinds.Manual, "manual-v1", 0, true,
                31, 24, "Company-approved manual exchange-rate evidence.", true, now);
            _db.ExchangeRateSources.Add(source);
        }
        else if (source.SourceKind != ExchangeRateSourceKinds.Manual)
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.ImportConflict,
                "The source key is already owned by a provider source.", true);
        else if (!source.IsEnabled)
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.ProviderUnavailable,
                "Enable the manual exchange-rate source before importing observations.");

        var existing = await _db.ExchangeRateSets.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Source).Include(x => x.Observations)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SourceId == source.Id &&
                                       x.ImportIdentity == command.ImportIdentity.Trim(), cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
                throw new ExchangeRateOperationException(ExchangeRateReasonCodes.ImportConflict,
                    "The import identity is already bound to different exchange-rate evidence.", true);
            return await MapSetAsync(existing, cancellationToken);
        }

        var corrections = await ResolveManualCorrectionsAsync(command.CompanyId, source.Id, normalized,
            command.CorrectsRateSetId, cancellationToken);
        foreach (var observation in normalized)
        {
            await EnsureCurrencyAsync(command.CompanyId, observation.BaseCurrency, true, now, cancellationToken);
            await EnsureCurrencyAsync(command.CompanyId, observation.QuoteCurrency, true, now, cancellationToken);
        }
        await EnsureConfiguredBaseCurrencyAsync(command.CompanyId, now, cancellationToken);
        var nextVersion = (await _db.ExchangeRateSets.IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId && x.SourceId == source.Id)
            .MaxAsync(x => (long?)x.SetVersion, cancellationToken) ?? 0) + 1;
        var setId = Guid.NewGuid();
        var set = new ExchangeRateSet(setId, command.CompanyId, source.Id, nextVersion,
            command.ImportIdentity, contentHash, normalized.Min(x => x.EffectiveDate),
            normalized.Max(x => x.EffectiveDate), command.PublishedUtc, command.ActorUserId,
            command.CorrectsRateSetId, true, now);
        foreach (var item in normalized)
        {
            var correction = corrections.GetValueOrDefault(Key(item));
            set.Observations.Add(new ExchangeRateObservation(Guid.NewGuid(), command.CompanyId, set.Id,
                item.BaseCurrency, item.QuoteCurrency, item.Rate, item.RatePrecision,
                item.QuotationConvention, item.EffectiveDate, command.PublishedUtc, correction));
        }
        set.Evidence.Add(CreateEvidence(command.CompanyId, set.Id, canonical, "application/json", now));
        _db.ExchangeRateSets.Add(set);
        await AuditAsync(command.CompanyId, command.ActorUserId, "exchange_rate.manual_set_imported",
            "exchange_rate_set", set.Id, "A manual exchange-rate set was imported for independent review.",
            command.CorrelationId, new Dictionary<string, string?>
            {
                ["sourceKey"] = sourceKey, ["status"] = set.Status, ["contentHash"] = contentHash,
                ["observationCount"] = normalized.Length.ToString(CultureInfo.InvariantCulture)
            }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Import(sourceKey, set.Status, normalized.Length, null);
        return await MapSetAsync(set, cancellationToken);
    }

    public async Task<ExchangeRateSetResult> ReviewSetAsync(
        ReviewExchangeRateSetCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var set = await _db.ExchangeRateSets.IgnoreQueryFilters().Include(x => x.Source)
            .Include(x => x.Observations).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.RateSetId,
                cancellationToken) ?? throw new KeyNotFoundException("The exchange-rate set was not found.");
        if (set.Source.SourceKind == ExchangeRateSourceKinds.Manual && set.ImportedByUserId == command.ActorUserId)
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.PendingApproval,
                "A different accounting administrator must review a manually imported rate set.");
        try
        {
            if (command.Approve) set.Approve(command.ActorUserId, command.ReviewNote, command.ExpectedVersion, Now());
            else set.Reject(command.ActorUserId, command.ReviewNote, command.ExpectedVersion);
        }
        catch (InvalidOperationException exception)
        {
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.ConcurrencyConflict,
                exception.Message, true);
        }
        await AuditAsync(command.CompanyId, command.ActorUserId,
            command.Approve ? "exchange_rate.set_approved" : "exchange_rate.set_rejected",
            "exchange_rate_set", set.Id,
            command.Approve ? "The exchange-rate set was approved for deterministic selection." : "The exchange-rate set was rejected.",
            command.CorrelationId, new Dictionary<string, string?> { ["sourceKey"] = set.Source.SourceKey, ["status"] = set.Status }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Import(set.Source.SourceKey, set.Status, set.Observations.Count, null);
        return await MapSetAsync(set, cancellationToken);
    }

    public async Task<ExchangeRateRefreshJobResult> QueueRefreshAsync(
        QueueExchangeRateRefreshCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var provider = _providers.GetRequired(command.ProviderKey);
        var now = Now();
        var source = await _db.ExchangeRateSources.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SourceKey == provider.Descriptor.ProviderKey,
                cancellationToken);
        if (source is null)
        {
            source = CreateProviderSource(command.CompanyId, provider.Descriptor,
                provider.Descriptor.DefaultPriority, provider.Descriptor.RequiresApproval,
                provider.Descriptor.DefaultMaxStalenessDays, provider.Descriptor.DefaultRefreshIntervalHours, true, now);
            _db.ExchangeRateSources.Add(source);
        }
        if (!source.IsEnabled)
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.ProviderUnavailable,
                "Enable the exchange-rate source before requesting a refresh.");
        var currencies = (command.Currencies.Count == 0 ? provider.Descriptor.DefaultCurrencies : command.Currencies)
            .Select(Currency).Where(x => x != provider.Descriptor.BaseCurrency)
            .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (currencies.Length == 0) throw new ExchangeRateOperationException(ExchangeRateReasonCodes.UnsupportedCurrency,
            "Select at least one non-base currency for provider refresh.");
        await EnsureCurrencyAsync(command.CompanyId, provider.Descriptor.BaseCurrency, true, now, cancellationToken);
        foreach (var currency in currencies) await EnsureCurrencyAsync(command.CompanyId, currency, true, now, cancellationToken);
        await EnsureConfiguredBaseCurrencyAsync(command.CompanyId, now, cancellationToken);

        var key = Required(command.IdempotencyKey, 200);
        var csv = string.Join(',', currencies);
        var existing = await _db.ExchangeRateRefreshJobs.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            if (existing.SourceId != source.Id || existing.RequestedDate != command.RequestedDate || existing.RequestedCurrencies != csv)
                throw new ExchangeRateOperationException(ExchangeRateReasonCodes.ImportConflict,
                    "The refresh idempotency key is already bound to a different request.", true);
            return MapJob(existing);
        }
        var job = new ExchangeRateRefreshJob(Guid.NewGuid(), command.CompanyId, source.Id, key,
            command.RequestedDate, csv, command.ActorUserId, command.CorrelationId, now);
        _db.ExchangeRateRefreshJobs.Add(job);
        await AuditAsync(command.CompanyId, command.ActorUserId, "exchange_rate.refresh_queued",
            "exchange_rate_refresh_job", job.Id, "A durable exchange-rate provider refresh was queued.", command.CorrelationId,
            new Dictionary<string, string?> { ["sourceKey"] = source.SourceKey, ["requestedDate"] = command.RequestedDate.ToString("yyyy-MM-dd") },
            cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Refresh(source.SourceKey, ExchangeRateRefreshJobStatuses.Queued, ExchangeRateReasonCodes.RefreshQueued);
        return MapJob(job);
    }

    public async Task<ExchangeRateConversionResult> ConvertAsync(
        ConvertCurrencyCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        var from = Currency(command.FromCurrency);
        var to = Currency(command.ToCurrency);
        var purpose = Purpose(command.Purpose);
        var key = Required(command.IdempotencyKey, 200);
        var inputAmount = AccountingDecimal(command.Amount);
        var requestHash = Hash(string.Join('|', command.CompanyId.ToString("N"),
            inputAmount.ToString("G29", CultureInfo.InvariantCulture), from, to,
            command.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), purpose));
        var existing = await _db.ExchangeRateConversions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == key, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase))
                throw new ExchangeRateOperationException(ExchangeRateReasonCodes.ImportConflict,
                    "The conversion idempotency key is already bound to different inputs.", true);
            return await MapConversionAsync(existing, cancellationToken);
        }

        var lookup = await LookupCoreAsync(new ExchangeRateLookupQuery(command.CompanyId, from, to, command.Date, purpose), cancellationToken);
        if (!lookup.IsReady || !lookup.EffectiveRate.HasValue)
            throw new ExchangeRateOperationException(lookup.ReasonCode, lookup.Explanation,
                lookup.ReasonCode == ExchangeRateReasonCodes.AmbiguousRate);
        var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        var precision = await ResolveCurrencyPrecisionAsync(command.CompanyId, to, configuration.BaseCurrency,
            configuration.RoundingPrecision, cancellationToken);
        var midpoint = configuration.RoundingMode == AccountingRoundingModeValues.AwayFromZero
            ? MidpointRounding.AwayFromZero : MidpointRounding.ToEven;
        decimal unrounded;
        try { unrounded = AccountingDecimal(inputAmount * lookup.EffectiveRate.Value); }
        catch (OverflowException)
        {
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.InvalidRate,
                "The conversion result exceeds the supported accounting precision.");
        }
        var rounded = Math.Round(unrounded, precision, midpoint);
        var conversion = new ExchangeRateConversion(Guid.NewGuid(), command.CompanyId, key, requestHash,
            purpose, command.Date, inputAmount, from, to, lookup.EffectiveRate.Value,
            unrounded, rounded, unrounded - rounded, precision, configuration.RoundingMode, Now());
        for (var index = 0; index < lookup.Legs.Count; index++)
        {
            var leg = lookup.Legs[index];
            conversion.Legs.Add(new ExchangeRateConversionLeg(Guid.NewGuid(), command.CompanyId,
                conversion.Id, index + 1, leg.ObservationId, leg.FromCurrency, leg.ToCurrency, leg.Factor));
        }
        _db.ExchangeRateConversions.Add(conversion);
        await AuditAsync(command.CompanyId, command.ActorUserId, "exchange_rate.conversion_recorded",
            "exchange_rate_conversion", conversion.Id, "A deterministic currency conversion was retained.",
            command.CorrelationId, new Dictionary<string, string?>
            {
                ["fromCurrency"] = from, ["toCurrency"] = to, ["requestedDate"] = command.Date.ToString("yyyy-MM-dd"),
                ["purpose"] = purpose, ["legCount"] = lookup.Legs.Count.ToString(CultureInfo.InvariantCulture)
            }, cancellationToken);
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            existing = await _db.ExchangeRateConversions.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == key, cancellationToken);
            if (existing is null || !string.Equals(existing.RequestHash, requestHash, StringComparison.OrdinalIgnoreCase)) throw;
            return await MapConversionAsync(existing, cancellationToken);
        }
        return new ExchangeRateConversionResult(conversion.Id, key, purpose, command.Date, inputAmount,
            from, to, lookup.EffectiveRate.Value, unrounded, rounded, unrounded - rounded, precision,
            configuration.RoundingMode, conversion.CreatedUtc, lookup.Legs);
    }

    internal async Task<ExchangeRateSet> ImportProviderResponseAsync(ExchangeRateRefreshJob job,
        ExchangeRateSource source, ExchangeRateProviderResponse response, CancellationToken cancellationToken)
    {
        if (response.Observations.Count is < 1 or > 5000)
            throw new ExchangeRateProviderException(ExchangeRateReasonCodes.ProviderPayloadInvalid,
                "The provider response contained an unsupported number of observations.", false);
        var contentHash = Hash(response.RawEvidence);
        var existing = await _db.ExchangeRateSets.IgnoreQueryFilters()
            .Include(x => x.Observations).SingleOrDefaultAsync(x => x.CompanyId == job.CompanyId &&
                x.SourceId == source.Id && x.ImportIdentity == response.ImportIdentity, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
                throw new ExchangeRateProviderException(ExchangeRateReasonCodes.ImportConflict,
                    "The provider reused an import identity for different evidence.", false);
            return existing;
        }

        var observations = response.Observations.Select(NormalizeProvider).OrderBy(x => x.EffectiveDate)
            .ThenBy(x => x.BaseCurrency).ThenBy(x => x.QuoteCurrency).ToArray();
        if (observations.GroupBy(x => new { x.BaseCurrency, x.QuoteCurrency, x.EffectiveDate }).Any(x => x.Count() > 1))
            throw new ExchangeRateProviderException(ExchangeRateReasonCodes.ProviderPayloadInvalid,
                "The provider returned ambiguous rates for the same pair and date.", false);
        var prior = await LoadLatestApprovedByPairAsync(job.CompanyId, source.Id, observations, cancellationToken);
        var changedPrior = prior.Where(x => observations.Any(item => Key(item) == x.Key &&
            (item.Rate != x.Value.Rate || item.QuotationConvention != x.Value.QuotationConvention))).ToArray();
        Guid? correctsSetId = changedPrior.Select(x => x.Value.RateSetId).Distinct().Count() == 1
            ? changedPrior.Select(x => x.Value.RateSetId).SingleOrDefault() : null;
        foreach (var observation in observations)
        {
            await EnsureCurrencyAsync(job.CompanyId, observation.BaseCurrency, true, Now(), cancellationToken);
            await EnsureCurrencyAsync(job.CompanyId, observation.QuoteCurrency, true, Now(), cancellationToken);
        }
        await EnsureConfiguredBaseCurrencyAsync(job.CompanyId, Now(), cancellationToken);
        var nextVersion = (await _db.ExchangeRateSets.IgnoreQueryFilters()
            .Where(x => x.CompanyId == job.CompanyId && x.SourceId == source.Id)
            .MaxAsync(x => (long?)x.SetVersion, cancellationToken) ?? 0) + 1;
        var set = new ExchangeRateSet(Guid.NewGuid(), job.CompanyId, source.Id, nextVersion,
            response.ImportIdentity, contentHash, observations.Min(x => x.EffectiveDate),
            observations.Max(x => x.EffectiveDate), response.PublishedUtc, job.RequestedByUserId,
            correctsSetId, source.RequiresApproval, Now());
        foreach (var item in observations)
        {
            prior.TryGetValue(Key(item), out var previous);
            Guid? correctionId = previous is not null &&
                (previous.Rate != item.Rate || previous.QuotationConvention != item.QuotationConvention)
                ? previous.Id : null;
            set.Observations.Add(new ExchangeRateObservation(Guid.NewGuid(), job.CompanyId, set.Id,
                item.BaseCurrency, item.QuoteCurrency, item.Rate, item.RatePrecision,
                item.QuotationConvention, item.EffectiveDate, item.ObservedUtc, correctionId));
        }
        set.Evidence.Add(CreateEvidence(job.CompanyId, set.Id, response.RawEvidence, response.ContentType, Now()));
        _db.ExchangeRateSets.Add(set);
        await AuditAsync(job.CompanyId, job.RequestedByUserId, "exchange_rate.provider_set_imported",
            "exchange_rate_set", set.Id, "A provider exchange-rate set was retained with protected source evidence.",
            job.CorrelationId, new Dictionary<string, string?>
            {
                ["sourceKey"] = source.SourceKey, ["status"] = set.Status,
                ["contentHash"] = contentHash, ["observationCount"] = observations.Length.ToString(CultureInfo.InvariantCulture)
            }, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Import(source.SourceKey, set.Status, observations.Length, null);
        return set;
    }

    private async Task<ExchangeRateLookupResult> LookupCoreAsync(
        ExchangeRateLookupQuery query, CancellationToken cancellationToken)
    {
        Require(query.CompanyId, nameof(query.CompanyId));
        var from = Currency(query.FromCurrency);
        var to = Currency(query.ToCurrency);
        var purpose = Purpose(query.Purpose);
        var configuration = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == query.CompanyId, cancellationToken);
        if (configuration is null)
            return Block(from, to, query.Date, purpose, ExchangeRateReasonCodes.MissingAccountingConfiguration,
                "Complete accounting configuration before selecting an exchange rate.");
        var enabledCurrencies = (await _db.CompanyCurrencyDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.IsEnabled)
            .Select(x => x.Code).Take(200).ToArrayAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        enabledCurrencies.Add(configuration.BaseCurrency);
        if (!enabledCurrencies.Contains(from) || !enabledCurrencies.Contains(to))
            return Block(from, to, query.Date, purpose, ExchangeRateReasonCodes.UnsupportedCurrency,
                "Both currencies must be explicitly enabled for this company.");
        if (from == to)
            return new ExchangeRateLookupResult(ExchangeRateDecisionStatuses.Ready,
                ExchangeRateReasonCodes.IdentityConversion, "No exchange is required because both currencies are identical.",
                from, to, query.Date, purpose, 1m, query.Date, []);

        var max = Math.Clamp(_options.MaximumLookupCandidates, 100, 10000);
        var candidates = await _db.ExchangeRateObservations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.RateSet).ThenInclude(x => x.Source)
            .Include(x => x.RateSet).ThenInclude(x => x.Evidence)
            .Where(x => x.CompanyId == query.CompanyId && x.EffectiveDate <= query.Date &&
                (x.BaseCurrency == from || x.QuoteCurrency == from || x.BaseCurrency == to || x.QuoteCurrency == to))
            .OrderByDescending(x => x.EffectiveDate).ThenByDescending(x => x.RateSet.SetVersion)
            .Take(max + 1).ToArrayAsync(cancellationToken);
        if (candidates.Length > max)
            return Block(from, to, query.Date, purpose, ExchangeRateReasonCodes.AmbiguousRate,
                "The bounded rate lookup returned too many candidates. Narrow the enabled catalogue before posting.");

        var direct = SelectLeg(candidates, from, to, query.Date);
        if (direct.Status == ExchangeRateDecisionStatuses.Ready)
            return Ready(from, to, query.Date, purpose, [direct.Leg!], "A direct approved observation was selected by source priority and effective date.");
        if (direct.ReasonCode != ExchangeRateReasonCodes.MissingRate)
            return Block(from, to, query.Date, purpose, direct.ReasonCode, direct.Explanation);

        var pivots = candidates.SelectMany(x => new[] { x.BaseCurrency, x.QuoteCurrency })
            .Where(x => x != from && x != to).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x == configuration.BaseCurrency ? 0 : x == "SEK" ? 1 : 2)
            .ThenBy(x => x, StringComparer.Ordinal).ToArray();
        var failures = new List<LegSelection>();
        foreach (var pivot in pivots)
        {
            var first = SelectLeg(candidates, from, pivot, query.Date);
            var second = SelectLeg(candidates, pivot, to, query.Date);
            if (first.Status == ExchangeRateDecisionStatuses.Ready && second.Status == ExchangeRateDecisionStatuses.Ready)
                return Ready(from, to, query.Date, purpose, [first.Leg!, second.Leg!],
                    $"A two-leg approved cross rate was selected through {pivot}.");
            if (first.ReasonCode != ExchangeRateReasonCodes.MissingRate) failures.Add(first);
            if (second.ReasonCode != ExchangeRateReasonCodes.MissingRate) failures.Add(second);
        }
        var failure = failures.OrderBy(x => FailureRank(x.ReasonCode)).FirstOrDefault();
        return failure is null
            ? Block(from, to, query.Date, purpose, ExchangeRateReasonCodes.MissingRate,
                "No approved direct or one-pivot cross rate exists on or before the requested date.")
            : Block(from, to, query.Date, purpose, failure.ReasonCode, failure.Explanation);
    }

    private static LegSelection SelectLeg(IReadOnlyCollection<ExchangeRateObservation> candidates,
        string from, string to, DateOnly date)
    {
        var connected = candidates.Where(x => Connects(x, from, to)).ToArray();
        var approved = connected.Where(x => x.RateSet.Status == ExchangeRateSetStatuses.Approved && x.RateSet.Source.IsEnabled).ToArray();
        if (approved.Length == 0)
        {
            if (connected.Any(x => x.RateSet.Status == ExchangeRateSetStatuses.PendingReview && x.RateSet.Source.IsEnabled))
                return new(ExchangeRateDecisionStatuses.ReviewRequired, ExchangeRateReasonCodes.PendingApproval,
                    "A matching observation exists but requires approval before accounting can use it.", null);
            return new(ExchangeRateDecisionStatuses.Blocked, ExchangeRateReasonCodes.MissingRate,
                $"No approved {from}/{to} observation exists on or before {date:yyyy-MM-dd}.", null);
        }
        var priority = approved.Min(x => x.RateSet.Source.Priority);
        var preferred = approved.Where(x => x.RateSet.Source.Priority == priority).ToArray();
        var effectiveDate = preferred.Max(x => x.EffectiveDate);
        preferred = preferred.Where(x => x.EffectiveDate == effectiveDate)
            .GroupBy(x => x.RateSet.SourceId)
            .Select(group => group.OrderByDescending(x => x.RateSet.SetVersion).ThenByDescending(x => x.ObservedUtc).First())
            .ToArray();
        var age = date.DayNumber - effectiveDate.DayNumber;
        var fresh = preferred.Where(x => age <= x.RateSet.Source.MaxStalenessDays).ToArray();
        if (fresh.Length == 0)
        {
            var selected = preferred.OrderBy(x => x.RateSet.Source.SourceKey, StringComparer.Ordinal).First();
            return new(ExchangeRateDecisionStatuses.Blocked, ExchangeRateReasonCodes.StaleRate,
                $"The highest-precedence {from}/{to} observation is {age} day(s) old; source policy allows {selected.RateSet.Source.MaxStalenessDays}.", null);
        }
        var factors = fresh.Select(x => (Observation: x, Factor: Factor(x, from, to))).ToArray();
        if (factors.Select(x => Decimal.Round(x.Factor, 12)).Distinct().Count() > 1)
            return new(ExchangeRateDecisionStatuses.ReviewRequired, ExchangeRateReasonCodes.AmbiguousRate,
                $"Multiple equally ranked sources disagree for {from}/{to} on {effectiveDate:yyyy-MM-dd}.", null);
        var winner = factors.OrderBy(x => x.Observation.RateSet.Source.SourceKey, StringComparer.Ordinal)
            .ThenByDescending(x => x.Observation.RateSet.SetVersion).First();
        var checksum = winner.Observation.RateSet.Evidence.OrderBy(x => x.CreatedUtc).Select(x => x.Checksum).FirstOrDefault()
            ?? winner.Observation.RateSet.ContentHash;
        return new(ExchangeRateDecisionStatuses.Ready, ExchangeRateReasonCodes.None, "Approved observation selected.",
            new ExchangeRateLookupLeg(winner.Observation.Id, winner.Observation.RateSet.Source.SourceKey,
                winner.Observation.RateSet.SetVersion, from, to, winner.Observation.Rate, winner.Factor,
                winner.Observation.RatePrecision, winner.Observation.EffectiveDate, age,
                winner.Observation.QuotationConvention, checksum));
    }

    private static ExchangeRateLookupResult Ready(string from, string to, DateOnly date, string purpose,
        IReadOnlyList<ExchangeRateLookupLeg> legs, string explanation)
    {
        decimal factor = 1m;
        try { foreach (var leg in legs) factor *= leg.Factor; }
        catch (OverflowException)
        {
            return Block(from, to, date, purpose, ExchangeRateReasonCodes.InvalidRate,
                "The selected rate path exceeds supported accounting precision.");
        }
        return new ExchangeRateLookupResult(ExchangeRateDecisionStatuses.Ready, ExchangeRateReasonCodes.None,
            explanation, from, to, date, purpose, AccountingDecimal(factor), legs.Min(x => x.EffectiveDate), legs);
    }

    private static ExchangeRateLookupResult Block(string from, string to, DateOnly date, string purpose,
        string reasonCode, string explanation) => new(
        reasonCode is ExchangeRateReasonCodes.PendingApproval or ExchangeRateReasonCodes.AmbiguousRate
            ? ExchangeRateDecisionStatuses.ReviewRequired : ExchangeRateDecisionStatuses.Blocked,
        reasonCode, explanation, from, to, date, purpose, null, null, []);

    private async Task<Dictionary<string, Guid?>> ResolveManualCorrectionsAsync(Guid companyId, Guid sourceId,
        IReadOnlyCollection<NormalizedObservation> observations, Guid? correctsRateSetId, CancellationToken cancellationToken)
    {
        if (correctsRateSetId == Guid.Empty) throw new ExchangeRateOperationException(ExchangeRateReasonCodes.CorrectionMismatch,
            "A correction set identifier cannot be empty.");
        if (correctsRateSetId.HasValue && !await _db.ExchangeRateSets.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.SourceId == sourceId && x.Id == correctsRateSetId &&
                           x.Status == ExchangeRateSetStatuses.Approved, cancellationToken))
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.CorrectionMismatch,
                "The corrected rate set is not an approved set from this company and source.");
        var prior = await LoadLatestApprovedByPairAsync(companyId, sourceId, observations, cancellationToken);
        var result = new Dictionary<string, Guid?>(StringComparer.Ordinal);
        foreach (var item in observations)
        {
            var key = Key(item);
            prior.TryGetValue(key, out var previous);
            if (item.CorrectsObservationId.HasValue)
            {
                if (previous is null || previous.Id != item.CorrectsObservationId ||
                    (correctsRateSetId.HasValue && previous.RateSetId != correctsRateSetId))
                    throw new ExchangeRateOperationException(ExchangeRateReasonCodes.CorrectionMismatch,
                        "A corrected observation must reference the latest approved observation for the same company, source, pair, and date.");
                result[key] = previous.Id;
            }
            else if (previous is not null)
                throw new ExchangeRateOperationException(ExchangeRateReasonCodes.CorrectionRequired,
                    "An approved observation already exists for this source, pair, and date. Import an explicit linked correction.");
            else result[key] = null;
        }
        return result;
    }

    private async Task<Dictionary<string, ExchangeRateObservation>> LoadLatestApprovedByPairAsync(
        Guid companyId, Guid sourceId, IReadOnlyCollection<NormalizedObservation> observations,
        CancellationToken cancellationToken)
    {
        var dates = observations.Select(x => x.EffectiveDate).Distinct().ToArray();
        var currencies = observations.SelectMany(x => new[] { x.BaseCurrency, x.QuoteCurrency }).Distinct().ToArray();
        var rows = await _db.ExchangeRateObservations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.RateSet)
            .Where(x => x.CompanyId == companyId && x.RateSet.SourceId == sourceId &&
                x.RateSet.Status == ExchangeRateSetStatuses.Approved && dates.Contains(x.EffectiveDate) &&
                currencies.Contains(x.BaseCurrency) && currencies.Contains(x.QuoteCurrency))
            .ToArrayAsync(cancellationToken);
        return rows.Where(x => observations.Any(item => Key(item) == Key(x)))
            .GroupBy(Key).ToDictionary(x => x.Key,
                x => x.OrderByDescending(item => item.RateSet.SetVersion).ThenByDescending(item => item.ObservedUtc).First(),
                StringComparer.Ordinal);
    }

    private async Task EnsureConfiguredBaseCurrencyAsync(Guid companyId, DateTime now,
        CancellationToken cancellationToken)
    {
        var baseCurrency = await _db.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId).Select(x => x.BaseCurrency).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ExchangeRateOperationException(ExchangeRateReasonCodes.MissingAccountingConfiguration,
                "Complete accounting configuration before importing exchange rates.");
        await EnsureCurrencyAsync(companyId, baseCurrency, true, now, cancellationToken);
    }

    private async Task EnsureCurrencyAsync(Guid companyId, string code, bool enabled, DateTime now,
        CancellationToken cancellationToken)
    {
        code = Currency(code);
        var tracked = _db.ChangeTracker.Entries<CompanyCurrencyDefinition>()
            .Select(x => x.Entity).SingleOrDefault(x => x.CompanyId == companyId && x.Code == code);
        if (tracked is not null)
        {
            if (enabled && !tracked.IsEnabled)
                throw new ExchangeRateOperationException(ExchangeRateReasonCodes.UnsupportedCurrency,
                    $"Currency {code} is disabled and cannot be enabled implicitly by an import.");
            return;
        }
        var existing = await _db.CompanyCurrencyDefinitions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Code == code, cancellationToken);
        if (existing is null)
            _db.CompanyCurrencyDefinitions.Add(new CompanyCurrencyDefinition(Guid.NewGuid(), companyId, code,
                code, MinorUnits(code), enabled, now));
        else if (enabled && !existing.IsEnabled)
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.UnsupportedCurrency,
                $"Currency {code} is disabled and cannot be enabled implicitly by an import.");
    }

    private ExchangeRateEvidence CreateEvidence(Guid companyId, Guid setId, string raw, string contentType, DateTime now)
    {
        var checksum = Hash(raw);
        var protectedPayload = _encryption.Encrypt(companyId, $"{EvidencePurposePrefix}:{setId:D}:v1", raw);
        return new ExchangeRateEvidence(Guid.NewGuid(), companyId, setId, checksum, protectedPayload,
            contentType, now.AddDays(Math.Clamp(_options.RawEvidenceRetentionDays, 365, 3650)), now);
    }

    private async Task<ExchangeRateConversionResult> MapConversionAsync(ExchangeRateConversion conversion,
        CancellationToken cancellationToken)
    {
        var legs = await _db.ExchangeRateConversionLegs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == conversion.CompanyId && x.ConversionId == conversion.Id)
            .OrderBy(x => x.Sequence).ToArrayAsync(cancellationToken);
        var ids = legs.Select(x => x.ObservationId).ToArray();
        var observations = await _db.ExchangeRateObservations.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.RateSet).ThenInclude(x => x.Source)
            .Include(x => x.RateSet).ThenInclude(x => x.Evidence)
            .Where(x => x.CompanyId == conversion.CompanyId && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        var lookupLegs = legs.Select(x =>
        {
            var observation = observations[x.ObservationId];
            return new ExchangeRateLookupLeg(observation.Id, observation.RateSet.Source.SourceKey,
                observation.RateSet.SetVersion, x.FromCurrency, x.ToCurrency, observation.Rate, x.Factor,
                observation.RatePrecision, observation.EffectiveDate,
                conversion.RequestedDate.DayNumber - observation.EffectiveDate.DayNumber,
                observation.QuotationConvention,
                observation.RateSet.Evidence.OrderBy(e => e.CreatedUtc).Select(e => e.Checksum).FirstOrDefault()
                    ?? observation.RateSet.ContentHash);
        }).ToArray();
        return new ExchangeRateConversionResult(conversion.Id, conversion.IdempotencyKey, conversion.Purpose,
            conversion.RequestedDate, conversion.InputAmount, conversion.InputCurrency, conversion.OutputCurrency,
            conversion.EffectiveRate, conversion.UnroundedAmount, conversion.RoundedAmount, conversion.RoundingResidual,
            conversion.OutputPrecision, conversion.RoundingMode, conversion.CreatedUtc, lookupLegs);
    }

    private async Task<ExchangeRateSetResult> MapSetAsync(ExchangeRateSet set, CancellationToken cancellationToken)
    {
        var sourceKey = set.Source?.SourceKey ?? await _db.ExchangeRateSources.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == set.CompanyId && x.Id == set.SourceId).Select(x => x.SourceKey)
            .SingleAsync(cancellationToken);
        var count = set.Observations.Count > 0 ? set.Observations.Count :
            await _db.ExchangeRateObservations.IgnoreQueryFilters().CountAsync(x => x.CompanyId == set.CompanyId && x.RateSetId == set.Id, cancellationToken);
        return new ExchangeRateSetResult(set.Id, set.SourceId, sourceKey, set.SetVersion, set.ImportIdentity,
            set.ContentHash, set.Status, set.EffectiveFrom, set.EffectiveThrough, set.PublishedUtc,
            set.ImportedByUserId, set.ApprovedByUserId, set.CorrectsRateSetId, set.ApprovedUtc,
            set.ReviewNote, set.Version, count);
    }

    private static ExchangeRateObservationResult MapObservation(ExchangeRateObservation observation) => new(
        observation.Id, observation.RateSetId, observation.RateSet.Source.SourceKey,
        observation.RateSet.SetVersion, observation.BaseCurrency, observation.QuoteCurrency,
        observation.Rate, observation.RatePrecision, observation.QuotationConvention,
        observation.EffectiveDate, observation.ObservedUtc, observation.CorrectsObservationId,
        observation.RateSet.Status,
        observation.RateSet.Evidence.OrderBy(x => x.CreatedUtc).Select(x => x.Checksum).FirstOrDefault()
            ?? observation.RateSet.ContentHash);

    private async Task<int> ResolveCurrencyPrecisionAsync(Guid companyId, string code, string baseCurrency,
        int basePrecision, CancellationToken cancellationToken) => string.Equals(code, baseCurrency, StringComparison.OrdinalIgnoreCase)
        ? basePrecision
        : await _db.CompanyCurrencyDefinitions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Code == code && x.IsEnabled)
            .Select(x => (int?)x.MinorUnitPrecision).SingleOrDefaultAsync(cancellationToken)
            ?? throw new ExchangeRateOperationException(ExchangeRateReasonCodes.UnsupportedCurrency,
                $"Currency {code} is not enabled.");

    private async Task EnsureActorAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId)); Require(actorUserId, nameof(actorUserId));
        if (!await _db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.UserId == actorUserId && x.Status == CompanyMembershipStatus.Active,
                cancellationToken))
            throw new UnauthorizedAccessException("An active company member is required for exchange-rate operations.");
    }

    private Task AuditAsync(Guid companyId, Guid? actorUserId, string action, string targetType,
        Guid targetId, string summary, string? correlationId, IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken) => _audit.WriteAsync(new AuditEventWriteRequest(companyId,
            actorUserId.HasValue ? AuditActorTypes.User : AuditActorTypes.System, actorUserId, action,
            targetType, targetId.ToString("D"), AuditEventOutcomes.Succeeded, summary,
            ["exchange-rate-catalogue"], metadata, correlationId, Now()), cancellationToken);

    private static ExchangeRateSource CreateProviderSource(Guid companyId, ExchangeRateProviderDescriptor descriptor,
        int priority, bool requiresApproval, int maxStalenessDays, int refreshIntervalHours, bool enabled, DateTime now) =>
        new(Guid.NewGuid(), companyId, descriptor.ProviderKey, descriptor.DisplayName,
            ExchangeRateSourceKinds.Provider, descriptor.AdapterVersion, priority, requiresApproval,
            maxStalenessDays, refreshIntervalHours, descriptor.LicenseSummary, enabled, now);

    private static CurrencyDefinitionResult MapCurrency(CompanyCurrencyDefinition value) =>
        new(value.Id, value.Code, value.Name, value.MinorUnitPrecision, value.IsEnabled, value.Version);

    private static ExchangeRateSourceResult MapSource(ExchangeRateSource value) =>
        new(value.Id, value.SourceKey, value.DisplayName, value.SourceKind, value.SourceVersion,
            value.Priority, value.RequiresApproval, value.MaxStalenessDays, value.RefreshIntervalHours,
            value.LicenseSummary, value.IsEnabled, value.LastSuccessfulRefreshUtc, value.NextRefreshUtc,
            value.LastFailureReasonCode, value.LastFailureSummary, value.Version);

    private static ExchangeRateRefreshJobResult MapJob(ExchangeRateRefreshJob value) =>
        new(value.Id, value.SourceId, value.Status, value.RequestedDate,
            value.RequestedCurrencies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            value.AttemptCount, value.NextAttemptUtc, value.FailureReasonCode, value.FailureSummary,
            value.RateSetId, value.Version);

    private static NormalizedObservation NormalizeManual(ManualExchangeRateObservationInput item)
    {
        ValidateRate(item.Rate, item.RatePrecision, item.QuotationConvention);
        return new(Currency(item.BaseCurrency), Currency(item.QuoteCurrency), item.Rate,
            item.RatePrecision, item.QuotationConvention, item.EffectiveDate,
            DateTime.UnixEpoch, item.CorrectsObservationId);
    }

    private static NormalizedObservation NormalizeProvider(ExchangeRateProviderObservation item)
    {
        ValidateRate(item.Rate, item.RatePrecision, item.QuotationConvention);
        return new(Currency(item.BaseCurrency), Currency(item.QuoteCurrency), item.Rate,
            item.RatePrecision, item.QuotationConvention, item.EffectiveDate,
            Utc(item.ObservedUtc), null);
    }

    private static void ValidateRate(decimal rate, int precision, string convention)
    {
        if (rate <= 0m || precision is < 0 or > 18 || convention is not
            (ExchangeRateQuotationConventions.BaseCurrencyPerQuoteCurrency or ExchangeRateQuotationConventions.QuoteCurrencyPerBaseCurrency))
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.InvalidRate,
                "An exchange-rate observation contains an invalid positive rate, precision, or quotation convention.");
    }

    private static bool Connects(ExchangeRateObservation value, string from, string to) =>
        value.BaseCurrency == from && value.QuoteCurrency == to || value.BaseCurrency == to && value.QuoteCurrency == from;

    private static decimal Factor(ExchangeRateObservation value, string from, string to)
    {
        if (!Connects(value, from, to)) throw new InvalidOperationException("Observation does not connect the requested currencies.");
        var baseToQuote = from == value.BaseCurrency;
        var factor = value.QuotationConvention == ExchangeRateQuotationConventions.BaseCurrencyPerQuoteCurrency
            ? baseToQuote ? 1m / value.Rate : value.Rate
            : baseToQuote ? value.Rate : 1m / value.Rate;
        return AccountingDecimal(factor);
    }

    private static int FailureRank(string reasonCode) => reasonCode switch
    {
        ExchangeRateReasonCodes.AmbiguousRate => 0,
        ExchangeRateReasonCodes.PendingApproval => 1,
        ExchangeRateReasonCodes.StaleRate => 2,
        _ => 3
    };

    private static string Key(NormalizedObservation value) =>
        $"{value.BaseCurrency}|{value.QuoteCurrency}|{value.EffectiveDate:yyyyMMdd}";
    private static string Key(ExchangeRateObservation value) =>
        $"{value.BaseCurrency}|{value.QuoteCurrency}|{value.EffectiveDate:yyyyMMdd}";

    private static int MinorUnits(string code) => code switch
    {
        "BHD" or "JOD" or "KWD" or "OMR" or "TND" => 3,
        "CLP" or "ISK" or "JPY" or "KRW" or "PYG" or "VND" => 0,
        _ => 2
    };

    private static decimal AccountingDecimal(decimal value) =>
        decimal.Round(value, 18, MidpointRounding.ToEven);

    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Currency(string value)
    {
        var normalized = Required(value, 3).ToUpperInvariant();
        if (normalized.Length != 3 || normalized.Any(character => character is < 'A' or > 'Z'))
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.UnsupportedCurrency,
                "Currency codes must contain three alphabetic characters.");
        return normalized;
    }
    private static string Purpose(string value)
    {
        var normalized = Token(value, 32);
        return ExchangeRateLookupPurposes.IsSupported(normalized) ? normalized :
            throw new ExchangeRateOperationException(ExchangeRateReasonCodes.InvalidRate,
                "The exchange-rate lookup purpose is not supported.");
    }
    private static string Token(string value, int max) => Required(value, max).Replace('-', '_').ToLowerInvariant();
    private static string Required(string value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A required value is missing.");
        var normalized = value.Trim();
        return normalized.Length <= max ? normalized : throw new ArgumentOutOfRangeException(nameof(value));
    }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static void Require(Guid value, string name) { if (value == Guid.Empty) throw new ArgumentException($"{name} is required.", name); }
    private static ExchangeRateOperationException Conflict(string message) =>
        new(ExchangeRateReasonCodes.ConcurrencyConflict, message, true);

    private sealed record NormalizedObservation(string BaseCurrency, string QuoteCurrency, decimal Rate,
        int RatePrecision, string QuotationConvention, DateOnly EffectiveDate, DateTime ObservedUtc,
        Guid? CorrectsObservationId);
    private sealed record LegSelection(string Status, string ReasonCode, string Explanation, ExchangeRateLookupLeg? Leg);
}
