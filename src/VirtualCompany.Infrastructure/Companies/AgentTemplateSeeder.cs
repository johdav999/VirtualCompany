using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentTemplateSeeder
{
    private readonly VirtualCompanyDbContext _dbContext;

    public AgentTemplateSeeder(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await AgentTemplateSeedCatalogReader.LoadAsync(cancellationToken);
        if (catalog.Templates.Count == 0)
        {
            return;
        }

        var existingTemplates = await _dbContext.AgentTemplates
            .ToDictionaryAsync(x => x.TemplateId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var seed in catalog.Templates)
        {
            var defaultSeniority = AgentSeniorityValues.Parse(seed.DefaultSeniority);

            if (existingTemplates.TryGetValue(seed.TemplateId, out var existing))
            {
                existing.UpdateDefinition(
                    seed.TemplateId,
                    seed.RoleName,
                    seed.Department,
                    seed.PersonaSummary,
                    defaultSeniority,
                    seed.AvatarUrl,
                    seed.SortOrder,
                    seed.IsActive,
                    seed.Personality,
                    seed.Objectives,
                    seed.Kpis,
                    seed.Tools,
                    seed.Scopes,
                    seed.Thresholds,
                    seed.EscalationRules);
            }
            else
            {
                _dbContext.AgentTemplates.Add(new AgentTemplate(
                    seed.Id,
                    seed.TemplateId,
                    seed.RoleName,
                    seed.Department,
                    seed.PersonaSummary,
                    defaultSeniority,
                    seed.AvatarUrl,
                    seed.SortOrder,
                    seed.IsActive,
                    seed.Personality,
                    seed.Objectives,
                    seed.Kpis,
                    seed.Tools,
                    seed.Scopes,
                    seed.Thresholds,
                    seed.EscalationRules));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}