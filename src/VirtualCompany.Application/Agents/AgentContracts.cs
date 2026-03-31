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

public interface ICompanyAgentService
{
    Task<IReadOnlyList<AgentTemplateCatalogItemDto>> GetTemplatesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyAgentSummaryDto>> GetRosterAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CreateAgentFromTemplateResultDto> CreateFromTemplateAsync(Guid companyId, CreateAgentFromTemplateCommand command, CancellationToken cancellationToken);
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