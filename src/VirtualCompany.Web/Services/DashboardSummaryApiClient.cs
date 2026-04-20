using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class DashboardSummaryApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public DashboardSummaryApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public async Task<DashboardBriefingSummaryViewModel?> GetBriefingSummaryAsync(
        Guid companyId,
        CancellationToken cancellationToken = default)
    {
        if (companyId == Guid.Empty)
        {
            throw new OnboardingApiException("A valid company id is required to load the dashboard summary.");
        }

        if (_useOfflineMode)
        {
            return new DashboardBriefingSummaryViewModel
            {
                Summary = "The workspace is consolidating attention around a small set of operational items. Review-oriented finance work is leading the queue, with approvals and anomaly follow-up carrying the most urgency. The current focus suggests that near-term execution discipline matters more than broad exploration. Clearing the highest-priority decisions first should reduce friction across the rest of the day.",
                GeneratedUtc = DateTime.UtcNow,
                UsedArtificialIntelligence = false
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/dashboard/briefing-summary?companyId={companyId:D}");
        request.Headers.TryAddWithoutValidation("X-Company-Id", companyId.ToString("D"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            return null;
        }

        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<DashboardBriefingSummaryViewModel>(SerializerOptions, cancellationToken)
            : throw await CreateExceptionAsync(response, cancellationToken);
    }

    private static async Task<OnboardingApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return problem?.Errors is { Count: > 0 }
            ? new OnboardingApiException(problem.Detail ?? problem.Title ?? "The request failed.", problem.Errors)
            : new OnboardingApiException(problem?.Detail ?? problem?.Title ?? $"The request failed with status code {(int)response.StatusCode}.");
    }

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public sealed class DashboardBriefingSummaryViewModel
{
    public string Summary { get; set; } = string.Empty;
    public DateTime GeneratedUtc { get; set; }
    public bool UsedArtificialIntelligence { get; set; }
    public string? Model { get; set; }
}
