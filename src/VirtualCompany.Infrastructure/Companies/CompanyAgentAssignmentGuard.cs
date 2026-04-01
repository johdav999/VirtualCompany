using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyAgentAssignmentGuard : IAgentAssignmentGuard
{
    private readonly VirtualCompanyDbContext _dbContext;

    public CompanyAgentAssignmentGuard(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AssignableAgentDto> GetAssignableAgentAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var agent = await GetAgentAsync(companyId, agentId, cancellationToken);

        return new AssignableAgentDto(
            agent.Id,
            agent.CompanyId,
            agent.Status.ToStorageValue(),
            agent.CanReceiveAssignments);
    }

    public async Task EnsureAgentCanReceiveNewTasksAsync(
        Guid companyId,
        Guid agentId,
        string fieldName,
        CancellationToken cancellationToken)
    {
        var agent = await GetAgentAsync(companyId, agentId, cancellationToken);
        if (agent.CanReceiveAssignments)
        {
            return;
        }

        throw new AgentAssignmentValidationException(
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [fieldName] = [Agent.ArchivedAssignmentErrorMessage]
            });
    }

    private async Task<Agent> GetAgentAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        var agent = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);

        return agent ?? throw new KeyNotFoundException("Agent not found.");
    }
}
