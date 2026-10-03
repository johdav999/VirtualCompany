using System.Net;
using System.Net.Http.Json;
using System.Globalization;

namespace VirtualCompany.Web.Services;

public sealed class SalesOperationalApiException(string message, HttpStatusCode? status = null, bool uncertain = false) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
    public bool IsUncertain { get; } = uncertain;
}

public sealed class SalesOperationalApiClient(ICompanyApiTransport transport, bool offline)
{
    public Task<SalesOpportunityReportViewModel> OpportunitiesAsync(Guid company, bool forecast, int days, string? currency, Guid? stage, CancellationToken ct = default) =>
        Send<SalesOpportunityReportViewModel>(company, HttpMethod.Get,
            $"opportunities?forecast={forecast.ToString().ToLowerInvariant()}&days={days}&currency={Uri.EscapeDataString(currency ?? "")}&stageId={stage}", null, ct);
    public Task<SalesActivityReportViewModel> ActivitiesAsync(Guid company, string status, Guid? deal = null, CancellationToken ct = default) =>
        Send<SalesActivityReportViewModel>(company, HttpMethod.Get, $"activities?status={Uri.EscapeDataString(status)}&dealId={deal}", null, ct);
    public Task<SalesCommitmentViewModel> RecordAsync(Guid company, Guid deal, Guid command, string summary, DateTime dueUtc, CancellationToken ct = default) =>
        Send<SalesCommitmentViewModel>(company, HttpMethod.Post, $"deals/{deal:D}/commitments", JsonContent.Create(new { CommandId = command, Summary = summary, DueUtc = dueUtc }), ct);
    public Task<SalesCommitmentViewModel> ReviewAsync(Guid company, SalesCommitmentViewModel activity, CancellationToken ct = default) =>
        Send<SalesCommitmentViewModel>(company, HttpMethod.Post, $"activities/{activity.Id:D}/review", JsonContent.Create(new { ExpectedUpdatedUtc = DateTime.SpecifyKind(activity.UpdatedUtc, DateTimeKind.Utc) }), ct);

    private async Task<T> Send<T>(Guid company, HttpMethod method, string route, HttpContent? content, CancellationToken ct)
    {
        if (company == Guid.Empty) throw new ArgumentException("Choose a company.", nameof(company));
        if (offline) throw new SalesOperationalApiException("Sales reports require the connected API.");
        try
        {
            using var response = await transport.SendAsync(company, method, "api/sales/operational/" + route, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode switch
                {
                    HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "Sales access is restricted for this company.",
                    HttpStatusCode.NotFound => "This opportunity or activity is no longer available.",
                    HttpStatusCode.BadRequest => "Check the note, due time and report filters.",
                    HttpStatusCode.Conflict => "This record changed. Reload activity history before retrying.",
                    _ => "Sales is unavailable. Reload before retrying."
                };
                throw new SalesOperationalApiException(message, response.StatusCode, method != HttpMethod.Get && (int)response.StatusCode >= 500);
            }
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct)
                ?? throw new SalesOperationalApiException("The response was empty. Reload before retrying.", uncertain: method != HttpMethod.Get);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        { throw new SalesOperationalApiException("The result could not be confirmed. Reload before retrying.", uncertain: method != HttpMethod.Get); }
    }
}

public sealed record SalesOpportunityReportViewModel(Guid CompanyId, DateTime AsOfUtc, bool IsForecast, int Days,
    string? Currency, Guid? StageId, string CalculationVersion, IReadOnlyList<SalesOpportunityEvidenceViewModel> Rows,
    IReadOnlyList<SalesCurrencyTotalViewModel> Totals, IReadOnlyList<string> AvailableCurrencies,
    IReadOnlyList<SalesForecastCurrencyWindowViewModel>? Windows = null);
public sealed record SalesForecastCurrencyWindowViewModel(int Days, string Currency, int DealCount, decimal GrossAmount, decimal ExpectedAmount);
public sealed record SalesOpportunityEvidenceViewModel(Guid DealId, string Title, Guid StageId, string Stage, string Currency,
    decimal Amount, DateTime? ExpectedCloseUtc, DateTime UpdatedUtc, decimal StageProbability, decimal Risk,
    DateTime? RiskCalculatedUtc, decimal ExpectedAmount);
public sealed record SalesCurrencyTotalViewModel(string Currency, int DealCount, decimal GrossAmount, decimal ExpectedAmount);
public sealed record SalesActivityReportViewModel(Guid CompanyId, DateTime AsOfUtc, string Status, Guid? DealId,
    IReadOnlyList<SalesCommitmentViewModel> Commitments, IReadOnlyList<SalesMeetingCommitmentViewModel> Meetings);
public sealed record SalesCommitmentViewModel(Guid Id, Guid DealId, string DealTitle, string Summary, string Status, DateTime DueUtc, DateTime UpdatedUtc);
public sealed record SalesMeetingCommitmentViewModel(Guid Id, Guid LeadId, Guid? DealId, string Title, DateTime StartsUtc, DateTime EndsUtc, string Status, DateTime UpdatedUtc, string? FailureCode);

public static class SalesOperationalCsv
{
    private static string Field(object? value)
    {
        var text = value is DateTime date ? DateTime.SpecifyKind(date, DateTimeKind.Utc).ToString("O", CultureInfo.InvariantCulture) :
            Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        if (text.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private static string Row(params object?[] values) => string.Join(",", values.Select(Field)) + "\r\n";
    public static string Export(SalesOpportunityReportViewModel report) =>
        Row("Company", "As of UTC", "Calculation", "Horizon days", "Currency filter", "Stage filter") +
        Row(report.CompanyId, report.AsOfUtc, report.CalculationVersion, report.Days, report.Currency, report.StageId) +
        Row("Deal", "Title", "Stage", "Currency", "Gross", "Expected", "Close UTC", "Updated UTC", "Stage probability", "Risk", "Risk calculated UTC") +
        string.Concat(report.Rows.Select(x => Row(x.DealId, x.Title, x.Stage, x.Currency, x.Amount, x.ExpectedAmount, x.ExpectedCloseUtc, x.UpdatedUtc, x.StageProbability, x.Risk, x.RiskCalculatedUtc)));
    public static string Export(SalesActivityReportViewModel report) =>
        Row("Company", "As of UTC", "Status", "Deal filter") + Row(report.CompanyId, report.AsOfUtc, report.Status, report.DealId) +
        Row("Type", "Record", "Opportunity or lead", "Summary", "Status", "Due or start UTC", "Updated UTC") +
        string.Concat(report.Commitments.Select(x => Row("Internal follow-up", x.Id, x.DealId, x.Summary, x.Status, x.DueUtc, x.UpdatedUtc))) +
        string.Concat(report.Meetings.Select(x => Row("Meeting delivery", x.Id, x.LeadId, x.Title, x.Status, x.StartsUtc, x.UpdatedUtc)));
}
