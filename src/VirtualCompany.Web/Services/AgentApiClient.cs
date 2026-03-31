using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class AgentApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly OfflineAgentStore OfflineStore = new();
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;

    public AgentApiClient(HttpClient httpClient, bool useOfflineMode = false)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
    }

    public Task<IReadOnlyList<AgentTemplateCatalogItemViewModel>> GetTemplatesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<IReadOnlyList<AgentTemplateCatalogItemViewModel>>(OfflineStore.GetTemplates())
            : GetAsync<IReadOnlyList<AgentTemplateCatalogItemViewModel>>($"api/companies/{companyId}/agents/templates", cancellationToken);

    public Task<IReadOnlyList<CompanyAgentSummaryViewModel>> GetRosterAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<IReadOnlyList<CompanyAgentSummaryViewModel>>(OfflineStore.GetRoster(companyId))
            : GetAsync<IReadOnlyList<CompanyAgentSummaryViewModel>>($"api/companies/{companyId}/agents", cancellationToken);

    public Task<CreateAgentFromTemplateResultViewModel> CreateAgentFromTemplateAsync(Guid companyId, CreateAgentFromTemplateRequest request, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.CreateFromTemplate(companyId, request))
            : SendAsync<CreateAgentFromTemplateResultViewModel>(HttpMethod.Post, $"api/companies/{companyId}/agents/from-template", request, cancellationToken);

    public Task<CreateAgentFromTemplateResultViewModel> HireAgentAsync(Guid companyId, CreateAgentFromTemplateRequest request, CancellationToken cancellationToken = default) =>
        CreateAgentFromTemplateAsync(companyId, request, cancellationToken);

    public Task<CreateAgentFromTemplateResultViewModel> HireAgentAsync(Guid companyId, HireAgentRequest request, CancellationToken cancellationToken = default) =>
        CreateAgentFromTemplateAsync(companyId, request.ToCreateAgentFromTemplateRequest(), cancellationToken);

    private async Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
                if (result is null)
                {
                    throw new OnboardingApiException("The server returned an empty response.");
                }

                return result;
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
                var result = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
                if (result is null)
                {
                    throw new OnboardingApiException("The server returned an empty response.");
                }

                return result;
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
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.Equals(contentType, "application/json", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(contentType, "application/problem+json", StringComparison.OrdinalIgnoreCase))
        {
            await response.Content.ReadAsStringAsync(cancellationToken);
            return new OnboardingApiException($"The request failed with status code {(int)response.StatusCode}.");
        }

        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(SerializerOptions, cancellationToken);
        if (problem?.Errors is { Count: > 0 })
        {
            return new OnboardingApiException(problem.Detail ?? problem.Title ?? "The request failed.", problem.Errors);
        }

        return new OnboardingApiException(problem?.Detail ?? problem?.Title ?? "The request failed.");
    }

    private OnboardingApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
    }

    private sealed class ApiProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]>? Errors { get; set; }
    }

    private sealed class OfflineAgentStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<Guid, List<CompanyAgentSummaryViewModel>> _agentsByCompany = [];
        private readonly List<AgentTemplateCatalogItemViewModel> _templates =
        [
            new() { TemplateId = "finance", RoleName = "Finance Manager", Department = "Finance", PersonaSummary = "Cash-focused operator who keeps books clean and escalates anomalies early.", DefaultSeniority = "senior", AvatarUrl = "/avatars/agents/finance-manager.png" },
            new() { TemplateId = "sales", RoleName = "Sales Manager", Department = "Sales", PersonaSummary = "Pipeline builder who keeps follow-up tight and qualification clear.", DefaultSeniority = "senior", AvatarUrl = "/avatars/agents/sales-manager.png" },
            new() { TemplateId = "marketing", RoleName = "Marketing Manager", Department = "Marketing", PersonaSummary = "Demand-generation lead who ties campaigns to revenue outcomes.", DefaultSeniority = "senior", AvatarUrl = "/avatars/agents/marketing-manager.png" },
            new() { TemplateId = "support", RoleName = "Support Lead", Department = "Support", PersonaSummary = "Customer advocate who protects SLA health and improves resolution quality.", DefaultSeniority = "lead", AvatarUrl = "/avatars/agents/support-lead.png" },
            new() { TemplateId = "operations", RoleName = "Operations Manager", Department = "Operations", PersonaSummary = "Execution owner who keeps workflows stable and handoffs clear.", DefaultSeniority = "lead", AvatarUrl = "/avatars/agents/operations-manager.png" },
            new() { TemplateId = "executive-assistant", RoleName = "Executive Assistant", Department = "Executive", PersonaSummary = "Founder support partner who protects calendar focus and tracks commitments.", DefaultSeniority = "executive", AvatarUrl = "/avatars/agents/executive-assistant.png" }
        ];

        public IReadOnlyList<AgentTemplateCatalogItemViewModel> GetTemplates() => _templates;

        public IReadOnlyList<CompanyAgentSummaryViewModel> GetRoster(Guid companyId)
        {
            lock (_sync)
            {
                return _agentsByCompany.TryGetValue(companyId, out var agents) ? agents.ToList() : [];
            }
        }

        public CreateAgentFromTemplateResultViewModel CreateFromTemplate(Guid companyId, CreateAgentFromTemplateRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.TemplateId))
            {
                throw new OnboardingApiException("TemplateId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                throw new OnboardingApiException("DisplayName is required.");
            }

            var template = _templates.SingleOrDefault(x => string.Equals(x.TemplateId, request.TemplateId, StringComparison.OrdinalIgnoreCase));
            if (template is null)
            {
                throw new OnboardingApiException("The selected template was not found.");
            }

            var agent = new CompanyAgentSummaryViewModel
            {
                Id = Guid.NewGuid(),
                CompanyId = companyId,
                TemplateId = template.TemplateId,
                DisplayName = request.DisplayName.Trim(),
                AvatarUrl = string.IsNullOrWhiteSpace(request.AvatarUrl) ? template.AvatarUrl : request.AvatarUrl.Trim(),
                Department = string.IsNullOrWhiteSpace(request.Department) ? template.Department : request.Department.Trim(),
                RoleName = string.IsNullOrWhiteSpace(request.RoleName) ? template.RoleName : request.RoleName.Trim(),
                Personality = string.IsNullOrWhiteSpace(request.Personality) ? template.PersonaSummary : request.Personality.Trim(),
                Seniority = string.IsNullOrWhiteSpace(request.Seniority) ? template.DefaultSeniority : request.Seniority.Trim(),
                Status = "active"
            };

            lock (_sync)
            {
                if (!_agentsByCompany.TryGetValue(companyId, out var agents))
                { 
                    agents = [];
                    _agentsByCompany[companyId] = agents;
                }

                agents.Add(agent);
            }

            return new CreateAgentFromTemplateResultViewModel { Agent = agent };
        }
    }
}

