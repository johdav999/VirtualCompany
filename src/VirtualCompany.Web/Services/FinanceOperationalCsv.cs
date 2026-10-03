using System.Globalization;
using System.Text;

namespace VirtualCompany.Web.Services;

public static class FinanceOperationalCsv
{
    public static string Build(FinanceOperationalReportResponse report, string kind)
    {
        var csv = new StringBuilder();
        void Line(params object?[] values) => csv.AppendLine(string.Join(",", values.Select(Cell)));
        Line("Report", kind, "Company", report.CompanyId, "Cutoff UTC", report.AsOfDate.ToString("yyyy-MM-dd"), "Through UTC", report.ThroughDate.ToString("yyyy-MM-dd"));
        Line("Observed UTC", report.ObservedAtUtc.ToString("O"), "Calculation", report.CalculationVersion, "Currency", report.CurrencyFilter, "Bucket", report.BucketFilter);
        Line("Meaning", report.Meaning); Line("Forecast assumptions", report.ForecastAssumptions);
        foreach (var gap in report.CoverageGaps) Line("Coverage gap", gap);
        if (kind == "forecast")
        {
            Line("Currency", "Starting cash", "Expected inflows", "Expected outflows", "Projected cash");
            foreach (var total in report.Totals) Line(total.Currency, total.StartingCash, total.ExpectedInflows, total.ExpectedOutflows, total.ProjectedCash);
            Line("Cash account", "Amount", "Currency", "Source observed UTC", "Basis");
            foreach (var cash in report.CashEvidence) Line(cash.AccountName, cash.Amount, cash.Currency, cash.SourceObservedUtc?.ToString("O"), cash.Basis);
        }
        Line("Kind", "Id", "Number", "Counterparty", "Due UTC", "Days overdue", "Bucket", "Remaining", "Currency", "Recorded status", "Source updated UTC");
        var rows = kind == "receivables" ? report.Receivables : kind == "payables" ? report.Payables : report.Receivables.Concat(report.Payables).Where(x => DateOnly.FromDateTime(x.DueUtc) <= report.ThroughDate).ToArray();
        foreach (var row in rows) Line(row.Kind, row.Id, row.Number, row.Counterparty, row.DueUtc.ToString("O"), row.DaysOverdue, row.Bucket, row.RemainingAmount, row.Currency, row.Status, row.SourceUpdatedUtc.ToString("O"));
        return csv.ToString();
    }
    private static string Cell(object? value)
    {
        var text = value is IFormattable number ? number.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
        if (value is string && text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
