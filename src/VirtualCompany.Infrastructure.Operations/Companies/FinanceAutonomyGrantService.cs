using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class FinanceAutonomyGrantService :
    IFinanceAutonomyGrantService,
    IFinanceAutonomyPolicyEvaluator
{
    private const int MaximumBoundedRecords = 1000;
    private const int MaximumBoundedActions = 100;
    private const int MaximumFreshnessMinutes = 10080;
    private const int MaximumTriggerIntervalMinutes = 43200;
    private const int MaximumDebounceMinutes = 1440;
    private const int MaximumLateEventToleranceMinutes = 43200;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipResolver;
    private readonly IAgentEffectiveAuthorityResolver _authorityResolver;
    private readonly IFinanceAgentCoverageCatalogue _coverageCatalogue;
    private readonly IAuditEventWriter _audit;
    private readonly IScheduleExpressionValidator _scheduleValidator;
    private readonly TimeProvider _timeProvider;

    public FinanceAutonomyGrantService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipResolver,
        IAgentEffectiveAuthorityResolver authorityResolver,
        IFinanceAgentCoverageCatalogue coverageCatalogue,
        IAuditEventWriter audit,
        IScheduleExpressionValidator scheduleValidator,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _membershipResolver = membershipResolver;
        _authorityResolver = authorityResolver;
        _coverageCatalogue = coverageCatalogue;
        _audit = audit;
        _scheduleValidator = scheduleValidator;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<FinanceAutonomyGrantDto>> ListAsync(
        Guid companyId, Guid? agentId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var query = _dbContext.FinanceAutonomyGrants.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId);
        if (agentId.HasValue) query = query.Where(x => x.AgentId == agentId.Value);
        return (await query.Include(x => x.Versions)
                .OrderBy(x => x.AgentId).ThenBy(x => x.CapabilityId)
                .ToListAsync(cancellationToken))
            .Select(Map).ToArray();
    }

    public async Task<FinanceAutonomyGrantDto> GetAsync(
        Guid companyId, Guid grantId, CancellationToken cancellationToken) =>
        Map(await LoadGrantAsync(companyId, grantId, false, cancellationToken));

    public async Task<FinanceAutonomyGrantDto> CreateAsync(
        Guid companyId, CreateFinanceAutonomyGrantCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureCompany(companyId);
        var actorId = (await RequireManagerAsync(companyId, cancellationToken)).UserId;
        var now = UtcNow();
        var validated = await ValidateDefinitionAsync(companyId, command.Definition, cancellationToken);

        if (await _dbContext.FinanceAutonomyGrants.IgnoreQueryFilters().AnyAsync(
                x => x.CompanyId == companyId &&
                     x.AgentId == command.Definition.AgentId &&
                     x.CapabilityId == validated.Capability.Id, cancellationToken))
        {
            throw Validation(nameof(command.Definition.CapabilityId), "A Finance autonomy grant already exists for this agent and capability. Create a new version instead.");
        }

        var grant = new FinanceAutonomyGrant(Guid.NewGuid(), companyId, command.Definition.AgentId, validated.Capability.Id, now);
        var version = CreateVersionEntity(grant, command.Definition, validated, actorId, now);
        _dbContext.FinanceAutonomyGrants.Add(grant);
        _dbContext.FinanceAutonomyGrantVersions.Add(version);
        await SaveAsync(cancellationToken);
        await WriteAuditAsync(companyId, actorId, AuditEventActions.FinanceAutonomyGrantVersionCreated,
            grant.Id, AuditEventOutcomes.Succeeded, "A prospective Finance autonomy grant version was created.",
            version, command.Rationale, cancellationToken);
        return Map(grant);
    }

    public async Task<FinanceAutonomyGrantDto> CreateVersionAsync(
        Guid companyId, Guid grantId, CreateFinanceAutonomyGrantVersionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actorId = (await RequireManagerAsync(companyId, cancellationToken)).UserId;
        var grant = await LoadGrantAsync(companyId, grantId, true, cancellationToken);
        if (command.ExpectedGrantVersion > 0 && grant.Version != command.ExpectedGrantVersion)
            throw new FinanceAutonomyConcurrencyException("The Finance autonomy grant changed. Refresh and retry.");
        if (command.Definition.AgentId != grant.AgentId ||
            !string.Equals(command.Definition.CapabilityId, grant.CapabilityId, StringComparison.OrdinalIgnoreCase))
            throw Validation(nameof(command.Definition), "A new version cannot change the grant's agent or capability.");

        var now = UtcNow();
        var validated = await ValidateDefinitionAsync(companyId, command.Definition, cancellationToken);
        var version = CreateVersionEntity(grant, command.Definition, validated, actorId, now);
        _dbContext.FinanceAutonomyGrantVersions.Add(version);
        await SaveAsync(cancellationToken);
        await WriteAuditAsync(companyId, actorId, AuditEventActions.FinanceAutonomyGrantVersionCreated,
            grant.Id, AuditEventOutcomes.Succeeded, "A new prospective Finance autonomy grant version was created.",
            version, command.Rationale, cancellationToken);
        return Map(grant);
    }

    public async Task<FinanceAutonomyGrantDto> ActivateAsync(
        Guid companyId, Guid grantId, Guid versionId, ActivateFinanceAutonomyGrantVersionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var actorId = (await RequireManagerAsync(companyId, cancellationToken)).UserId;
        var grant = await LoadGrantAsync(companyId, grantId, true, cancellationToken);
        var version = grant.Versions.SingleOrDefault(x => x.Id == versionId)
            ?? throw new KeyNotFoundException("Finance autonomy grant version not found.");
        if (command.ExpectedGrantVersion > 0 && grant.Version != command.ExpectedGrantVersion)
            throw new FinanceAutonomyConcurrencyException("The Finance autonomy grant changed. Refresh and retry.");

        var definition = ToDefinition(grant, version);
        var validated = await ValidateDefinitionAsync(companyId, definition, cancellationToken);
        if (!string.Equals(version.CapabilityPolicyHash, validated.CapabilityPolicyHash, StringComparison.Ordinal) ||
            !string.Equals(version.AuthorityVersion, validated.Authority.AuthorityVersion, StringComparison.Ordinal) ||
            !string.Equals(version.AuthorityHash, validated.Authority.AuthorityHash, StringComparison.Ordinal))
            throw Validation("versionId", "The Finance capability, risk, or agent authority changed. Create and review a new grant version.");

        if (IsElevated(version.Level))
        {
            if (string.IsNullOrWhiteSpace(command.ReviewReason))
                throw Validation(nameof(command.ReviewReason), "Elevated Finance autonomy requires a documented review.");
            if (version.CreatedByUserId == actorId)
                throw Validation(nameof(command.ReviewReason), "Elevated Finance autonomy must be reviewed by a different authorized user.");
        }

        var now = UtcNow();
        if (version.ExpiresUtc <= now)
            throw Validation("versionId", "An expired Finance autonomy version cannot be activated.");
        var previouslyActive = grant.Versions.FirstOrDefault(x => x.Id == grant.ActiveVersionId);
        previouslyActive?.Supersede();
        version.Activate(actorId, command.ReviewReason, now);
        try
        {
            grant.Activate(version.Id, command.ExpectedGrantVersion, now);
        }
        catch (InvalidOperationException ex)
        {
            throw new FinanceAutonomyConcurrencyException(ex.Message);
        }

        await SaveAsync(cancellationToken);
        await WriteAuditAsync(companyId, actorId, AuditEventActions.FinanceAutonomyGrantActivated,
            grant.Id, AuditEventOutcomes.Approved, "A reviewed Finance autonomy grant version was activated.",
            version, command.ReviewReason, cancellationToken);
        return Map(grant);
    }

    public async Task<FinanceAutonomyGrantDto> RevokeAsync(
        Guid companyId, Guid grantId, RevokeFinanceAutonomyGrantCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Reason)) throw Validation(nameof(command.Reason), "A revocation reason is required.");
        var actorId = (await RequireManagerAsync(companyId, cancellationToken)).UserId;
        var grant = await LoadGrantAsync(companyId, grantId, true, cancellationToken);
        var active = grant.Versions.SingleOrDefault(x => x.Id == grant.ActiveVersionId)
            ?? throw Validation("grantId", "The Finance autonomy grant has no active version.");
        if (command.ExpectedGrantVersion > 0 && grant.Version != command.ExpectedGrantVersion)
            throw new FinanceAutonomyConcurrencyException("The Finance autonomy grant changed. Refresh and retry.");
        var now = UtcNow();
        active.Revoke(actorId, command.Reason, now);
        try
        {
            grant.ClearActiveVersion(command.ExpectedGrantVersion, now);
        }
        catch (InvalidOperationException ex)
        {
            throw new FinanceAutonomyConcurrencyException(ex.Message);
        }

        await SaveAsync(cancellationToken);
        await WriteAuditAsync(companyId, actorId, AuditEventActions.FinanceAutonomyGrantRevoked,
            grant.Id, AuditEventOutcomes.Succeeded, "The active Finance autonomy grant was revoked.",
            active, command.Reason, cancellationToken);
        return Map(grant);
    }

    public async Task<FinanceAutonomyControlDto> SetControlAsync(
        Guid companyId, SetFinanceAutonomyControlCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EnsureCompany(companyId);
        var actorId = (await RequireManagerAsync(companyId, cancellationToken)).UserId;
        if (string.IsNullOrWhiteSpace(command.Reason)) throw Validation(nameof(command.Reason), "A reason is required.");
        FinanceAutonomyControlScope scope;
        FinanceAutonomyControlState state;
        try
        {
            scope = FinanceAutonomyEnumValues.ParseFinanceAutonomyControlScope(command.Scope);
            state = FinanceAutonomyEnumValues.ParseFinanceAutonomyControlState(command.State);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw Validation(nameof(command), ex.Message);
        }

        if (command.AgentId.HasValue &&
            !await _dbContext.Agents.IgnoreQueryFilters().AnyAsync(
                x => x.CompanyId == companyId && x.Id == command.AgentId.Value, cancellationToken))
            throw Validation(nameof(command.AgentId), "The Finance autonomy control agent does not exist in this company.");
        if (!string.IsNullOrWhiteSpace(command.CapabilityId) && !TryGetCapability(command.CapabilityId, out _))
            throw Validation(nameof(command.CapabilityId), "The Finance autonomy capability is unknown.");

        string scopeKey;
        try
        {
            scopeKey = FinanceAutonomyControl.CreateScopeKey(scope, command.AgentId, command.CapabilityId);
        }
        catch (ArgumentException ex)
        {
            throw Validation(nameof(command), ex.Message);
        }

        var now = UtcNow();
        var control = await _dbContext.FinanceAutonomyControls.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ScopeKey == scopeKey, cancellationToken);
        if (control is null)
        {
            control = new FinanceAutonomyControl(companyId, scope, command.AgentId, command.CapabilityId, now);
            _dbContext.FinanceAutonomyControls.Add(control);
        }

        try
        {
            control.Change(state, command.Reason, actorId, now, command.ExpectedVersion);
        }
        catch (InvalidOperationException ex)
        {
            throw new FinanceAutonomyConcurrencyException(ex.Message);
        }

        await SaveAsync(cancellationToken);
        var action = state switch
        {
            FinanceAutonomyControlState.Paused => AuditEventActions.FinanceAutonomyPaused,
            FinanceAutonomyControlState.EmergencyStopped => AuditEventActions.FinanceAutonomyEmergencyStopped,
            _ => AuditEventActions.FinanceAutonomyResumed
        };
        await _audit.WriteAsync(new AuditEventWriteRequest(
            companyId, AuditActorTypes.User, actorId, action, AuditTargetTypes.FinanceAutonomyControl,
            control.Id.ToString("N"), AuditEventOutcomes.Succeeded, command.Reason,
            Metadata: new Dictionary<string, string?>
            {
                ["scope"] = control.Scope.ToStorageValue(),
                ["scopeKey"] = control.ScopeKey,
                ["state"] = control.State.ToStorageValue(),
                ["version"] = control.Version.ToString()
            }), cancellationToken);
        return Map(control);
    }

    public async Task<FinanceAutonomyPolicySnapshotDto> GetEffectivePolicyAsync(
        Guid companyId, Guid agentId, string capabilityId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var grant = await _dbContext.FinanceAutonomyGrants.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.AgentId == agentId &&
                                       x.CapabilityId == Normalize(capabilityId), cancellationToken);
        var controls = await LoadApplicableControlsAsync(companyId, agentId, capabilityId, cancellationToken);
        var active = grant?.Versions.FirstOrDefault(x => x.Id == grant.ActiveVersionId);
        var decision = await EvaluateAsync(new FinanceAutonomyEvaluationRequest(
            companyId, agentId, capabilityId,
            active?.AllowedTriggers.FirstOrDefault() ?? FinanceAutonomyTriggers.ManualReview,
            active?.AllowedActionClasses.FirstOrDefault() ?? "read",
            active?.AllowedTools.FirstOrDefault() ?? string.Empty,
            EvidenceObservedUtc: UtcNow()), cancellationToken);
        var agent = await _dbContext.Agents.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken)
            ?? throw new KeyNotFoundException("Agent not found.");
        return new FinanceAutonomyPolicySnapshotDto(companyId, agentId, Normalize(capabilityId),
            MapCompatibilityLevel(agent.AutonomyLevel), grant is null ? null : Map(grant), controls.Select(Map).ToArray(),
            decision, UtcNow());
    }

    public async Task<FinanceAutonomyDecisionDto> EvaluateAsync(
        FinanceAutonomyEvaluationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureCompany(request.CompanyId);
        var now = UtcNow();

        var companyOperating = await _dbContext.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == request.CompanyId, cancellationToken);
        if (companyOperating?.EmergencyStopped == true)
            return Deny(FinanceAutonomyDecisionReasonCodes.EmergencyStopped, "Company emergency stop is active.", now);
        if (companyOperating?.IsPaused == true)
            return Deny(FinanceAutonomyDecisionReasonCodes.CompanyPaused, "Company operations are paused.", now);

        var controls = await LoadApplicableControlsAsync(request.CompanyId, request.AgentId, request.CapabilityId, cancellationToken);
        var stopped = controls.FirstOrDefault(x => x.State == FinanceAutonomyControlState.EmergencyStopped);
        if (stopped is not null)
            return Deny(FinanceAutonomyDecisionReasonCodes.EmergencyStopped, "Finance autonomy is emergency-stopped for this scope.", now);
        var paused = controls.FirstOrDefault(x => x.State == FinanceAutonomyControlState.Paused);
        if (paused is not null)
            return Deny(FinanceAutonomyDecisionReasonCodes.Paused, "Finance autonomy is paused for this scope.", now);

        var grant = await _dbContext.FinanceAutonomyGrants.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Versions)
            .SingleOrDefaultAsync(x => x.CompanyId == request.CompanyId && x.AgentId == request.AgentId &&
                                       x.CapabilityId == Normalize(request.CapabilityId), cancellationToken);
        if (grant?.ActiveVersionId is null)
            return Deny(FinanceAutonomyDecisionReasonCodes.GrantMissing, "No active Finance autonomy grant exists.", now);
        var version = grant.Versions.SingleOrDefault(x => x.Id == grant.ActiveVersionId);
        if (version?.Status != FinanceAutonomyGrantVersionStatus.Active)
            return Deny(FinanceAutonomyDecisionReasonCodes.GrantInactive, "The Finance autonomy grant is not active.", now, grant, version);
        if (version.EffectiveFromUtc > now)
            return Deny(FinanceAutonomyDecisionReasonCodes.GrantNotYetEffective, "The Finance autonomy grant is not yet effective.", now, grant, version);
        if (version.ExpiresUtc <= now)
            return Deny(FinanceAutonomyDecisionReasonCodes.GrantExpired, "The Finance autonomy grant has expired.", now, grant, version);
        if (!version.AllowedTriggers.Contains(Normalize(request.Trigger), StringComparer.Ordinal))
            return Deny(FinanceAutonomyDecisionReasonCodes.TriggerDenied, "The trigger is not included in the Finance autonomy grant.", now, grant, version);
        if (!version.AllowedActionClasses.Contains(Normalize(request.ActionClass), StringComparer.Ordinal))
            return Deny(FinanceAutonomyDecisionReasonCodes.ActionDenied, "The action class is not included in the Finance autonomy grant.", now, grant, version);
        if (!version.AllowedTools.Contains(Normalize(request.ToolName), StringComparer.OrdinalIgnoreCase))
            return Deny(FinanceAutonomyDecisionReasonCodes.ToolDenied, "The tool is not included in the Finance autonomy grant.", now, grant, version);
        if (request.RecordCount < 0 || request.RecordCount > version.MaximumRecordsPerRun ||
            request.ActionCount < 1 || request.ActionCount > version.MaximumActionsPerRun ||
            (request.Amount.HasValue && (!version.MaximumAmountPerRun.HasValue || Math.Abs(request.Amount.Value) > version.MaximumAmountPerRun.Value)))
            return Deny(FinanceAutonomyDecisionReasonCodes.LimitExceeded, "The proposed work exceeds the grant's bounded limits.", now, grant, version);
        var evidenceUtc = request.EvidenceObservedUtc.HasValue
            ? DateTime.SpecifyKind(request.EvidenceObservedUtc.Value, DateTimeKind.Utc)
            : (DateTime?)null;
        if (!evidenceUtc.HasValue || evidenceUtc.Value > now.AddMinutes(1) ||
            now - evidenceUtc.Value > TimeSpan.FromMinutes(version.EvidenceFreshnessMinutes))
            return Deny(FinanceAutonomyDecisionReasonCodes.EvidenceStale, "Current evidence is required before proactive Finance work begins.", now, grant, version);
        if (!IsWithinWindow(version, now))
            return Deny(FinanceAutonomyDecisionReasonCodes.TriggerDenied, "The current company-local time is outside the grant window.", now, grant, version);

        if (!TryGetCapability(request.CapabilityId, out var capability))
            return Deny(FinanceAutonomyDecisionReasonCodes.PolicyStale, "The granted Finance capability is no longer in the supported catalogue.", now, grant, version);
        var operation = capability.Operations.SingleOrDefault(x => string.Equals(x.ToolName, request.ToolName, StringComparison.OrdinalIgnoreCase));
        if (operation is null)
            return Deny(FinanceAutonomyDecisionReasonCodes.PolicyStale, "The granted tool is no longer part of this Finance capability.", now, grant, version);
        if (operation.SupportState == FinanceAgentCoverageSupportStates.HumanOnly)
            return Deny(FinanceAutonomyDecisionReasonCodes.HumanOnly, "This Finance operation is reserved for a human.", now, grant, version);
        if (!string.Equals(version.CapabilityPolicyHash, ComputeCapabilityPolicyHash(capability, version.AllowedTools), StringComparison.Ordinal))
            return Deny(FinanceAutonomyDecisionReasonCodes.PolicyStale, "Finance capability or risk policy changed and requires a new review.", now, grant, version);

        var authority = await _authorityResolver.ResolveAsync(request.CompanyId, request.AgentId, cancellationToken);
        var effectiveTool = authority.Tools.SingleOrDefault(x =>
            string.Equals(x.ToolName, request.ToolName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.ActionType, request.ActionClass, StringComparison.OrdinalIgnoreCase));
        if (effectiveTool?.IsUsable != true ||
            !string.Equals(version.AuthorityVersion, authority.AuthorityVersion, StringComparison.Ordinal) ||
            !string.Equals(version.AuthorityHash, authority.AuthorityHash, StringComparison.Ordinal))
            return Deny(FinanceAutonomyDecisionReasonCodes.AuthorityStale, "The agent's effective authority changed and the grant must be reviewed.", now, grant, version);

        var requiresApproval = operation.ApprovalBehavior != "allow" ||
                               version.ConfirmationBehavior == FinanceAutonomyConfirmationBehaviors.ApprovalRequired ||
                               effectiveTool.State == AgentCapabilityStates.ApprovalRequired;
        var requiresConfirmation = requiresApproval ||
                                   version.ConfirmationBehavior == FinanceAutonomyConfirmationBehaviors.ExplicitConfirmation;
        return new FinanceAutonomyDecisionDto(true, FinanceAutonomyDecisionReasonCodes.Allowed,
            "The proposed Finance work is inside an active, current, bounded grant.",
            grant.Id, version.Id, version.VersionNumber, version.Level.ToStorageValue(),
            requiresConfirmation, requiresApproval,
            Math.Max(0, version.MaximumRecordsPerRun - request.RecordCount),
            Math.Max(0, version.MaximumActionsPerRun - request.ActionCount),
            version.MaximumAmountPerRun.HasValue && request.Amount.HasValue
                ? Math.Max(0m, version.MaximumAmountPerRun.Value - Math.Abs(request.Amount.Value))
                : version.MaximumAmountPerRun,
            FinanceAutonomyPolicyVersions.V1, version.CatalogueVersion, authority.AuthorityVersion, authority.AuthorityHash, now);
    }

    private async Task<ValidatedDefinition> ValidateDefinitionAsync(
        Guid companyId, FinanceAutonomyGrantDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string key, string message)
        {
            if (!errors.TryGetValue(key, out var list)) errors[key] = list = [];
            list.Add(message);
        }

        if (definition.AgentId == Guid.Empty) Add(nameof(definition.AgentId), "Agent is required.");
        var agent = definition.AgentId == Guid.Empty ? null : await _dbContext.Agents.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == definition.AgentId, cancellationToken);
        if (agent is null) Add(nameof(definition.AgentId), "The Finance agent does not exist in this company.");
        else if (!string.Equals(agent.Department, "Finance", StringComparison.OrdinalIgnoreCase))
            Add(nameof(definition.AgentId), "Finance autonomy grants can only target a Finance agent.");

        FinanceAutonomyLevel level = default;
        try { level = FinanceAutonomyEnumValues.ParseFinanceAutonomyLevel(definition.Level); }
        catch (ArgumentOutOfRangeException) { Add(nameof(definition.Level), "The Finance autonomy level is unknown."); }

        if (!TryGetCapability(definition.CapabilityId, out var capability))
            Add(nameof(definition.CapabilityId), "The Finance capability is unknown.");

        var triggers = NormalizeList(definition.AllowedTriggers);
        var eventTypes = NormalizeList(definition.AllowedEventTypes ?? []);
        if (triggers.Count == 0) Add(nameof(definition.AllowedTriggers), "At least one explicit trigger is required.");
        foreach (var trigger in triggers)
            if (!FinanceAutonomyTriggers.All.Contains(trigger) || trigger == "*")
                Add(nameof(definition.AllowedTriggers), $"Trigger '{trigger}' is not allowed.");
        foreach (var eventType in eventTypes)
            if (!FinanceAutonomyEventTypes.All.Contains(eventType))
                Add(nameof(definition.AllowedEventTypes), $"Finance event type '{eventType}' is not in the reviewed allowlist.");
        if (triggers.Contains(FinanceAutonomyTriggers.BusinessEvent) && eventTypes.Count == 0)
            Add(nameof(definition.AllowedEventTypes), "A business-event grant must name at least one reviewed Finance event type.");
        if (!triggers.Contains(FinanceAutonomyTriggers.BusinessEvent) && eventTypes.Count > 0)
            Add(nameof(definition.AllowedEventTypes), "Finance event types require the business-event trigger.");

        var actions = NormalizeList(definition.AllowedActionClasses);
        var tools = NormalizeList(definition.AllowedTools);
        if (actions.Count == 0) Add(nameof(definition.AllowedActionClasses), "At least one explicit action class is required.");
        if (tools.Count == 0) Add(nameof(definition.AllowedTools), "At least one named tool is required.");
        if (actions.Contains("*") || tools.Contains("*")) Add(nameof(definition.AllowedTools), "Wildcard Finance authority is not allowed.");
        foreach (var action in actions)
            if (action is not "read" and not "recommend" and not "execute")
                Add(nameof(definition.AllowedActionClasses), $"Action class '{action}' is unknown.");

        if (level == FinanceAutonomyLevel.ReadMonitor && actions.Any(x => x != "read"))
            Add(nameof(definition.AllowedActionClasses), "Read/monitor grants may only include read actions.");
        if (level == FinanceAutonomyLevel.RecommendDraft && actions.Contains("execute"))
            Add(nameof(definition.AllowedActionClasses), "Recommend/draft grants cannot include execute actions.");
        if (level == FinanceAutonomyLevel.ScheduledBoundedExecute &&
            !triggers.Any(x => x is FinanceAutonomyTriggers.Schedule or FinanceAutonomyTriggers.BusinessEvent))
            Add(nameof(definition.AllowedTriggers), "Scheduled bounded execute requires a schedule or business-event trigger.");

        if (definition.MaximumRecordsPerRun is < 1 or > MaximumBoundedRecords)
            Add(nameof(definition.MaximumRecordsPerRun), $"Record limit must be between 1 and {MaximumBoundedRecords}.");
        if (definition.MaximumActionsPerRun is < 1 or > MaximumBoundedActions)
            Add(nameof(definition.MaximumActionsPerRun), $"Action limit must be between 1 and {MaximumBoundedActions}.");
        if (definition.MaximumAmountPerRun is <= 0) Add(nameof(definition.MaximumAmountPerRun), "Amount limit must be positive.");
        if (definition.EvidenceFreshnessMinutes is < 1 or > MaximumFreshnessMinutes)
            Add(nameof(definition.EvidenceFreshnessMinutes), $"Evidence freshness must be between 1 and {MaximumFreshnessMinutes} minutes.");
        if (definition.MinimumIntervalMinutes is < 1 or > MaximumTriggerIntervalMinutes)
            Add(nameof(definition.MinimumIntervalMinutes), $"Minimum trigger interval must be between 1 and {MaximumTriggerIntervalMinutes} minutes.");
        if (definition.MaximumRunsPerWindow is < 1 or > 100)
            Add(nameof(definition.MaximumRunsPerWindow), "Maximum runs per company-local window must be between 1 and 100.");
        if (definition.DebounceMinutes is < 1 or > MaximumDebounceMinutes)
            Add(nameof(definition.DebounceMinutes), $"Event debounce must be between 1 and {MaximumDebounceMinutes} minutes.");
        if (!FinanceAutonomyCatchUpBehaviors.All.Contains(Normalize(definition.CatchUpBehavior)))
            Add(nameof(definition.CatchUpBehavior), "Catch-up behavior must be 'skip' or 'latest'.");
        if (definition.MaximumCatchUpWindows is < 1 or > 3)
            Add(nameof(definition.MaximumCatchUpWindows), "Maximum catch-up windows must be between 1 and 3.");
        if (definition.LateEventToleranceMinutes is < 1 or > MaximumLateEventToleranceMinutes)
            Add(nameof(definition.LateEventToleranceMinutes), $"Late-event tolerance must be between 1 and {MaximumLateEventToleranceMinutes} minutes.");
        if (!FinanceAutonomyConfirmationBehaviors.All.Contains(Normalize(definition.ConfirmationBehavior)))
            Add(nameof(definition.ConfirmationBehavior), "Confirmation behavior is unknown.");
        if (actions.Contains("execute") && Normalize(definition.ConfirmationBehavior) == FinanceAutonomyConfirmationBehaviors.NoConfirmation)
            Add(nameof(definition.ConfirmationBehavior), "Execute authority cannot disable confirmation and approval policy.");
        if (string.IsNullOrWhiteSpace(definition.EscalationRoute))
            Add(nameof(definition.EscalationRoute), "An escalation route is required.");
        if (!TimeOnly.TryParseExact(definition.WindowStartLocal, "HH:mm", out _) ||
            !TimeOnly.TryParseExact(definition.WindowEndLocal, "HH:mm", out _))
            Add(nameof(definition.WindowStartLocal), "The local execution window must use HH:mm values.");
        var timezoneValidation = _scheduleValidator.ValidateTimeZoneId(definition.Timezone);
        if (!timezoneValidation.IsValid)
            Add(nameof(definition.Timezone), timezoneValidation.Error ?? "The timezone is not configured on this host.");
        if (triggers.Contains(FinanceAutonomyTriggers.Schedule))
        {
            var scheduleValidation = _scheduleValidator.ValidateCronExpression(definition.ScheduleExpression);
            if (!scheduleValidation.IsValid)
                Add(nameof(definition.ScheduleExpression), scheduleValidation.Error ?? "A valid schedule expression is required.");
        }
        if (definition.ExpiresUtc.HasValue &&
            definition.ExpiresUtc.Value <= (definition.EffectiveFromUtc ?? UtcNow()))
            Add(nameof(definition.ExpiresUtc), "Grant expiry must follow its effective time.");

        AgentEffectiveAuthorityDto? authority = null;
        if (agent is not null && capability is not null)
        {
            authority = await _authorityResolver.ResolveAsync(companyId, definition.AgentId, cancellationToken);
            foreach (var tool in tools)
            {
                var matching = capability.Operations.Where(x => string.Equals(x.ToolName, tool, StringComparison.OrdinalIgnoreCase)).ToArray();
                if (matching.Length == 0)
                {
                    Add(nameof(definition.AllowedTools), $"Tool '{tool}' is unknown for capability '{capability.Id}'.");
                    continue;
                }

                var operation = matching[0];
                if (operation.SupportState == FinanceAgentCoverageSupportStates.HumanOnly)
                    Add(nameof(definition.AllowedTools), $"Tool '{tool}' is human-only.");
                if (operation.SupportState is FinanceAgentCoverageSupportStates.Unsupported or FinanceAgentCoverageSupportStates.ConfigurationDependent)
                    Add(nameof(definition.AllowedTools), $"Tool '{tool}' is not currently configured for autonomous use.");
                if (!actions.Contains(Normalize(operation.ActionClass)))
                    Add(nameof(definition.AllowedActionClasses), $"Action class '{operation.ActionClass}' is required by tool '{tool}'.");
                if (operation.ActionClass == "execute" &&
                    operation.ExternalSideEffectIsNotInternal())
                    Add(nameof(definition.AllowedTools), $"Tool '{tool}' has an external or permanent effect and cannot be granted proactively.");
                if (level == FinanceAutonomyLevel.ScheduledBoundedExecute &&
                    operation.ActionClass == "execute" && operation.RiskTier != FinanceToolRiskTiers.Low)
                    Add(nameof(definition.AllowedTools), $"Scheduled execute tool '{tool}' exceeds the conservative low-risk boundary.");

                var effective = authority.Tools.SingleOrDefault(x =>
                    string.Equals(x.ToolName, tool, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.ActionType, operation.ActionClass, StringComparison.OrdinalIgnoreCase));
                if (effective?.IsUsable != true)
                    Add(nameof(definition.AllowedTools), $"Tool '{tool}' is not effective in the agent's current authority and configuration.");
            }
        }

        if (errors.Count > 0)
            throw new FinanceAutonomyValidationException(errors.ToDictionary(x => x.Key, x => x.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase));

        return new ValidatedDefinition(level, capability!, authority!,
            ComputeCapabilityPolicyHash(capability!, tools));
    }

    private FinanceAutonomyGrantVersion CreateVersionEntity(
        FinanceAutonomyGrant grant, FinanceAutonomyGrantDefinition definition,
        ValidatedDefinition validated, Guid actorId, DateTime now)
    {
        var versionNumber = grant.ReserveNextVersion(now);
        return new FinanceAutonomyGrantVersion(
            Guid.NewGuid(), grant.CompanyId, grant.Id, versionNumber, validated.Level,
            definition.AllowedTriggers, definition.AllowedActionClasses, definition.AllowedTools,
            definition.MaximumRecordsPerRun, definition.MaximumAmountPerRun, definition.MaximumActionsPerRun,
            definition.ScheduleExpression, definition.Timezone, definition.WindowStartLocal, definition.WindowEndLocal,
            definition.EvidenceFreshnessMinutes, definition.ConfirmationBehavior, definition.EscalationRoute,
            definition.EffectiveFromUtc ?? now, definition.ExpiresUtc, FinanceAgentCoverageVersions.V1,
            validated.CapabilityPolicyHash, validated.Authority.AuthorityVersion, validated.Authority.AuthorityHash,
            actorId, now, IsElevated(validated.Level), definition.AllowedEventTypes,
            definition.MinimumIntervalMinutes, definition.MaximumRunsPerWindow, definition.DebounceMinutes,
            definition.CatchUpBehavior, definition.MaximumCatchUpWindows, definition.LateEventToleranceMinutes);
    }

    private async Task<FinanceAutonomyGrant> LoadGrantAsync(
        Guid companyId, Guid grantId, bool tracked, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var query = _dbContext.FinanceAutonomyGrants.IgnoreQueryFilters().Include(x => x.Versions)
            .Where(x => x.CompanyId == companyId && x.Id == grantId);
        if (!tracked) query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Finance autonomy grant not found.");
    }

    private async Task<List<FinanceAutonomyControl>> LoadApplicableControlsAsync(
        Guid companyId, Guid agentId, string capabilityId, CancellationToken cancellationToken)
    {
        var keys = new[]
        {
            "company",
            $"agent:{agentId:N}",
            $"capability:{Normalize(capabilityId)}"
        };
        return await _dbContext.FinanceAutonomyControls.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && keys.Contains(x.ScopeKey))
            .OrderByDescending(x => x.State).ToListAsync(cancellationToken);
    }

    private bool TryGetCapability(string? capabilityId, out FinanceAgentCoverageCapabilityManifest capability)
    {
        capability = _coverageCatalogue.ListManifests().SingleOrDefault(x =>
            string.Equals(x.Id, capabilityId?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return capability is not null;
    }

    private static string ComputeCapabilityPolicyHash(
        FinanceAgentCoverageCapabilityManifest capability, IReadOnlyCollection<string> tools)
    {
        var selected = capability.Operations.Where(x => !string.IsNullOrWhiteSpace(x.ToolName) &&
                                                        tools.Contains(x.ToolName!, StringComparer.OrdinalIgnoreCase))
            .OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new
            {
                x.Id, x.ActionClass, x.SupportState, x.RequiredPermission, x.RequiredScope,
                x.RiskTier, x.ApprovalBehavior, x.AvailabilityReasonCode, x.ToolName
            });
        var canonical = JsonSerializer.Serialize(new { capability.Id, capability.Version, Operations = selected });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static bool IsWithinWindow(FinanceAutonomyGrantVersion version, DateTime utc)
    {
        try
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(version.Timezone));
            var time = new TimeOnly(local.Hour, local.Minute);
            var start = TimeOnly.ParseExact(version.WindowStartLocal, "HH:mm");
            var end = TimeOnly.ParseExact(version.WindowEndLocal, "HH:mm");
            return start <= end ? time >= start && time <= end : time >= start || time <= end;
        }
        catch
        {
            return false;
        }
    }

    private static FinanceAutonomyDecisionDto Deny(
        string reasonCode, string explanation, DateTime evaluatedUtc,
        FinanceAutonomyGrant? grant = null, FinanceAutonomyGrantVersion? version = null) =>
        new(false, reasonCode, explanation, grant?.Id, version?.Id, version?.VersionNumber,
            version?.Level.ToStorageValue(), false, false, 0, 0, null,
            FinanceAutonomyPolicyVersions.V1, version?.CatalogueVersion, version?.AuthorityVersion,
            version?.AuthorityHash, evaluatedUtc);

    private static FinanceAutonomyGrantDto Map(FinanceAutonomyGrant grant) =>
        new(grant.Id, grant.CompanyId, grant.AgentId, grant.CapabilityId, grant.ActiveVersionId,
            grant.LatestVersionNumber, grant.Version, grant.CreatedUtc, grant.UpdatedUtc,
            grant.Versions.OrderByDescending(x => x.VersionNumber).Select(Map).ToArray());

    private static FinanceAutonomyGrantVersionDto Map(FinanceAutonomyGrantVersion version) =>
        new(version.Id, version.VersionNumber, version.Level.ToStorageValue(), version.Status.ToStorageValue(),
            version.AllowedTriggers, version.AllowedActionClasses, version.AllowedTools,
            version.MaximumRecordsPerRun, version.MaximumAmountPerRun, version.MaximumActionsPerRun,
            version.ScheduleExpression, version.Timezone, version.WindowStartLocal, version.WindowEndLocal,
            version.EvidenceFreshnessMinutes, version.ConfirmationBehavior, version.EscalationRoute,
            version.EffectiveFromUtc, version.ExpiresUtc, version.CatalogueVersion, version.CapabilityPolicyHash,
            version.AuthorityVersion, version.AuthorityHash, version.CreatedByUserId, version.CreatedUtc,
            version.ReviewedByUserId, version.ReviewReason, version.ReviewedUtc, version.ActivatedUtc,
            version.RevokedByUserId, version.RevocationReason, version.RevokedUtc,
            version.AllowedEventTypes, version.MinimumIntervalMinutes, version.MaximumRunsPerWindow,
            version.DebounceMinutes, version.CatchUpBehavior, version.MaximumCatchUpWindows,
            version.LateEventToleranceMinutes);

    private static FinanceAutonomyControlDto Map(FinanceAutonomyControl control) =>
        new(control.Id, control.CompanyId, control.Scope.ToStorageValue(), control.ScopeKey,
            control.AgentId, control.CapabilityId, control.State.ToStorageValue(), control.Reason,
            control.ChangedByUserId, control.UpdatedUtc, control.Version);

    private static FinanceAutonomyGrantDefinition ToDefinition(
        FinanceAutonomyGrant grant, FinanceAutonomyGrantVersion version) =>
        new(grant.AgentId, grant.CapabilityId, version.Level.ToStorageValue(), version.AllowedTriggers,
            version.AllowedActionClasses, version.AllowedTools, version.MaximumRecordsPerRun,
            version.MaximumAmountPerRun, version.MaximumActionsPerRun, version.ScheduleExpression,
            version.Timezone, version.WindowStartLocal, version.WindowEndLocal, version.EvidenceFreshnessMinutes,
            version.ConfirmationBehavior, version.EscalationRoute, version.EffectiveFromUtc, version.ExpiresUtc,
            version.AllowedEventTypes, version.MinimumIntervalMinutes, version.MaximumRunsPerWindow,
            version.DebounceMinutes, version.CatchUpBehavior, version.MaximumCatchUpWindows,
            version.LateEventToleranceMinutes);

    private async Task WriteAuditAsync(
        Guid companyId, Guid actorId, string action, Guid grantId, string outcome, string summary,
        FinanceAutonomyGrantVersion version, string? rationale, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new AuditEventWriteRequest(
            companyId, AuditActorTypes.User, actorId, action, AuditTargetTypes.FinanceAutonomyGrant,
            grantId.ToString("N"), outcome, string.IsNullOrWhiteSpace(rationale) ? summary : rationale,
            Metadata: new Dictionary<string, string?>
            {
                ["grantVersionId"] = version.Id.ToString("N"),
                ["versionNumber"] = version.VersionNumber.ToString(),
                ["level"] = version.Level.ToStorageValue(),
                ["status"] = version.Status.ToStorageValue(),
                ["catalogueVersion"] = version.CatalogueVersion,
                ["authorityVersion"] = version.AuthorityVersion,
                ["authorityHash"] = version.AuthorityHash
            }), cancellationToken);

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _dbContext.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException ex) { throw new FinanceAutonomyConcurrencyException($"Finance autonomy policy changed concurrently: {ex.Message}"); }
    }

    private async Task<ResolvedCompanyMembershipContext> RequireManagerAsync(
        Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _membershipResolver.ResolveAsync(companyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Active company membership is required.");
        if (membership.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))
            throw new UnauthorizedAccessException("Company manager access is required.");
        return membership;
    }

    private static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

    private static bool IsElevated(FinanceAutonomyLevel level) =>
        level is FinanceAutonomyLevel.SupervisedInternalExecute or FinanceAutonomyLevel.ScheduledBoundedExecute;

    private static string MapCompatibilityLevel(AgentAutonomyLevel level) => level switch
    {
        AgentAutonomyLevel.Level0 => FinanceAutonomyLevels.ReadMonitor,
        AgentAutonomyLevel.Level1 => FinanceAutonomyLevels.RecommendDraft,
        AgentAutonomyLevel.Level2 => FinanceAutonomyLevels.SupervisedInternalExecute,
        AgentAutonomyLevel.Level3 => FinanceAutonomyLevels.ScheduledBoundedExecute,
        _ => FinanceAutonomyLevels.ReadMonitor
    };

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
    private static List<string> NormalizeList(IEnumerable<string>? values) =>
        values?.Where(x => !string.IsNullOrWhiteSpace(x)).Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];

    private static FinanceAutonomyValidationException Validation(string key, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [key] = [message] });

    private sealed record ValidatedDefinition(
        FinanceAutonomyLevel Level,
        FinanceAgentCoverageCapabilityManifest Capability,
        AgentEffectiveAuthorityDto Authority,
        string CapabilityPolicyHash);
}

internal static class FinanceAutonomyCoverageOperationExtensions
{
    public static bool ExternalSideEffectIsNotInternal(this FinanceAgentCoverageOperationManifest operation)
    {
        if (string.IsNullOrWhiteSpace(operation.ToolName)) return true;
        return !FinanceToolRiskPolicyCatalog.TryGet(operation.ToolName, out var risk) ||
               risk.ExternalSideEffectClassification != FinanceToolExternalSideEffects.InternalStateChange;
    }
}
