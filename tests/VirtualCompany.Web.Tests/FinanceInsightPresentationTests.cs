using VirtualCompany.Application.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceInsightPresentationTests
{
    [Fact]
    public void BuildDashboardGroupKey_uses_condition_key_when_present()
    {
        var key = FinanceInsightPresentation.BuildDashboardGroupKey(
            FinancialCheckDefinitions.OverdueReceivables.Code,
            "collections:customer-contoso",
            "invoice",
            "invoice-1");

        Assert.Equal("overdue_receivables|collections:customer_contoso", key);
    }

    [Fact]
    public void BuildDashboardGroupKey_falls_back_to_entity_scope_when_condition_key_is_missing()
    {
        var key = FinanceInsightPresentation.BuildDashboardGroupKey(
            FinancialCheckDefinitions.TransactionAnomaly.Code,
            null,
            "finance_invoice",
            "INV-9");

        Assert.Equal("transaction_anomaly|finance_invoice|inv_9", key);
    }

    [Fact]
    public void BuildEntityTypeCandidates_includes_prefixed_finance_aliases()
    {
        var invoiceCandidates = FinanceInsightPresentation.BuildEntityTypeCandidates("invoice");
        var billCandidates = FinanceInsightPresentation.BuildEntityTypeCandidates("finance_bill");
        var paymentCandidates = FinanceInsightPresentation.BuildEntityTypeCandidates("payment");

        Assert.Contains("invoice", invoiceCandidates);
        Assert.Contains("finance_invoice", invoiceCandidates);
        Assert.Contains("bill", billCandidates);
        Assert.Contains("finance_bill", billCandidates);
        Assert.Contains("payment", paymentCandidates);
        Assert.Contains("finance_payment", paymentCandidates);
    }

    [Fact]
    public void BuildDashboardText_returns_friendly_copy_for_forecast_gap()
    {
        var text = FinanceInsightPresentation.BuildDashboardText(
            FinancialCheckDefinitions.ForecastGap.Code,
            "Acme Co",
            "No forecast entries were found for the current planning period.",
            "Add at least one forecast version.",
            1,
            1);

        Assert.Equal("Forecast coverage is missing", text.Title);
        Assert.Contains("planning period", text.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forecast", text.Recommendation, StringComparison.OrdinalIgnoreCase);
    }
}
