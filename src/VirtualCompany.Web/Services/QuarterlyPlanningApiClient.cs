using System.Net;
using System.Net.Http.Json;
namespace VirtualCompany.Web.Services;

public sealed class QuarterlyPlanningApiClient(ICompanyApiTransport transport, bool offline = false)
{
    public async Task<QuarterlyPlanningOptions> Options(Guid c, int y, int q, CancellationToken ct)
    { var r = await Send<QuarterlyPlanningOptions>(c, HttpMethod.Get, $"/options?fiscalYear={y}&quarter={q}", null, ct); if (r.Period is null || r.Period.FiscalYear != y || r.Period.Quarter != q || r.Goals is null || r.Owners is null || r.Evidence is null || r.Initiatives is null) throw new InvalidDataException("Planning options do not match this quarter."); return r; }
    public async Task<IReadOnlyList<QuarterReviewSummary>> History(Guid c, int y, int q, CancellationToken ct)
    { var r = await Send<IReadOnlyList<QuarterReviewSummary>>(c, HttpMethod.Get, $"/reviews?fiscalYear={y}&quarter={q}", null, ct); if (r.Any(x => x.CompanyId != c || x.FiscalYear != y || x.Quarter != q)) throw new InvalidDataException("Review history differs from the selected company or quarter."); return r; }
    public async Task<QuarterReviewPreview> Preview(Guid c, PreviewQuarterReview p, CancellationToken ct)
    { var r = await Send<QuarterReviewPreview>(c, HttpMethod.Post, "/preview", p, ct); if (r.Proposal is null || r.Fingerprint?.Length != 64 || System.Text.Json.JsonSerializer.Serialize(r.Proposal) != System.Text.Json.JsonSerializer.Serialize(p)) throw new InvalidDataException("Preview differs from the current proposal."); return r; }
    public async Task<QuarterReviewDocument> Save(Guid c, SaveQuarterReview p, CancellationToken ct)
    { var r = await Send<QuarterReviewDocument>(c, HttpMethod.Post, "/reviews", p, ct); if (r.Summary.PreviousId != p.PreviousId || r.Summary.Revision != p.ExpectedRevision + 1 || r.Review.Fingerprint != p.ExpectedFingerprint) throw new InvalidDataException("Saved review differs from the reviewed proposal."); return r; }
    public async Task<QuarterReviewDocument> Open(Guid c, Guid id, CancellationToken ct)
    { var r = await Send<QuarterReviewDocument>(c, HttpMethod.Post, $"/reviews/{id:D}/open", null, ct); if (r.Summary.Id != id || r.Review.CompanyId != c || r.Review.Period.FiscalYear != r.Summary.FiscalYear || r.Review.Period.Quarter != r.Summary.Quarter || r.Review.Fingerprint?.Length != 64) throw new InvalidDataException("Retained review identity or period differs from this link."); return r; }
    private async Task<T> Send<T>(Guid company, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (offline || company == Guid.Empty) throw new InvalidOperationException("Choose a connected company to plan.");
        using var response = await transport.SendAsync(company, method, $"api/companies/{company:D}/planning/quarters{path}", body is null ? null : JsonContent.Create(body), ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new TodayWorkspaceAccessException(response.StatusCode);
        if (!response.IsSuccessStatusCode)
        {
            string? detail = null;
            try { using var json = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); if (json.RootElement.TryGetProperty("detail", out var d)) detail = d.GetString(); } catch (System.Text.Json.JsonException) { }
            throw new InvalidOperationException(detail ?? (response.StatusCode == HttpStatusCode.NotFound ? "This review or evidence is unavailable in the selected company." : "Quarterly planning failed. Reload the current review."));
        }
        var value = await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new InvalidDataException("Planning response is empty.");
        var context = value switch { QuarterlyPlanningOptions x => x.CompanyId, QuarterReviewPreview x => x.CompanyId, QuarterReviewDocument x => x.Summary.CompanyId, _ => company };
        if (context != company) throw new InvalidDataException("Planning response company differs from the selected company.");
        return value;
    }
}
