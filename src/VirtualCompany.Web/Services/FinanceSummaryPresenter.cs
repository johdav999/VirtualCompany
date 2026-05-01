using System.Globalization;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public static class FinanceSummaryPresenter
{
    private static readonly CultureInfo DisplayCulture = CultureInfo.InvariantCulture;

    public static CashPositionSummaryViewModel? ToCashPositionViewModel(FinanceCashPositionResponse? response)
    {
        if (response is null || response.CompanyId == Guid.Empty || string.IsNullOrWhiteSpace(response.Currency))
        {
            return null;
        }

        var formattedAvailableBalance = FormatCurrency(response.AvailableBalance, response.Currency);
        var formattedAverageMonthlyBurn = FormatCurrency(response.AverageMonthlyBurn, response.Currency);
        var estimatedRunway = response.EstimatedRunwayDays is int runwayDays ? $"{runwayDays:N0} days" : "n/a";
        var health = BuildCashPositionHealth(response, formattedAvailableBalance, formattedAverageMonthlyBurn, estimatedRunway);

        return new CashPositionSummaryViewModel(
            response.CompanyId,
            FormatDate(response.AsOfUtc),
            formattedAvailableBalance,
            formattedAverageMonthlyBurn,
            estimatedRunway,
            NormalizeLabel(response.RiskLevel),
            NormalizeCssToken(response.RiskLevel),
            response.RecommendedAction,
            response.Rationale,
            response.Classification,
            $"{response.Confidence:P0}",
            response.Confidence > 0m ? $"Confidence: {response.Confidence:P0}" : string.Empty,
            response.Thresholds.WarningCashAmount is decimal warningCash ? FormatCurrency(warningCash, response.Thresholds.Currency) : "n/a",
            response.Thresholds.CriticalCashAmount is decimal criticalCash ? FormatCurrency(criticalCash, response.Thresholds.Currency) : "n/a",
            $"{response.Thresholds.WarningRunwayDays:N0} days",
            $"{response.Thresholds.CriticalRunwayDays:N0} days",
            BuildAlertStatus(response.AlertState),
            "Monthly spending",
            "This is what you spend on average each month.",
            response.EstimatedRunwayDays is int
                ? $"Your cash can last about {estimatedRunway}."
                : "We need more spending data to estimate how long your cash will last.",
            "Cash health",
            health.FriendlyHealthLabel,
            health.FriendlyHealthTone,
            health.CashHealthMessage,
            "Cash guide",
            "Pay attention level",
            "Act now level",
            "Low cash warning",
            "Urgent cash level",
            "We use these levels to let you know when to take action.",
            "What this means",
            health.MeaningTitle,
            health.MeaningParagraphs,
            health.MeaningStatus,
            health.ActionGuidance);
    }

    public static BalancesSummaryViewModel? ToBalancesViewModel(Guid companyId, DateTime referenceUtc, IReadOnlyList<FinanceAccountBalanceResponse>? accounts)
    {
        if (accounts is not { Count: > 0 })
        {
            return null;
        }

        var orderedAccounts = accounts
            .OrderBy(account => account.AccountCode, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var currency = orderedAccounts[0].Currency;

        return new BalancesSummaryViewModel(
            companyId,
            FormatDate(referenceUtc),
            FormatCurrency(orderedAccounts.Sum(account => account.Amount), currency),
            orderedAccounts.Select(account => new BalanceAccountViewModel(
                account.AccountId,
                account.AccountCode,
                account.AccountName,
                NormalizeLabel(account.AccountType),
                FormatCurrency(account.Amount, account.Currency),
                FormatDate(account.AsOfUtc)))
            .ToArray());
    }

    public static MonthlySummaryViewModel? ToMonthlySummaryViewModel(FinanceMonthlySummaryResponse? response)
    {
        if (response is null || response.ProfitAndLoss is null)
        {
            return null;
        }
        
        // A reported month with zero totals is still a valid backend summary for the active tenant.
        var expenseBreakdown = response.ExpenseBreakdown;
        var totalExpenses = expenseBreakdown?.TotalExpenses ?? 0m;

        var categoryItems = (expenseBreakdown?.Categories ?? [])
            .OrderByDescending(category => category.Amount)
            .Select(category => new MonthlyExpenseCategoryViewModel(
                category.Category,
                FormatCurrency(category.Amount, ResolveCurrency(category.Currency, expenseBreakdown?.Currency ?? response.ProfitAndLoss.Currency)),
                FormatPercentage(totalExpenses == 0m ? 0m : category.Amount / totalExpenses)))
            .ToArray();

        return new MonthlySummaryViewModel(
            response.CompanyId,
            $"{response.StartUtc:yyyy-MM-dd} to {response.EndUtc.AddDays(-1):yyyy-MM-dd}",
            FormatCurrency(response.ProfitAndLoss.Revenue, response.ProfitAndLoss.Currency),
            FormatCurrency(response.ProfitAndLoss.Expenses, response.ProfitAndLoss.Currency),
            FormatCurrency(response.ProfitAndLoss.NetResult, response.ProfitAndLoss.Currency),
            categoryItems);
    }

    public static AnomaliesSummaryViewModel? ToAnomaliesViewModel(Guid companyId, IReadOnlyList<FinanceSeedAnomalyResponse>? anomalies)
    {
        if (anomalies is not { Count: > 0 })
        {
            return null;
        }

        return new AnomaliesSummaryViewModel(
            companyId,
            anomalies.Count,
            anomalies.Select(anomaly => new FinanceAnomalyItemViewModel(
                anomaly.Id,
                NormalizeLabel(anomaly.AnomalyType),
                NormalizeLabel(anomaly.ScenarioProfile),
                anomaly.AffectedRecordIds.Count == 1 ? "1 record" : $"{anomaly.AffectedRecordIds.Count} records",
                SummarizeMetadata(anomaly.ExpectedDetectionMetadataJson)))
            .ToArray());
    }

    private static string BuildAlertStatus(FinanceCashPositionAlertStateResponse alertState)
    {
        if (alertState.AlertCreated)
        {
            return "New alert created";
        }

        if (alertState.AlertDeduplicated)
        {
            return "Existing alert reused";
        }

        if (!string.IsNullOrWhiteSpace(alertState.AlertStatus))
        {
            return NormalizeLabel(alertState.AlertStatus);
        }

        return alertState.IsLowCash ? "Low cash detected" : "No active alert";
    }

    private static CashPositionHealthPresentation BuildCashPositionHealth(
        FinanceCashPositionResponse response,
        string formattedAvailableBalance,
        string formattedAverageMonthlyBurn,
        string estimatedRunway)
    {
        var tone = ResolveCashHealthTone(response.RiskLevel, response.AlertState.RiskLevel, response.AlertState.IsLowCash);
        return tone switch
        {
            "success" => new CashPositionHealthPresentation(
                "Good",
                "success",
                "Your cash levels look healthy.",
                "All good",
                [
                    "No alerts right now.",
                    $"You have {formattedAvailableBalance} available. You spend about {formattedAverageMonthlyBurn} each month, so your cash can last about {estimatedRunway}.",
                    "We will let you know if anything changes."
                ],
                "Healthy",
                "Keep monitoring your cash position."),
            "warning" => new CashPositionHealthPresentation(
                "Needs attention",
                "warning",
                "Your cash is available, but it is worth reviewing soon.",
                "Needs attention",
                [
                    "Your cash is still available, but it is getting close to a level where you should review spending.",
                    BuildRunwayContext(response, formattedAvailableBalance, formattedAverageMonthlyBurn, estimatedRunway),
                    "Review upcoming bills and payments."
                ],
                "Needs attention",
                "Review upcoming bills and payments."),
            "danger" => new CashPositionHealthPresentation(
                "Urgent",
                "danger",
                "Your cash is below the safe level.",
                "Act now",
                [
                    "Your cash is below the safe level.",
                    BuildRunwayContext(response, formattedAvailableBalance, formattedAverageMonthlyBurn, estimatedRunway),
                    "Review payments, bills, and expected incoming cash today."
                ],
                "Critical",
                "Review payments, bills, and expected incoming cash today."),
            _ => new CashPositionHealthPresentation(
                "Unknown",
                "neutral",
                "We need more finance activity before we can assess your cash position.",
                "Not enough data yet",
                ["We need more finance activity before we can assess your cash position."],
                "Not available",
                "Add or import finance activity to improve this view.")
        };
    }

    private static string ResolveCashHealthTone(string? riskLevel, string? alertRiskLevel, bool isLowCash)
    {
        var value = $"{riskLevel} {alertRiskLevel}".ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(value))
        {
            return "neutral";
        }

        if (value.Contains("critical", StringComparison.Ordinal) ||
            value.Contains("high", StringComparison.Ordinal) ||
            value.Contains("urgent", StringComparison.Ordinal))
        {
            return "danger";
        }

        if (value.Contains("medium", StringComparison.Ordinal) ||
            value.Contains("warning", StringComparison.Ordinal) ||
            value.Contains("attention", StringComparison.Ordinal))
        {
            return "warning";
        }

        if (!isLowCash && (value.Contains("low", StringComparison.Ordinal) || value.Contains("healthy", StringComparison.Ordinal)))
        {
            return "success";
        }

        return isLowCash ? "warning" : "neutral";
    }

    private static string BuildRunwayContext(
        FinanceCashPositionResponse response,
        string formattedAvailableBalance,
        string formattedAverageMonthlyBurn,
        string estimatedRunway) =>
        response.EstimatedRunwayDays is int
            ? $"You have {formattedAvailableBalance} available and spend about {formattedAverageMonthlyBurn} each month. Your cash can last about {estimatedRunway}."
            : $"You have {formattedAvailableBalance} available. We need more spending data to estimate how long your cash will last.";

    private static string ResolveCurrency(string? currency, string fallbackCurrency) =>
        string.IsNullOrWhiteSpace(currency) ? fallbackCurrency : currency;

    private static string SummarizeMetadata(string metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return "No detector metadata";
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            return string.Join(
                " | ",
                document.RootElement.EnumerateObject()
                    .Select(property => $"{NormalizeLabel(property.Name)}: {property.Value.ToString()}"));
        }
        catch (JsonException)
        {
            return metadataJson;
        }
    }

    private static string FormatCurrency(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", DisplayCulture)}";

    private static string FormatPercentage(decimal percentage) =>
        percentage.ToString("P0", DisplayCulture);

    private static string FormatDate(DateTime utcDateTime) =>
        utcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", DisplayCulture);

    private static string NormalizeLabel(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "n/a"
            : string.Join(" ", value.Trim().Replace("-", "_", StringComparison.Ordinal).Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static string NormalizeCssToken(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "neutral" : value.Trim().ToLowerInvariant().Replace("_", "-", StringComparison.Ordinal);
}

public sealed record CashPositionSummaryViewModel(
    Guid CompanyId,
    string AsOf,
    string AvailableBalance,
    string AverageMonthlyBurn,
    string EstimatedRunway,
    string RiskLevel,
    string RiskLevelCssClass,
    string RecommendedAction,
    string Rationale,
    string Classification,
    string Confidence,
    string ConfidenceText,
    string WarningThreshold,
    string CriticalThreshold,
    string WarningRunway,
    string CriticalRunway,
    string AlertStatus,
    string MonthlySpendingTitle,
    string MonthlySpendingDescription,
    string RunwayDescription,
    string CashHealthTitle,
    string FriendlyHealthLabel,
    string FriendlyHealthTone,
    string CashHealthMessage,
    string CashGuideTitle,
    string WarningCashLabel,
    string CriticalCashLabel,
    string WarningRunwayLabel,
    string CriticalRunwayLabel,
    string CashGuideHelperText,
    string MeaningCardTitle,
    string MeaningTitle,
    IReadOnlyList<string> MeaningParagraphs,
    string MeaningStatus,
    string ActionGuidance);

internal sealed record CashPositionHealthPresentation(
    string FriendlyHealthLabel,
    string FriendlyHealthTone,
    string CashHealthMessage,
    string MeaningTitle,
    IReadOnlyList<string> MeaningParagraphs,
    string MeaningStatus,
    string ActionGuidance);

public sealed record BalancesSummaryViewModel(Guid CompanyId, string AsOf, string TotalBalance, IReadOnlyList<BalanceAccountViewModel> Accounts);
public sealed record BalanceAccountViewModel(Guid AccountId, string AccountCode, string AccountName, string AccountType, string Amount, string AsOf);
public sealed record MonthlySummaryViewModel(Guid CompanyId, string Period, string Revenue, string Expenses, string NetResult, IReadOnlyList<MonthlyExpenseCategoryViewModel> ExpenseCategories);
public sealed record MonthlyExpenseCategoryViewModel(string Category, string Amount, string Share);
public sealed record AnomaliesSummaryViewModel(Guid CompanyId, int TotalAnomalies, IReadOnlyList<FinanceAnomalyItemViewModel> Items);
public sealed record FinanceAnomalyItemViewModel(Guid Id, string Type, string ScenarioProfile, string AffectedRecords, string DetectorSummary);
