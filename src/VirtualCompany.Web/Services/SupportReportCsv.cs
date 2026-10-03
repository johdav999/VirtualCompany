using System.Globalization;
using System.Text;

namespace VirtualCompany.Web.Services;

public static class SupportReportCsv
{
    public static string Build(SupportOperationalReport report)
    {
        var csv = new StringBuilder("View,As of UTC,Calendar timezone,Case,Subject,Customer,Owner,Status,Age hours,Age bucket,Next deadline UTC,At risk,Breached,Waiting,Missing target\r\n");
        foreach (var row in report.Cases)
        {
            var c = row.Case;
            csv.AppendLine(string.Join(',', new[] { report.View, report.AsOfUtc.ToString("O"), report.Calendar.TimeZoneId,
                c.CaseNumber, c.Subject, c.CustomerName ?? c.ContactName ?? "Unmatched", row.Owner, c.StatusLabel,
                row.AgeHours.ToString(CultureInfo.InvariantCulture), row.AgeBucket, row.NextDeadlineUtc?.ToString("O") ?? "",
                c.IsSlaRisk.ToString(), c.IsSlaBreached.ToString(), row.Waiting.ToString(), row.MissingTarget.ToString() }.Select(Cell)));
        }
        return csv.ToString();
    }
    private static string Cell(string value)
    {
        if (value.TrimStart().StartsWith('=') || value.TrimStart().StartsWith('+') || value.TrimStart().StartsWith('-') || value.TrimStart().StartsWith('@')) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
