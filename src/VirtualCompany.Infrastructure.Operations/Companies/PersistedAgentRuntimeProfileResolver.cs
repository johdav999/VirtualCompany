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
    private readonly IAgentEffectiveAuthorityResolver _effectiveAuthorityResolver;

    public PersistedAgentRuntimeProfileResolver(
        VirtualCompanyDbContext dbContext,
        IAgentCommunicationProfileResolver communicationProfileResolver,
        IAgentEffectiveAuthorityResolver effectiveAuthorityResolver)
    {
        _dbContext = dbContext;
        _communicationProfileResolver = communicationProfileResolver;
        _effectiveAuthorityResolver = effectiveAuthorityResolver;
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
        var authority = await _effectiveAuthorityResolver.ResolveAsync(companyId, agentId, cancellationToken);
        var useEffectiveFinanceAuthority = IsLauraFinanceAgent(agent);
        var effectiveTools = useEffectiveFinanceAuthority
            ? BuildEffectiveToolPermissions(authority)
            : CloneNodes(agent.Tools);
        var effectiveScopes = useEffectiveFinanceAuthority
            ? BuildEffectiveDataScopes(authority)
            : CloneNodes(agent.Scopes);

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
            effectiveTools,
            effectiveScopes,
            CloneNodes(agent.Thresholds),
            CloneNodes(agent.EscalationRules),
            CloneNodes(agent.TriggerLogic),
            CloneNodes(agent.WorkingHours),
            communicationProfile,
            ResolveBriefing(agent.CommunicationProfile),
            agent.CanReceiveAssignments,
            agent.UpdatedUtc,
            agent.AutonomyLevel.ToStorageValue(),
            CloneNodes(agent.Tools),
            CloneNodes(agent.Scopes),
            authority.AuthorityVersion,
            authority.AuthorityHash);
    }

    private static bool IsLauraFinanceAgent(Agent agent) =>
        string.Equals(agent.TemplateId, LauraFinanceAgentSeedData.TemplateId, StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(agent.DisplayName, "Laura", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(agent.Department, "Finance", StringComparison.OrdinalIgnoreCase));

    private static Dictionary<string, JsonNode?> BuildEffectiveToolPermissions(AgentEffectiveAuthorityDto authority)
    {
        var usable = authority.Tools.Where(item => item.IsUsable).ToArray();
        return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["allowed"] = ToJsonArray(usable.Select(item => item.ToolName).Distinct(StringComparer.OrdinalIgnoreCase)),
            ["actions"] = ToJsonArray(usable.Select(item => item.ActionType).Distinct(StringComparer.OrdinalIgnoreCase)),
            ["denied"] = ToJsonArray(authority.Tools.Where(item => !item.IsUsable).Select(item => item.ToolName).Distinct(StringComparer.OrdinalIgnoreCase)),
            ["deniedActions"] = new JsonArray()
        };
    }

    private static Dictionary<string, JsonNode?> BuildEffectiveDataScopes(AgentEffectiveAuthorityDto authority)
    {
        var result = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var action in new[] { "read", "recommend", "execute" })
        {
            result[action] = ToJsonArray(authority.Tools
                .Where(item => item.IsUsable && item.ActionType.Equals(action, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Scope).Distinct(StringComparer.OrdinalIgnoreCase));
        }
        result["write"] = new JsonArray();
        return result;
    }

    private static JsonArray ToJsonArray(IEnumerable<string> values) =>
        new(values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
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
