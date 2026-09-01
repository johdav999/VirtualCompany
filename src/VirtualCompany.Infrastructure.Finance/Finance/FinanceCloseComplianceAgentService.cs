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
        var result = await closes.ListTemplatesAsync(new(request.CompanyId, Text(request, "status", 30),
            Integer(request, "skip", 0, 0, 100_000), Integer(request, "take", 100, 1, FinanceCloseComplianceAgentContract.MaximumPageSize)), ct);
        return Success("closeTemplates", result, result.Items.Select(x => Source("close_template", x.Id)),
            ["read_close_template", "read_close_instance"]);
    }

    private async Task<InternalToolExecutionResponse> ReadInstanceAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var closeId = RequiredGuid(request, "closeInstanceId");
        var close = await closes.GetAsync(new(request.CompanyId, closeId), ct);
        return Success("closeInstance", close,
            close.Tasks.Select(x => Source("close_task", x.Id)).Prepend(Source("close_instance", close.Id)), close.AllowedActions);
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
        return Success("closeReadiness", view, [Source("close_instance", closeId.Value)], workspace.AllowedActions,
            new() { ["readinessHash"] = JsonValue.Create(hash), ["readinessIsStale"] = JsonValue.Create(workspace.Readiness?.IsStale) });
    }

    private async Task<InternalToolExecutionResponse> ReadPeriodHistoryAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var periodId = RequiredGuid(request, "fiscalPeriodId");
        var history = await reporting.GetPeriodHistoryAsync(request.CompanyId, periodId, ct);
        return Success("periodLockHistory", new { FiscalPeriodId = periodId, History = history, Authority = Authority("period_lock_history") },
            [Source("fiscal_period", periodId)], ["read_close_readiness"]);
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
        var calendar = await compliance.GetCalendarAsync(new(request.CompanyId, from, to), ct);
        var states = calendar.Obligations.Select(ComplianceAuthorityState).ToArray();
        return Success("complianceObligations", new { Calendar = calendar, AuthorityStates = states, Authority = Authority("compliance_preparation") },
            calendar.Obligations.Select(x => Source("compliance_obligation", x.Id)), ["prepare_evidence", "request_human_submission", "record_provider_acknowledgement"]);
    }

    private InternalToolExecutionResponse ComplianceSuccess(ComplianceObligationDto obligation) =>
        Success("complianceObligations", new { Obligation = obligation, AuthorityState = ComplianceAuthorityState(obligation), Authority = Authority("compliance_preparation") },
            [Source("compliance_obligation", obligation.Id)], obligation.AllowedActions);

    private async Task<InternalToolExecutionResponse> ReadAuditPackagesAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "packageId", out var packageId))
        {
            var package = await auditPackages.GetAsync(request.CompanyId, packageId, ct);
            return AuditSuccess(package);
        }
        var workspace = await auditPackages.ListAsync(new(request.CompanyId, OptionalGuid(request, "fiscalPeriodId"),
            Integer(request, "skip", 0, 0, 100_000), Integer(request, "take", 100, 1, FinanceCloseComplianceAgentContract.MaximumPageSize)), ct);
        return Success("auditPackages", new
        {
            Workspace = workspace,
            Download = new { ContentReturned = false, AuthorizationCreated = false, OneTimeAuthorizationRequired = true },
            Authority = Authority("audit_package_technical_verification")
        }, workspace.Packages.Select(x => Source("audit_package", x.Id)), ["request_owning_authorization", "human_approval"]);
    }

    private InternalToolExecutionResponse AuditSuccess(AuditPackageDto package) =>
        Success("auditPackages", new
        {
            Package = package,
            Download = new { ContentReturned = false, AuthorizationCreated = false, OneTimeAuthorizationRequired = true, package.RetainUntilUtc },
            Authority = Authority("audit_package_technical_verification")
        }, [Source("audit_package", package.Id)], ["request_owning_authorization", "human_approval"]);

    private async Task<InternalToolExecutionResponse> ReadAccountantActivityAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var grants = await accountantCollaboration.ListGrantsAsync(request.CompanyId, ct);
        var engagements = await accountantCollaboration.ListEngagementsAsync(request.CompanyId, ct);
        var grantId = OptionalGuid(request, "grantId");
        var engagementId = OptionalGuid(request, "engagementId");
        var filteredGrants = grantId.HasValue ? grants.Where(x => x.Id == grantId).ToArray() : grants;
        var filteredEngagements = engagementId.HasValue ? engagements.Where(x => x.Id == engagementId).ToArray() : engagements;
        if (grantId.HasValue && filteredGrants.Count == 0 || engagementId.HasValue && filteredEngagements.Count == 0)
            return Reject("accountant_grant_object_not_found", "The requested grant or engagement was not found in this company-scoped access set.");
        return Success("accountantAccessActivity", new { Grants = filteredGrants, Engagements = filteredEngagements, Authority = Authority("accountant_collaboration") },
            filteredGrants.Select(x => Source("accountant_grant", x.Id)).Concat(filteredEngagements.Select(x => Source("accountant_engagement", x.Id))),
            ["request_evidence", "request_independent_human_review"]);
    }

    private async Task<InternalToolExecutionResponse> ReadYearEndAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        if (TryGuid(request, "runId", out var runId))
        {
            var run = await yearEnd.GetAsync(new(request.CompanyId, runId), ct);
            return Success("yearEnd", new { Run = run, Authority = Authority("year_end_technical_readiness") },
                [Source("year_end_run", run.Id)], run.AllowedActions,
                new() { ["readinessHash"] = JsonValue.Create(run.CurrentReadiness?.EvidenceHash) });
        }
        var runs = await yearEnd.ListAsync(new(request.CompanyId, Integer(request, "take", 20, 1, 100)), ct);
        return Success("yearEnd", new { Runs = runs, Authority = Authority("year_end_technical_readiness") },
            runs.Select(x => Source("year_end_run", x.Id)), ["read_year_end_run", "request_human_review"]);
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
        var blockers = (workspace.Readiness?.Blockers ?? [])
            .Concat(workspace.Tasks.SelectMany(x => x.Blockers))
            .DistinctBy(x => (x.Code, x.EvidenceHash, x.OwnerUserId))
            .OrderByDescending(x => !string.Equals(x.Status, "resolved", StringComparison.OrdinalIgnoreCase))
            .ThenBy(x => x.ObservedUtc)
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
            Blockers = blockers,
            SafeNextActions = blockers.Select(x => x.SafeNextAction).Distinct().ToArray(),
            Authority = Authority("close_coordination")
        }, [Source("close_instance", closeId.Value)]);
    }

    private async Task<InternalToolExecutionResponse> RecommendComplianceAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var obligation = await compliance.GetAsync(request.CompanyId, RequiredGuid(request, "obligationId"), ct);
        var missing = obligation.Requirements.Where(x => !x.IsSatisfied).Select(x => new { x.Kind, x.Label, x.EvidenceReference }).ToArray();
        return Recommendation("complianceRecommendation", new
        {
            obligation.Id, obligation.Title, obligation.DueDate, obligation.OwnerUserId,
            MissingEvidence = missing,
            AuthorityState = ComplianceAuthorityState(obligation),
            SafeNextActions = missing.Select(x => "Prepare evidence for " + x.Label).Append("Ask an authorized human to complete any filing or declaration.").ToArray(),
            Authority = Authority("compliance_preparation")
        }, [Source("compliance_obligation", obligation.Id)]);
    }

    private async Task<InternalToolExecutionResponse> RecommendAuditAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var package = await auditPackages.GetAsync(request.CompanyId, RequiredGuid(request, "packageId"), ct);
        var missing = package.Artifacts.Where(x => x.IsRequired && !string.Equals(x.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
                                                   !string.Equals(x.Status, "available", StringComparison.OrdinalIgnoreCase)).ToArray();
        return Recommendation("auditRecommendation", new
        {
            package.Id, package.Status, package.ScopeVersion, package.ScopeHash, package.ManifestChecksum, package.PackageChecksum,
            MissingRequiredArtifacts = missing,
            VerificationCount = package.Verifications.Count,
            IsTechnicallyComplete = package.IsFinal && missing.Length == 0 && package.Verifications.Any(x => x.IsValid),
            Download = new { ContentReturned = false, AuthorizationCreated = false, OneTimeAuthorizationRequired = true },
            Authority = Authority("audit_package_completeness")
        }, [Source("audit_package", package.Id)]);
    }

    private async Task<InternalToolExecutionResponse> RecommendYearEndAsync(InternalToolExecutionRequest request, CancellationToken ct)
    {
        var run = await yearEnd.GetAsync(new(request.CompanyId, RequiredGuid(request, "runId")), ct);
        var checks = run.CurrentReadiness?.Checks ?? [];
        var blockers = checks.Where(x => x.Blocking && !x.Passed).OrderBy(x => x.Code).ToArray();
        return Recommendation("yearEndRecommendation", new
        {
            run.Id, run.Status,
            ReadinessHash = run.CurrentReadiness?.EvidenceHash,
            PrerequisiteBlockers = blockers,
            SafeNextActions = blockers.Select(x => x.Explanation).Distinct().ToArray(),
            PendingHumanApproval = run.ApprovedByUserId is null,
            Authority = Authority("year_end_prerequisites")
        }, [Source("year_end_run", run.Id)]);
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
        IEnumerable<string> allowedActions, Dictionary<string, JsonNode?>? extra = null)
    {
        var metadata = Metadata(sources, allowedActions);
        if (extra is not null) foreach (var item in extra) metadata[item.Key] = item.Value;
        return InternalToolExecutionResponse.Succeeded("Authoritative close and compliance read completed.",
            new() { [property] = JsonSerializer.SerializeToNode(value, JsonOptions) }, metadata);
    }

    private static InternalToolExecutionResponse Recommendation<T>(string property, T value, IEnumerable<string> sources) =>
        InternalToolExecutionResponse.Succeeded("Evidence-backed recommendation prepared; final authority remains human-only.",
            new() { [property] = JsonSerializer.SerializeToNode(value, JsonOptions) },
            Metadata(sources, ["review_evidence", "request_authorized_human_action"]));

    private static InternalToolExecutionResponse Reject(string code, string message) =>
        InternalToolExecutionResponse.Failed("blocked", code, message, null,
            Metadata([], ["correct_request", "open_owning_workspace"]));

    private static Dictionary<string, JsonNode?> Metadata(IEnumerable<string> sources, IEnumerable<string> allowedActions) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["contractVersion"] = JsonValue.Create(FinanceCloseComplianceAgentContract.Version),
            ["generatedUtc"] = JsonValue.Create(DateTime.UtcNow),
            ["freshness"] = JsonValue.Create("authoritative_live"),
            ["sourceIds"] = new JsonArray(sources.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["allowedActions"] = new JsonArray(allowedActions.Distinct(StringComparer.OrdinalIgnoreCase).Select(x => (JsonNode?)JsonValue.Create(x)).ToArray()),
            ["authorityNotice"] = JsonValue.Create(FinanceCloseComplianceAgentContract.AuthorityNotice)
        };

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
    private static string Safe(string message) => string.IsNullOrWhiteSpace(message) || message.Length > 500
        ? "The requested close or compliance object is unavailable."
        : message;
}
