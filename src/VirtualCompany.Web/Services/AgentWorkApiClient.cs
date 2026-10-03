using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace VirtualCompany.Web.Services;

public sealed class AgentWorkApiClient(ICompanyApiTransport transport, bool useOfflineMode = false)
{
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
