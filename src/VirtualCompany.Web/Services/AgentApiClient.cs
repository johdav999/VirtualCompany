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

    public Task<AgentRosterResponseViewModel> GetRosterViewAsync(
        Guid companyId,
        string? department = null,
        string? status = null,
        CancellationToken cancellationToken = default)
    {
        if (_useOfflineMode)
        {
            return Task.FromResult(OfflineStore.GetRosterView(companyId, department, status));
        }

        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(department))
        {
            query.Add($"department={Uri.EscapeDataString(department)}");
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            query.Add($"status={Uri.EscapeDataString(status)}");
        }

        var uri = query.Count == 0 ? $"api/companies/{companyId}/agents/roster" : $"api/companies/{companyId}/agents/roster?{string.Join("&", query)}";
        return GetAsync<AgentRosterResponseViewModel>(uri, cancellationToken);
    }

    public Task<AgentStatusCardsResponseViewModel> GetStatusCardsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.GetStatusCards(companyId))
            : GetAsync<AgentStatusCardsResponseViewModel>($"api/companies/{companyId}/agents/status-cards", cancellationToken);

    public Task<AgentStatusDetailViewModel> GetStatusDetailAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.GetStatusDetail(companyId, agentId))
            : GetAsync<AgentStatusDetailViewModel>($"api/companies/{companyId}/agents/{agentId}/status-detail", cancellationToken);

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

    public Task<AgentProfileViewModel> GetProfileViewAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? OfflineStore.GetProfileViewAsync(companyId, agentId, cancellationToken)
            : GetAsync<AgentProfileViewModel>($"api/companies/{companyId}/agents/{agentId}", cancellationToken);

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
        private static readonly HashSet<string> SupportedStatuses = new(StringComparer.OrdinalIgnoreCase)
        {
            "active",
            "paused",
            "restricted",
            "archived"
        };
        private static readonly IReadOnlyList<string> SupportedStatusOptions =
            ["active", "paused", "restricted", "archived"];
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

        public AgentRosterResponseViewModel GetRosterView(Guid companyId, string? department, string? status)
        {
            lock (_sync)
            {
                var allItems = GetRoster(companyId)
                    .Select(agent => new AgentRosterItemViewModel
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
                        Personality = agent.Personality,
                        AutonomyLevel = "level_0",
                        ProfileRoute = BuildProfileRoute(agent.CompanyId, agent.Id),
                        WorkloadSummary = new AgentWorkloadSummaryViewModel
                        {
                            LastActivityUtc = _profilesByAgentId.TryGetValue(agent.Id, out var profile) ? profile.UpdatedUtc : DateTime.UtcNow,
                            HealthSummary = BuildOfflineHealthSummary(agent.Status, _profilesByAgentId.TryGetValue(agent.Id, out profile) ? profile.UpdatedUtc : DateTime.UtcNow),
                            HealthStatus = BuildOfflineHealthSummary(agent.Status, _profilesByAgentId.TryGetValue(agent.Id, out profile) ? profile.UpdatedUtc : DateTime.UtcNow).Status,
                            Summary = BuildOfflineSummaryText(agent.Status, _profilesByAgentId.TryGetValue(agent.Id, out profile) ? profile.UpdatedUtc : DateTime.UtcNow)
                        }
                    })
                    .ToList();

                return new AgentRosterResponseViewModel
                {
                    Items = allItems.Where(x => MatchesFilter(x.Department, department) && MatchesFilter(x.Status, status)).ToList(),
                    Departments = allItems.Select(x => x.Department).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                    Statuses = SupportedStatusOptions.ToList()
                };
            }
        }

        public AgentStatusCardsResponseViewModel GetStatusCards(Guid companyId)
        {
            lock (_sync)
            {
                var generatedUtc = DateTime.UtcNow;
                var cards = GetRoster(companyId)
                    .Select(agent => new AgentStatusCardViewModel
                    {
                        AgentId = agent.Id,
                        CompanyId = agent.CompanyId,
                        DisplayName = agent.DisplayName,
                        RoleName = agent.RoleName,
                        Department = agent.Department,
                        Workload = new AgentStatusWorkloadViewModel
                        {
                            ActiveTaskCount = 0,
                            BlockedTaskCount = 0,
                            AwaitingApprovalCount = 0,
                            ActiveWorkflowCount = 0,
                            WorkloadLevel = "Idle"
                        },
                        HealthStatus = "Healthy",
                        HealthReasons = ["Offline mode cannot inspect live task, workflow, or alert state."],
                        ActiveAlertsCount = 0,
                        RecentActions = [],
                        LastUpdatedUtc = _profilesByAgentId.TryGetValue(agent.Id, out var profile) ? profile.UpdatedUtc : generatedUtc,
                        DetailLink = new AgentStatusDetailLinkViewModel
                        {
                            Path = $"/agents/{agent.Id}",
                            ActiveTab = "work",
                            Query = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["companyId"] = companyId.ToString("D"),
                                ["show"] = "active",
                                ["include"] = "tasks,workflows,alerts"
                            }
                        }
                    })
                    .ToList();

                return new AgentStatusCardsResponseViewModel
                {
                    Items = cards,
                    GeneratedUtc = generatedUtc
                };
            }
        }

        public AgentStatusDetailViewModel GetStatusDetail(Guid companyId, Guid agentId)
        {
            var card = GetStatusCards(companyId).Items.SingleOrDefault(x => x.AgentId == agentId);
            if (card is null)
            {
                throw new OnboardingApiException("The selected agent status could not be loaded.");
            }

            return new AgentStatusDetailViewModel
            {
                AgentId = card.AgentId,
                CompanyId = card.CompanyId,
                DisplayName = card.DisplayName,
                RoleName = card.RoleName,
                Department = card.Department,
                Workload = card.Workload,
                Health = new AgentStatusHealthBreakdownViewModel { Status = card.HealthStatus, Reasons = card.HealthReasons, Metrics = new AgentStatusHealthMetricsViewModel() },
                ActiveAlertsCount = card.ActiveAlertsCount,
                RecentActions = card.RecentActions,
                LastUpdatedUtc = card.LastUpdatedUtc,
                GeneratedUtc = DateTime.UtcNow
            };
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

        public Task<AgentProfileViewModel> GetProfileViewAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken)
        {
            lock (_sync)
            {
                if (!_profilesByAgentId.TryGetValue(agentId, out var profile) || profile.CompanyId != companyId)
                {
                    throw new OnboardingApiException("The selected agent profile could not be loaded.");
                }

                return Task.FromResult(new AgentProfileViewModel
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
                    Personality = profile.RoleBrief ?? string.Empty,
                    RoleBrief = profile.RoleBrief,
                    Objectives = CloneNodes(profile.Objectives),
                    Kpis = CloneNodes(profile.Kpis),
                    ToolPermissions = CloneNodes(profile.ToolPermissions),
                    DataScopes = CloneNodes(profile.DataScopes),
                    ApprovalThresholds = CloneNodes(profile.ApprovalThresholds),
                    EscalationRules = CloneNodes(profile.EscalationRules),
                    WorkingHours = CloneNodes(profile.WorkingHours),
                    UpdatedUtc = profile.UpdatedUtc,
                    AutonomyLevel = profile.AutonomyLevel,
                    Visibility = new AgentProfileVisibilityViewModel
                    {
                        CanViewPermissions = true,
                        CanViewThresholds = true,
                        CanViewWorkingHours = true,
                        CanEditAgent = true,
                        CanEditRoleBrief = true,
                        CanEditObjectives = true,
                        CanEditKpis = true,
                        CanEditWorkingHours = true,
                        CanEditStatus = true,
                        CanEditSensitiveGovernance = true,
                        CanPauseOrRestrictAgent = true
                    },
                    WorkloadSummary = new AgentWorkloadSummaryViewModel
                    {
                        LastActivityUtc = profile.UpdatedUtc == default ? null : profile.UpdatedUtc,
                        HealthSummary = BuildOfflineHealthSummary(profile.Status, profile.UpdatedUtc == default ? null : profile.UpdatedUtc),
                        HealthStatus = BuildOfflineHealthSummary(profile.Status, profile.UpdatedUtc == default ? null : profile.UpdatedUtc).Status,
                        Summary = BuildOfflineSummaryText(profile.Status, profile.UpdatedUtc == default ? null : profile.UpdatedUtc)
                    },
                    ProfileRoute = BuildProfileRoute(profile.CompanyId, profile.Id),
                    Sections = BuildProfileSections(),
                    AnalyticsPreview = new AgentProfileAnalyticsPreviewViewModel
                    {
                        SectionId = "analytics",
                        Heading = "Profile analytics",
                        Description = "Future KPI, workload, health, and trend modules should extend the profile page instead of branching to a separate destination.",
                        PlannedModules =
                        [
                            "Workload and health summary",
                            "Recent activity rollups",
                            "KPI and metrics panels",
                            "Trend and analytics modules"
                        ]
                    },
                });
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

                existing.Status = NormalizeStatus(request.Status);
                existing.RoleBrief = string.IsNullOrWhiteSpace(request.RoleBrief) ? null : request.RoleBrief.Trim();
                existing.AutonomyLevel = string.IsNullOrWhiteSpace(request.AutonomyLevel) ? existing.AutonomyLevel : request.AutonomyLevel.Trim();
                existing.Objectives = request.Objectives is null ? existing.Objectives : CloneNodes(request.Objectives);
                existing.Kpis = request.Kpis is null ? existing.Kpis : CloneNodes(request.Kpis);
                existing.ToolPermissions = request.ToolPermissions is null ? existing.ToolPermissions : CloneNodes(request.ToolPermissions);
                existing.DataScopes = request.DataScopes is null ? existing.DataScopes : CloneNodes(request.DataScopes);
                existing.ApprovalThresholds = request.ApprovalThresholds is null ? existing.ApprovalThresholds : CloneNodes(request.ApprovalThresholds);
                existing.EscalationRules = request.EscalationRules is null ? existing.EscalationRules : CloneNodes(request.EscalationRules);
                existing.TriggerLogic = request.TriggerLogic is null ? existing.TriggerLogic : CloneNodes(request.TriggerLogic);
                existing.WorkingHours = request.WorkingHours is null ? existing.WorkingHours : CloneNodes(request.WorkingHours);
                existing.UpdatedUtc = DateTime.UtcNow;
                existing.CanReceiveAssignments =
                    !string.Equals(existing.Status, "paused", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(existing.Status, "archived", StringComparison.OrdinalIgnoreCase);

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
                CanReceiveAssignments = profile.CanReceiveAssignments,
                Visibility = profile.Visibility,
                AutonomyLevel = profile.AutonomyLevel
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

        private static string NormalizeStatus(string? status)
        {
            if (string.IsNullOrWhiteSpace(status) || !SupportedStatuses.Contains(status.Trim()))
            {
                throw new OnboardingApiException(
                    "Status must be one of active, paused, restricted, or archived.",
                    new Dictionary<string, string[]>
                    {
                        [nameof(UpdateAgentOperatingProfileRequest.Status)] =
                        [
                            "Status must be one of active, paused, restricted, or archived."
                        ]
                    });
            }

            return status.Trim().ToLowerInvariant();
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

        private static string BuildProfileRoute(Guid companyId, Guid agentId) =>
            $"/agents/{agentId}?companyId={companyId}";

        private static List<AgentProfileSectionViewModel> BuildProfileSections() =>
            [
                new() { Id = "overview", Title = "Overview", Description = "Identity, current workload, and health summary.", IsAvailable = true },
                new() { Id = "objectives", Title = "Objectives", Description = "Current objectives and operating intent.", IsAvailable = true },
                new() { Id = "permissions", Title = "Permissions", Description = "Tool permissions and data scopes.", IsAvailable = true },
                new() { Id = "thresholds", Title = "Thresholds", Description = "Approval thresholds and escalation rules.", IsAvailable = true },
                new() { Id = "working-hours", Title = "Working hours", Description = "Availability and coverage windows.", IsAvailable = true },
                new() { Id = "recent-activity", Title = "Recent activity", Description = "Latest executions, approvals, and tenant-scoped events.", IsAvailable = true },
                new() { Id = "analytics", Title = "Analytics", Description = "Reserved profile surface for future KPI, health, and trend modules.", IsAvailable = true }
            ];

        private static AgentHealthSummaryViewModel BuildOfflineHealthSummary(string status, DateTime? lastActivityUtc)
        {
            if (string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentHealthSummaryViewModel
                {
                    Status = "inactive",
                    Label = "Inactive",
                    Reason = "Offline mode only has the saved operating profile timestamp available."
                };
            }

            if (string.Equals(status, "paused", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "restricted", StringComparison.OrdinalIgnoreCase))
            {
                return new AgentHealthSummaryViewModel
                {
                    Status = "needs_attention",
                    Label = "Needs attention",
                    Reason = "Offline mode cannot inspect live task state for this agent."
                };
            }

            if (lastActivityUtc is null)
            {
                return new AgentHealthSummaryViewModel
                {
                    Status = "inactive",
                    Label = "Inactive",
                    Reason = "Offline mode has not recorded any recent activity yet."
                };
            }

            return new AgentHealthSummaryViewModel
            {
                Status = "healthy",
                Label = "Healthy",
                Reason = "Offline mode uses the saved operating profile timestamp as the activity source."
            };
        }

        private static string BuildOfflineSummaryText(string status, DateTime? lastActivityUtc) =>
            $"{BuildOfflineHealthSummary(status, lastActivityUtc).Label} - {BuildOfflineHealthSummary(status, lastActivityUtc).Reason}";

        private static bool MatchesFilter(string value, string? filter) =>
            string.IsNullOrWhiteSpace(filter) ||
            string.Equals(value, filter.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class AgentRosterResponseViewModel
{
    public List<AgentRosterItemViewModel> Items { get; set; } = [];
    public List<string> Departments { get; set; } = [];
    public List<string> Statuses { get; set; } = [];
}

public sealed class AgentRosterItemViewModel
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
    public string AutonomyLevel { get; set; } = string.Empty;
    public AgentWorkloadSummaryViewModel WorkloadSummary { get; set; } = new();
    public string? ProfileRoute { get; set; }
}

public sealed class AgentWorkloadSummaryViewModel
{
    public int OpenItemsCount { get; set; }
    public int AwaitingApprovalCount { get; set; }
    public int ExecutedCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime? LastActivityUtc { get; set; }
    public AgentHealthSummaryViewModel HealthSummary { get; set; } = new();
    public string HealthStatus { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed class AgentStatusCardsResponseViewModel
{
    public List<AgentStatusCardViewModel> Items { get; set; } = [];
    public DateTime GeneratedUtc { get; set; }
}

public sealed class AgentStatusCardViewModel
{
    public Guid AgentId { get; set; }
    public Guid CompanyId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public AgentStatusWorkloadViewModel Workload { get; set; } = new();
    public string HealthStatus { get; set; } = string.Empty;
    public List<string> HealthReasons { get; set; } = [];
    public int ActiveAlertsCount { get; set; }
    public List<AgentStatusRecentActionViewModel> RecentActions { get; set; } = [];
    public DateTime LastUpdatedUtc { get; set; }
    public AgentStatusDetailLinkViewModel DetailLink { get; set; } = new();
}

public sealed class AgentStatusWorkloadViewModel
{
    public int ActiveTaskCount { get; set; }
    public int BlockedTaskCount { get; set; }
    public int AwaitingApprovalCount { get; set; }
    public int ActiveWorkflowCount { get; set; }
    public string WorkloadLevel { get; set; } = string.Empty;
}

public sealed class AgentStatusRecentActionViewModel
{
    public DateTime OccurredUtc { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string RelatedEntityType { get; set; } = string.Empty;
    public Guid? RelatedEntityId { get; set; }
}

public sealed class AgentStatusDetailLinkViewModel
{
    public string Path { get; set; } = string.Empty;
    public string ActiveTab { get; set; } = string.Empty;
    public Dictionary<string, string> Query { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentStatusDetailViewModel
{
    public Guid AgentId { get; set; }
    public Guid CompanyId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public AgentStatusWorkloadViewModel Workload { get; set; } = new();
    public AgentStatusHealthBreakdownViewModel Health { get; set; } = new();
    public int ActiveAlertsCount { get; set; }
    public List<AgentStatusDetailTaskViewModel> ActiveTasks { get; set; } = [];
    public List<AgentStatusDetailWorkflowViewModel> ActiveWorkflows { get; set; } = [];
    public List<AgentStatusDetailAlertViewModel> ActiveAlerts { get; set; } = [];
    public List<AgentStatusRecentActionViewModel> RecentActions { get; set; } = [];
    public DateTime LastUpdatedUtc { get; set; }
    public DateTime GeneratedUtc { get; set; }
}

public sealed class AgentStatusHealthBreakdownViewModel
{
    public string Status { get; set; } = string.Empty;
    public List<string> Reasons { get; set; } = [];
    public AgentStatusHealthMetricsViewModel Metrics { get; set; } = new();
}

public sealed class AgentStatusHealthMetricsViewModel
{
    public int FailedRunCount { get; set; }
    public int StalledWorkCount { get; set; }
    public int PolicyViolationCount { get; set; }
}

public sealed class AgentStatusDetailTaskViewModel
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? DueUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class AgentStatusDetailWorkflowViewModel
{
    public Guid Id { get; set; }
    public string DefinitionName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? CurrentStep { get; set; }
    public DateTime StartedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class AgentStatusDetailAlertViewModel
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime UpdatedUtc { get; set; }
}

public sealed class AgentHealthSummaryViewModel
{
    public string Status { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class AgentRecentActivityViewModel
{

    public DateTime OccurredUtc { get; set; }
    public string ActivityType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Detail { get; set; }
}

public sealed class AgentProfileVisibilityViewModel
{
    public bool CanViewPermissions { get; set; }
    public bool CanViewThresholds { get; set; }
    public bool CanViewWorkingHours { get; set; }
    public bool CanEditAgent { get; set; }
    public bool CanEditRoleBrief { get; set; }
    public bool CanEditObjectives { get; set; }
    public bool CanEditKpis { get; set; }
    public bool CanEditWorkingHours { get; set; }
    public bool CanEditStatus { get; set; }
    public bool CanEditSensitiveGovernance { get; set; }
    public bool CanPauseOrRestrictAgent { get; set; }
}

public sealed class AgentProfileSectionViewModel
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
}

public sealed class AgentProfileAnalyticsPreviewViewModel
{
    public string SectionId { get; set; } = "analytics";
    public string Heading { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> PlannedModules { get; set; } = [];
}

public sealed class AgentProfileViewModel
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
    public string? RoleBrief { get; set; }
    public string AutonomyLevel { get; set; } = string.Empty;
    public Dictionary<string, JsonNode?> Objectives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Kpis { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ToolPermissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> DataScopes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> ApprovalThresholds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> EscalationRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> WorkingHours { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public AgentWorkloadSummaryViewModel WorkloadSummary { get; set; } = new();
    public List<AgentRecentActivityViewModel> RecentActivity { get; set; } = [];
    public AgentProfileVisibilityViewModel Visibility { get; set; } = new();
    public string ProfileRoute { get; set; } = string.Empty;
    public List<AgentProfileSectionViewModel> Sections { get; set; } = [];
    public AgentProfileAnalyticsPreviewViewModel AnalyticsPreview { get; set; } = new();
    public DateTime UpdatedUtc { get; set; }
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
    public AgentProfileVisibilityViewModel Visibility { get; set; } = new();
    public DateTime UpdatedUtc { get; set; }
    public bool CanReceiveAssignments { get; set; }
    public string AutonomyLevel { get; set; } = string.Empty;
}

public sealed class UpdateAgentOperatingProfileRequest
{
    public string Status { get; set; } = "active";
    public string? RoleBrief { get; set; }
    public string? AutonomyLevel { get; set; }
    public Dictionary<string, JsonNode?>? Objectives { get; set; }
    public Dictionary<string, JsonNode?>? Kpis { get; set; }
    public Dictionary<string, JsonNode?>? ToolPermissions { get; set; }
    public Dictionary<string, JsonNode?>? DataScopes { get; set; }
    public Dictionary<string, JsonNode?>? ApprovalThresholds { get; set; }
    public Dictionary<string, JsonNode?>? EscalationRules { get; set; }
    public Dictionary<string, JsonNode?>? TriggerLogic { get; set; }
    public Dictionary<string, JsonNode?>? WorkingHours { get; set; }
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
