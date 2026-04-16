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

public sealed record AgentStatusWorkloadDto(
    int ActiveTaskCount,
    int BlockedTaskCount,
    int AwaitingApprovalCount,
    int ActiveWorkflowCount,
    string WorkloadLevel);

public sealed record AgentStatusRecentActionDto(
    DateTime OccurredUtc,
    string ActionType,
    string Title,
    string Status,
    string RelatedEntityType,
    Guid? RelatedEntityId);

public sealed record AgentStatusDetailLinkDto(
    string Path,
    string ActiveTab,
    IReadOnlyDictionary<string, string> Query);

public sealed record AgentStatusCardDto(
    Guid AgentId,
    Guid CompanyId,
    string DisplayName,
    string RoleName,
    string Department,
    AgentStatusWorkloadDto Workload,
    string HealthStatus,
    IReadOnlyList<string> HealthReasons,
    int ActiveAlertsCount,
    IReadOnlyList<AgentStatusRecentActionDto> RecentActions,
    DateTime LastUpdatedUtc,
    AgentStatusDetailLinkDto DetailLink);

public sealed record AgentStatusCardsResponseDto(
    IReadOnlyList<AgentStatusCardDto> Items,
    DateTime GeneratedUtc);

public sealed record AgentStatusDetailTaskDto(
    Guid Id,
    string Type,
    string Title,
    string Priority,
    string Status,
    DateTime? DueUtc,
    DateTime UpdatedUtc);

public sealed record AgentStatusDetailWorkflowDto(
    Guid Id,
    string DefinitionName,
    string State,
    string? CurrentStep,
    DateTime StartedUtc,
    DateTime UpdatedUtc);

public sealed record AgentStatusDetailAlertDto(
    Guid Id,
    string Type,
    string Severity,
    string Title,
    string Summary,
    string Status,
    DateTime UpdatedUtc);

public sealed record AgentStatusHealthBreakdownDto(
    string Status,
    IReadOnlyList<string> Reasons,
    AgentStatusHealthMetrics Metrics);

public sealed record AgentStatusDetailDto(
    Guid AgentId,
    Guid CompanyId,
    string DisplayName,
    string RoleName,
    string Department,
    AgentStatusWorkloadDto Workload,
    AgentStatusHealthBreakdownDto Health,
    int ActiveAlertsCount,
    IReadOnlyList<AgentStatusDetailTaskDto> ActiveTasks,
    IReadOnlyList<AgentStatusDetailWorkflowDto> ActiveWorkflows,
    IReadOnlyList<AgentStatusDetailAlertDto> ActiveAlerts,
    IReadOnlyList<AgentStatusRecentActionDto> RecentActions,
    DateTime LastUpdatedUtc,
    DateTime GeneratedUtc);

public sealed record AgentStatusHealthMetrics(
    int FailedRunCount,
    int StalledWorkCount,
    int PolicyViolationCount);

public sealed record AgentStatusHealthResult(
    string Status,
    IReadOnlyList<string> Reasons);

public sealed class AgentStatusHealthThresholds
{
    public const int DefaultWarningFailedRuns = 1;
    public const int DefaultCriticalFailedRuns = 3;
    public const int DefaultWarningStalledWork = 1;
    public const int DefaultCriticalStalledWork = 3;
    public const int DefaultWarningPolicyViolations = 1;
    public const int DefaultCriticalPolicyViolations = 2;

    public int WarningFailedRuns { get; init; } = DefaultWarningFailedRuns;
    public int CriticalFailedRuns { get; init; } = DefaultCriticalFailedRuns;
    public int WarningStalledWork { get; init; } = DefaultWarningStalledWork;
    public int CriticalStalledWork { get; init; } = DefaultCriticalStalledWork;
    public int WarningPolicyViolations { get; init; } = DefaultWarningPolicyViolations;
    public int CriticalPolicyViolations { get; init; } = DefaultCriticalPolicyViolations;
}

public interface IAgentStatusAggregationService
{
    Task<AgentStatusCardsResponseDto> GetStatusCardsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<AgentStatusDetailDto> GetStatusDetailAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken);
}

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
    Dictionary<string, JsonNode?> CommunicationProfile,
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
    string? AutonomyLevel = null,
    AgentCommunicationProfileInput? CommunicationProfile = null);

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
    AgentCommunicationProfileDto CommunicationProfile,
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
        CancellationToken cancellationToken,
        string? generationPath = null,
        string? correlationId = null);
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
    [JsonPropertyName("event")]
    public string? Event { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("condition")]
    public ConditionExpressionInput? Condition { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; set; }
}

public sealed class ConditionExpressionInput
{
    public ConditionTargetReferenceInput? Target { get; set; }
    public string? Operator { get; set; }
    public string? ValueType { get; set; }
    public JsonElement? ComparisonValue { get; set; }
    public string? RepeatFiringMode { get; set; }
}

public sealed class ConditionTargetReferenceInput
{
    public string? SourceType { get; set; }
    public string? MetricName { get; set; }
    public string? EntityType { get; set; }
    public string? FieldPath { get; set; }
}

public sealed record ConditionEvaluationSnapshotDto(
    DateTime EvaluatedUtc,
    Dictionary<string, JsonNode?> InputValues,
    bool Outcome,
    bool? PreviousOutcome,
    bool IsFalseToTrueTransition);

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