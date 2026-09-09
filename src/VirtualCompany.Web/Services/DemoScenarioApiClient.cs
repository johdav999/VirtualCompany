using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class DemoScenarioApiClient(
    HttpClient httpClient,
    ICompanyApiTransport transport,
    bool useOfflineMode,
    IApiProblemMessageResolver? problemResolver = null)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DemoScenarioDefinitionViewModel>> ListAsync(CancellationToken cancellationToken = default)
    {
        EnsureOnline();
        return await httpClient.GetFromJsonAsync<List<DemoScenarioDefinitionViewModel>>("api/demo-scenarios", Json, cancellationToken) ?? [];
    }

    public async Task<ProvisionDemoScenarioResultViewModel> ProvisionAsync(
        ProvisionDemoScenarioViewModel request, CancellationToken cancellationToken = default)
    {
        EnsureOnline();
        using var response = await httpClient.PostAsJsonAsync("api/demo-scenarios/provision", request, Json, cancellationToken);
        return await ReadAsync<ProvisionDemoScenarioResultViewModel>(response, cancellationToken)
            ?? throw new DemoScenarioApiException("The demo provisioning API returned an empty response.");
    }

    public Task<DemoScenarioStatusViewModel?> GetCurrentAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendCompanyAsync<DemoScenarioStatusViewModel>(companyId, HttpMethod.Get, "api/demo-scenarios/current", null, true, cancellationToken);

    public Task<DemoScenarioStatusViewModel?> LinkMeetingAsync(Guid companyId,
        LinkDemoScenarioMeetingViewModel request, CancellationToken cancellationToken = default) =>
        SendCompanyAsync<DemoScenarioStatusViewModel>(companyId, HttpMethod.Post, "api/demo-scenarios/current/link-meeting",
            JsonContent.Create(request), false, cancellationToken);

    public Task<DemoScenarioStatusViewModel?> StartAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        SendCompanyAsync<DemoScenarioStatusViewModel>(companyId, HttpMethod.Post, "api/demo-scenarios/current/start",
            JsonContent.Create(new { }), false, cancellationToken);

    public Task<DemoScenarioResetPreviewViewModel?> PreviewResetAsync(Guid companyId, string scenarioKey,
        int scenarioVersion, CancellationToken cancellationToken = default) =>
        SendCompanyAsync<DemoScenarioResetPreviewViewModel>(companyId, HttpMethod.Get,
            $"api/demo-scenarios/current/reset-preview?scenarioKey={Uri.EscapeDataString(scenarioKey)}&scenarioVersion={scenarioVersion}",
            null, false, cancellationToken);

    public Task<DemoScenarioStatusViewModel?> ResetAsync(Guid companyId, ResetDemoScenarioViewModel request,
        CancellationToken cancellationToken = default) =>
        SendCompanyAsync<DemoScenarioStatusViewModel>(companyId, HttpMethod.Post, "api/demo-scenarios/current/reset",
            JsonContent.Create(request), false, cancellationToken);

    public Task<DemoScenarioCommandResultViewModel?> ExecuteAsync(Guid companyId,
        ExecuteDemoScenarioCommandViewModel request, CancellationToken cancellationToken = default) =>
        SendCompanyAsync<DemoScenarioCommandResultViewModel>(companyId, HttpMethod.Post, "api/demo-scenarios/current/commands",
            JsonContent.Create(request), false, cancellationToken);

    private async Task<T?> SendCompanyAsync<T>(Guid companyId, HttpMethod method, string uri, HttpContent? content,
        bool allowNotFound, CancellationToken cancellationToken)
    {
        EnsureOnline();
        try
        {
            using var response = await transport.SendAsync(companyId, method, uri, content, cancellationToken);
            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound) return default;
            return await ReadAsync<T>(response, cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new DemoScenarioApiException("The controlled demo service could not be reached. No demo action was executed.");
        }
    }

    private async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode) throw await CreateExceptionAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken);
    }

    private async Task<DemoScenarioApiException> CreateExceptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json"))
            return new DemoScenarioApiException($"The demo request failed with status code {(int)response.StatusCode}.", response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemResponse>(Json, cancellationToken);
        var fallback = problem?.Errors?.SelectMany(x => x.Value).FirstOrDefault() ?? problem?.Detail ?? problem?.Title ??
            "The demo request was rejected without changing data.";
        return new DemoScenarioApiException(problemResolver?.Resolve(problem, fallback) ?? fallback, response.StatusCode, problem?.Errors);
    }

    private void EnsureOnline()
    {
        if (useOfflineMode)
            throw new DemoScenarioApiException("Controlled demos require the backend API. Offline mode never substitutes mock demo data.");
    }
}

