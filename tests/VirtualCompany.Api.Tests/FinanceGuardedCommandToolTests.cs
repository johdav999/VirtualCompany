using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Shared;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceGuardedCommandToolTests
{
    [Fact]
    public void Every_finance_execute_tool_has_one_complete_consistent_readiness_contract()
    {
        var registry = new StaticCompanyToolRegistry();
        var executeTools = registry.ListToolDefinitions()
            .Where(definition => definition.ActionType == ToolActionType.Execute &&
                                 registry.TryGetTool(definition.ToolName, out var registration) &&
                                 registration.Scopes.Contains("finance"))
            .ToArray();

        Assert.Equal(18, executeTools.Length);
        Assert.Equal(executeTools.Length, FinanceExecuteToolReadinessCatalog.All.Count);
        foreach (var definition in executeTools)
        {
            var registration = AssertRegistration(registry, definition.ToolName);
            var readiness = Assert.IsType<FinanceExecuteToolReadinessContract>(registration.FinanceExecuteReadiness);
            var risk = Assert.IsType<FinanceToolRiskClassification>(registration.FinanceRiskClassification);
            Assert.Equal(FinanceGuardedCommandContract.Version, readiness.ContractVersion);
            Assert.Equal(risk.RiskTier, readiness.RiskTier);
            Assert.Equal(risk.Reversibility, readiness.Reversibility);
            Assert.Equal(risk.RequiredActorPermission, readiness.RequiredActorPermission);
            Assert.Equal(risk.DefaultApprovalBehavior, readiness.ApprovalBehavior);
            Assert.False(string.IsNullOrWhiteSpace(readiness.OwningApplicationContract));
            Assert.False(string.IsNullOrWhiteSpace(readiness.TargetContract));
            Assert.False(string.IsNullOrWhiteSpace(readiness.VersionContract));
            Assert.False(string.IsNullOrWhiteSpace(readiness.IdempotencyContract));
            Assert.False(string.IsNullOrWhiteSpace(readiness.TransactionalBehavior));
            Assert.False(string.IsNullOrWhiteSpace(readiness.RetryBehavior));
            Assert.False(string.IsNullOrWhiteSpace(readiness.ReconciliationBehavior));
            Assert.False(string.IsNullOrWhiteSpace(readiness.AuditBehavior));
            Assert.False(string.IsNullOrWhiteSpace(readiness.RollbackOrRecoveryBehavior));
            Assert.False(string.IsNullOrWhiteSpace(readiness.AfterStateReadContract));
            Assert.InRange(readiness.MaximumBatchSize, 1, FinanceGuardedCommandContract.MaximumCategorizationBatchSize);
            Assert.NotEmpty(readiness.RequiredRequestFields);
            var schemaRequired = definition.InputSchema["required"]!.AsArray()
                .Select(node => node!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            Assert.True(readiness.RequiredRequestFields.ToHashSet(StringComparer.Ordinal).SetEquals(schemaRequired),
                $"Readiness required fields differ from the closed schema for {definition.ToolName}.");
        }

        Assert.DoesNotContain(registry.ListTools(), tool =>
            tool.ToolName.Contains("sql", StringComparison.OrdinalIgnoreCase) ||
            tool.ToolName.Contains("credential", StringComparison.OrdinalIgnoreCase) ||
            tool.ToolName.Contains("statutory_signoff", StringComparison.OrdinalIgnoreCase) ||
            tool.ToolName.Contains("final_close", StringComparison.OrdinalIgnoreCase) ||
            tool.ToolName.Contains("generic_command", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Bounded_batch_manifest_is_closed_risk_classified_and_requires_edit_authority()
    {
        var registry = new StaticCompanyToolRegistry();
        var registration = AssertRegistration(registry, FinanceGuardedCommandToolIds.CategorizeTransactions);
        Assert.True(registration.SensitiveAction);
        Assert.Equal(FinanceToolApprovalBehaviors.ReviewUnlessBoundedCategorizationException,
            registration.FinanceRiskClassification!.DefaultApprovalBehavior);
        Assert.Equal(FinancePermissions.Edit,
            FinanceAgentAuthorizationService.ResolveRequirements(FinanceGuardedCommandToolIds.CategorizeTransactions,
                ToolActionType.Execute).Permissions.Last());

        Assert.True(registry.TryGetToolDefinition(FinanceGuardedCommandToolIds.CategorizeTransactions, out var definition));
        Assert.False(definition.InputSchema["additionalProperties"]!.GetValue<bool>());
        var items = definition.InputSchema["properties"]!["items"]!;
        Assert.Equal(FinanceGuardedCommandContract.MaximumCategorizationBatchSize,
            items["maxItems"]!.GetValue<int>());
        Assert.False(items["items"]!["additionalProperties"]!.GetValue<bool>());
        Assert.Contains("categorizationBatch", definition.OutputSchema.ToJsonString());
    }

    [Fact]
    public async Task Mixed_and_replayed_batch_mutates_only_current_eligible_items_and_reports_every_decision()
    {
        var companyId = Guid.NewGuid();
        var validId = Guid.NewGuid();
        var staleId = Guid.NewGuid();
        var missingId = Guid.NewGuid();
        var categories = new Dictionary<Guid, string>
        {
            [validId] = "uncategorized",
            [staleId] = "income"
        };
        var commandCalls = 0;
        var reads = Proxy<IFinanceReadService>((method, args) =>
        {
            Assert.Equal(nameof(IFinanceReadService.GetTransactionDetailAsync), method.Name);
            var query = Assert.IsType<GetFinanceTransactionDetailQuery>(args![0]);
            Assert.Equal(companyId, query.CompanyId);
            return Task.FromResult(categories.TryGetValue(query.TransactionId, out var category)
                ? Detail(query.TransactionId, category)
                : null);
        });
        var commands = Proxy<IFinanceCommandService>((method, args) =>
        {
            Assert.Equal(nameof(IFinanceCommandService.UpdateTransactionCategoryAsync), method.Name);
            var command = Assert.IsType<UpdateFinanceTransactionCategoryCommand>(args![0]);
            Assert.Equal(companyId, command.CompanyId);
            commandCalls++;
            categories[command.TransactionId] = command.Category;
            return Task.FromResult(Transaction(command.TransactionId, command.Category));
        });
        var service = new FinanceGuardedCommandService(reads, commands);
        var request = new CategorizeTransactionsGuardedCommand(companyId, Guid.NewGuid(), Guid.NewGuid(),
            "batch-categories-20260901",
            [
                new(validId, "uncategorized", "office_supplies"),
                new(staleId, "uncategorized", "office_supplies"),
                new(missingId, "uncategorized", "office_supplies")
            ], "corr-batch");

        var first = await service.CategorizeTransactionsAsync(request, default);

        Assert.Equal(3, first.RequestedCount);
        Assert.Equal(1, first.MutatedCount);
        Assert.Equal(2, first.RejectedCount);
        Assert.True(first.PartiallyApplied);
        Assert.Equal(["applied", "rejected", "rejected"], first.Items.Select(item => item.Outcome).ToArray());
        Assert.Equal("transaction_state_stale", first.Items[1].ReasonCode);
        Assert.Equal("transaction_not_found", first.Items[2].ReasonCode);
        Assert.Equal(1, commandCalls);
        Assert.Equal("office_supplies", categories[validId]);

        var replay = await service.CategorizeTransactionsAsync(request, default);

        Assert.Equal(0, replay.MutatedCount);
        Assert.Equal(3, replay.RejectedCount);
        Assert.Equal(1, commandCalls);
    }

    [Fact]
    public async Task Internal_boundary_rejects_oversized_batch_before_owner_and_emits_readiness_effect_evidence()
    {
        var ownerCalled = false;
        var guarded = Proxy<IFinanceGuardedCommandService>((_, _) =>
        {
            ownerCalled = true;
            throw new InvalidOperationException("Owner must not be called.");
        });
        var contract = Contract(guarded);
        var items = new JsonArray(Enumerable.Range(0, FinanceGuardedCommandContract.MaximumCategorizationBatchSize + 1)
            .Select(_ => (JsonNode?)new JsonObject
            {
                ["transactionId"] = Guid.NewGuid(), ["expectedCategory"] = "uncategorized", ["category"] = "expense"
            }).ToArray());
        var request = new InternalToolExecutionRequest(FinanceGuardedCommandToolIds.CategorizeTransactions,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ToolActionType.Execute, "finance",
                ActorUserId: Guid.NewGuid()),
            new Dictionary<string, JsonNode?> { ["idempotencyKey"] = "oversized-batch", ["items"] = items });

        var response = await contract.ExecuteAsync(request, default);

        Assert.False(response.Success);
        Assert.Equal("finance_command_not_ready", response.ErrorCode);
        Assert.False(ownerCalled);
        var effect = Assert.IsType<JsonObject>(response.Data["commandEffect"]);
        Assert.Contains("batch_limit_exceeded", effect["readinessBlockers"]!.ToJsonString());
        Assert.True(response.Metadata["requestedActualEffectRecorded"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Successful_internal_command_returns_exact_requested_actual_and_after_state_evidence()
    {
        var transactionId = Guid.NewGuid();
        var result = new GuardedCategorizationBatchResultDto(FinanceGuardedCommandContract.Version,
            "successful-batch", 1, 1, 1, 0, 125m,
            [new(0, transactionId, "uncategorized", "office_supplies", "office_supplies", 125m, "SEK",
                "applied", "category_updated", "Applied.", true, Transaction(transactionId, "office_supplies"))],
            false, "Applied one item.");
        var guarded = Proxy<IFinanceGuardedCommandService>((method, _) =>
        {
            Assert.Equal(nameof(IFinanceGuardedCommandService.CategorizeTransactionsAsync), method.Name);
            return Task.FromResult(result);
        });
        var request = new InternalToolExecutionRequest(FinanceGuardedCommandToolIds.CategorizeTransactions,
            new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ToolActionType.Execute, "finance",
                ActorUserId: Guid.NewGuid()),
            new Dictionary<string, JsonNode?>
            {
                ["idempotencyKey"] = "successful-batch",
                ["items"] = new JsonArray(new JsonObject
                {
                    ["transactionId"] = transactionId,
                    ["expectedCategory"] = "uncategorized",
                    ["category"] = "office_supplies"
                })
            });

        var response = await Contract(guarded).ExecuteAsync(request, default);

        Assert.True(response.Success);
        var effect = Assert.IsType<JsonObject>(response.Data["commandEffect"]);
        Assert.Equal("successful-batch", effect["requested"]!["idempotencyKey"]!.GetValue<string>());
        Assert.NotNull(effect["actual"]!["categorizationBatch"]);
        Assert.NotNull(effect["afterState"]!["categorizationBatch"]);
        Assert.Equal("applied", effect["itemDecisions"]![0]!["outcome"]!.GetValue<string>());
    }

    private static TrustedToolRegistration AssertRegistration(StaticCompanyToolRegistry registry, string toolName)
    {
        Assert.True(registry.TryGetTool(toolName, out var registration));
        return registration;
    }

    private static FinanceTransactionDetailDto Detail(Guid id, string category) => new(
        id, Guid.NewGuid(), "Business account", null, null, null, null, DateTime.UtcNow,
        category, 125m, "SEK", "Reviewed transaction", $"ref-{id:N}", false, "clear", [],
        new(true, false, false), new("unavailable", "No document.", false, null));

    private static FinanceTransactionDto Transaction(Guid id, string category) => new(
        id, Guid.NewGuid(), "Business account", null, null, null, null, DateTime.UtcNow,
        category, 125m, "SEK", "Reviewed transaction", $"ref-{id:N}", null);

    private static InternalCompanyToolContract Contract(IFinanceGuardedCommandService guarded) => new(
        Proxy<ICompanyTaskQueryService>(), Proxy<ICompanyTaskCommandService>(), Proxy<IProactiveTaskCreationService>(),
        Proxy<IApprovalRequestService>(), Proxy<ICompanyKnowledgeSearchService>(), Proxy<IFinanceToolProvider>(),
        Proxy<IFinanceTransactionAnomalyDetectionService>(), Proxy<IFinanceAgentAnalysisService>(),
        Proxy<IFinanceLedgerAgentReadService>(), Proxy<IFinanceCloseComplianceAgentService>(),
        Proxy<IFinanceAdvancedAccountingAgentService>(), Proxy<IFinanceAccountingDraftAgentService>(),
        Proxy<IFinanceOperationalProposalAgentService>(), guarded, Proxy<IAccountingProviderSwitchAgentService>(),
        Proxy<ILeadGenerationService>());

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?>? callback = null) where T : class
    {
        var value = DispatchProxy.Create<T, CallbackProxy>();
        ((CallbackProxy)(object)value).Callback = callback;
        return value;
    }

    private class CallbackProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Callback { get; set; }
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            Callback?.Invoke(targetMethod!, args) ?? throw new InvalidOperationException($"Unexpected call to {targetMethod?.Name}.");
    }
}
