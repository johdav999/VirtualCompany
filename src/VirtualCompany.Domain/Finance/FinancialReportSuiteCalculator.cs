using System.Security.Cryptography;
using System.Text;

namespace VirtualCompany.Domain.Finance;

public sealed record FinancialReportAmountSeed(
    string LineKey, string Section, string Label, string Currency,
    decimal CurrentAmount, decimal ComparativeAmount, decimal RollingAmount, int ItemCount);

public sealed record FinancialReportCalculatedLine(
    string LineKey, string Section, string Label, string Currency,
    decimal Amount, decimal ComparativeAmount, decimal RollingAmount, int ItemCount);

public static class FinancialReportSuiteCalculator
{
    public const string CalculationVersion = "financial-report-suite/1.0";

    public static IReadOnlyList<FinancialReportCalculatedLine> Calculate(IEnumerable<FinancialReportAmountSeed> seeds) =>
        seeds.GroupBy(x => new { x.LineKey, x.Section, x.Label, x.Currency })
            .OrderBy(x => x.Key.Section, StringComparer.Ordinal)
            .ThenBy(x => x.Key.LineKey, StringComparer.Ordinal)
            .Select(x => new FinancialReportCalculatedLine(x.Key.LineKey, x.Key.Section, x.Key.Label, x.Key.Currency,
                Round(x.Sum(y => y.CurrentAmount)), Round(x.Sum(y => y.ComparativeAmount)),
                Round(x.Sum(y => y.RollingAmount)), x.Sum(y => y.ItemCount)))
            .ToArray();

    public static string Checksum(IEnumerable<FinancialReportCalculatedLine> lines) => Sha256(
        string.Join('\n', lines.OrderBy(x => x.Section, StringComparer.Ordinal).ThenBy(x => x.LineKey, StringComparer.Ordinal)
            .Select(x => $"{x.Section}|{x.LineKey}|{x.Currency}|{x.Amount:0.00}|{x.ComparativeAmount:0.00}|{x.RollingAmount:0.00}|{x.ItemCount}")));

    public static (string Bucket, int DaysPastDue) AgingBucket(DateOnly dueDate, DateOnly asOfDate)
    {
        var days = asOfDate.DayNumber - dueDate.DayNumber;
        return (days switch
        {
            <= 0 => "current",
            <= 30 => "past_due_1_30",
            <= 60 => "past_due_31_60",
            <= 90 => "past_due_61_90",
            _ => "past_due_over_90"
        }, Math.Max(0, days));
    }

    public static decimal Round(decimal amount) => decimal.Round(amount, 2, MidpointRounding.ToEven);
    public static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
