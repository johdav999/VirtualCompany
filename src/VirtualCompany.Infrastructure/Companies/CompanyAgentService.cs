using System.Globalization;
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
    private const int RoleBriefMaxLength = 4000;
    private const int AvatarUrlMaxLength = 2048;
    private const int PersonalitySummaryMaxLength = 1000;

    private static readonly HashSet<string> SupportedWorkingDays = new(StringComparer.OrdinalIgnoreCase)
    {
        "monday",
        "tuesday",
        "wednesday",
        "thursday",
        "friday",
        "saturday",
        "sunday"
    };

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

        var templates = await _dbContext.AgentTemplates
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.RoleName)
            .ToListAsync(cancellationToken);

        return templates
            .Select(x => new AgentTemplateCatalogItemDto(
                x.TemplateId,
                AgentTemplateSeedData.TryGetTemplateVersion(x.TemplateId, out var version) ? version : AgentTemplateSeedData.BaselineTemplateVersion,
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

    public async Task<AgentOperatingProfileDto> GetOperatingProfileAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken)
    {
        await EnsureAdministrativeAccessAsync(companyId, cancellationToken);

        var agent = await _dbContext.Agents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);

        if (agent is null)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        return ToOperatingProfileDto(agent);
    }

    public async Task<AgentOperatingProfileDto> UpdateOperatingProfileAsync(Guid companyId, Guid agentId, UpdateAgentOperatingProfileCommand command, CancellationToken cancellationToken)
    {
        var membership = await EnsureAdministrativeAccessAsync(companyId, cancellationToken);
        Validate(command);

        var agent = await _dbContext.Agents
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);

        if (agent is null)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        var changed = agent.UpdateOperatingProfile(
            command.RoleBrief,
            AgentStatusValues.Parse(command.Status),
            command.Objectives,
            command.Kpis,
            command.ToolPermissions,
            command.DataScopes, 
            command.ApprovalThresholds,
            command.EscalationRules,
            command.TriggerLogic,
            command.WorkingHours);

        if (changed)
        {
            await _auditEventWriter.WriteAsync(
                new AuditEventWriteRequest(
                    companyId,
                    AuditActorTypes.User,
                    membership.UserId,
                    AuditEventActions.AgentOperatingProfileUpdated,
                    AuditTargetTypes.Agent,
                    agent.Id.ToString("N"),
                    AuditEventOutcomes.Succeeded,
                    Metadata: new Dictionary<string, string?>
                    {
                        ["status"] = agent.Status.ToStorageValue(),
                        ["displayName"] = agent.DisplayName
                    }),
                cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToOperatingProfileDto(agent);
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

    private async Task<ResolvedCompanyMembershipContext> EnsureAdministrativeAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        if (membership.MembershipRole != CompanyMembershipRole.Owner &&
            membership.MembershipRole != CompanyMembershipRole.Admin)
        {
            throw new UnauthorizedAccessException("Only owner and admin memberships can manage agent operating profiles.");
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

    private static AgentOperatingProfileDto ToOperatingProfileDto(Agent agent) =>
        new(
            agent.Id,
            agent.CompanyId,
            agent.TemplateId,
            agent.DisplayName,
            agent.RoleName,
            agent.Department,
            agent.Seniority.ToStorageValue(),
            agent.Status.ToStorageValue(),
            agent.AvatarUrl,
            agent.RoleBrief,
            CloneNodes(agent.Objectives),
            CloneNodes(agent.Kpis),
            CloneNodes(agent.Tools),
            CloneNodes(agent.Scopes),
            CloneNodes(agent.Thresholds),
            CloneNodes(agent.EscalationRules),
            CloneNodes(agent.TriggerLogic),
            CloneNodes(agent.WorkingHours),
            agent.UpdatedUtc,
            agent.CanReceiveAssignments);

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
        AddAvatarReference(errors, nameof(command.AvatarUrl), command.AvatarUrl);
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

    private static void Validate(UpdateAgentOperatingProfileCommand command)
    {
        UpdateAgentOperatingProfileCommandValidator.ValidateAndThrow(command);
    }

    private static void ValidateJsonObject(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? value,
        bool requireNonEmpty)
    {
        if (value is null || value.Count == 0)
        {
            if (requireNonEmpty)
            {
                AddError(errors, key, $"{key} must contain at least one entry.");
            }

            return;
        }

        foreach (var (entryKey, entryValue) in value)
        {
            if (string.IsNullOrWhiteSpace(entryKey))
            {
                AddError(errors, key, $"{key} contains an empty property name.");
            }

            if (entryValue is null)
            {
                AddError(errors, key, $"{key}.{entryKey} cannot be null.");
            }
        }
    }

    private static void ValidateStringArray(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload,
        string propertyName)
    {
        if (payload is null ||
            !payload.TryGetValue(propertyName, out var node) ||
            node is null)
        {
            return;
        }

        if (node is not JsonArray array)
        {
            AddError(errors, key, $"{key}.{propertyName} must be an array of strings.");
            return;
        }

        foreach (var item in array)
        {
            if (item is null || item is not JsonValue value || !value.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
            {
                AddError(errors, key, $"{key}.{propertyName} must contain non-empty string values.");
                return;
            }
        }
    }

    private static void ValidateApprovalThresholds(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null)
        {
            return;
        }

        foreach (var (entryKey, node) in payload)
        {
            ValidateThresholdNode(errors, key, $"{key}.{entryKey}", node);
        }
    }

    private static void ValidateThresholdNode(
        IDictionary<string, List<string>> errors,
        string key,
        string path,
        JsonNode? node)
    {
        if (node is null)
        {
            AddError(errors, key, $"{path} cannot be null.");
            return;
        }

        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject)
            {
                ValidateThresholdNode(errors, key, $"{path}.{property.Key}", property.Value);
            }

            return;
        }

        if (node is JsonArray jsonArray)
        {
            foreach (var item in jsonArray)
            {
                ValidateThresholdNode(errors, key, path, item);
            }

            return;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<decimal>(out var decimalValue) && decimalValue < 0)
            {
                AddError(errors, key, $"{path} must be non-negative.");
            }
            else if (jsonValue.TryGetValue<double>(out var doubleValue) && doubleValue < 0)
            {
                AddError(errors, key, $"{path} must be non-negative.");
            }
        }
    }

    private static void ValidateEscalationRules(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return;
        }

        var hasRoute = false;

        if (payload.TryGetValue("critical", out var criticalNode))
        {
            ValidateStringArray(errors, key, payload, "critical");
            hasRoute = criticalNode is JsonArray criticalArray && criticalArray.Count > 0;
        }

        if (payload.TryGetValue("escalateTo", out var escalateToNode))
        {
            if (!TryGetNonEmptyString(escalateToNode, out _))
            {
                AddError(errors, key, $"{key}.escalateTo must be a non-empty string.");
            }
            else
            {
                hasRoute = true;
            }
        }

        if (payload.TryGetValue("notifyAfterMinutes", out var notifyAfterNode) &&
            !TryGetNonNegativeNumber(notifyAfterNode, out _))
        {
            AddError(errors, key, $"{key}.notifyAfterMinutes must be a non-negative number.");
        }

        if (!hasRoute)
        {
            AddError(errors, key, $"{key} must define at least one escalation route.");
        }
    }

    private static void ValidateTriggerLogic(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return;
        }

        var isEnabled = false;
        if (payload.TryGetValue("enabled", out var enabledNode) &&
            enabledNode is not null)
        {
            if (enabledNode is not JsonValue enabledValue || !enabledValue.TryGetValue<bool>(out isEnabled))
            {
                AddError(errors, key, $"{key}.enabled must be a boolean.");
            }
        }

        if (!payload.TryGetValue("conditions", out var conditionsNode) || conditionsNode is null)
        {
            if (isEnabled)
            {
                AddError(errors, key, $"{key}.conditions must contain at least one condition when enabled is true.");
            }

            return;
        }

        if (conditionsNode is not JsonArray conditions)
        {
            AddError(errors, key, $"{key}.conditions must be an array.");
            return;
        }

        if (isEnabled && conditions.Count == 0)
        {
            AddError(errors, key, $"{key}.conditions must contain at least one condition when enabled is true.");
        }

        foreach (var condition in conditions)
        {
            if (condition is not JsonObject conditionObject)
            {
                AddError(errors, key, $"{key}.conditions must contain objects.");
                return;
            }

            if (conditionObject.Any(property => string.IsNullOrWhiteSpace(property.Key)))
            {
                AddError(errors, key, $"{key}.conditions entries cannot contain empty property names.");
                return;
            }

            var eventName = conditionObject["event"]?.GetValue<string>();
            var typeName = conditionObject["type"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(eventName) && string.IsNullOrWhiteSpace(typeName))
            {
                AddError(errors, key, $"{key}.conditions entries must define either 'event' or 'type'.");
                return;
            }
        }
    }

    private static void ValidateWorkingHours(
        IDictionary<string, List<string>> errors,
        string key,
        IDictionary<string, JsonNode?>? payload)
    {
        if (payload is null || payload.Count == 0)
        {
            return;
        }

        if (!payload.TryGetValue("timezone", out var timezoneNode) ||
            timezoneNode is not JsonValue timezoneValue ||
            !timezoneValue.TryGetValue<string>(out var timezone) ||
            string.IsNullOrWhiteSpace(timezone))
        {
            AddError(errors, key, $"{key}.timezone is required.");
        }
        else if (!IsValidTimeZoneId(timezone))
        {
            AddError(errors, key, $"{key}.timezone must be a valid timezone identifier.");
        }

        if (!payload.TryGetValue("windows", out var windowsNode) || windowsNode is null)
        {
            AddError(errors, key, $"{key}.windows is required.");
            return;
        }

        var windowsByDay = new Dictionary<string, List<(TimeOnly Start, TimeOnly End)>>(StringComparer.OrdinalIgnoreCase);

        if (windowsNode is not JsonArray windows || windows.Count == 0)
        {
            AddError(errors, key, $"{key}.windows must contain at least one time window.");
            return;
        }

        foreach (var window in windows)
        {
            if (window is not JsonObject windowObject)
            {
                AddError(errors, key, $"{key}.windows must contain objects.");
                return;
            }

            var day = windowObject["day"]?.GetValue<string>();
            var start = windowObject["start"]?.GetValue<string>();
            var end = windowObject["end"]?.GetValue<string>();

            if (string.IsNullOrWhiteSpace(day))
            {
                AddError(errors, key, $"{key}.windows.day is required.");
                return;
            }

            var normalizedDay = day.Trim();
            if (!SupportedWorkingDays.Contains(normalizedDay))
            {
                AddError(errors, key, $"{key}.windows.day must be a valid weekday.");
                return;
            }

            if (!TimeOnly.TryParseExact(start, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startTime) ||
                !TimeOnly.TryParseExact(end, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endTime))
            {
                AddError(errors, key, $"{key}.windows start and end must use HH:mm format.");
                return;
            }

            if (endTime <= startTime)
            {
                AddError(errors, key, $"{key}.windows end must be later than start.");
                return;
            }

            if (!windowsByDay.TryGetValue(normalizedDay, out var dayWindows))
            {
                dayWindows = [];
                windowsByDay[normalizedDay] = dayWindows;
            }

            if (dayWindows.Any(existing => startTime < existing.End && endTime > existing.Start))
            {
                AddError(errors, key, $"{key}.windows cannot contain overlapping intervals for the same day.");
                return;
            }

            dayWindows.Add((startTime, endTime));
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

    private static void AddAvatarReference(
        IDictionary<string, List<string>> errors,
        string key,
        string? value)
    {
        if (!AvatarReferenceRules.TryValidate(value, key, AvatarUrlMaxLength, out var error))
        {
            AddError(errors, key, error!);
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

    private static bool TryGetNonEmptyString(JsonNode? node, out string value)
    {
        value = string.Empty;

        if (node is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<string>(out var text) ||
            string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text.Trim();
        return true;
    }

    private static bool TryGetNonNegativeNumber(JsonNode? node, out decimal value)
    {
        value = 0;

        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        if (jsonValue.TryGetValue<decimal>(out var decimalValue))
        {
            value = decimalValue;
            return decimalValue >= 0;
        }

        if (jsonValue.TryGetValue<double>(out var doubleValue))
        {
            value = (decimal)doubleValue;
            return doubleValue >= 0;
        }

        return false;
    }

    private static bool IsValidTimeZoneId(string timezone)
    {
        try
        {
            _ = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return string.Equals(timezone, "UTC", StringComparison.OrdinalIgnoreCase) ||
                   (timezone.Contains('/', StringComparison.Ordinal) && timezone.All(ch => char.IsLetterOrDigit(ch) || ch is '/' or '_' or '-' or '+'));
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }
}
