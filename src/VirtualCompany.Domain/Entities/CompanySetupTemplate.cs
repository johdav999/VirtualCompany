using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;

public sealed class CompanySetupTemplate
{
    private const int TemplateIdMaxLength = 100;
    private const int NameMaxLength = 200;
    private const int CategoryMaxLength = 100;
    private const int TagMaxLength = 100;
    private const int DescriptionMaxLength = 2000;

    private CompanySetupTemplate()
    {
    }

    public CompanySetupTemplate(
        Guid id,
        string templateId,
        string name,
        string? description,
        string? category,
        string? industryTag,
        string? businessTypeTag,
        int sortOrder,
        bool isActive,
        IDictionary<string, JsonNode?>? defaults = null,
        IDictionary<string, JsonNode?>? metadata = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
        UpdateDefinition(
            templateId,
            name,
            description,
            category,
            industryTag,
            businessTypeTag,
            sortOrder,
            isActive,
            defaults,
            metadata);
    }

    public Guid Id { get; private set; }
    public string TemplateId { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? Category { get; private set; }
    public string? IndustryTag { get; private set; }
    public string? BusinessTypeTag { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public Dictionary<string, JsonNode?> Defaults { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Metadata { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void UpdateDefinition(
        string templateId,
        string name,
        string? description,
        string? category,
        string? industryTag,
        string? businessTypeTag,
        int sortOrder,
        bool isActive,
        IDictionary<string, JsonNode?>? defaults = null,
        IDictionary<string, JsonNode?>? metadata = null)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder), "Sort order cannot be negative.");
        }

        TemplateId = NormalizeRequired(templateId, nameof(templateId), TemplateIdMaxLength);
        Name = NormalizeRequired(name, nameof(name), NameMaxLength);
        Description = NormalizeOptional(description, nameof(description), DescriptionMaxLength);
        Category = NormalizeOptional(category, nameof(category), CategoryMaxLength);
        IndustryTag = NormalizeOptional(industryTag, nameof(industryTag), TagMaxLength);
        BusinessTypeTag = NormalizeOptional(businessTypeTag, nameof(businessTypeTag), TagMaxLength);
        SortOrder = sortOrder;
        IsActive = isActive;
        Defaults = CloneNodes(defaults);
        Metadata = CloneNodes(metadata);
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return nodes.ToDictionary(
            pair => pair.Key,
            pair => pair.Value?.DeepClone(),
            StringComparer.OrdinalIgnoreCase);
    }
}
