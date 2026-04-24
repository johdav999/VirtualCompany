using System.Globalization;

namespace VirtualCompany.Application.Finance;

public static class FinanceIntelligenceHeuristics
{
    private const int DefaultNoHistoryScore = 45;
    private const string DefaultNoHistorySeverity = "medium_risk";
    private const string DefaultNoHistoryConfidence = "no_history";

    public static FinanceIntelligenceSnapshotDto Evaluate(FinanceIntelligenceInputDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var paymentProfiles = BuildPaymentPatternProfiles(input.HistoricalReceivablePayments ?? []);
        var sevenDayProjection = BuildProjection(input, 7);
        var thirtyDayProjection = BuildProjection(input, 30);
        var overdueInvoices = BuildOverdueInvoices(input, paymentProfiles);
        var dueSoonBills = BuildDueSoonBills(input, sevenDayProjection, thirtyDayProjection);
        var obligationCoverage = BuildObligationCoverage(input, sevenDayProjection);

        return new FinanceIntelligenceSnapshotDto(
            input.AsOfUtc,
            sevenDayProjection,
            thirtyDayProjection,
            obligationCoverage,
            overdueInvoices,
            dueSoonBills);
    }

    private static FinanceCashProjectionDto BuildProjection(FinanceIntelligenceInputDto input, int horizonDays)
    {
        var horizonEndDate = input.AsOfUtc.Date.AddDays(horizonDays);

        var invoiceInflows = Round(input.OpenInvoices
            .Where(x => x.OutstandingAmount > 0m && x.DueUtc.Date <= horizonEndDate)
            .Sum(x => x.OutstandingAmount));

        var billOutflows = Round(input.OpenBills
            .Where(x => x.OutstandingAmount > 0m && x.DueUtc.Date <= horizonEndDate)
            .Sum(x => x.OutstandingAmount));

        var recurringOutflows = Round(input.RecurringOutflows
            .Where(x => x.Amount > 0m && x.DueUtc.Date <= horizonEndDate)
            .Sum(x => x.Amount));

        var projectedOutflows = Round(billOutflows + recurringOutflows);

        return new FinanceCashProjectionDto(
            horizonDays,
            Round(input.CurrentCash),
            invoiceInflows,
            projectedOutflows,
            Round(input.CurrentCash + invoiceInflows - projectedOutflows),
            invoiceInflows,
            billOutflows,
            recurringOutflows);
    }

    private static FinanceObligationCoverageDto BuildObligationCoverage(
        FinanceIntelligenceInputDto input,
        FinanceCashProjectionDto sevenDayProjection)
    {
        var nearTermObligations = sevenDayProjection.ProjectedOutflows;
        var availableCash = Round(input.CurrentCash + sevenDayProjection.ProjectedInflows);
        var coverageRatio = nearTermObligations <= 0m
            ? 999m
            : Round(availableCash / nearTermObligations, 4);

        var severity = nearTermObligations <= 0m
            ? "healthy"
            : sevenDayProjection.EndingCash < 0m || coverageRatio < 0.75m
                ? "critical"
                : coverageRatio < 1m
                    ? "at_risk"
                    : coverageRatio < 1.25m
                        ? "watch"
                        : "healthy";

        var recommendation = severity switch
        {
            "critical" => new Recommendation(
                "coverage",
                "critical",
                "coverage_critical",
                "Projected cash turns negative inside the next 7 days. Delay non-critical bills immediately and escalate collections today."),
            "at_risk" => new Recommendation(
                "coverage",
                "high",
                "coverage_at_risk",
                "Near-term obligations are not fully covered over the next 7 days. Delay non-critical bills and accelerate collections today."),
            "watch" => new Recommendation(
                "coverage",
                "medium",
                "coverage_watch_cash_buffer",
                "Near-term obligations are covered with a thin 7-day buffer. Delay non-urgent bills and confirm expected receipts."),
            _ when nearTermObligations <= 0m => new Recommendation(
                "coverage",
                "low",
                "coverage_clear",
                "No bills or recurring outflows fall due in the next 7 days."),
            _ => new Recommendation(
                "coverage",
                "low",
                "coverage_healthy",
                "Near-term obligations are covered for the next 7 days. Pay overdue bills now and keep collections moving.")
        };

        return new FinanceObligationCoverageDto(
            7,
            availableCash,
            nearTermObligations,
            coverageRatio,
            severity,
            recommendation.Code,
            recommendation.Text);
    }

