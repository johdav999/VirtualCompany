using System.Globalization;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class PaymentBatchEligibilityPolicy : IPaymentBatchEligibilityPolicy
{
    private readonly PaymentBatchPolicyOptions _options;
    private readonly HashSet<DateOnly> _holidays;
    public PaymentBatchEligibilityPolicy(IOptions<PaymentBatchPolicyOptions> options)
    {
        _options = options.Value;
        _holidays = _options.HolidayDates
            .Select(value => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date) ? date : (DateOnly?)null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToHashSet();
    }

    public PaymentBatchEligibilityDecision Evaluate(PaymentBatchEligibilityInput input)
    {
        var recommended = RecommendDate(input.UtcNow, input.DueDate, input.DiscountDate,
            out var usesDiscount);
        var evidence = new List<string>
        {
            $"obligation:{input.ObligationType}", $"currency:{input.Currency}",
            $"rail:{PaymentRails.Normalize(input.Rail)}", $"due:{input.DueDate:yyyy-MM-dd}",
            $"recommended:{recommended:yyyy-MM-dd}"
        };

        if (input.IsHeld) return Block(PaymentBatchReasonCodes.ObligationHeld,
            "The obligation is on hold and cannot be included in a payment instruction.");
        if (input.IsDisputed) return Block(PaymentBatchReasonCodes.ObligationDisputed,
            "The obligation is disputed and must be resolved before payment.");
        if (input.IsSettled) return Block(PaymentBatchReasonCodes.ObligationSettled,
            "The obligation is already settled.");
        if (input.IsDuplicate) return Block(PaymentBatchReasonCodes.ObligationDuplicate,
            "The obligation already belongs to another active payment batch.");
        if (!input.IsSourceCurrent) return Block(PaymentBatchReasonCodes.SourceChanged,
            "The obligation changed after it was selected. Refresh the batch before continuing.");
        if (!input.IsBeneficiaryVerified) return Block(PaymentBatchReasonCodes.BeneficiaryUnverified,
            "Verified beneficiary payment details are required.");

        var rail = PaymentRails.Normalize(input.Rail);
        if (!PaymentRails.IsSupported(rail)) return Block(PaymentBatchReasonCodes.UnsupportedRail,
            "The beneficiary payment rail is not supported for native batches.");
        var currency = input.Currency.Trim().ToUpperInvariant();
        if (!_options.SupportedCurrencies.Contains(currency, StringComparer.OrdinalIgnoreCase))
            return Block(PaymentBatchReasonCodes.UnsupportedCurrency,
                "The obligation currency is not enabled for native payment batches.");
        if ((rail is PaymentRails.Bankgiro or PaymentRails.Plusgiro) && currency != "SEK")
            return Block(PaymentBatchReasonCodes.UnsupportedRail,
                "Bankgiro and Plusgiro instructions are supported only in SEK.");
        if (input.AvailableCash is null) return Block(PaymentBatchReasonCodes.CashAvailabilityUnknown,
            "Current cash availability is missing for this currency.");
        if (input.AvailableCash.Value < input.Amount) return Block(PaymentBatchReasonCodes.InsufficientCash,
            "Available cash does not cover this obligation.");
        if (input.RequestedExecutionDate < EarliestExecutionDate(input.UtcNow) || input.RequestedExecutionDate > recommended)
            return Block(PaymentBatchReasonCodes.InvalidExecutionDate,
                "Choose a business execution date no later than the due-date recommendation.");

        return new(true, PaymentBatchReasonCodes.Ready,
            usesDiscount ? "Eligible for the verified early-payment date." : "Eligible for the recommended due-date payment run.",
            recommended, usesDiscount, evidence);

        PaymentBatchEligibilityDecision Block(string reasonCode, string explanation) =>
            new(false, reasonCode, explanation, recommended, usesDiscount, evidence);
    }

    private DateOnly RecommendDate(DateTime utcNow, DateOnly dueDate, DateOnly? discountDate,
        out bool usesDiscount)
    {
        var earliest = EarliestExecutionDate(utcNow);
        var dueBusinessDay = PreviousBusinessDay(dueDate);
        usesDiscount = discountDate is { } discount && PreviousBusinessDay(discount) >= earliest;
        var target = usesDiscount ? PreviousBusinessDay(discountDate!.Value) : dueBusinessDay;
        return target < earliest ? earliest : target;
    }

    private DateOnly EarliestExecutionDate(DateTime utcNow)
    {
        var zone = ResolveStockholmTimeZone();
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc), zone);
        var date = DateOnly.FromDateTime(local);
        if (local.Hour >= Math.Clamp(_options.CutOffHourEuropeStockholm, 0, 23)) date = date.AddDays(1);
        return NextBusinessDay(date);
    }

    private DateOnly NextBusinessDay(DateOnly date)
    { while (!IsBusinessDay(date)) date = date.AddDays(1); return date; }
    private DateOnly PreviousBusinessDay(DateOnly date)
    { while (!IsBusinessDay(date)) date = date.AddDays(-1); return date; }
    private bool IsBusinessDay(DateOnly date) =>
        date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !_holidays.Contains(date);
    private static TimeZoneInfo ResolveStockholmTimeZone()
    {
        foreach (var id in new[] { "Europe/Stockholm", "W. Europe Standard Time" })
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        return TimeZoneInfo.Utc;
    }
}
