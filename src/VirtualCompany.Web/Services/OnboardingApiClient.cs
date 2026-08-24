using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Web.Localization.Formatting;

namespace VirtualCompany.Web.Services;

public sealed class OnboardingApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly OfflineOnboardingStore OfflineStore = new();
    private readonly HttpClient _httpClient;
    private readonly bool _useOfflineMode;
    private readonly ICompanyPresentationContext? _presentationContext;
    private readonly IApiProblemMessageResolver? _problemResolver;

    public OnboardingApiClient(HttpClient httpClient, bool useOfflineMode = false, ICompanyPresentationContext? presentationContext = null, IApiProblemMessageResolver? problemResolver = null)
    {
        _httpClient = httpClient;
        _useOfflineMode = useOfflineMode;
        _presentationContext = presentationContext;
        _problemResolver = problemResolver;
    }

    public Task<IReadOnlyList<OnboardingTemplateViewModel>> GetTemplatesAsync(CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<IReadOnlyList<OnboardingTemplateViewModel>>(OfflineStore.GetTemplates())
            : GetTemplatesCoreAsync(cancellationToken);

    public Task<OnboardingTemplateRecommendationViewModel?> GetRecommendedDefaultsAsync(
        string? industry,
        string? businessType,
        CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.GetRecommendedDefaults(industry, businessType))
            : GetRecommendedDefaultsCoreAsync(industry, businessType, cancellationToken);

    public Task<OnboardingProgressViewModel?> GetProgressAsync(CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.GetProgress())
            : GetProgressCoreAsync(null, cancellationToken);

    public Task<OnboardingProgressViewModel?> GetProgressAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.GetProgress())
            : GetProgressCoreAsync(companyId, cancellationToken);

    public Task<CurrentUserContextViewModel?> GetCurrentUserContextAsync(CancellationToken cancellationToken = default) =>
        GetCurrentUserContextAsync(selectedCompanyId: null, cancellationToken);

    public async Task<CurrentUserContextViewModel?> GetCurrentUserContextAsync(
        Guid? selectedCompanyId,
        CancellationToken cancellationToken = default)
    {
        var context = _useOfflineMode
            ? OfflineStore.GetCurrentUserContext()
            : await GetCurrentUserContextCoreAsync(cancellationToken);
        if (selectedCompanyId is Guid companyId &&
            context?.ActiveCompany?.CompanyId != companyId &&
            context?.Memberships.Any(membership =>
                membership.CompanyId == companyId &&
                string.Equals(membership.Status, "active", StringComparison.OrdinalIgnoreCase)) == true)
        {
            await SelectCompanyAsync(companyId, cancellationToken);
            context = _useOfflineMode
                ? OfflineStore.GetCurrentUserContext()
                : await GetCurrentUserContextCoreAsync(cancellationToken);
        }

        _presentationContext?.SetActiveCompany(context?.ActiveCompany?.Timezone, context?.ActiveCompany?.Currency);
        _presentationContext?.SetFormattingCulture(context?.User.FormattingCulture ?? context?.User.UiCulture);
        return context;
    }

    public Task<CompanySelectionViewModel> SelectCompanyAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.SelectCompany(companyId))
            : SendAsync<CompanySelectionViewModel>(
                HttpMethod.Post,
                "api/auth/select-company",
                new SelectCompanyRequest { CompanyId = companyId },
                cancellationToken);

    public Task<UserPreferenceViewModel> GetUserPreferencesAsync(CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(new UserPreferenceViewModel { UiCulture = "en-GB" })
            : GetUserPreferencesCoreAsync(cancellationToken);

    public Task<UserPreferenceViewModel> UpdateUserPreferencesAsync(
        UpdateUserPreferenceRequest request,
        CancellationToken cancellationToken = default) =>
        SendAsync<UserPreferenceViewModel>(HttpMethod.Put, "api/auth/preferences", request, cancellationToken);

    public Task<CreateCompanyResultViewModel> CreateCompanyAsync(CreateCompanyRequest request, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.CreateCompany(request))
            : SendAsync<CreateCompanyResultViewModel>(HttpMethod.Post, "api/onboarding/company", request, cancellationToken);

    public Task<OnboardingProgressViewModel> CreateWorkspaceAsync(SaveOnboardingRequest request, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.SaveWorkspace(request.ToCreateRequest()))
            : SendAsync<OnboardingProgressViewModel>(HttpMethod.Post, "api/onboarding/workspace", request.ToCreateRequest(), cancellationToken);

    public Task<OnboardingWorkshopBootstrapViewModel> StartWorkshopAsync(SaveOnboardingRequest request,CancellationToken cancellationToken=default)=>
        _useOfflineMode
            ? throw new OnboardingApiException("The guided onboarding workshop requires the backend API.")
            : SendAsync<OnboardingWorkshopBootstrapViewModel>(HttpMethod.Post,"api/onboarding/workshop",request.ToCreateRequest(),cancellationToken);

    public Task<OnboardingProgressViewModel> SaveProgressAsync(SaveOnboardingRequest request, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.SaveProgress(request))
            : SendAsync<OnboardingProgressViewModel>(HttpMethod.Put, "api/onboarding/progress", request, cancellationToken);

    public Task<OnboardingProgressViewModel> AbandonAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.Abandon(companyId))
            : SendAsync<OnboardingProgressViewModel>(HttpMethod.Post, "api/onboarding/abandon", new AbandonOnboardingRequest { CompanyId = companyId }, cancellationToken);

    public Task<CompleteOnboardingResultViewModel> CompleteAsync(CompleteOnboardingRequest request, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.Complete(request))
            : SendAsync<CompleteOnboardingResultViewModel>(HttpMethod.Post, "api/onboarding/complete", request, cancellationToken);

    public Task<CompanyAccessViewModel?> GetCompanyAccessAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.GetCompanyAccess(companyId))
            : GetCompanyAccessCoreAsync(companyId, cancellationToken);

    public Task<CompanyDashboardEntryViewModel?> GetDashboardEntryAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult(OfflineStore.GetDashboardEntry(companyId))
            : GetDashboardEntryCoreAsync(companyId, cancellationToken);

    public Task<DashboardBriefingCardViewModel?> GetLatestBriefingsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<DashboardBriefingCardViewModel?>(new DashboardBriefingCardViewModel())
            : GetLatestBriefingsCoreAsync(companyId, cancellationToken);

    public Task<BriefingPreferenceViewModel?> GetBriefingPreferencesAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        _useOfflineMode
            ? Task.FromResult<BriefingPreferenceViewModel?>(new BriefingPreferenceViewModel { CompanyId = companyId, InAppEnabled = true, DailyEnabled = true, WeeklyEnabled = true })
            : GetBriefingPreferencesCoreAsync(companyId, cancellationToken);

    public Task<BriefingPreferenceViewModel> UpdateBriefingPreferencesAsync(Guid companyId, BriefingPreferenceUpdateRequest request, CancellationToken cancellationToken = default) =>
        SendAsync<BriefingPreferenceViewModel>(HttpMethod.Put, $"api/companies/{companyId}/briefings/preferences", request, cancellationToken);

    private async Task<IReadOnlyList<OnboardingTemplateViewModel>> GetTemplatesCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/onboarding/templates", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await ReadJsonOrDefaultAsync<IReadOnlyList<OnboardingTemplateViewModel>>(response.Content, cancellationToken) ?? [];
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<OnboardingTemplateRecommendationViewModel?> GetRecommendedDefaultsCoreAsync(string? industry, string? businessType, CancellationToken cancellationToken)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(industry))
        {
            query.Add($"industry={Uri.EscapeDataString(industry)}");
        }

        if (!string.IsNullOrWhiteSpace(businessType))
        {
            query.Add($"businessType={Uri.EscapeDataString(businessType)}");
        }

        var uri = query.Count == 0 ? "api/onboarding/recommended-defaults" : $"api/onboarding/recommended-defaults?{string.Join("&", query)}";

        try
        {
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return await ReadJsonOrDefaultAsync<OnboardingTemplateRecommendationViewModel?>(response.Content, cancellationToken);
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<OnboardingProgressViewModel?> GetProgressCoreAsync(Guid? companyId, CancellationToken cancellationToken)
    {
        try
        {
            var uri = companyId.HasValue
                ? $"api/onboarding/progress?companyId={companyId.Value:D}"
                : "api/onboarding/progress";
            using var response = await _httpClient.GetAsync(uri, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentLength is 0)
                {
                    return null;
                }

                return await ReadJsonOrDefaultAsync<OnboardingProgressViewModel?>(response.Content, cancellationToken);
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<CurrentUserContextViewModel?> GetCurrentUserContextCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/auth/me", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (response.IsSuccessStatusCode)
            {
                return await ReadJsonOrDefaultAsync<CurrentUserContextViewModel?>(response.Content, cancellationToken);
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<UserPreferenceViewModel> GetUserPreferencesCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync("api/auth/preferences", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                throw new OnboardingApiException("Sign in to manage your language preference.");
            }

            if (response.IsSuccessStatusCode)
            {
                return await ReadJsonOrDefaultAsync<UserPreferenceViewModel>(response.Content, cancellationToken)
                    ?? new UserPreferenceViewModel();
            }

            throw await CreateExceptionAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<CompanyAccessViewModel?> GetCompanyAccessCoreAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/companies/{companyId}/access", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync(response, cancellationToken);
            }

            return await ReadJsonOrDefaultAsync<CompanyAccessViewModel>(response.Content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<CompanyDashboardEntryViewModel?> GetDashboardEntryCoreAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/companies/{companyId}/dashboard-entry", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden ||
                response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync(response, cancellationToken);
            }

            return await ReadJsonOrDefaultAsync<CompanyDashboardEntryViewModel>(response.Content, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw CreateNetworkException(ex);
        }
    }

    private async Task<DashboardBriefingCardViewModel?> GetLatestBriefingsCoreAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/companies/{companyId}/briefings/latest", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync(response, cancellationToken);
            }

            return await ReadJsonOrDefaultAsync<DashboardBriefingCardViewModel>(response.Content, cancellationToken);
        }
        catch (HttpRequestException ex) { throw CreateNetworkException(ex); }
    }

    private async Task<BriefingPreferenceViewModel?> GetBriefingPreferencesCoreAsync(Guid companyId, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"api/companies/{companyId}/briefings/preferences", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden || response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateExceptionAsync(response, cancellationToken);
            }

            return await ReadJsonOrDefaultAsync<BriefingPreferenceViewModel>(response.Content, cancellationToken);
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
                var result = await ReadJsonOrDefaultAsync<T>(response.Content, cancellationToken);
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

        var problem = await ReadJsonOrDefaultAsync<ApiProblemResponse>(response.Content, cancellationToken);
        if (problem?.Errors is { Count: > 0 })
        {
            return new OnboardingApiException(_problemResolver?.Resolve(problem, "The request failed.") ?? problem.Detail ?? problem.Title ?? "The request failed.", problem.Errors);
        }

        return new OnboardingApiException(_problemResolver?.Resolve(problem, "The request failed.") ?? problem?.Detail ?? problem?.Title ?? "The request failed.");
    }

    private OnboardingApiException CreateNetworkException(HttpRequestException ex)
    {
        var baseAddress = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? "the configured API";
        return new OnboardingApiException($"The web app could not reach the backend API at {baseAddress}. Start the API project or update the web app API base URL.");
    }

    private static async Task<T?> ReadJsonOrDefaultAsync<T>(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is 0)
        {
            return default;
        }

        try
        {
            return await content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private sealed class OfflineOnboardingStore
    {
        private readonly object _sync = new();
        private readonly Guid _userId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        private OfflineDraftState? _draft;
        private OfflineCompletedState? _completed;

        public IReadOnlyList<OnboardingTemplateViewModel> GetTemplates() =>
            [
                new OnboardingTemplateViewModel
                {
                    TemplateId = "general",
                    Name = "General workspace",
                    Description = "Balanced defaults for a new company workspace.",
                    Category = "general",
                    StarterGuidance = DefaultGuidance()
                },
                new OnboardingTemplateViewModel
                {
                    TemplateId = "services",
                    Name = "Services company",
                    Description = "Recommended defaults for service-oriented companies.",
                    Category = "services",
                    Industry = "Services",
                    BusinessType = "Agency",
                    Defaults = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["timezone"] = JsonDocument.Parse("\"Europe/Stockholm\"").RootElement.Clone(),
                        ["currency"] = JsonDocument.Parse("\"SEK\"").RootElement.Clone(),
                        ["language"] = JsonDocument.Parse("\"en\"").RootElement.Clone(),
                        ["complianceRegion"] = JsonDocument.Parse("\"EU\"").RootElement.Clone()
                    },
                    StarterGuidance = DefaultGuidance()
                }
            ];

        public OnboardingTemplateRecommendationViewModel? GetRecommendedDefaults(string? industry, string? businessType)
        {
            if (string.IsNullOrWhiteSpace(industry) && string.IsNullOrWhiteSpace(businessType))
            {
                return null;
            }

            return new OnboardingTemplateRecommendationViewModel
            {
                TemplateId = "services",
                Name = "Services company",
                Description = "Recommended defaults for service-oriented companies.",
                MatchKind = !string.IsNullOrWhiteSpace(industry) && !string.IsNullOrWhiteSpace(businessType)
                    ? "industry_and_business_type"
                    : !string.IsNullOrWhiteSpace(industry)
                        ? "industry"
                        : "business_type",
                Category = "services",
                Industry = string.IsNullOrWhiteSpace(industry) ? "Services" : industry.Trim(),
                BusinessType = string.IsNullOrWhiteSpace(businessType) ? "Agency" : businessType.Trim(),
                Defaults = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["timezone"] = JsonDocument.Parse("\"Europe/Stockholm\"").RootElement.Clone(),
                    ["currency"] = JsonDocument.Parse("\"SEK\"").RootElement.Clone(),
                    ["language"] = JsonDocument.Parse("\"en\"").RootElement.Clone(),
                    ["complianceRegion"] = JsonDocument.Parse("\"EU\"").RootElement.Clone()
                },
                StarterGuidance = DefaultGuidance()
            };
        }

        public OnboardingProgressViewModel? GetProgress()
        {
            lock (_sync)
            {
                return _draft is null ? null : CloneProgress(_draft.Progress);
            }
        }

        public CurrentUserContextViewModel GetCurrentUserContext()
        {
            lock (_sync)
            {
                var memberships = new List<CompanyMembershipViewModel>();
                ResolvedCompanyContextViewModel? activeCompany = null;

                if (_completed is not null)
                {
                    memberships.Add(new CompanyMembershipViewModel
                    {
                        MembershipId = _completed.MembershipId,
                        CompanyId = _completed.CompanyId,
                        CompanyName = _completed.CompanyName,
                        MembershipRole = "owner",
                        Status = "active"
                    });

                    activeCompany = new ResolvedCompanyContextViewModel
                    {
                        MembershipId = _completed.MembershipId,
                        CompanyId = _completed.CompanyId,
                        CompanyName = _completed.CompanyName,
                        MembershipRole = "owner",
                        Status = "active"
                    };
                }

                return new CurrentUserContextViewModel
                {
                    User = new CurrentUserViewModel
                    {
                        Id = _userId,
                        Email = "demo@virtualcompany.local",
                        DisplayName = "Offline Demo User",
                        AuthProvider = "offline",
                        AuthSubject = "offline-demo"
                    },
                    Memberships = memberships,
                    ActiveCompany = activeCompany,
                    CompanySelectionRequired = false
                };
            }
        }

        public CompanySelectionViewModel SelectCompany(Guid companyId)
        {
            var context = GetCurrentUserContext();
            var membership = context.Memberships.SingleOrDefault(item => item.CompanyId == companyId)
                ?? throw new OnboardingApiException("The selected company is unavailable.");
            return new CompanySelectionViewModel
            {
                CompanyId = companyId,
                HeaderName = "X-Company-Id",
                HeaderValue = companyId.ToString(),
                ActiveCompany = new ResolvedCompanyContextViewModel
                {
                    MembershipId = membership.MembershipId,
                    CompanyId = membership.CompanyId,
                    CompanyName = membership.CompanyName,
                    MembershipRole = membership.MembershipRole,
                    Status = membership.Status
                }
            };
        }

        public CreateCompanyResultViewModel CreateCompany(CreateCompanyRequest request)
        {
            var companyId = Guid.NewGuid();
            var companyName = NormalizeRequired(request.Name, "Company name");
            var dashboardPath = $"/dashboard?companyId={companyId}&welcome=onboarding";
            var starterGuidance = DefaultGuidance();

            lock (_sync)
            {
                _draft = new OfflineDraftState
                {
                    Progress = new OnboardingProgressViewModel
                    {
                        CompanyId = companyId,
                        Name = companyName,
                        Industry = request.Industry ?? string.Empty,
                        BusinessType = request.BusinessType ?? string.Empty,
                        Timezone = request.Timezone ?? string.Empty,
                        Currency = request.Currency ?? string.Empty,
                        Language = request.Language ?? string.Empty,
                        ComplianceRegion = request.ComplianceRegion ?? string.Empty,
                        CurrentStep = 3,
                        SelectedTemplateId = request.SelectedTemplateId,
                        Status = "completed",
                        IsCompleted = true,
                        CanResume = false,
                        CompletedUtc = DateTime.UtcNow,
                        StarterGuidance = starterGuidance,
                        DashboardPath = dashboardPath
                    }
                };

                _completed = new OfflineCompletedState
                {
                    CompanyId = companyId,
                    CompanyName = companyName,
                    MembershipId = Guid.NewGuid(),
                    StarterGuidance = starterGuidance,
                    CompletedUtc = DateTime.UtcNow
                };
            }

            return new CreateCompanyResultViewModel
            {
                CompanyId = companyId,
                CompanyName = companyName,
                DashboardPath = dashboardPath,
                StarterGuidance = starterGuidance
            };
        }

        public OnboardingProgressViewModel SaveWorkspace(CreateWorkspaceRequest request)
        {
            lock (_sync)
            {
                var companyId = _draft?.Progress.CompanyId ?? Guid.NewGuid();
                _draft = BuildDraft(
                    companyId,
                    request.Name,
                    request.Industry,
                    request.BusinessType,
                    request.Timezone,
                    request.Currency,
                    request.Language,
                    request.ComplianceRegion,
                    request.CurrentStep,
                    request.SelectedTemplateId);

                return CloneProgress(_draft.Progress);
            }
        }

        public OnboardingProgressViewModel SaveProgress(SaveOnboardingRequest request)
        {
            lock (_sync)
            {
                var companyId = request.CompanyId ?? _draft?.Progress.CompanyId ?? Guid.NewGuid();
                _draft = BuildDraft(
                    companyId,
                    request.Name,
                    request.Industry,
                    request.BusinessType,
                    request.Timezone,
                    request.Currency,
                    request.Language,
                    request.ComplianceRegion,
                    request.CurrentStep,
                    request.SelectedTemplateId);

                return CloneProgress(_draft.Progress);
            }
        }

        public OnboardingProgressViewModel Abandon(Guid companyId)
        {
            lock (_sync)
            {
                var progress = _draft is not null && _draft.Progress.CompanyId == companyId
                    ? CloneProgress(_draft.Progress)
                    : new OnboardingProgressViewModel();

                progress.CompanyId = null;
                progress.CurrentStep = 1;
                progress.Status = "abandoned";
                progress.CanResume = false;
                progress.LastSavedUtc = null;
                progress.AbandonedUtc = DateTime.UtcNow;
                progress.StarterGuidance = DefaultGuidance();
                _draft = new OfflineDraftState { Progress = progress };
                return CloneProgress(progress);
            }
        }

        public CompleteOnboardingResultViewModel Complete(CompleteOnboardingRequest request)
        {
            lock (_sync)
            {
                var companyId = request.CompanyId == Guid.Empty ? (_draft?.Progress.CompanyId ?? Guid.NewGuid()) : request.CompanyId;
                var companyName = NormalizeRequired(request.Name, "Company name");
                var completedUtc = DateTime.UtcNow;
                var guidance = ResolveGuidance(request.SelectedTemplateId);
                var dashboardPath = $"/dashboard?companyId={companyId}&welcome=onboarding";

                _draft = new OfflineDraftState
                {
                    Progress = new OnboardingProgressViewModel
                    {
                        CompanyId = companyId,
                        Name = companyName,
                        Industry = request.Industry,
                        BusinessType = request.BusinessType,
                        Timezone = request.Timezone ?? string.Empty,
                        Currency = request.Currency ?? string.Empty,
                        Language = request.Language ?? string.Empty,
                        ComplianceRegion = request.ComplianceRegion ?? string.Empty,
                        CurrentStep = 3,
                        SelectedTemplateId = request.SelectedTemplateId,
                        Status = "completed",
                        IsCompleted = true,
                        CanResume = false,
                        CompletedUtc = completedUtc,
                        StarterGuidance = guidance,
                        DashboardPath = dashboardPath
                    }
                };

                _completed = new OfflineCompletedState
                {
                    CompanyId = companyId,
                    CompanyName = companyName,
                    MembershipId = Guid.NewGuid(),
                    StarterGuidance = guidance,
                    CompletedUtc = completedUtc
                };

                return new CompleteOnboardingResultViewModel
                {
                    CompanyId = companyId,
                    CompanyName = companyName,
                    DashboardPath = dashboardPath,
                    StarterGuidance = guidance
                };
            }
        }

        public CompanyAccessViewModel? GetCompanyAccess(Guid companyId)
        {
            lock (_sync)
            {
                if (_completed is null || _completed.CompanyId != companyId)
                {
                    return null;
                }

                return new CompanyAccessViewModel
                {
                    CompanyId = companyId,
                    CompanyName = _completed.CompanyName,
                    MembershipRole = "owner",
                    Status = "active"
                };
            }
        }

        public CompanyDashboardEntryViewModel? GetDashboardEntry(Guid companyId)
        {
            lock (_sync)
            {
                if (_completed is null || _completed.CompanyId != companyId)
                {
                    return null;
                }

                return new CompanyDashboardEntryViewModel
                {
                    CompanyId = companyId,
                    CompanyName = _completed.CompanyName,
                    RequiresOnboarding = false,
                    ShowStarterGuidance = true,
                    OnboardingCompletedUtc = _completed.CompletedUtc,
                    StarterGuidance = _completed.StarterGuidance.ToList()
                };
            }
        }

        private OfflineDraftState BuildDraft(Guid companyId, string name, string industry, string businessType, string? timezone, string? currency, string? language, string? complianceRegion, int currentStep, string? selectedTemplateId) =>
            new()
            {
                Progress = new OnboardingProgressViewModel
                {
                    CompanyId = companyId,
                    Name = name,
                    Industry = industry,
                    BusinessType = businessType,
                    Timezone = timezone ?? string.Empty,
                    Currency = currency ?? string.Empty,
                    Language = language ?? string.Empty,
                    ComplianceRegion = complianceRegion ?? string.Empty,
                    CurrentStep = Math.Clamp(currentStep, 1, 3),
                    SelectedTemplateId = selectedTemplateId,
                    Status = "in_progress",
                    IsCompleted = false,
                    CanResume = true,
                    LastSavedUtc = DateTime.UtcNow,
                    StarterGuidance = ResolveGuidance(selectedTemplateId)
                }
            };

        private static string NormalizeRequired(string? value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new OnboardingApiException($"{fieldName} is required.");
            }

            return value.Trim();
        }

        private static List<string> ResolveGuidance(string? templateId) => DefaultGuidance();

        private static List<string> DefaultGuidance() =>
            [
                "Invite teammates after the workspace is live.",
                "Assign one owner for each core workflow.",
                "Upload company policies and documents as workspace knowledge."
            ];

        private static OnboardingProgressViewModel CloneProgress(OnboardingProgressViewModel progress) =>
            new()
            {
                CompanyId = progress.CompanyId,
                Name = progress.Name,
                Industry = progress.Industry,
                BusinessType = progress.BusinessType,
                Timezone = progress.Timezone,
                Currency = progress.Currency,
                Language = progress.Language,
                ComplianceRegion = progress.ComplianceRegion,
                CurrentStep = progress.CurrentStep,
                SelectedTemplateId = progress.SelectedTemplateId,
                Status = progress.Status,
                IsCompleted = progress.IsCompleted,
                CanResume = progress.CanResume,
                LastSavedUtc = progress.LastSavedUtc,
                CompletedUtc = progress.CompletedUtc,
                AbandonedUtc = progress.AbandonedUtc,
                StarterGuidance = progress.StarterGuidance.ToList(),
                DashboardPath = progress.DashboardPath
            };

        private sealed class OfflineDraftState
        {
            public OnboardingProgressViewModel Progress { get; set; } = new();
        }

        private sealed class OfflineCompletedState
        {
            public Guid CompanyId { get; set; }
            public string CompanyName { get; set; } = string.Empty;
            public Guid MembershipId { get; set; }
            public DateTime CompletedUtc { get; set; }
            public List<string> StarterGuidance { get; set; } = [];
        }
    }
}