    private static IReadOnlyDictionary<string, PaymentPatternProfile> BuildPaymentPatternProfiles(
        IReadOnlyList<FinanceHistoricalReceivablePaymentDto> history)
    {
        return history
            .GroupBy(x => BuildCustomerKey(x.CustomerId, x.CustomerName), StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var daysLate = group
                        .Select(item => Math.Max(0, (item.PaidUtc.Date - item.DueUtc.Date).Days))
                        .ToArray();

                    var averageDaysLate = RoundToInt(daysLate.Average());
                    var lateInvoicePercent = group.Count() == 0
                        ? 0
                        : RoundToInt(group.Count(item => item.PaidUtc.Date > item.DueUtc.Date) * 100m / group.Count());
                    var severeLateCount = daysLate.Count(days => days >= 30);

                    var score = Math.Clamp(
                        15 +
                        (averageDaysLate * 2) +
                        (lateInvoicePercent / 2) +
                        (severeLateCount * 10),
                        0,
                        100);

                    return new PaymentPatternProfile(
                        score,
                        ResolvePaymentPatternSeverity(score),
                        ResolvePaymentPatternConfidence(group.Count()),
                        group.Count(),
                        averageDaysLate,
                        lateInvoicePercent,
                        severeLateCount);
                },
                StringComparer.Ordinal);
    }

    private static IReadOnlyList<FinanceOverdueInvoiceRecommendationDto> BuildOverdueInvoices(
        FinanceIntelligenceInputDto input,
        IReadOnlyDictionary<string, PaymentPatternProfile> paymentProfiles)
    {
        var asOfDate = input.AsOfUtc.Date;

        return input.OpenInvoices
            .Where(x => x.OutstandingAmount > 0m && x.DueUtc.Date < asOfDate)
            .Select(x => new
            {
                Invoice = x,
                OverdueDays = Math.Max(0, (int)(asOfDate - x.DueUtc.Date).TotalDays),
                PaymentPattern = ResolvePaymentPatternProfile(x.CustomerId, x.CustomerName, paymentProfiles)
            })
            .Where(x => x.OverdueDays > 0)
            .Select(x => new
            {
                x.Invoice,
                x.OverdueDays,
                x.PaymentPattern,
                AgingBucket = ResolveAgingBucket(x.OverdueDays),
                OverdueDaysFactor = ResolveReceivableOverdueFactor(x.OverdueDays),
                AmountFactor = ResolveReceivableAmountFactor(x.Invoice.OutstandingAmount)
            })
            .Select(x => new
            {
                x.Invoice,
                x.OverdueDays,
                x.PaymentPattern,
                x.AgingBucket,
                x.OverdueDaysFactor,
                x.AmountFactor,
                PriorityScore = Math.Clamp(x.OverdueDaysFactor + x.AmountFactor, 0, 100)
            })
            .OrderByDescending(x => x.PriorityScore)
            .ThenByDescending(x => x.OverdueDays)
            .ThenByDescending(x => x.Invoice.OutstandingAmount)
            .ThenByDescending(x => x.PaymentPattern.Score)
            .ThenBy(x => x.Invoice.DueUtc)
            .ThenBy(x => x.Invoice.InvoiceNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Invoice.InvoiceId)
            .Select((x, index) =>
            {
                var severity = ResolveReceivableSeverity(x.PriorityScore);
                var recommendation = BuildInvoiceRecommendation(x.Invoice, x.OverdueDays, severity, x.PaymentPattern);
                return new FinanceOverdueInvoiceRecommendationDto(
                    index + 1,
                    x.Invoice.InvoiceId,
                    x.Invoice.InvoiceNumber,
                    x.Invoice.CustomerName,
                    x.Invoice.DueUtc,
                    Round(x.Invoice.OutstandingAmount),
                    NormalizeCurrency(x.Invoice.Currency, input.Currency),
                    x.OverdueDays,
                    severity,
                    recommendation.Code,
                    recommendation.Text,
                    x.AgingBucket,
                    x.PaymentPattern.Score,
                    x.PaymentPattern.Severity,
                    x.PaymentPattern.Confidence,
                    recommendation.Severity,
                    x.PriorityScore,
                    recommendation.Action,
                    BuildReceivableScoringFactors(x.OverdueDaysFactor, x.AmountFactor, x.PaymentPattern.Score));
            })
            .ToArray();
    }

    private static IReadOnlyList<FinanceDueSoonBillRecommendationDto> BuildDueSoonBills(
        FinanceIntelligenceInputDto input,
        FinanceCashProjectionDto sevenDayProjection,
        FinanceCashProjectionDto thirtyDayProjection)
    {
        var asOfDate = input.AsOfUtc.Date;
        var horizonEndDate = asOfDate.AddDays(30);

        return input.OpenBills
            .Where(x => x.OutstandingAmount > 0m && x.DueUtc.Date <= horizonEndDate)
            .Select(x =>
            {
                var daysUntilDue = (int)(x.DueUtc.Date - asOfDate).TotalDays;
                var dueDateFactor = ResolvePayableDueDateFactor(daysUntilDue);
                var amountFactor = ResolvePayableAmountFactor(x.OutstandingAmount);
                var vendorCriticality = ResolveVendorCriticality(x);
                var vendorCriticalityFactor = ResolveVendorCriticalityFactor(vendorCriticality);
                var cashPressure = ResolveCashPressure(
                    x.OutstandingAmount,
                    input.CurrentCash,
                    sevenDayProjection,
                    thirtyDayProjection);
                var cashPressureFactor = ResolveCashPressureFactor(cashPressure, vendorCriticality);
                var urgencyScore = Math.Clamp(
                    dueDateFactor +
                    amountFactor +
                    vendorCriticalityFactor +
                    cashPressureFactor,
                    0,
                    100);
                var severity = ResolvePayableSeverity(urgencyScore);
                var cashImpact = ResolveLegacyCashImpact(cashPressure);

                return new
                {
                    Bill = x,
                    DaysUntilDue = daysUntilDue,
                    DueDateFactor = dueDateFactor,
                    AmountFactor = amountFactor,
                    VendorCriticality = vendorCriticality,
                    VendorCriticalityFactor = vendorCriticalityFactor,
                    CashPressure = cashPressure,
                    CashPressureFactor = cashPressureFactor,
                    UrgencyScore = urgencyScore,
                    Severity = severity,
                    CashImpact = cashImpact
                };
            })
            .OrderByDescending(x => x.UrgencyScore)
            .ThenBy(x => x.Bill.DueUtc)
            .ThenByDescending(x => x.Bill.OutstandingAmount)
            .ThenBy(x => x.Bill.BillNumber, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Bill.BillId)
            .Select((x, index) =>
            {
                var recommendation = BuildBillRecommendation(
                    x.Bill,
                    x.DaysUntilDue,
                    x.CashPressure,
                    x.VendorCriticality,
                    x.Severity,
                    sevenDayProjection,
                    thirtyDayProjection);

                return new FinanceDueSoonBillRecommendationDto(
                    index + 1,
                    x.Bill.BillId,
                    x.Bill.BillNumber,
                    x.Bill.SupplierName,
                    x.Bill.DueUtc,
                    Round(x.Bill.OutstandingAmount),
                    NormalizeCurrency(x.Bill.Currency, input.Currency),
                    x.DaysUntilDue,
                    x.CashImpact,
                    x.Severity,
                    recommendation.Code,
                    recommendation.Text,
                    x.UrgencyScore,
                    recommendation.Action,
                    recommendation.Severity,
                    BuildCashImpactRationale(x.Bill.OutstandingAmount, input.CurrentCash, x.CashImpact),
                    x.VendorCriticality,
                    BuildVendorCriticalityReason(x.VendorCriticality),
                    x.CashPressure,
                    BuildCashPressureReason(x.CashPressure),
                    x.DueDateFactor,
                    x.AmountFactor,
                    x.VendorCriticalityFactor,
                    x.CashPressureFactor,
                    BuildPayableScoringFactors(
                        x.DueDateFactor,
                        x.AmountFactor,
                        x.VendorCriticalityFactor,
                        x.CashPressureFactor));
            })
            .ToArray();
    }

    private static string ResolveAgingBucket(int overdueDays) =>
        overdueDays switch
        {
            <= 0 => "current",
            <= 30 => "1_30",
            <= 60 => "31_60",
            <= 90 => "61_90",
            _ => "91_plus"
        };

    private static int ResolveReceivableOverdueFactor(int overdueDays) =>
        overdueDays switch
        {
            >= 61 => 70,
            >= 31 => 55,
            >= 14 => 40,
            >= 1 => 25,
            _ => 0
        };

    private static int ResolveReceivableAmountFactor(decimal outstandingAmount) =>
        outstandingAmount switch
        {
            >= 1000m => 25,
            >= 500m => 18,
            >= 250m => 12,
            > 0m => 6,
            _ => 0
        };

    private static string ResolveReceivableSeverity(int priorityScore) =>
        priorityScore switch
        {
            >= 70 => "critical",
            >= 50 => "high",
            >= 30 => "medium",
            _ => "low"
        };

    private static string ResolvePaymentPatternSeverity(int score) =>
        score switch
        {
            >= 85 => "critical_risk",
            >= 65 => "high_risk",
            >= 40 => "medium_risk",
            _ => "low_risk"
        };

    private static string ResolvePaymentPatternConfidence(int historyCount) =>
        historyCount switch
        {
            <= 0 => DefaultNoHistoryConfidence,
            <= 2 => "low",
            <= 4 => "medium",
            _ => "high"
        };

    private static int ResolvePayableDueDateFactor(int daysUntilDue) =>
        daysUntilDue switch
        {
            <= 0 => 58 + Math.Min(7, Math.Abs(daysUntilDue)),
            <= 3 => 42 - (daysUntilDue * 4),
            <= 7 => 30 - ((daysUntilDue - 4) * 3),
            <= 14 => 26 - ((daysUntilDue - 8) * 2),
            _ => Math.Max(5, 10 - Math.Min(5, (daysUntilDue - 15) / 3))
        };

    private static int ResolvePayableAmountFactor(decimal outstandingAmount) =>
        outstandingAmount switch
        {
            >= 1000m => 18,
            >= 500m => 14,
            >= 250m => 10,
            > 0m => 5,
            _ => 0
        };

    private static string ResolveVendorCriticality(FinanceOpenPayableItemDto bill)
    {
        var normalizedText = $"{bill.SupplierName} {bill.BillNumber}".Trim().ToUpperInvariant();

        if (ContainsAny(
                normalizedText,
                "PAYROLL",
                "TAX",
                "RENT",
                "LANDLORD",
                "UTILITY",
                "UTILITIES",
                "POWER",
                "ENERGY",
                "TELECOM",
                "HOSTING",
                "CLOUD",
                "INFRA",
                "INFRASTRUCTURE",
                "SECURITY",
                "INSURANCE",
                "BENEFIT"))
        {
            return "operationally_critical";
        }

        if (ContainsAny(
                normalizedText,
                "SOFTWARE",
                "LICENSE",
                "PLATFORM",
                "SUBSCRIPTION",
                "FABRIKAM",
                "CONTOSO"))
        {
            return "important";
        }

        var first = normalizedText.FirstOrDefault(char.IsLetter);
        if (first is >= 'A' and <= 'G')
        {
            return "important";
        }

        if (first is >= 'H' and <= 'R')
        {
            return "standard";
        }

        return "flexible";
    }

    private static int ResolveVendorCriticalityFactor(string vendorCriticality) =>
        vendorCriticality switch
        {
            "operationally_critical" => 18,
            "important" => 14,
            "standard" => 9,
            _ => 4
        };

    private static string ResolveCashPressure(
        decimal outstandingAmount,
        decimal currentCash,
        FinanceCashProjectionDto sevenDayProjection,
        FinanceCashProjectionDto thirtyDayProjection)
    {
        if (outstandingAmount <= 0m)
        {
            return "low";
        }

        if (currentCash <= 0m ||
            sevenDayProjection.EndingCash - outstandingAmount < 0m)
        {
            return "severe";
        }

        if (thirtyDayProjection.EndingCash - outstandingAmount < 0m ||
            outstandingAmount >= Math.Max(500m, currentCash * 0.5m))
        {
            return "elevated";
        }

        if (outstandingAmount >= Math.Max(250m, currentCash * 0.2m) ||
            sevenDayProjection.EndingCash - outstandingAmount < currentCash * 0.5m)
        {
            return "manageable";
        }

        return "low";
    }

    private static int ResolveCashPressureFactor(string cashPressure, string vendorCriticality)
    {
        var criticalVendor = string.Equals(vendorCriticality, "operationally_critical", StringComparison.Ordinal) ||
                             string.Equals(vendorCriticality, "important", StringComparison.Ordinal);

        return cashPressure switch
        {
            "severe" => criticalVendor ? 8 : 1,
            "elevated" => criticalVendor ? 6 : 3,
            "manageable" => criticalVendor ? 4 : 5,
            _ => criticalVendor ? 2 : 2
        };
    }

    private static string ResolveLegacyCashImpact(string cashPressure) =>
        cashPressure switch
        {
            "severe" => "high",
            "elevated" => "high",
            "manageable" => "medium",
            _ => "low"
        };

    private static string ResolvePayableSeverity(int urgencyScore) =>
        urgencyScore switch
        {
            >= 80 => "critical",
            >= 60 => "high",
            >= 45 => "medium",
            _ => "low"
        };

    private static PaymentPatternProfile ResolvePaymentPatternProfile(
        Guid? customerId,
        string customerName,
        IReadOnlyDictionary<string, PaymentPatternProfile> paymentProfiles)
    {
        var key = BuildCustomerKey(customerId, customerName);
        return paymentProfiles.TryGetValue(key, out var profile)
            ? profile
            : new PaymentPatternProfile(
                DefaultNoHistoryScore,
                DefaultNoHistorySeverity,
                DefaultNoHistoryConfidence,
                0,
                0,
                0,
                0);
    }

    private static Recommendation BuildInvoiceRecommendation(
        FinanceOpenReceivableItemDto invoice,
        int overdueDays,
        string severity,
        PaymentPatternProfile paymentPattern)
    {
        var amount = FormatAmount(invoice.OutstandingAmount);
        var currency = NormalizeCurrency(invoice.Currency, null);

        if (overdueDays >= 31 || (invoice.OutstandingAmount >= 500m && paymentPattern.Score >= 65))
        {
            return new Recommendation(
                "follow_up",
                "critical",
                "call_today_escalate",
                $"Call {invoice.CustomerName} today about {invoice.InvoiceNumber} and escalate to the account owner. It is {overdueDays} day(s) overdue and the customer payment-pattern score is {paymentPattern.Score}.");
        }

        if (overdueDays >= 14 || paymentPattern.Score >= 40 || string.Equals(severity, "high", StringComparison.OrdinalIgnoreCase))
        {
            return new Recommendation(
                "follow_up",
                "high",
                "call_today_secure_commitment",
                $"Call {invoice.CustomerName} today about {invoice.InvoiceNumber} and secure a payment commitment. It is {overdueDays} day(s) overdue and the balance is {amount} {currency}.");
        }

        return new Recommendation(
            "follow_up",
            "medium",
            "send_reminder_schedule_follow_up",
            $"Send a reminder today for {invoice.InvoiceNumber} and schedule follow-up in 3 business days. It is {overdueDays} day(s) overdue for {amount} {currency}.");
    }

    private static Recommendation BuildBillRecommendation(
        FinanceOpenPayableItemDto bill,
        int daysUntilDue,
        string cashPressure,
        string vendorCriticality,
        string severity,
        FinanceCashProjectionDto sevenDayProjection,
        FinanceCashProjectionDto thirtyDayProjection)
    {
        if (daysUntilDue <= 0)
        {
            return new Recommendation(
                "pay_now",
                "critical",
                "pay_now",
                $"Pay now. {bill.BillNumber} is overdue and should be cleared before lower-priority cash uses.");
        }

        if (daysUntilDue <= 3 && !string.Equals(cashPressure, "severe", StringComparison.OrdinalIgnoreCase))
        {
            return new Recommendation(
                "pay_now",
                "high",
                "pay_now",
                $"Pay now. {bill.BillNumber} falls inside the immediate payment window and current cash coverage remains acceptable.");
        }

        var reviewDate = bill.DueUtc.Date.AddDays(-2);
        var criticalVendor = string.Equals(vendorCriticality, "operationally_critical", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(vendorCriticality, "important", StringComparison.OrdinalIgnoreCase);

        if ((string.Equals(cashPressure, "severe", StringComparison.OrdinalIgnoreCase) && !criticalVendor) ||
            (daysUntilDue > 7 && !criticalVendor && !string.Equals(severity, "low", StringComparison.OrdinalIgnoreCase)))
        {
            return new Recommendation(
                "delay",
                "medium",
                "delay_preserve_cash",
                $"Delay. Preserve cash for higher-priority obligations and review {bill.BillNumber} on {reviewDate:yyyy-MM-dd}.");
        }

        if ((string.Equals(severity, "critical", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(severity, "high", StringComparison.OrdinalIgnoreCase)) &&
            criticalVendor &&
            thirtyDayProjection.EndingCash - bill.OutstandingAmount >= 0m)
        {
            return new Recommendation(
                "pay_now",
                "high",
                "pay_now_priority_window",
                $"Pay now. {bill.BillNumber} is inside the priority payment window and current projections still cover the next 30 days.");
        }

        return new Recommendation(
            "delay",
            "low",
            "delay_standard",
            $"Delay. {bill.BillNumber} is not yet urgent; review it again on {reviewDate:yyyy-MM-dd}.");
    }

    private static string NormalizeCurrency(string? primaryCurrency, string? fallbackCurrency)
    {
        if (!string.IsNullOrWhiteSpace(primaryCurrency))
        {
            return primaryCurrency.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(fallbackCurrency))
        {
            return fallbackCurrency.Trim().ToUpperInvariant();
        }

        return "USD";
    }

    private static string FormatAmount(decimal amount) =>
        Round(amount).ToString("0.00", CultureInfo.InvariantCulture);

    private static int SeverityWeight(string severity) =>
        severity switch
        {
            "critical" => 3,
            "high" => 2,
            "medium" => 1,
            _ => 0
        };

    private static int RoundToInt(decimal value) =>
        (int)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static int RoundToInt(double value) =>
        (int)Math.Round(value, 0, MidpointRounding.AwayFromZero);

    private static decimal Round(decimal value, int decimals = 2) =>
        Math.Round(value, decimals, MidpointRounding.AwayFromZero);

    private static string BuildReceivableScoringFactors(int overdueDaysFactor, int amountFactor, int paymentPatternScore) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"overdue_days_factor={overdueDaysFactor}; amount_factor={amountFactor}; payment_pattern_score={paymentPatternScore}");

    private static string BuildPayableScoringFactors(
        int dueDateFactor,
        int amountFactor,
        int vendorCriticalityFactor,
        int cashPressureFactor) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"due_date_factor={dueDateFactor}; amount_factor={amountFactor}; vendor_criticality_factor={vendorCriticalityFactor}; cash_pressure_factor={cashPressureFactor}");

    private static string BuildVendorCriticalityReason(string vendorCriticality) =>
        vendorCriticality switch
        {
            "operationally_critical" => "Vendor matches an operationally critical payment bucket.",
            "important" => "Vendor maps to the important payment bucket.",
            "standard" => "Vendor maps to the standard payment bucket.",
            _ => "Vendor maps to the flexible payment bucket."
        };

    private static string BuildCashPressureReason(string cashPressure) =>
        cashPressure switch
        {
            "severe" => "Paying this bill would put the near-term cash position under immediate pressure.",
            "elevated" => "Paying this bill would materially tighten the projected cash buffer.",
            "manageable" => "Paying this bill would reduce but not break the projected cash buffer.",
            _ => "Paying this bill has limited effect on projected cash."
        };

    private static string BuildCashImpactRationale(decimal amount, decimal currentCash, string cashImpact)
    {
        if (currentCash <= 0m)
        {
            return "Current cash is already at or below zero.";
        }

        var ratioPercent = Round((amount / currentCash) * 100m);
        return cashImpact switch
        {
            "high" => $"Paying this bill now would consume {ratioPercent.ToString("0.00", CultureInfo.InvariantCulture)}% of current cash and materially tighten the buffer.",
            "medium" => $"Paying this bill now would consume {ratioPercent.ToString("0.00", CultureInfo.InvariantCulture)}% of current cash.",
            _ => $"Paying this bill now would consume {ratioPercent.ToString("0.00", CultureInfo.InvariantCulture)}% of current cash and has a limited cash impact."
        };
    }

    private static bool ContainsAny(string value, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (value.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildCustomerKey(Guid? customerId, string customerName) =>
        customerId.HasValue && customerId.Value != Guid.Empty
            ? customerId.Value.ToString("D")
            : customerName.Trim().ToUpperInvariant();

    private sealed record Recommendation(
        string Action,
        string Severity,
        string Code,
        string Text);

    private sealed record PaymentPatternProfile(
        int Score,
        string Severity,
        string Confidence,
        int HistoryCount,
        int AverageDaysLate,
        int LateInvoicePercent,
        int SevereLateCount);
}