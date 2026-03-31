using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Infrastructure.Companies;

internal static class AgentTemplateSeedCatalogReader
{
    private const string ResourceName = "VirtualCompany.Infrastructure.Persistence.SeedData.agent-templates.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task<AgentTemplateSeedCatalog> LoadAsync(CancellationToken cancellationToken = default)
    {
        var assembly = typeof(AgentTemplateSeedCatalogReader).Assembly;
        await using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded agent template seed '{ResourceName}' was not found.");

        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(cancellationToken);
        var catalog = JsonSerializer.Deserialize<AgentTemplateSeedCatalog>(json, SerializerOptions) ?? new AgentTemplateSeedCatalog();
        Validate(catalog);
        return catalog;
    }

    private static void Validate(AgentTemplateSeedCatalog catalog)
    {
        if (catalog.Version <= 0)
        {
            throw new InvalidOperationException("Agent template seed catalog version must be greater than zero.");
        }

        if (catalog.Templates.Count == 0)
        {
            throw new InvalidOperationException("Agent template seed catalog must contain at least one template.");
        }

        var ids = new HashSet<Guid>();
        var templateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var template in catalog.Templates)
        {
            ValidateTemplate(template);

            if (!ids.Add(template.Id))
            {
                throw new InvalidOperationException($"Agent template seed contains duplicate id '{template.Id}'.");
            }

            if (!templateIds.Add(template.TemplateId))
            {
                throw new InvalidOperationException($"Agent template seed contains duplicate templateId '{template.TemplateId}'.");
            }
        }
    }

    private static void ValidateTemplate(AgentTemplateSeedDefinition template)
    {
        if (template.Id == Guid.Empty)
        {
            throw new InvalidOperationException($"Agent template '{template.TemplateId}' must declare a stable id.");
        }

        if (template.TemplateVersion <= 0)
        {
            throw new InvalidOperationException($"Agent template '{template.TemplateId}' must declare a templateVersion greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(template.TemplateId))
        {
            throw new InvalidOperationException("Agent template seed must declare a templateId.");
        }

        if (string.IsNullOrWhiteSpace(template.RoleName))
        {
            throw new InvalidOperationException($"Agent template '{template.TemplateId}' must declare a roleName.");
        }

        if (string.IsNullOrWhiteSpace(template.Department))
        {
            throw new InvalidOperationException($"Agent template '{template.TemplateId}' must declare a department.");
        }

        if (string.IsNullOrWhiteSpace(template.DefaultSeniority))
        {
            throw new InvalidOperationException($"Agent template '{template.TemplateId}' must declare a defaultSeniority.");
        }

        ValidatePayload(template.TemplateId, nameof(template.Personality), template.Personality);
        ValidatePayload(template.TemplateId, nameof(template.Objectives), template.Objectives);
        ValidatePayload(template.TemplateId, nameof(template.Kpis), template.Kpis);
        ValidatePayload(template.TemplateId, nameof(template.Tools), template.Tools);
        ValidatePayload(template.TemplateId, nameof(template.Scopes), template.Scopes);
        ValidatePayload(template.TemplateId, nameof(template.Thresholds), template.Thresholds);
        ValidatePayload(template.TemplateId, nameof(template.EscalationRules), template.EscalationRules);
    }

    private static void ValidatePayload(string templateId, string payloadName, Dictionary<string, JsonNode?> payload)
    {
        if (payload.Count == 0)
        {
            throw new InvalidOperationException($"Agent template '{templateId}' must declare a non-empty {payloadName} payload.");
        }
    }
}

internal sealed class AgentTemplateSeedCatalog
{
    public int Version { get; set; }
    public List<AgentTemplateSeedDefinition> Templates { get; set; } = [];
}

internal sealed class AgentTemplateSeedDefinition
{
    public Guid Id { get; set; }
    public int TemplateVersion { get; set; } = 1;
    public string TemplateId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string? PersonaSummary { get; set; }
    public string DefaultSeniority { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public Dictionary<string, JsonNode?> Personality { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Objectives { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Kpis { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Tools { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Scopes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Thresholds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> EscalationRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}