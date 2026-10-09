using System.Net;
using System.Net.Http.Json;
namespace VirtualCompany.Web.Services;

public sealed class FinanceRollingPlanningApiClient(ICompanyApiTransport transport, bool offline)
{
    public static string Parameters(FinancePlanningQuery q) => string.Join("&", new Dictionary<string, object?> {
        ["year"] = q.Year, ["month"] = q.Month, ["months"] = q.Months, ["budgetVersion"] = q.BudgetVersion,
        ["forecastVersion"] = q.ForecastVersion, ["currency"] = q.Currency, ["financeAccountId"] = q.FinanceAccountId,
        ["costCenterId"] = q.CostCenterId, ["fiscalPeriodId"] = q.FiscalPeriodId }.Where(x => x.Value != null)
        .Select(x => x.Key + "=" + Uri.EscapeDataString(Convert.ToString(x.Value, System.Globalization.CultureInfo.InvariantCulture)!)));
    public static void Validate(FinancePlanningReport r, Guid company)
    {
        if (r is null || r.CompanyId != company || r.Query is null || r.CalculationVersion != "finance-rolling-planning.v1" ||
            r.Fingerprint?.Length != 64 || r.Rows is null || r.Sources is null || r.BudgetVersions is null || r.ForecastVersions is null ||
            r.Accounts is null || r.CostCenters is null || r.FiscalPeriods is null || r.Coverage is null || r.Explanations is null)
            throw new InvalidDataException("Finance planning context cannot be verified.");
    }
    public async Task<FinancePlanningReport> Report(Guid company, FinancePlanningQuery q, CancellationToken ct)
    {
        var r = await Send<FinancePlanningReport>(company, HttpMethod.Get, "/analysis?" + Parameters(q), null, ct); Validate(r, company);
        if (r.Query != q) throw new InvalidDataException("Selected planning filters differ from the response."); return r;
    }
    public Task<FinancePlanningExport> Export(Guid company, FinancePlanningQuery q, CancellationToken ct) => Send<FinancePlanningExport>(company, HttpMethod.Get, "/export?" + Parameters(q), null, ct);
    public Task<FinanceVarianceExplanationDto> Explain(Guid company, ExplainFinanceVariance c, CancellationToken ct) => Send<FinanceVarianceExplanationDto>(company, HttpMethod.Post, "/explanations", c, ct);
    public async Task<FinanceForecastPreview> Preview(Guid company, PreviewFinanceForecast c, CancellationToken ct)
    { var r = await Send<FinanceForecastPreview>(company, HttpMethod.Post, "/preview", c, ct); ValidatePreview(r, company);
        if(r.Report.Query != c.Query || r.Input.ActualThroughUtc != c.ActualThroughUtc) throw new InvalidDataException("Preview filters differ from the current input."); return r; }
    public async Task<FinanceForecastRevision> Save(Guid company, SaveFinanceForecast c, CancellationToken ct)
    { var r = await Send<FinanceForecastRevision>(company, HttpMethod.Post, "/versions", c, ct); ValidatePreview(r?.Preview!, company);
        if(r?.Summary is null) throw new InvalidDataException("Saved forecast metadata is missing.");
        if(r.Summary.PreviousId != c.PreviousId || r.Summary.Name != c.Name.Trim() || r.Preview.Fingerprint != c.ExpectedFingerprint) throw new InvalidDataException("Saved forecast identity differs from this request."); return r; }
    public async Task<FinanceForecastRevision> Open(Guid company, Guid id, CancellationToken ct)
    { var r = await Send<FinanceForecastRevision>(company, HttpMethod.Get, $"/versions/{id:D}", null, ct); ValidatePreview(r?.Preview!, company);
        if(r?.Summary is null) throw new InvalidDataException("Saved forecast metadata is missing.");
        if(r.Summary.Id != id) throw new InvalidDataException("Saved forecast identity differs from this link."); return r; }
    public Task<IReadOnlyList<FinanceForecastRevisionSummary>> History(Guid company, int skip, CancellationToken ct) => Send<IReadOnlyList<FinanceForecastRevisionSummary>>(company, HttpMethod.Get, "/versions?skip=" + skip, null, ct);
    public Task<FinancePlanningExport> ComparisonExport(Guid company, Guid earlier, Guid later, string? currency, Guid? costCenter, CancellationToken ct)
        => Send<FinancePlanningExport>(company, HttpMethod.Get, $"/compare/export?earlier={earlier:D}&later={later:D}&currency={Uri.EscapeDataString(currency ?? "")}&costCenterId={costCenter}", null, ct);
    public async Task<FinanceForecastComparison> Compare(Guid company, Guid earlier, Guid later, string? currency, Guid? costCenter, CancellationToken ct)
    { var r = await Send<FinanceForecastComparison>(company, HttpMethod.Get, $"/compare?earlier={earlier:D}&later={later:D}&currency={Uri.EscapeDataString(currency ?? "")}&costCenterId={costCenter}", null, ct);
        ValidatePreview(r?.Earlier?.Preview!, company); ValidatePreview(r?.Later?.Preview!, company);
        if(r?.Earlier.Summary is null || r.Later.Summary is null) throw new InvalidDataException("Compared version metadata is missing.");
        if(r.Earlier.Summary.Id != earlier || r.Later.Summary.Id != later || r.Rows is null) throw new InvalidDataException("Compared version context differs from this link."); return r; }
    private static void ValidatePreview(FinanceForecastPreview p, Guid company)
    {
        if(p is null || p.Input is null || p.Input.Assumptions is null || p.Values is null || p.Fingerprint?.Length != 64)
            throw new InvalidDataException("Forecast preview evidence is missing.");
        Validate(p.Report,company);
        if(p.Report.Query != p.Input.Query) throw new InvalidDataException("Preview report and assumptions have inconsistent filters.");
    }
    private async Task<T> Send<T>(Guid company, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (company == Guid.Empty) throw new ArgumentException("Choose a company."); if (offline) throw new InvalidOperationException("Planning requires the connected Finance API.");
        using var response = await transport.SendAsync(company, method, $"internal/companies/{company:D}/finance/planning" + path, body == null ? null : JsonContent.Create(body), ct);
        if(response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new TodayWorkspaceAccessException(response.StatusCode);
        if(!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct); string? message = null;
            try { using var json = System.Text.Json.JsonDocument.Parse(detail); if(json.RootElement.TryGetProperty("detail", out var d)) message = d.GetString(); } catch(System.Text.Json.JsonException) { }
            throw new InvalidOperationException(message ?? response.StatusCode switch { HttpStatusCode.NotFound => "This source or version is unavailable in the current company.",
                HttpStatusCode.Conflict => "Planning sources changed. Reload or preview again before saving.", HttpStatusCode.UnprocessableEntity => "Retained evidence cannot be reproduced.",
                HttpStatusCode.BadRequest => "Check the planning range, versions, currency and dimensions.", _ => "Finance request failed. Retry when the connection is available." });
        }
        try { return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new InvalidDataException("Empty Finance response."); }
        catch(System.Text.Json.JsonException ex) { throw new InvalidDataException("Finance response cannot be read.", ex); }
    }
}
