using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentTemplateCatalogSeeder
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ILogger<AgentTemplateCatalogSeeder> _logger;

    public AgentTemplateCatalogSeeder(
        VirtualCompanyDbContext dbContext,
        ILogger<AgentTemplateCatalogSeeder> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var canonicalTemplates = AgentTemplateSeedData.CreateRuntimeTemplates();
        var existingTemplates = await _dbContext.AgentTemplates
            .ToDictionaryAsync(x => x.TemplateId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var existingIds = existingTemplates.Values
            .ToDictionary(x => x.Id, x => x.TemplateId);
        var restored = 0;
        var reactivated = 0;

        foreach (var canonical in canonicalTemplates)
        {
            if (!existingTemplates.TryGetValue(canonical.TemplateId, out var existing))
            {
                if (existingIds.TryGetValue(canonical.Id, out var conflictingTemplateId))
                {
                    throw new InvalidOperationException(
                        $"Agent template seed id '{canonical.Id}' is already used by template '{conflictingTemplateId}'.");
                }

                _dbContext.AgentTemplates.Add(canonical);
                existingTemplates.Add(canonical.TemplateId, canonical);
                existingIds.Add(canonical.Id, canonical.TemplateId);
                restored++;
                continue;
            }

            if (!existing.IsActive && canonical.IsActive)
            {
                ApplyCanonicalDefinition(existing, canonical);
                reactivated++;
            }
        }

        if (restored == 0 && reactivated == 0)
        {
            return;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Reconciled canonical agent template catalog. Restored={RestoredCount}, Reactivated={ReactivatedCount}.",
            restored,
            reactivated);
    }

    private static void ApplyCanonicalDefinition(AgentTemplate target, AgentTemplate source) =>
        target.UpdateDefinition(
            source.TemplateId,
            source.RoleName,
            source.Department,
            source.PersonaSummary,
            source.DefaultSeniority,
            source.AvatarUrl,
            source.SortOrder,
            source.IsActive,
            source.Personality,
            source.Objectives,
            source.Kpis,
            source.Tools,
            source.Scopes,
            source.Thresholds,
            source.EscalationRules);
}