public sealed class AgentTemplateCatalogItemViewModel
{
    public string TemplateId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string PersonaSummary { get; set; } = string.Empty;
    public string DefaultSeniority { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

public sealed class CompanyAgentSummaryViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Seniority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string Personality { get; set; } = string.Empty;
}

public sealed class CreateAgentFromTemplateRequest
{
    public string TemplateId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Department { get; set; }
    public string? RoleName { get; set; }
    public string? Personality { get; set; }
    public string? Seniority { get; set; }
}

public sealed class HireAgentRequest
{
    public string TemplateId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? Department { get; set; }
    public string? RoleName { get; set; }
    public string? Personality { get; set; }
    public string? Seniority { get; set; }

    public string? PersonalitySummary
    {
        get => Personality;
        set => Personality = value;
    }

    public CreateAgentFromTemplateRequest ToCreateAgentFromTemplateRequest() =>
        new()
        {
            TemplateId = TemplateId,
            DisplayName = DisplayName,
            AvatarUrl = AvatarUrl,
            Department = Department,
            RoleName = RoleName,
            Personality = Personality,
            Seniority = Seniority
        };
}

public sealed class CreateAgentFromTemplateResultViewModel
{
    public CompanyAgentSummaryViewModel Agent { get; set; } = new();
}
