using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
namespace VirtualCompany.Web.Services;
public sealed class BriefingCadenceApiClient(ICompanyApiTransport transport, bool offline = false)
{
    public Task<BriefingCadenceContext> Get(Guid company, CancellationToken ct) => Send<BriefingCadenceContext>(company, HttpMethod.Get, "", null, ct);
    public Task<BriefingCadenceContext> Save(Guid company, BriefingCadenceSettings settings, CancellationToken ct) => Send<BriefingCadenceContext>(company, HttpMethod.Put, "", settings, ct);
    public Task<BriefingCadencePreview> Preview(Guid company, CancellationToken ct) => Send<BriefingCadencePreview>(company, HttpMethod.Get, "/preview", null, ct);
    public async Task<BriefingCadencePreview> Open(Guid company, Guid id, CancellationToken ct)
    { var p = await Send<BriefingCadencePreview>(company, HttpMethod.Get, $"/deliveries/{id:D}", null, ct); if (p.Audit.Length != 1 || p.Audit[0].Id != id) throw new InvalidDataException("Briefing delivery response differs from the requested delivery."); return p; }
    private async Task<T> Send<T>(Guid c, HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (offline || c == Guid.Empty) throw new InvalidOperationException("Choose a connected company to manage briefings.");
        using var r = await transport.SendAsync(c, method, $"api/companies/{c:D}/briefings/cadence{path}", body == null ? null : JsonContent.Create(body), ct);
        if (r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized) throw new TodayWorkspaceAccessException(r.StatusCode);
        if (!r.IsSuccessStatusCode) { string? detail = null; try { using var j = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct)); if (j.RootElement.TryGetProperty("detail", out var d)) detail = d.GetString(); } catch (JsonException) { } throw new InvalidOperationException(detail ?? "Briefing settings or delivery are unavailable. Reload and retry."); }
        T v; try { v = await r.Content.ReadFromJsonAsync<T>(ct) ?? throw new InvalidDataException("Briefing response is empty."); } catch (JsonException e) { throw new InvalidDataException("Briefing response cannot be read.", e); }
        var valid = v switch {
            BriefingCadenceContext x => x.CompanyId == c && x.UserId != Guid.Empty && x.Settings != null && x.Settings.Schedules?.Length == 7 && x.EligiblePeople != null && x.AllowedAreas != null && x.Channels != null,
            BriefingCadencePreview x => x.CompanyId == c && x.UserId != Guid.Empty && x.Items != null && x.Audit != null && x.Cadences != null && x.Items.All(i => i.WorkPath == $"/work?companyId={c:D}&taskId={i.Id:D}" && (i.RetainedSourcePath == null || i.RetainedSourcePath == $"/work/source?companyId={c:D}&taskId={i.Id:D}")) && x.Audit.All(a => a.Path == $"/briefings?companyId={c:D}&deliveryId={a.Id:D}"), _ => false };
        if (!valid) throw new InvalidDataException("Briefing response differs from the requested company or route."); return v;
    }
}
