using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class AgentWorkApiClient(ICompanyApiTransport transport, bool useOfflineMode = false)
{
    public async Task<BusinessWorkEvidenceDto?> BusinessAsync(Guid companyId, string kind, Guid id, CancellationToken token = default)
    {
        if (id == Guid.Empty || !new[] { "deal", "case", "invoice", "bill", "campaign", "brief" }.Contains(kind)) throw new ArgumentException("Invalid business identity.");
        var result = await ReadAsync<BusinessWorkEvidenceDto>(companyId, $"api/companies/{companyId:D}/agent-work/business/{kind}/{id:D}", token);
        if (result is not null && (result.CompanyId != companyId || result.RecordKind != kind || result.RecordId != id ||
            result.Artifacts is null || result.Work is null || result.Collaboration is null || result.Diagnostics is null || result.Decisions is null ||
            result.Decisions.Any(x => !result.Work.Any(w => w.Id == x.TaskId) || DecisionReviewRoutes.Local(x.Route, companyId) is null) || result.Work.Any(x => !Usable(x)) ||
            result.Artifacts.Any(x => x.Id == Guid.Empty || x.Diagnostics is null || AgentWorkRoutes.Local(x.RecordRoute, companyId) is null || x.DecisionRoute is not null && DecisionReviewRoutes.Local(x.DecisionRoute, companyId) is null) ||
            result.Collaboration.Any(x => x.CompanyId != companyId || !result.Work.Any(w => w.Id == x.WorkId) || x.Artifacts is null || x.Handoffs is null || x.Diagnostics is null ||
                x.Handoffs.Any(h => !x.Artifacts.Any(a => a.Id == h.InputId) || !x.Artifacts.Any(a => a.Id == h.RecipientId)))))
            throw new OnboardingApiException("Business work returned incomplete or mismatched evidence. Refresh to retry.");
        return result;
    }
    public async Task<CollaborationEvidenceDto?> CollaborationAsync(Guid companyId, string kind, Guid id, CancellationToken token = default)
    {
        if (id == Guid.Empty || !new[] { "task", "initiative", "case", "deal" }.Contains(kind)) throw new ArgumentException("Invalid work identity.");
        var result = await ReadAsync<CollaborationEvidenceDto>(companyId, $"api/companies/{companyId:D}/agent-work/{kind}/{id:D}/collaboration", token);
        if (result is not null && (result.CompanyId != companyId || result.Kind != kind || result.WorkId != id ||
            result.Artifacts is null || result.Handoffs is null || result.RelatedRecords is null || result.Diagnostics is null ||
            result.Artifacts.Select(x => x.Id).Distinct().Count() != result.Artifacts.Count ||
            result.Artifacts.Any(x => x.Agent is null || x.InputIds is null || x.Sources is null || x.Version < 1) ||
            result.Handoffs.Any(x => !result.Artifacts.Any(a => a.Id == x.InputId) || !result.Artifacts.Any(a => a.Id == x.RecipientId))))
            throw new OnboardingApiException("Collaboration returned incomplete or mismatched evidence. Refresh to retry.");
        return result;
    }
    public async Task<AgentWorkBoardDto?> ListAsync(AgentWorkQuery query, CancellationToken token = default)
    {
        if (query.Skip < 0 || query.Take is < 1 or > 100) throw new ArgumentException("Invalid work page.");
        var values = new Dictionary<string, string?> { ["responsibility"] = query.Responsibility, ["agentId"] = query.AgentId?.ToString("D"),
            ["objective"] = query.Objective, ["state"] = query.State, ["skip"] = query.Skip.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["take"] = query.Take.ToString(System.Globalization.CultureInfo.InvariantCulture), ["perState"] = query.PerState ? "true" : null };
        var qs = string.Join('&', values.Where(x => !string.IsNullOrEmpty(x.Value)).Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value!)}"));
        var result = await ReadAsync<AgentWorkBoardDto>(query.CompanyId, $"api/companies/{query.CompanyId:D}/agent-work?{qs}", token);
        if (result is not null && (result.CompanyId != query.CompanyId || result.Items is null || result.Agents is null ||
            result.Responsibilities is null || result.Diagnostics is null || result.StateCounts is null || result.Total < 0 ||
            AgentWorkStates.All.Any(x => !result.StateCounts.TryGetValue(x,out var count) || count < 0) || result.Items.Any(x => !Usable(x))))
            throw new OnboardingApiException("Work returned incomplete or mismatched evidence. Refresh to retry.");
        return result;
    }
    public async Task<AgentWorkItemDto?> GetAsync(Guid companyId, string kind, Guid id, CancellationToken token = default)
    {
        if (id == Guid.Empty || !new[] { "task", "initiative", "case", "deal" }.Contains(kind)) throw new ArgumentException("Invalid work identity.");
        var result = await ReadAsync<AgentWorkItemDto>(companyId, $"api/companies/{companyId:D}/agent-work/{kind}/{id:D}", token);
        if (result is not null && (result.Id != id || result.Kind != kind || !Usable(result)))
            throw new OnboardingApiException("Work returned incomplete or mismatched evidence. Refresh to retry.");
        return result;
    }
    private static bool Usable(AgentWorkItemDto item) => item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Title) &&
        AgentWorkStates.All.Contains(item.State) && item.Agents is not null && item.RelatedRecords is not null &&
        item.Evidence is not null && item.Outputs is not null && item.Diagnostics is not null;
    public async Task<AuthorityExplanationDto?> AuthorityAsync(Guid companyId, Guid? agentId = null, string? workKind = null,
        Guid? workId = null, CancellationToken token = default)
    {
        var values = new Dictionary<string, string?> { ["agentId"] = agentId?.ToString("D"), ["workKind"] = workKind, ["workId"] = workId?.ToString("D") };
        var query = string.Join('&', values.Where(x => x.Value is not null).Select(x => $"{x.Key}={Uri.EscapeDataString(x.Value!)}"));
        var result = await ReadAsync<AuthorityExplanationDto>(companyId, $"api/companies/{companyId:D}/authority-explanation?{query}", token);
        if (result is not null && (result.CompanyId != companyId || result.WorkKind != workKind || result.WorkId != workId ||
            result.ProjectionVersion != "authority-explanation-v1" || string.IsNullOrWhiteSpace(result.ProjectionHash) ||
            !AgentAuthorityTransparencyPresenter.CompanyPositions.Any(x => x.Value == result.CompanyIntent) ||
            result.CompanyLimits is null || result.TaskPolicy is null || result.AvailableAgents is null || result.Diagnostics is null || result.Agents is null ||
            result.Agents.Any(x => x.Id == Guid.Empty || agentId.HasValue && x.Id != agentId || x.Actions is null || x.Grants is null ||
                x.Actions.Any(a => a.Check is null) || x.Grants.Any(g => g.Check is null || g.Limits is null))))
            throw new OnboardingApiException("Authority returned incomplete or mismatched evidence. Refresh to retry.");
        return result;
    }
    private async Task<T?> ReadAsync<T>(Guid company, string path, CancellationToken token)
    {
        if (company == Guid.Empty) throw new ArgumentException("A company is required.");
        if (useOfflineMode) throw new OnboardingApiException("Agent work requires a backend connection. Reconnect and refresh.");
        try
        {
            using var response = await transport.SendAsync(company, HttpMethod.Get, path, null, token);
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized or HttpStatusCode.NotFound) return default;
            if (!response.IsSuccessStatusCode) throw new OnboardingApiException("Work could not be loaded. Check the filters and refresh.");
            return await response.Content.ReadFromJsonAsync<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web), token)
                ?? throw new OnboardingApiException("Work returned no usable evidence. Refresh to retry.");
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is TaskCanceledException && !token.IsCancellationRequested)
        { throw new OnboardingApiException("Work could not be loaded. Refresh to retry."); }
    }
}
