using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
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
            CloneNodes(agent.Tools),
            CloneNodes(agent.Scopes),
            CloneNodes(agent.Thresholds),
            CloneNodes(agent.EscalationRules),
            CloneNodes(agent.TriggerLogic),
            CloneNodes(agent.WorkingHours),
            communicationProfile,
            agent.CanReceiveAssignments,
            agent.UpdatedUtc,
            agent.AutonomyLevel.ToStorageValue());
    }

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
}
