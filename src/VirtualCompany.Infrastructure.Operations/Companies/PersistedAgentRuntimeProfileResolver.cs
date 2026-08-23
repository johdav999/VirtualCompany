using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class PersistedAgentRuntimeProfileResolver : IAgentRuntimeProfileResolver
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAgentCommunicationProfileResolver _communicationProfileResolver;

    public PersistedAgentRuntimeProfileResolver(
        VirtualCompanyDbContext dbContext,
        IAgentCommunicationProfileResolver communicationProfileResolver)
    {
        _dbContext = dbContext;
        _communicationProfileResolver = communicationProfileResolver;
    }

    public async Task<AgentRuntimeProfileDto> GetCurrentProfileAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken,
        string? generationPath = null,
        string? correlationId = null)
    {
        // Runtime resolution must re-read persisted agent state so later orchestration
        // runs pick up operating and communication profile edits immediately.
        var agent = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);

        if (agent is null)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        var communicationProfile = _communicationProfileResolver.Resolve(
            agent.CommunicationProfile,
            new CommunicationProfileResolutionContext(
                companyId, agentId, generationPath, correlationId));

        var tools = CloneNodes(agent.Tools);
        var scopes = CloneNodes(agent.Scopes);
        if (IsLaura(agent))
        {
            BackfillLauraMigrationTools(tools);
            BackfillLauraFinanceScopes(scopes);
        }

        return new AgentRuntimeProfileDto(
            agent.Id,
            agent.CompanyId,
            agent.TemplateId,
            agent.DisplayName,
            agent.RoleName,
            agent.Department,
            agent.Seniority.ToStorageValue(),
            agent.Status.ToStorageValue(),
            agent.RoleBrief,
            CloneNodes(agent.Personality),
            CloneNodes(agent.Objectives),
            CloneNodes(agent.Kpis),
            tools,
            scopes,
            CloneNodes(agent.Thresholds),
            CloneNodes(agent.EscalationRules),
            CloneNodes(agent.TriggerLogic),
            CloneNodes(agent.WorkingHours),
            communicationProfile,
            ResolveBriefing(agent.CommunicationProfile),
            agent.CanReceiveAssignments,
            agent.UpdatedUtc,
            agent.AutonomyLevel.ToStorageValue());
    }

    private static bool IsLaura(Agent agent) =>
        string.Equals(agent.TemplateId, LauraFinanceAgentSeedData.TemplateId, StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(agent.DisplayName, "Laura", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(agent.Department, "Finance", StringComparison.OrdinalIgnoreCase));

    private static void BackfillLauraMigrationTools(Dictionary<string, JsonNode?> tools)
    {
        var allowed = ReadStrings(tools, "allowed");
        allowed.UnionWith(AccountingProviderSwitchAgentToolIds.All);
        tools["allowed"] = ToJsonArray(allowed);

        var actions = ReadStrings(tools, "actions");
        actions.UnionWith(["read", "recommend", "execute"]);
        tools["actions"] = ToJsonArray(actions);

        var denied = ReadStrings(tools, "denied");
        denied.ExceptWith(AccountingProviderSwitchAgentToolIds.All);
        tools["denied"] = ToJsonArray(denied);
    }

    private static void BackfillLauraFinanceScopes(Dictionary<string, JsonNode?> scopes)
    {
        foreach (var action in new[] { "read", "recommend", "execute" })
        {
            var values = ReadStrings(scopes, action);
            values.Add("finance");
            scopes[action] = ToJsonArray(values);
        }
    }

    private static HashSet<string> ReadStrings(IReadOnlyDictionary<string, JsonNode?> values, string key)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!values.TryGetValue(key, out var node) || node is not JsonArray array) return result;
        foreach (var item in array.OfType<JsonValue>())
            if (item.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) result.Add(text.Trim());
        return result;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return nodes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.DeepClone(),
            StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> ResolveBriefing(
        IReadOnlyDictionary<string, JsonNode?> communicationProfile)
    {
        if (!communicationProfile.TryGetValue("briefing", out var briefingNode) ||
            briefingNode is not JsonObject briefing)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in AgentBriefingCategories.All)
        {
            if (briefing[category] is JsonValue value &&
                value.TryGetValue<string>(out var content) &&
                !string.IsNullOrWhiteSpace(content))
            {
                result[category] = content.Trim();
            }
        }

        return result;
    }
}
