using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class AgentTemplate
{
    private const int TemplateIdMaxLength = 100;
    private const int RoleNameMaxLength = 100;
    private const int DepartmentMaxLength = 100;
    private const int PersonaSummaryMaxLength = 1000;
    private const int AvatarUrlMaxLength = 2048;

    private AgentTemplate()
    {
    }

    public AgentTemplate(
        Guid id,
        string templateId,
        string roleName,
        string department,
        string? personaSummary,
        AgentSeniority defaultSeniority,
        string? avatarUrl,
        int sortOrder,
        bool isActive,
        IDictionary<string, JsonNode?>? personality = null,
        IDictionary<string, JsonNode?>? objectives = null,
        IDictionary<string, JsonNode?>? kpis = null,
        IDictionary<string, JsonNode?>? tools = null,
        IDictionary<string, JsonNode?>? scopes = null,
        IDictionary<string, JsonNode?>? thresholds = null,
        IDictionary<string, JsonNode?>? escalationRules = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
        UpdateDefinition(
            templateId,
            roleName,
            department,
            personaSummary,
            defaultSeniority,
            avatarUrl,
            sortOrder,
            isActive,
            personality,
            objectives,
            kpis,
            tools,
            scopes,
            thresholds,
            escalationRules);
    }

    public Guid Id { get; private set; }
    public string TemplateId { get; private set; } = null!;
    public string RoleName { get; private set; } = null!;
    public string Department { get; private set; } = null!;
    public string? PersonaSummary { get; private set; }
    public AgentSeniority DefaultSeniority { get; private set; }
    public string? AvatarUrl { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public Dictionary<string, JsonNode?> Personality { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Objectives { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Kpis { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Tools { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Scopes { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Thresholds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> EscalationRules { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void UpdateDefinition(
        string templateId,
        string roleName,
        string department,
        string? personaSummary,
        AgentSeniority defaultSeniority,
        string? avatarUrl,
        int sortOrder,
        bool isActive,
        IDictionary<string, JsonNode?>? personality = null,
        IDictionary<string, JsonNode?>? objectives = null,
        IDictionary<string, JsonNode?>? kpis = null,
        IDictionary<string, JsonNode?>? tools = null,
        IDictionary<string, JsonNode?>? scopes = null,
        IDictionary<string, JsonNode?>? thresholds = null,
        IDictionary<string, JsonNode?>? escalationRules = null)
    {
        if (sortOrder < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sortOrder), "Sort order cannot be negative.");
        }

        AgentSeniorityValues.EnsureSupported(defaultSeniority, nameof(defaultSeniority));
        TemplateId = NormalizeRequired(templateId, nameof(templateId), TemplateIdMaxLength);
        RoleName = NormalizeRequired(roleName, nameof(roleName), RoleNameMaxLength);
        Department = NormalizeRequired(department, nameof(department), DepartmentMaxLength);
        PersonaSummary = NormalizeOptional(personaSummary, nameof(personaSummary), PersonaSummaryMaxLength);
        AvatarUrl = NormalizeOptional(avatarUrl, nameof(avatarUrl), AvatarUrlMaxLength);
        DefaultSeniority = defaultSeniority;
        SortOrder = sortOrder;
        IsActive = isActive;
        Personality = CloneNodes(personality);
        Objectives = CloneNodes(objectives);
        Kpis = CloneNodes(kpis);
        Tools = CloneNodes(tools);
        Scopes = CloneNodes(scopes);
        Thresholds = CloneNodes(thresholds);
        EscalationRules = CloneNodes(escalationRules);
        UpdatedUtc = DateTime.UtcNow;
    }

    public Agent CreateCompanyAgent(
        Guid companyId,
        string displayName,
        string? roleName,
        string? department,
        string? avatarUrl,
        string? personality,
        AgentSeniority? seniority = null)
    {
        var resolvedSeniority = seniority ?? DefaultSeniority;
        AgentSeniorityValues.EnsureSupported(resolvedSeniority, nameof(seniority));

        return new Agent(
            Guid.NewGuid(),
            companyId,
            TemplateId,
            NormalizeRequired(displayName, nameof(displayName), 200),
            FirstNonEmpty(roleName, RoleName)!,
            FirstNonEmpty(department, Department)!,
            FirstNonEmpty(avatarUrl, AvatarUrl),
            resolvedSeniority,
            AgentStatusValues.DefaultStatus,
            MergePersonality(personality),
            CloneNodes(Objectives),
            CloneNodes(Kpis),
            CloneNodes(Tools),
            CloneNodes(Scopes),
            CloneNodes(Thresholds),
            CloneNodes(EscalationRules));
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

    private Dictionary<string, JsonNode?> MergePersonality(string? personality)
    {
        var merged = CloneNodes(Personality);
        var summary = FirstNonEmpty(personality, ResolvePersonalitySummary(merged), PersonaSummary);

        if (!string.IsNullOrWhiteSpace(summary))
        {
            merged["summary"] = JsonValue.Create(summary);
        }

        return merged;
    }

    private static string ResolvePersonalitySummary(IDictionary<string, JsonNode?> personality)
    {
        if (!personality.TryGetValue("summary", out var node) || node is null)
        {
            return string.Empty;
        }

        return node is JsonValue value && value.TryGetValue<string>(out var summary)
            ? NormalizeOptional(summary, nameof(personality), PersonaSummaryMaxLength) ?? string.Empty
            : node.ToJsonString();
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }
}

public sealed class Agent : ICompanyOwnedEntity
{
    private const int TemplateIdMaxLength = 100;
    private const int DisplayNameMaxLength = 200;
    private const int RoleNameMaxLength = 100;
    private const int DepartmentMaxLength = 100;
    private const int AvatarUrlMaxLength = 2048;

    private Agent()
    {
    }

    public Agent(
        Guid id,
        Guid companyId,
        string templateId,
        string displayName,
        string roleName,
        string department,
        string? avatarUrl,
        AgentSeniority seniority,
        AgentStatus? status = null,
        IDictionary<string, JsonNode?>? personality = null,
        IDictionary<string, JsonNode?>? objectives = null,
        IDictionary<string, JsonNode?>? kpis = null,
        IDictionary<string, JsonNode?>? tools = null,
        IDictionary<string, JsonNode?>? scopes = null,
        IDictionary<string, JsonNode?>? thresholds = null,
        IDictionary<string, JsonNode?>? escalationRules = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        AgentSeniorityValues.EnsureSupported(seniority, nameof(seniority));
        var resolvedStatus = status ?? AgentStatusValues.DefaultStatus;
        AgentStatusValues.EnsureSupported(resolvedStatus, nameof(status));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        TemplateId = NormalizeRequired(templateId, nameof(templateId), TemplateIdMaxLength);
        DisplayName = NormalizeRequired(displayName, nameof(displayName), DisplayNameMaxLength);
        RoleName = NormalizeRequired(roleName, nameof(roleName), RoleNameMaxLength);
        Department = NormalizeRequired(department, nameof(department), DepartmentMaxLength);
        AvatarUrl = NormalizeOptional(avatarUrl, nameof(avatarUrl), AvatarUrlMaxLength);
        Seniority = seniority;
        Status = resolvedStatus;
        Personality = CloneNodes(personality);
        Objectives = CloneNodes(objectives);
        Kpis = CloneNodes(kpis);
        Tools = CloneNodes(tools);
        Scopes = CloneNodes(scopes);
        Thresholds = CloneNodes(thresholds);
        EscalationRules = CloneNodes(escalationRules);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string TemplateId { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string RoleName { get; private set; } = null!;
    public string Department { get; private set; } = null!;
    public string? AvatarUrl { get; private set; }
    public AgentSeniority Seniority { get; private set; }
    public AgentStatus Status { get; private set; }
    public Dictionary<string, JsonNode?> Personality { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Objectives { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Kpis { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Tools { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Scopes { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Thresholds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> EscalationRules { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

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

        return nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }
}