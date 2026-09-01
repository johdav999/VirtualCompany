using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceOperationalProposalAgentService(
    IAccountingCloseService closes,
    IComplianceObligationService compliance,
    IAuditPackageService auditPackages,
    IAccountingScheduleService schedules,
    ICurrencyRevaluationService revaluation,
    IFixedAssetService fixedAssets,
    IProactiveTaskCreationService tasks,
    IAgentHandoffService handoffs) : IFinanceOperationalProposalAgentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InternalToolExecutionResponse> ExecuteAsync(InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!FinanceOperationalProposalAgentToolIds.Contains(request.ToolName))
            return Failed("unsupported_operational_proposal_tool", "This Finance operational proposal tool is not available.");
        if (request.CompanyId == Guid.Empty || request.AgentId == Guid.Empty || request.ExecutionId == Guid.Empty)
            return Failed("operational_proposal_context_required", "Company, agent, and execution context are required.");
        if (!request.ActorUserId.HasValue || request.ActorUserId == Guid.Empty)
            return Failed("operational_proposal_actor_required", "A current Finance reviewer identity is required.");
        try
        {
            return request.ToolName switch
            {
                FinanceOperationalProposalAgentToolIds.ProposeCloseTaskAssignment => await ProposeCloseAssignmentAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.ProposeEvidenceRequest => await ProposeEvidenceRequestAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.ProposeComplianceChecklist => await ProposeComplianceChecklistAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.PreviewAuditPackage => await PreviewAuditPackageAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.ProposeAccountingSchedule => await ProposeScheduleAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.PreviewCurrencyRevaluation => await PreviewRevaluationAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.ProposeFixedAssetAddition => await PreviewAssetAdditionAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.ProposeFixedAssetDisposal => await PreviewAssetDisposalAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.PreviewFixedAssetDepreciation => await PreviewDepreciationAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.SubmitForApproval => await SubmitAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.AssignCloseTask => await AssignCloseTaskAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.RequestEvidence => await RequestEvidenceAsync(request, cancellationToken),
                FinanceOperationalProposalAgentToolIds.RequestAuditPackageGeneration => await RequestAuditPackageAsync(request, cancellationToken),
                _ => throw new InvalidOperationException("Unreachable operational proposal route.")
            };
        }
        catch (AccountingCloseException ex) { return Failed(ex.ReasonCode, Safe(ex.Message)); }
        catch (ComplianceObligationException ex) { return Failed(ex.Code, Safe(ex.Message)); }
        catch (AuditPackageException ex) { return Failed(ex.ReasonCode, Safe(ex.Message)); }
        catch (AccountingScheduleException ex) { return Failed(ex.ReasonCode, Safe(ex.Message)); }
        catch (CurrencyRevaluationException ex) { return Failed(ex.ReasonCode, Safe(ex.Message)); }
        catch (FixedAssetException ex) { return Failed(ex.ReasonCode, Safe(ex.Message)); }
        catch (TaskValidationException) { return Failed("operational_evidence_task_invalid", "The evidence task was not valid."); }
        catch (ArgumentException ex) { return Failed("operational_proposal_invalid", Safe(ex.Message)); }
        catch (KeyNotFoundException) { return Failed("operational_proposal_target_not_found", "The requested company-scoped target was not found."); }
        catch (UnauthorizedAccessException) { return Failed("operational_proposal_access_denied", "The requested target is not accessible to this Finance actor."); }
    }

    private async Task<InternalToolExecutionResponse> ProposeCloseAssignmentAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Recommend);
        var closeId = RequiredGuid(request, "closeInstanceId");
        var taskId = RequiredGuid(request, "closeTaskId");
        var ownerId = RequiredGuid(request, "ownerUserId");
        var close = await closes.GetAsync(new(request.CompanyId, closeId), ct);
        var task = RequireTask(close, taskId);
        var proposal = CloseAssignmentProposal(close, task, ownerId);
        return ProposalSuccess(request, proposal, "Prepared a version-bound close-task assignment proposal; no task was assigned.");
    }

    private async Task<InternalToolExecutionResponse> ProposeEvidenceRequestAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Recommend);
        var proposal = await BuildEvidenceRequestAsync(request, ct);
        return ProposalSuccess(request, proposal, "Prepared a typed evidence request proposal without marking evidence complete.");
    }

    private async Task<InternalToolExecutionResponse> ProposeComplianceChecklistAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Recommend);
        var obligation = await compliance.GetAsync(request.CompanyId, RequiredGuid(request, "obligationId"), ct);
        var missing = obligation.Requirements.Where(x => !x.IsSatisfied).ToArray();
        var changes = new
        {
            obligation.Id, obligation.Title, obligation.DueDate, obligation.OwnerUserId,
            Requirements = obligation.Requirements,
            MissingRequirements = missing,
            ExistingEvidence = obligation.SubmissionEvidence.Select(x => new { x.Id, x.Reference, x.ContentHash, x.ReviewStatus }).ToArray(),
            MarkedComplete = false, StatutoryConclusion = false
        };
        var proposal = Proposal(FinanceOperationalProposalKinds.ComplianceChecklist, "compliance_obligation",
            obligation.Id, obligation.Version,
            [.. obligation.SubmissionEvidence.Select(x => $"compliance_evidence:{x.Id:D}:{x.ContentHash}"),
             $"policy_pack:{obligation.PolicyPackKey}:{obligation.PolicyPackVersion}:{obligation.PolicyPackDefinitionHash}"],
            changes, missing.Select(x => $"missing:{x.Kind}").ToArray(), ["independent_evidence_review", "statutory_signoff_outside_agent"],
            ["collect_evidence", "human_review", "manual_or_provider_submission_outside_agent"],
            missing.Length == 0 ? ["review_checklist"] : ["request_evidence", "create_handoff"]);
        return ProposalSuccess(request, proposal, "Prepared a compliance evidence checklist without filing, sign-off, or completion.");
    }

    private async Task<InternalToolExecutionResponse> PreviewAuditPackageAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Recommend);
        var preview = await AuditPreviewAsync(request, ct);
        var proposal = AuditProposal(preview);
        return ProposalSuccess(request, proposal, "Previewed the frozen audit-package definition; no artifact or download was created.",
            new() { ["auditPackagePreview"] = Serialize(preview) });
    }

    private async Task<InternalToolExecutionResponse> ProposeScheduleAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Recommend);
        var input = RequiredObject<AccountingScheduleInput>(request, "schedule");
        var idempotencyKey = RequiredText(request, "idempotencyKey", 200);
        AccountingScheduleDto schedule;
        if (OptionalGuid(request, "scheduleId") is { } scheduleId)
        {
            schedule = await schedules.UpdateAsync(new(request.CompanyId, scheduleId,
                RequiredLong(request, "expectedVersion"), input, idempotencyKey,
                request.ActorUserId!.Value, Correlation(request)), ct);
        }
        else
        {
            schedule = await schedules.CreateAsync(new(request.CompanyId, input, idempotencyKey,
                request.ActorUserId!.Value, Correlation(request)), ct);
        }
        var preview = await schedules.PreviewAsync(new(request.CompanyId, schedule.Id, schedule.Version,
            request.ActorUserId!.Value), ct);
        var proposal = ScheduleProposal(preview);
        return ProposalSuccess(request, proposal, "Created and deterministically validated an unposted schedule proposal.",
            new() { ["schedulePreview"] = Serialize(preview) });
    }

    private async Task<InternalToolExecutionResponse> PreviewRevaluationAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Recommend);
        var run = await revaluation.PreviewAsync(new(request.CompanyId,
            RequiredGuid(request, "fiscalPeriodId"), RequiredText(request, "voucherSeriesCode", 30),
            RequiredText(request, "idempotencyKey", 200), request.ActorUserId!.Value,
            Correlation(request)), ct);
        var proposal = RevaluationProposal(run);
        return ProposalSuccess(request, proposal, "Calculated and retained an unposted currency-revaluation preview.",
            new() { ["revaluationPreview"] = Serialize(run) });
    }

    private async Task<InternalToolExecutionResponse> PreviewAssetAdditionAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Recommend);
        var preview = await fixedAssets.PreviewRegistrationAsync(new(request.CompanyId,
            RequiredObject<RegisterFixedAssetInput>(request, "asset"), request.ActorUserId!.Value), ct);
        var blockers = preview.IsRegistered ? ["asset_source_already_registered"] : Array.Empty<string>();
        var proposal = Proposal(FinanceOperationalProposalKinds.FixedAssetAddition, "fixed_asset_class",
            preview.AssetClass.Id, preview.AssetClass.Version,
            SourceEvidence(preview.Asset.SourceType, preview.Asset.SourceId, preview.Asset.SourceVersion,
                preview.Asset.SourceDocumentId), preview, blockers,
            preview.RequiresApproval ? ["fixed_asset_class_approval", "independent_reviewer"] : ["independent_reviewer"],
            preview.IsRegistered
                ? ["no_change_existing_registered_asset"]
                : ["register_asset_after_guarded_human_action", "capitalization_and_posting_remain_separate"],
            blockers.Length == 0 ? ["review_asset_addition"] : ["review_existing_asset"]);
        return ProposalSuccess(request, proposal, "Validated a fixed-asset addition proposal without registering or posting the asset.",
            new() { ["assetAdditionPreview"] = Serialize(preview) });
    }

    private async Task<InternalToolExecutionResponse> PreviewAssetDisposalAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Recommend);
        var preview = await fixedAssets.PreviewDisposalAsync(new(request.CompanyId,
            RequiredGuid(request, "assetId"), RequiredDate(request, "disposalDate"),
            RequiredGuid(request, "fiscalPeriodId"), RequiredGuid(request, "proceedsAccountId"),
            RequiredDecimal(request, "proceeds"), RequiredLong(request, "expectedVersion"),
            RequiredText(request, "sourceVersion", 200), request.ActorUserId!.Value), ct);
        var blockers = preview.PostingPreview.Issues.Select(x => x.ReasonCode).Distinct().ToArray();
        var proposal = Proposal(FinanceOperationalProposalKinds.FixedAssetDisposal, "fixed_asset",
            preview.Asset.Id, preview.Asset.Version,
            [$"fixed_asset:{preview.Asset.Id:D}:{preview.Asset.Version}", $"source:{RequiredText(request, "sourceVersion", 200)}"],
            preview, blockers, ["independent_reviewer", "posting_authority_outside_agent"],
            ["governed_disposal_posting_after_human_action", "asset_register_update_after_posting"],
            blockers.Length == 0 ? ["review_disposal"] : ["resolve_posting_issues"]);
        return ProposalSuccess(request, proposal, "Calculated a fixed-asset disposal preview through posting validation without posting it.",
            new() { ["assetDisposalPreview"] = Serialize(preview) });
    }

    private async Task<InternalToolExecutionResponse> PreviewDepreciationAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Recommend);
        var fiscalPeriodId = RequiredGuid(request, "fiscalPeriodId");
        var preview = await fixedAssets.PreviewDepreciationAsync(new(request.CompanyId,
            RequiredDate(request, "periodStart"), RequiredDate(request, "periodEnd")), ct);
        var blockers = preview.Items.Where(x => x.Status == "failed").Select(x => x.FailureCode ?? "asset_validation_failed").Distinct().ToArray();
        var proposal = Proposal(FinanceOperationalProposalKinds.FixedAssetDepreciation, "fiscal_period",
            fiscalPeriodId, preview.Items.Select(x => x.AssetVersion).DefaultIfEmpty(0).Max(),
            preview.Items.Select(x => $"fixed_asset:{x.AssetId:D}:{x.AssetVersion}").ToArray(), preview,
            blockers, ["independent_reviewer", "posting_authority_outside_agent"],
            ["depreciation_run_and_posting_remain_outside_direct_agent_execution"],
            blockers.Length == 0 ? ["review_depreciation"] : ["resolve_asset_exceptions"]);
        return ProposalSuccess(request, proposal, "Calculated a deterministic fixed-asset depreciation preview without starting a posting run.",
            new() { ["assetDepreciationPreview"] = Serialize(preview) });
    }

    private async Task<InternalToolExecutionResponse> SubmitAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Execute); RequireReviewed(request);
        var kind = RequiredText(request, "proposalKind", 80).ToLowerInvariant();
        var targetId = RequiredGuid(request, "targetId");
        var expectedVersion = RequiredLong(request, "expectedVersion");
        var expectedHash = RequiredHash(request);
        var key = RequiredText(request, "idempotencyKey", 200);
        if (kind == FinanceOperationalProposalKinds.AccountingSchedule)
        {
            var current = await schedules.GetAsync(new(request.CompanyId, targetId), ct);
            if (current.Version != expectedVersion) return Stale();
            var preview = await schedules.PreviewAsync(new(request.CompanyId, targetId, expectedVersion,
                request.ActorUserId!.Value), ct);
            if (!HashMatches(ScheduleProposal(preview), expectedHash)) return Stale();
            if (preview.Issues.Count > 0) return Failed("operational_proposal_blocked", "The schedule proposal still has deterministic validation blockers.",
                new() { ["validation"] = Serialize(preview) });
            var submitted = await schedules.SubmitAsync(new(request.CompanyId, targetId, expectedVersion,
                key, request.ActorUserId!.Value, Correlation(request)), ct);
            return ExecuteSuccess(request, "Submitted the current schedule proposal for independent approval; no occurrence was generated or posted.",
                new() { ["proposalSubmission"] = Serialize(submitted) }, "awaiting_approval");
        }
        if (kind == FinanceOperationalProposalKinds.CurrencyRevaluation)
        {
            var current = await revaluation.GetAsync(new(request.CompanyId, targetId), ct);
            if (current.Version != expectedVersion || !HashMatches(RevaluationProposal(current), expectedHash)) return Stale();
            if (!string.Equals(current.Status, "draft", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(current.Status, "reviewed", StringComparison.OrdinalIgnoreCase))
                return Failed("operational_proposal_blocked", "The revaluation preview is not eligible for approval submission.");
            var submitted = await revaluation.SubmitAsync(new(request.CompanyId, targetId,
                expectedVersion, request.ActorUserId!.Value, Correlation(request)), ct);
            return ExecuteSuccess(request, "Submitted the current revaluation proposal for independent approval; no journal was posted.",
                new() { ["proposalSubmission"] = Serialize(submitted) }, "awaiting_approval");
        }
        return Failed("operational_proposal_submission_unsupported",
            "Only current schedule and currency-revaluation proposals can enter an owning approval workflow through this tool.");
    }

    private async Task<InternalToolExecutionResponse> AssignCloseTaskAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Execute); RequireReviewed(request);
        var closeId = RequiredGuid(request, "closeInstanceId"); var taskId = RequiredGuid(request, "closeTaskId");
        var ownerId = RequiredGuid(request, "ownerUserId"); var expectedVersion = RequiredLong(request, "expectedVersion");
        var close = await closes.GetAsync(new(request.CompanyId, closeId), ct); var task = RequireTask(close, taskId);
        if (task.Version != expectedVersion || !HashMatches(CloseAssignmentProposal(close, task, ownerId), RequiredHash(request))) return Stale();
        var assigned = await closes.AssignTaskAsync(new(request.CompanyId, closeId, taskId, expectedVersion,
            ownerId, RequiredText(request, "idempotencyKey", 200), request.ActorUserId!.Value,
            Correlation(request)), ct);
        return ExecuteSuccess(request, "Assigned the eligible close task through the owning close service; sign-off remains separate.",
            new() { ["closeAssignment"] = Serialize(assigned) }, "assigned");
    }

    private async Task<InternalToolExecutionResponse> RequestEvidenceAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Execute); RequireReviewed(request);
        var current = await BuildEvidenceRequestAsync(request, ct);
        if (!HashMatches(current, RequiredHash(request))) return Stale();
        var title = RequiredText(request, "title", 200); var description = RequiredText(request, "description", 2000);
        var targetId = RequiredGuid(request, "targetId"); var scopeType = RequiredText(request, "scopeType", 60).ToLowerInvariant();
        var assignedAgentId = OptionalGuid(request, "assignedAgentId");
        var result = await tasks.CreateAsync(new(new(request.CompanyId, request.AgentId,
            "finance_operational_evidence_request", $"{scopeType}:{targetId:D}:{current.ProposalHash}",
            Correlation(request), "Current Finance evidence is owned by another responsible role.",
            new()
            {
                ["proposalHash"] = JsonValue.Create(current.ProposalHash), ["scopeType"] = JsonValue.Create(scopeType),
                ["targetId"] = JsonValue.Create(targetId), ["targetVersion"] = JsonValue.Create(current.TargetVersion),
                ["authorityNotice"] = JsonValue.Create(FinanceOperationalProposalAgentContract.AuthorityNotice)
            }, TaskType: "finance_evidence_request", TaskTitle: title, TaskDescription: description,
            TaskPriority: OptionalText(request, "priority", 20) ?? "high", DueAt: OptionalDateTime(request, "dueAt"),
            AssignedAgentId: assignedAgentId)), ct);
        AgentHandoffDto? handoff = null;
        if (OptionalGuid(request, "receivingAgentId") is { } receivingAgentId)
            handoff = await handoffs.CreateAsync(request.CompanyId, request.AgentId,
                new(AgentHandoffTypes.InternalRequest, receivingAgentId, title,
                    "Provide the requested evidence or explicitly report the blocker; do not mark statutory completion.",
                    OptionalDateTime(request, "dueAt"), [$"{scopeType}:{targetId:D}", $"proposal:{current.ProposalHash}"]), ct);
        return ExecuteSuccess(request, "Created an authorized typed evidence task and optional agent handoff without completing evidence.",
            new() { ["evidenceTask"] = Serialize(result), ["handoff"] = Serialize(handoff) }, "evidence_requested");
    }

    private async Task<InternalToolExecutionResponse> RequestAuditPackageAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        RequireAction(request, ToolActionType.Execute); RequireReviewed(request);
        var preview = await AuditPreviewAsync(request, ct); var current = AuditProposal(preview);
        if (!HashMatches(current, RequiredHash(request))) return Stale();
        if (!preview.IsEligible) return Failed("audit_package_preview_blocked", "The current package definition is not eligible for generation.",
            new() { ["auditPackagePreview"] = Serialize(preview) });
        var package = await auditPackages.RequestAsync(new(request.CompanyId, preview.FiscalPeriodId,
            request.ActorUserId!.Value, "accounting_admin", RequiredText(request, "idempotencyKey", 200),
            preview.ScopeKey, preview.ScopeVersion), ct);
        return ExecuteSuccess(request,
            "Requested the current frozen audit-package definition. Generation remains approval-gated and background-owned; no download was authorized.",
            new() { ["auditPackageRequest"] = Serialize(new { Package = package, Downloadable = false, ArtifactReturned = false }) },
            package.Status);
    }

    private async Task<FinanceOperationalProposalDto> BuildEvidenceRequestAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var scopeType = RequiredText(request, "scopeType", 60).ToLowerInvariant();
        var targetId = RequiredGuid(request, "targetId");
        var title = RequiredText(request, "title", 200); var description = RequiredText(request, "description", 2000);
        var proposed = new { title, description, DueAt = OptionalDateTime(request, "dueAt"),
            AssignedAgentId = OptionalGuid(request, "assignedAgentId"), ReceivingAgentId = OptionalGuid(request, "receivingAgentId") };
        if (scopeType == "close_task")
        {
            var close = await closes.GetAsync(new(request.CompanyId, RequiredGuid(request, "closeInstanceId")), ct);
            var task = RequireTask(close, targetId);
            var sources = task.Evidence.Select(x => $"close_evidence:{x.Id:D}:{x.ContentHash}")
                .Append($"close_task:{task.Id:D}:{task.Version}").ToArray();
            var blockers = task.Blockers.Where(x => x.Status == "open").Select(x => x.ReasonCode)
                .Concat(task.BlockingReasonCodes).Distinct().ToArray();
            return Proposal(FinanceOperationalProposalKinds.EvidenceRequest, "accounting_close_task", task.Id,
                task.Version, sources, proposed, blockers, ["responsible_owner_review"],
                ["typed_task", "optional_agent_handoff", "evidence_completion_remains_with_owner"],
                ["request_evidence"]);
        }
        if (scopeType == "compliance_obligation")
        {
            var obligation = await compliance.GetAsync(request.CompanyId, targetId, ct);
            return Proposal(FinanceOperationalProposalKinds.EvidenceRequest, "compliance_obligation", obligation.Id,
                obligation.Version,
                obligation.SubmissionEvidence.Select(x => $"compliance_evidence:{x.Id:D}:{x.ContentHash}").ToArray(),
                proposed, obligation.Requirements.Where(x => !x.IsSatisfied).Select(x => $"missing:{x.Kind}").ToArray(),
                ["responsible_owner_review", "statutory_signoff_outside_agent"],
                ["typed_task", "optional_agent_handoff", "no_filing_or_completion"], ["request_evidence"]);
        }
        throw new ArgumentException("scopeType must be close_task or compliance_obligation.");
    }

    private async Task<AuditPackagePreviewDto> AuditPreviewAsync(InternalToolExecutionRequest request, CancellationToken ct) =>
        await auditPackages.PreviewAsync(new(request.CompanyId, RequiredGuid(request, "fiscalPeriodId"),
            OptionalText(request, "scopeKey", 100) ?? AuditPackageScopeValues.PeriodClose,
            OptionalText(request, "scopeVersion", 64) ?? AuditPackageScopeValues.CurrentVersion), ct);

    private static FinanceOperationalProposalDto AuditProposal(AuditPackagePreviewDto preview) =>
        Proposal(FinanceOperationalProposalKinds.AuditPackage, "fiscal_period", preview.FiscalPeriodId,
            preview.ExistingPackageVersion ?? 0,
            [$"snapshot:{Hash(preview.SnapshotVersionsJson)}", $"scope:{preview.ScopeHash}"], preview,
            preview.Blockers, ["independent_audit_package_approval"],
            ["approval_gated_background_generation", "protected_object_storage", "checksum_verification", "retention_policy", "failure_visible_and_retryable"],
            preview.IsEligible ? ["request_generation"] : ["resolve_blockers"]);

    private static FinanceOperationalProposalDto ScheduleProposal(AccountingSchedulePreviewDto preview) =>
        Proposal(FinanceOperationalProposalKinds.AccountingSchedule, "accounting_schedule", preview.Schedule.Id,
            preview.Schedule.Version,
            [$"schedule_version:{preview.Schedule.CurrentVersionNumber}:{preview.Schedule.CurrentVersionHash}"], preview,
            preview.Issues.Select(x => x.ReasonCode).Distinct().ToArray(), ["finance_approver"],
            ["approval_then_activation_by_owner", "future_occurrence_generation", "posting_remains_separate"],
            preview.Issues.Count == 0 ? ["submit_for_approval"] : ["edit_schedule"]);

    private static FinanceOperationalProposalDto RevaluationProposal(CurrencyRevaluationRunDto run)
    {
        var blockers = run.Population.Where(x => x.Status == "needs_review").Select(x => x.ReviewReason ?? "revaluation_review_required")
            .Concat(string.IsNullOrWhiteSpace(run.FailureReasonCode) ? [] : [run.FailureReasonCode]).Distinct().ToArray();
        return Proposal(FinanceOperationalProposalKinds.CurrencyRevaluation, "currency_revaluation_run", run.Id,
            run.Version,
            [.. run.Population.Select(x => $"source:{x.PopulationKey}:{x.SourceChecksum}"),
             .. run.RateBindings.Select(x => $"rate:{x.ExchangeRateConversionId:D}:{x.EvidenceChecksum}"),
             $"population:{run.PopulationChecksum}", $"proposal:{run.ProposalChecksum}"], run,
            blockers, ["finance_approver", "posting_authority_outside_agent"],
            ["approval_review", "posting_and_reversal_remain_separate"],
            blockers.Length == 0 ? ["submit_for_approval"] : ["review_population"]);
    }

    private static FinanceOperationalProposalDto CloseAssignmentProposal(AccountingCloseDto close,
        AccountingCloseTaskDto task, Guid ownerId)
    {
        var blockers = task.AllowedActions.Contains("assign", StringComparer.OrdinalIgnoreCase)
            ? task.BlockingReasonCodes : task.BlockingReasonCodes.Append("assignment_not_allowed").Distinct().ToArray();
        return Proposal(FinanceOperationalProposalKinds.CloseTaskAssignment, "accounting_close_task",
            task.Id, task.Version,
            [$"close:{close.Id:D}:{close.Version}", $"close_task:{task.Id:D}:{task.Version}"],
            new { CloseInstanceId = close.Id, CloseTaskId = task.Id, OwnerUserId = ownerId,
                PreviousOwnerUserId = task.OwnerUserId, task.OwnerRole, task.DueUtc, task.RequiresSignOff,
                task.SignOffRole, task.MaterialityAmount }, blockers,
            task.RequiresSignOff ? ["independent_close_task_signoff"] : ["responsible_owner_review"],
            ["assign_current_task", "work_task_moves_to_in_progress", "signoff_remains_separate"],
            blockers.Contains("assignment_not_allowed") ? ["refresh_close"] : ["assign_close_task"]);
    }

    private static FinanceOperationalProposalDto Proposal(string kind, string targetType, Guid targetId,
        long targetVersion, IReadOnlyList<string> sources, object changes, IReadOnlyList<string> blockers,
        IReadOnlyList<string> approvals, IReadOnlyList<string> effects, IReadOnlyList<string> actions)
    {
        var normalizedSources = sources.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order().ToArray();
        var normalizedBlockers = blockers.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Order().ToArray();
        var hash = Hash(JsonSerializer.Serialize(new { kind, targetType, targetId, targetVersion,
            Sources = normalizedSources, Changes = changes, Blockers = normalizedBlockers,
            Approvals = approvals, Effects = effects }, JsonOptions));
        return new(kind, hash, targetType, targetId, targetVersion, normalizedSources, changes,
            normalizedBlockers, approvals, effects, actions, Posted: false, EvidenceCompleted: false,
            FinanceOperationalProposalAgentContract.AuthorityNotice);
    }

    private static AccountingCloseTaskDto RequireTask(AccountingCloseDto close, Guid taskId) =>
        close.Tasks.SingleOrDefault(x => x.Id == taskId)
        ?? throw new AccountingCloseException(AccountingCloseReasonCodes.NotFound, "The accounting close task was not found.");

    private static IReadOnlyList<string> SourceEvidence(string type, string id, string version, Guid? documentId) =>
        documentId.HasValue ? [$"source:{type}:{id}:{version}", $"document:{documentId:D}"] : [$"source:{type}:{id}:{version}"];

    private static InternalToolExecutionResponse ProposalSuccess(InternalToolExecutionRequest request,
        FinanceOperationalProposalDto proposal, string summary, Dictionary<string, JsonNode?>? extra = null)
    {
        var data = extra ?? new(); data["operationalProposal"] = Serialize(proposal);
        return Success(request, summary, data, proposal.Blockers.Count == 0 ? "proposal_ready_for_review" : "review_required");
    }

    private static InternalToolExecutionResponse ExecuteSuccess(InternalToolExecutionRequest request,
        string summary, Dictionary<string, JsonNode?> data, string state)
    {
        data["proposalExecution"] = Serialize(new
        {
            State = state, Posted = false, EvidenceCompleted = false,
            StatutoryConclusion = false, DownloadAuthorized = false
        });
        return Success(request, summary, data, state);
    }
    private static InternalToolExecutionResponse Success(InternalToolExecutionRequest request, string summary,
        Dictionary<string, JsonNode?> data, string state) => InternalToolExecutionResponse.Succeeded(summary, data,
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["contractVersion"] = JsonValue.Create(FinanceOperationalProposalAgentContract.Version),
            ["companyId"] = JsonValue.Create(request.CompanyId), ["agentId"] = JsonValue.Create(request.AgentId),
            ["executionId"] = JsonValue.Create(request.ExecutionId), ["correlationId"] = JsonValue.Create(Correlation(request)),
            ["state"] = JsonValue.Create(state), ["posted"] = JsonValue.Create(false),
            ["evidenceCompleted"] = JsonValue.Create(false), ["statutoryConclusion"] = JsonValue.Create(false),
            ["downloadAuthorized"] = JsonValue.Create(false),
            ["authorityNotice"] = JsonValue.Create(FinanceOperationalProposalAgentContract.AuthorityNotice)
        });

    private static InternalToolExecutionResponse Stale() => Failed("operational_proposal_stale",
        "The proposal target or retained calculation changed after review. Refresh and review the current proposal.");
    private static bool HashMatches(FinanceOperationalProposalDto proposal, string hash) =>
        string.Equals(proposal.ProposalHash, hash, StringComparison.OrdinalIgnoreCase);
    private static string RequiredHash(InternalToolExecutionRequest request) => RequiredText(request, "expectedProposalHash", 64);
    private static void RequireReviewed(InternalToolExecutionRequest request)
    { if (!RequiredBool(request, "reviewed")) throw new ArgumentException("A reviewer must explicitly confirm the current proposal."); }
    private static void RequireAction(InternalToolExecutionRequest request, ToolActionType expected)
    { if (request.Context.ActionType != expected) throw new ArgumentException($"{request.ToolName} requires the {expected.ToString().ToLowerInvariant()} action class."); }
    private static InternalToolExecutionResponse Failed(string code, string summary, Dictionary<string, JsonNode?>? data = null) =>
        InternalToolExecutionResponse.Failed("failed", code, summary, data);
    private static JsonNode? Serialize<T>(T value) => JsonSerializer.SerializeToNode(value, JsonOptions);
    private static string Correlation(InternalToolExecutionRequest request) => request.CorrelationId ?? request.ExecutionId.ToString("N");
    private static string Safe(string value) => value.Length <= 1000 ? value : value[..1000];
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static T RequiredObject<T>(InternalToolExecutionRequest request, string name) =>
        request.Payload.TryGetValue(name, out var node) && node is not null
            ? node.Deserialize<T>(JsonOptions) ?? throw new ArgumentException($"{name} is required.")
            : throw new ArgumentException($"{name} is required.");
    private static string RequiredText(InternalToolExecutionRequest request, string name, int max) =>
        OptionalText(request, name, max) ?? throw new ArgumentException($"{name} is required and must be {max} characters or fewer.");
    private static string? OptionalText(InternalToolExecutionRequest request, string name, int max) =>
        request.Payload.TryGetValue(name, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text) && text.Trim().Length <= max ? text.Trim() : null;
    private static Guid RequiredGuid(InternalToolExecutionRequest request, string name) =>
        OptionalGuid(request, name) ?? throw new ArgumentException($"{name} is required.");
    private static Guid? OptionalGuid(InternalToolExecutionRequest request, string name)
    {
        if (request.Payload.TryGetValue(name, out var node) && node is JsonValue value &&
            ((value.TryGetValue<Guid>(out var id) && id != Guid.Empty) ||
             (value.TryGetValue<string>(out var text) && Guid.TryParse(text, out id) && id != Guid.Empty))) return id;
        return null;
    }
    private static long RequiredLong(InternalToolExecutionRequest request, string name)
    {
        if (request.Payload.TryGetValue(name, out var node) && node is JsonValue value &&
            ((value.TryGetValue<long>(out var number) && number >= 0) ||
             (value.TryGetValue<string>(out var text) && long.TryParse(text, out number) && number >= 0))) return number;
        throw new ArgumentException($"{name} is required.");
    }
    private static decimal RequiredDecimal(InternalToolExecutionRequest request, string name)
    {
        if (request.Payload.TryGetValue(name, out var node) && node is JsonValue value && value.TryGetValue<decimal>(out var number)) return number;
        throw new ArgumentException($"{name} is required.");
    }
    private static DateOnly RequiredDate(InternalToolExecutionRequest request, string name)
    {
        var text = RequiredText(request, name, 20);
        return DateOnly.TryParse(text, out var value) ? value : throw new ArgumentException($"{name} must be a valid date.");
    }
    private static DateTime? OptionalDateTime(InternalToolExecutionRequest request, string name) =>
        OptionalText(request, name, 40) is { } text && DateTime.TryParse(text, out var value) ? value.ToUniversalTime() : null;
    private static bool RequiredBool(InternalToolExecutionRequest request, string name) =>
        request.Payload.TryGetValue(name, out var node) && node is JsonValue value && value.TryGetValue<bool>(out var result) && result;
}
