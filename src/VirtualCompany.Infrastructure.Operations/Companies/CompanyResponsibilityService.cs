using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyResponsibilityService : ICompanyResponsibilityService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipResolver;
    private readonly IAuditEventWriter _auditWriter;
    private readonly ICorrelationContextAccessor _correlationAccessor;
    private readonly IExecutiveCockpitDashboardCacheInvalidator? _dashboardCache;

    public CompanyResponsibilityService(VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipResolver, IAuditEventWriter auditWriter,
        ICorrelationContextAccessor correlationAccessor,
        IExecutiveCockpitDashboardCacheInvalidator? dashboardCache = null)
    {
        _dbContext = dbContext;
        _membershipResolver = membershipResolver;
        _auditWriter = auditWriter;
        _correlationAccessor = correlationAccessor;
        _dashboardCache = dashboardCache;
    }

    public async Task<CompanyResponsibilitiesDto> GetAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var actor = await RequireActiveMemberAsync(companyId, false, cancellationToken);
        var company = await _dbContext.Companies.AsNoTracking().SingleAsync(x => x.Id == companyId, cancellationToken);
        var assignments = await LoadAssignmentsAsync(companyId, cancellationToken);
        var members = await _dbContext.CompanyMemberships.AsNoTracking().Include(x => x.User)
            .Where(x => x.CompanyId == companyId && x.Status == CompanyMembershipStatus.Active)
            .OrderBy(x => x.Role).ThenBy(x => x.User != null ? x.User.DisplayName : x.InvitedEmail)
            .ToListAsync(cancellationToken);
        var agents = await _dbContext.Agents.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == AgentStatus.Active)
            .OrderBy(x => x.Department).ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
        return new(companyId, company.SizeBand, assignments.Select(Map).ToArray(), Presets,
            actor.MembershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin,
            members.Select(Map).ToArray(), agents.Select(Map).ToArray());
    }

    public async Task<ResponsibilityPresetPreviewDto> PreviewPresetAsync(Guid companyId, ResponsibilityPresetRequest request,
        CancellationToken cancellationToken)
    {
        await RequireActiveMemberAsync(companyId, false, cancellationToken);
        return await BuildPreviewAsync(companyId, request, cancellationToken);
    }

    public async Task<ResponsibilityPresetApplyResultDto> ApplyPresetAsync(Guid companyId, ResponsibilityPresetRequest request,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActiveMemberAsync(companyId, true, cancellationToken);
        var preview = await BuildPreviewAsync(companyId, request, cancellationToken);
        var assignments = await LoadAssignmentsAsync(companyId, cancellationToken, tracking: true);
        var byPrimaryArea = assignments.Where(x => x.AssignmentKind == ResponsibilityAssignmentKind.Primary)
            .ToDictionary(x => x.ResponsibilityArea);

        foreach (var change in preview.Changes.Where(x => x.ChangeKind != ResponsibilityPresetChangeKind.Retain))
        {
            CompanyResponsibilityAssignment? target = null;
            if (change.AssignmentKind == ResponsibilityAssignmentKind.Primary)
            {
                byPrimaryArea.TryGetValue(change.ResponsibilityArea, out target);
            }
            else
            {
                target = assignments.FirstOrDefault(x => x.AssignmentKind == change.AssignmentKind &&
                    x.ResponsibilityArea == change.ResponsibilityArea && x.AssignedMembershipId == change.AssignedMembershipId);
            }

            if (target is null)
            {
                target = new CompanyResponsibilityAssignment(Guid.NewGuid(), companyId, change.ResponsibilityArea,
                    change.AssignmentKind, change.AssignedMembershipId, change.PrimaryAgentId,
                    AgentAutonomyLevel.Level1, null, null);
                _dbContext.CompanyResponsibilityAssignments.Add(target);
                assignments.Add(target);
                if (target.AssignmentKind == ResponsibilityAssignmentKind.Primary) byPrimaryArea[target.ResponsibilityArea] = target;
            }
            else
            {
                target.Update(change.AssignedMembershipId, change.PrimaryAgentId, target.AuthorityLevel,
                    target.ApprovalPolicyId, target.EscalationMembershipId);
            }

            await WriteAuditAsync(companyId, actor.UserId, AuditEventActions.CompanyResponsibilityAssignmentChanged,
                target.Id, change.ResponsibilityArea, request.Reason, change, cancellationToken);
        }

        var company = await _dbContext.Companies.SingleAsync(x => x.Id == companyId, cancellationToken);
        if (company.SizeBand != request.CompanySize)
        {
            company.UpdateWorkspaceProfile(company.Name, company.Industry, company.BusinessType, company.Timezone,
                company.Currency, company.Language, company.ComplianceRegion, request.CompanySize);
        }

        await WriteAuditAsync(companyId, actor.UserId, AuditEventActions.CompanyResponsibilityPresetApplied,
            companyId, null, request.Reason, preview, cancellationToken);
        await SaveWithConflictTranslationAsync(cancellationToken);
        await InvalidateTodayAsync(companyId, cancellationToken);
        var saved = await LoadAssignmentsAsync(companyId, cancellationToken);
        return new(preview, saved.Select(Map).ToArray());
    }

    public async Task<CompanyResponsibilityAssignmentDto> UpsertAsync(Guid companyId,
        UpsertCompanyResponsibilityAssignmentCommand command, CancellationToken cancellationToken)
    {
        var actor = await RequireActiveMemberAsync(companyId, true, cancellationToken);
        _ = command.ResponsibilityArea.ToStorageValue();
        _ = command.AssignmentKind.ToStorageValue();
        AgentAutonomyLevel authorityLevel;
        try { authorityLevel = AgentAutonomyLevelValues.Parse(command.AuthorityLevel); }
        catch (ArgumentOutOfRangeException) { throw Validation("AuthorityLevel", AgentAutonomyLevelValues.BuildValidationMessage(command.AuthorityLevel)); }
        await ValidateMembershipAsync(companyId, command.AssignedMembershipId, "AssignedMembershipId", cancellationToken);
        if (command.EscalationMembershipId.HasValue)
            await ValidateMembershipAsync(companyId, command.EscalationMembershipId.Value, "EscalationMembershipId", cancellationToken);
        if (command.PrimaryAgentId.HasValue)
            await ValidateAgentAsync(companyId, command.PrimaryAgentId.Value, command.ResponsibilityArea, cancellationToken);

        CompanyResponsibilityAssignment? assignment;
        if (command.AssignmentId.HasValue)
        {
            assignment = await _dbContext.CompanyResponsibilityAssignments.SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Id == command.AssignmentId.Value, cancellationToken);
            if (assignment is null) throw new KeyNotFoundException("Responsibility assignment not found.");
            if (assignment.ResponsibilityArea != command.ResponsibilityArea || assignment.AssignmentKind != command.AssignmentKind)
                throw Validation("AssignmentId", "The assignment does not match the requested responsibility area and kind.");
        }
        else if (command.AssignmentKind == ResponsibilityAssignmentKind.Primary)
        {
            assignment = await _dbContext.CompanyResponsibilityAssignments.SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.ResponsibilityArea == command.ResponsibilityArea &&
                     x.AssignmentKind == ResponsibilityAssignmentKind.Primary, cancellationToken);
        }
        else
        {
            assignment = await _dbContext.CompanyResponsibilityAssignments.SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.ResponsibilityArea == command.ResponsibilityArea &&
                     x.AssignmentKind == command.AssignmentKind && x.AssignedMembershipId == command.AssignedMembershipId,
                cancellationToken);
        }

        object? before = assignment is null ? null : AuditSnapshot(assignment);
        if (assignment is null)
        {
            assignment = new CompanyResponsibilityAssignment(Guid.NewGuid(), companyId, command.ResponsibilityArea,
                command.AssignmentKind, command.AssignedMembershipId, command.PrimaryAgentId, authorityLevel,
                command.ApprovalPolicyId, command.EscalationMembershipId);
            _dbContext.CompanyResponsibilityAssignments.Add(assignment);
        }
        else
        {
            if (command.ExpectedVersion.HasValue && assignment.Version != command.ExpectedVersion.Value)
                throw new CompanyResponsibilityConflictException("The responsibility assignment changed. Refresh it and try again.");
            assignment.Update(command.AssignedMembershipId, command.PrimaryAgentId, authorityLevel,
                command.ApprovalPolicyId, command.EscalationMembershipId);
        }

        await WriteAuditAsync(companyId, actor.UserId, AuditEventActions.CompanyResponsibilityAssignmentChanged,
            assignment.Id, assignment.ResponsibilityArea, command.Reason,
            new { previous = before, current = AuditSnapshot(assignment) }, cancellationToken);
        await SaveWithConflictTranslationAsync(cancellationToken);
        await InvalidateTodayAsync(companyId, cancellationToken);
        return Map((await LoadAssignmentsAsync(companyId, cancellationToken)).Single(x => x.Id == assignment.Id));
    }

    public async Task RemoveAsync(Guid companyId, Guid assignmentId, long? expectedVersion, string? reason,
        CancellationToken cancellationToken)
    {
        var actor = await RequireActiveMemberAsync(companyId, true, cancellationToken);
        var assignment = await _dbContext.CompanyResponsibilityAssignments.SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == assignmentId, cancellationToken);
        if (assignment is null) throw new KeyNotFoundException("Responsibility assignment not found.");
        if (expectedVersion.HasValue && assignment.Version != expectedVersion.Value)
            throw new CompanyResponsibilityConflictException("The responsibility assignment changed. Refresh it and try again.");
        var before = AuditSnapshot(assignment);
        _dbContext.CompanyResponsibilityAssignments.Remove(assignment);
        await WriteAuditAsync(companyId, actor.UserId, AuditEventActions.CompanyResponsibilityAssignmentRemoved,
            assignment.Id, assignment.ResponsibilityArea, reason, new { previous = before, current = (object?)null }, cancellationToken);
        await SaveWithConflictTranslationAsync(cancellationToken);
        await InvalidateTodayAsync(companyId, cancellationToken);
    }

    private async Task<ResponsibilityPresetPreviewDto> BuildPreviewAsync(Guid companyId, ResponsibilityPresetRequest request,
        CancellationToken cancellationToken)
    {
        if (!CompanySizeBandValues.All.Contains(request.CompanySize))
            throw Validation("CompanySize", "Company size must be micro, small, or medium.");
        if (!Enum.IsDefined(request.Mode)) throw Validation("Mode", "Preset mode is not supported.");
        var owner = await ValidateMembershipAsync(companyId, request.OwnerMembershipId, "OwnerMembershipId", cancellationToken);
        if (owner.Role != CompanyMembershipRole.Owner)
            throw Validation("OwnerMembershipId", "The selected membership must be an active company owner.");

        var selections = request.ManagerMembershipIds ?? new Dictionary<ResponsibilityArea, Guid>();
        foreach (var selection in selections)
        {
            _ = selection.Key.ToStorageValue();
            var membership = await ValidateMembershipAsync(companyId, selection.Value, "ManagerMembershipIds", cancellationToken);
            if (membership.Role is not (CompanyMembershipRole.Manager or CompanyMembershipRole.Admin or CompanyMembershipRole.Owner))
                throw Validation("ManagerMembershipIds", "Selected responsibility managers must have an Owner, Admin, or Manager membership.");
        }

        var current = await LoadAssignmentsAsync(companyId, cancellationToken);
        var agents = await _dbContext.Agents.AsNoTracking().Where(x => x.CompanyId == companyId && x.Status == AgentStatus.Active)
            .ToListAsync(cancellationToken);
        var desired = new List<(ResponsibilityArea Area, ResponsibilityAssignmentKind Kind, Guid MembershipId, Guid? AgentId)>();
        foreach (var area in ResponsibilityAreaValues.All)
        {
            var memberId = request.CompanySize == CompanySizeBand.Micro
                ? owner.Id
                : selections.TryGetValue(area, out var selected) ? selected : owner.Id;
            desired.Add((area, ResponsibilityAssignmentKind.Primary, memberId, FindUnambiguousAgent(agents, area)));
            if (request.CompanySize == CompanySizeBand.Medium)
                desired.Add((area, ResponsibilityAssignmentKind.ExecutiveOversight, owner.Id, null));
        }

        var changes = new List<ResponsibilityPresetChangeDto>();
        foreach (var item in desired)
        {
            var existing = item.Kind == ResponsibilityAssignmentKind.Primary
                ? current.SingleOrDefault(x => x.ResponsibilityArea == item.Area && x.AssignmentKind == item.Kind)
                : current.SingleOrDefault(x => x.ResponsibilityArea == item.Area && x.AssignmentKind == item.Kind && x.AssignedMembershipId == item.MembershipId);
            var kind = existing is null ? ResponsibilityPresetChangeKind.Add
                : request.Mode == ResponsibilityPresetMode.ReplaceExisting &&
                  (existing.AssignedMembershipId != item.MembershipId || existing.PrimaryAgentId != item.AgentId)
                    ? ResponsibilityPresetChangeKind.Replace : ResponsibilityPresetChangeKind.Retain;
            changes.Add(new(item.Area, item.Kind, kind, existing?.AssignedMembershipId, item.MembershipId,
                existing?.PrimaryAgentId, kind == ResponsibilityPresetChangeKind.Retain ? existing?.PrimaryAgentId : item.AgentId));
        }
        return new(companyId, request.CompanySize, request.Mode, changes);
    }

    private async Task<CompanyMembership> ValidateMembershipAsync(Guid companyId, Guid membershipId, string field,
        CancellationToken cancellationToken)
    {
        var membership = await _dbContext.CompanyMemberships.AsNoTracking().SingleOrDefaultAsync(
            x => x.CompanyId == companyId && x.Id == membershipId, cancellationToken);
        if (membership is null || membership.Status != CompanyMembershipStatus.Active)
            throw Validation(field, "The selected membership must be active and belong to this company.");
        return membership;
    }

    private async Task ValidateAgentAsync(Guid companyId, Guid agentId, ResponsibilityArea area, CancellationToken cancellationToken)
    {
        var agent = await _dbContext.Agents.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken);
        if (agent is null || agent.Status != AgentStatus.Active)
            throw Validation("PrimaryAgentId", "The selected agent must be active and belong to this company.");
        if (!IsCompatible(agent, area))
            throw Validation("PrimaryAgentId", "The selected agent is not eligible for this responsibility area.");
    }

    private async Task<VirtualCompany.Application.Auth.ResolvedCompanyMembershipContext> RequireActiveMemberAsync(Guid companyId,
        bool mutation, CancellationToken cancellationToken)
    {
        var member = await _membershipResolver.ResolveAsync(companyId, cancellationToken);
        if (member is null) throw new UnauthorizedAccessException("An active company membership is required.");
        if (mutation && member.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin))
            throw new UnauthorizedAccessException("Only company owners and admins can change responsibility assignments.");
        return member;
    }

    private async Task<List<CompanyResponsibilityAssignment>> LoadAssignmentsAsync(Guid companyId,
        CancellationToken cancellationToken, bool tracking = false)
    {
        // Every query remains constrained by the explicit, membership-authorized company id.
        // Ignore the ambient filter so onboarding completion is also idempotent when its
        // unscoped endpoint invokes this service before middleware has selected a company.
        var query = _dbContext.CompanyResponsibilityAssignments.IgnoreQueryFilters()
            .Include(x => x.AssignedMembership).ThenInclude(x => x.User)
            .Include(x => x.EscalationMembership).ThenInclude(x => x!.User)
            .Include(x => x.PrimaryAgent).Where(x => x.CompanyId == companyId);
        if (!tracking) query = query.AsNoTracking();
        return await query.OrderBy(x => x.ResponsibilityArea).ThenBy(x => x.AssignmentKind).ThenBy(x => x.CreatedUtc)
            .ToListAsync(cancellationToken);
    }

    private static CompanyResponsibilityAssignmentDto Map(CompanyResponsibilityAssignment x) => new(
        x.Id, x.CompanyId, x.ResponsibilityArea, x.AssignmentKind, Map(x.AssignedMembership),
        x.PrimaryAgent is null ? null : Map(x.PrimaryAgent), x.AuthorityLevel.ToStorageValue(), x.ApprovalPolicyId,
        x.EscalationMembership is null ? null : Map(x.EscalationMembership), x.Version, x.CreatedUtc, x.UpdatedUtc);

    private static ResponsibilityMemberDto Map(CompanyMembership x) => new(x.Id, x.UserId,
        x.User?.DisplayName ?? x.InvitedEmail ?? "Company member", x.User?.Email ?? x.InvitedEmail ?? string.Empty, x.Role, x.Status);

    private static ResponsibilityAgentDto Map(Agent x) => new(x.Id, x.DisplayName, x.RoleName, x.Department,
        x.Status, ResponsibilityAreaValues.All.Where(area => IsCompatible(x, area)).ToArray());

    private static Guid? FindUnambiguousAgent(IReadOnlyList<Agent> agents, ResponsibilityArea area)
    {
        var matches = agents.Where(x => IsCompatible(x, area)).Select(x => x.Id).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    private static bool IsCompatible(Agent agent, ResponsibilityArea area)
    {
        var department = agent.Department.Trim().ToLowerInvariant();
        return area switch
        {
            ResponsibilityArea.CashAndAccounting => department is "finance" or "accounting",
            ResponsibilityArea.Compliance => department is "finance" or "accounting" or "compliance" or "legal",
            ResponsibilityArea.Sales => department == "sales",
            ResponsibilityArea.Marketing => department == "marketing",
            ResponsibilityArea.CustomerSupport => department is "support" or "customer support" or "customer success",
            ResponsibilityArea.CompanyPerformance => department is "operations" or "executive" or "leadership",
            _ => false
        };
    }

    private async Task WriteAuditAsync(Guid companyId, Guid actorId, string action, Guid targetId,
        ResponsibilityArea? area, string? reason, object diff, CancellationToken cancellationToken)
    {
        var correlationId = string.IsNullOrWhiteSpace(_correlationAccessor.CorrelationId)
            ? System.Diagnostics.Activity.Current?.Id ?? Guid.NewGuid().ToString("N") : _correlationAccessor.CorrelationId!;
        await _auditWriter.WriteAsync(new(companyId, AuditActorTypes.User, actorId, action,
            AuditTargetTypes.CompanyResponsibilityAssignment, targetId.ToString("N"), AuditEventOutcomes.Succeeded,
            string.IsNullOrWhiteSpace(reason) ? "Responsibility configuration changed." : reason.Trim(),
            ["company_responsibility_assignments", "http_request"],
            new Dictionary<string, string?> { ["responsibilityArea"] = area?.ToStorageValue(), ["reason"] = reason?.Trim() ?? "not_provided" },
            correlationId, PayloadDiffJson: JsonSerializer.Serialize(diff)), cancellationToken);
    }

    private async Task SaveWithConflictTranslationAsync(CancellationToken cancellationToken)
    {
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new CompanyResponsibilityConflictException("The responsibility assignment changed. Refresh it and try again."); }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("UX_company_responsibility_primary", StringComparison.OrdinalIgnoreCase) == true ||
            ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true)
        { throw new CompanyResponsibilityConflictException("A primary assignment already exists for this responsibility. Refresh it and try again."); }
    }

    private Task InvalidateTodayAsync(Guid companyId, CancellationToken cancellationToken) =>
        _dashboardCache?.InvalidateAsync(companyId, cancellationToken) ?? Task.CompletedTask;

    private static object AuditSnapshot(CompanyResponsibilityAssignment x) => new
    { x.Id, responsibilityArea = x.ResponsibilityArea.ToStorageValue(), assignmentKind = x.AssignmentKind.ToStorageValue(),
      x.AssignedMembershipId, x.PrimaryAgentId, authorityLevel = x.AuthorityLevel.ToStorageValue(), x.ApprovalPolicyId,
      x.EscalationMembershipId, x.Version };

    private static CompanyResponsibilityValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static IReadOnlyList<ResponsibilityPresetMetadataDto> Presets { get; } =
    [
        new(CompanySizeBand.Micro, "Micro company", "One owner holds all primary responsibilities.", ResponsibilityAreaValues.All, false, false),
        new(CompanySizeBand.Small, "Small company", "The owner retains unassigned responsibilities and selected managers can own functional areas.", ResponsibilityAreaValues.All, true, false),
        new(CompanySizeBand.Medium, "Medium company", "Selected managers own functions and the owner receives executive oversight.", ResponsibilityAreaValues.All, true, true)
    ];
}
