using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyWorkflowDefinitionSeeder
{
    private readonly VirtualCompanyDbContext _dbContext;

    public CompanyWorkflowDefinitionSeeder(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        foreach (var catalogEntry in PredefinedWorkflowCatalog.All)
        {
            var exists = await _dbContext.WorkflowDefinitions
                .IgnoreQueryFilters()
                .AnyAsync(
                    x => x.CompanyId == null &&
                        x.Code == catalogEntry.Code &&
                        x.Version == catalogEntry.Version,
                    cancellationToken);

            if (exists)
            {
                continue;
            }

            if (!WorkflowTriggerTypeValues.TryParse(catalogEntry.TriggerType, out var triggerType))
            {
                throw new InvalidOperationException($"Predefined workflow '{catalogEntry.Code}' has an invalid trigger type.");
            }

            _dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                StableId(catalogEntry.Code, catalogEntry.Version),
                null,
                catalogEntry.Code,
                catalogEntry.Name,
                catalogEntry.Department,
                triggerType,
                catalogEntry.Version,
                PredefinedWorkflowCatalog.CloneDefinitionJson(catalogEntry.Code),
                active: true));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static Guid StableId(string code, int version) =>
        code switch
        {
            "DAILY-EXECUTIVE-BRIEFING" => Guid.Parse("f33d253c-3d80-46d5-a43c-68f2a5a72f01"),
            "INVOICE-APPROVAL-REVIEW" => Guid.Parse("4810f5bb-2eaa-42d0-a650-754f9616cc02"),
            "SUPPORT-ESCALATION-TRIAGE" => Guid.Parse("af11bff4-01dc-4a85-91cf-68a35bfa5ce3"),
            "LEAD-FOLLOW-UP" => Guid.Parse("ec1f7bb3-1f3f-4f56-bba6-f403bd02ea04"),
            _ => Guid.NewGuid()
        };
}