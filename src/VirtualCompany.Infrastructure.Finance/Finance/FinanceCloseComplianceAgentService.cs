using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceCloseComplianceAgentService(
    IAccountingCloseService closes,
    IAccountingCloseWorkspaceService closeWorkspace,
    IAccountingCloseGovernanceService closeGovernance,
    IAccountingReportingService reporting,
    IComplianceObligationService compliance,
    IAuditPackageService auditPackages,
    IAccountantCollaborationService accountantCollaboration,
    IYearEndRolloverService yearEnd) : IFinanceCloseComplianceAgentService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var toolName = request.ToolName.Trim().ToLowerInvariant();
        if (!FinanceCloseComplianceAgentToolIds.Contains(toolName))
            return Reject("unsupported_close_compliance_tool", "This close and compliance tool is not supported.");

        var expectedAction = FinanceCloseComplianceAgentToolIds.ActionFor(toolName);
        if (request.ActionKind != expectedAction)
            return Reject("close_compliance_action_mismatch", $"{toolName} requires the {expectedAction.ToString().ToLowerInvariant()} action class.");

        try
        {
            return toolName switch
            {
                FinanceCloseComplianceAgentToolIds.ReadTemplates => await ReadTemplatesAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadInstance => await ReadInstanceAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadReadiness => await ReadReadinessAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadPeriodLockHistory => await ReadPeriodHistoryAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadComplianceObligations => await ReadComplianceAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadAuditPackages => await ReadAuditPackagesAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadAccountantAccessActivity => await ReadAccountantActivityAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ReadYearEnd => await ReadYearEndAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.PrioritizeCloseBlockers => await RecommendCloseAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ExplainCompliancePreparation => await RecommendComplianceAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ExplainAuditPackageCompleteness => await RecommendAuditAsync(request, cancellationToken),
                FinanceCloseComplianceAgentToolIds.ExplainYearEndPrerequisites => await RecommendYearEndAsync(request, cancellationToken),
                _ => throw new InvalidOperationException("Unreachable close/compliance tool route.")
            };
        }
        catch (AccountingCloseException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (AccountingCloseGovernanceException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (ComplianceObligationException ex) { return Reject(ex.Code, Safe(ex.Message)); }
        catch (AuditPackageException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (AccountantCollaborationException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (YearEndRolloverException ex) { return Reject(ex.ReasonCode, Safe(ex.Message)); }
        catch (KeyNotFoundException) { return Reject("finance_close_object_not_found", "The requested object was not found in this company or is not accessible to this caller."); }
        catch (UnauthorizedAccessException) { return Reject("finance_close_object_access_denied", "The requested object is not accessible to this caller."); }
        catch (ArgumentException ex) { return Reject("finance_close_request_invalid", Safe(ex.Message)); }
    }

    private async Task<InternalToolExecutionResponse> ReadTemplatesAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "templateId", out var templateId))
        {
            var template = await closes.GetTemplateAsync(new(request.CompanyId, templateId), ct);
            return Success("closeTemplates", template, [Source("close_template", template.Id)], template.AllowedActions);
        }
        var skip = Integer(request, "skip", 0, 0, 100_000);
        var take = Integer(request, "take", FinanceCloseComplianceAgentContract.MaximumPageSize, 1,
            FinanceCloseComplianceAgentContract.MaximumPageSize);
        var result = await closes.ListTemplatesAsync(new(request.CompanyId, Text(request, "status", 30),
            skip, take), ct);
        return Success("closeTemplates", result, result.Items.Select(x => Source("close_template", x.Id)),
            ["read_close_template", "read_close_instance"], truncated: result.Skip + result.Items.Count < result.TotalCount,
            page: new(result.Skip, result.Take, result.TotalCount));
    }

    private async Task<InternalToolExecutionResponse> ReadInstanceAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var closeId = RequiredGuid(request, "closeInstanceId");
        var close = await closes.GetAsync(new(request.CompanyId, closeId), ct);
        return Success("closeInstance", close, CloseSources(close), close.AllowedActions);
    }

    private async Task<InternalToolExecutionResponse> ReadReadinessAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var closeId = OptionalGuid(request, "closeInstanceId");
        var periodId = OptionalGuid(request, "fiscalPeriodId");
        RequireCloseSelector(closeId, periodId);
        var workspace = await closeWorkspace.GetAsync(new(request.CompanyId, periodId, closeId), ct);
        closeId ??= workspace.CloseInstanceId;
        if (!closeId.HasValue)
            return Reject("accounting_close_not_found", "No close instance exists for the selected fiscal period in this company.");
        var governance = await closeGovernance.GetAsync(new(request.CompanyId, closeId.Value), ct);
        var view = new
        {
            workspace.SelectedPeriod,
            workspace.CloseInstanceId,
            workspace.CloseName,
            workspace.CloseStatus,
            workspace.CloseVersion,
            workspace.Readiness,
            workspace.Tasks,
            workspace.Panels,
            workspace.SignOffs,
            Governance = governance,
            workspace.AllowedActions,
            workspace.EvidenceNotice,
            Authority = Authority("technical_readiness")
        };
        var hash = governance.CurrentSnapshot?.EvidenceHash ?? workspace.Readiness?.EvidenceHash;
        return Success("closeReadiness", view, CloseReadinessSources(workspace, governance), workspace.AllowedActions,
            new() { ["readinessHash"] = JsonValue.Create(hash), ["readinessIsStale"] = JsonValue.Create(workspace.Readiness?.IsStale) },
            freshness: workspace.Readiness?.IsStale == true ? "authoritative_stale" : "authoritative_live");
    }

    private async Task<InternalToolExecutionResponse> ReadPeriodHistoryAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var periodId = RequiredGuid(request, "fiscalPeriodId");
        var history = await reporting.GetPeriodHistoryAsync(request.CompanyId, periodId, ct);
        return Success("periodLockHistory", new { FiscalPeriodId = periodId, History = history, Authority = Authority("period_lock_history") },
            [Source("fiscal_period", periodId)], ["read_close_readiness"], freshness: "authoritative_history");
    }

    private async Task<InternalToolExecutionResponse> ReadComplianceAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "obligationId", out var obligationId))
        {
            var obligation = await compliance.GetAsync(request.CompanyId, obligationId, ct);
            return ComplianceSuccess(obligation);
        }
        var from = Date(request, "from") ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1));
        var to = Date(request, "to") ?? DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(6));
        if (to < from) throw new ArgumentException("to must be on or after from.");
        if (to.DayNumber - from.DayNumber > FinanceCloseComplianceAgentContract.MaximumCalendarRangeDays)
            throw new ArgumentException($"Compliance calendar ranges cannot exceed {FinanceCloseComplianceAgentContract.MaximumCalendarRangeDays} days.");
        var skip = Integer(request, "skip", 0, 0, 100_000);
        var take = Integer(request, "take", FinanceCloseComplianceAgentContract.MaximumPageSize, 1,
            FinanceCloseComplianceAgentContract.MaximumPageSize);
        var calendar = await compliance.GetCalendarAsync(new(request.CompanyId, from, to), ct);
        var obligations = calendar.Obligations.Skip(skip).Take(take).ToArray();
        var boundedCalendar = calendar with { Obligations = obligations };
        var states = obligations.Select(ComplianceAuthorityState).ToArray();
        return Success("complianceObligations", new { Calendar = boundedCalendar, AuthorityStates = states, Authority = Authority("compliance_preparation") },
            obligations.SelectMany(ComplianceSources), ["prepare_evidence", "request_human_submission", "record_provider_acknowledgement"],
            truncated: skip + obligations.Length < calendar.Obligations.Count,
            page: new(skip, take, calendar.Obligations.Count));
    }

    private InternalToolExecutionResponse ComplianceSuccess(ComplianceObligationDto obligation) =>
        Success("complianceObligations", new { Obligation = obligation, AuthorityState = ComplianceAuthorityState(obligation), Authority = Authority("compliance_preparation") },
            ComplianceSources(obligation), obligation.AllowedActions);

    private async Task<InternalToolExecutionResponse> ReadAuditPackagesAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "packageId", out var packageId))
        {
            var package = await auditPackages.GetAsync(request.CompanyId, packageId, ct);
            return AuditSuccess(package);
        }
        var skip = Integer(request, "skip", 0, 0, 100_000);
        var take = Integer(request, "take", FinanceCloseComplianceAgentContract.MaximumPageSize, 1,
            FinanceCloseComplianceAgentContract.MaximumPageSize);
        var workspace = await auditPackages.ListAsync(new(request.CompanyId, OptionalGuid(request, "fiscalPeriodId"), skip, take), ct);
        return Success("auditPackages", new
        {
            Workspace = workspace,
            Download = new { ContentReturned = false, AuthorizationCreated = false, OneTimeAuthorizationRequired = true },
            Authority = Authority("audit_package_technical_verification")
        }, workspace.Packages.SelectMany(AuditSources), ["request_owning_authorization", "human_approval"],
            truncated: skip + workspace.Packages.Count < workspace.TotalCount,
            page: new(skip, take, workspace.TotalCount));
    }

    private InternalToolExecutionResponse AuditSuccess(AuditPackageDto package) =>
        Success("auditPackages", new
        {
            Package = package,
            Download = new
            {
                ContentReturned = false,
                AuthorizationCreated = false,
                OneTimeAuthorizationRequired = true,
                AuthorizationEligible = package.IsFinal && package.RetainUntilUtc > DateTime.UtcNow,
                package.RetainUntilUtc
            },
            Authority = Authority("audit_package_technical_verification")
        }, AuditSources(package), ["request_owning_authorization", "human_approval"],
            freshness: package.IsFinal ? "immutable_final_package" : "authoritative_live");

    private async Task<InternalToolExecutionResponse> ReadAccountantActivityAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var grants = await accountantCollaboration.ListGrantsAsync(request.CompanyId, ct);
        var engagements = await accountantCollaboration.ListEngagementsAsync(request.CompanyId, ct);
        var grantId = OptionalGuid(request, "grantId");
        var engagementId = OptionalGuid(request, "engagementId");
        var matchingEngagements = engagementId.HasValue
            ? engagements.Where(x => x.Id == engagementId).ToArray()
            : grantId.HasValue
                ? engagements.Where(x => x.GrantId == grantId).ToArray()
                : engagements.ToArray();
        var relevantGrantIds = matchingEngagements.Select(x => x.GrantId).ToHashSet();
        var matchingGrants = grantId.HasValue
            ? grants.Where(x => x.Id == grantId).ToArray()
            : engagementId.HasValue
                ? grants.Where(x => relevantGrantIds.Contains(x.Id)).ToArray()
                : grants.ToArray();
        if (grantId.HasValue && matchingGrants.Length == 0 || engagementId.HasValue && matchingEngagements.Length == 0 ||
            grantId.HasValue && engagementId.HasValue && matchingEngagements.Any(x => x.GrantId != grantId.Value))
            return Reject("accountant_grant_object_not_found", "The requested grant or engagement was not found in this company-scoped access set.");
        var skip = Integer(request, "skip", 0, 0, 100_000);
        var take = Integer(request, "take", FinanceCloseComplianceAgentContract.MaximumPageSize, 1,
            FinanceCloseComplianceAgentContract.MaximumPageSize);
        var filteredGrants = grantId.HasValue || engagementId.HasValue
            ? matchingGrants
            : matchingGrants.Skip(skip).Take(take).ToArray();
        var filteredEngagements = grantId.HasValue || engagementId.HasValue
            ? matchingEngagements
            : matchingEngagements.Skip(skip).Take(take).ToArray();
        var safeEngagements = filteredEngagements.Select(SanitizeAccountantEngagement).ToArray();
        var truncated = !grantId.HasValue && !engagementId.HasValue &&
                        (skip + filteredGrants.Length < matchingGrants.Length ||
                         skip + filteredEngagements.Length < matchingEngagements.Length);
        return Success("accountantAccessActivity", new
        {
            Grants = filteredGrants,
            Engagements = safeEngagements,
            GrantTotalCount = matchingGrants.Length,
            EngagementTotalCount = matchingEngagements.Length,
            Authority = Authority("accountant_collaboration")
        }, AccountantSources(filteredGrants, safeEngagements),
            ["request_evidence", "request_independent_human_review"],
            grantId.HasValue || engagementId.HasValue ? null : new()
            {
                ["grantSkip"] = JsonValue.Create(skip),
                ["grantTake"] = JsonValue.Create(take),
                ["grantTotalCount"] = JsonValue.Create(matchingGrants.Length),
                ["engagementSkip"] = JsonValue.Create(skip),
                ["engagementTake"] = JsonValue.Create(take),
                ["engagementTotalCount"] = JsonValue.Create(matchingEngagements.Length)
            }, truncated: truncated);
    }

    private async Task<InternalToolExecutionResponse> ReadYearEndAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "runId", out var runId))
        {
            var run = await yearEnd.GetAsync(new(request.CompanyId, runId), ct);
            return Success("yearEnd", new { Run = run, Authority = Authority("year_end_technical_readiness") },
                YearEndSources(run), run.AllowedActions,
                new() { ["readinessHash"] = JsonValue.Create(run.CurrentReadiness?.EvidenceHash) },
                freshness: string.Equals(run.Status, "completed", StringComparison.OrdinalIgnoreCase)
                    ? "immutable_completed_history"
                    : "authoritative_live");
        }
        var take = Integer(request, "take", 20, 1, FinanceCloseComplianceAgentContract.MaximumPageSize);
        var runs = await yearEnd.ListAsync(new(request.CompanyId, take), ct);
        return Success("yearEnd", new { Runs = runs, Authority = Authority("year_end_technical_readiness") },
            runs.Select(x => Source("year_end_run", x.Id)), ["read_year_end_run", "request_human_review"],
            new() { ["requestedTake"] = JsonValue.Create(take), ["mayHaveMore"] = JsonValue.Create(runs.Count == take) },
            truncated: runs.Count == take);
    }

    private async Task<InternalToolExecutionResponse> RecommendCloseAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var closeId = OptionalGuid(request, "closeInstanceId");
        var periodId = OptionalGuid(request, "fiscalPeriodId");
        RequireCloseSelector(closeId, periodId);
        var workspace = await closeWorkspace.GetAsync(new(request.CompanyId, periodId, closeId), ct);
        closeId ??= workspace.CloseInstanceId;
        if (!closeId.HasValue)
            return Reject("accounting_close_not_found", "No close instance exists for the selected fiscal period in this company.");
        var allBlockers = (workspace.Readiness?.Blockers ?? [])
            .Concat(workspace.Tasks.SelectMany(x => x.Blockers))
            .DistinctBy(x => (x.Code, x.EvidenceHash, x.OwnerUserId))
            .OrderByDescending(x => !string.Equals(x.Status, "resolved", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.ObservedUtc).ToArray();
        var take = Integer(request, "take", FinanceCloseComplianceAgentContract.MaximumPageSize, 1,
            FinanceCloseComplianceAgentContract.MaximumPageSize);
        var blockers = allBlockers.Take(take)
            .Select((x, index) => new
            {
                Priority = index + 1, x.Code, x.Title, x.Explanation, x.SafeNextAction, x.OwnerUserId,
                x.Status, x.EvidenceCount, x.EvidenceHash, x.ObservedUtc,
                EvidenceAgeHours = Math.Max(0, (DateTime.UtcNow - x.ObservedUtc).TotalHours), x.IsWaivable
            }).ToArray();
        return Recommendation("closeRecommendation", new
        {
            workspace.CloseInstanceId,
            ReadinessHash = workspace.Readiness?.EvidenceHash,
            workspace.Readiness?.IsReady,
            workspace.Readiness?.IsStale,
            TotalBlockerCount = allBlockers.Length,
            Blockers = blockers,
            SafeNextActions = blockers.Select(x => x.SafeNextAction).Distinct().ToArray(),
            Authority = Authority("close_coordination")
        }, CloseWorkspaceSources(workspace), allBlockers.Length > blockers.Length);
    }

    private async Task<InternalToolExecutionResponse> RecommendComplianceAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var obligation = await compliance.GetAsync(request.CompanyId, RequiredGuid(request, "obligationId"), ct);
        var allMissing = obligation.Requirements.Where(x => !x.IsSatisfied).ToArray();
        var take = Integer(request, "take", FinanceCloseComplianceAgentContract.MaximumPageSize, 1,
            FinanceCloseComplianceAgentContract.MaximumPageSize);
        var missing = allMissing.Take(take).Select(x => new { x.Kind, x.Label, x.EvidenceReference }).ToArray();
        return Recommendation("complianceRecommendation", new
        {
            obligation.Id, obligation.Title, obligation.DueDate, obligation.OwnerUserId,
            TotalMissingEvidenceCount = allMissing.Length,
            MissingEvidence = missing,
            AuthorityState = ComplianceAuthorityState(obligation),
            SafeNextActions = missing.Select(x => "Prepare evidence for " + x.Label).Append("Ask an authorized human to complete any filing or declaration.").ToArray(),
            Authority = Authority("compliance_preparation")
        }, ComplianceSources(obligation), allMissing.Length > missing.Length);
    }

    private async Task<InternalToolExecutionResponse> RecommendAuditAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var package = await auditPackages.GetAsync(request.CompanyId, RequiredGuid(request, "packageId"), ct);
        var allMissing = package.Artifacts.Where(x => x.IsRequired && !string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
                                                      !string.Equals(x.Status, "available", StringComparison.OrdinalIgnoreCase)).ToArray();
        var take = Integer(request, "take", FinanceCloseComplianceAgentContract.MaximumPageSize, 1,
            FinanceCloseComplianceAgentContract.MaximumPageSize);
        var missing = allMissing.Take(take).ToArray();
        var matchingVerification = package.PackageChecksum is not null && package.ManifestChecksum is not null &&
            package.Verifications.Any(x => x.IsValid &&
                string.Equals(x.PackageChecksum, package.PackageChecksum, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ManifestChecksum, package.ManifestChecksum, StringComparison.OrdinalIgnoreCase));
        return Recommendation("auditRecommendation", new
        {
            package.Id, package.Status, package.ScopeVersion, package.ScopeHash, package.ManifestChecksum, package.PackageChecksum,
            TotalMissingRequiredArtifactCount = allMissing.Length,
            MissingRequiredArtifacts = missing,
            VerificationCount = package.Verifications.Count,
            MatchingVerificationRecorded = matchingVerification,
            IsTechnicallyComplete = package.IsFinal && allMissing.Length == 0 && matchingVerification,
            HumanApprovalRecorded = package.ApprovedByUserId.HasValue,
            StatutoryApprovalRecorded = false,
            Download = new { ContentReturned = false, AuthorizationCreated = false, OneTimeAuthorizationRequired = true },
            Authority = Authority("audit_package_completeness")
        }, AuditSources(package), allMissing.Length > missing.Length);
    }

    private async Task<InternalToolExecutionResponse> RecommendYearEndAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var run = await yearEnd.GetAsync(new(request.CompanyId, RequiredGuid(request, "runId")), ct);
        var checks = run.CurrentReadiness?.Checks ?? [];
        var allBlockers = checks.Where(x => x.Blocking && !x.Passed).OrderBy(x => x.Code).ToArray();
        var take = Integer(request, "take", FinanceCloseComplianceAgentContract.MaximumPageSize, 1,
            FinanceCloseComplianceAgentContract.MaximumPageSize);
        var blockers = allBlockers.Take(take).ToArray();
        return Recommendation("yearEndRecommendation", new
        {
            run.Id, run.Status,
            ReadinessHash = run.CurrentReadiness?.EvidenceHash,
            TotalPrerequisiteBlockerCount = allBlockers.Length,
            PrerequisiteBlockers = blockers,
            SafeNextActions = blockers.Select(x => x.Explanation).Distinct().ToArray(),
            PendingHumanApproval = run.ApprovedByUserId is null,
            Authority = Authority("year_end_prerequisites")
        }, YearEndSources(run), allBlockers.Length > blockers.Length);
    }

    private static object ComplianceAuthorityState(ComplianceObligationDto obligation) => new
    {
        obligation.Id,
        ManualSubmissionEvidenceRecorded = obligation.SubmissionEvidence.Count > 0,
        ProviderAcknowledgementRecorded = obligation.Acknowledgements.Count > 0,
        SubmittedOrAccepted = obligation.Acknowledgements.Count > 0,
        HumanApprovalRecorded = obligation.History.Any(x => x.Action.Contains("approve", StringComparison.OrdinalIgnoreCase)),
        StatutoryComplianceProven = false,
        obligation.ComplianceNotice
    };

    private static object Authority(string technicalState) => new
    {
        TechnicalState = technicalState,
        ManualSubmissionEvidenceIsAuthorityAcknowledgement = false,
        ProviderAcknowledgementIsHumanApproval = false,
        HumanApprovalIsProfessionalOrStatutorySignOff = false,
        FinalLockAllowed = false,
        ReopenAllowed = false,
        FilingAllowed = false,
        RolloverAllowed = false,
        ProfessionalApprovalAllowed = false,
        Notice = FinanceCloseComplianceAgentContract.AuthorityNotice
    };

    private static InternalToolExecutionResponse Success<T>(string property, T value, IEnumerable<string> sources,
        IEnumerable<string> allowedActions, Dictionary<string, JsonNode?>? extra = null, bool truncated = false,
        PageMetadata? page = null, string freshness = "authoritative_live")
    {
        var metadata = Metadata(sources, allowedActions, truncated, freshness);
        if (extra is not null) foreach (var item in extra) metadata[item.Key] = item.Value;
        if (page is not null)
        {
            metadata["skip"] = JsonValue.Create(page.Skip);
            metadata["take"] = JsonValue.Create(page.Take);
            metadata["totalCount"] = JsonValue.Create(page.TotalCount);
        }
        return InternalToolExecutionResponse.Succeeded("Authoritative close and compliance read completed.",
            new() { [property] = JsonSerializer.SerializeToNode(value, JsonOptions) }, metadata);
    }

    private static InternalToolExecutionResponse Recommendation<T>(string property, T value, IEnumerable<string> sources,
        bool truncated = false) =>
        InternalToolExecutionResponse.Succeeded("Evidence-backed recommendation prepared; final authority remains human-only.",
            new() { [property] = JsonSerializer.SerializeToNode(value, JsonOptions) },
            Metadata(sources, ["review_evidence", "request_authorized_human_action"], truncated));

    private static InternalToolExecutionResponse Reject(string code, string message) =>
        InternalToolExecutionResponse.Failed("blocked", code, message, null,
            Metadata([], ["correct_request", "open_owning_workspace"]));

    private static Dictionary<string, JsonNode?> Metadata(IEnumerable<string> sources, IEnumerable<string> allowedActions,
        bool truncated = false, string freshness = "authoritative_live")
    {
        var distinctSources = sources.Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["contractVersion"] = JsonValue.Create(FinanceCloseComplianceAgentContract.Version),
            ["generatedUtc"] = JsonValue.Create(DateTime.UtcNow),
            ["freshness"] = JsonValue.Create(freshness),
            ["truncated"] = JsonValue.Create(truncated),
            ["sourceIdCount"] = JsonValue.Create(distinctSources.Length),
            ["sourceIdsTruncated"] = JsonValue.Create(distinctSources.Length > FinanceCloseComplianceAgentContract.MaximumSourceIds),
            ["sourceIds"] = new JsonArray(distinctSources.Take(FinanceCloseComplianceAgentContract.MaximumSourceIds)
                .Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["allowedActions"] = new JsonArray(allowedActions.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["authorityNotice"] = JsonValue.Create(FinanceCloseComplianceAgentContract.AuthorityNotice)
        };
    }

    private static IEnumerable<string> CloseSources(AccountingCloseDto close) =>
        new[]
        {
            Source("close_instance", close.Id), Source("fiscal_period", close.FiscalPeriodId),
            Source("close_template", close.TemplateId), Source("close_template_version", close.TemplateVersionId)
        }
        .Concat(close.Tasks.Select(task => Source("close_task", task.Id)))
        .Concat(close.Tasks.SelectMany(task => task.Evidence).Select(evidence => Source("document", evidence.DocumentId)));

    private static IEnumerable<string> CloseWorkspaceSources(AccountingCloseWorkspaceDto workspace) =>
        (workspace.CloseInstanceId.HasValue ? [Source("close_instance", workspace.CloseInstanceId.Value)] : Array.Empty<string>())
        .Concat(workspace.SelectedPeriod is null ? [] : [Source("fiscal_period", workspace.SelectedPeriod.FiscalPeriodId)])
        .Concat(workspace.Readiness is null ? [] : [Source("close_readiness_snapshot", workspace.Readiness.SnapshotId)])
        .Concat(workspace.Tasks.Select(task => Source("close_task", task.Id)))
        .Concat(workspace.Tasks.SelectMany(task => task.Evidence).Select(evidence => Source("document", evidence.DocumentId)));

    private static IEnumerable<string> CloseReadinessSources(AccountingCloseWorkspaceDto workspace,
        AccountingCloseGovernanceDto governance) => CloseWorkspaceSources(workspace)
        .Concat(governance.Snapshots.Select(snapshot => Source("close_readiness_snapshot", snapshot.Id)))
        .Concat(governance.Waivers.Select(waiver => Source("close_waiver", waiver.Id)))
        .Concat(governance.Waivers.Select(waiver => Source("document", waiver.EvidenceDocumentId)))
        .Concat(governance.ReopenRequests.Select(request => Source("close_reopen_request", request.Id)))
        .Concat(governance.SignOffs.Select(signOff => Source("close_sign_off", signOff.Id)));

    private static IEnumerable<string> ComplianceSources(ComplianceObligationDto obligation) =>
        new[] { Source("compliance_obligation", obligation.Id), Source("vat_filing_period", obligation.VatFilingPeriodId) }
            .Concat(obligation.SubmissionEvidence.Select(evidence => Source("compliance_evidence", evidence.Id)))
            .Concat(obligation.Acknowledgements.Select(acknowledgement => Source("compliance_acknowledgement", acknowledgement.Id)));

    private static IEnumerable<string> AuditSources(AuditPackageDto package) =>
        new[] { Source("audit_package", package.Id), Source("fiscal_period", package.FiscalPeriodId) }
            .Concat(package.Artifacts.Select(artifact => Source("audit_package_artifact", artifact.Id)))
            .Concat(package.Verifications.Select(verification => Source("audit_package_verification", verification.Id)));

    private static AccountantEngagementDto SanitizeAccountantEngagement(AccountantEngagementDto engagement) =>
        engagement with
        {
            EvidenceRequests = engagement.EvidenceRequests.Select(request => request with
            {
                Responses = request.Responses.Select(response => response.DocumentAccessible
                    ? response
                    : response with { DocumentId = null }).ToArray()
            }).ToArray()
        };

    private static IEnumerable<string> AccountantSources(IEnumerable<AccountantGrantDto> grants,
        IEnumerable<AccountantEngagementDto> engagements)
    {
        var materialized = engagements.ToArray();
        return grants.Select(grant => Source("accountant_grant", grant.Id))
            .Concat(materialized.Select(engagement => Source("accountant_engagement", engagement.Id)))
            .Concat(materialized.SelectMany(engagement => engagement.ReviewItems).Select(item => Source("accountant_review_item", item.Id)))
            .Concat(materialized.SelectMany(engagement => engagement.EvidenceRequests).Select(request => Source("accountant_evidence_request", request.Id)))
            .Concat(materialized.SelectMany(engagement => engagement.EvidenceRequests)
                .SelectMany(request => request.Responses)
                .Where(response => response.DocumentAccessible && response.DocumentId.HasValue)
                .Select(response => Source("document", response.DocumentId!.Value)))
            .Concat(materialized.SelectMany(engagement => engagement.SignOffs).Select(signOff => Source("accountant_sign_off", signOff.Id)));
    }

    private static IEnumerable<string> YearEndSources(YearEndRunDto run) =>
        new[] { Source("year_end_run", run.Id), Source("fiscal_period", run.TargetFiscalPeriodId) }
            .Concat(run.CurrentReadiness is null ? [] : [Source("year_end_readiness_snapshot", run.CurrentReadiness.Id)])
            .Concat(run.RetainedEarningsLedgerEntryId.HasValue ? [Source("ledger_entry", run.RetainedEarningsLedgerEntryId.Value)] : [])
            .Concat(run.OpeningBalanceLedgerEntryId.HasValue ? [Source("ledger_entry", run.OpeningBalanceLedgerEntryId.Value)] : [])
            .Concat(run.SubsequentEvents.Where(item => item.EvidenceDocumentId.HasValue)
                .Select(item => Source("document", item.EvidenceDocumentId!.Value)))
            .Concat(run.SignOffs.Select(signOff => Source("year_end_sign_off", signOff.Id)));

    private static string Source(string type, Guid id) => type + ":" + id;
    private static void RequireCloseSelector(Guid? closeInstanceId, Guid? fiscalPeriodId)
    {
        if (!closeInstanceId.HasValue && !fiscalPeriodId.HasValue)
            throw new ArgumentException("closeInstanceId or fiscalPeriodId is required.");
    }
    private static Guid RequiredGuid(InternalToolExecutionRequest request, string key) =>
        OptionalGuid(request, key) ?? throw new ArgumentException($"{key} is required.");
    private static Guid? OptionalGuid(InternalToolExecutionRequest request, string key) =>
        TryGuid(request, key, out var value) ? value : null;
    private static bool TryGuid(InternalToolExecutionRequest request, string key, out Guid value)
    {
        value = Guid.Empty;
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return false;
        if (!Guid.TryParse(node.GetValue<string>(), out value) || value == Guid.Empty)
            throw new ArgumentException($"{key} must be a non-empty UUID.");
        return true;
    }
    private static string? Text(InternalToolExecutionRequest request, string key, int maximumLength)
    {
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return null;
        var value = node.GetValue<string>().Trim();
        if (value.Length == 0) return null;
        if (value.Length > maximumLength) throw new ArgumentException($"{key} exceeds {maximumLength} characters.");
        return value;
    }
    private static int Integer(InternalToolExecutionRequest request, string key, int fallback, int minimum, int maximum)
    {
        if (!request.Payload.TryGetValue(key, out var node) || node is null) return fallback;
        var value = node.GetValue<int>();
        if (value < minimum || value > maximum) throw new ArgumentException($"{key} must be between {minimum} and {maximum}.");
        return value;
    }
    private static DateOnly? Date(InternalToolExecutionRequest request, string key)
    {
        var value = Text(request, key, 10);
        if (value is null) return null;
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new ArgumentException($"{key} must use yyyy-MM-dd.");
        return date;
    }
    private sealed record PageMetadata(long Skip, int Take, long TotalCount);
    private static string Safe(string message) => string.IsNullOrWhiteSpace(message) || message.Length > 500
        ? "The requested close or compliance object is unavailable."
        : message;
}
