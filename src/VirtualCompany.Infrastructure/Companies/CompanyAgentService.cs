using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyAgentService : ICompanyAgentService
{
    private const int TemplateIdMaxLength = 100;
    private const int DisplayNameMaxLength = 200;
    private const int DepartmentMaxLength = 100;
    private const int RoleNameMaxLength = 100;
    private const int AvatarUrlMaxLength = 2048;
    private const int PersonalitySummaryMaxLength = 1000;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IAuditEventWriter _auditEventWriter;

    public CompanyAgentService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IAuditEventWriter auditEventWriter)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _auditEventWriter = auditEventWriter;
    }

    public async Task<IReadOnlyList<AgentTemplateCatalogItemDto>> GetTemplatesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var catalog = await AgentTemplateSeedCatalogReader.LoadAsync(cancellationToken);
        var versionsByTemplateId = catalog.Templates.ToDictionary(x => x.TemplateId, x => x.TemplateVersion, StringComparer.OrdinalIgnoreCase);

        var templates = await _dbContext.AgentTemplates
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.RoleName)
            .ToListAsync(cancellationToken);

        return templates
            .Select(x => new AgentTemplateCatalogItemDto(
                x.TemplateId,
                versionsByTemplateId.TryGetValue(x.TemplateId, out var version) ? version : catalog.Version,
                x.RoleName,
                x.Department,
                x.PersonaSummary ?? string.Empty,
                x.DefaultSeniority.ToStorageValue(),
                x.AvatarUrl,
                CloneNodes(x.Personality),
                CloneNodes(x.Objectives),
                CloneNodes(x.Kpis),
                CloneNodes(x.Tools),
                CloneNodes(x.Scopes),
                CloneNodes(x.Thresholds),
                CloneNodes(x.EscalationRules)))
            .ToList();
    }

    public async Task<IReadOnlyList<CompanyAgentSummaryDto>> GetRosterAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var agents = await _dbContext.Agents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.Department)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return agents.Select(ToSummaryDto).ToList();
    }

    public async Task<CreateAgentFromTemplateResultDto> CreateFromTemplateAsync(Guid companyId, CreateAgentFromTemplateCommand command, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        Validate(command);

        var template = await _dbContext.AgentTemplates
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.TemplateId == command.TemplateId && x.IsActive, cancellationToken);

        if (template is null)
        {
            throw new AgentTemplateNotFoundException(command.TemplateId);
        }

        var seniority = string.IsNullOrWhiteSpace(command.Seniority)
            ? template.DefaultSeniority
            : AgentSeniorityValues.Parse(command.Seniority);

        var agent = template.CreateCompanyAgent(
            companyId,
            NormalizeRequired(command.DisplayName),
            command.RoleName,
            command.Department,
            command.AvatarUrl,
            command.Personality,
            seniority);

        _dbContext.Agents.Add(agent);

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                membership.UserId,
                AuditEventActions.AgentHired,
                AuditTargetTypes.Agent,
                agent.Id.ToString(),
                AuditEventOutcomes.Succeeded,
                Metadata: new Dictionary<string, string?>
                {
                    ["templateId"] = template.TemplateId,
                    ["displayName"] = agent.DisplayName,
                    ["roleName"] = agent.RoleName,
                    ["department"] = agent.Department,
                    ["seniority"] = agent.Seniority.ToStorageValue()
                }),
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new CreateAgentFromTemplateResultDto(ToSummaryDto(agent));
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }

        return membership;
    }

    private static CompanyAgentSummaryDto ToSummaryDto(Agent agent) =>
        new(
            agent.Id,
            agent.CompanyId,
            agent.TemplateId,
            agent.DisplayName,
            agent.RoleName,
            agent.Department,
            agent.Seniority.ToStorageValue(),
            agent.Status.ToStorageValue(),
            agent.AvatarUrl, ResolvePersonalitySummary(agent.Personality));

    private static string ResolvePersonalitySummary(IDictionary<string, JsonNode?> personality)
    {
        if (!personality.TryGetValue("summary", out var node) || node is null)
        {
            return string.Empty;
        }

        try
        {
            return NormalizeOptional(node.GetValue<string>()) ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return node.ToJsonString();
        }
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IDictionary<string, JsonNode?>? nodes)
    {
        if (nodes is null || nodes.Count == 0)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private static string NormalizeRequired(string value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Validate(CreateAgentFromTemplateCommand command)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        AddRequired(errors, nameof(command.TemplateId), command.TemplateId, "TemplateId is required.", TemplateIdMaxLength);
        AddRequired(errors, nameof(command.DisplayName), command.DisplayName, "DisplayName is required.", DisplayNameMaxLength);
        AddOptional(errors, nameof(command.Department), command.Department, DepartmentMaxLength);
        AddOptional(errors, nameof(command.RoleName), command.RoleName, RoleNameMaxLength);
        AddOptional(errors, nameof(command.AvatarUrl), command.AvatarUrl, AvatarUrlMaxLength);
        AddOptional(errors, nameof(command.Personality), command.Personality, PersonalitySummaryMaxLength);

        if (!string.IsNullOrWhiteSpace(command.Seniority) && !AgentSeniorityValues.TryParse(command.Seniority, out _))
        {
            AddError(errors, nameof(command.Seniority), AgentSeniorityValues.BuildValidationMessage(command.Seniority));
        }

        if (errors.Count > 0)
        {
            throw new AgentValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static void AddRequired(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        string message,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, key, message);
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            AddError(errors, key, $"{key} must be {maxLength} characters or fewer.");
        }
    }

    private static void AddOptional(
        IDictionary<string, List<string>> errors,
        string key,
        string? value,
        int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            AddError(errors, key, $"{key} must be {maxLength} characters or fewer.");
        }
    }

    private static void AddError(
        IDictionary<string, List<string>> errors,
        string key,
        string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }
}
