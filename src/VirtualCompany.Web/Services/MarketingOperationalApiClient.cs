using System.Net.Http.Json;
using System.Globalization;

namespace VirtualCompany.Web.Services;

public sealed class MarketingOperationalApiClient(ICompanyApiTransport transport, bool offline)
{
    public Task<MarketingOperationalReportViewModel> ReportAsync(Guid companyId, MarketingOperationalFilterViewModel filter, CancellationToken ct = default) =>
        Get<MarketingOperationalReportViewModel>(companyId, $"report?fromUtc={Uri.EscapeDataString(filter.FromUtc.ToString("O", CultureInfo.InvariantCulture))}&toUtc={Uri.EscapeDataString(filter.ToUtc.ToString("O", CultureInfo.InvariantCulture))}&campaignId={filter.CampaignId}&currency={Uri.EscapeDataString(filter.Currency ?? "")}&state={Uri.EscapeDataString(filter.State ?? "")}", ct);
    public Task<MarketingCampaignReviewViewModel> ReviewAsync(Guid companyId, Guid? campaignId, Guid? briefId, CancellationToken ct = default) =>
        Get<MarketingCampaignReviewViewModel>(companyId, $"review?campaignId={campaignId}&briefId={briefId}", ct);
    private async Task<T> Get<T>(Guid companyId, string path, CancellationToken ct)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Choose a company.");
        if (offline) throw new MarketingApiException("Marketing reports require the connected API.");
        try
        {
            using var response = await transport.SendAsync(companyId, HttpMethod.Get, "api/marketing/operational/" + path, null, ct);
            if (!response.IsSuccessStatusCode) throw new MarketingApiException(response.StatusCode switch
            {
                System.Net.HttpStatusCode.Forbidden => "Marketing access is restricted. Check your company and responsibility.",
                System.Net.HttpStatusCode.NotFound => "The selected campaign or content is unavailable in this company.",
                System.Net.HttpStatusCode.BadRequest => "Check the UTC period, campaign, currency and delivery state.",
                _ => "Marketing data could not be refreshed. Retry to reload current evidence."
            });
            return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new MarketingApiException("Marketing returned no data. Retry.");
        }
        catch (HttpRequestException) { throw new MarketingApiException("Marketing is unreachable. Retry to reload current evidence."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new MarketingApiException("Marketing timed out. Retry to reload current evidence."); }
        catch (System.Text.Json.JsonException) { throw new MarketingApiException("Marketing returned incomplete evidence. Retry to reload it."); }
    }
}
