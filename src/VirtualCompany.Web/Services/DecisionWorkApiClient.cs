using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace VirtualCompany.Web.Services;
public sealed class DecisionWorkApiClient(ICompanyApiTransport transport, bool offline = false)
{
    public async Task<DecisionWorkContext> Context(Guid c, DecisionWorkSourceRef source, CancellationToken ct)
    { var r = await Send<DecisionWorkContext>(c, HttpMethod.Get, $"/context?kind={Uri.EscapeDataString(source.Kind)}&versionId={source.VersionId:D}&itemKey={Uri.EscapeDataString(source.ItemKey)}", null, ct); Check(r.Source.Reference == source); return r; }
    public async Task<DecisionWorkPreview> Preview(Guid c, DecisionWorkInput input, CancellationToken ct)
    { var r = await Send<DecisionWorkPreview>(c, HttpMethod.Post, "/preview", input, ct); Check(Same(r.Input, input)); return r; }
    public async Task<DecisionWorkDocument> Create(Guid c, ConfirmDecisionWork command, CancellationToken ct)
    { var r = await Send<DecisionWorkDocument>(c, HttpMethod.Post, "", command, ct); Check(Same(r.Created.Input, command.Input) && r.Created.Fingerprint == command.ExpectedFingerprint); return r; }
    public async Task<DecisionWorkDocument> Open(Guid c, Guid task, CancellationToken ct)
    { var r = await Send<DecisionWorkDocument>(c, HttpMethod.Post, $"/tasks/{task:D}/open", null, ct); Check(r.TaskId == task); return r; }
    public async Task<DecisionWorkDocument> Review(Guid c, Guid task, CancellationToken ct)
    { var r = await Send<DecisionWorkDocument>(c, HttpMethod.Post, $"/tasks/{task:D}/review", null, ct); Check(r.TaskId == task && r.ApprovalId.HasValue); return r; }
    private static bool Same<T>(T a, T b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);
    private static void Check(bool valid) { if (!valid) throw new InvalidDataException("Work response differs from the requested company, source or proposal."); }
    private static bool Valid(Guid c, DecisionWorkSource s) => s.CompanyId == c && s.Reference != null && s.Reference.VersionId != Guid.Empty && s.Version > 0 && s.Fingerprint?.Length == 64 &&
        s.SourcePath?.StartsWith("/dashboard?companyId=" + c.ToString("D"), StringComparison.Ordinal) == true && s.EvidenceJson != null;
    private static bool Valid(Guid c, DecisionWorkPreview p) => p.CompanyId == c && p.Input != null && p.Source != null && p.Input.Source == p.Source.Reference && Valid(c, p.Source) && p.Collaborators != null && p.Fingerprint?.Length == 64;
    private static bool Valid(Guid c, DecisionWorkDocument d) => d.CompanyId == c && d.TaskId != Guid.Empty && d.Created != null && Valid(c, d.Created) &&
        d.WorkPath == $"/work?companyId={c:D}&taskId={d.TaskId:D}" && d.OriginPath == DecisionWorkRoutes.Source(c, d.TaskId);
    private async Task<T> Send<T>(Guid c, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (offline || c == Guid.Empty) throw new InvalidOperationException("Choose a connected company before creating owned work.");
        using var r = await transport.SendAsync(c, method, $"api/companies/{c:D}/decision-work{path}", body == null ? null : JsonContent.Create(body), ct);
        if (r.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new TodayWorkspaceAccessException(r.StatusCode);
        if (!r.IsSuccessStatusCode) { string? detail = null; try { using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)); if (j.RootElement.TryGetProperty("detail", out var d)) detail = d.GetString(); } catch (JsonException) { }
            throw new InvalidOperationException(detail ?? "The source or work is unavailable. Reload the authorized source and retry."); }
        T value; try { value = await r.Content.ReadFromJsonAsync<T>(ct) ?? throw new InvalidDataException("Work response is empty."); } catch (JsonException e) { throw new InvalidDataException("Work response cannot be read.", e); }
        Check(value switch { DecisionWorkContext x => x.CompanyId == c && x.Source != null && Valid(c, x.Source) && x.Owners != null && x.Work != null && x.Work.All(w => Valid(c, w)), DecisionWorkPreview x => Valid(c, x), DecisionWorkDocument x => Valid(c, x), _ => false }); return value;
    }
}
