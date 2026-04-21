using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyReconciliationScoringService : IReconciliationScoringService
{
    private readonly IReconciliationScoringSettingsProvider _settingsProvider;

    public CompanyReconciliationScoringService(IReconciliationScoringSettingsProvider settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    public async Task<ReconciliationSuggestionsResult> ScoreBankTransactionCandidatesAsync(
        ScoreBankTransactionPaymentCandidatesQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        ValidateRecord(query.BankTransactionId, nameof(query.BankTransactionId));

        var settings = await _settingsProvider.GetSettingsAsync(query.CompanyId, cancellationToken);
        var source = new ReconciliationComparableRecord(
            query.BankTransactionId,
            query.Amount,
            query.Currency,
            query.BookingDate,
            query.ReferenceText,
            query.Counterparty);

        return new ReconciliationSuggestionsResult(
            query.CompanyId,
            ReconciliationSourceTypes.BankTransaction,
            query.BankTransactionId,
            settings,
            ReconciliationScoringEngine.ScoreCandidates(source, MapCandidates(query.Candidates), settings));
    }

    public async Task<ReconciliationSuggestionsResult> ScoreInvoiceCandidatesAsync(
        ScoreInvoicePaymentCandidatesQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        ValidateRecord(query.InvoiceId, nameof(query.InvoiceId));

        var settings = await _settingsProvider.GetSettingsAsync(query.CompanyId, cancellationToken);
        var source = new ReconciliationComparableRecord(
            query.InvoiceId,
            query.Amount,
            query.Currency,
            query.DueUtc,
            query.InvoiceNumber,
            query.CounterpartyName);

        return new ReconciliationSuggestionsResult(
            query.CompanyId,
            ReconciliationSourceTypes.Invoice,
            query.InvoiceId,
            settings,
            ReconciliationScoringEngine.ScoreCandidates(source, MapCandidates(query.Candidates), settings));
    }

    public async Task<ReconciliationSuggestionsResult> ScoreBillCandidatesAsync(
        ScoreBillPaymentCandidatesQuery query,
        CancellationToken cancellationToken)
    {
        ValidateCompany(query.CompanyId);
        ValidateRecord(query.BillId, nameof(query.BillId));

        var settings = await _settingsProvider.GetSettingsAsync(query.CompanyId, cancellationToken);
        var source = new ReconciliationComparableRecord(
            query.BillId,
            query.Amount,
            query.Currency,
            query.DueUtc,
            query.BillNumber,
            query.CounterpartyName);

        return new ReconciliationSuggestionsResult(
            query.CompanyId,
            ReconciliationSourceTypes.Bill,
            query.BillId,
            settings,
            ReconciliationScoringEngine.ScoreCandidates(source, MapCandidates(query.Candidates), settings));
    }

    private static IReadOnlyList<ReconciliationComparableRecord> MapCandidates(
        IReadOnlyList<ReconciliationPaymentCandidate> candidates)
    {
        if (candidates is null)
        {
            return [];
        }

        return candidates
            .Select(candidate =>
            {
                ValidateRecord(candidate.PaymentId, nameof(candidate.PaymentId));
                return new ReconciliationComparableRecord(
                    candidate.PaymentId,
                    candidate.Amount,
                    candidate.Currency,
                    candidate.PaymentDate,
                    candidate.CounterpartyReference,
                    candidate.CounterpartyName);
            })
            .ToList();
    }

    private static void ValidateCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }
    }

    private static void ValidateRecord(Guid recordId, string name)
    {
        if (recordId == Guid.Empty)
        {
            throw new ArgumentException("Record id is required.", name);
        }
    }
}

public sealed class CompanyReconciliationScoringSettingsProvider : IReconciliationScoringSettingsProvider
{
    private const string ExtensionKey = "reconciliationScoring";
    private const string AmountToleranceKey = "nearAmountTolerance";
    private const string DateWindowKey = "dateProximityWindowDays";
    private const string WeightsKey = "scoringWeights";
    private const string AmountExactWeightKey = "amountExactMatch";
    private const string AmountNearWeightKey = "amountNearMatch";
    private const string DateWeightKey = "dateProximity";
    private const string ReferenceWeightKey = "referenceSimilarity";
    private const string CounterpartyWeightKey = "counterpartySimilarity";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;

    public CompanyReconciliationScoringSettingsProvider(VirtualCompanyDbContext dbContext)
        : this(dbContext, null)
    {
    }

    public CompanyReconciliationScoringSettingsProvider(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<ReconciliationScoringSettings> GetSettingsAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        EnsureTenant(companyId);

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);

        if (company is null)
        {
            throw new KeyNotFoundException($"Company '{companyId}' was not found.");
        }

        if (!company.Settings.Extensions.TryGetValue(ExtensionKey, out var node) || node is not JsonObject values)
        {
            return ReconciliationScoringSettings.Default;
        }

        return BuildSettings(values);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (_companyContextAccessor?.CompanyId is Guid scopedCompanyId && scopedCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Reconciliation scoring settings can only be resolved for the active company context.");
        }
    }

    private static ReconciliationScoringSettings BuildSettings(JsonObject values)
    {
        var defaultSettings = ReconciliationScoringSettings.Default;
        var defaultWeights = defaultSettings.Weights;
        var configuredWeights = values[WeightsKey] as JsonObject;
        var weightValues = configuredWeights ?? values;

        var tolerance = ReadDecimal(values, AmountToleranceKey) ?? defaultSettings.NearAmountTolerance;
        var dateWindow = ReadInt(values, DateWindowKey) ?? defaultSettings.DateProximityWindowDays;
        var weights = new ReconciliationScoringWeights(
            ReadDecimal(weightValues, AmountExactWeightKey) ?? defaultWeights.AmountExactMatch,
            ReadDecimal(weightValues, AmountNearWeightKey) ?? defaultWeights.AmountNearMatch,
            ReadDecimal(weightValues, DateWeightKey) ?? defaultWeights.DateProximity,
            ReadDecimal(weightValues, ReferenceWeightKey) ?? defaultWeights.ReferenceSimilarity,
            ReadDecimal(weightValues, CounterpartyWeightKey) ?? defaultWeights.CounterpartySimilarity);

        return new ReconciliationScoringSettings(tolerance, dateWindow, weights);
    }

    private static decimal? ReadDecimal(JsonObject values, string key)
    {
        if (!values.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<decimal>(out var decimalValue))
            {
                return decimalValue;
            }

            if (jsonValue.TryGetValue<double>(out var doubleValue))
            {
                return (decimal)doubleValue;
            }

            if (jsonValue.TryGetValue<string>(out var stringValue) &&
                decimal.TryParse(stringValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return decimal.TryParse(node.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var fallback)
            ? fallback
            : null;
    }

    private static int? ReadInt(JsonObject values, string key)
    {
        if (!values.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                return intValue;
            }

            if (jsonValue.TryGetValue<string>(out var stringValue) &&
                int.TryParse(stringValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                return parsed;
            }
        }

        return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var fallback)
            ? fallback
            : null;
    }
}
