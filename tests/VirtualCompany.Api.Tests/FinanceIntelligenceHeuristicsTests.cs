using VirtualCompany.Application.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceIntelligenceHeuristicsTests
{
    [Fact]
    public void Evaluate_returns_deterministic_rankings_projection_values_scoring_and_recommendation_text()
    {
        var input = CreateSeededScenario();

        var first = FinanceIntelligenceHeuristics.Evaluate(input);
        var second = FinanceIntelligenceHeuristics.Evaluate(input);

        Assert.Equivalent(first, second);

        Assert.Equal(1000m, first.SevenDayProjection.StartingCash);
        Assert.Equal(2300m, first.SevenDayProjection.ProjectedInflows);
        Assert.Equal(1950m, first.SevenDayProjection.ProjectedOutflows);
        Assert.Equal(2300m, first.SevenDayProjection.InvoiceInflows);
        Assert.Equal(1650m, first.SevenDayProjection.BillOutflows);
        Assert.Equal(300m, first.SevenDayProjection.RecurringOutflows);
        Assert.Equal(1350m, first.SevenDayProjection.EndingCash);

        Assert.Equal(1000m, first.ThirtyDayProjection.StartingCash);
        Assert.Equal(3200m, first.ThirtyDayProjection.ProjectedInflows);
        Assert.Equal(3000m, first.ThirtyDayProjection.ProjectedOutflows);
        Assert.Equal(3200m, first.ThirtyDayProjection.InvoiceInflows);
        Assert.Equal(2200m, first.ThirtyDayProjection.BillOutflows);
        Assert.Equal(800m, first.ThirtyDayProjection.RecurringOutflows);
        Assert.Equal(1200m, first.ThirtyDayProjection.EndingCash);

        Assert.Equal("healthy", first.ObligationCoverage.Severity);
        Assert.Equal("coverage_healthy", first.ObligationCoverage.RecommendationCode);
        Assert.Equal("Near-term obligations are covered for the next 7 days. Pay overdue bills now and keep collections moving.", first.ObligationCoverage.RecommendationText);

        Assert.Equal(new[] { "INV-100", "INV-200", "INV-210" }, first.OverdueInvoices.Select(x => x.InvoiceNumber).ToArray());
        Assert.Equal(new[] { 45, 18, 18 }, first.OverdueInvoices.Select(x => x.OverdueDays).ToArray());
        Assert.Equal(new[] { "31_60", "1_30", "1_30" }, first.OverdueInvoices.Select(x => x.AgingBucket).ToArray());
        Assert.Equal(new[] { 100, 100, 15 }, first.OverdueInvoices.Select(x => x.PaymentPatternScore).ToArray());
        Assert.Equal(new[] { "critical_risk", "critical_risk", "low_risk" }, first.OverdueInvoices.Select(x => x.PaymentPatternSeverity).ToArray());
        Assert.Equal(new[] { "medium", "medium", "low" }, first.OverdueInvoices.Select(x => x.PaymentPatternConfidence).ToArray());
        Assert.Equal(new[] { "critical", "high", "high" }, first.OverdueInvoices.Select(x => x.Severity).ToArray());
        Assert.Equal(new[] { 73, 52, 52 }, first.OverdueInvoices.Select(x => x.PriorityScore).ToArray());
        Assert.All(first.OverdueInvoices, x => Assert.Equal("follow_up", x.RecommendationType));
        Assert.Equal(new[] { "critical", "high", "high" }, first.OverdueInvoices.Select(x => x.RecommendationSeverity).ToArray());
        Assert.Equal(
            "Call Northwind today about INV-100 and escalate to the account owner. It is 45 day(s) overdue and the customer payment-pattern score is 100.",
            first.OverdueInvoices[0].RecommendationText);
        Assert.Equal(
            "Call Northwind today about INV-200 and secure a payment commitment. It is 18 day(s) overdue and the balance is 450.00 USD.",
            first.OverdueInvoices[1].RecommendationText);
        Assert.Equal(
            "Call Tailspin today about INV-210 and secure a payment commitment. It is 18 day(s) overdue and the balance is 450.00 USD.",
            first.OverdueInvoices[2].RecommendationText);

        Assert.Equal(new[] { "BILL-100", "BILL-200", "BILL-210", "BILL-300", "BILL-400" }, first.DueSoonBills.Select(x => x.BillNumber).ToArray());
        Assert.Equal(new[] { 87, 68, 68, 46, 25 }, first.DueSoonBills.Select(x => x.UrgencyScore).ToArray());
        Assert.Equal(new[] { "critical", "high", "high", "medium", "low" }, first.DueSoonBills.Select(x => x.Severity).ToArray());
        Assert.Equal(new[] { "pay_now", "pay_now", "pay_now", "delay", "delay" }, first.DueSoonBills.Select(x => x.RecommendationAction).ToArray());
        Assert.Equal(new[] { "medium", "high", "high", "medium", "low" }, first.DueSoonBills.Select(x => x.CashImpact).ToArray());
        Assert.Equal(new[] { "important", "important", "important", "standard", "standard" }, first.DueSoonBills.Select(x => x.VendorCriticality).ToArray());
        Assert.Equal(new[] { "manageable", "elevated", "elevated", "manageable", "low" }, first.DueSoonBills.Select(x => x.CashPressure).ToArray());
        Assert.Equal(new[] { 59, 34, 34, 22, 9 }, first.DueSoonBills.Select(x => x.DueDateFactor).ToArray());
        Assert.Equal(new[] { 10, 14, 14, 10, 5 }, first.DueSoonBills.Select(x => x.AmountFactor).ToArray());
        Assert.Equal(new[] { 14, 14, 14, 9, 9 }, first.DueSoonBills.Select(x => x.VendorCriticalityFactor).ToArray());
        Assert.Equal(new[] { 4, 6, 6, 5, 2 }, first.DueSoonBills.Select(x => x.CashPressureFactor).ToArray());
        Assert.All(first.DueSoonBills, x => Assert.False(string.IsNullOrWhiteSpace(x.ScoringFactors)));
        Assert.Equal(
            "Pay now. BILL-100 is overdue and should be cleared before lower-priority cash uses.",
            first.DueSoonBills[0].RecommendationText);
        Assert.Equal(
            "Pay now. BILL-200 falls inside the immediate payment window and current cash coverage remains acceptable.",
            first.DueSoonBills[1].RecommendationText);
        Assert.Equal(
            "Delay. Preserve cash for higher-priority obligations and review BILL-300 on 2026-04-30.",
            first.DueSoonBills[3].RecommendationText);
        Assert.Equal(
            "Delay. BILL-400 is not yet urgent; review it again on 2026-05-08.",
            first.DueSoonBills[4].RecommendationText);
    }

    [Fact]
    public void Evaluate_uses_payment_pattern_score_as_receivables_tie_breaker()
    {
        var asOfUtc = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc);
        var input = new FinanceIntelligenceInputDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            asOfUtc,
            500m,
            "USD",
            [
                new FinanceOpenReceivableItemDto(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), "INV-TIE-100", "Northwind", new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc), 450m, "USD", "open", Guid.Parse("10000000-0000-0000-0000-000000000001")),
                new FinanceOpenReceivableItemDto(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), "INV-TIE-200", "Tailspin", new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc), 450m, "USD", "open", Guid.Parse("20000000-0000-0000-0000-000000000002"))
            ],
            [],
            [],
            [
                new FinanceHistoricalReceivablePaymentDto(Guid.Parse("33333333-3333-3333-3333-333333333331"), "Northwind", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 2, 14, 0, 0, 0, DateTimeKind.Utc), 500m, "USD", Guid.Parse("10000000-0000-0000-0000-000000000001")),
                new FinanceHistoricalReceivablePaymentDto(Guid.Parse("33333333-3333-3333-3333-333333333332"), "Tailspin", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), 500m, "USD", Guid.Parse("20000000-0000-0000-0000-000000000002"))
            ]);

        var result = FinanceIntelligenceHeuristics.Evaluate(input);

        Assert.Equal(new[] { "INV-TIE-100", "INV-TIE-200" }, result.OverdueInvoices.Select(x => x.InvoiceNumber).ToArray());
        Assert.Equal(new[] { 95, 15 }, result.OverdueInvoices.Select(x => x.PaymentPatternScore).ToArray());
    }

    [Fact]
    public void Evaluate_uses_deterministic_payables_tie_breakers_when_urgency_scores_match()
    {
        var asOfUtc = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc);
        var input = new FinanceIntelligenceInputDto(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            asOfUtc,
            1000m,
            "USD",
            [],
            [
                new FinanceOpenPayableItemDto(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"), "BILL-100", "Fabrikam", new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc), 700m, "USD", "open"),
                new FinanceOpenPayableItemDto(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2"), "BILL-200", "Contoso", new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc), 700m, "USD", "open")
            ],
            [],
            []);

        var result = FinanceIntelligenceHeuristics.Evaluate(input);

        Assert.Equal(new[] { "BILL-100", "BILL-200" }, result.DueSoonBills.Select(x => x.BillNumber).ToArray());
        Assert.Equal(new[] { 68, 68 }, result.DueSoonBills.Select(x => x.UrgencyScore).ToArray());
        Assert.Equal(new[] { "important", "important" }, result.DueSoonBills.Select(x => x.VendorCriticality).ToArray());
        Assert.Equal(new[] { "elevated", "elevated" }, result.DueSoonBills.Select(x => x.CashPressure).ToArray());
    }

    private static FinanceIntelligenceInputDto CreateSeededScenario()
    {
        var companyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var asOfUtc = new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc);
        var northwindId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var tailspinId = Guid.Parse("20000000-0000-0000-0000-000000000002");
        var litwareId = Guid.Parse("30000000-0000-0000-0000-000000000003");

        return new FinanceIntelligenceInputDto(
            companyId,
            asOfUtc,
            1000m,
            "USD",
            [
                new FinanceOpenReceivableItemDto(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), "INV-100", "Northwind", new DateTime(2026, 3, 8, 0, 0, 0, DateTimeKind.Utc), 800m, "USD", "open", northwindId),
                new FinanceOpenReceivableItemDto(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2"), "INV-200", "Northwind", new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc), 450m, "USD", "open", northwindId),
                new FinanceOpenReceivableItemDto(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3"), "INV-210", "Tailspin", new DateTime(2026, 4, 4, 0, 0, 0, DateTimeKind.Utc), 450m, "USD", "open", tailspinId),
                new FinanceOpenReceivableItemDto(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa4"), "INV-300", "Tailspin", new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc), 600m, "USD", "open", tailspinId),
                new FinanceOpenReceivableItemDto(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa5"), "INV-400", "Litware", new DateTime(2026, 5, 15, 0, 0, 0, DateTimeKind.Utc), 900m, "USD", "open", litwareId)
            ],
            [
                new FinanceOpenPayableItemDto(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb1"), "BILL-100", "Fabrikam", new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc), 250m, "USD", "open"),
                new FinanceOpenPayableItemDto(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2"), "BILL-200", "Fabrikam", new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc), 700m, "USD", "open"),
                new FinanceOpenPayableItemDto(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3"), "BILL-210", "Contoso", new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc), 700m, "USD", "open"),
                new FinanceOpenPayableItemDto(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb4"), "BILL-300", "Litware", new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc), 400m, "USD", "open"),
                new FinanceOpenPayableItemDto(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb5"), "BILL-400", "Litware", new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc), 150m, "USD", "open")
            ],
            [
                new FinanceRecurringOutflowItemDto("Payroll", new DateTime(2026, 4, 28, 0, 0, 0, DateTimeKind.Utc), 300m, "USD", "monthly", "PAYROLL"),
                new FinanceRecurringOutflowItemDto("Office rent", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), 500m, "USD", "monthly", "RENT")
            ],
            [
                new FinanceHistoricalReceivablePaymentDto(Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc1"), "Northwind", new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc), 500m, "USD", northwindId),
                new FinanceHistoricalReceivablePaymentDto(Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc2"), "Northwind", new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 3, 17, 0, 0, 0, DateTimeKind.Utc), 650m, "USD", northwindId),
                new FinanceHistoricalReceivablePaymentDto(Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc3"), "Northwind", new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc), 700m, "USD", northwindId),
                new FinanceHistoricalReceivablePaymentDto(Guid.Parse("cccccccc-cccc-cccc-cccc-ccccccccccc4"), "Tailspin", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), 400m, "USD", tailspinId)
            ]);
    }
}