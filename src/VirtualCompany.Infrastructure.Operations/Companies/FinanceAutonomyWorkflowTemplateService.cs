using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
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

public sealed class FinanceAutonomyWorkflowTemplateService(
    ICompanyMembershipContextResolver memberships,
    IFinanceAgentCoverageCatalogue coverage,
    IFinanceAutonomyGrantService grants,
    IAuditEventWriter audit) : IFinanceAutonomyWorkflowTemplateService
{
    private static readonly IReadOnlySet<string> PermittedRequestedEffects =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "read", "recommend", "create_internal_task", "create_internal_draft" };

    public async Task<IReadOnlyList<FinanceAutonomyWorkflowTemplate>> ListAsync(
        Guid companyId, string? locale, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(companyId, manager: false, cancellationToken);
        _ = NormalizeLocale(locale);
        return FinanceAutonomyWorkflowTemplateCatalogue.All;
    }

    public async Task<FinanceAutonomyWorkflowActivationPreview> PreviewAsync(
        Guid companyId, PreviewFinanceAutonomyWorkflowTemplateCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var member = await RequireMemberAsync(companyId, manager: false, cancellationToken);
        var template = Find(command.TemplateCode);
        var blocking = new List<string>();
        var requestedEffects = (command.RequestedEffects ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var effect in FindUnsupportedRequestedEffects(requestedEffects))
            blocking.Add($"Requested effect '{effect}' is outside the reviewed read/recommend and internal task/draft boundary.");

        FinanceAgentEffectiveCoverageDto? effective = null;
        try { effective = await coverage.GetEffectiveCoverageAsync(companyId, command.AgentId, cancellationToken); }
        catch (Exception ex) when (ex is KeyNotFoundException or UnauthorizedAccessException)
        { blocking.Add(ex.Message); }
        var operation = effective?.Capabilities
            .SingleOrDefault(x => string.Equals(x.Id, template.CapabilityId, StringComparison.OrdinalIgnoreCase))?
            .Operations.SingleOrDefault(x => string.Equals(x.ToolName, template.ToolName, StringComparison.OrdinalIgnoreCase));
        if (effective is not null && operation?.EffectiveState is not (AgentCapabilityStates.Available or AgentCapabilityStates.ApprovalRequired))
            blocking.Add(operation is null
                ? "The reviewed tool is no longer present in the Finance capability catalogue."
                : operation.Explanation);

        var definition = BuildDefinition(template, command.AgentId, command.Timezone);
        var preview = new FinanceAutonomyWorkflowActivationPreview(
            template, blocking.Count == 0, blocking, definition, false, false,
            "This preview creates no grant and activates nothing. A manager may create a separate prospective version; activation remains an explicit reviewed action.");
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
            AuditEventActions.FinanceAutonomyWorkflowTemplatePreviewed,
            AuditTargetTypes.FinanceAutonomyWorkflowTemplate, template.Code,
            blocking.Count == 0 ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            blocking.Count == 0
                ? "A conservative Finance workflow activation preview was generated without activation."
                : "A Finance workflow activation preview exposed blocking authority or effect constraints.",
            Metadata: new Dictionary<string, string?>
            {
                ["templateVersion"] = template.Version,
                ["agentId"] = command.AgentId.ToString("N"),
                ["capabilityId"] = template.CapabilityId,
                ["toolName"] = template.ToolName,
                ["isReady"] = preview.IsReady.ToString()
            }, CorrelationId: $"finance-workflow-preview:{StableHash($"{companyId:N}|{template.Code}|{command.AgentId:N}")}"), cancellationToken);
        return preview;
    }

    public async Task<FinanceAutonomyWorkflowDraftResult> CreateDraftAsync(
        Guid companyId, CreateFinanceAutonomyWorkflowTemplateDraftCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var member = await RequireMemberAsync(companyId, manager: true, cancellationToken);
        var preview = await PreviewAsync(companyId,
            new(command.TemplateCode, command.AgentId, command.Timezone, command.RequestedEffects), cancellationToken);
        if (!preview.IsReady)
            throw Validation(nameof(command), preview.BlockingReasons.ToArray());

        var existing = (await grants.ListAsync(companyId, command.AgentId, cancellationToken))
            .SingleOrDefault(x => string.Equals(x.CapabilityId, preview.Template.CapabilityId, StringComparison.OrdinalIgnoreCase));
        FinanceAutonomyGrantDto grant;
        var created = existing is null;
        var reused = false;
        if (existing is null)
        {
            grant = await grants.CreateAsync(companyId,
                new CreateFinanceAutonomyGrantCommand(preview.ProspectiveGrant,
                    command.Rationale ?? $"Reviewed workflow template {preview.Template.Code} {preview.Template.Version}."),
                cancellationToken);
        }
        else
        {
            var latest = existing.Versions.OrderByDescending(x => x.VersionNumber).First();
            if (latest.Status == "prospective" && Equivalent(latest, preview.ProspectiveGrant))
            {
                grant = existing;
                reused = true;
            }
            else
            {
                grant = await grants.CreateVersionAsync(companyId, existing.Id,
                    new(preview.ProspectiveGrant, existing.Version,
                        command.Rationale ?? $"Reviewed workflow template {preview.Template.Code} {preview.Template.Version}."),
                    cancellationToken);
            }
        }

        var prospective = grant.Versions.OrderByDescending(x => x.VersionNumber).First();
        await audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, member.UserId,
            AuditEventActions.FinanceAutonomyWorkflowTemplateDraftCreated,
            AuditTargetTypes.FinanceAutonomyWorkflowTemplate, preview.Template.Code,
            AuditEventOutcomes.Succeeded,
            reused
                ? "An equivalent prospective Finance workflow grant version already existed; no duplicate was created."
                : "A prospective conservative Finance workflow grant version was created; it was not activated.",
            Metadata: new Dictionary<string, string?>
            {
                ["templateVersion"] = preview.Template.Version,
                ["grantId"] = grant.Id.ToString("N"),
                ["grantVersionId"] = prospective.Id.ToString("N"),
                ["activated"] = "false",
                ["reused"] = reused.ToString()
            }, CorrelationId: $"finance-workflow-draft:{companyId:N}:{prospective.Id:N}"), cancellationToken);
        return new(preview, grant, prospective.Id, created, reused);
    }

    private static FinanceAutonomyGrantDefinition BuildDefinition(
        FinanceAutonomyWorkflowTemplate template, Guid agentId, string timezone)
    {
        var normalizedTimezone = string.IsNullOrWhiteSpace(timezone) ? "Europe/Stockholm" : timezone.Trim();
        return new(agentId, template.CapabilityId,
            template.ActionClass == "recommend"
                ? FinanceAutonomyLevels.RecommendDraft
                : FinanceAutonomyLevels.ReadMonitor,
            template.Triggers, [template.ActionClass], [template.ToolName],
            template.Limits.MaximumRecordsPerRun, null, template.Limits.MaximumActionsPerRun,
            template.DefaultScheduleExpression, normalizedTimezone, "07:00", "18:00",
            template.Limits.EvidenceFreshnessMinutes, FinanceAutonomyConfirmationBehaviors.NoConfirmation,
            template.OwnerRole, null, null, template.EventTypes,
            template.Limits.MinimumIntervalMinutes, template.Limits.MaximumRunsPerWindow,
            template.Limits.DebounceMinutes, FinanceAutonomyCatchUpBehaviors.Latest, 1, 1440);
    }

    private static bool Equivalent(FinanceAutonomyGrantVersionDto version,
        FinanceAutonomyGrantDefinition definition) =>
        string.Equals(version.Level, definition.Level, StringComparison.Ordinal) &&
        version.AllowedTriggers.ToHashSet(StringComparer.Ordinal).SetEquals(definition.AllowedTriggers) &&
        version.AllowedActionClasses.ToHashSet(StringComparer.Ordinal).SetEquals(definition.AllowedActionClasses) &&
        version.AllowedTools.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(definition.AllowedTools) &&
        version.AllowedEventTypes.ToHashSet(StringComparer.Ordinal).SetEquals(definition.AllowedEventTypes ?? []) &&
        version.MaximumRecordsPerRun == definition.MaximumRecordsPerRun &&
        version.MaximumActionsPerRun == definition.MaximumActionsPerRun &&
        version.MaximumAmountPerRun == definition.MaximumAmountPerRun &&
        string.Equals(version.ScheduleExpression, definition.ScheduleExpression, StringComparison.Ordinal) &&
        string.Equals(version.Timezone, definition.Timezone, StringComparison.Ordinal) &&
        version.EvidenceFreshnessMinutes == definition.EvidenceFreshnessMinutes &&
        version.MinimumIntervalMinutes == definition.MinimumIntervalMinutes &&
        version.MaximumRunsPerWindow == definition.MaximumRunsPerWindow &&
        version.DebounceMinutes == definition.DebounceMinutes &&
        string.Equals(version.ConfirmationBehavior, definition.ConfirmationBehavior, StringComparison.Ordinal) &&
        string.Equals(version.EscalationRoute, definition.EscalationRoute, StringComparison.Ordinal);

    private async Task<ResolvedCompanyMembershipContext> RequireMemberAsync(
        Guid companyId, bool manager, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        var member = await memberships.ResolveAsync(companyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Active company membership is required.");
        if (manager && member.MembershipRole is not
            (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))
            throw new UnauthorizedAccessException("Company manager access is required.");
        return member;
    }

    private static FinanceAutonomyWorkflowTemplate Find(string code) =>
        FinanceAutonomyWorkflowTemplateCatalogue.Find(code)
        ?? throw Validation(nameof(code), "The reviewed Finance workflow template was not found.");

    private static string NormalizeLocale(string? locale) =>
        locale?.Trim().StartsWith("sv", StringComparison.OrdinalIgnoreCase) == true ? "sv" : "en";

    internal static IReadOnlyList<string> FindUnsupportedRequestedEffects(IEnumerable<string> requestedEffects) =>
        requestedEffects.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Where(x => !PermittedRequestedEffects.Contains(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();

    private static FinanceAutonomyWorkflowTemplateValidationException Validation(
        string key, params string[] errors) =>
        new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [key] = errors });

    private static string StableHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class FinanceAutonomyWorkflowOutcomeService(
    VirtualCompanyDbContext db,
    IAuditEventWriter audit,
    TimeProvider clock) : IFinanceAutonomyWorkflowOutcomeService
{
    private const string TriggerSource = "finance_autonomy_workflow_template";

    public async Task<FinanceAutonomyWorkflowOutcomeResult> MaterializeAsync(
        Guid companyId, MaterializeFinanceAutonomyWorkflowOutcomeCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!FinanceAutonomyWorkflowOutcomeStates.All.Contains(command.Outcome))
            throw new ArgumentOutOfRangeException(nameof(command.Outcome), command.Outcome, "Unknown workflow outcome.");
        var template = FinanceAutonomyWorkflowTemplateCatalogue.Find(command.TemplateCode)
            ?? throw new InvalidOperationException("The immutable reviewed workflow template is unavailable.");
        var run = await db.FinanceAutonomyRuns.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Sources)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == command.RunId, cancellationToken)
            ?? throw new KeyNotFoundException("Finance autonomy run was not found.");
        if (!await db.FinanceAutonomyRunSteps.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.RunId == run.Id && x.Id == command.StepId,
                    cancellationToken))
            throw new KeyNotFoundException("Finance autonomy run step was not found.");
        if (!string.Equals(run.CapabilityId, template.CapabilityId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The workflow template does not own this run capability.");

        var authoritativeSources = run.Sources.Where(x => x.SourceType == "authoritative_event").ToArray();
        var targetSources = authoritativeSources.Length > 0 ? authoritativeSources : run.Sources.ToArray();
        var targetKey = string.Join("|", targetSources
            .OrderBy(x => x.EntityType, StringComparer.Ordinal).ThenBy(x => x.EntityId, StringComparer.Ordinal)
            .Select(x => $"{x.EntityType}:{x.EntityId}"));
        var targetVersion = Hash(string.Join("|", targetSources
            .OrderBy(x => x.EntityType, StringComparer.Ordinal).ThenBy(x => x.EntityId, StringComparer.Ordinal)
            .Select(x => $"{x.SourceVersion}:{x.ContentHash}")));
        var reviewWindow = $"{run.WindowStartUtc:O}|{run.WindowEndUtc:O}";
        var dedupeKey = Hash($"{companyId:N}|{template.CapabilityId}|{template.Code}|{targetKey}|{targetVersion}|{reviewWindow}");
        var receipt = await db.AgentTaskCreationDedupeRecords.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DedupeKey == dedupeKey, cancellationToken);
        var existingTask = receipt is null ? null : await db.WorkTasks.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == receipt.TaskId, cancellationToken);

        if (command.Outcome == FinanceAutonomyWorkflowOutcomeStates.Healthy)
        {
            var resolved = existingTask is not null && existingTask.Status != WorkTaskStatus.Completed;
            if (resolved)
                existingTask!.UpdateStatus(WorkTaskStatus.Completed,
                    new Dictionary<string, JsonNode?> { ["resolution"] = JsonValue.Create("no_material_exception") },
                    "Current authoritative evidence no longer contains a material exception.");
            if (resolved) await db.SaveChangesAsync(cancellationToken);
            await WriteOutcomeAuditAsync(run, template, command, existingTask?.Id, resolved ? "resolved" : "no_action_required",
                AuditEventOutcomes.Succeeded, cancellationToken);
            return new(command.Outcome, existingTask?.Id, false, receipt is not null, resolved, false, 0, dedupeKey);
        }

        if (existingTask is not null)
        {
            var reopened = existingTask.Status == WorkTaskStatus.Completed;
            if (reopened)
            {
                existingTask.UpdateStatus(TaskStatus(command.Outcome),
                    new Dictionary<string, JsonNode?> { ["reopenedByRunId"] = JsonValue.Create(run.Id) },
                    "The same bounded evidence target requires review again.");
                await db.SaveChangesAsync(cancellationToken);
            }
            await WriteOutcomeAuditAsync(run, template, command, existingTask.Id,
                reopened ? "reopened" : "duplicate_suppressed", AuditEventOutcomes.Succeeded, cancellationToken);
            return new(command.Outcome, existingTask.Id, false, !reopened, false, reopened, 0, dedupeKey);
        }

        var related = (await db.WorkTasks.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Type == TaskType(template.Code) &&
                            x.TriggerSource == TriggerSource && x.Status != WorkTaskStatus.Completed)
                .OrderByDescending(x => x.CreatedUtc).Take(100).ToListAsync(cancellationToken))
            .Where(x => Read(x.InputPayload, "targetKey") == targetKey &&
                        Read(x.InputPayload, "reviewWindow") == reviewWindow)
            .ToArray();
        foreach (var prior in related)
            prior.UpdateStatus(WorkTaskStatus.Completed,
                new Dictionary<string, JsonNode?>
                {
                    ["resolution"] = JsonValue.Create("superseded_by_new_source_version"),
                    ["supersededByRunId"] = JsonValue.Create(run.Id)
                }, "A newer authoritative source version superseded this review item.");

        var actorUserId = await db.FinanceAutonomyGrantVersions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id == run.GrantVersionId)
            .Select(x => x.ReviewedByUserId ?? x.CreatedByUserId).SingleAsync(cancellationToken);
        var taskId = Guid.NewGuid();
        var task = new WorkTask(taskId, companyId, TaskType(template.Code), template.TaskTitle.En,
            template.Description.En, Priority(command.Outcome), null, null, AuditActorTypes.System, null,
            new Dictionary<string, JsonNode?>
            {
                ["templateCode"] = JsonValue.Create(template.Code),
                ["templateVersion"] = JsonValue.Create(template.Version),
                ["localizedTitleSv"] = JsonValue.Create(template.TaskTitle.Sv),
                ["outcome"] = JsonValue.Create(command.Outcome),
                ["ownerRole"] = JsonValue.Create(template.OwnerRole),
                ["ownerUserId"] = JsonValue.Create(actorUserId),
                ["runId"] = JsonValue.Create(run.Id),
                ["stepId"] = JsonValue.Create(command.StepId),
                ["policyVersion"] = JsonValue.Create(run.PolicyVersion),
                ["grantId"] = JsonValue.Create(run.GrantId),
                ["grantVersionId"] = JsonValue.Create(run.GrantVersionId),
                ["grantVersionNumber"] = JsonValue.Create(run.GrantVersionNumber),
                ["capabilityId"] = JsonValue.Create(run.CapabilityId),
                ["targetKey"] = JsonValue.Create(targetKey),
                ["targetVersion"] = JsonValue.Create(targetVersion),
                ["reviewWindow"] = JsonValue.Create(reviewWindow),
                ["nextHumanAction"] = JsonValue.Create(template.NextHumanAction.En),
                ["nextHumanActionSv"] = JsonValue.Create(template.NextHumanAction.Sv),
                ["sources"] = new JsonArray(run.Sources.Select(x => (JsonNode)new JsonObject
                {
                    ["sourceType"] = x.SourceType,
                    ["entityType"] = x.EntityType,
                    ["entityId"] = x.EntityId,
                    ["sourceVersion"] = x.SourceVersion,
                    ["contentHash"] = x.ContentHash,
                    ["safeLabel"] = x.SafeLabel
                }).ToArray())
            }, run.WorkflowInstanceId, rationaleSummary: command.SafeSummary,
            correlationId: run.CorrelationId, sourceType: WorkTaskSourceTypes.Agent,
            originatingAgentId: run.AgentId, triggerSource: TriggerSource,
            creationReason: "A reviewed low-risk Finance workflow found evidence that requires human review.",
            triggerEventId: dedupeKey, status: TaskStatus(command.Outcome));
        db.WorkTasks.Add(task);
        var dedupeCreatedUtc = UtcNow();
        var dedupeExpiresUtc = new[]
        {
            run.WindowEndUtc.AddMinutes(template.Limits.ReviewWindowMinutes),
            dedupeCreatedUtc.AddMinutes(template.Limits.ReviewWindowMinutes)
        }.Max();
        db.AgentTaskCreationDedupeRecords.Add(new AgentTaskCreationDedupeRecord(Guid.NewGuid(), companyId,
            dedupeKey, taskId, run.AgentId, TriggerSource, dedupeKey, run.CorrelationId,
            dedupeCreatedUtc, dedupeExpiresUtc));
        await db.SaveChangesAsync(cancellationToken);
        await WriteOutcomeAuditAsync(run, template, command, taskId, "created",
            AuditEventOutcomes.Succeeded, cancellationToken);
        return new(command.Outcome, taskId, true, false, false, false, related.Length, dedupeKey);
    }

    private async Task WriteOutcomeAuditAsync(FinanceAutonomyRun run,
        FinanceAutonomyWorkflowTemplate template, MaterializeFinanceAutonomyWorkflowOutcomeCommand command,
        Guid? taskId, string disposition, string auditOutcome, CancellationToken cancellationToken) =>
        await audit.WriteAsync(new AuditEventWriteRequest(run.CompanyId, AuditActorTypes.System, null,
            AuditEventActions.FinanceAutonomyWorkflowOutcomeMaterialized,
            AuditTargetTypes.FinanceAutonomyWorkflowTemplate, template.Code, auditOutcome,
            disposition == "no_action_required"
                ? "The scheduled Finance review found no material exception and created no task."
                : "The reviewed Finance workflow outcome was materialized idempotently.",
            Metadata: new Dictionary<string, string?>
            {
                ["templateVersion"] = template.Version,
                ["runId"] = run.Id.ToString("N"),
                ["stepId"] = command.StepId.ToString("N"),
                ["taskId"] = taskId?.ToString("N"),
                ["outcome"] = command.Outcome,
                ["disposition"] = disposition,
                ["grantVersionId"] = run.GrantVersionId.ToString("N"),
                ["policyVersion"] = run.PolicyVersion
            }, CorrelationId: run.CorrelationId), cancellationToken);

    private static WorkTaskStatus TaskStatus(string outcome) =>
        outcome is FinanceAutonomyWorkflowOutcomeStates.Stale or FinanceAutonomyWorkflowOutcomeStates.Missing
            ? WorkTaskStatus.Blocked : WorkTaskStatus.New;
    private static WorkTaskPriority Priority(string outcome) =>
        outcome == FinanceAutonomyWorkflowOutcomeStates.Missing ? WorkTaskPriority.High : WorkTaskPriority.Normal;
    private static string TaskType(string code) => $"finance.autonomy.workflow.{code}";
    private static string? Read(IReadOnlyDictionary<string, JsonNode?> payload, string key) =>
        payload.TryGetValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text)
            ? text : null;
    private DateTime UtcNow() => clock.GetUtcNow().UtcDateTime;
    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
