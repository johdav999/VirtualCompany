using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

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
    string Personality);

public sealed record CreateAgentFromTemplateCommand(
    string TemplateId,
    string DisplayName,
    string? AvatarUrl,
    string? Department,
    string? RoleName,
    string? Personality,
    string? Seniority);

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
    DateTime UpdatedUtc,
    bool CanReceiveAssignments);

public sealed record UpdateAgentOperatingProfileCommand(
    string Status,
    string? RoleBrief,
    Dictionary<string, JsonNode?>? Objectives,
    Dictionary<string, JsonNode?>? Kpis,
    Dictionary<string, JsonNode?>? ToolPermissions,
    Dictionary<string, JsonNode?>? DataScopes,
    Dictionary<string, JsonNode?>? ApprovalThresholds,
    Dictionary<string, JsonNode?>? EscalationRules,
    Dictionary<string, JsonNode?>? TriggerLogic,
    Dictionary<string, JsonNode?>? WorkingHours);

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
    Dictionary<string, JsonNode?> Objectives,
    Dictionary<string, JsonNode?> Kpis,
    Dictionary<string, JsonNode?> ToolPermissions,
    Dictionary<string, JsonNode?> DataScopes,
    Dictionary<string, JsonNode?> ApprovalThresholds,
    Dictionary<string, JsonNode?> EscalationRules,
    Dictionary<string, JsonNode?> TriggerLogic,
    Dictionary<string, JsonNode?> WorkingHours,
    bool CanReceiveAssignments,
    DateTime UpdatedUtc);

public interface ICompanyAgentService
{
    Task<IReadOnlyList<AgentTemplateCatalogItemDto>> GetTemplatesAsync(Guid companyId, CancellationToken cancellationToken);
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

public sealed class AgentValidationException : Exception
{
    public AgentValidationException(IDictionary<string, string[]> errors)
        : base("Agent validation failed.")
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