public sealed class OnboardingApiException : Exception
{
    public OnboardingApiException(string message)
        : this(message, null, null)
    {
    }

    public OnboardingApiException(string message, IReadOnlyDictionary<string, string[]>? errors)
        : this(message, errors, null)
    {
    }

    public OnboardingApiException(string message, IReadOnlyDictionary<string, string[]>? errors, int? statusCode)
        : base(message)
    {
        Errors = errors ?? new Dictionary<string, string[]>();
        StatusCode = statusCode;
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public int? StatusCode { get; }
}

public sealed class OnboardingTemplateViewModel
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Industry { get; set; }
    public string? BusinessType { get; set; }
    public int SortOrder { get; set; }
    public Dictionary<string, JsonElement> Defaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonElement> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> StarterGuidance { get; set; } = [];
}

public sealed class OnboardingTemplateRecommendationViewModel
{
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string MatchKind { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Industry { get; set; }
    public string? BusinessType { get; set; }
    public Dictionary<string, JsonElement> Defaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonElement> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> StarterGuidance { get; set; } = [];
}

public sealed class OnboardingProgressViewModel
{
    public Guid? CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string Timezone { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string ComplianceRegion { get; set; } = string.Empty;
    public int CurrentStep { get; set; }
    public string? SelectedTemplateId { get; set; }
    public string Status { get; set; } = "not_started";
    public bool IsCompleted { get; set; }
    public bool CanResume { get; set; }
    public DateTime? LastSavedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public DateTime? AbandonedUtc { get; set; }
    public List<string> StarterGuidance { get; set; } = [];
    public string? DashboardPath { get; set; }
}

public sealed class OnboardingWorkshopBootstrapViewModel
{
    public Guid CompanyId{get;set;}
    public Guid AgentId{get;set;}
    public Guid SessionId{get;set;}
    public string Route{get;set;}=string.Empty;
    public bool Resumed{get;set;}
}

public sealed class CreateCompanyRequest
{
    public string Name { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
    public string? Language { get; set; }
    public string? ComplianceRegion { get; set; }
    public string? SelectedTemplateId { get; set; }
    public bool ExplicitNewCompany { get; set; }
}

public sealed class SaveOnboardingRequest
{
    public Guid? CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
    public string? Language { get; set; }
    public string? ComplianceRegion { get; set; }
    public int CurrentStep { get; set; }
    public string? SelectedTemplateId { get; set; }
    public bool ExplicitNewCompany { get; set; }

