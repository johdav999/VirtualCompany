using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class TodayFocusApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public TodayFocusApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<IReadOnlyList<FocusItemViewModel>> GetAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult<IReadOnlyList<FocusItemViewModel>>(OfflineItems(companyId));
        }

        return GetCoreAsync(companyId, cancellationToken);
    }

    private async Task<IReadOnlyList<FocusItemViewModel>> GetCoreAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new OnboardingApiException("A valid company id is required to load dashboard focus items.");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"api/dashboard/focus?companyId={companyId:D}");
            request.Headers.TryAddWithoutValidation("X-Company-Id", companyId.ToString("D"));

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<IReadOnlyList<FocusItemViewModel>>(SerializerOptions, cancellationToken) ?? [];
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<OnboardingApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        return new OnboardingApiException(problem?.Detail ?? problem?.Title ?? $"The request failed with status code {(int)response.StatusCode}.");
    }

    private OnboardingApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
    }

    private static IReadOnlyList<FocusItemViewModel> OfflineItems(Guid companyId) =>
    [
        new()
        {
            Id = "approval-d0131d5718954b7596249b8b04d8a116",
            Title = "Approval required for task",
            Description = "Review the pending threshold approval before the finance workflow can continue.",
            ActionType = "review",
            PriorityScore = 100,
            NavigationTarget = $"/approvals?companyId={companyId:D}&approvalId=d0131d57-1895-4b75-9624-9b8b04d8a116",
            SourceType = "approval"
        },
        new()
        {
            Id = "task-21fa1767fd7742d7a4d5c6d50ec8bf5b",
            Title = "Resolve blocked fulfillment task",
            Description = "A blocked operations task needs attention to clear downstream work.",
            ActionType = "open",
            PriorityScore = 82,
            NavigationTarget = $"/tasks?companyId={companyId:D}&taskId=21fa1767-fd77-42d7-a4d5-c6d50ec8bf5b",
            SourceType = "task"
        },
        new()
        {
            Id = "anomaly-4df1af18bf984d7ea1f074f59b13f7b3",
            Title = "Investigate finance issue",
            Description = "A recent finance issue was flagged and should be investigated.",
            ActionType = "investigate",
            PriorityScore = 67,
            NavigationTarget = $"/finance/issues/4df1af18-bf98-4d7e-a1f0-74f59b13f7b3?companyId={companyId:D}",
            SourceType = "anomaly"
        }
    ];

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
    }
}

public sealed class FocusItemViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public int PriorityScore { get; set; }
    public string NavigationTarget { get; set; } = string.Empty;
    public string? SourceType { get; set; }
}
