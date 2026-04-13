using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace VirtualCompany.Application.Agents;

public sealed record AgentTemplateCatalogItemDto(
    string TemplateId,
    int TemplateVersion,
    string RoleName,
    string Department,
    string PersonaSummary,
    string DefaultSeniority,
    string? AvatarUrl,
    Dictionary<string, JsonNode?> Personality,
    Dictionary<string, JsonNode?> Objectives,
    Dictionary<string, JsonNode?> Kpis,
    Dictionary<string, JsonNode?> Tools,
    Dictionary<string, JsonNode?> Scopes,
    Dictionary<string, JsonNode?> Thresholds,
    Dictionary<string, JsonNode?> EscalationRules);

public sealed record CompanyAgentSummaryDto(
    Guid Id,
    Guid CompanyId,
    string TemplateId,
    string DisplayName,
    string RoleName,
    string Department,
    string Seniority,
    string Status,
    string? AvatarUrl,
    string Personality,
    string AutonomyLevel);

public sealed record AgentHealthSummaryDto(
    string Status,
    string Label,
    string Reason);

public sealed record AgentWorkloadSummaryDto(
    int OpenItemsCount,
    int AwaitingApprovalCount,
    int ExecutedCount,
    int FailedCount,
    DateTime? LastActivityUtc,
    AgentHealthSummaryDto HealthSummary,
    // Retained for existing clients that still read the legacy flat fields.
    string HealthStatus,
    string Summary);

public sealed record AgentRosterItemDto(
    Guid Id,
    Guid CompanyId,
    string TemplateId,
    string DisplayName,
    string RoleName,
    string Department,
    string Seniority,
    string Status,
    string? AvatarUrl,
    string Personality,
    string AutonomyLevel,
    AgentWorkloadSummaryDto WorkloadSummary,
    string? ProfileRoute);

public sealed record AgentRosterResponseDto(
    IReadOnlyList<AgentRosterItemDto> Items,
    IReadOnlyList<string> Departments,
    IReadOnlyList<string> Statuses);

public sealed record AgentRosterFilterDto(
    string? Department,
    string? Status);

public sealed record AgentRecentActivityDto(
    DateTime OccurredUtc,
    string ActivityType,
    string Title,
    string Status,
    string? Detail);

public sealed record AgentProfileSectionDto(
    string Id,
    string Title,
    string Description,
    bool IsAvailable);

public sealed record AgentProfileAnalyticsPreviewDto(
    string SectionId,
    string Heading,
    string Description,
    IReadOnlyList<string> PlannedModules);


public sealed record AgentProfileVisibilityDto(
    bool CanViewPermissions,
    bool CanViewThresholds,
    bool CanViewWorkingHours,
    bool CanEditAgent,
    bool CanEditRoleBrief,
    bool CanEditObjectives,
    bool CanEditKpis,
    bool CanEditWorkingHours,
    bool CanEditStatus,
    bool CanEditSensitiveGovernance,
    bool CanPauseOrRestrictAgent);

public sealed record AgentProfileViewDto(
    Guid Id,
    Guid CompanyId,
    string TemplateId,
    string DisplayName,
    string RoleName,
    string Department,
    string Seniority,
    string Status,
    string? AvatarUrl,
    string Personality,
    string? RoleBrief,
    string AutonomyLevel,
    Dictionary<string, JsonNode?> Objectives,
    Dictionary<string, JsonNode?> Kpis,
    Dictionary<string, JsonNode?> ToolPermissions,
    Dictionary<string, JsonNode?> DataScopes,
    Dictionary<string, JsonNode?> ApprovalThresholds,
    Dictionary<string, JsonNode?> EscalationRules,
    Dictionary<string, JsonNode?> WorkingHours,
    AgentWorkloadSummaryDto WorkloadSummary,
    IReadOnlyList<AgentRecentActivityDto> RecentActivity,
    AgentProfileVisibilityDto Visibility,
    string ProfileRoute,
    IReadOnlyList<AgentProfileSectionDto> Sections,
    AgentProfileAnalyticsPreviewDto AnalyticsPreview,
    DateTime UpdatedUtc);

public sealed record CreateAgentFromTemplateCommand(
    string TemplateId,
    string DisplayName,
    string? AvatarUrl,
    string? Department,
    string? RoleName,
    string? Personality,
    string? Seniority,
    string? AutonomyLevel = null);

public sealed record CreateAgentFromTemplateResultDto(CompanyAgentSummaryDto Agent);

public sealed record AgentOperatingProfileDto(
    Guid Id,
    Guid CompanyId,
    string TemplateId,
    string DisplayName,
    string RoleName,
    string Department,
    string Seniority,
    string Status,
    string? AvatarUrl,
    string? RoleBrief,
    Dictionary<string, JsonNode?> Objectives,
    Dictionary<string, JsonNode?> Kpis,
    Dictionary<string, JsonNode?> ToolPermissions,
    Dictionary<string, JsonNode?> DataScopes,
    Dictionary<string, JsonNode?> ApprovalThresholds,
    Dictionary<string, JsonNode?> EscalationRules,
    Dictionary<string, JsonNode?> TriggerLogic,
    Dictionary<string, JsonNode?> WorkingHours,
    AgentProfileVisibilityDto Visibility,
    DateTime UpdatedUtc,
    bool CanReceiveAssignments,
    string AutonomyLevel);

