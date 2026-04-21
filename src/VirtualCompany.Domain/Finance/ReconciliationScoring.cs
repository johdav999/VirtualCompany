using System.Globalization;
using System.Text;

namespace VirtualCompany.Domain.Finance;

public static class ReconciliationRuleNames
{
    public const string AmountExact = "amount_exact";
    public const string AmountNear = "amount_near";
    public const string DateProximity = "date_proximity";
    public const string ReferenceSimilarity = "reference_similarity";
    public const string CounterpartySimilarity = "counterparty_similarity";
}

public sealed record ReconciliationScoringWeights
{
    public static ReconciliationScoringWeights Default { get; } = new(0.40m, 0.30m, 0.20m, 0.25m, 0.15m);

    public ReconciliationScoringWeights(
        decimal amountExactMatch,
        decimal amountNearMatch,
        decimal dateProximity,
        decimal referenceSimilarity,
        decimal counterpartySimilarity)
    {
        AmountExactMatch = ClampWeight(amountExactMatch);
        AmountNearMatch = ClampWeight(amountNearMatch);
        DateProximity = ClampWeight(dateProximity);
        ReferenceSimilarity = ClampWeight(referenceSimilarity);
        CounterpartySimilarity = ClampWeight(counterpartySimilarity);
    }

    public decimal AmountExactMatch { get; }
    public decimal AmountNearMatch { get; }
    public decimal DateProximity { get; }
    public decimal ReferenceSimilarity { get; }
    public decimal CounterpartySimilarity { get; }

    public bool HasPositiveWeight =>
        AmountExactMatch > 0m ||
        AmountNearMatch > 0m ||
        DateProximity > 0m ||
        ReferenceSimilarity > 0m ||
        CounterpartySimilarity > 0m;

    private static decimal ClampWeight(decimal value) =>
        decimal.Round(Math.Max(0m, value), 2, MidpointRounding.AwayFromZero);
}

public sealed record ReconciliationScoringSettings
{
    public static ReconciliationScoringSettings Default { get; } = new(5m, 7, ReconciliationScoringWeights.Default);

    public ReconciliationScoringSettings(
        decimal nearAmountTolerance,
        int dateProximityWindowDays,
        ReconciliationScoringWeights? weights = null)
    {
        NearAmountTolerance = decimal.Round(Math.Max(0m, nearAmountTolerance), 2, MidpointRounding.AwayFromZero);
        DateProximityWindowDays = Math.Max(0, dateProximityWindowDays);
        Weights = weights is { HasPositiveWeight: true }
            ? weights
            : ReconciliationScoringWeights.Default;
    }

    public decimal NearAmountTolerance { get; }
    public int DateProximityWindowDays { get; }
    public ReconciliationScoringWeights Weights { get; }
}

public sealed record ReconciliationComparableRecord(
    Guid RecordId,
    decimal Amount,
    string Currency,
    DateTime Date,
    string? Reference,
    string? Counterparty);

public sealed record ReconciliationRuleDetail(
    string RuleName,
    decimal Score,
    decimal Weight,
    bool Matched,
    string Explanation,
    string? SourceValue = null,
    string? CandidateValue = null,
    decimal AwardedScore = 0m);

public sealed record ReconciliationSuggestion(
    Guid CandidatePaymentId,
    decimal CandidateAmount,
    string CandidateCurrency,
    DateTime CandidateDate,
    string? CandidateReference,
    string? CandidateCounterparty,
    decimal ConfidenceScore,
    int Rank,
    IReadOnlyList<ReconciliationRuleDetail> RuleDetails,
    string Explanation);

public static class ReconciliationScoringEngine
{
    public static IReadOnlyList<ReconciliationSuggestion> ScoreCandidates(
        ReconciliationComparableRecord source,
        IEnumerable<ReconciliationComparableRecord> candidates,
        ReconciliationScoringSettings settings)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(settings);

        var scored = candidates
            .Select(candidate => BuildSuggestion(source, candidate, settings))
            .OrderByDescending(x => x.ConfidenceScore)
            .ThenByDescending(x => GetRuleScore(x.RuleDetails, ReconciliationRuleNames.AmountExact))
            .ThenByDescending(x => GetRuleScore(x.RuleDetails, ReconciliationRuleNames.AmountNear))
            .ThenByDescending(x => GetRuleScore(x.RuleDetails, ReconciliationRuleNames.DateProximity))
            .ThenByDescending(x => GetRuleScore(x.RuleDetails, ReconciliationRuleNames.ReferenceSimilarity))
            .ThenByDescending(x => GetRuleScore(x.RuleDetails, ReconciliationRuleNames.CounterpartySimilarity))
            .ThenBy(x => x.CandidatePaymentId)
            .ToList();

