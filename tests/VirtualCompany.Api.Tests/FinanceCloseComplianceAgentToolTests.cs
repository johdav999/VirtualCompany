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
        var response = await service.ExecuteAsync(Request(FinanceCloseComplianceAgentToolIds.PrioritizeCloseBlockers,
            ToolActionType.Read, Guid.NewGuid(), ("closeInstanceId", JsonValue.Create(Guid.NewGuid().ToString()))), default);
        Assert.False(response.Success);
        Assert.Equal("close_compliance_action_mismatch", response.ErrorCode);
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

    private static FinanceCloseComplianceAgentService Service(
        IComplianceObligationService? compliance = null,
        IAuditPackageService? auditPackages = null,
        IAccountingCloseWorkspaceService? closeWorkspace = null) => new(
        Proxy<IAccountingCloseService>((m, _) => Unexpected(m)),
        closeWorkspace ?? Proxy<IAccountingCloseWorkspaceService>((m, _) => Unexpected(m)),
        Proxy<IAccountingCloseGovernanceService>((m, _) => Unexpected(m)),
        Proxy<IAccountingReportingService>((m, _) => Unexpected(m)),
        compliance ?? Proxy<IComplianceObligationService>((m, _) => Unexpected(m)),
        auditPackages ?? Proxy<IAuditPackageService>((m, _) => Unexpected(m)),
        Proxy<IAccountantCollaborationService>((m, _) => Unexpected(m)),
        Proxy<IYearEndRolloverService>((m, _) => Unexpected(m)));

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