public sealed class DemoScenarioApiException(string message, HttpStatusCode? statusCode = null,
    IReadOnlyDictionary<string, string[]>? errors = null) : Exception(message)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
    public IReadOnlyDictionary<string, string[]>? Errors { get; } = errors;
}

public sealed record DemoScenarioSeededEntityViewModel(string EntityType, string LogicalKey, int Count);
public sealed record DemoScenarioActionViewModel(int StepNumber, string CommandName, string DisplayName, string ExpectedVisibleOutcome);
public sealed record DemoScenarioInitialStateViewModel(string CustomerCompanyName, string CustomerIndustry, string ContactName,
    string ContactEmail, string ContactTitle, string LeadTitle, decimal EstimatedValue, string Currency, DateTime SeedTimestampUtc);
public sealed record DemoScenarioDefinitionViewModel(int SchemaVersion, string ScenarioKey, int ScenarioVersion, string Title,
    string CompanyName, DemoScenarioInitialStateViewModel InitialState, IReadOnlyList<DemoScenarioSeededEntityViewModel> SeededEntities,
    IReadOnlyList<string> AllowedRoles, IReadOnlyList<DemoScenarioActionViewModel> Actions, IReadOnlyList<string> ResetRules,
    IReadOnlyList<string> ValidationChecks, IReadOnlyList<string> DisabledIntegrations);
public sealed record ProvisionDemoScenarioViewModel(string ScenarioKey, int ScenarioVersion, bool ConfirmSyntheticDataOnly);
public sealed record ProvisionDemoScenarioResultViewModel(Guid CompanyId, string CompanyName, Guid RunId, string ScenarioKey,
    int ScenarioVersion, string Status, bool IsDemoTenant, bool ExternalIntegrationsBlocked);
public sealed record LinkDemoScenarioMeetingViewModel(Guid MeetingSessionId, string ScenarioKey, int ScenarioVersion);
public sealed record DemoScenarioAffectedRecordViewModel(string RecordClass, int ExistingCount, int StartingCount);
public sealed record DemoScenarioValidationViewModel(string Code, bool Passed, string Message);
public sealed record DemoScenarioResetPreviewViewModel(Guid CompanyId, string CompanyName, Guid RunId, string ScenarioKey,
    int ScenarioVersion, int ResetGeneration, IReadOnlyList<DemoScenarioAffectedRecordViewModel> AffectedRecords,
    IReadOnlyList<string> DisabledIntegrations, IReadOnlyList<DemoScenarioValidationViewModel> Validations,
    IReadOnlyList<string> ExpectedPostResetInvariants, bool AuditHistoryPreserved, bool CanReset, string PreviewToken);
public sealed record ResetDemoScenarioViewModel(string ScenarioKey, int ScenarioVersion, string ExpectedCompanyName, string PreviewToken);
public sealed record ExecuteDemoScenarioCommandViewModel(string CommandName, string IdempotencyKey);
public sealed record DemoScenarioStatusViewModel(Guid CompanyId, string CompanyName, Guid RunId, string ScenarioKey,
    int ScenarioVersion, Guid? MeetingSessionId, string Status, int CurrentStep, int StepCount, int ResetGeneration,
    bool ExternalIntegrationsBlocked, DemoScenarioActionViewModel? NextAction, IReadOnlyList<DemoScenarioActionViewModel> Actions,
    IReadOnlyList<DemoScenarioValidationViewModel> Validations, DateTime UpdatedUtc);
public sealed record DemoScenarioCommandResultViewModel(string Disposition, string? ReasonCode, string Message,
    string CommandName, int StepNumber, string ExpectedVisibleOutcome, string EntityType, Guid EntityId,
    DemoScenarioStatusViewModel Status);

