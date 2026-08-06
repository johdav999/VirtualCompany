using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CoreCompanyAgentSeeder : ICoreCompanyAgentSeeder
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly AgentTemplateCatalogSeeder _templateCatalogSeeder;

    public CoreCompanyAgentSeeder(
        VirtualCompanyDbContext dbContext,
        AgentTemplateCatalogSeeder templateCatalogSeeder)
    {
        _dbContext = dbContext;
        _templateCatalogSeeder = templateCatalogSeeder;
    }

    public async Task SeedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        // Company-agent backfill must be safe when invoked outside the API startup
        // sequence, including onboarding, mailbox ingestion, and focused tests.
        await _templateCatalogSeeder.SeedAsync(cancellationToken);
        await SeedCoreAsync(companyId, cancellationToken);
    }

    private async Task SeedCoreAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var existingTemplateIds = await _dbContext.Agents
            .IgnoreQueryFilters()
            .Where(agent => agent.CompanyId == companyId)
            .Select(agent => agent.TemplateId)
            .ToListAsync(cancellationToken);

        var existing = existingTemplateIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(LauraFinanceAgentSeedData.TemplateId))
        {
            _dbContext.Agents.Add(LauraFinanceAgentSeedData.CreateCompanyAgent(companyId));
        }

        var missingAgents = new[]
            {
                new CoreAgentSeedDefinition(CoreAgentTemplateIds.Sales, "Alex", "Sales Manager", "Sales"),
                new CoreAgentSeedDefinition(CoreAgentTemplateIds.Support, "Ben", "Support Manager", "Support"),
                new CoreAgentSeedDefinition(CoreAgentTemplateIds.Marketing, "Maya", "Marketing Manager", "Marketing")
            }
            .Where(definition => !existing.Contains(definition.TemplateId))
            .ToArray();

        if (missingAgents.Length == 0)
        {
            return;
        }

        var requiredTemplateIds = missingAgents
            .Select(definition => definition.TemplateId)
            .ToArray();
        var templates = await _dbContext.AgentTemplates
            .AsNoTracking()
            .Where(template => requiredTemplateIds.Contains(template.TemplateId) && template.IsActive)
            .ToDictionaryAsync(template => template.TemplateId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var missingAgent in missingAgents)
        {
            AddFromTemplate(
                templates,
                companyId,
                missingAgent.TemplateId,
                missingAgent.DisplayName,
                missingAgent.RoleName,
                missingAgent.Department);
        }
    }

    public async Task BackfillAllCompaniesAsync(CancellationToken cancellationToken)
    {
        await _templateCatalogSeeder.SeedAsync(cancellationToken);

        var companyIds = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(company => company.Id)
            .ToListAsync(cancellationToken);

        foreach (var companyId in companyIds)
        {
            await SeedCoreAsync(companyId, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void AddFromTemplate(
        IReadOnlyDictionary<string, AgentTemplate> templates,
        Guid companyId,
        string templateId,
        string displayName,
        string roleName,
        string department)
    {
        if (!templates.TryGetValue(templateId, out var template))
        {
            throw new InvalidOperationException($"The required core agent template '{templateId}' is not available.");
        }

        _dbContext.Agents.Add(template.CreateCompanyAgent(
            companyId,
            displayName,
            roleName,
            department,
            template.AvatarUrl,
            null,
            autonomyLevel: AgentAutonomyLevel.Guided));
    }

    private sealed record CoreAgentSeedDefinition(
        string TemplateId,
        string DisplayName,
        string RoleName,
        string Department);
}
