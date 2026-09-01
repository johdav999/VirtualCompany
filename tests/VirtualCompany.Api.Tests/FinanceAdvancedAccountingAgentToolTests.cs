using System.Reflection;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAdvancedAccountingAgentToolTests
{
    [Fact]
    public void Manifest_registers_bounded_read_and_recommendation_tools_without_execute_authority()
    {
        var registry = new StaticCompanyToolRegistry();
        var definitions = registry.ListToolDefinitions();
        var operations = FinanceAgentCoverageCatalogue.Manifests.SelectMany(x => x.Operations).ToArray();

        foreach (var toolName in FinanceAdvancedAccountingAgentToolIds.All)
        {
            var definition = Assert.Single(definitions, x => x.ToolName == toolName);
            var operation = Assert.Single(operations, x => x.ToolName == toolName);
            var expected = FinanceAdvancedAccountingAgentToolIds.ActionFor(toolName);
            Assert.Equal(expected, definition.ActionType);
            Assert.False(definition.SensitiveAction);
            Assert.Equal(FinancePermissions.View, operation.RequiredPermission);
            Assert.Equal(expected == ToolActionType.Read
                ? FinanceAgentCoverageSupportStates.ImplementedRead
                : FinanceAgentCoverageSupportStates.ImplementedRecommendDraft, operation.SupportState);
            Assert.Contains(definition.SelectionMetadata!.NaturalLanguageExamples, example => !string.IsNullOrWhiteSpace(example));
        }

        Assert.DoesNotContain(definitions, x => FinanceAdvancedAccountingAgentToolIds.Contains(x.ToolName) &&
                                               x.ActionType == ToolActionType.Execute);
        foreach (var definition in definitions.Where(x => FinanceAdvancedAccountingAgentToolIds.Contains(x.ToolName) &&
                                                          x.ToolName != FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary))
        {
            var take = definition.InputSchema["properties"]?["take"];
            if (take is not null)
                Assert.Equal(FinanceAdvancedAccountingAgentContract.MaximumPageSize, take["maximum"]!.GetValue<int>());
        }
        Assert.Contains(operations, x => x.ToolName == FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary &&
                                         x.SourceTypes.Contains("unsupported_inventory_accounting_boundary"));
    }

    [Fact]
    public async Task Reconciliation_exception_preserves_owning_confidence_and_evidence_without_applying_match()
    {
        var companyId = Guid.NewGuid();
        var group = ReconciliationGroup(companyId, 0.73m, stale: true);
        var read = Proxy<IAdvancedReconciliationReadService>((method, args) =>
        {
            Assert.Equal(nameof(IAdvancedReconciliationReadService.GetAsync), method.Name);
            Assert.Equal(companyId, ((GetAdvancedReconciliationGroupQuery)args![0]!).CompanyId);
            return Task.FromResult<AdvancedReconciliationGroupDetailDto?>(group);
        });
        var service = Service(reconciliation: read);

        var response = await service.ExecuteAsync(Request(
            FinanceAdvancedAccountingAgentToolIds.RecommendReconciliationReview, ToolActionType.Recommend, companyId,
            ("groupId", JsonValue.Create(group.Summary.Id.ToString()))), default);

        Assert.True(response.Success);
        var recommendation = response.Data["reconciliationRecommendation"]!;
        Assert.Equal(0.73m, recommendation["confidenceScore"]!.GetValue<decimal>());
        Assert.True(recommendation["isStale"]!.GetValue<bool>());
        Assert.True(recommendation["reviewRequired"]!.GetValue<bool>());
        Assert.False(recommendation["matchApplied"]!.GetValue<bool>());
        Assert.Equal("reference_match", recommendation["reasonContributions"]![0]!["featureKey"]!.GetValue<string>());
        Assert.Contains("reconciliation_group:" + group.Summary.Id,
            response.Metadata["sourceIds"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public async Task Stale_or_unapproved_rate_evidence_requires_review_and_never_fabricates_rate_or_amount()
    {
        var companyId = Guid.NewGuid();
        var source = new ExchangeRateSourceResult(Guid.NewGuid(), "riksbank", "Riksbank", "provider", "v1", 1,
            true, 2, 24, "Public rates", true, DateTime.UtcNow.AddDays(-10), null, null, null, 4);
        var set = new ExchangeRateSetResult(Guid.NewGuid(), source.Id, source.SourceKey, 7, "set-7", "evidence-hash",
            "pending_review", new DateOnly(2026, 8, 31), new DateOnly(2026, 8, 31), DateTime.UtcNow.AddDays(-10),
            Guid.NewGuid(), null, null, null, null, 2, 14);
        var readiness = new ExchangeRateReadinessResult("review_required", "SEK", 2, 1, 1, 0,
            DateTime.UtcNow.AddDays(-10), [new("exchange_rate_pending_approval", "Approval is pending.", "warning")], [source]);
        var rates = Proxy<IExchangeRateService>((method, _) => method.Name switch
        {
            nameof(IExchangeRateService.GetReadinessAsync) => Task.FromResult(readiness),
            nameof(IExchangeRateService.GetSetsAsync) => Task.FromResult<IReadOnlyList<ExchangeRateSetResult>>([set]),
            _ => Unexpected(method)
        });
        var runs = Proxy<ICurrencyRevaluationService>((method, args) =>
        {
            Assert.Equal(nameof(ICurrencyRevaluationService.ListAsync), method.Name);
            Assert.Equal(companyId, ((ListCurrencyRevaluationRunsQuery)args![0]!).CompanyId);
            return Task.FromResult(new CurrencyRevaluationRunListDto([], 0, 0, 100));
        });
        var service = Service(exchangeRates: rates, revaluation: runs);

        var response = await service.ExecuteAsync(Request(
            FinanceAdvancedAccountingAgentToolIds.RecommendRateEvidenceRemediation, ToolActionType.Recommend, companyId), default);

        Assert.True(response.Success);
        var recommendation = response.Data["rateEvidenceRecommendation"]!;
        Assert.True(recommendation["reviewRequired"]!.GetValue<bool>());
        Assert.Null(recommendation["inventedRate"]);
        Assert.Null(recommendation["inventedAmount"]);
        Assert.Equal("pending_review", recommendation["reviewRateSets"]![0]!["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fixed_asset_depreciation_preview_is_returned_exactly_from_owning_service_and_range_is_bounded()
    {
        var companyId = Guid.NewGuid();
        var preview = new FixedAssetDepreciationPreviewDto(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31),
            123.45m, "population-hash-v4",
            [new(Guid.NewGuid(), "A-100", "Laptop", 8, 123.45m, 12_000m, 9_000m, 31, 31,
                "straight_line", "Owning calculator result.", "preview")]);
        var assets = Proxy<IFixedAssetService>((method, args) => method.Name switch
        {
            nameof(IFixedAssetService.ListAsync) => Task.FromResult(new FixedAssetListDto([], 0, 0, 100,
                0, 0, 0, 0, 0)),
            nameof(IFixedAssetService.PreviewDepreciationAsync) =>
                AssertPreviewCompany(args, companyId, preview),
            _ => Unexpected(method)
        });
        var service = Service(fixedAssets: assets);

        var response = await service.ExecuteAsync(Request(FinanceAdvancedAccountingAgentToolIds.ReadFixedAssets,
            ToolActionType.Read, companyId, ("periodStart", JsonValue.Create("2026-08-01")),
            ("periodEnd", JsonValue.Create("2026-08-31"))), default);

        Assert.True(response.Success);
        var result = response.Data["fixedAssets"]!;
        Assert.Equal(123.45m, result["depreciationPreview"]!["totalAmount"]!.GetValue<decimal>());
        Assert.Equal("population-hash-v4", result["depreciationPreview"]!["populationHash"]!.GetValue<string>());
        Assert.Equal("owning_fixed_asset_service", result["calculationSource"]!.GetValue<string>());

        var rejected = await service.ExecuteAsync(Request(FinanceAdvancedAccountingAgentToolIds.ReadFixedAssets,
            ToolActionType.Read, companyId, ("periodStart", JsonValue.Create("2025-01-01")),
            ("periodEnd", JsonValue.Create("2026-12-31"))), default);
        Assert.False(rejected.Success);
        Assert.Equal("advanced_accounting_request_invalid", rejected.ErrorCode);
    }

    [Fact]
    public async Task Subledger_read_is_tenant_scoped_and_preserves_settlement_currency_version_and_rate_provenance()
    {
        var companyId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        var allocation = new FinancePaymentAllocationDto(Guid.NewGuid(), companyId, Guid.NewGuid(), invoiceId, null,
            100m, "EUR", DateTime.UtcNow, DateTime.UtcNow, null, null, null,
            AllocatedPaymentAmount: 100m, PaymentCurrency: "EUR", FunctionalCurrency: "SEK",
            AllocatedFunctionalAmount: 1_120m, SettlementRateDate: new DateOnly(2026, 8, 31), SettlementRate: 11.2m,
            SettlementExchangeRateConversionId: Guid.NewGuid(), SettlementRateIdentity: "riksbank:set-7:obs-2",
            SettlementStatus: "settled", Version: 9);
        var payments = Proxy<IFinancePaymentReadService>((method, args) =>
        {
            Assert.Equal(nameof(IFinancePaymentReadService.GetAllocationsByInvoiceAsync), method.Name);
            var query = (GetFinanceInvoiceAllocationsQuery)args![0]!;
            Assert.Equal(companyId, query.CompanyId);
            Assert.Equal(invoiceId, query.InvoiceId);
            return Task.FromResult<IReadOnlyList<FinancePaymentAllocationDto>>([allocation]);
        });
        var service = Service(paymentReads: payments);

        var response = await service.ExecuteAsync(Request(FinanceAdvancedAccountingAgentToolIds.ReadSubledgerSettlement,
            ToolActionType.Read, companyId, ("invoiceId", JsonValue.Create(invoiceId.ToString()))), default);

        Assert.True(response.Success);
        var item = response.Data["subledgerSettlement"]![0]!;
        Assert.Equal("EUR", item["currency"]!.GetValue<string>());
        Assert.Equal("SEK", item["functionalCurrency"]!.GetValue<string>());
        Assert.Equal(11.2m, item["settlementRate"]!.GetValue<decimal>());
        Assert.Equal(9, item["version"]!.GetValue<long>());
        Assert.Contains("exchange_rate_conversion:" + allocation.SettlementExchangeRateConversionId,
            response.Metadata["sourceIds"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public async Task Inventory_question_returns_explicit_unsupported_boundary_not_commerce_data()
    {
        var response = await Service().ExecuteAsync(Request(
            FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary, ToolActionType.Read, Guid.NewGuid()), default);

        Assert.True(response.Success);
        var boundary = response.Data["inventoryBoundary"]!;
        Assert.False(boundary["supported"]!.GetValue<bool>());
        Assert.False(boundary["quantityAccountingSupported"]!.GetValue<bool>());
        Assert.False(boundary["valuationSupported"]!.GetValue<bool>());
        Assert.False(boundary["cogsAccountingSupported"]!.GetValue<bool>());
        Assert.Equal("inventory_accounting_unsupported", boundary["reasonCode"]!.GetValue<string>());
        Assert.Contains("Commerce records must not", boundary["explanation"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Payment_execution_read_returns_evidence_but_redacts_bank_authorization_uri()
    {
        var companyId = Guid.NewGuid();
        var execution = PaymentExecution(companyId);
        var executions = Proxy<IPaymentBatchExecutionService>((method, args) =>
        {
            Assert.Equal(nameof(IPaymentBatchExecutionService.GetAsync), method.Name);
            var query = (GetPaymentBatchExecutionQuery)args![0]!;
            Assert.Equal(companyId, query.CompanyId);
            Assert.Equal(execution.Id, query.ExecutionId);
            return Task.FromResult<PaymentBatchExecutionDto?>(execution);
        });
        var service = Service(paymentExecutions: executions);

        var response = await service.ExecuteAsync(Request(FinanceAdvancedAccountingAgentToolIds.ReadPaymentBatches,
            ToolActionType.Read, companyId, ("executionId", JsonValue.Create(execution.Id.ToString()))), default);

        Assert.True(response.Success);
        var value = response.Data["paymentBatches"]!["execution"]!;
        Assert.Null(value["authorizationUri"]);
        Assert.Equal("request-hash", value["requestHash"]!.GetValue<string>());
        Assert.Equal("pending_authorization", value["status"]!.GetValue<string>());
        Assert.DoesNotContain("https://bank.example/authorize", response.Data["paymentBatches"]!.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_and_recommendation_tools_reject_execute_or_wrong_action_classes()
    {
        var service = Service();
        var read = await service.ExecuteAsync(Request(FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary,
            ToolActionType.Execute, Guid.NewGuid()), default);
        var recommend = await service.ExecuteAsync(Request(FinanceAdvancedAccountingAgentToolIds.RecommendReconciliationReview,
            ToolActionType.Read, Guid.NewGuid(), ("groupId", JsonValue.Create(Guid.NewGuid().ToString()))), default);

        Assert.False(read.Success);
        Assert.Equal("advanced_accounting_action_mismatch", read.ErrorCode);
        Assert.False(recommend.Success);
        Assert.Equal("advanced_accounting_action_mismatch", recommend.ErrorCode);
    }

    [Fact]
    public async Task Internal_tool_contract_routes_prompt4_tools_to_the_advanced_accounting_service()
    {
        var called = false;
        var advanced = Proxy<IFinanceAdvancedAccountingAgentService>((method, args) =>
        {
            Assert.Equal(nameof(IFinanceAdvancedAccountingAgentService.ExecuteAsync), method.Name);
            var request = (InternalToolExecutionRequest)args![0]!;
            Assert.Equal(FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary, request.ToolName);
            called = true;
            return Task.FromResult(InternalToolExecutionResponse.Succeeded("routed", new(), new()));
        });
        var contract = new InternalCompanyToolContract(
            Proxy<ICompanyTaskQueryService>((m, _) => Unexpected(m)),
            Proxy<ICompanyTaskCommandService>((m, _) => Unexpected(m)),
            Proxy<IProactiveTaskCreationService>((m, _) => Unexpected(m)),
            Proxy<IApprovalRequestService>((m, _) => Unexpected(m)),
            Proxy<ICompanyKnowledgeSearchService>((m, _) => Unexpected(m)),
            Proxy<IFinanceToolProvider>((m, _) => Unexpected(m)),
            Proxy<IFinanceTransactionAnomalyDetectionService>((m, _) => Unexpected(m)),
            Proxy<IFinanceAgentAnalysisService>((m, _) => Unexpected(m)),
            Proxy<IFinanceLedgerAgentReadService>((m, _) => Unexpected(m)),
            Proxy<IFinanceCloseComplianceAgentService>((m, _) => Unexpected(m)),
            advanced,
            Proxy<IFinanceAccountingDraftAgentService>((m, _) => Unexpected(m)),
            Proxy<IFinanceOperationalProposalAgentService>((m, _) => Unexpected(m)),
            Proxy<IFinanceGuardedCommandService>((m, _) => Unexpected(m)),
            Proxy<IAccountingProviderSwitchAgentService>((m, _) => Unexpected(m)),
            Proxy<ILeadGenerationService>((m, _) => Unexpected(m)));

        var response = await contract.ExecuteAsync(Request(FinanceAdvancedAccountingAgentToolIds.ReadInventoryBoundary,
            ToolActionType.Read, Guid.NewGuid()), default);

        Assert.True(response.Success);
        Assert.True(called);
    }

    private static Task<FixedAssetDepreciationPreviewDto> AssertPreviewCompany(object?[]? args, Guid companyId,
        FixedAssetDepreciationPreviewDto preview)
    {
        Assert.Equal(companyId, ((PreviewFixedAssetDepreciationQuery)args![0]!).CompanyId);
        return Task.FromResult(preview);
    }

    private static AdvancedReconciliationGroupDetailDto ReconciliationGroup(Guid companyId, decimal confidence, bool stale)
    {
        var summary = new AdvancedReconciliationGroupSummaryDto(Guid.NewGuid(), "BANK-42", "Supplier AB", "SEK",
            1_000m, confidence, "needs_review", "one_to_many", 1, 1, 1, 3, 7, true, stale, DateTime.UtcNow);
        var node = new AdvancedReconciliationNodeDto(Guid.NewGuid(), "bank_transaction", Guid.NewGuid(), "Bank row",
            "BANK-42", "SEK", 1_000m, "credit", null, 0, 0, "version-4", 1);
        var reason = new AdvancedReconciliationReasonContributionDto("reference_match", 0.45m,
            "References match after deterministic normalization.", "BANK-42 == BANK-42");
        return new(summary, 1_000m, 0, 0, 0, 0, true, "Human review required by policy", [node], [], [reason], [], []);
    }

    private static PaymentBatchExecutionDto PaymentExecution(Guid companyId) => new(
        Guid.NewGuid(), Guid.NewGuid(), "BATCH-42", 3, 8, "bank-provider", "Bank Provider", Guid.NewGuid(),
        "Bank", Guid.NewGuid(), "Operating account", "****1234", "pending_authorization", null,
        new Uri("https://bank.example/authorize"), null, "request-hash", "business-key-v3", true, true,
        null, null, DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow, null, null, null, [], [], [], [], null,
        new(true, true, true, false, false, false, null, "Human authorization is required."));

    private static FinanceAdvancedAccountingAgentService Service(
        IAdvancedReconciliationReadService? reconciliation = null,
        IFinancePaymentReadService? paymentReads = null,
        IExchangeRateService? exchangeRates = null,
        ICurrencyRevaluationService? revaluation = null,
        IPaymentBatchExecutionService? paymentExecutions = null,
        IFixedAssetService? fixedAssets = null) => new(
        Proxy<IBankStatementImportCenterService>((m, _) => Unexpected(m)),
        reconciliation ?? Proxy<IAdvancedReconciliationReadService>((m, _) => Unexpected(m)),
        Proxy<IBankTransactionReadService>((m, _) => Unexpected(m)),
        paymentReads ?? Proxy<IFinancePaymentReadService>((m, _) => Unexpected(m)),
        Proxy<IFinanceReadService>((m, _) => Unexpected(m)),
        Proxy<IPaymentBatchService>((m, _) => Unexpected(m)),
        paymentExecutions ?? Proxy<IPaymentBatchExecutionService>((m, _) => Unexpected(m)),
        exchangeRates ?? Proxy<IExchangeRateService>((m, _) => Unexpected(m)),
        revaluation ?? Proxy<ICurrencyRevaluationService>((m, _) => Unexpected(m)),
        Proxy<IAccountingDimensionService>((m, _) => Unexpected(m)),
        Proxy<IAccountingScheduleService>((m, _) => Unexpected(m)),
        fixedAssets ?? Proxy<IFixedAssetService>((m, _) => Unexpected(m)));

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
