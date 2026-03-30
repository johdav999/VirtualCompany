using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class CompanyBranding
{
    public string? LogoUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? Theme { get; set; }
    public IDictionary<string, JsonNode?> Extensions { get; set; } = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
}

public sealed class CompanySettings
{
    public string? Locale { get; set; }
    public string? TemplateId { get; set; }
    public CompanyOnboardingSettings Onboarding { get; set; } = new();
    public IDictionary<string, bool> FeatureFlags { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, JsonNode?> Extensions { get; set; } = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
}

public sealed class CompanyOnboardingSettings
{
    public string? Name { get; set; }
    public string? Industry { get; set; }
    public string? BusinessType { get; set; }
    public string? Timezone { get; set; }
    public string? Currency { get; set; }
    public string? Language { get; set; }
    public string? ComplianceRegion { get; set; }
    public int? CurrentStep { get; set; }
    public string? SelectedTemplateId { get; set; }
    public bool IsCompleted { get; set; }
    public List<string> StarterGuidance { get; set; } = [];
    public IDictionary<string, JsonNode?> Extensions { get; set; } = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
}
