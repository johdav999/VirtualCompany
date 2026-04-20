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

        return new CashPositionSummaryViewModel(
            response.CompanyId,
            FormatDate(response.AsOfUtc),
            FormatCurrency(response.AvailableBalance, response.Currency),
            FormatCurrency(response.AverageMonthlyBurn, response.Currency),
            response.EstimatedRunwayDays is int runwayDays ? $"{runwayDays} days" : "n/a",
            NormalizeLabel(response.RiskLevel),
            NormalizeCssToken(response.RiskLevel),
            response.RecommendedAction,
            response.Rationale,
            response.Classification,
            $"{response.Confidence:P0}",
            response.Thresholds.WarningCashAmount is decimal warningCash ? FormatCurrency(warningCash, response.Thresholds.Currency) : "n/a",
            response.Thresholds.CriticalCashAmount is decimal criticalCash ? FormatCurrency(criticalCash, response.Thresholds.Currency) : "n/a",
            $"{response.Thresholds.WarningRunwayDays} days",
            $"{response.Thresholds.CriticalRunwayDays} days",
            BuildAlertStatus(response.AlertState));
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
    string WarningThreshold,
    string CriticalThreshold,
    string WarningRunway,
    string CriticalRunway,
    string AlertStatus);

public sealed record BalancesSummaryViewModel(Guid CompanyId, string AsOf, string TotalBalance, IReadOnlyList<BalanceAccountViewModel> Accounts);
public sealed record BalanceAccountViewModel(Guid AccountId, string AccountCode, string AccountName, string AccountType, string Amount, string AsOf);
public sealed record MonthlySummaryViewModel(Guid CompanyId, string Period, string Revenue, string Expenses, string NetResult, IReadOnlyList<MonthlyExpenseCategoryViewModel> ExpenseCategories);
public sealed record MonthlyExpenseCategoryViewModel(string Category, string Amount, string Share);
public sealed record AnomaliesSummaryViewModel(Guid CompanyId, int TotalAnomalies, IReadOnlyList<FinanceAnomalyItemViewModel> Items);
public sealed record FinanceAnomalyItemViewModel(Guid Id, string Type, string ScenarioProfile, string AffectedRecords, string DetectorSummary);