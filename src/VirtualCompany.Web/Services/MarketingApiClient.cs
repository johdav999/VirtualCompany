using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class MarketingApiClient
{
    private const string CompanyHeader = "X-Company-Id";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly bool offline;

    public MarketingApiClient(HttpClient httpClient, bool offline)
    {
        this.httpClient = httpClient;
        this.offline = offline;
    }

    public Task<MarketingDashboardViewModel> GetDashboardAsync(Guid companyId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        GetAsync<MarketingDashboardViewModel>(companyId,
            $"api/marketing/dashboard?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}", ct);

    public Task<MarketingObjectiveViewModel> CreateObjectiveAsync(Guid companyId, CreateMarketingObjectiveViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingObjectiveViewModel, MarketingObjectiveViewModel>(companyId, HttpMethod.Post, "api/marketing/objectives", request, ct);
    public Task<MarketingObjectiveViewModel> ActivateObjectiveAsync(Guid companyId, Guid objectiveId, CancellationToken ct = default) =>
        SendAsync<object, MarketingObjectiveViewModel>(companyId, HttpMethod.Post, $"api/marketing/objectives/{objectiveId:D}/activate", new { }, ct);

    public Task<MarketingPlanViewModel> CreatePlanAsync(Guid companyId, CreateMarketingPlanViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingPlanViewModel, MarketingPlanViewModel>(companyId, HttpMethod.Post, "api/marketing/plans", request, ct);
    public Task<MarketingPlanViewModel> ActivatePlanAsync(Guid companyId, Guid planId, CancellationToken ct = default) =>
        SendAsync<object, MarketingPlanViewModel>(companyId, HttpMethod.Post, $"api/marketing/plans/{planId:D}/activate", new { }, ct);

    public Task<MarketingContentBriefViewModel> CreateContentAsync(Guid companyId, CreateMarketingContentBriefViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingContentBriefViewModel, MarketingContentBriefViewModel>(companyId, HttpMethod.Post, "api/marketing/content", request, ct);

    public Task ReviewContentAsync(Guid companyId, Guid briefId, bool approved, CancellationToken ct = default) =>
        SendNoContentAsync(companyId, HttpMethod.Post, $"api/marketing/content/{briefId:D}/review", new { approved }, ct);
    public Task SubmitContentAsync(Guid companyId, Guid briefId, CancellationToken ct = default) =>
        SendNoContentAsync(companyId, HttpMethod.Post, $"api/marketing/content/{briefId:D}/submit", new { }, ct);
    public Task<MarketingContentPreflightViewModel> PreflightContentAsync(Guid companyId, Guid briefId, CancellationToken ct = default) =>
        GetAsync<MarketingContentPreflightViewModel>(companyId, $"api/marketing/content/{briefId:D}/preflight", ct);

    public Task<MarketingQualificationDefinitionViewModel> CreateQualificationDefinitionAsync(
        Guid companyId, CreateMarketingQualificationDefinitionViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingQualificationDefinitionViewModel, MarketingQualificationDefinitionViewModel>(
            companyId, HttpMethod.Post, "api/marketing/qualification-definitions", request, ct);
    public Task<MarketingQualificationDefinitionViewModel> ActivateQualificationDefinitionAsync(
        Guid companyId, Guid definitionId, CancellationToken ct = default) =>
        SendAsync<object, MarketingQualificationDefinitionViewModel>(
            companyId, HttpMethod.Post, $"api/marketing/qualification-definitions/{definitionId:D}/activate", new { }, ct);

    public Task<MarketingSalesHandoffViewModel> DecideHandoffAsync(Guid companyId, Guid handoffId, bool accepted, string reason, CancellationToken ct = default) =>
        SendAsync<object, MarketingSalesHandoffViewModel>(companyId, HttpMethod.Post, $"api/marketing/handoffs/{handoffId:D}/decision",
            new { accepted, reason, leadId = (Guid?)null, dealId = (Guid?)null }, ct);

    public Task<MarketingExperimentViewModel> CreateExperimentAsync(Guid companyId, CreateMarketingExperimentViewModel request, CancellationToken ct = default) =>
        SendAsync<CreateMarketingExperimentViewModel, MarketingExperimentViewModel>(companyId, HttpMethod.Post, "api/marketing/experiments", request, ct);
    public Task<MarketingExperimentViewModel> ActivateExperimentAsync(Guid companyId, Guid experimentId, CancellationToken ct = default) =>
        SendAsync<object, MarketingExperimentViewModel>(companyId, HttpMethod.Post, $"api/marketing/experiments/{experimentId:D}/activate", new { }, ct);
    public Task<MarketingExperimentViewModel> CompleteExperimentAsync(Guid companyId, Guid experimentId, string decision, CancellationToken ct = default) =>
        SendAsync<object, MarketingExperimentViewModel>(companyId, HttpMethod.Post, $"api/marketing/experiments/{experimentId:D}/complete", new { decision }, ct);

    public Task<RoleAgentAnalysisViewModel> AnalyzeAsync(Guid companyId, Guid agentId, string objective, CancellationToken ct = default) =>
        SendAsync<object, RoleAgentAnalysisViewModel>(companyId, HttpMethod.Post, $"api/marketing/agents/{agentId:D}/analysis",
            new { analysisType = "marketing-operating-review", horizonDays = 30, objective }, ct);

    private async Task<T> GetAsync<T>(Guid companyId, string uri, CancellationToken ct)
    {
        EnsureOnline();
        using var request = CreateRequest(companyId, HttpMethod.Get, uri, null);
        using var response = await httpClient.SendAsync(request, ct);
        return await ReadAsync<T>(response, ct);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(Guid companyId, HttpMethod method, string uri, TRequest payload, CancellationToken ct)
    {
        EnsureOnline();
        using var request = CreateRequest(companyId, method, uri, JsonContent.Create(payload, options: JsonOptions));
        using var response = await httpClient.SendAsync(request, ct);
        return await ReadAsync<TResponse>(response, ct);
    }

    private async Task SendNoContentAsync<TRequest>(Guid companyId, HttpMethod method, string uri, TRequest payload, CancellationToken ct)
    {
        EnsureOnline();
        using var request = CreateRequest(companyId, method, uri, JsonContent.Create(payload, options: JsonOptions));
        using var response = await httpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new MarketingApiException(await ReadProblemAsync(response, ct));
        }
    }

    private static HttpRequestMessage CreateRequest(Guid companyId, HttpMethod method, string uri, HttpContent? content)
    {
        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Add(CompanyHeader, companyId.ToString("D"));
        return request;
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new MarketingApiException(await ReadProblemAsync(response, ct));
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
            ?? throw new MarketingApiException("The marketing API returned an empty response.");
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<MarketingProblem>(JsonOptions, ct);
            return problem?.Detail ?? problem?.Title ?? $"Marketing request failed ({(int)response.StatusCode}).";
        }
        catch (JsonException)
        {
            return $"Marketing request failed ({(int)response.StatusCode}).";
        }
    }

    private void EnsureOnline()
    {
        if (offline)
        {
            throw new MarketingApiException("Marketing needs the backend API. Start the API to use live company data.");
        }
    }

    private sealed record MarketingProblem(string? Title, string? Detail);
}

public sealed class MarketingApiException(string message) : Exception(message);
public sealed record MarketingDashboardViewModel(Guid CompanyId, DateTime GeneratedUtc,
    IReadOnlyList<MarketingMetricViewModel> Metrics, IReadOnlyList<MarketingObjectiveViewModel> Objectives,
    IReadOnlyList<MarketingPlanViewModel> Plans, IReadOnlyList<MarketingCalendarItemViewModel> Calendar,
    IReadOnlyList<MarketingContentBriefViewModel> Content, IReadOnlyList<MarketingSalesHandoffViewModel> Handoffs,
    IReadOnlyList<MarketingExperimentViewModel> Experiments,
    IReadOnlyList<MarketingQualificationDefinitionViewModel> QualificationDefinitions,
    IReadOnlyList<MarketingQualificationEvaluationViewModel> QualificationEvaluations);
public sealed record MarketingMetricViewModel(string Name, decimal? Value, string Unit, string State, string Explanation);
public sealed record MarketingObjectiveViewModel(Guid Id, string Name, string ObjectiveType, decimal TargetValue,
    string Unit, decimal? BaselineValue, DateTime PeriodStartUtc, DateTime PeriodEndUtc, string Status, int Version);
public sealed record MarketingPlanViewModel(Guid Id, string Name, string Summary, DateTime StartsUtc, DateTime EndsUtc,
    decimal? PlannedBudget, string BudgetCurrency, string Status, int Version);
public sealed record MarketingCalendarItemViewModel(Guid Id, string Kind, string Name, DateTime StartsUtc, DateTime EndsUtc,
    string Status, Guid? CampaignId, Guid? OwnerAgentId);
public sealed record MarketingContentBriefViewModel(Guid Id, Guid? CampaignId, Guid? PlanId, string Title, string Purpose,
    string Audience, string Channel, string Language, string Tone, string CallToAction, DateTime? DueUtc,
    string Status, int Version, IReadOnlyList<MarketingContentVariantViewModel> Variants);
public sealed record MarketingContentVariantViewModel(Guid Id, string Name, string Body, string SourceReferences,
    bool GeneratedByAi, string Status, DateTime CreatedUtc);
public sealed record MarketingSalesHandoffViewModel(Guid Id, Guid? CampaignId, Guid? ContactId, Guid? CustomerCompanyId,
    Guid? LinkedLeadId, Guid? LinkedDealId, string Reason, string SuggestedAction, string Urgency,
    DateTime ExpiresUtc, string EvidenceReferences, string Status, string? DecisionReason, DateTime UpdatedUtc);
public sealed record MarketingExperimentViewModel(Guid Id, Guid? CampaignId, string Name, string Hypothesis,
    string PrimaryMetric, string GuardrailMetric, int MinimumSampleSize, DateTime StartsUtc, DateTime EndsUtc,
    string Status, string? Decision);
public sealed record MarketingContentPreflightViewModel(Guid BriefId, bool ReadyForReview,
    IReadOnlyList<MarketingContentPreflightIssueViewModel> Issues);
public sealed record MarketingContentPreflightIssueViewModel(string Code, string Severity, string Explanation,
    string? Field = null);
public sealed record MarketingQualificationDefinitionViewModel(Guid Id, string Name, string AudienceType,
    string Channel, decimal MinimumScore, int FreshnessDays, bool RequiresCustomerCompany,
    DateTime EffectiveFromUtc, DateTime? EffectiveToUtc, string Status, int Version);
public sealed record MarketingQualificationEvaluationViewModel(Guid Id, Guid DefinitionId, int DefinitionVersion,
    Guid ContactId, decimal Score, string Status, string ReasonCodesJson, DateTime ObservedUtc);
public sealed record CreateMarketingObjectiveViewModel(string Name, string ObjectiveType, decimal TargetValue, string Unit,
    DateTime PeriodStartUtc, DateTime PeriodEndUtc, decimal? BaselineValue = null);
public sealed record CreateMarketingPlanViewModel(string Name, string Summary, DateTime StartsUtc, DateTime EndsUtc,
    decimal? PlannedBudget, string BudgetCurrency, IReadOnlyList<Guid>? ObjectiveIds = null);
public sealed record CreateMarketingContentBriefViewModel(Guid? CampaignId, Guid? PlanId, string Title, string Purpose,
    string Audience, string Channel, string Language, string Tone, string CallToAction, DateTime? DueUtc);
public sealed record CreateMarketingExperimentViewModel(Guid? CampaignId, string Name, string Hypothesis,
    string PrimaryMetric, string GuardrailMetric, int MinimumSampleSize, DateTime StartsUtc, DateTime EndsUtc);
public sealed record CreateMarketingQualificationDefinitionViewModel(string Name, string AudienceType,
    string Channel, decimal MinimumScore, int FreshnessDays, bool RequiresCustomerCompany,
    DateTime EffectiveFromUtc, DateTime? EffectiveToUtc = null, string RulesJson = "{}", string ExclusionsJson = "{}");
