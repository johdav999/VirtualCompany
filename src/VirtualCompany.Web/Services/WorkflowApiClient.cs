using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed class WorkflowApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public WorkflowApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<IReadOnlyList<WorkflowCatalogItemViewModel>> GetCatalogAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<IReadOnlyList<WorkflowCatalogItemViewModel>>(OfflineCatalog)
            : GetAsync<IReadOnlyList<WorkflowCatalogItemViewModel>>($"api/companies/{companyId}/workflows/catalog", cancellationToken);

    public Task<IReadOnlyList<WorkflowInstanceViewModel>> GetInstancesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<IReadOnlyList<WorkflowInstanceViewModel>>([])
            : GetAsync<IReadOnlyList<WorkflowInstanceViewModel>>($"api/companies/{companyId}/workflows/instances", cancellationToken);

    public Task<WorkflowInstanceViewModel> GetInstanceAsync(Guid companyId, Guid instanceId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineInstance(companyId, instanceId))
            : GetAsync<WorkflowInstanceViewModel>($"api/companies/{companyId}/workflows/instances/{instanceId}", cancellationToken);

    public async Task<WorkflowDefinitionViewModel> GetLatestDefinitionByCodeAsync(Guid companyId, string code, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            var catalogItem = OfflineCatalog.First(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            return new WorkflowDefinitionViewModel
            {
                Id = Guid.Parse("ec1f7bb3-1f3f-4f56-bba6-f403bd02ea04"),
                CompanyId = null,
                Code = catalogItem.Code,
                Name = catalogItem.Name,
                Department = catalogItem.Department,
                Version = catalogItem.Version,
                TriggerType = catalogItem.TriggerType,
                DefinitionJson = catalogItem.DefinitionJson,
                Active = true
            };
        }

        var definitions = await GetAsync<IReadOnlyList<WorkflowDefinitionViewModel>>(
            $"api/companies/{companyId}/workflows/definitions?activeOnly=true&latestOnly=true&includeSystem=true",
            cancellationToken);
        return definitions.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase))
            ?? throw new OnboardingApiException("The selected workflow definition is not active.");
    }

    public Task<WorkflowInstanceViewModel> StartManualWorkflowAsync(
        Guid companyId,
        Guid definitionId,
        StartManualWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(new WorkflowInstanceViewModel
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                DefinitionId = definitionId,
                TriggerSource = "manual",
                TriggerRef = request.TriggerRef,
                Status = "started",
                State = "started",
                CurrentStep = "qualify-lead",
                StartedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DefinitionCode = "LEAD-FOLLOW-UP",
                DefinitionName = "Lead follow-up"
            });
        }

        return SendAsync<WorkflowInstanceViewModel>(
            HttpMethod.Post,
            $"api/companies/{companyId}/workflows/definitions/{definitionId}/start",
            request,
            cancellationToken);
    }

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                    ?? throw new OnboardingApiException("The server returned an empty response.");
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string uri, object payload, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri)
            {
                Content = JsonContent.Create(payload)
            };
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken)
                    ?? throw new OnboardingApiException("The server returned an empty response.");
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
        return problem?.Errors is { Count: > 0 }
            ? new OnboardingApiException(problem.Detail ?? problem.Title ?? "The request failed.", problem.Errors)
            : new OnboardingApiException(problem?.Detail ?? problem?.Title ?? $"The request failed with status code {(int)response.StatusCode}.");
    }

    private OnboardingApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
    }

    private static readonly IReadOnlyList<WorkflowCatalogItemViewModel> OfflineCatalog =
    [
        new()
        {
            Code = "LEAD-FOLLOW-UP",
            Name = "Lead follow-up",
            Description = "Start a focused lead follow-up sequence for sales teams.",
            Department = "Sales",
            Version = 1,
            TriggerType = "manual",
            SupportedStepHandlers = ["qualify_lead", "draft_follow_up", "schedule_next_action"],
            DefinitionJson = new Dictionary<string, JsonNode?> { ["templateCode"] = JsonValue.Create("LEAD-FOLLOW-UP") }
        }
    ];

    private static WorkflowInstanceViewModel OfflineInstance(Guid companyId, Guid instanceId) =>
        new()
        {
            Id = instanceId,
            CompanyId = companyId,
            DefinitionId = Guid.Parse("ec1f7bb3-1f3f-4f56-bba6-f403bd02ea04"),
            TriggerSource = "manual",
            TriggerRef = "offline",
            Status = "started",
            State = "started",
            CurrentStep = "qualify-lead",
            StartedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
            DefinitionCode = "LEAD-FOLLOW-UP",
            DefinitionName = "Lead follow-up"
        };

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }
}

public class WorkflowCatalogItemViewModel
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Department { get; set; }
    public int Version { get; set; }
    public string TriggerType { get; set; } = string.Empty;
    public List<string> SupportedStepHandlers { get; set; } = [];
    public Dictionary<string, JsonNode?> DefinitionJson { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkflowDefinitionViewModel : WorkflowCatalogItemViewModel
{
    public Guid Id { get; set; }
    public Guid? CompanyId { get; set; }
    public bool Active { get; set; }
}

public sealed class WorkflowInstanceViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid DefinitionId { get; set; }
    public Guid? TriggerId { get; set; }
    public string TriggerSource { get; set; } = string.Empty;
    public string? TriggerRef { get; set; }
    public string Status { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? CurrentStep { get; set; }
    public Dictionary<string, JsonNode?> InputPayload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ContextJson { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> OutputPayload { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime StartedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string DefinitionCode { get; set; } = string.Empty;
    public string DefinitionName { get; set; } = string.Empty;
}

public sealed class StartManualWorkflowRequest
{
    public Guid DefinitionId { get; set; }
    public string? TriggerRef { get; set; }
    public Dictionary<string, JsonNode?>? InputPayload { get; set; }
}
