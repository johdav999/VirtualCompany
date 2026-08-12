using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingAgentAccessGuard(VirtualCompanyDbContext db) : IMarketingAgentAccessGuard
{
    public async Task<MarketingAgentAccessContext> RequireActiveMarketingAgentAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || agentId == Guid.Empty)
        {
            throw new MarketingAgentAccessException(
                MarketingAgentAccessReasonCodes.InvalidContext,
                "A valid company and Marketing agent are required.");
        }

        var agent = await db.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == agentId)
            .Select(x => new
            {
                x.CompanyId,
                x.Id,
                x.TemplateId,
                x.DisplayName,
                x.RoleName,
                x.Department,
                x.Status,
                x.AutonomyLevel
            })
            .SingleOrDefaultAsync(cancellationToken);

        var isMarketingAgent = agent is not null &&
            (string.Equals(agent.TemplateId, CoreAgentTemplateIds.Marketing, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(agent.Department, "Marketing", StringComparison.OrdinalIgnoreCase));
        var isActive = agent?.Status == AgentStatus.Active;

        if (!isMarketingAgent || !isActive)
        {
            // Use one response for missing, cross-company, wrong-role, paused, and archived agents.
            // Callers must not be able to enumerate agent identity through this boundary.
            throw new MarketingAgentAccessException(
                MarketingAgentAccessReasonCodes.Unavailable,
                "The Marketing agent is not available for this company.");
        }

        return new MarketingAgentAccessContext(
            agent!.CompanyId,
            agent.Id,
            agent.DisplayName,
            agent.RoleName,
            agent.Department,
            agent.Status.ToStorageValue(),
            agent.AutonomyLevel.ToStorageValue());
    }
}
