using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    public async Task<IReadOnlyList<AgentTemplateCatalogItemViewModel>> GetTemplatesAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return await OfflineStore.GetTemplatesAsync(_httpClient, cancellationToken);
        }

        return await GetAsync<IReadOnlyList<AgentTemplateCatalogItemViewModel>>($"api/companies/{companyId}/agents/templates", cancellationToken);
    }

    public Task<IReadOnlyList<CompanyAgentSummaryViewModel>> GetRosterAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.GetRoster(companyId))
            : GetAsync<IReadOnlyList<CompanyAgentSummaryViewModel>>($"api/companies/{companyId}/agents", cancellationToken);

    public Task<CreateAgentFromTemplateResultViewModel> CreateAgentFromTemplateAsync(Guid companyId, CreateAgentFromTemplateRequest request, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? OfflineStore.CreateFromTemplateAsync(companyId, request, _httpClient, cancellationToken)
            : SendAsync<CreateAgentFromTemplateResultViewModel>(HttpMethod.Post, $"api/companies/{companyId}/agents/from-template", request, cancellationToken);

    public Task<CreateAgentFromTemplateResultViewModel> HireAgentAsync(Guid companyId, CreateAgentFromTemplateRequest request, CancellationToken cancellationToken = default) =>
        CreateAgentFromTemplateAsync(companyId, request, cancellationToken);

    public Task<CreateAgentFromTemplateResultViewModel> HireAgentAsync(Guid companyId, HireAgentRequest request, CancellationToken cancellationToken = default) =>
        CreateAgentFromTemplateAsync(companyId, request.ToCreateAgentFromTemplateRequest(), cancellationToken);

    public Task<AgentOperatingProfileViewModel> GetOperatingProfileAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? OfflineStore.GetOperatingProfileAsync(companyId, agentId, cancellationToken)
            : GetAsync<AgentOperatingProfileViewModel>($"api/companies/{companyId}/agents/{agentId}/profile", cancellationToken);

    public Task<AgentOperatingProfileViewModel> UpdateOperatingProfileAsync(Guid companyId, Guid agentId, UpdateAgentOperatingProfileRequest request, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? OfflineStore.UpdateOperatingProfileAsync(companyId, agentId, request, cancellationToken)
            : SendAsync<AgentOperatingProfileViewModel>(HttpMethod.Put, $"api/companies/{companyId}/agents/{agentId}/profile", request, cancellationToken);

    private const string OfflineTemplateCatalogPath = "offline/agent-templates.json";

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
        private readonly Dictionary<Guid, AgentOperatingProfileViewModel> _profilesByAgentId = [];
        private Task<IReadOnlyList<AgentTemplateCatalogItemViewModel>>? _templatesTask;

        public Task<IReadOnlyList<AgentTemplateCatalogItemViewModel>> GetTemplatesAsync(HttpClient httpClient, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                _templatesTask ??= LoadTemplatesAsync(httpClient, cancellationToken);
                return _templatesTask;
            }
        }

        public IReadOnlyList<CompanyAgentSummaryViewModel> GetRoster(Guid companyId)
        {
            lock (_sync)
            {
                return _agentsByCompany.TryGetValue(companyId, out var agents)
                    ? agents.Select(CloneAgent).ToList()
                    : [];
            }
        }

        public Task<AgentOperatingProfileViewModel> GetOperatingProfileAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (_profilesByAgentId.TryGetValue(agentId, out var profile) && profile.CompanyId == companyId)
                {
                    return Task.FromResult(CloneProfile(profile));
                }

                throw new OnboardingApiException("The selected agent profile could not be loaded.");
            }
        }

        public Task<AgentOperatingProfileViewModel> UpdateOperatingProfileAsync(
            Guid companyId,
            Guid agentId,
            UpdateAgentOperatingProfileRequest request,
            CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_profilesByAgentId.TryGetValue(agentId, out var existing) || existing.CompanyId != companyId)
                {
                    throw new OnboardingApiException("The selected agent profile could not be loaded.");
                }

                existing.Status = request.Status.Trim();
                existing.RoleBrief = string.IsNullOrWhiteSpace(request.RoleBrief) ? null : request.RoleBrief.Trim();
                existing.Objectives = CloneNodes(request.Objectives);
                existing.Kpis = CloneNodes(request.Kpis);
                existing.ToolPermissions = CloneNodes(request.ToolPermissions);
                existing.DataScopes = CloneNodes(request.DataScopes);
                existing.ApprovalThresholds = CloneNodes(request.ApprovalThresholds);
                existing.EscalationRules = CloneNodes(request.EscalationRules);
                existing.TriggerLogic = CloneNodes(request.TriggerLogic);
                existing.WorkingHours = CloneNodes(request.WorkingHours);
                existing.UpdatedUtc = DateTime.UtcNow;
                existing.CanReceiveAssignments = !string.Equals(existing.Status, "archived", StringComparison.OrdinalIgnoreCase);

                if (_agentsByCompany.TryGetValue(companyId, out var roster))
                {
                    var agent = roster.SingleOrDefault(x => x.Id == agentId);
                    if (agent is not null)
                    {
                        agent.Status = existing.Status;
                    }
                }

                return Task.FromResult(CloneProfile(existing));
            }
        }

        public async Task<CreateAgentFromTemplateResultViewModel> CreateFromTemplateAsync(
            Guid companyId,
            CreateAgentFromTemplateRequest request,
            HttpClient httpClient,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.TemplateId))
            {
                throw new OnboardingApiException("TemplateId is required.");
            }

            if (string.IsNullOrWhiteSpace(request.DisplayName))
            {
                throw new OnboardingApiException("DisplayName is required.");
            }

            if (!TryValidateAvatarReference(request.AvatarUrl, out var avatarError))
            {
                throw new OnboardingApiException(avatarError!);
            }

            var templates = await GetTemplatesAsync(httpClient, cancellationToken);
            var template = templates.SingleOrDefault(x => string.Equals(x.TemplateId, request.TemplateId, StringComparison.OrdinalIgnoreCase));
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

            var profile = new AgentOperatingProfileViewModel
            {
                Id = agent.Id,
                CompanyId = companyId,
                TemplateId = agent.TemplateId,
                DisplayName = agent.DisplayName,
                RoleName = agent.RoleName,
                Department = agent.Department,
                Seniority = agent.Seniority,
                Status = agent.Status,
                AvatarUrl = agent.AvatarUrl,
                RoleBrief = request.Personality,
                Objectives = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
                Kpis = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
                ToolPermissions = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
                DataScopes = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
                ApprovalThresholds = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
                EscalationRules = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
                TriggerLogic = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
                WorkingHours = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
                UpdatedUtc = DateTime.UtcNow,
                CanReceiveAssignments = true
            };

            lock (_sync)
            {
                if (!_agentsByCompany.TryGetValue(companyId, out var agents))
                { 
                    agents = [];
                    _agentsByCompany[companyId] = agents;
                }

                agents.Add(agent);
                _profilesByAgentId[agent.Id] = CloneProfile(profile);
            }

            return new CreateAgentFromTemplateResultViewModel { Agent = agent };
        }

        private static AgentOperatingProfileViewModel CloneProfile(AgentOperatingProfileViewModel profile) =>
            new()
            {
                Id = profile.Id,
                CompanyId = profile.CompanyId,
                TemplateId = profile.TemplateId,
                DisplayName = profile.DisplayName,
                RoleName = profile.RoleName,
                Department = profile.Department,
                Seniority = profile.Seniority,
                Status = profile.Status,
                AvatarUrl = profile.AvatarUrl,
                RoleBrief = profile.RoleBrief,
                Objectives = CloneNodes(profile.Objectives),
                Kpis = CloneNodes(profile.Kpis),
                ToolPermissions = CloneNodes(profile.ToolPermissions),
                DataScopes = CloneNodes(profile.DataScopes),
                ApprovalThresholds = CloneNodes(profile.ApprovalThresholds),
                EscalationRules = CloneNodes(profile.EscalationRules),
                TriggerLogic = CloneNodes(profile.TriggerLogic),
                WorkingHours = CloneNodes(profile.WorkingHours),
                UpdatedUtc = profile.UpdatedUtc,
                CanReceiveAssignments = profile.CanReceiveAssignments
            };

        private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes)
        {
            if (nodes is null || nodes.Count == 0)
            {
                return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
            }

            return nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
        }

        private static CompanyAgentSummaryViewModel CloneAgent(CompanyAgentSummaryViewModel agent) =>
            new()
            {
                Id = agent.Id,
                CompanyId = agent.CompanyId,
                TemplateId = agent.TemplateId,
                DisplayName = agent.DisplayName,
                RoleName = agent.RoleName,
                Department = agent.Department,
                Seniority = agent.Seniority,
                Status = agent.Status,
                AvatarUrl = agent.AvatarUrl,
                Personality = agent.Personality
            };

        private static async Task<IReadOnlyList<AgentTemplateCatalogItemViewModel>> LoadTemplatesAsync(HttpClient httpClient, CancellationToken cancellationToken)
        {
            var templates = await httpClient.GetFromJsonAsync<List<AgentTemplateCatalogItemViewModel>>(
                OfflineTemplateCatalogPath,
                SerializerOptions,
                cancellationToken);

            if (templates is null || templates.Count == 0)
            {
                throw new OnboardingApiException("The offline agent template catalog is missing or empty.");
            }

            return templates
                .OrderBy(x => x.RoleName, StringComparer.OrdinalIgnoreCase)
                .Select(x => new AgentTemplateCatalogItemViewModel
                {
                    TemplateId = x.TemplateId,
                    RoleName = x.RoleName,
                    Department = x.Department,
                    PersonaSummary = x.PersonaSummary,
                    DefaultSeniority = x.DefaultSeniority,
                    AvatarUrl = x.AvatarUrl
                })
                .ToList();
        }

        private static bool TryValidateAvatarReference(string? value, out string? error)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                error = null;
                return true;
            }

            var trimmed = value.Trim();
            if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
            {
                error = "AvatarUrl must be an external http/https URL or a file/storage reference, not inline image data.";
                return false;
            }

            if ((trimmed.Contains("://", StringComparison.Ordinal) ||
                 trimmed.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
                 trimmed.StartsWith("https:", StringComparison.OrdinalIgnoreCase)) &&
                (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                error = "AvatarUrl must be a valid absolute http/https URL or a file/storage reference.";
                return false;
            }

            error = null;
            return true;
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

public sealed class AgentOperatingProfileViewModel
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
    public string? RoleBrief { get; set; }
    public Dictionary<string, JsonNode?> Objectives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Kpis { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ToolPermissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> DataScopes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ApprovalThresholds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> EscalationRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> TriggerLogic { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> WorkingHours { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime UpdatedUtc { get; set; }
    public bool CanReceiveAssignments { get; set; }
}

public sealed class UpdateAgentOperatingProfileRequest
{
    public string Status { get; set; } = "active";
    public string? RoleBrief { get; set; }
    public Dictionary<string, JsonNode?> Objectives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Kpis { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ToolPermissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> DataScopes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ApprovalThresholds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> EscalationRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> TriggerLogic { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> WorkingHours { get; set; } = new(StringComparer.OrdinalIgnoreCase);
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
