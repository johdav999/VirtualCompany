using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Observability;
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
    private const int AuditMetadataValueMaxLength = 512;

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
    private readonly ICorrelationContextAccessor _correlationContextAccessor;

    public CompanyAgentService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IAuditEventWriter auditEventWriter,
        ICorrelationContextAccessor correlationContextAccessor)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _auditEventWriter = auditEventWriter;
        _correlationContextAccessor = correlationContextAccessor;
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

    public async Task<AgentRosterResponseDto> GetRosterViewAsync(
        Guid companyId,
        AgentRosterFilterDto filter,
        CancellationToken cancellationToken)
    {
        await EnsureManagerAccessAsync(companyId, cancellationToken);

        var normalizedFilter = new AgentRosterFilterDto(
            NormalizeFilter(filter.Department),
            NormalizeFilter(filter.Status));

        var tenantAgentsQuery = _dbContext.Agents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        var departments = (await tenantAgentsQuery
            .Select(x => x.Department)
            .Where(x => x != string.Empty)
            .ToListAsync(cancellationToken))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var filteredAgentsQuery = tenantAgentsQuery;

        if (normalizedFilter.Department is not null)
        {
            filteredAgentsQuery = filteredAgentsQuery.Where(x => x.Department == normalizedFilter.Department);
        }

        if (normalizedFilter.Status is not null)
        {
            if (AgentStatusValues.TryParse(normalizedFilter.Status, out var parsedStatus))
            {
                filteredAgentsQuery = filteredAgentsQuery.Where(x => x.Status == parsedStatus);
            }
            else
            {
                filteredAgentsQuery = filteredAgentsQuery.Where(static _ => false);
            }
        }

        var agents = await filteredAgentsQuery
            .OrderBy(x => x.Department)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        var statusOptions = AgentStatusValues.AllowedValues.ToList();

        if (agents.Count == 0)
        {
            return new AgentRosterResponseDto([], departments, statusOptions);
        }

        var agentIds = agents.Select(x => x.Id).ToArray();
        var busyActivityCutoffUtc = DateTime.UtcNow - AgentHealthSummaryCalculator.BusyActivityWindow;
        var auditTargetIds = agentIds.Select(x => x.ToString("N")).ToArray();

        var executionSummaries = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && agentIds.Contains(x.AgentId))
            .GroupBy(x => x.AgentId)
            .Select(group => new AgentExecutionAggregate(
                group.Key,
                group.Count(x => x.Status == ToolExecutionStatus.AwaitingApproval),
                group.Count(x => x.Status == ToolExecutionStatus.Executed),
                group.Count(x => x.Status == ToolExecutionStatus.Executed && (x.ExecutedUtc ?? x.UpdatedUtc) >= busyActivityCutoffUtc),
                group.Count(x => x.Status == ToolExecutionStatus.Failed),
                group.Max(x => (DateTime?)(x.ExecutedUtc ?? x.UpdatedUtc))))
            .ToListAsync(cancellationToken);

        var approvalSummaries = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && agentIds.Contains(x.AgentId))
            .GroupBy(x => x.AgentId)
            .Select(group => new AgentApprovalAggregate(
                group.Key,
                group.Count(x => x.Status == ApprovalRequestStatus.Pending),
                group.Max(x => (DateTime?)x.UpdatedUtc)))
            .ToListAsync(cancellationToken);

        var auditSummaries = await _dbContext.AuditEvents
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.TargetType == AuditTargetTypes.Agent &&
                auditTargetIds.Contains(x.TargetId))
            .GroupBy(x => x.TargetId)
            .Select(group => new AgentAuditAggregate(
                group.Key,
                group.Max(x => (DateTime?)x.OccurredUtc)))
            .ToListAsync(cancellationToken);

        var executionByAgentId = executionSummaries.ToDictionary(x => x.AgentId);
        var approvalByAgentId = approvalSummaries.ToDictionary(x => x.AgentId);
        var auditByTargetId = auditSummaries.ToDictionary(x => x.TargetId, StringComparer.OrdinalIgnoreCase);
        var items = agents
            .Select(agent =>
            {
                executionByAgentId.TryGetValue(agent.Id, out var execution);
                approvalByAgentId.TryGetValue(agent.Id, out var approval);
                auditByTargetId.TryGetValue(agent.Id.ToString("N"), out var audit);

                return new AgentRosterItemDto(
                    agent.Id,
                    agent.CompanyId,
                    agent.TemplateId,
                    agent.DisplayName,
                    agent.RoleName,
                    agent.Department,
                    agent.Seniority.ToStorageValue(),
                    agent.Status.ToStorageValue(),
                    agent.AvatarUrl,
                    ResolvePersonalitySummary(agent.Personality),
                    agent.AutonomyLevel.ToStorageValue(),
                    BuildWorkloadSummary(agent, execution, approval, audit?.LastActivityUtc),
                    BuildAgentProfileRoute(agent.CompanyId, agent.Id));
            })
            .ToList();

        return new AgentRosterResponseDto(
            items,
            departments,
            statusOptions);
    }

    public async Task<AgentProfileViewDto> GetProfileViewAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);

        var agent = await _dbContext.Agents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);

        if (agent is null)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        var busyActivityCutoffUtc = DateTime.UtcNow - AgentHealthSummaryCalculator.BusyActivityWindow;
        var executionAttempts = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AgentId == agentId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(8)
            .ToListAsync(cancellationToken);

        var approvalRequests = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AgentId == agentId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(8)
            .ToListAsync(cancellationToken);

        var executionSummary = await _dbContext.ToolExecutionAttempts
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AgentId == agentId)
            .GroupBy(x => x.AgentId)
            .Select(group => new AgentExecutionAggregate(
                group.Key,
                group.Count(x => x.Status == ToolExecutionStatus.AwaitingApproval),
                group.Count(x => x.Status == ToolExecutionStatus.Executed),
                group.Count(x => x.Status == ToolExecutionStatus.Executed && (x.ExecutedUtc ?? x.UpdatedUtc) >= busyActivityCutoffUtc),
                group.Count(x => x.Status == ToolExecutionStatus.Failed),
                group.Max(x => (DateTime?)(x.ExecutedUtc ?? x.UpdatedUtc))))
            .SingleOrDefaultAsync(cancellationToken);

        var approvalSummary = await _dbContext.ApprovalRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.AgentId == agentId)
            .GroupBy(x => x.AgentId)
            .Select(group => new AgentApprovalAggregate(
                group.Key,
                group.Count(x => x.Status == ApprovalRequestStatus.Pending),
                group.Max(x => (DateTime?)x.UpdatedUtc)))
            .SingleOrDefaultAsync(cancellationToken);

        var auditEvents = await _dbContext.AuditEvents
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.TargetType == AuditTargetTypes.Agent &&
                x.TargetId == agentId.ToString("N"))
            .OrderByDescending(x => x.OccurredUtc)
            .Take(8)
            .ToListAsync(cancellationToken);

        return ToProfileViewDto(agent, membership.MembershipRole, executionSummary, approvalSummary, auditEvents, executionAttempts, approvalRequests);
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
        var membership = await EnsureOperatingProfileAccessAsync(companyId, cancellationToken);
        var visibility = BuildProfileVisibility(membership.MembershipRole);

        var agent = await _dbContext.Agents
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);

        if (agent is null)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        return ToOperatingProfileDto(agent, visibility);
    }

    public async Task<AgentOperatingProfileDto> UpdateOperatingProfileAsync(Guid companyId, Guid agentId, UpdateAgentOperatingProfileCommand command, CancellationToken cancellationToken)
    {
        var membership = await EnsureOperatingProfileAccessAsync(companyId, cancellationToken);
        Validate(command);

        var visibility = BuildProfileVisibility(membership.MembershipRole);

        var agent = await _dbContext.Agents
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);

        if (agent is null)
        {
            throw new KeyNotFoundException("Agent not found.");
        }

        var beforeSnapshot = CreateOperatingProfileAuditSnapshot(agent);
        ValidateOperatingProfileUpdateAccess(command, agent, visibility);

        var changed = agent.UpdateOperatingProfile(
            visibility.CanEditRoleBrief ? command.RoleBrief : agent.RoleBrief,
            ResolveRequestedStatus(agent, command, visibility),
            ResolveRequestedAutonomyLevel(agent, command, visibility),
            visibility.CanEditObjectives ? AgentOperatingProfileJsonMapper.ToJsonDictionary(command.Objectives) : CloneNodes(agent.Objectives),
            visibility.CanEditKpis ? AgentOperatingProfileJsonMapper.ToJsonDictionary(command.Kpis) : CloneNodes(agent.Kpis),
            visibility.CanEditSensitiveGovernance ? AgentOperatingProfileJsonMapper.ToJsonDictionary(command.ToolPermissions) : CloneNodes(agent.Tools),
            visibility.CanEditSensitiveGovernance ? AgentOperatingProfileJsonMapper.ToJsonDictionary(command.DataScopes) : CloneNodes(agent.Scopes),
            visibility.CanEditSensitiveGovernance ? AgentOperatingProfileJsonMapper.ToJsonDictionary(command.ApprovalThresholds) : CloneNodes(agent.Thresholds),
            visibility.CanEditSensitiveGovernance ? AgentOperatingProfileJsonMapper.ToJsonDictionary(command.EscalationRules) : CloneNodes(agent.EscalationRules),
            visibility.CanEditSensitiveGovernance ? AgentOperatingProfileJsonMapper.ToJsonDictionary(command.TriggerLogic) : CloneNodes(agent.TriggerLogic),
            visibility.CanEditWorkingHours ? command.WorkingHours : CloneNodes(agent.WorkingHours));

        if (changed)
        {
            await WriteOperatingProfileAuditEventAsync(companyId, membership, agent, beforeSnapshot, cancellationToken);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return ToOperatingProfileDto(agent, visibility);
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

        var autonomyLevel = string.IsNullOrWhiteSpace(command.AutonomyLevel)
            ? AgentAutonomyLevelValues.DefaultLevel
            : AgentAutonomyLevelValues.Parse(command.AutonomyLevel);

        var agent = template.CreateCompanyAgent(
            companyId,
            NormalizeRequired(command.DisplayName),
            command.RoleName,
            command.Department,
            command.AvatarUrl,
            command.Personality,
            seniority,
            autonomyLevel);

        _dbContext.Agents.Add(agent);

        await WriteAgentCreatedAuditEventAsync(
            companyId, membership, agent, template.TemplateId,
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

    private async Task<ResolvedCompanyMembershipContext> EnsureManagerAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        if (!CanAccessDirectory(membership.MembershipRole))
        {
            throw new UnauthorizedAccessException("Only owner, admin, and manager memberships can view the agent directory.");
        }

        return membership;
    }

    private async Task<ResolvedCompanyMembershipContext> EnsureOperatingProfileAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(companyId, cancellationToken);
        if (!CanAccessOperatingProfileEditor(membership.MembershipRole))
        {
            throw new UnauthorizedAccessException("Only owner, admin, and manager memberships can access agent operating profiles.");
        }

        return membership;
    }

    private static bool CanAccessDirectory(CompanyMembershipRole role) =>
        role is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager;

    private static bool CanAccessOperatingProfileEditor(CompanyMembershipRole role) =>
        role is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager;

    private Task WriteAgentCreatedAuditEventAsync(
        Guid companyId,
        ResolvedCompanyMembershipContext membership,
        Agent agent,
        string templateId,
        CancellationToken cancellationToken)
    {
        var snapshot = CreateOperatingProfileAuditSnapshot(agent);
        var metadata = CreateAuditMetadata(
            ("templateId", templateId),
            ("displayName", agent.DisplayName),
            ("roleName", agent.RoleName),
            ("department", agent.Department),
            ("seniority", agent.Seniority.ToStorageValue()),
            ("status", snapshot.Status),
            ("autonomyLevel", snapshot.AutonomyLevel),
            ("configuredFields", JsonSerializer.Serialize(new[]
            {
                "objectives",
                "kpis",
                "roleBrief",
                "toolPermissions",
                "dataScopes",
                "approvalThresholds",
                "escalationRules",
                "triggerLogic",
                "workingHours",
                "status"
            })),
            ("roleBrief", snapshot.RoleBrief),
            ("objectives", snapshot.ObjectivesJson),
            ("kpis", snapshot.KpisJson),
            ("toolPermissions", snapshot.ToolPermissionsJson),
            ("dataScopes", snapshot.DataScopesJson),
            ("approvalThresholds", snapshot.ApprovalThresholdsJson),
            ("escalationRules", snapshot.EscalationRulesJson),
            ("triggerLogic", snapshot.TriggerLogicJson),
            ("workingHours", snapshot.WorkingHoursJson));

        return _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                membership.UserId,
                AuditEventActions.AgentHired,
                AuditTargetTypes.Agent,
                agent.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                "Hired agent from template and captured the initial operating profile snapshot.",
                ["agent_management", "http_request"],
                metadata,
                CreateCorrelationId()),
            cancellationToken);
    }

    private Task WriteOperatingProfileAuditEventAsync(
        Guid companyId,
        ResolvedCompanyMembershipContext membership,
        Agent agent,
        AgentOperatingProfileAuditSnapshot beforeSnapshot,
        CancellationToken cancellationToken)
    {
        var afterSnapshot = CreateOperatingProfileAuditSnapshot(agent);
        var changes = BuildOperatingProfileChanges(beforeSnapshot, afterSnapshot);
        var action = changes.Count == 1 && string.Equals(changes[0].Field, "status", StringComparison.Ordinal)
            ? AuditEventActions.AgentStatusUpdated
            : AuditEventActions.AgentOperatingProfileUpdated;

        var metadata = CreateAuditMetadata(
            ("templateId", agent.TemplateId),
            ("displayName", agent.DisplayName),
            ("status", afterSnapshot.Status),
            ("autonomyLevel", afterSnapshot.AutonomyLevel),
            ("changedFieldCount", changes.Count.ToString(CultureInfo.InvariantCulture)),
            ("changedFields", JsonSerializer.Serialize(changes.Select(static change => change.Field).ToArray())));

        foreach (var change in changes)
        {
            metadata[$"{change.Field}Before"] = PrepareMetadataValue(change.Before);
            metadata[$"{change.Field}After"] = PrepareMetadataValue(change.After);
        }

        return _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                membership.UserId,
                action,
                AuditTargetTypes.Agent,
                agent.Id.ToString("N"),
                AuditEventOutcomes.Succeeded,
                BuildOperatingProfileRationaleSummary(action, afterSnapshot, changes),
                ["agent_management", "http_request"],
                metadata,
                CreateCorrelationId()),
            cancellationToken);
    }

    private string CreateCorrelationId() =>
        string.IsNullOrWhiteSpace(_correlationContextAccessor.CorrelationId)
            ? Activity.Current?.Id ?? Guid.NewGuid().ToString("N")
            : _correlationContextAccessor.CorrelationId!;

    private static AgentOperatingProfileAuditSnapshot CreateOperatingProfileAuditSnapshot(Agent agent) =>
        new(
            agent.RoleBrief,
            agent.Status.ToStorageValue(),
            agent.AutonomyLevel.ToStorageValue(),
            SerializeJsonDictionary(agent.Objectives),
            SerializeJsonDictionary(agent.Kpis),
            SerializeJsonDictionary(agent.Tools),
            SerializeJsonDictionary(agent.Scopes),
            SerializeJsonDictionary(agent.Thresholds),
            SerializeJsonDictionary(agent.EscalationRules),
            SerializeJsonDictionary(agent.TriggerLogic),
            SerializeJsonDictionary(agent.WorkingHours));

    private static List<AgentOperatingProfileAuditChange> BuildOperatingProfileChanges(
        AgentOperatingProfileAuditSnapshot before,
        AgentOperatingProfileAuditSnapshot after)
    {
        var changes = new List<AgentOperatingProfileAuditChange>();

        AddOperatingProfileChange(changes, "objectives", before.ObjectivesJson, after.ObjectivesJson);
        AddOperatingProfileChange(changes, "kpis", before.KpisJson, after.KpisJson);
        AddOperatingProfileChange(changes, "roleBrief", before.RoleBrief, after.RoleBrief);
        AddOperatingProfileChange(changes, "toolPermissions", before.ToolPermissionsJson, after.ToolPermissionsJson);
        AddOperatingProfileChange(changes, "dataScopes", before.DataScopesJson, after.DataScopesJson);
        AddOperatingProfileChange(changes, "approvalThresholds", before.ApprovalThresholdsJson, after.ApprovalThresholdsJson);
        AddOperatingProfileChange(changes, "escalationRules", before.EscalationRulesJson, after.EscalationRulesJson);
        AddOperatingProfileChange(changes, "triggerLogic", before.TriggerLogicJson, after.TriggerLogicJson);
        AddOperatingProfileChange(changes, "workingHours", before.WorkingHoursJson, after.WorkingHoursJson);
        AddOperatingProfileChange(changes, "status", before.Status, after.Status);
        AddOperatingProfileChange(changes, "autonomyLevel", before.AutonomyLevel, after.AutonomyLevel);

        return changes;
    }

    private static void AddOperatingProfileChange(
        ICollection<AgentOperatingProfileAuditChange> changes,
        string field,
        string? before,
        string? after)
    {
        if (!string.Equals(before, after, StringComparison.Ordinal))
        {
            changes.Add(new AgentOperatingProfileAuditChange(field, before, after));
        }
    }

    private static string BuildOperatingProfileRationaleSummary(
        string action,
        AgentOperatingProfileAuditSnapshot afterSnapshot,
        IReadOnlyCollection<AgentOperatingProfileAuditChange> changes)
    {
        if (string.Equals(action, AuditEventActions.AgentStatusUpdated, StringComparison.Ordinal))
        {
            return $"Updated agent status to {afterSnapshot.Status}.";
        }

        return $"Updated agent operating profile: {string.Join(", ", changes.Select(static change => DescribeOperatingProfileField(change.Field)))}.";
    }

    private static string DescribeOperatingProfileField(string field) => field switch
    {
        "objectives" => "objectives",
        "kpis" => "KPIs",
        "roleBrief" => "role brief",
        "toolPermissions" => "tool permissions",
        "dataScopes" => "data scopes",
        "approvalThresholds" => "approval thresholds",
        "escalationRules" => "escalation rules",
        "triggerLogic" => "trigger logic",
        "workingHours" => "working hours",
        "status" => "status",
        "autonomyLevel" => "autonomy level",
        _ => field
    };

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
            agent.AvatarUrl,
            ResolvePersonalitySummary(agent.Personality),
            agent.AutonomyLevel.ToStorageValue());

    private static AgentOperatingProfileDto ToOperatingProfileDto(Agent agent, AgentProfileVisibilityDto visibility) =>
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
            visibility.CanViewPermissions ? CloneNodes(agent.Tools) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility.CanViewPermissions ? CloneNodes(agent.Scopes) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility.CanViewThresholds ? CloneNodes(agent.Thresholds) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility.CanViewThresholds ? CloneNodes(agent.EscalationRules) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility.CanEditSensitiveGovernance ? CloneNodes(agent.TriggerLogic) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility.CanViewWorkingHours ? CloneNodes(agent.WorkingHours) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility,
            agent.UpdatedUtc,
            agent.CanReceiveAssignments,
            agent.AutonomyLevel.ToStorageValue());

    private static AgentProfileViewDto ToProfileViewDto(
        Agent agent,
        CompanyMembershipRole membershipRole,
        AgentExecutionAggregate? execution,
        AgentApprovalAggregate? approval,
        IReadOnlyList<AuditEvent> auditEvents,
        IReadOnlyList<ToolExecutionAttempt> executionAttempts,
        IReadOnlyList<ApprovalRequest> approvalRequests)
    {
        var visibility = BuildProfileVisibility(membershipRole);
        var workloadSummary = BuildWorkloadSummary(
            agent,
            execution,
            approval,
            auditEvents.Count == 0 ? null : auditEvents.Max(x => (DateTime?)x.OccurredUtc));

        var recentActivity = executionAttempts
            .Select(x => new AgentRecentActivityDto(
                x.ExecutedUtc ?? x.UpdatedUtc,
                "tool_execution",
                $"{x.ToolName} ({x.ActionType.ToStorageValue()})",
                x.Status.ToStorageValue(),
                string.IsNullOrWhiteSpace(x.Scope) ? null : $"Scope: {x.Scope}"))
            .Concat(approvalRequests.Select(x => new AgentRecentActivityDto(
                x.UpdatedUtc,
                "approval_request",
                $"{x.ToolName} approval",
                x.Status.ToStorageValue(),
                string.IsNullOrWhiteSpace(x.ApprovalTarget) ? null : $"Target: {x.ApprovalTarget}")))
            .Concat((visibility.CanEditSensitiveGovernance ? auditEvents : []).Select(x => new AgentRecentActivityDto(
                x.OccurredUtc,
                "audit_event",
                x.Action,
                x.Outcome,
                x.RationaleSummary)))
            .OrderByDescending(x => x.OccurredUtc)
            .Take(8)
            .ToList();

        return new AgentProfileViewDto(
            agent.Id,
            agent.CompanyId,
            agent.TemplateId,
            agent.DisplayName,
            agent.RoleName,
            agent.Department,
            agent.Seniority.ToStorageValue(),
            agent.Status.ToStorageValue(),
            agent.AvatarUrl,
            ResolvePersonalitySummary(agent.Personality),
            agent.RoleBrief,
            agent.AutonomyLevel.ToStorageValue(),
            CloneNodes(agent.Objectives),
            CloneNodes(agent.Kpis),
            visibility.CanViewPermissions ? CloneNodes(agent.Tools) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility.CanViewPermissions ? CloneNodes(agent.Scopes) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility.CanViewThresholds ? CloneNodes(agent.Thresholds) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility.CanViewThresholds ? CloneNodes(agent.EscalationRules) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            visibility.CanViewWorkingHours ? CloneNodes(agent.WorkingHours) : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            workloadSummary,
            recentActivity,
            visibility,
            BuildAgentProfileRoute(agent.CompanyId, agent.Id),
            BuildProfileSections(visibility),
            BuildAnalyticsPreview(),
            agent.UpdatedUtc);
    }

    private static AgentProfileVisibilityDto BuildProfileVisibility(CompanyMembershipRole membershipRole)
    {
        var canEditSensitiveGovernance = membershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin;
        var canEditOperationalFields = canEditSensitiveGovernance || membershipRole == CompanyMembershipRole.Manager;

        return new AgentProfileVisibilityDto(
            canEditSensitiveGovernance,
            canEditSensitiveGovernance,
            canEditOperationalFields,
            canEditOperationalFields,
            canEditOperationalFields,
            canEditOperationalFields,
            canEditOperationalFields,
            canEditOperationalFields,
            canEditOperationalFields,
            canEditSensitiveGovernance,
            canEditSensitiveGovernance);
    }

    private static IReadOnlyList<AgentProfileSectionDto> BuildProfileSections(AgentProfileVisibilityDto visibility) =>
        [
            new("overview", "Overview", "Identity, current workload, and health summary.", true),
            new("objectives", "Objectives", "Current objectives and operating intent.", true),
            new("permissions", "Permissions", "Tool permissions and data scopes.", visibility.CanViewPermissions),
            new("thresholds", "Thresholds", "Approval thresholds and escalation rules.", visibility.CanViewThresholds),
            new("working-hours", "Working hours", "Availability and coverage windows.", visibility.CanViewWorkingHours),
            new("recent-activity", "Recent activity", "Latest executions, approvals, and tenant-scoped events.", true),
            new("analytics", "Analytics", "Reserved profile surface for future KPI, health, and trend modules.", true)
        ];

    private static AgentProfileAnalyticsPreviewDto BuildAnalyticsPreview() =>
        new(
            "analytics",
            "Profile analytics",
            "Future KPI, workload, health, and trend modules should extend the agent profile instead of branching to a separate destination.",
            [
                "Workload and health summary",
                "Recent activity rollups",
                "KPI and metrics panels",
                "Trend and analytics modules"
            ]);

    private static void ValidateOperatingProfileUpdateAccess(
        UpdateAgentOperatingProfileCommand command,
        Agent agent,
        AgentProfileVisibilityDto visibility)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        EnsureStatusChangeIsAuthorized(errors, command, agent, visibility);
        EnsureOptionalStringChangeIsAuthorized(errors, nameof(command.RoleBrief), "role brief", visibility.CanEditRoleBrief, command.RoleBrief, agent.RoleBrief);
        EnsureJsonChangeIsAuthorized(errors, nameof(command.Objectives), "objectives", visibility.CanEditObjectives, AgentOperatingProfileJsonMapper.ToJsonDictionary(command.Objectives), agent.Objectives);
        EnsureJsonChangeIsAuthorized(errors, nameof(command.Kpis), "KPIs", visibility.CanEditKpis, AgentOperatingProfileJsonMapper.ToJsonDictionary(command.Kpis), agent.Kpis);
        EnsureJsonChangeIsAuthorized(errors, nameof(command.ToolPermissions), "tool permissions", visibility.CanEditSensitiveGovernance, AgentOperatingProfileJsonMapper.ToJsonDictionary(command.ToolPermissions), agent.Tools);
        EnsureJsonChangeIsAuthorized(errors, nameof(command.DataScopes), "data scopes", visibility.CanEditSensitiveGovernance, AgentOperatingProfileJsonMapper.ToJsonDictionary(command.DataScopes), agent.Scopes);
        EnsureJsonChangeIsAuthorized(errors, nameof(command.ApprovalThresholds), "approval thresholds", visibility.CanEditSensitiveGovernance, AgentOperatingProfileJsonMapper.ToJsonDictionary(command.ApprovalThresholds), agent.Thresholds);
        EnsureJsonChangeIsAuthorized(errors, nameof(command.EscalationRules), "escalation rules", visibility.CanEditSensitiveGovernance, AgentOperatingProfileJsonMapper.ToJsonDictionary(command.EscalationRules), agent.EscalationRules);
        EnsureJsonChangeIsAuthorized(errors, nameof(command.TriggerLogic), "trigger logic", visibility.CanEditSensitiveGovernance, AgentOperatingProfileJsonMapper.ToJsonDictionary(command.TriggerLogic), agent.TriggerLogic);
        EnsureJsonChangeIsAuthorized(errors, nameof(command.WorkingHours), "working hours", visibility.CanEditWorkingHours, command.WorkingHours, agent.WorkingHours);

        var requestedAutonomyLevel = NormalizeOptional(command.AutonomyLevel);
        if (!visibility.CanEditSensitiveGovernance &&
            requestedAutonomyLevel is not null &&
            !string.Equals(requestedAutonomyLevel, agent.AutonomyLevel.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, nameof(command.AutonomyLevel), "Only owner and admin memberships can edit autonomy level.");
        }

        if (errors.Count > 0)
        {
            throw new AgentValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static AgentStatus ResolveRequestedStatus(Agent agent, UpdateAgentOperatingProfileCommand command, AgentProfileVisibilityDto visibility)
    {
        var requestedStatus = NormalizeRequired(command.Status);
        var currentStatus = agent.Status.ToStorageValue();

        if (!string.Equals(requestedStatus, currentStatus, StringComparison.OrdinalIgnoreCase))
        {
            return AgentStatusValues.Parse(command.Status);
        }

        return agent.Status;
    }

    private static AgentAutonomyLevel ResolveRequestedAutonomyLevel(Agent agent, UpdateAgentOperatingProfileCommand command, AgentProfileVisibilityDto visibility) =>
        visibility.CanEditSensitiveGovernance && !string.IsNullOrWhiteSpace(command.AutonomyLevel)
            ? AgentAutonomyLevelValues.Parse(command.AutonomyLevel)
            : agent.AutonomyLevel;

    private static void EnsureStatusChangeIsAuthorized(
        IDictionary<string, List<string>> errors,
        UpdateAgentOperatingProfileCommand command,
        Agent agent,
        AgentProfileVisibilityDto visibility)
    {
        var requestedStatus = NormalizeRequired(command.Status);
        var currentStatus = agent.Status.ToStorageValue();

        if (string.Equals(requestedStatus, currentStatus, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!visibility.CanEditStatus)
        {
            AddError(errors, nameof(command.Status), "Your membership cannot change agent status.");
            return;
        }

        if (!visibility.CanPauseOrRestrictAgent &&
            (!IsManagerEditableStatus(currentStatus) || !IsManagerEditableStatus(requestedStatus)))
        {
            AddError(errors, nameof(command.Status), "Only owner and admin memberships can change an agent to or from restricted or archived status.");
        }
    }

    private static void EnsureOptionalStringChangeIsAuthorized(
        IDictionary<string, List<string>> errors,
        string key,
        string fieldName,
        bool canEdit,
        string? requestedValue,
        string? currentValue)
    {
        if (canEdit)
        {
            return;
        }

        var normalizedRequested = NormalizeOptional(requestedValue);
        var normalizedCurrent = NormalizeOptional(currentValue);
        if (!string.IsNullOrEmpty(normalizedRequested) &&
            !string.Equals(normalizedRequested, normalizedCurrent, StringComparison.Ordinal))
        {
            AddError(errors, key, $"Only owner, admin, or manager memberships with edit access can change {fieldName}.");
        }
    }

    private static void EnsureJsonChangeIsAuthorized(
        IDictionary<string, List<string>> errors,
        string key,
        string fieldName,
        bool canEdit,
        IDictionary<string, JsonNode?>? requestedValue,
        IDictionary<string, JsonNode?> currentValue)
    {
        if (canEdit || requestedValue is null)
        {
            return;
        }

        if (!JsonDictionaryEquals(requestedValue, currentValue))
        {
            AddError(errors, key, $"You do not have permission to edit {fieldName}.");
        }
    }

    private static bool JsonDictionaryEquals(IDictionary<string, JsonNode?>? left, IDictionary<string, JsonNode?>? right) =>
        string.Equals(
            SerializeJsonDictionary(CloneNodes(left)),
            SerializeJsonDictionary(CloneNodes(right)),
            StringComparison.Ordinal);

    private static bool IsManagerEditableStatus(string status) =>
        string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "paused", StringComparison.OrdinalIgnoreCase);

    private static AgentWorkloadSummaryDto BuildWorkloadSummary(
        Agent agent,
        AgentExecutionAggregate? execution,
        AgentApprovalAggregate? approval,
        DateTime? auditActivityUtc)
    {
        var awaitingApprovalCount = (execution?.AwaitingApprovalCount ?? 0) + (approval?.PendingApprovalCount ?? 0);
        var failedCount = execution?.FailedCount ?? 0;
        var recentExecutionCount = execution?.RecentExecutionCount ?? 0;
        var executedCount = execution?.ExecutedCount ?? 0;
        var deduplicatedAwaitingApprovalCount = Math.Max(execution?.AwaitingApprovalCount ?? 0, approval?.PendingApprovalCount ?? 0);
        var openItemsCount = deduplicatedAwaitingApprovalCount + failedCount;
        var lastActivityUtc = MaxUtc(execution?.LastActivityUtc, approval?.LastActivityUtc, auditActivityUtc);
        var healthSummary = AgentHealthSummaryCalculator.Calculate(
            new AgentHealthDerivationInput(
                deduplicatedAwaitingApprovalCount,
                failedCount,
                deduplicatedAwaitingApprovalCount + recentExecutionCount,
                lastActivityUtc),
            DateTime.UtcNow);

        return new AgentWorkloadSummaryDto(
            openItemsCount,
            deduplicatedAwaitingApprovalCount,
            executedCount,
            failedCount,
            lastActivityUtc,
            healthSummary,
            healthSummary.Status,
            AgentHealthSummaryCalculator.BuildSummaryText(healthSummary));
    }

    private static string BuildAgentProfileRoute(Guid companyId, Guid agentId) =>
        $"/agents/{agentId}?companyId={companyId}";

    private static DateTime? MaxUtc(params DateTime?[] values)
    {
        DateTime? current = null;

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            current = current is null || value > current ? value : current;
        }

        return current;
    }

    private static string? NormalizeFilter(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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

        if (!string.IsNullOrWhiteSpace(command.AutonomyLevel) &&
            !AgentAutonomyLevelValues.TryParse(command.AutonomyLevel, out _))
        {
            AddError(errors, nameof(command.AutonomyLevel), AgentAutonomyLevelValues.BuildValidationMessage(command.AutonomyLevel));
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

    private static string SerializeJsonDictionary(IReadOnlyDictionary<string, JsonNode?> values)
    {
        var json = new JsonObject();
        foreach (var pair in values.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            json[pair.Key] = pair.Value?.DeepClone();
        }

        return json.ToJsonString();
    }

    private static Dictionary<string, string?> CreateAuditMetadata(params (string Key, string? Value)[] values)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in values)
        {
            metadata[key] = PrepareMetadataValue(value);
        }

        return metadata;
    }

    private static string? PrepareMetadataValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= AuditMetadataValueMaxLength
            ? trimmed
            : $"{trimmed[..(AuditMetadataValueMaxLength - 3)]}...";
    }

    private sealed record AgentOperatingProfileAuditSnapshot(
        string? RoleBrief,
        string Status,
        string AutonomyLevel,
        string ObjectivesJson,
        string KpisJson,
        string ToolPermissionsJson,
        string DataScopesJson,
        string ApprovalThresholdsJson,
        string EscalationRulesJson,
        string TriggerLogicJson,
        string WorkingHoursJson);

    private sealed record AgentExecutionAggregate(Guid AgentId, int AwaitingApprovalCount, int ExecutedCount, int RecentExecutionCount, int FailedCount, DateTime? LastActivityUtc);
    private sealed record AgentApprovalAggregate(Guid AgentId, int PendingApprovalCount, DateTime? LastActivityUtc);
    private sealed record AgentAuditAggregate(string TargetId, DateTime? LastActivityUtc);

    private sealed record AgentOperatingProfileAuditChange(string Field, string? Before, string? After);
}
