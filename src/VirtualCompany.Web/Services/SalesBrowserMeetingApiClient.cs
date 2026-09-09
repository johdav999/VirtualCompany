using System.Net.Http.Json;
namespace VirtualCompany.Web.Services;
public sealed record SalesBrowserMeetingReadinessView(bool Ready, string? Reason);
public sealed record SalesBrowserMeetingLinkView(string Url)
{ public override string ToString() => "SalesBrowserMeetingLinkView { Url = [redacted] }"; }
public sealed class SalesBrowserMeetingApiClient(ICompanyApiTransport transport, bool offline)
{
    public Task<SalesBrowserMeetingReadinessView> ReadinessAsync(Guid company, CancellationToken ct = default) => Get<SalesBrowserMeetingReadinessView>(company, "readiness", ct);
    public Task<SalesBrowserMeetingLinkView> LinkAsync(Guid company, Guid invitation, CancellationToken ct = default) => Get<SalesBrowserMeetingLinkView>(company, $"invitations/{invitation:D}/link", ct);
    public async Task RetryChangeAsync(Guid company,Guid invitation,Guid change,CancellationToken ct=default)
    {
        if(company==Guid.Empty)throw new ArgumentException("Company context is required.");
        if(offline)throw new InvalidOperationException("Browser scheduling needs the backend connection.");
        try {using var response=await transport.SendAsync(company,HttpMethod.Post,$"api/sales/browser-meeting-scheduling/invitations/{invitation:D}/changes/{change:D}/retry",JsonContent.Create(new{}),ct);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("The approved change could not be retried.");}
        catch(HttpRequestException){throw new InvalidOperationException("The browser meeting service could not be reached.");}
    }
    public async Task ReconcileAsync(Guid company, Guid invitation, Guid? change, CancellationToken ct = default)
    {
        if (company == Guid.Empty) throw new ArgumentException("Company context is required.");
        if (offline) throw new InvalidOperationException("Browser scheduling needs the backend connection.");
        try { using var response = await transport.SendAsync(company, HttpMethod.Post, $"api/sales/browser-meeting-scheduling/invitations/{invitation:D}/reconcile" + (change.HasValue ? $"?change={change.Value:D}" : ""), JsonContent.Create(new { }), ct); if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Calendar inspection could not be queued."); }
        catch (HttpRequestException) { throw new InvalidOperationException("The browser meeting service could not be reached."); }
    }
    private async Task<T> Get<T>(Guid company, string path, CancellationToken ct)
    {
        if (company == Guid.Empty) throw new ArgumentException("Company context is required.");
        if (offline) throw new InvalidOperationException("Browser meeting scheduling needs the backend connection.");
        try
        {
            using var response = await transport.SendAsync(company, HttpMethod.Get, "api/sales/browser-meeting-scheduling/" + path, null, ct);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("The browser invitation is not available. The organizer can copy it after delivery is confirmed.");
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct) ?? throw new InvalidOperationException("The browser meeting service returned no result.");
        }
        catch (HttpRequestException) { throw new InvalidOperationException("The browser meeting service could not be reached."); }
    }
}
