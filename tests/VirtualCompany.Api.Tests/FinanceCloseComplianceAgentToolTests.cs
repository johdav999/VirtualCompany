using System.Reflection;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceCloseComplianceAgentToolTests
{
    [Fact]
    public void Manifest_registers_prompt3_reads_and_recommendations_without_execute_authority()
    {
        var registry = new StaticCompanyToolRegistry();
        var definitions = registry.ListToolDefinitions();
        var operations = FinanceAgentCoverageCatalogue.Manifests.SelectMany(x => x.Operations).ToArray();

        foreach (var toolName in FinanceCloseComplianceAgentToolIds.All)
        {
            var definition = Assert.Single(definitions, x => x.ToolName == toolName);
            var operation = Assert.Single(operations, x => x.ToolName == toolName);
            var expected = FinanceCloseComplianceAgentToolIds.ActionFor(toolName);
            Assert.Equal(expected, definition.ActionType);
            Assert.False(definition.SensitiveAction);
            Assert.Equal(FinancePermissions.View, operation.RequiredPermission);
            Assert.Equal(expected == ToolActionType.Read
                ? FinanceAgentCoverageSupportStates.ImplementedRead
                : FinanceAgentCoverageSupportStates.ImplementedRecommendDraft, operation.SupportState);
        }

        Assert.DoesNotContain(definitions, x => FinanceCloseComplianceAgentToolIds.Contains(x.ToolName) && x.ActionType == ToolActionType.Execute);
        Assert.DoesNotContain(definitions, x => FinanceCloseComplianceAgentToolIds.Contains(x.ToolName) &&
                                               x.ToolName.Contains("download", StringComparison.OrdinalIgnoreCase));
        var complianceSchema = definitions.Single(x => x.ToolName == FinanceCloseComplianceAgentToolIds.ReadComplianceObligations);
        Assert.Equal(FinanceCloseComplianceAgentContract.MaximumPageSize,
            complianceSchema.InputSchema["properties"]!["take"]!["maximum"]!.GetValue<int>());
        var accountantSchema = definitions.Single(x => x.ToolName == FinanceCloseComplianceAgentToolIds.ReadAccountantAccessActivity);
        Assert.Equal(FinanceCloseComplianceAgentContract.MaximumPageSize,
            accountantSchema.InputSchema["properties"]!["take"]!["maximum"]!.GetValue<int>());

        Assert.Contains(operations, x => x.Id == "final_close_year_end_authority" &&
                                         x.SupportState == FinanceAgentCoverageSupportStates.HumanOnly);
        Assert.Contains(operations, x => x.Id == "final_statutory_filing" &&
                                         x.SupportState == FinanceAgentCoverageSupportStates.HumanOnly);
    }

    [Fact]
    public async Task Manual_submission_evidence_without_acknowledgement_is_not_reported_as_submitted_or_accepted()
    {
        var companyId = Guid.NewGuid();
        var obligationId = Guid.NewGuid();
        var obligation = new ComplianceObligationDto(obligationId, companyId, "se_vat_return", "VAT return", "SE",
            "sweden-statutory-candidate", "1.4.0", "definition-hash", "explicit", new DateOnly(2026, 9, 14),
            Guid.NewGuid(), "manual_submission_recorded", "manual", Guid.NewGuid(), null, null, null, null,
            "source-hash", "export-ref", "export-checksum", 4, DateTime.UtcNow, DateTime.UtcNow,
            [new("vat_return", "VAT return evidence", true, "document:1")], [],
            [new(Guid.NewGuid(), "manual-evidence", "evidence-hash", Guid.NewGuid(), DateTime.UtcNow, "recorded", null, null)],
            [], [], [], "Recorded evidence is not authority acknowledgement.");
        var compliance = Proxy<IComplianceObligationService>((method, _) => method.Name == nameof(IComplianceObligationService.GetAsync)
            ? Task.FromResult(obligation)
            : Unexpected(method));
        var service = Service(compliance: compliance);

        var response = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ReadComplianceObligations,
            ToolActionType.Read, companyId, ("obligationId", JsonValue.Create(obligationId.ToString()))), default);

        Assert.True(response.Success);
        var state = response.Data["complianceObligations"]!["authorityState"]!;
        Assert.True(state["manualSubmissionEvidenceRecorded"]!.GetValue<bool>());
        Assert.False(state["providerAcknowledgementRecorded"]!.GetValue<bool>());
        Assert.False(state["submittedOrAccepted"]!.GetValue<bool>());
        Assert.False(state["statutoryComplianceProven"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Protected_package_read_never_authorizes_or_streams_download_content()
    {
        var companyId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var package = new AuditPackageDto(packageId, companyId, Guid.NewGuid(), "August 2026", "period_close", "v1",
            "scope-hash", "{}", "expired", true, "manifest", "package", "audit.zip", "application/zip", 100,
            Guid.NewGuid(), null, DateTime.UtcNow.AddDays(-10), DateTime.UtcNow, DateTime.UtcNow.AddDays(-1),
            DateTime.UtcNow.AddDays(-9), 1, 3, false, null, null, 2, [], [], [], []);
        var audit = Proxy<IAuditPackageService>((method, _) => method.Name == nameof(IAuditPackageService.GetAsync)
            ? Task.FromResult(package)
            : Unexpected(method));
        var service = Service(auditPackages: audit);

        var response = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ReadAuditPackages,
            ToolActionType.Read, companyId, ("packageId", JsonValue.Create(packageId.ToString()))), default);

        Assert.True(response.Success);
        var download = response.Data["auditPackages"]!["download"]!;
        Assert.False(download["contentReturned"]!.GetValue<bool>());
        Assert.False(download["authorizationCreated"]!.GetValue<bool>());
        Assert.True(download["oneTimeAuthorizationRequired"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Recommendation_tools_reject_read_or_execute_action_classes_before_querying()
    {
        var service = Service();
        foreach (var action in new[] { ToolActionType.Read, ToolActionType.Execute })
        {
            var response = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.PrioritizeCloseBlockers,
                action, Guid.NewGuid(), ("closeInstanceId", JsonValue.Create(Guid.NewGuid().ToString()))), default);
            Assert.False(response.Success);
            Assert.Equal("close_compliance_action_mismatch", response.ErrorCode);
        }

        var lockAttempt = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ReadReadiness,
            ToolActionType.Execute, Guid.NewGuid(), ("closeInstanceId", JsonValue.Create(Guid.NewGuid().ToString()))), default);
        Assert.False(lockAttempt.Success);
        Assert.Equal("close_compliance_action_mismatch", lockAttempt.ErrorCode);
    }

    [Fact]
    public async Task August_close_question_can_use_resolved_period_and_returns_hash_owner_evidence_age_and_next_action()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var closeId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var observed = DateTime.UtcNow.AddHours(-5);
        var blocker = new AccountingCloseWorkspaceBlockerDto("missing_bank_evidence", "Bank evidence missing",
            "The statement has not been linked.", "Link the authorized bank statement.", ownerId, "open", 0,
            observed, "/finance/accounting/close/tasks/1", false, "evidence-hash-v2");
        var readiness = new AccountingCloseWorkspaceReadinessDto(Guid.NewGuid(), 2, "prepared", false,
            "readiness-hash-v2", DateTime.UtcNow, 3, 1, 0, false, [blocker]);
        var workspace = new AccountingCloseWorkspaceDto(companyId, "Company", "manager", DateTime.UtcNow,
            new(periodId, "August 2026", DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow, false, closeId, "in_progress", DateTime.UtcNow),
            closeId, "August close", "in_progress", 5, [], readiness, [], [], [], [], []);
        GetAccountingCloseWorkspaceQuery? captured = null;
        var workspaceService = Proxy<IAccountingCloseWorkspaceService>((method, args) =>
        {
            if (method.Name != nameof(IAccountingCloseWorkspaceService.GetAsync)) return Unexpected(method);
            captured = (GetAccountingCloseWorkspaceQuery)args![0]!;
            return Task.FromResult(workspace);
        });
        var service = Service(closeWorkspace: workspaceService);

        var response = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.PrioritizeCloseBlockers,
            ToolActionType.Recommend, companyId, ("fiscalPeriodId", JsonValue.Create(periodId.ToString()))), default);

        Assert.True(response.Success);
        Assert.Equal(periodId, captured!.FiscalPeriodId);
        Assert.Null(captured.CloseInstanceId);
        var recommendation = response.Data["closeRecommendation"]!;
        Assert.Equal("readiness-hash-v2", recommendation["readinessHash"]!.GetValue<string>());
        var first = recommendation["blockers"]![0]!;
        Assert.Equal(ownerId, first["ownerUserId"]!.GetValue<Guid>());
        Assert.Equal("evidence-hash-v2", first["evidenceHash"]!.GetValue<string>());
        Assert.True(first["evidenceAgeHours"]!.GetValue<double>() >= 5);
        Assert.Equal("Link the authorized bank statement.", first["safeNextAction"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stale_close_readiness_preserves_the_exact_hash_and_is_not_labelled_live()
    {
        var companyId = Guid.NewGuid();
        var periodId = Guid.NewGuid();
        var closeId = Guid.NewGuid();
        var snapshotId = Guid.NewGuid();
        var readiness = new AccountingCloseWorkspaceReadinessDto(snapshotId, 3, "prepared", false,
            "close-hash-v3", DateTime.UtcNow.AddHours(-2), 3, 1, 0, true, []);
        var workspace = new AccountingCloseWorkspaceDto(companyId, "Company", "manager", DateTime.UtcNow,
            new(periodId, "August 2026", DateTime.UtcNow.AddMonths(-1), DateTime.UtcNow, false, closeId,
                "in_progress", DateTime.UtcNow), closeId, "August close", "in_progress", 4, [], readiness,
            [], [], [], [], []);
        var policy = new AccountingClosePolicyDto(Guid.NewGuid(), companyId, 10_000m, "SEK", 24, 1,
            Guid.NewGuid(), DateTime.UtcNow);
        var governanceSnapshot = new AccountingCloseReadinessSnapshotDto(snapshotId, 3, "prepared",
            "close-hash-v3", "trial-balance-v3", false, Guid.NewGuid(), DateTime.UtcNow.AddHours(-2),
            null, null, null, null, null, null, null, null, null, 3, []);
        var governance = new AccountingCloseGovernanceDto(closeId, periodId, "in_progress", 4, policy,
            governanceSnapshot, [governanceSnapshot], [], [], [], []);
        var workspaceService = Proxy<IAccountingCloseWorkspaceService>((method, _) =>
            method.Name == nameof(IAccountingCloseWorkspaceService.GetAsync) ? Task.FromResult(workspace) : Unexpected(method));
        var governanceService = Proxy<IAccountingCloseGovernanceService>((method, _) =>
            method.Name == nameof(IAccountingCloseGovernanceService.GetAsync) ? Task.FromResult(governance) : Unexpected(method));
        var service = Service(closeWorkspace: workspaceService, closeGovernance: governanceService);

        var response = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ReadReadiness,
            ToolActionType.Read, companyId, ("closeInstanceId", JsonValue.Create(closeId.ToString()))), default);

        Assert.True(response.Success);
        Assert.Equal("close-hash-v3", response.Metadata["readinessHash"]!.GetValue<string>());
        Assert.True(response.Metadata["readinessIsStale"]!.GetValue<bool>());
        Assert.Equal("authoritative_stale", response.Metadata["freshness"]!.GetValue<string>());
        Assert.Contains("close_readiness_snapshot:" + snapshotId,
            response.Metadata["sourceIds"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public async Task Compliance_calendar_applies_bounded_pages_and_rejects_unbounded_date_ranges()
    {
        var companyId = Guid.NewGuid();
        var obligations = Enumerable.Range(1, 3).Select(index => Obligation(companyId, index)).ToArray();
        var calls = 0;
        var compliance = Proxy<IComplianceObligationService>((method, args) =>
        {
            if (method.Name != nameof(IComplianceObligationService.GetCalendarAsync)) return Unexpected(method);
            calls++;
            var query = (GetComplianceCalendarQuery)args![0]!;
            return Task.FromResult(new ComplianceCalendarDto(companyId, query.From, query.To, 3, 0, 0, 0, obligations));
        });
        var service = Service(compliance: compliance);

        var page = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ReadComplianceObligations,
            ToolActionType.Read, companyId, ("from", JsonValue.Create("2026-01-01")),
            ("to", JsonValue.Create("2026-12-31")), ("skip", JsonValue.Create(1)), ("take", JsonValue.Create(1))), default);
        var unbounded = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ReadComplianceObligations,
            ToolActionType.Read, companyId, ("from", JsonValue.Create("2025-01-01")),
            ("to", JsonValue.Create("2027-01-02"))), default);

        Assert.True(page.Success);
        Assert.Single(page.Data["complianceObligations"]!["calendar"]!["obligations"]!.AsArray());
        Assert.Equal(obligations[1].Id,
            page.Data["complianceObligations"]!["calendar"]!["obligations"]![0]!["id"]!.GetValue<Guid>());
        Assert.True(page.Metadata["truncated"]!.GetValue<bool>());
        Assert.Equal(1L, page.Metadata["skip"]!.GetValue<long>());
        Assert.Equal(1, page.Metadata["take"]!.GetValue<int>());
        Assert.Equal(3L, page.Metadata["totalCount"]!.GetValue<long>());
        Assert.False(unbounded.Success);
        Assert.Equal("finance_close_request_invalid", unbounded.ErrorCode);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Audit_completeness_requires_verification_of_current_hashes_and_never_claims_statutory_approval()
    {
        var companyId = Guid.NewGuid();
        var packageId = Guid.NewGuid();
        var staleVerification = new AuditPackageVerificationDto(Guid.NewGuid(), Guid.NewGuid(), true,
            "old-package", "old-manifest", 2, 0, 0, "verified", "Old version verified.", DateTime.UtcNow.AddDays(-1));
        var package = AuditPackage(companyId, packageId, "manifest-v2", "package-v2", [staleVerification]);
        var audit = Proxy<IAuditPackageService>((method, _) => method.Name == nameof(IAuditPackageService.GetAsync)
            ? Task.FromResult(package)
            : Unexpected(method));
        var service = Service(auditPackages: audit);

        var response = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ExplainAuditPackageCompleteness,
            ToolActionType.Recommend, companyId, ("packageId", JsonValue.Create(packageId.ToString()))), default);

        Assert.True(response.Success);
        var recommendation = response.Data["auditRecommendation"]!;
        Assert.False(recommendation["matchingVerificationRecorded"]!.GetValue<bool>());
        Assert.False(recommendation["isTechnicallyComplete"]!.GetValue<bool>());
        Assert.False(recommendation["humanApprovalRecorded"]!.GetValue<bool>());
        Assert.False(recommendation["statutoryApprovalRecorded"]!.GetValue<bool>());
        Assert.False(recommendation["authority"]!["professionalApprovalAllowed"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Accountant_activity_is_grant_scoped_and_hides_inaccessible_document_identifiers()
    {
        var companyId = Guid.NewGuid();
        var grantOne = Grant(companyId, "Accountant one");
        var grantTwo = Grant(companyId, "Accountant two");
        var inaccessibleDocumentId = Guid.NewGuid();
        var accessibleDocumentId = Guid.NewGuid();
        var engagementOne = Engagement(companyId, grantOne.Id, "First review", inaccessibleDocumentId, accessibleDocumentId);
        var engagementTwo = Engagement(companyId, grantTwo.Id, "Second review", Guid.NewGuid(), Guid.NewGuid());
        var collaboration = Proxy<IAccountantCollaborationService>((method, _) => method.Name switch
        {
            nameof(IAccountantCollaborationService.ListGrantsAsync) =>
                Task.FromResult<IReadOnlyList<AccountantGrantDto>>([grantOne, grantTwo]),
            nameof(IAccountantCollaborationService.ListEngagementsAsync) =>
                Task.FromResult<IReadOnlyList<AccountantEngagementDto>>([engagementOne, engagementTwo]),
            _ => Unexpected(method)
        });
        var service = Service(accountantCollaboration: collaboration);

        var scoped = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ReadAccountantAccessActivity,
            ToolActionType.Read, companyId, ("grantId", JsonValue.Create(grantOne.Id.ToString()))), default);
        var mismatched = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ReadAccountantAccessActivity,
            ToolActionType.Read, companyId, ("grantId", JsonValue.Create(grantOne.Id.ToString())),
            ("engagementId", JsonValue.Create(engagementTwo.Id.ToString()))), default);

        Assert.True(scoped.Success);
        var result = scoped.Data["accountantAccessActivity"]!;
        Assert.Single(result["grants"]!.AsArray());
        Assert.Single(result["engagements"]!.AsArray());
        Assert.Equal(engagementOne.Id, result["engagements"]![0]!["id"]!.GetValue<Guid>());
        var responses = result["engagements"]![0]!["evidenceRequests"]![0]!["responses"]!.AsArray();
        Assert.Null(responses[0]!["documentId"]);
        Assert.Equal(accessibleDocumentId, responses[1]!["documentId"]!.GetValue<Guid>());
        var sources = scoped.Metadata["sourceIds"]!.AsArray().Select(node => node!.GetValue<string>()).ToArray();
        Assert.DoesNotContain("document:" + inaccessibleDocumentId, sources);
        Assert.Contains("document:" + accessibleDocumentId, sources);
        Assert.False(mismatched.Success);
        Assert.Equal("accountant_grant_object_not_found", mismatched.ErrorCode);
    }

    [Fact]
    public async Task Prompt3_provenance_is_capped_without_hiding_the_complete_source_count()
    {
        var companyId = Guid.NewGuid();
        var obligations = Enumerable.Range(1, FinanceCloseComplianceAgentContract.MaximumPageSize)
            .Select(index => Obligation(companyId, index, evidenceCount: 20)).ToArray();
        var compliance = Proxy<IComplianceObligationService>((method, args) =>
        {
            if (method.Name != nameof(IComplianceObligationService.GetCalendarAsync)) return Unexpected(method);
            var query = (GetComplianceCalendarQuery)args![0]!;
            return Task.FromResult(new ComplianceCalendarDto(companyId, query.From, query.To, obligations.Length,
                0, 0, 0, obligations));
        });
        var service = Service(compliance: compliance);

        var response = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ReadComplianceObligations,
            ToolActionType.Read, companyId, ("from", JsonValue.Create("2026-01-01")),
            ("to", JsonValue.Create("2026-12-31")),
            ("take", JsonValue.Create(FinanceCloseComplianceAgentContract.MaximumPageSize))), default);

        Assert.True(response.Success);
        Assert.Equal(2_200, response.Metadata["sourceIdCount"]!.GetValue<int>());
        Assert.True(response.Metadata["sourceIdsTruncated"]!.GetValue<bool>());
        Assert.Equal(FinanceCloseComplianceAgentContract.MaximumSourceIds,
            response.Metadata["sourceIds"]!.AsArray().Count);
    }

    [Fact]
    public async Task Year_end_prerequisites_keep_technical_readiness_separate_from_pending_human_rollover_authority()
    {
        var companyId = Guid.NewGuid();
        var run = YearEndRun(companyId);
        var yearEnd = Proxy<IYearEndRolloverService>((method, _) => method.Name == nameof(IYearEndRolloverService.GetAsync)
            ? Task.FromResult(run)
            : Unexpected(method));
        var service = Service(yearEnd: yearEnd);

        var response = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.ExplainYearEndPrerequisites,
            ToolActionType.Recommend, companyId, ("runId", JsonValue.Create(run.Id.ToString()))), default);

        Assert.True(response.Success);
        var recommendation = response.Data["yearEndRecommendation"]!;
        Assert.Equal("year-end-hash-v3", recommendation["readinessHash"]!.GetValue<string>());
        Assert.True(recommendation["pendingHumanApproval"]!.GetValue<bool>());
        Assert.Single(recommendation["prerequisiteBlockers"]!.AsArray());
        Assert.False(recommendation["authority"]!["rolloverAllowed"]!.GetValue<bool>());
        Assert.False(recommendation["authority"]!["professionalApprovalAllowed"]!.GetValue<bool>());
        Assert.Contains("year_end_readiness_snapshot:" + run.CurrentReadiness!.Id,
            response.Metadata["sourceIds"]!.AsArray().Select(node => node!.GetValue<string>()));
    }

    private static FinanceCloseComplianceAgentService Service(
        IComplianceObligationService? compliance = null,
        IAuditPackageService? auditPackages = null,
        IAccountingCloseWorkspaceService? closeWorkspace = null,
        IAccountingCloseGovernanceService? closeGovernance = null,
        IAccountantCollaborationService? accountantCollaboration = null,
        IYearEndRolloverService? yearEnd = null) => new(
        Proxy<IAccountingCloseService>((m, _) => Unexpected(m)),
        closeWorkspace ?? Proxy<IAccountingCloseWorkspaceService>((m, _) => Unexpected(m)),
        closeGovernance ?? Proxy<IAccountingCloseGovernanceService>((m, _) => Unexpected(m)),
        Proxy<IAccountingReportingService>((m, _) => Unexpected(m)),
        compliance ?? Proxy<IComplianceObligationService>((m, _) => Unexpected(m)),
        auditPackages ?? Proxy<IAuditPackageService>((m, _) => Unexpected(m)),
        accountantCollaboration ?? Proxy<IAccountantCollaborationService>((m, _) => Unexpected(m)),
        yearEnd ?? Proxy<IYearEndRolloverService>((m, _) => Unexpected(m)));

    private static ComplianceObligationDto Obligation(Guid companyId, int index, int evidenceCount = 0)
    {
        var evidence = Enumerable.Range(0, evidenceCount).Select(item => new ComplianceEvidenceDto(
            Guid.NewGuid(), $"evidence:{index}:{item}", $"hash-{index}-{item}", Guid.NewGuid(), DateTime.UtcNow,
            "recorded", null, null)).ToArray();
        return new(Guid.NewGuid(), companyId, "se_vat_return", $"VAT return {index}", "SE",
            "sweden-statutory-candidate", "1.4.0", "definition-hash", "explicit",
            new DateOnly(2026, Math.Clamp(index, 1, 12), 14), Guid.NewGuid(), "prepared", "manual",
            Guid.NewGuid(), null, null, null, null, $"source-hash-{index}", null, null, index,
            DateTime.UtcNow, DateTime.UtcNow, [], [], evidence, [], [], [],
            "Recorded evidence is not authority acknowledgement.");
    }

    private static AuditPackageDto AuditPackage(Guid companyId, Guid packageId, string manifestChecksum,
        string packageChecksum, IReadOnlyList<AuditPackageVerificationDto> verifications) => new(
        packageId, companyId, Guid.NewGuid(), "August 2026", "period_close", "v1", "scope-hash", "{}",
        "final", true, manifestChecksum, packageChecksum, "audit.zip", "application/zip", 100,
        Guid.NewGuid(), null, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow, DateTime.UtcNow.AddDays(30),
        DateTime.UtcNow.AddDays(-1), 1, 3, false, null, null, 2, [], [], [], verifications);

    private static AccountantGrantDto Grant(Guid companyId, string name) => new(
        Guid.NewGuid(), companyId, "Company", Guid.NewGuid(), Guid.NewGuid(), name, "finance_review", true,
        true, true, "active", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow.AddDays(30), Guid.NewGuid(),
        Guid.NewGuid(), null, DateTime.UtcNow, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, 1);

    private static AccountantEngagementDto Engagement(Guid companyId, Guid grantId, string title,
        Guid inaccessibleDocumentId, Guid accessibleDocumentId)
    {
        var responses = new[]
        {
            new AccountantEvidenceResponseDto(Guid.NewGuid(), "Unavailable attachment", Guid.NewGuid(),
                inaccessibleDocumentId, false, DateTime.UtcNow),
            new AccountantEvidenceResponseDto(Guid.NewGuid(), "Authorized attachment", Guid.NewGuid(),
                accessibleDocumentId, true, DateTime.UtcNow)
        };
        var request = new AccountantEvidenceRequestDto(Guid.NewGuid(), "Provide evidence", "close_task", Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow.AddDays(2), "open", DateTime.UtcNow, DateTime.UtcNow,
            null, responses);
        return new(Guid.NewGuid(), companyId, "Company", grantId, Guid.NewGuid(), "August 2026", title,
            "period_close", Guid.NewGuid(), Guid.NewGuid(), "in_review", DateTime.UtcNow.AddDays(3),
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, 1, [], [request], [], []);
    }

    private static YearEndRunDto YearEndRun(Guid companyId)
    {
        var targetPeriodId = Guid.NewGuid();
        var check = new YearEndReadinessCheckDto("close_not_final", "Final close", false, true, 1,
            "Complete independent close review before rollover.", "fiscal_period", targetPeriodId,
            DateTime.UtcNow.AddHours(-2));
        var readiness = new YearEndReadinessSnapshotDto(Guid.NewGuid(), 3, "prepared", "year-end-hash-v3",
            "journal-cutoff-v3", 1, 12, Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), 3, [check]);
        return new(Guid.NewGuid(), companyId, "Company", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31),
            targetPeriodId, "January 2027", "A", "prepared", Guid.NewGuid(), null, null, null, null, null,
            null, null, null, null, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, null, null, null,
            3, readiness, null, [], [], [], [], ["refresh_readiness", "submit_for_review"]);
    }

    private static InternalToolExecutionRequest Request(string toolName, ToolActionType action, Guid companyId,
        params (string Key, JsonNode? Value)[] payload) => new(toolName,
        new(companyId, Guid.NewGuid(), Guid.NewGuid(), action, "finance"),
        payload.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HandlerProxy>();
        ((HandlerProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object Unexpected(MethodInfo method) => throw new InvalidOperationException("Unexpected call to " + method.Name);

    public class HandlerProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