        for (var index = 0; index < scored.Count; index++)
        {
            scored[index] = scored[index] with { Rank = index + 1 };
        }

        return scored;
    }

    private static ReconciliationSuggestion BuildSuggestion(
        ReconciliationComparableRecord source,
        ReconciliationComparableRecord candidate,
        ReconciliationScoringSettings settings)
    {
        var amountExactDetail = ScoreAmountExact(source, candidate, settings.Weights.AmountExactMatch);
        var amountNearDetail = ScoreAmountNear(source, candidate, settings);
        var dateDetail = ScoreDate(source, candidate, settings, settings.Weights.DateProximity);
        var referenceDetail = ScoreTextRule(
            ReconciliationRuleNames.ReferenceSimilarity,
            source.Reference,
            candidate.Reference,
            settings.Weights.ReferenceSimilarity,
            "Reference");
        var counterpartyDetail = ScoreTextRule(
            ReconciliationRuleNames.CounterpartySimilarity,
            source.Counterparty,
            candidate.Counterparty,
            settings.Weights.CounterpartySimilarity,
            "Counterparty");

        var rawDetails = new[]
        {
            amountExactDetail,
            amountNearDetail,
            dateDetail,
            referenceDetail,
            counterpartyDetail
        };

        var normalizationWeight = GetNormalizationWeight(rawDetails, settings.Weights);
        var confidenceScore = normalizationWeight == 0m
            ? 0m
            : RoundScore(rawDetails.Sum(x => x.Score * x.Weight) / normalizationWeight);
        var details = rawDetails
            .Select(detail => detail with
            {
                AwardedScore = normalizationWeight == 0m
                    ? 0m
                    : RoundScore((detail.Score * detail.Weight) / normalizationWeight)
            })
            .ToArray();
        var explanation = string.Join(
            "; ",
            details
                .Where(x => x.Matched)
                .Select(x => x.Explanation));

        return new ReconciliationSuggestion(
            candidate.RecordId,
            decimal.Round(candidate.Amount, 2, MidpointRounding.AwayFromZero),
            NormalizeCurrency(candidate.Currency),
            NormalizeUtc(candidate.Date),
            candidate.Reference?.Trim(),
            candidate.Counterparty?.Trim(),
            confidenceScore,
            0,
            details,
            string.IsNullOrWhiteSpace(explanation) ? "No reconciliation rules matched." : explanation);
    }

    private static ReconciliationRuleDetail ScoreAmountExact(
        ReconciliationComparableRecord source,
        ReconciliationComparableRecord candidate,
        decimal weight)
    {
        var comparison = CreateAmountComparison(source, candidate);
        if (!comparison.CurrencyMatches)
        {
            return new ReconciliationRuleDetail(
                ReconciliationRuleNames.AmountExact,
                0m,
                weight,
                false,
                $"Currency mismatch ({comparison.SourceCurrency} vs {comparison.CandidateCurrency}).",
                comparison.SourceDisplay,
                comparison.CandidateDisplay);
        }

        if (comparison.Difference == 0m)
        {
            return new ReconciliationRuleDetail(
                ReconciliationRuleNames.AmountExact,
                1m,
                weight,
                true,
                "Amount exact match.",
                comparison.SourceDisplay,
                comparison.CandidateDisplay);
        }

        return new ReconciliationRuleDetail(
            ReconciliationRuleNames.AmountExact,
            0m,
            weight,
            false,
            $"Amount difference {comparison.Difference.ToString("0.00", CultureInfo.InvariantCulture)} is not an exact match.",
            comparison.SourceDisplay,
            comparison.CandidateDisplay);
    }

    private static ReconciliationRuleDetail ScoreAmountNear(
        ReconciliationComparableRecord source,
        ReconciliationComparableRecord candidate,
        ReconciliationScoringSettings settings)
    {
        var comparison = CreateAmountComparison(source, candidate);
        if (!comparison.CurrencyMatches)
        {
            return new ReconciliationRuleDetail(
                ReconciliationRuleNames.AmountNear,
                0m,
                settings.Weights.AmountNearMatch,
                false,
                $"Currency mismatch ({comparison.SourceCurrency} vs {comparison.CandidateCurrency}).",
                comparison.SourceDisplay,
                comparison.CandidateDisplay);
        }

        if (comparison.Difference == 0m)
        {
            return new ReconciliationRuleDetail(
                ReconciliationRuleNames.AmountNear,
                0m,
                settings.Weights.AmountNearMatch,
                false,
                "Exact amount match takes precedence over near-match scoring.",
                comparison.SourceDisplay,
                comparison.CandidateDisplay);
        }

        if (settings.NearAmountTolerance <= 0m)
        {
            return new ReconciliationRuleDetail(
                ReconciliationRuleNames.AmountNear,
                0m,
                settings.Weights.AmountNearMatch,
                false,
                "Near amount tolerance is disabled for this tenant.",
                comparison.SourceDisplay,
                comparison.CandidateDisplay);
        }

        if (comparison.Difference <= settings.NearAmountTolerance)
        {
            var ratio = 1m - (comparison.Difference / settings.NearAmountTolerance);
            var score = 0.70m + (0.20m * Math.Clamp(ratio, 0m, 1m));
            return new ReconciliationRuleDetail(
                ReconciliationRuleNames.AmountNear,
                RoundScore(score),
                settings.Weights.AmountNearMatch,
                true,
                $"Amount within tenant tolerance of {settings.NearAmountTolerance.ToString("0.00", CultureInfo.InvariantCulture)}.",
                comparison.SourceDisplay,
                comparison.CandidateDisplay);
        }

        return new ReconciliationRuleDetail(
            ReconciliationRuleNames.AmountNear,
            0m,
            settings.Weights.AmountNearMatch,
            false,
            $"Amount difference {comparison.Difference.ToString("0.00", CultureInfo.InvariantCulture)} exceeds tenant tolerance of {settings.NearAmountTolerance.ToString("0.00", CultureInfo.InvariantCulture)}.",
            comparison.SourceDisplay,
            comparison.CandidateDisplay);
    }

    private static ReconciliationRuleDetail ScoreDate(
        ReconciliationComparableRecord source,
        ReconciliationComparableRecord candidate,
        ReconciliationScoringSettings settings,
        decimal weight)
    {
        var sourceDate = NormalizeUtc(source.Date).Date;
        var candidateDate = NormalizeUtc(candidate.Date).Date;
        var differenceDays = Math.Abs((sourceDate - candidateDate).Days);

        if (differenceDays == 0)
        {
            return new ReconciliationRuleDetail(
                ReconciliationRuleNames.DateProximity,
                1m,
                weight,
                true,
                "Date exact match.",
                sourceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                candidateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (differenceDays <= settings.DateProximityWindowDays && settings.DateProximityWindowDays > 0)
        {
            var score = 1m - (differenceDays / (decimal)(settings.DateProximityWindowDays + 1));
            return new ReconciliationRuleDetail(
                ReconciliationRuleNames.DateProximity,
                RoundScore(score),
                weight,
                true,
                $"Date difference {differenceDays} day(s) within window {settings.DateProximityWindowDays}.",
                sourceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                candidateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return new ReconciliationRuleDetail(
            ReconciliationRuleNames.DateProximity,
            0m,
            weight,
            false,
            settings.DateProximityWindowDays == 0
                ? "Date did not exactly match."
                : $"Date difference {differenceDays} day(s) is outside window {settings.DateProximityWindowDays}.",
            sourceDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            candidateDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    private static ReconciliationRuleDetail ScoreTextRule(
        string ruleName,
        string? sourceValue,
        string? candidateValue,
        decimal weight,
        string label)
    {
        var sourceNormalized = NormalizeText(sourceValue);
        var candidateNormalized = NormalizeText(candidateValue);

        if (string.IsNullOrWhiteSpace(sourceNormalized) || string.IsNullOrWhiteSpace(candidateNormalized))
        {
            return new ReconciliationRuleDetail(
                ruleName,
                0m,
                weight,
                false,
                $"{label} missing for comparison.",
                sourceValue?.Trim(),
                candidateValue?.Trim());
        }

        if (string.Equals(sourceNormalized, candidateNormalized, StringComparison.Ordinal))
        {
            return new ReconciliationRuleDetail(
                ruleName,
                1m,
                weight,
                true,
                $"{label} normalized exact match.",
                sourceValue?.Trim(),
                candidateValue?.Trim());
        }

        if (sourceNormalized.Contains(candidateNormalized, StringComparison.Ordinal) ||
            candidateNormalized.Contains(sourceNormalized, StringComparison.Ordinal))
        {
            return new ReconciliationRuleDetail(
                ruleName,
                0.85m,
                weight,
                true,
                $"{label} contains match after normalization.",
                sourceValue?.Trim(),
                candidateValue?.Trim());
        }

        var sourceTokens = Tokenize(sourceNormalized);
        var candidateTokens = Tokenize(candidateNormalized);
        var commonTokens = sourceTokens.Intersect(candidateTokens, StringComparer.Ordinal).ToArray();
        if (commonTokens.Length > 0)
        {
            var overlap = commonTokens.Length / (decimal)Math.Max(sourceTokens.Length, candidateTokens.Length);
            var score = Math.Max(0.35m, overlap * 0.75m);
            return new ReconciliationRuleDetail(
                ruleName,
                RoundScore(score),
                weight,
                true,
                $"{label} token overlap matched on {string.Join(", ", commonTokens)}.",
                sourceValue?.Trim(),
                candidateValue?.Trim());
        }

        return new ReconciliationRuleDetail(
            ruleName,
            0m,
            weight,
            false,
            $"{label} did not match after normalization.",
            sourceValue?.Trim(),
            candidateValue?.Trim());
    }

    private static decimal GetNormalizationWeight(
        IReadOnlyList<ReconciliationRuleDetail> details,
        ReconciliationScoringWeights weights)
    {
        var amountWeight = details.Any(x => x.RuleName == ReconciliationRuleNames.AmountExact && x.Matched)
            ? weights.AmountExactMatch
            : details.Any(x => x.RuleName == ReconciliationRuleNames.AmountNear && x.Matched)
                ? weights.AmountNearMatch
                : Math.Max(weights.AmountExactMatch, weights.AmountNearMatch);

        return amountWeight +
            weights.DateProximity +
            weights.ReferenceSimilarity +
            weights.CounterpartySimilarity;
    }

    private static decimal GetRuleScore(
        IReadOnlyList<ReconciliationRuleDetail> details,
        string ruleName) =>
        details.First(x => string.Equals(x.RuleName, ruleName, StringComparison.Ordinal)).Score;

    private static AmountComparison CreateAmountComparison(
        ReconciliationComparableRecord source,
        ReconciliationComparableRecord candidate)
    {
        var sourceCurrency = NormalizeCurrency(source.Currency);
        var candidateCurrency = NormalizeCurrency(candidate.Currency);
        var sourceAmount = decimal.Round(Math.Abs(source.Amount), 2, MidpointRounding.AwayFromZero);
        var candidateAmount = decimal.Round(Math.Abs(candidate.Amount), 2, MidpointRounding.AwayFromZero);

        return new AmountComparison(
            sourceCurrency,
            candidateCurrency,
            sourceAmount,
            candidateAmount,
            decimal.Round(Math.Abs(sourceAmount - candidateAmount), 2, MidpointRounding.AwayFromZero),
            string.Equals(sourceCurrency, candidateCurrency, StringComparison.OrdinalIgnoreCase),
            FormatAmount(sourceAmount, sourceCurrency),
            FormatAmount(candidateAmount, candidateCurrency));
    }

    private static string NormalizeCurrency(string currency) =>
        string.IsNullOrWhiteSpace(currency)
            ? string.Empty
            : currency.Trim().ToUpperInvariant();

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = false;
        foreach (var character in value.Trim().ToUpperInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
                continue;
            }

            if (previousWasSpace)
            {
                continue;
            }

            builder.Append(' ');
            previousWasSpace = true;
        }

        return builder.ToString().Trim();
    }

    private static string[] Tokenize(string normalized) =>
        normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    private static string FormatAmount(decimal amount, string currency) =>
        string.IsNullOrWhiteSpace(currency)
            ? amount.ToString("0.00", CultureInfo.InvariantCulture)
            : $"{amount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}";

    private static decimal RoundScore(decimal score) =>
        decimal.Round(Math.Clamp(score, 0m, 1m), 2, MidpointRounding.AwayFromZero);

    private sealed record AmountComparison(
        string SourceCurrency,
        string CandidateCurrency,
        decimal SourceAmount,
        decimal CandidateAmount,
        decimal Difference,
        bool CurrencyMatches,
        string SourceDisplay,
        string CandidateDisplay);
}