    public CreateWorkspaceRequest ToCreateRequest() =>
        new()
        {
            CompanyId = CompanyId,
            Name = Name,
            Industry = Industry,
            BusinessType = BusinessType,
            Timezone = Timezone,
            Currency = Currency,
            Language = Language,
            ComplianceRegion = ComplianceRegion,
            CurrentStep = CurrentStep,
            SelectedTemplateId = SelectedTemplateId,
            ExplicitNewCompany = ExplicitNewCompany
        };
}

public sealed class CreateWorkspaceRequest
{
    public Guid? CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
    public string? Language { get; set; }
    public string? ComplianceRegion { get; set; }
    public int CurrentStep { get; set; }
    public string? SelectedTemplateId { get; set; }
    public bool ExplicitNewCompany { get; set; }
}

public sealed class CompleteOnboardingRequest
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
    public string? Language { get; set; }
    public string? ComplianceRegion { get; set; }
    public string? SelectedTemplateId { get; set; }
}

public sealed class AbandonOnboardingRequest
{
    public Guid CompanyId { get; set; }
}

public sealed class CreateCompanyResultViewModel
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string DashboardPath { get; set; } = string.Empty;
    public List<string> StarterGuidance { get; set; } = [];
}

public sealed class CompleteOnboardingResultViewModel
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string DashboardPath { get; set; } = string.Empty;
    public List<string> StarterGuidance { get; set; } = [];
}

