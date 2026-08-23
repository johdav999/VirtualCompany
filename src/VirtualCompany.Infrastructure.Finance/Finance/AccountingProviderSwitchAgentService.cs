using System.Globalization;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchAgentService : IAccountingProviderSwitchAgentService
{
    private readonly IAccountingProviderSwitchService _switches;
    private readonly IAccountingProviderSwitchAssessmentService _assessments;
    private readonly IAccountingProviderSwitchStagingService _staging;
    private readonly IAccountingProviderSwitchRehearsalService _rehearsals;
    private readonly IAccountingProviderSwitchPreparationService _preparation;
    private readonly IAccountingProviderSwitchTargetTransferService _targetTransfers;
    private readonly IAccountingProviderSwitchCutoverService _cutovers;
    private readonly IAccountingProviderSwitchMonitoringService _monitoring;
    private readonly IAuditQueryService _audit;
    private readonly TimeProvider _timeProvider;

    public AccountingProviderSwitchAgentService(
        IAccountingProviderSwitchService switches,
        IAccountingProviderSwitchAssessmentService assessments,
        IAccountingProviderSwitchStagingService staging,
        IAccountingProviderSwitchRehearsalService rehearsals,
        IAccountingProviderSwitchPreparationService preparation,
        IAccountingProviderSwitchTargetTransferService targetTransfers,
        IAccountingProviderSwitchCutoverService cutovers,
        IAccountingProviderSwitchMonitoringService monitoring,
        IAuditQueryService audit,
        TimeProvider timeProvider)
    {
        _switches = switches;
        _assessments = assessments;
        _staging = staging;
        _rehearsals = rehearsals;
        _preparation = preparation;
        _targetTransfers = targetTransfers;
        _cutovers = cutovers;
        _monitoring = monitoring;
        _audit = audit;
        _timeProvider = timeProvider;
    }

    public async Task<AccountingProviderSwitchAgentBriefingDto> GetBriefingAsync(
        GetAccountingProviderSwitchAgentBriefingQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query.CompanyId, query.SwitchId);
        var max = Math.Clamp(query.MaxItems, 1, 50);
        var providerSwitch = await _switches.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken);
        var allowed = await _switches.GetAllowedActionsAsync(new(query.CompanyId, query.SwitchId), cancellationToken);
        var assessment = await OptionalAsync(
            () => _assessments.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken));
        var rehearsal = await OptionalAsync(
            () => _rehearsals.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken));
        var cutover = await OptionalAsync(
            () => _cutovers.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken));
        var monitoring = await OptionalAsync(
            () => _monitoring.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken));

        var blockers = new List<string>();
        if (!string.IsNullOrWhiteSpace(providerSwitch.FailureSummary))
            blockers.Add("The current workflow has a recorded failure that an accounting administrator must review in the secured migration record.");
        if (assessment is not null)
            blockers.AddRange(assessment.Gaps.Where(x => x.IsBlocking).Take(max).Select(x => x.Explanation));
        if (rehearsal is not null)
            blockers.AddRange(rehearsal.Checks.Where(x => !IsPassed(x.Result)).Take(max)
                .Select(x => $"{Plain(x.CheckKey)} needs attention: {Plain(x.ReasonCode)}."));
        if (cutover?.ProviderReconciliationRequired == true)
            blockers.Add("The provider outcome must be reconciled before the cutover can continue.");
        if (monitoring is not null)
            blockers.AddRange(monitoring.Incidents.Where(x => x.Status == AccountingProviderSwitchMonitoringIncidentStatuses.Open && x.IsBlocking)
                .Take(max).Select(x => x.Explanation));

        var evidence = new List<string>
        {
            $"The current persisted step is {providerSwitch.StatusLabel}.",
            $"The source is {providerSwitch.Source.DisplayName} and the target is {providerSwitch.Target.DisplayName}.",
            $"The selected approach is {providerSwitch.MigrationStrategyLabel} for {providerSwitch.EffectiveFrom:yyyy-MM-dd}."
        };
        if (assessment is not null)
            evidence.Add($"Assessment is {Plain(assessment.Status)} with {assessment.ProgressPercent}% complete and {assessment.Gaps.Count} recorded gap(s).");
        if (rehearsal is not null)
            evidence.Add($"Rehearsal is {Plain(rehearsal.Status)} with {rehearsal.ProgressPercent}% complete.");
        if (cutover is not null)
            evidence.Add($"Cutover is {Plain(cutover.Status)} at {Plain(cutover.CurrentStep)}.");
        if (monitoring is not null)
            evidence.Add($"Post-activation monitoring is {Plain(monitoring.Status)}; pass {monitoring.CheckSequence} has {monitoring.Incidents.Count(x => x.Status == AccountingProviderSwitchMonitoringIncidentStatuses.Open)} open issue(s) and ends {monitoring.WindowEndsUtc:yyyy-MM-dd}.");

        var actions = allowed.AllowedTransitions.Select(Plain).ToList();
        if (allowed.CanUpdatePlan) actions.Add("Review or update the migration plan");
        if (allowed.CanCancel) actions.Add("Cancel before activation");
        if (cutover?.AllowedActions.RequiresProviderReconciliation == true) actions.Add("Reconcile the provider outcome");
        if (monitoring?.AllowedActions.CanRunNow == true) actions.Add("Run current monitoring checks");
        if (monitoring?.AllowedActions.CanRetry == true) actions.Add("Retry monitoring after resolving the failure");
        if (monitoring?.AllowedActions.CanRequestClosure == true) actions.Add("Request monitoring closure approval");
        if (monitoring?.AllowedActions.CanCreateCorrectiveCutover == true) actions.Add("Prepare a separately controlled corrective cutover");

        return new AccountingProviderSwitchAgentBriefingDto(
            providerSwitch.Id,
            providerSwitch.Version,
            providerSwitch.StatusLabel,
            ExplainWhy(providerSwitch.Status),
            blockers.Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToArray(),
            evidence.Take(max).ToArray(),
            actions.Distinct(StringComparer.OrdinalIgnoreCase).Take(max).ToArray(),
            providerSwitch.ResponsibleAgentId.HasValue ? "Laura and the responsible accounting administrator" : "The responsible accounting administrator",
            ResolveNextCheckpoint(providerSwitch, allowed, assessment, rehearsal, cutover, monitoring),
            ["accounting switch", "allowed actions", "assessment evidence", "rehearsal evidence", "cutover evidence", "post-activation monitoring evidence"],
            UtcNow());
    }

    public async Task<AccountingProviderSwitchAgentEvidenceDto> GetEvidenceAsync(
        GetAccountingProviderSwitchAgentEvidenceQuery query,
        CancellationToken cancellationToken)
    {
        Validate(query.CompanyId, query.SwitchId);
        var providerSwitch = await _switches.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken);
        var max = Math.Clamp(query.MaxItems, 1, 50);
        var items = new List<AccountingProviderSwitchAgentEvidenceItemDto>();
        var sources = new List<string> { "accounting switch" };

        switch (query.View)
        {
            case AccountingProviderSwitchAgentEvidenceViews.Status:
                var allowed = await _switches.GetAllowedActionsAsync(new(query.CompanyId, query.SwitchId), cancellationToken);
                items.Add(new("Current step", providerSwitch.StatusLabel, allowed.Explanation,
                    providerSwitch.Id.ToString("D"), !allowed.IsReadyForNextStep));
                sources.Add("allowed actions");
                break;

            case AccountingProviderSwitchAgentEvidenceViews.Capabilities:
            case AccountingProviderSwitchAgentEvidenceViews.Inventory:
            case AccountingProviderSwitchAgentEvidenceViews.Gaps:
                var assessment = await _assessments.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken);
                sources.Add("persisted assessment");
                if (query.View == AccountingProviderSwitchAgentEvidenceViews.Capabilities)
                    items.AddRange(assessment.Capabilities.Take(max).Select(x => new AccountingProviderSwitchAgentEvidenceItemDto(
                        $"{Plain(x.EndpointRole)}: {Plain(x.CapabilityKey)}", Plain(x.Level), x.Explanation, NeedsAttention: IsAttentionLevel(x.Level))));
                else if (query.View == AccountingProviderSwitchAgentEvidenceViews.Inventory)
                    items.AddRange(assessment.Datasets.Take(max).Select(x => new AccountingProviderSwitchAgentEvidenceItemDto(
                        $"{Plain(x.EndpointRole)}: {Plain(x.DatasetKey)}", Plain(x.Availability),
                        $"{x.RecordCount} record(s) were observed; capability is {Plain(x.CapabilityLevel)}.", NeedsAttention: IsAttentionLevel(x.Availability))));
                else
                    items.AddRange(assessment.Gaps.Take(max).Select(x => new AccountingProviderSwitchAgentEvidenceItemDto(
                        Plain(x.Category), x.IsBlocking ? "Blocks progress" : "Review recommended", x.Explanation,
                        x.Id.ToString("D"), x.IsBlocking)));
                break;

            case AccountingProviderSwitchAgentEvidenceViews.Mappings:
                var completeness = await _staging.GetCompletenessAsync(new(query.CompanyId, query.SwitchId), cancellationToken);
                items.AddRange(completeness.Datasets.Take(max).Select(x => new AccountingProviderSwitchAgentEvidenceItemDto(
                    Plain(x.Dataset), x.IsComplete ? "Complete" : "Needs attention", x.Explanation, NeedsAttention: !x.IsComplete)));
                sources.Add("staging completeness");
                break;

            case AccountingProviderSwitchAgentEvidenceViews.Rehearsal:
            case AccountingProviderSwitchAgentEvidenceViews.Reconciliation:
                var rehearsal = await _rehearsals.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken);
                sources.Add("persisted rehearsal");
                if (query.View == AccountingProviderSwitchAgentEvidenceViews.Rehearsal)
                    items.AddRange(rehearsal.Datasets.Take(max).Select(x => new AccountingProviderSwitchAgentEvidenceItemDto(
                        Plain(x.Dataset), Plain(x.Result),
                        $"Expected {x.ExpectedCount} record(s); observed {x.ObservedCount}.", x.Id.ToString("D"), !IsPassed(x.Result))));
                else
                    items.AddRange(rehearsal.Checks.Take(max).Select(x => new AccountingProviderSwitchAgentEvidenceItemDto(
                        Plain(x.CheckKey), Plain(x.Result), Plain(x.ReasonCode), x.Id.ToString("D"), !IsPassed(x.Result))));
                break;

            case AccountingProviderSwitchAgentEvidenceViews.Approvals:
                var readiness = await _rehearsals.GetPlanReadinessAsync(new(query.CompanyId, query.SwitchId), cancellationToken);
                items.Add(new("Migration plan", readiness.Plan?.ApprovalStatus is null ? "Not requested" : Plain(readiness.Plan.ApprovalStatus),
                    readiness.Explanation, readiness.Plan?.ApprovalRequestId?.ToString("D"), !readiness.IsReady));
                var approvalCutover = await OptionalAsync(() => _cutovers.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken));
                if (approvalCutover?.ActivationApproval is not null)
                    items.Add(new("Activation approval", Plain(approvalCutover.ActivationApproval.Status),
                        "Activation approval is bound to the final snapshot and reconciliation evidence.",
                        approvalCutover.ActivationApproval.ApprovalRequestId.ToString("D"),
                        !string.Equals(approvalCutover.ActivationApproval.Status, "approved", StringComparison.OrdinalIgnoreCase)));
                sources.Add("approval bindings");
                break;

            case AccountingProviderSwitchAgentEvidenceViews.TransferProgress:
                await AddTransferEvidenceAsync(query.CompanyId, query.SwitchId, max, items, sources, cancellationToken);
                break;

            case AccountingProviderSwitchAgentEvidenceViews.Monitoring:
                var monitoringCutover = await OptionalAsync(() => _cutovers.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken));
                var monitoringRun = await OptionalAsync(() => _monitoring.GetAsync(new(query.CompanyId, query.SwitchId), cancellationToken));
                if (monitoringRun is null)
                    items.Add(new("Post-cutover monitoring", MonitoringStatus(providerSwitch, monitoringCutover),
                        MonitoringExplanation(providerSwitch, monitoringCutover),
                        monitoringCutover?.Id.ToString("D"), monitoringCutover?.ProviderReconciliationRequired == true));
                else
                    items.AddRange(monitoringRun.Checks.Take(max).Select(x => new AccountingProviderSwitchAgentEvidenceItemDto(
                        Plain(x.CheckKey), Plain(x.Status), x.Explanation, monitoringRun.Id.ToString("D"), x.IsBlocking)));
                sources.Add("durable post-activation monitoring state");
                break;

            case AccountingProviderSwitchAgentEvidenceViews.Audit:
                var history = await _audit.ListAsync(query.CompanyId, new AuditHistoryFilter(Take: Math.Min(max * 5, 100)), cancellationToken);
                items.AddRange(history.Items.Where(x => IsSwitchAudit(x, query.SwitchId)).Take(max).Select(x =>
                    new AccountingProviderSwitchAgentEvidenceItemDto(
                        x.Explanation.Summary, Plain(x.Outcome), x.Explanation.WhyThisAction,
                        x.Id.ToString("D"), string.Equals(x.Outcome, "failed", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(x.Outcome, "blocked", StringComparison.OrdinalIgnoreCase))));
                sources.Add("business audit history");
                break;

            default:
                throw new ArgumentException("The requested migration evidence view is not supported.", nameof(query));
        }

        return new AccountingProviderSwitchAgentEvidenceDto(
            query.SwitchId, providerSwitch.Version, query.View,
            items.Count == 0 ? "No current evidence is available for this step." : $"{items.Count} current evidence item(s) were found.",
            items.Take(max).ToArray(), sources.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), UtcNow());
    }

    public async Task<AccountingProviderSwitchAgentRecommendationDto> RecommendAsync(
        RecommendAccountingProviderSwitchActionQuery query,
        CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(query));
        AccountingProviderSwitchDto? providerSwitch = query.SwitchId.HasValue
            ? await _switches.GetAsync(new(query.CompanyId, query.SwitchId.Value), cancellationToken)
            : (await _switches.ListAsync(new(query.CompanyId, Limit: 10), cancellationToken)).FirstOrDefault(x => !IsTerminal(x.Status));

        var (recommendation, rationale, preconditions, confidence) = query.RecommendationType switch
        {
            AccountingProviderSwitchAgentToolIds.RecommendEffectivePeriod => RecommendEffectivePeriod(providerSwitch),
            AccountingProviderSwitchAgentToolIds.RecommendStrategy => RecommendStrategy(providerSwitch, query.RequestedStrategy),
            AccountingProviderSwitchAgentToolIds.RecommendMapping => ("Review the current mapping evidence and submit material mappings for approval before applying them.", "Mappings must remain versioned and tied to current staged source hashes.", new[] { "Current staging evidence", "A named target key", "Approval for material mappings" }, 0.92m),
            AccountingProviderSwitchAgentToolIds.RecommendGapResolution => ("Resolve blocking gaps from their persisted operator actions before moving forward.", "Gap severity is determined by Finance policy; Laura cannot waive a blocking gap.", new[] { "Current assessment", "Named evidence owner", "Fresh provider extraction when required" }, 0.96m),
            AccountingProviderSwitchAgentToolIds.RecommendRequiredEvidence => ("Collect the evidence named by each incomplete reconciliation check and keep it linked to the current snapshot.", "Evidence that is stale, free-form only, or bound to an older snapshot cannot prove readiness.", new[] { "Current rehearsal", "Current source snapshot", "System-produced totals" }, 0.95m),
            AccountingProviderSwitchAgentToolIds.RecommendCutoverPlan => ("Generate a cutover plan only after rehearsal passes, then request independent approval.", "The immutable plan must bind the source snapshot, strategy, mappings, participants, freeze window, and recovery boundary.", new[] { "Successful rehearsal", "Complete dispositions", "No blocking gaps" }, 0.95m),
            AccountingProviderSwitchAgentToolIds.RecommendFreezeWindow => ("Use the approved low-activity window around the effective monthly boundary and allow time for final extraction and reconciliation.", "A quiet source period reduces final-delta risk, but the exact window must remain in the approved persisted plan.", new[] { "Approved plan", "Reached monthly boundary", "Healthy source and target connections" }, 0.88m),
            AccountingProviderSwitchAgentToolIds.RecommendMonitoringPeriod => ("Monitor the new authority for at least 14 days; extend to 30 days for full-history or provider-to-provider moves.", "Longer and more complex migrations need more time to detect duplicate, missing, permission, bank, tax, and control-account differences.", new[] { "Target activated", "Monitoring owner", "Escalation route for financial differences" }, 0.86m),
            AccountingProviderSwitchAgentToolIds.ExplainReadiness => await ExplainReadinessAsync(query, providerSwitch, cancellationToken),
            _ => throw new ArgumentException("The requested migration recommendation is not supported.", nameof(query))
        };

        return new AccountingProviderSwitchAgentRecommendationDto(
            providerSwitch?.Id, providerSwitch?.Version, query.RecommendationType, recommendation, rationale,
            preconditions, providerSwitch is null ? ["accounting switch list", "migration policy"] : ["accounting switch", "persisted workflow evidence", "migration policy"],
            confidence, UtcNow());
    }

    public async Task<AccountingProviderSwitchAgentCommandResultDto> StartAssessmentAsync(
        AccountingProviderSwitchAgentCommandContext context, CancellationToken cancellationToken)
    {
        Validate(context);
        var result = await _assessments.StartAsync(new(context.CompanyId, context.SwitchId,
            context.ExpectedSwitchVersion, context.ActorUserId, context.CorrelationId, context.IdempotencyKey), cancellationToken);
        return await CommandResultAsync(context, AccountingProviderSwitchAgentToolIds.StartAssessment,
            Plain(result.Status), "Assessment was queued from current persisted switch evidence.",
            "Wait for the durable assessment to finish, then review blocking gaps.", cancellationToken);
    }

    public async Task<AccountingProviderSwitchAgentCommandResultDto> StartRehearsalAsync(
        AccountingProviderSwitchAgentCommandContext context, CancellationToken cancellationToken)
    {
        Validate(context);
        var result = await _rehearsals.StartAsync(new(context.CompanyId, context.SwitchId,
            context.ExpectedSwitchVersion, context.ActorUserId, context.CorrelationId, context.IdempotencyKey), cancellationToken);
        return await CommandResultAsync(context, AccountingProviderSwitchAgentToolIds.StartRehearsal,
            Plain(result.Status), "Rehearsal was queued without changing accounting authority.",
            "Review reconciliation checks after the rehearsal finishes.", cancellationToken);
    }

    public async Task<AccountingProviderSwitchAgentCommandResultDto> StartPreparationAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid planId, CancellationToken cancellationToken)
    {
        Validate(context);
        if (planId == Guid.Empty) throw new ArgumentException("PlanId is required.", nameof(planId));
        var providerSwitch = await _switches.GetAsync(new(context.CompanyId, context.SwitchId), cancellationToken);
        string status;
        if (string.Equals(providerSwitch.Target.Kind, "internal", StringComparison.OrdinalIgnoreCase))
        {
            var preparation = await _preparation.StartAsync(new(context.CompanyId, context.SwitchId, planId,
                context.ExpectedSwitchVersion, context.ActorUserId, context.IdempotencyKey, context.CorrelationId), cancellationToken);
            status = preparation.Status;
        }
        else
        {
            var transfer = await _targetTransfers.StartAsync(new(context.CompanyId, context.SwitchId, planId,
                context.ExpectedSwitchVersion, context.ActorUserId, context.IdempotencyKey, context.CorrelationId), cancellationToken);
            status = transfer.Status;
        }

        return await CommandResultAsync(context, AccountingProviderSwitchAgentToolIds.StartPreparation,
            Plain(status), "Approved target preparation was started; accounting authority was not changed.",
            "Review preparation progress and reconcile any provider uncertainty.", cancellationToken);
    }

    public async Task<AccountingProviderSwitchAgentCommandResultDto> ApplyApprovedMappingAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid stagedRecordId, Guid mappingDecisionId,
        long expectedRecordVersion, string disposition, CancellationToken cancellationToken)
    {
        Validate(context);
        if (stagedRecordId == Guid.Empty || mappingDecisionId == Guid.Empty || expectedRecordVersion <= 0)
            throw new ArgumentException("Current staged record and approved mapping versions are required.");
        var result = await _staging.ResolveDispositionAsync(new(context.CompanyId, context.SwitchId,
            stagedRecordId, disposition, "Applied through Laura using a current approved mapping.",
            mappingDecisionId, null, expectedRecordVersion, context.ActorUserId, context.CorrelationId), cancellationToken);
        return await CommandResultAsync(context, AccountingProviderSwitchAgentToolIds.ApplyApprovedMapping,
            Plain(result.Disposition), "The already approved mapping was applied to the current staged record version.",
            "Recheck staging completeness before rehearsal.", cancellationToken);
    }

    public async Task<AccountingProviderSwitchAgentCommandResultDto> RequestPlanApprovalAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid planId, CancellationToken cancellationToken)
    {
        Validate(context);
        var result = await _rehearsals.RequestPlanApprovalAsync(new(context.CompanyId, context.SwitchId,
            planId, context.ExpectedSwitchVersion, context.ActorUserId, context.CorrelationId), cancellationToken);
        return await CommandResultAsync(context, AccountingProviderSwitchAgentToolIds.RequestPlanApproval,
            Plain(result.ApprovalStatus ?? "pending"), "Independent approval was requested for the current immutable cutover plan.",
            "Wait for an authorized accounting administrator to decide the request.", cancellationToken);
    }

    public async Task<AccountingProviderSwitchAgentCommandResultDto> StartApprovedFreezeAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid cutoverExecutionId,
        long expectedExecutionVersion, CancellationToken cancellationToken)
    {
        Validate(context);
        var result = await _cutovers.StartFreezeAsync(new(context.CompanyId, context.SwitchId,
            cutoverExecutionId, expectedExecutionVersion, context.ActorUserId, context.CorrelationId), cancellationToken);
        return await CommandResultAsync(context, AccountingProviderSwitchAgentToolIds.StartApprovedFreeze,
            Plain(result.Status), "The approved final freeze and extraction workflow was started.",
            result.ProviderReconciliationRequired ? "Reconcile the provider outcome before continuing." : "Wait for final reconciliation evidence.",
            cancellationToken);
    }

    public async Task<AccountingProviderSwitchAgentCommandResultDto> RequestActivationApprovalAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid cutoverExecutionId,
        long expectedExecutionVersion, CancellationToken cancellationToken)
    {
        Validate(context);
        var result = await _cutovers.RequestActivationApprovalAsync(new(context.CompanyId, context.SwitchId,
            cutoverExecutionId, expectedExecutionVersion, context.ActorUserId, context.CorrelationId), cancellationToken);
        return await CommandResultAsync(context, AccountingProviderSwitchAgentToolIds.RequestActivationApproval,
            Plain(result.ActivationApproval?.Status ?? "pending"), "Separate activation approval was requested against current final evidence.",
            "An authorized user must approve activation; Laura cannot activate authority.", cancellationToken);
    }

    public async Task<AccountingProviderSwitchAgentCommandResultDto> ResumeRecoveryAsync(
        AccountingProviderSwitchAgentCommandContext context, Guid cutoverExecutionId,
        long expectedExecutionVersion, CancellationToken cancellationToken)
    {
        Validate(context);
        var result = await _cutovers.ResumeAsync(new(context.CompanyId, context.SwitchId,
            cutoverExecutionId, expectedExecutionVersion, context.ActorUserId, context.CorrelationId), cancellationToken);
        return await CommandResultAsync(context, AccountingProviderSwitchAgentToolIds.ResumeRecovery,
            Plain(result.Status), "The persisted recovery workflow was resumed after its safety checks passed.",
            result.NextAction ?? "Review the refreshed cutover evidence.", cancellationToken);
    }

    private async Task AddTransferEvidenceAsync(Guid companyId, Guid switchId, int max,
        List<AccountingProviderSwitchAgentEvidenceItemDto> items, List<string> sources,
        CancellationToken cancellationToken)
    {
        var transfer = await OptionalAsync(() => _targetTransfers.GetAsync(new(companyId, switchId), cancellationToken));
        if (transfer is not null)
        {
            items.Add(new("External target preparation", Plain(transfer.Status),
                $"{transfer.CompletedItemCount} of {transfer.TotalItemCount} item(s) completed; {transfer.ReconciliationItemCount} need reconciliation.",
                transfer.Id.ToString("D"), transfer.ReconciliationItemCount > 0 || transfer.FailedItemCount > 0));
            items.AddRange(transfer.Items.Where(x => x.ReconciliationNeeded || !string.IsNullOrWhiteSpace(x.FailureCategory)).Take(max - 1)
                .Select(x => new AccountingProviderSwitchAgentEvidenceItemDto(Plain(x.Dataset), Plain(x.Status),
                    x.SafeSummary ?? "This transfer item needs review.", x.Id.ToString("D"), true)));
            sources.Add("target transfer batch");
            return;
        }

        var preparation = await OptionalAsync(() => _preparation.GetAsync(new(companyId, switchId), cancellationToken));
        if (preparation is not null)
        {
            items.Add(new("Internal target preparation", Plain(preparation.Status),
                $"{preparation.ValidCandidateCount} valid candidate(s); {preparation.RejectedCandidateCount} rejected candidate(s).",
                preparation.Id.ToString("D"), !preparation.IsActivationReady));
            sources.Add("native candidate preparation");
        }
    }

    private async Task<(string, string, string[], decimal)> ExplainReadinessAsync(
        RecommendAccountingProviderSwitchActionQuery query, AccountingProviderSwitchDto? providerSwitch,
        CancellationToken cancellationToken)
    {
        if (providerSwitch is null)
            return ("Create and review a draft switch before assessing readiness.",
                "No active persisted switch exists for this company.", ["Source and target", "Future monthly period", "Migration strategy", "Responsible accounting administrator"], 0.99m);
        var briefing = await GetBriefingAsync(new(query.CompanyId, providerSwitch.Id, query.MaxItems), cancellationToken);
        return briefing.Blockers.Count == 0
            ? ($"The switch is at {briefing.CurrentStep}; follow the allowed next checkpoint: {briefing.NextCheckpoint}",
                "No current blocking evidence was found, but backend policy will recheck every sensitive transition.", briefing.AllowedActions.ToArray(), 0.94m)
            : ($"Do not advance yet. Resolve {briefing.Blockers.Count} blocking item(s) first.",
                string.Join(" ", briefing.Blockers.Take(3)), briefing.AllowedActions.ToArray(), 0.98m);
    }

    private async Task<AccountingProviderSwitchAgentCommandResultDto> CommandResultAsync(
        AccountingProviderSwitchAgentCommandContext context, string operation, string status,
        string summary, string nextCheckpoint, CancellationToken cancellationToken)
    {
        var current = await _switches.GetAsync(new(context.CompanyId, context.SwitchId), cancellationToken);
        return new(current.Id, current.Version, operation, status, summary, nextCheckpoint,
            ["policy guardrail", "accounting switch", "Finance application service"], new JsonObject
            {
                ["persisted"] = true,
                ["switchVersion"] = current.Version,
                ["refreshRequired"] = true
            });
    }

    private (string, string, string[], decimal) RecommendEffectivePeriod(AccountingProviderSwitchDto? providerSwitch)
    {
        if (providerSwitch is not null)
            return ($"Keep the persisted effective period beginning {providerSwitch.EffectiveFrom:yyyy-MM-dd} unless new evidence requires a reviewed plan change.",
                "The current date is already bound to an existing monthly fiscal period and switch version.", ["Future monthly fiscal period", "Source authority through the prior day"], 0.97m);
        var today = DateOnly.FromDateTime(UtcNow());
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        return ($"Review {nextMonth:yyyy-MM-dd} as the earliest candidate monthly boundary.",
            "A switch can begin only at a future existing monthly fiscal period; Finance must validate the actual period when the draft is created.", ["Existing fiscal period", "Source authority", "Accounting administrator review"], 0.82m);
    }

    private static (string, string, string[], decimal) RecommendStrategy(AccountingProviderSwitchDto? providerSwitch, string? requested)
    {
        if (providerSwitch is not null)
            return ($"Continue with {providerSwitch.MigrationStrategyLabel} unless reporting or archive requirements justify a reviewed plan change.",
                "The recommendation follows the current persisted strategy and does not change the plan.", ["Current gap assessment", "Archive requirements", "Accounting administrator review"], 0.95m);
        var requestedLabel = string.IsNullOrWhiteSpace(requested) ? null : Plain(requested);
        return (requestedLabel is null
                ? "Start with opening balances and open items."
                : $"Compare {requestedLabel} with opening balances and open items before creating the draft.",
            "Opening balances and open items is the lower-risk default; broader history needs stronger evidence and reconciliation.", ["Reporting requirements", "Legal archive needs", "Source archive availability"], 0.9m);
    }

    private static string ResolveNextCheckpoint(AccountingProviderSwitchDto providerSwitch,
        AccountingProviderSwitchAllowedActionsDto allowed, AccountingProviderSwitchAssessmentDto? assessment,
        AccountingProviderSwitchRehearsalDto? rehearsal, AccountingProviderSwitchCutoverDto? cutover,
        AccountingProviderSwitchMonitoringDto? monitoring)
    {
        if (monitoring?.AllowedActions.CanCreateCorrectiveCutover == true) return "Resolve the blocking discrepancy or prepare a separately controlled corrective cutover.";
        if (monitoring?.AllowedActions.CanRequestClosure == true) return "Request closure approval from the current monitoring evidence.";
        if (monitoring?.AllowedActions.CanClose == true) return "Close monitoring using the approved current evidence.";
        if (monitoring is not null) return monitoring.AllowedActions.Explanation;
        if (cutover?.ProviderReconciliationRequired == true) return "Reconcile the ambiguous provider outcome; do not retry blindly.";
        if (assessment?.HasBlockingGaps == true) return "Resolve the blocking assessment gaps using their recorded operator actions.";
        if (rehearsal is not null && !rehearsal.IsReadyForPlan) return rehearsal.ReadinessExplanation;
        if (!string.IsNullOrWhiteSpace(cutover?.NextAction)) return cutover.NextAction;
        if (allowed.AllowedTransitions.Count > 0) return $"Review whether to move to {Plain(allowed.AllowedTransitions[0])}.";
        return providerSwitch.CompletedUtc.HasValue ? "Keep the migration audit evidence available for review." : allowed.Explanation;
    }

    private static string ExplainWhy(string status) => status.ToLowerInvariant() switch
    {
        "draft" => "The migration intent can still be reviewed without changing accounting authority.",
        "assessing" => "Current source and target evidence is being collected before planning.",
        "blocked" => "A deterministic Finance control has stopped progress until current evidence supports recovery.",
        "activation_awaiting_approval" => "Final reconciliation is complete, but a separate authorized user must approve authority activation.",
        "monitoring" => "The target is authoritative and needs close observation for delayed, duplicate, missing, or inconsistent activity.",
        _ => "This step preserves one authoritative accounting system while Finance checks the evidence required for the next transition."
    };

    private static string MonitoringStatus(AccountingProviderSwitchDto providerSwitch, AccountingProviderSwitchCutoverDto? cutover) =>
        string.Equals(providerSwitch.Status, "monitoring", StringComparison.OrdinalIgnoreCase) ? "Active" :
        providerSwitch.CompletedUtc.HasValue ? "Completed" : cutover?.ProviderReconciliationRequired == true ? "Needs attention" : "Not started";

    private static string MonitoringExplanation(AccountingProviderSwitchDto providerSwitch, AccountingProviderSwitchCutoverDto? cutover) =>
        cutover?.ProviderReconciliationRequired == true
            ? "A provider outcome needs reconciliation before reliable monitoring can continue."
            : string.Equals(providerSwitch.Status, "monitoring", StringComparison.OrdinalIgnoreCase)
                ? "Monitor provider operations, projections, invoices, permissions, bank, tax, and control-account differences."
                : "Monitoring begins only after deterministic activation; Laura cannot start authority activation.";

    private static bool IsSwitchAudit(AuditHistoryListItem item, Guid switchId) =>
        (string.Equals(item.TargetType, AuditTargetTypes.AccountingProviderSwitch, StringComparison.OrdinalIgnoreCase) &&
         string.Equals(item.TargetId, switchId.ToString("D"), StringComparison.OrdinalIgnoreCase)) ||
        item.AffectedEntities.Any(x => string.Equals(x.EntityType, AuditTargetTypes.AccountingProviderSwitch, StringComparison.OrdinalIgnoreCase) &&
                                       string.Equals(x.EntityId, switchId.ToString("D"), StringComparison.OrdinalIgnoreCase));

    private static bool IsAttentionLevel(string value) =>
        value.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("not_authorized", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("failed", StringComparison.OrdinalIgnoreCase);

    private static bool IsPassed(string value) =>
        string.Equals(value, "passed", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "success", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "complete", StringComparison.OrdinalIgnoreCase);

    private static string Plain(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Not available";
        var text = value.Trim().Replace('_', ' ').Replace('-', ' ');
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());
    }

    private static bool IsTerminal(string status) =>
        status.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("cancelled", StringComparison.OrdinalIgnoreCase);

    private static async Task<T?> OptionalAsync<T>(Func<Task<T>> action) where T : class
    {
        try { return await action(); }
        catch (AccountingAuthorityException ex) when (IsNotFound(ex.ReasonCode)) { return null; }
    }

    private static bool IsNotFound(string reasonCode) => reasonCode is
        AccountingProviderSwitchReasonCodes.NotFound or
        AccountingProviderSwitchReasonCodes.AssessmentNotFound or
        AccountingProviderSwitchRehearsalReasonCodes.NotFound or
        AccountingProviderSwitchPreparationReasonCodes.NotFound or
        AccountingProviderSwitchTargetTransferReasonCodes.BatchNotFound or
        AccountingProviderSwitchCutoverReasonCodes.NotFound or
        AccountingProviderSwitchMonitoringReasonCodes.NotFound;

    private static void Validate(Guid companyId, Guid switchId)
    {
        if (companyId == Guid.Empty || switchId == Guid.Empty)
            throw new ArgumentException("Company and accounting-system switch are required.");
    }

    private static void Validate(AccountingProviderSwitchAgentCommandContext context)
    {
        Validate(context.CompanyId, context.SwitchId);
        if (context.ExpectedSwitchVersion <= 0 || context.ActorUserId == Guid.Empty || context.AgentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(context.CorrelationId) || string.IsNullOrWhiteSpace(context.IdempotencyKey))
            throw new ArgumentException("Current version, actor, agent, correlation, and idempotency context are required.");
    }

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
