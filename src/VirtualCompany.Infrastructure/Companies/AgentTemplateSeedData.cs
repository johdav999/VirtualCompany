using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

internal static class AgentTemplateSeedData
{
    internal const int BaselineTemplateVersion = 1;
    private const string ResourceName = "VirtualCompany.Infrastructure.Persistence.SeedData.agent-templates.json";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Lazy<AgentTemplateSeedCatalog> Catalog = new(LoadCatalog);

    public static object[] GetModelSeeds() =>
        Catalog.Value.Templates.Select(CreateSeed).ToArray();

    public static bool TryGetTemplateVersion(string templateId, out int version) =>
        Catalog.Value.TemplateVersions.TryGetValue(templateId, out version);

    private static object CreateSeed(AgentTemplateSeedDefinition template) =>
        new
        {
            Id = template.Id,
            TemplateId = template.TemplateId,
            RoleName = template.RoleName,
            Department = template.Department,
            PersonaSummary = template.PersonaSummary,
            DefaultSeniority = AgentSeniorityValues.Parse(template.DefaultSeniority),
            AvatarUrl = template.AvatarUrl,
            SortOrder = template.SortOrder,
            IsActive = template.IsActive,
            Personality = ClonePayload(template.Personality),
            Objectives = ClonePayload(template.Objectives),
            Kpis = ClonePayload(template.Kpis),
            Tools = ClonePayload(template.Tools),
            Scopes = ClonePayload(template.Scopes),
            Thresholds = ClonePayload(template.Thresholds),
            EscalationRules = ClonePayload(template.EscalationRules),
            CreatedUtc = template.CreatedUtc,
            UpdatedUtc = template.UpdatedUtc
        };

    private static AgentTemplateSeedCatalog LoadCatalog()
    {
        var assembly = typeof(AgentTemplateSeedData).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded agent template seed '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var catalog = JsonSerializer.Deserialize<AgentTemplateSeedCatalog>(json, SerializerOptions)
            ?? new AgentTemplateSeedCatalog();

        if (catalog.Templates.Count == 0)
        {
            throw new InvalidOperationException("Agent template seed catalog is empty.");
        }

        foreach (var template in catalog.Templates)
        {
            if (template.Id == Guid.Empty)
            {
                throw new InvalidOperationException("Agent template seed is missing a valid id.");
            }

            if (string.IsNullOrWhiteSpace(template.TemplateId) ||
                string.IsNullOrWhiteSpace(template.RoleName) ||
                string.IsNullOrWhiteSpace(template.Department) ||
                string.IsNullOrWhiteSpace(template.DefaultSeniority))
            {
                throw new InvalidOperationException($"Agent template seed '{template.TemplateId}' is missing required fields.");
            }

            AgentSeniorityValues.EnsureSupported(
                AgentSeniorityValues.Parse(template.DefaultSeniority),
                nameof(template.DefaultSeniority));
        }

        catalog.TemplateVersions = catalog.Templates.ToDictionary(
            x => x.TemplateId,
            x => x.TemplateVersion <= 0 ? BaselineTemplateVersion : x.TemplateVersion,
            StringComparer.OrdinalIgnoreCase);

        return catalog;
    }

    private static Dictionary<string, JsonNode?> ClonePayload(IDictionary<string, JsonNode?> payload) =>
        payload.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.DeepClone(),
            StringComparer.OrdinalIgnoreCase);

    private sealed class AgentTemplateSeedCatalog
    {
        public List<AgentTemplateSeedDefinition> Templates { get; set; } = [];
        public IReadOnlyDictionary<string, int> TemplateVersions { get; set; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class AgentTemplateSeedDefinition
    {
        public Guid Id { get; set; }
        public string TemplateId { get; set; } = string.Empty;
        public int TemplateVersion { get; set; } = BaselineTemplateVersion;
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
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}