public sealed record UpdateAgentOperatingProfileCommand(
    string Status,
    string? RoleBrief,
    AgentObjectivesInput? Objectives,
    AgentKpisInput? Kpis,
    AgentToolPermissionsInput? ToolPermissions,
    AgentDataScopesInput? DataScopes,
    AgentApprovalThresholdsInput? ApprovalThresholds,
    AgentEscalationRulesInput? EscalationRules,
    AgentTriggerLogicInput? TriggerLogic,
    Dictionary<string, JsonNode?>? WorkingHours,
    string? AutonomyLevel = null);

public sealed record AgentRuntimeProfileDto(
    Guid Id,
    Guid CompanyId,
    string TemplateId,
    string DisplayName,
    string RoleName,
    string Department,
    string Seniority,
    string Status,
    string? RoleBrief,
    Dictionary<string, JsonNode?> Personality,
    Dictionary<string, JsonNode?> Objectives,
    Dictionary<string, JsonNode?> Kpis,
    Dictionary<string, JsonNode?> ToolPermissions,
    Dictionary<string, JsonNode?> DataScopes,
    Dictionary<string, JsonNode?> ApprovalThresholds,
    Dictionary<string, JsonNode?> EscalationRules,
    Dictionary<string, JsonNode?> TriggerLogic,
    Dictionary<string, JsonNode?> WorkingHours,
    bool CanReceiveAssignments,
    DateTime UpdatedUtc,
    string AutonomyLevel);

public interface ICompanyAgentService
{
    Task<IReadOnlyList<AgentTemplateCatalogItemDto>> GetTemplatesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<AgentRosterResponseDto> GetRosterViewAsync(Guid companyId, AgentRosterFilterDto filter, CancellationToken cancellationToken);
    Task<AgentProfileViewDto> GetProfileViewAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyAgentSummaryDto>> GetRosterAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CreateAgentFromTemplateResultDto> CreateFromTemplateAsync(Guid companyId, CreateAgentFromTemplateCommand command, CancellationToken cancellationToken);
    Task<AgentOperatingProfileDto> GetOperatingProfileAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken);
    Task<AgentOperatingProfileDto> UpdateOperatingProfileAsync(Guid companyId, Guid agentId, UpdateAgentOperatingProfileCommand command, CancellationToken cancellationToken);
}

public interface IAgentRuntimeProfileResolver
{
    Task<AgentRuntimeProfileDto> GetCurrentProfileAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken);
}

public sealed record AssignableAgentDto(
    Guid Id,
    Guid CompanyId,
    string Status,
    bool CanReceiveAssignments);

public interface IAgentAssignmentGuard
{
    Task<AssignableAgentDto> GetAssignableAgentAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken);
    Task EnsureAgentCanReceiveNewTasksAsync(
        Guid companyId,
        Guid agentId,
        string fieldName,
        CancellationToken cancellationToken);
}
public sealed class AgentObjectivesInput
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Sections { get; set; }
}

public sealed class AgentKpisInput
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Sections { get; set; }
}

public sealed class AgentToolPermissionsInput
{
    public List<string> Allowed { get; set; } = [];
    public List<string> Denied { get; set; } = [];
    public List<string> Actions { get; set; } = [];
    public List<string> DeniedActions { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AgentDataScopesInput
{
    public List<string> Read { get; set; } = [];
    public List<string> Recommend { get; set; } = [];
    public List<string> Execute { get; set; } = [];
    public List<string> Write { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AgentApprovalThresholdsInput
{
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Rules { get; set; }
}

public sealed class AgentEscalationRulesInput
{
    public List<string> Critical { get; set; } = [];
    public string? EscalateTo { get; set; }
    public decimal? NotifyAfterMinutes { get; set; }
    public AgentApprovalRequirementInput? RequireApproval { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AgentTriggerLogicInput
{
    public bool? Enabled { get; set; }
    public List<AgentTriggerConditionInput> Conditions { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AgentApprovalRequirementInput
{
    public List<string> Actions { get; set; } = [];
    public List<string> Tools { get; set; } = [];
    public List<string> Scopes { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AgentTriggerConditionInput
{
    public string? Event { get; set; }
    public string? Type { get; set; }
    public string? Source { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AgentWorkingHoursInput
{
    public string? Timezone { get; set; }
    public List<AgentWorkingHourWindowInput> Windows { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AgentWorkingHourWindowInput
{
    public string? Day { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class AgentValidationException : Exception
{
    public AgentValidationException(IDictionary<string, string[]> errors)
        : base("Agent validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class AgentAssignmentValidationException : Exception
{
    public AgentAssignmentValidationException(IDictionary<string, string[]> errors)
        : base("Agent assignment validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class AgentTemplateNotFoundException : Exception
{
    public AgentTemplateNotFoundException(string templateId)
        : base($"Agent template '{templateId}' was not found.")
    {
    }
}