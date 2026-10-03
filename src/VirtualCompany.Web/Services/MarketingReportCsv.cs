using System.Globalization;
using System.Text;

namespace VirtualCompany.Web.Services;

public static class MarketingReportCsv
{
    public static string Create(MarketingOperationalReportViewModel report, string kind)
    {
        var csv = new StringBuilder();
        void Row(params object?[] values) => csv.AppendLine(string.Join(",", values.Select(Escape)));
        Row("Report", kind, "Company", report.CompanyId, "AsOfUtc", report.AsOfUtc, "FromUtc", report.Filter.FromUtc,
            "ToUtcExclusive", report.Filter.ToUtc, "Campaign", report.Filter.CampaignId, "Currency", report.Filter.Currency, "State", report.Filter.State);
        Row("Calculation", report.CalculationVersion, "Coverage", report.Coverage[kind == "delivery" ? 0 : kind == "spend" ? 1 : 2]);
        if (kind == "delivery")
        {
            Row("ActionId", "CampaignId", "BriefId", "State", "ScheduledUtc", "UpdatedUtc", "Attempts", "ApprovalId", "ProviderReference", "FailureCode");
            foreach (var x in report.Deliveries) Row(x.Id, x.CampaignId, x.BriefId, x.State, x.ScheduledUtc, x.UpdatedUtc, x.Attempts, x.ApprovalId, x.ProviderReference, x.FailureCode);
        }
        else
        {
            Row("CampaignId", "Name", "Currency", "RecordedBudget", "KnownSpend", "KnownSpendMinusBudget_RecordedCostOnly", "UnknownCostTouches", "ObservedLeads", "AttributedContacts", "OutcomeContacts", "Gaps");
            foreach (var x in report.Campaigns) Row(x.Id, x.Name, x.Currency, x.Budget, x.KnownSpend, x.KnownSpend.HasValue && x.Budget.HasValue ? x.KnownSpend - x.Budget : null, x.UnknownCostTouches, x.ObservedLeads, x.AttributedSubjects, x.OutcomeSubjects, string.Join(" ", x.Gaps));
            if (kind == "spend") { Row("TouchId", "CampaignId", "OccurredUtc", "Cost", "Currency", "Source"); foreach (var x in report.Spend) Row(x.Id, x.CampaignId, x.OccurredUtc, x.Cost, x.Currency, x.SourceReference); }
            else { Row("ObservationId", "CampaignId", "Value", "Unit", "Classification", "Source", "ObservedUtc"); foreach (var x in report.Leads) Row(x.Id, x.CampaignId, x.Value, x.Unit, x.Classification, x.SourceReference, x.ObservedUtc); }
        }
        return csv.ToString();
    }
    private static string Escape(object? value)
    {
        var text = value is DateTime date ? DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
