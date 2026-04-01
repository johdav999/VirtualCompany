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
        AvatarUrl = AvatarReferenceRules.Normalize(avatarUrl, nameof(avatarUrl), AvatarUrlMaxLength);
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
        AgentSeniority? seniority = null,
        AgentAutonomyLevel? autonomyLevel = null)
    {
        var resolvedSeniority = seniority ?? DefaultSeniority;
        AgentSeniorityValues.EnsureSupported(resolvedSeniority, nameof(seniority));
        var configurationSnapshot = CreateConfigurationSnapshot(personality);


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
            autonomyLevel ?? AgentAutonomyLevelValues.DefaultLevel,
            configurationSnapshot.Personality,
            configurationSnapshot.Objectives,
            configurationSnapshot.Kpis,
            configurationSnapshot.Tools,
            configurationSnapshot.Scopes,
            configurationSnapshot.Thresholds,
            configurationSnapshot.EscalationRules);
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

    private AgentConfigurationSnapshot CreateConfigurationSnapshot(string? personality)
    {
        // Hired agents keep a company-owned snapshot of template defaults so later
        // template edits do not change persisted agent behavior.
        return new AgentConfigurationSnapshot(
            MergePersonality(personality),
            CloneNodes(Objectives),
            CloneNodes(Kpis),
            CloneNodes(Tools),
            CloneNodes(Scopes),
            CloneNodes(Thresholds),
            CloneNodes(EscalationRules));
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

    private sealed record AgentConfigurationSnapshot(
        Dictionary<string, JsonNode?> Personality,
        Dictionary<string, JsonNode?> Objectives,
        Dictionary<string, JsonNode?> Kpis,
        Dictionary<string, JsonNode?> Tools,
        Dictionary<string, JsonNode?> Scopes,
        Dictionary<string, JsonNode?> Thresholds,
        Dictionary<string, JsonNode?> EscalationRules);

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }
}

public static class AvatarReferenceRules
{
    private static readonly string[] InlinePrefixes = ["data:", "blob:"];
    private static readonly string[] Base64Prefixes = ["iVBORw0KGgo", "/9j/", "R0lGOD", "PHN2Zy"];

    public static string? Normalize(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!TryValidate(trimmed, fieldName, maxLength, out var error))
        {
            throw error!.Contains("characters or fewer", StringComparison.Ordinal)
                ? new ArgumentOutOfRangeException(fieldName, error)
                : new ArgumentException(error, fieldName);
        }

