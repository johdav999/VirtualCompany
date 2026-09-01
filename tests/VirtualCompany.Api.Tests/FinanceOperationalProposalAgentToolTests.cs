using System.Reflection;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceOperationalProposalAgentToolTests
{
    [Fact]
    public void Manifest_registers_nine_proposals_and_only_four_guarded_non_final_execute_tools()
    {
        var registry = new StaticCompanyToolRegistry();
        var definitions = registry.ListToolDefinitions();
        var operations = FinanceAgentCoverageCatalogue.Manifests.SelectMany(x => x.Operations).ToArray();

        Assert.Equal(9, FinanceOperationalProposalAgentToolIds.RecommendationTools.Count);
        Assert.Equal(4, FinanceOperationalProposalAgentToolIds.ExecuteTools.Count);
        foreach (var tool in FinanceOperationalProposalAgentToolIds.All)
        {
            var definition = Assert.Single(definitions, x => x.ToolName == tool);
            var expected = FinanceOperationalProposalAgentToolIds.ExecuteTools.Contains(tool)
                ? ToolActionType.Execute : ToolActionType.Recommend;
            Assert.Equal(expected, definition.ActionType);
            Assert.Equal(expected == ToolActionType.Execute, definition.SensitiveAction);
            Assert.Contains(definition.SelectionMetadata!.NaturalLanguageExamples,
                example => !string.IsNullOrWhiteSpace(example));
            var operation = Assert.Single(operations, x => x.ToolName == tool);
            Assert.Equal(FinancePermissions.AccountingAdmin, operation.RequiredPermission);
            Assert.Contains(FinancePermissions.AccountingAdmin,
                FinanceAgentAuthorizationService.ResolveRequirements(tool, expected).Permissions);
        }

        Assert.DoesNotContain(FinanceOperationalProposalAgentToolIds.ExecuteTools,
            x => x.Contains("post", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(FinanceOperationalProposalAgentToolIds.ExecuteTools,
            x => x.Contains("sign_off", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(FinanceOperationalProposalAgentToolIds.ExecuteTools,
            x => x.Contains("file", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(FinanceOperationalProposalAgentToolIds.ExecuteTools,
            x => x.Contains("close_period", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Audit_preview_uses_owning_definition_and_never_requests_or_returns_an_artifact()
    {
        var companyId = Guid.NewGuid(); var periodId = Guid.NewGuid();
        var preview = new AuditPackagePreviewDto(companyId, periodId, "August 2026", "period_close",
            "audit-package-v1", "scope-hash", "{\"ledger\":\"v9\"}", true, [], null, null, null, false);
        var audit = Proxy<IAuditPackageService>((method, args) => method.Name switch
        {
            nameof(IAuditPackageService.PreviewAsync) => Task.FromResult(AssertAuditPreview(args, companyId, periodId, preview)),
            _ => Unexpected(method)
        });
        var response = await Service(audit: audit).ExecuteAsync(Request(
            FinanceOperationalProposalAgentToolIds.PreviewAuditPackage, ToolActionType.Recommend, companyId,
            ("fiscalPeriodId", JsonValue.Create(periodId))), default);

        Assert.True(response.Success);
        Assert.False(response.Data["auditPackagePreview"]!["artifactGenerated"]!.GetValue<bool>());
        Assert.False(response.Metadata["downloadAuthorized"]!.GetValue<bool>());
        Assert.False(response.Metadata["posted"]!.GetValue<bool>());
        Assert.Equal("audit_package", response.Data["operationalProposal"]!["proposalKind"]!.GetValue<string>());
    }

    [Fact]
    public async Task Depreciation_proposal_preserves_deterministic_population_and_remains_unposted()
    {
        var companyId = Guid.NewGuid(); var assetId = Guid.NewGuid(); var periodId = Guid.NewGuid();
        var preview = new FixedAssetDepreciationPreviewDto(new(2026, 8, 1), new(2026, 8, 31), 125m,
            "population-sha256", [new(assetId, "FA-1", "Computer", 7, 125m, 3000m, 2875m,
                31, 31, "straight_line", "Owning calculator result", "ready")]);
        var assets = Proxy<IFixedAssetService>((method, args) => method.Name switch
        {
            nameof(IFixedAssetService.PreviewDepreciationAsync) => Task.FromResult(AssertDepreciation(args, companyId, preview)),
            _ => Unexpected(method)
        });
        var response = await Service(assets: assets).ExecuteAsync(Request(
            FinanceOperationalProposalAgentToolIds.PreviewFixedAssetDepreciation, ToolActionType.Recommend, companyId,
            ("fiscalPeriodId", JsonValue.Create(periodId)), ("periodStart", JsonValue.Create("2026-08-01")),
            ("periodEnd", JsonValue.Create("2026-08-31"))), default);

        Assert.True(response.Success);
        Assert.Equal(125m, response.Data["assetDepreciationPreview"]!["totalAmount"]!.GetValue<decimal>());
        Assert.Equal("population-sha256", response.Data["assetDepreciationPreview"]!["populationHash"]!.GetValue<string>());
        Assert.False(response.Data["operationalProposal"]!["posted"]!.GetValue<bool>());
        Assert.Contains("posting_remain", response.Data["operationalProposal"]!["expectedDownstreamEffects"]![0]!.GetValue<string>());
    }

    [Fact]
    public async Task Current_compliance_evidence_proposal_creates_typed_task_without_marking_evidence_complete()
    {
        var companyId = Guid.NewGuid(); var obligationId = Guid.NewGuid(); var actorId = Guid.NewGuid();
        var obligation = Obligation(companyId, obligationId);
        var compliance = Proxy<IComplianceObligationService>((method, args) => method.Name switch
        {
            nameof(IComplianceObligationService.GetAsync) => Task.FromResult(AssertCompliance(args, companyId, obligationId, obligation)),
            _ => Unexpected(method)
        });
        CreateAgentInitiatedTaskCommand? created = null;
        var tasks = Proxy<IProactiveTaskCreationService>((method, args) => method.Name switch
        {
            nameof(IProactiveTaskCreationService.CreateAsync) => Task.FromResult(CaptureTask(args, command => created = command, companyId)),
            _ => Unexpected(method)
        });
        var service = Service(compliance: compliance, tasks: tasks);
        var common = new (string, JsonNode?)[]
        {
            ("scopeType", JsonValue.Create("compliance_obligation")), ("targetId", JsonValue.Create(obligationId)),
            ("title", JsonValue.Create("Collect VAT evidence")), ("description", JsonValue.Create("Attach the source report and reviewer evidence."))
        };
        var proposal = await service.ExecuteAsync(Request(FinanceOperationalProposalAgentToolIds.ProposeEvidenceRequest,
            ToolActionType.Recommend, companyId, actorId, common), default);
        var hash = proposal.Data["operationalProposal"]!["proposalHash"]!.GetValue<string>();
        var executePayload = common.Concat(new (string, JsonNode?)[]
        {
            ("expectedProposalHash", JsonValue.Create(hash)), ("reviewed", JsonValue.Create(true))
        }).ToArray();
        var executed = await service.ExecuteAsync(Request(FinanceOperationalProposalAgentToolIds.RequestEvidence,
            ToolActionType.Execute, companyId, actorId, executePayload), default);

        Assert.True(executed.Success);
        Assert.NotNull(executed.Data["proposalExecution"]);
        Assert.NotNull(created);
        Assert.Equal("finance_evidence_request", created!.Trigger.TaskType);
        Assert.Equal("compliance_obligation", created.Trigger.Payload!["scopeType"]!.GetValue<string>());
        Assert.False(executed.Metadata["evidenceCompleted"]!.GetValue<bool>());
        Assert.False(executed.Metadata["statutoryConclusion"]!.GetValue<bool>());
    }

    private static AuditPackagePreviewDto AssertAuditPreview(object?[]? args, Guid companyId, Guid periodId,
        AuditPackagePreviewDto preview)
    {
        var query = Assert.IsType<PreviewAuditPackageQuery>(args![0]);
        Assert.Equal(companyId, query.CompanyId); Assert.Equal(periodId, query.FiscalPeriodId); return preview;
    }

    private static FixedAssetDepreciationPreviewDto AssertDepreciation(object?[]? args, Guid companyId,
        FixedAssetDepreciationPreviewDto preview)
    {
        var query = Assert.IsType<PreviewFixedAssetDepreciationQuery>(args![0]);
        Assert.Equal(companyId, query.CompanyId); return preview;
    }

    private static ComplianceObligationDto AssertCompliance(object?[]? args, Guid companyId, Guid obligationId,
        ComplianceObligationDto obligation)
    {
        Assert.Equal(companyId, args![0]); Assert.Equal(obligationId, args[1]); return obligation;
    }

    private static CreateAgentInitiatedTaskResult CaptureTask(object?[]? args,
        Action<CreateAgentInitiatedTaskCommand> capture, Guid companyId)
    {
        var command = Assert.IsType<CreateAgentInitiatedTaskCommand>(args![0]); capture(command);
        return new(Guid.NewGuid(), companyId, true, false, "open", command.Trigger.CorrelationId);
    }

    private static ComplianceObligationDto Obligation(Guid companyId, Guid id) => new(id, companyId,
        "vat_return", "VAT return", "SE", "se-bas", "v1", "definition-hash", "monthly", new(2026, 9, 26),
        Guid.NewGuid(), "preparing", "manual", Guid.NewGuid(), null, null, null, null, "source-hash",
        null, null, 7, DateTime.UtcNow, DateTime.UtcNow,
        [new("source_report", "VAT source report", false, null)], [], [], [], [], ["prepare"]);

    private static FinanceOperationalProposalAgentService Service(
        IComplianceObligationService? compliance = null, IAuditPackageService? audit = null,
        IFixedAssetService? assets = null, IProactiveTaskCreationService? tasks = null) => new(
        Proxy<IAccountingCloseService>((m, _) => Unexpected(m)),
        compliance ?? Proxy<IComplianceObligationService>((m, _) => Unexpected(m)),
        audit ?? Proxy<IAuditPackageService>((m, _) => Unexpected(m)),
        Proxy<IAccountingScheduleService>((m, _) => Unexpected(m)),
        Proxy<ICurrencyRevaluationService>((m, _) => Unexpected(m)),
        assets ?? Proxy<IFixedAssetService>((m, _) => Unexpected(m)),
        tasks ?? Proxy<IProactiveTaskCreationService>((m, _) => Unexpected(m)),
        Proxy<IAgentHandoffService>((m, _) => Unexpected(m)));

    private static InternalToolExecutionRequest Request(string tool, ToolActionType action, Guid companyId,
        params (string Key, JsonNode? Value)[] payload) => Request(tool, action, companyId, Guid.NewGuid(), payload);
    private static InternalToolExecutionRequest Request(string tool, ToolActionType action, Guid companyId,
        Guid actorId, params (string Key, JsonNode? Value)[] payload) => new(tool,
        new(companyId, Guid.NewGuid(), Guid.NewGuid(), action, "finance", CorrelationId: "proposal-plan-6", ActorUserId: actorId),
        payload.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase));

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, HandlerProxy>(); ((HandlerProxy)(object)proxy).Handler = handler; return proxy;
    }
    private static object Unexpected(MethodInfo method) => throw new InvalidOperationException("Unexpected call to " + method.Name);
    public class HandlerProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