public sealed class CompanyAccessViewModel
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string MembershipRole { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class CompanyDashboardEntryViewModel
{
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public bool RequiresOnboarding { get; set; }
    public bool ShowStarterGuidance { get; set; }
    public DateTime? OnboardingCompletedUtc { get; set; }
    public List<string> StarterGuidance { get; set; } = [];
}

public sealed class DashboardBriefingCardViewModel
{
    public CompanyBriefingViewModel? Daily { get; set; }
    public CompanyBriefingViewModel? Weekly { get; set; }
}

public sealed class CompanyBriefingViewModel
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public string BriefingType { get; set; } = string.Empty;
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public string Title { get; set; } = string.Empty;
    public string SummaryBody { get; set; } = string.Empty;
    public List<BriefingSourceReferenceViewModel> SourceReferences { get; set; } = [];
    public Guid? MessageId { get; set; }
    public DateTime GeneratedUtc { get; set; }
}

public sealed class BriefingSourceReferenceViewModel
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Status { get; set; }
    public string? Route { get; set; }
}

public sealed class BriefingPreferenceViewModel
{
    public Guid CompanyId { get; set; }
    public Guid UserId { get; set; }
    public bool InAppEnabled { get; set; }
    public bool MobileEnabled { get; set; }
    public bool DailyEnabled { get; set; }
    public bool WeeklyEnabled { get; set; }
    public TimeOnly PreferredDeliveryTime { get; set; } = new(8, 0);
    public string? PreferredTimezone { get; set; }
    public DateTime? UpdatedUtc { get; set; }
}

