using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

internal static class DocumentCurrencyFacts
{
    public const string BaseCurrencyIdentity = "base_currency_identity";
    public const string AuthoritativeRate = "authoritative_exchange_rate";
    public const string LegacyUnverifiedRate = "legacy_unverified_rate";

    public static string BaseIdentity(string currency, DateOnly date) =>
        $"identity:{NormalizeCurrency(currency)}:{date:yyyy-MM-dd}:1";

    public static string RateIdentity(ExchangeRateLookupResult lookup)
    {
        if (!lookup.IsReady || !lookup.EffectiveRate.HasValue)
            throw new ArgumentException("Only a ready exchange-rate lookup can identify document currency facts.", nameof(lookup));

        if (lookup.Legs.Count == 0)
            return BaseIdentity(lookup.FromCurrency, lookup.RequestedDate);

        var canonical = JsonSerializer.Serialize(new
        {
            FromCurrency = NormalizeCurrency(lookup.FromCurrency),
            ToCurrency = NormalizeCurrency(lookup.ToCurrency),
            lookup.RequestedDate,
            Purpose = lookup.Purpose.Trim().ToLowerInvariant(),
            EffectiveRate = lookup.EffectiveRate.Value.ToString("G29", CultureInfo.InvariantCulture),
            Legs = lookup.Legs.OrderBy(x => x.ObservationId).Select(x => new
            {
                x.ObservationId,
                SourceKey = x.SourceKey.Trim().ToLowerInvariant(),
                x.SourceSetVersion,
                x.EffectiveDate,
                Factor = x.Factor.ToString("G29", CultureInfo.InvariantCulture),
                SourceRate = x.SourceRate.ToString("G29", CultureInfo.InvariantCulture),
                x.RatePrecision,
                EvidenceChecksum = x.EvidenceChecksum.Trim().ToLowerInvariant()
            })
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    public static string RateIdentity(ExchangeRateConversionResult conversion)
    {
        var lookup = new ExchangeRateLookupResult(
            ExchangeRateDecisionStatuses.Ready,
            ExchangeRateReasonCodes.None,
            "Retained conversion.",
            conversion.InputCurrency,
            conversion.OutputCurrency,
            conversion.RequestedDate,
            conversion.Purpose,
            conversion.EffectiveRate,
            conversion.Legs.Select(x => x.EffectiveDate).DefaultIfEmpty(conversion.RequestedDate).Max(),
            conversion.Legs);
        return RateIdentity(lookup);
    }

    public static decimal Round(decimal amount, int precision, string roundingMode) =>
        decimal.Round(amount, precision,
            roundingMode == AccountingRoundingModeValues.AwayFromZero
                ? MidpointRounding.AwayFromZero
                : MidpointRounding.ToEven);

    private static string NormalizeCurrency(string currency) =>
        string.IsNullOrWhiteSpace(currency) ? string.Empty : currency.Trim().ToUpperInvariant();
}
