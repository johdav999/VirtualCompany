using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanySetupTemplateSeeder
{
    private const string ResourceName = "VirtualCompany.Infrastructure.Persistence.SeedData.onboarding-templates.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly VirtualCompanyDbContext _dbContext;

    public CompanySetupTemplateSeeder(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await LoadCatalogAsync(cancellationToken);
        if (catalog.Templates.Count == 0)
        {
            return;
        }

        var existingTemplates = await _dbContext.CompanySetupTemplates
            .ToDictionaryAsync(x => x.TemplateId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var seed in catalog.Templates)
        {
            if (existingTemplates.TryGetValue(seed.TemplateId, out var existing))
            {
                existing.UpdateDefinition(
                    seed.TemplateId,
                    seed.Name,
                    seed.Description,
                    seed.Category,
                    seed.Industry,
                    seed.BusinessType,
                    seed.SortOrder,
                    seed.IsActive,
                    seed.Defaults,
                    seed.Metadata);
            }
            else
            {
                _dbContext.CompanySetupTemplates.Add(new CompanySetupTemplate(
                    Guid.NewGuid(),
                    seed.TemplateId,
                    seed.Name,
                    seed.Description,
                    seed.Category,
                    seed.Industry,
                    seed.BusinessType,
                    seed.SortOrder,
                    seed.IsActive,
                    seed.Defaults,
                    seed.Metadata));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static async Task<TemplateSeedCatalog> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var assembly = typeof(CompanySetupTemplateSeeder).Assembly;
        await using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded onboarding template seed '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(cancellationToken);
        return JsonSerializer.Deserialize<TemplateSeedCatalog>(json, SerializerOptions)
            ?? new TemplateSeedCatalog();
    }

    private sealed class TemplateSeedCatalog
    {
        public List<TemplateSeedDefinition> Templates { get; set; } = [];
    }

    private sealed class TemplateSeedDefinition
    {
        public string TemplateId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
        public string? Industry { get; set; }
        public string? BusinessType { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public Dictionary<string, JsonNode?> Defaults { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, JsonNode?> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