public sealed class BriefingPreferenceUpdateRequest
{
    public bool InAppEnabled { get; set; }
    public bool MobileEnabled { get; set; }
    public bool DailyEnabled { get; set; }
    public bool WeeklyEnabled { get; set; }
    public TimeOnly? PreferredDeliveryTime { get; set; }
    public string? PreferredTimezone { get; set; }
}

public sealed class CurrentUserContextViewModel
{
    public CurrentUserViewModel User { get; set; } = new();
    public List<CompanyMembershipViewModel> Memberships { get; set; } = [];
    public ResolvedCompanyContextViewModel? ActiveCompany { get; set; }
    public bool CompanySelectionRequired { get; set; }
}

public sealed class SelectCompanyRequest
{
    public Guid CompanyId { get; set; }
}

public sealed class CompanySelectionViewModel
{
    public Guid CompanyId { get; set; }
    public string HeaderName { get; set; } = string.Empty;
    public string HeaderValue { get; set; } = string.Empty;
    public ResolvedCompanyContextViewModel ActiveCompany { get; set; } = new();
}

public sealed class CurrentUserViewModel
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string AuthProvider { get; set; } = string.Empty;
    public string AuthSubject { get; set; } = string.Empty;
    public string? UiCulture { get; set; }
    public string? FormattingCulture { get; set; }
}

public sealed class UserPreferenceViewModel
{
    public string UiCulture { get; set; } = "en-GB";
    public string? FormattingCulture { get; set; }
    public DateTime? UpdatedUtc { get; set; }
}

public sealed class UpdateUserPreferenceRequest
{
    public string UiCulture { get; set; } = "en-GB";
    public string? FormattingCulture { get; set; }
}

public sealed class CompanyMembershipViewModel
{
    public Guid MembershipId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string MembershipRole { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime? CompanyCreatedUtc { get; set; }
    public string? OnboardingStatus { get; set; }
}

public sealed class ResolvedCompanyContextViewModel
{
    public Guid MembershipId { get; set; }
    public Guid CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string MembershipRole { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
}
