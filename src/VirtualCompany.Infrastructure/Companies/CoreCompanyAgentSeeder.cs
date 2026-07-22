using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CoreCompanyAgentSeeder : ICoreCompanyAgentSeeder
{
    private const string SalesTemplateId = "sales";
    private const string SupportTemplateId = "support";

    private readonly VirtualCompanyDbContext _dbContext;

    public CoreCompanyAgentSeeder(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(Guid companyId, CancellationToken cancellationToken)
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

        var requiredTemplateIds = new[] { SalesTemplateId, SupportTemplateId }
            .Where(templateId => !existing.Contains(templateId))
            .ToArray();

        if (requiredTemplateIds.Length == 0)
        {
            return;
        }

        var templates = await _dbContext.AgentTemplates
            .AsNoTracking()
            .Where(template => requiredTemplateIds.Contains(template.TemplateId) && template.IsActive)
            .ToDictionaryAsync(template => template.TemplateId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        AddFromTemplate(templates, companyId, SalesTemplateId, "Alex", "Sales Manager", "Sales");
        AddFromTemplate(templates, companyId, SupportTemplateId, "Ben", "Support Manager", "Support");
    }

    public async Task BackfillAllCompaniesAsync(CancellationToken cancellationToken)
    {
        var companyIds = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Select(company => company.Id)
            .ToListAsync(cancellationToken);

        foreach (var companyId in companyIds)
        {
            await SeedAsync(companyId, cancellationToken);
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
}