        return trimmed;
    }

    public static bool TryValidate(string? value, string fieldName, int maxLength, out string? error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = null;
            return true;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            error = $"{fieldName} must be {maxLength} characters or fewer.";
            return false;
        }

        if (IsInlinePayload(trimmed))
        {
            error = $"{fieldName} must be an external http/https URL or a file/storage reference, not inline image data.";
            return false;
        }

        if (LooksLikeAbsoluteUrl(trimmed) &&
            (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            error = $"{fieldName} must be a valid absolute http/https URL or a file/storage reference.";
            return false;
        }

        error = null;
        return true;
    }

    private static bool IsInlinePayload(string value) =>
        InlinePrefixes.Any(prefix => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
        (value.Length >= 256 &&
         !value.Contains("/", StringComparison.Ordinal) &&
         !value.Contains("\\", StringComparison.Ordinal) &&
         !value.Contains(".", StringComparison.Ordinal) &&
         Base64Prefixes.Any(prefix => value.StartsWith(prefix, StringComparison.Ordinal)));

    private static bool LooksLikeAbsoluteUrl(string value) =>
        value.Contains("://", StringComparison.Ordinal) ||
        value.StartsWith("http:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("blob:", StringComparison.OrdinalIgnoreCase);
}

public sealed class Agent : ICompanyOwnedEntity
{
    private const int TemplateIdMaxLength = 100;
    private const int DisplayNameMaxLength = 200;
    private const int RoleNameMaxLength = 100;
    private const int DepartmentMaxLength = 100;
    private const int RoleBriefMaxLength = 4000;
    private const int AvatarUrlMaxLength = 2048;

    public const string ArchivedAssignmentErrorMessage = "Archived agents cannot be assigned new tasks.";

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
        AgentAutonomyLevel? autonomyLevel = null,
        IDictionary<string, JsonNode?>? personality = null,
        IDictionary<string, JsonNode?>? objectives = null,
        IDictionary<string, JsonNode?>? kpis = null,
        IDictionary<string, JsonNode?>? tools = null,
        IDictionary<string, JsonNode?>? scopes = null,
        IDictionary<string, JsonNode?>? thresholds = null,
        IDictionary<string, JsonNode?>? escalationRules = null,
        string? roleBrief = null,
        IDictionary<string, JsonNode?>? triggerLogic = null,
        IDictionary<string, JsonNode?>? workingHours = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        AgentSeniorityValues.EnsureSupported(seniority, nameof(seniority));
        var resolvedStatus = status ?? AgentStatusValues.DefaultStatus;
        AgentStatusValues.EnsureSupported(resolvedStatus, nameof(status));
        var resolvedAutonomyLevel = autonomyLevel ?? AgentAutonomyLevelValues.DefaultLevel;
        AgentAutonomyLevelValues.EnsureSupported(resolvedAutonomyLevel, nameof(autonomyLevel));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        TemplateId = NormalizeRequired(templateId, nameof(templateId), TemplateIdMaxLength);
        DisplayName = NormalizeRequired(displayName, nameof(displayName), DisplayNameMaxLength);
        RoleName = NormalizeRequired(roleName, nameof(roleName), RoleNameMaxLength);
        RoleBrief = NormalizeOptional(roleBrief, nameof(roleBrief), RoleBriefMaxLength);
        Department = NormalizeRequired(department, nameof(department), DepartmentMaxLength);
        AvatarUrl = AvatarReferenceRules.Normalize(avatarUrl, nameof(avatarUrl), AvatarUrlMaxLength);
        Seniority = seniority;
        Status = resolvedStatus;
        AutonomyLevel = resolvedAutonomyLevel;
        Personality = CloneNodes(personality);
        Objectives = CloneNodes(objectives);
        Kpis = CloneNodes(kpis);
        Tools = CloneNodes(tools);
        Scopes = CloneNodes(scopes);
        Thresholds = CloneNodes(thresholds);
        EscalationRules = CloneNodes(escalationRules);
        TriggerLogic = CloneNodes(triggerLogic);
        WorkingHours = CloneNodes(workingHours);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string TemplateId { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string RoleName { get; private set; } = null!;
    public string? RoleBrief { get; private set; }
    public string Department { get; private set; } = null!;
    public string? AvatarUrl { get; private set; }
    public AgentSeniority Seniority { get; private set; }
    public AgentStatus Status { get; private set; }
    public AgentAutonomyLevel AutonomyLevel { get; private set; }
    public Dictionary<string, JsonNode?> Personality { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Objectives { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Kpis { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Tools { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Scopes { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> Thresholds { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> EscalationRules { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> TriggerLogic { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, JsonNode?> WorkingHours { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public bool CanReceiveAssignments => Status != AgentStatus.Archived;

    public bool UpdateOperatingProfile(
        string? roleBrief,
        AgentStatus status,
        AgentAutonomyLevel autonomyLevel,
        IDictionary<string, JsonNode?>? objectives,
        IDictionary<string, JsonNode?>? kpis,
        IDictionary<string, JsonNode?>? tools,
        IDictionary<string, JsonNode?>? scopes,
        IDictionary<string, JsonNode?>? thresholds,
        IDictionary<string, JsonNode?>? escalationRules,
        IDictionary<string, JsonNode?>? triggerLogic,
        IDictionary<string, JsonNode?>? workingHours)
    {
        AgentStatusValues.EnsureSupported(status, nameof(status));
        AgentAutonomyLevelValues.EnsureSupported(autonomyLevel, nameof(autonomyLevel));

        var normalizedRoleBrief = NormalizeOptional(roleBrief, nameof(roleBrief), RoleBriefMaxLength);
        var updatedObjectives = CloneNodes(objectives);
        var updatedKpis = CloneNodes(kpis);
        var updatedTools = CloneNodes(tools);
        var updatedScopes = CloneNodes(scopes);
        var updatedThresholds = CloneNodes(thresholds);
        var updatedEscalationRules = CloneNodes(escalationRules);
        var updatedTriggerLogic = CloneNodes(triggerLogic);
        var updatedWorkingHours = CloneNodes(workingHours);

        if (string.Equals(RoleBrief, normalizedRoleBrief, StringComparison.Ordinal) &&
            Status == status &&
            AutonomyLevel == autonomyLevel &&
            JsonDictionariesEqual(Objectives, updatedObjectives) &&
            JsonDictionariesEqual(Kpis, updatedKpis) &&
            JsonDictionariesEqual(Tools, updatedTools) &&
            JsonDictionariesEqual(Scopes, updatedScopes) &&
            JsonDictionariesEqual(Thresholds, updatedThresholds) &&
            JsonDictionariesEqual(EscalationRules, updatedEscalationRules) &&
            JsonDictionariesEqual(TriggerLogic, updatedTriggerLogic) &&
            JsonDictionariesEqual(WorkingHours, updatedWorkingHours))
        {
            return false;
        }

        RoleBrief = normalizedRoleBrief;
        Status = status;
        AutonomyLevel = autonomyLevel;
        Objectives = updatedObjectives;
        Kpis = updatedKpis;
        Tools = updatedTools;
        Scopes = updatedScopes;
        Thresholds = updatedThresholds;
        EscalationRules = updatedEscalationRules;
        TriggerLogic = updatedTriggerLogic;
        WorkingHours = updatedWorkingHours;
        UpdatedUtc = DateTime.UtcNow;
        return true;
    }

    public void EnsureCanReceiveAssignments()
    {
        if (!CanReceiveAssignments)
        {
            throw new InvalidOperationException(ArchivedAssignmentErrorMessage);
        }
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

    private static bool JsonDictionariesEqual(
        IReadOnlyDictionary<string, JsonNode?> left,
        IReadOnlyDictionary<string, JsonNode?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (key, leftValue) in left)
        {
            if (!right.TryGetValue(key, out var rightValue) ||
                !string.Equals(leftValue?.ToJsonString(), rightValue?.ToJsonString(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
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