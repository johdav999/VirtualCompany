using System.Net;
using System.Net.Http.Json;
namespace VirtualCompany.Web.Services;

public sealed class MarketingManagementApiClient(ICompanyApiTransport transport, bool offline)
{
    public static string Parameters(MarketingManagementQuery q)
    {
        var values = new List<string> { $"year={q.Year}", $"month={q.Month}" };
        if (!string.IsNullOrWhiteSpace(q.Currency)) values.Add("currency=" + Uri.EscapeDataString(q.Currency));
        if (q.CampaignId.HasValue) values.Add("campaignId=" + q.CampaignId);
        if (q.SegmentVersionId.HasValue) values.Add("segmentVersionId=" + q.SegmentVersionId);
        if (q.ModelId.HasValue) values.Add("modelId=" + q.ModelId);
        return string.Join("&", values);
    }
    public async Task<MarketingManagementReport> Report(Guid company, MarketingManagementQuery q, CancellationToken ct = default)
    {
        var report = await Send<MarketingManagementReport>(company, HttpMethod.Get, "?" + Parameters(q), null, ct);
        Validate(report, company);
        if (report.Query != (q with { Currency = Normalize(q.Currency) })) throw new InvalidDataException("Report filters do not match.");
        return report;
    }
    public Task<MarketingManagementExport> Export(Guid company, MarketingManagementQuery q, CancellationToken ct = default)
        => Send<MarketingManagementExport>(company, HttpMethod.Get, "/export?" + Parameters(q), null, ct);
    public async Task<MarketingBudgetProposal> Open(Guid company, Guid id, CancellationToken ct = default)
    {
        var value = await Send<MarketingBudgetProposal>(company, HttpMethod.Get, $"/proposals/{id:D}", null, ct);
        Validate(value.Report, company);
        if (value.Summary is null || value.Summary.Id != id || value.Assumptions is null || value.Result is null)
            throw new InvalidDataException("Proposal identity or retained results are invalid.");
        return value;
    }
    public async Task<MarketingBudgetProposal> Save(Guid company, SaveMarketingBudgetProposal command, CancellationToken ct = default)
    {
        var value = await Send<MarketingBudgetProposal>(company, HttpMethod.Post, "/proposals", JsonContent.Create(command), ct);
        Validate(value.Report, company);
        if (value.Summary is null || value.Summary.PreviousId != command.PreviousId || value.Report.Query != (command.Query with { Currency = Normalize(command.Query.Currency) }))
            throw new InvalidDataException("Saved proposal context does not match.");
        return value;
    }
    public Task<IReadOnlyList<MarketingBudgetProposalSummary>> History(Guid company, int skip, CancellationToken ct = default)
        => Send<IReadOnlyList<MarketingBudgetProposalSummary>>(company, HttpMethod.Get, $"/proposals?skip={skip}", null, ct);
    internal static void Validate(MarketingManagementReport r, Guid company)
    {
        if (r is null || r.CompanyId != company || r.Query is null || r.CalculationVersion != "marketing-management.v1" ||
            r.Channels is null || r.Costs is null || r.Attribution is null || r.Models is null || r.Experiments is null ||
            r.Campaigns is null || r.Plans is null || r.Coverage is null)
            throw new InvalidDataException("Marketing report context cannot be verified.");
    }
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    private async Task<T> Send<T>(Guid company, HttpMethod method, string path, HttpContent? body, CancellationToken ct)
    {
        if (company == Guid.Empty) throw new ArgumentException("Choose a company.");
        if (offline) throw new InvalidOperationException("Management reports require the connected API.");
        using var response = await transport.SendAsync(company, method, "api/marketing/management" + path, body, ct);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new TodayWorkspaceAccessException(response.StatusCode);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException(response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "Check the selected cohort, currency, allocation total and planning ceilings.",
            HttpStatusCode.NotFound => "The selected evidence or private revision is unavailable. Open current history.",
            HttpStatusCode.Conflict => "This proposal changed. Reload its latest revision before saving new assumptions.",
            HttpStatusCode.UnprocessableEntity => "Retained evidence cannot be reproduced. Open another revision or current analysis.",
            _ => "Marketing request failed. Retry when the connection is available."
        });
        try { return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new InvalidDataException("Empty Marketing response."); }
        catch (System.Text.Json.JsonException) { throw new InvalidDataException("Marketing response could not be read. Reload or retry."); }
    }
}
