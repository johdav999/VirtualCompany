using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSummaryPresenterCashPositionTests
{
    [Fact]
    public void Cash_position_presenter_maps_low_risk_to_friendly_healthy_text()
    {
        var viewModel = FinanceSummaryPresenter.ToCashPositionViewModel(CreateCashPosition(riskLevel: "low", isLowCash: false));

        Assert.NotNull(viewModel);
        Assert.Equal("Good", viewModel!.FriendlyHealthLabel);
        Assert.Equal("success", viewModel.FriendlyHealthTone);
        Assert.Equal("Your cash levels look healthy.", viewModel.CashHealthMessage);
        Assert.Equal("All good", viewModel.MeaningTitle);
        Assert.Equal("Healthy", viewModel.MeaningStatus);
        Assert.Contains("No alerts right now.", viewModel.MeaningParagraphs);
        Assert.Contains(viewModel.MeaningParagraphs, text => text.Contains("USD 51,028.01", StringComparison.Ordinal));
        Assert.DoesNotContain(viewModel.MeaningParagraphs, text => text.Contains("low", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(viewModel.MeaningParagraphs, text => text.Contains("cash_position_healthy", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cash_position_presenter_maps_medium_risk_to_attention_text()
    {
        var viewModel = FinanceSummaryPresenter.ToCashPositionViewModel(CreateCashPosition(riskLevel: "medium", isLowCash: true));

        Assert.NotNull(viewModel);
        Assert.Equal("Needs attention", viewModel!.FriendlyHealthLabel);
        Assert.Equal("warning", viewModel.FriendlyHealthTone);
        Assert.Equal("Needs attention", viewModel.MeaningTitle);
        Assert.Equal("Needs attention", viewModel.MeaningStatus);
        Assert.Contains("Review upcoming bills and payments.", viewModel.MeaningParagraphs);
        Assert.DoesNotContain(viewModel.MeaningParagraphs, text => text.Contains("medium", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("critical")]
    [InlineData("high")]
    public void Cash_position_presenter_maps_critical_or_high_risk_to_urgent_text(string riskLevel)
    {
        var viewModel = FinanceSummaryPresenter.ToCashPositionViewModel(CreateCashPosition(riskLevel: riskLevel, isLowCash: true));

        Assert.NotNull(viewModel);
        Assert.Equal("Urgent", viewModel!.FriendlyHealthLabel);
        Assert.Equal("danger", viewModel.FriendlyHealthTone);
        Assert.Equal("Act now", viewModel.MeaningTitle);
        Assert.Equal("Critical", viewModel.MeaningStatus);
        Assert.Contains("Your cash is below the safe level.", viewModel.MeaningParagraphs);
        Assert.Contains("Review payments, bills, and expected incoming cash today.", viewModel.MeaningParagraphs);
        Assert.DoesNotContain(viewModel.MeaningParagraphs, text => text.Contains(riskLevel, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cash_position_presenter_handles_unknown_or_missing_risk_without_raw_codes()
    {
        var viewModel = FinanceSummaryPresenter.ToCashPositionViewModel(CreateCashPosition(riskLevel: "", alertRiskLevel: ""));

        Assert.NotNull(viewModel);
        Assert.Equal("Unknown", viewModel!.FriendlyHealthLabel);
        Assert.Equal("neutral", viewModel.FriendlyHealthTone);
        Assert.Equal("Not enough data yet", viewModel.MeaningTitle);
        Assert.Equal("Not available", viewModel.MeaningStatus);
        Assert.Contains("We need more finance activity before we can assess your cash position.", viewModel.MeaningParagraphs);
    }

    [Fact]
    public void Cash_position_presenter_omits_confidence_text_when_confidence_is_missing()
    {
        var viewModel = FinanceSummaryPresenter.ToCashPositionViewModel(CreateCashPosition(confidence: 0m));

        Assert.NotNull(viewModel);
        Assert.Equal(string.Empty, viewModel!.ConfidenceText);
    }

    [Fact]
    public void Cash_position_presenter_uses_plain_runway_fallback_when_runway_is_missing()
    {
        var viewModel = FinanceSummaryPresenter.ToCashPositionViewModel(CreateCashPosition(estimatedRunwayDays: null));

        Assert.NotNull(viewModel);
        Assert.Equal("We need more spending data to estimate how long your cash will last.", viewModel!.RunwayDescription);
        Assert.Contains(viewModel.MeaningParagraphs, text => text.Contains("We need more spending data", StringComparison.Ordinal));
    }

    private static FinanceCashPositionResponse CreateCashPosition(
        string riskLevel = "low",
        string? alertRiskLevel = null,
        bool isLowCash = false,
        decimal confidence = 0.86m,
        int? estimatedRunwayDays = 4724) =>
        new()
        {
            CompanyId = Guid.NewGuid(),
            AsOfUtc = new DateTime(2026, 7, 21, 2, 34, 0, DateTimeKind.Utc),
            AvailableBalance = 51028.01m,
            Currency = "USD",
            AverageMonthlyBurn = 324m,
            EstimatedRunwayDays = estimatedRunwayDays,
            RiskLevel = riskLevel,
            Classification = "cash_position_healthy",
            Confidence = confidence,
            AlertState = new FinanceCashPositionAlertStateResponse
            {
                IsLowCash = isLowCash,
                RiskLevel = alertRiskLevel ?? riskLevel
            },
            Thresholds = new FinanceCashPositionThresholdsResponse
            {
                Currency = "USD",
                WarningCashAmount = 972m,
                CriticalCashAmount = 324m,
                WarningRunwayDays = 90,
                CriticalRunwayDays = 30
            }
        };
}
