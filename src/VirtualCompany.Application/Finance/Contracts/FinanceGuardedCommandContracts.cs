using System.Text.Json.Nodes;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public static class FinanceGuardedCommandContract
{
    public const string Version = "2026-09-01.prompt7.v1";
    public const int MaximumCategorizationBatchSize = 20;
}

public static class FinanceGuardedCommandToolIds
{
    public const string CategorizeTransactions = "finance.guarded_commands.categorize_transactions";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { CategorizeTransactions };
}

public static class FinanceExecuteTransactionBehaviors
{
    public const string SingleAuthoritativeTransaction = "single_authoritative_transaction";
    public const string PerItemAuthoritativeTransaction = "per_item_authoritative_transaction";
    public const string OwningWorkflowTransaction = "owning_workflow_transaction";
}

public static class FinanceExecuteRetryBehaviors
{
    public const string ReadBeforeRetry = "read_before_retry";
    public const string OwningIdempotencyReceipt = "owning_idempotency_receipt";
    public const string ReconciliationBeforeRetry = "reconciliation_before_retry";
}

public sealed record FinanceExecuteToolReadinessContract(
    string ToolName,
    string ContractVersion,
    string OwningApplicationContract,
    string RequiredActorPermission,
    string RiskTier,
    string Reversibility,
    string ApprovalBehavior,
    string TargetContract,
    string VersionContract,
    string IdempotencyContract,
    string TransactionalBehavior,
    string ExternalEffectClassification,
    string RetryBehavior,
    string ReconciliationBehavior,
    string AuditBehavior,
    string RollbackOrRecoveryBehavior,
    string AfterStateReadContract,
    int MaximumBatchSize,
    string MaterialityExposureContract,
    bool ProducesPerItemDecisions,
    IReadOnlyList<string> RequiredRequestFields);

public static class FinanceExecuteToolReadinessCatalog
{
    private static readonly IReadOnlyDictionary<string, FinanceExecuteToolReadinessContract> Contracts =
        Build().ToDictionary(item => item.ToolName, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<FinanceExecuteToolReadinessContract> All { get; } =
        Contracts.Values.OrderBy(item => item.ToolName, StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool TryGet(string toolName, out FinanceExecuteToolReadinessContract contract) =>
        Contracts.TryGetValue(toolName, out contract!);

    public static FinanceExecuteToolReadinessContract GetRequired(string toolName) =>
        TryGet(toolName, out var contract)
            ? contract
            : throw new InvalidOperationException($"Finance execute tool '{toolName}' has no {FinanceGuardedCommandContract.Version} readiness contract.");

    public static IReadOnlyList<string> ValidateRequest(
        FinanceExecuteToolReadinessContract contract,
        IReadOnlyDictionary<string, JsonNode?> payload)
    {
        var blockers = contract.RequiredRequestFields
            .Where(field => !payload.TryGetValue(field, out var value) || IsMissing(value))
            .Select(field => $"required_field_missing:{field}")
            .ToList();

        foreach (var array in payload.Values.OfType<JsonArray>())
        {
            if (array.Count > contract.MaximumBatchSize)
                blockers.Add($"batch_limit_exceeded:{contract.MaximumBatchSize}");
        }

        return blockers.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static bool IsMissing(JsonNode? value) => value switch
    {
        null => true,
        JsonArray array => array.Count == 0,
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => string.IsNullOrWhiteSpace(text),
        _ => false
    };

    private static IEnumerable<FinanceExecuteToolReadinessContract> Build()
    {
        yield return Contract("categorize_transaction", "IFinanceToolProvider.UpdateTransactionCategoryAsync",
            FinancePermissions.Edit, "finance_transaction_id", "owning_record_reloaded_immediately_before_change",
            "tool_execution_attempt_and_idempotent_requested_state", FinanceExecuteTransactionBehaviors.SingleAuthoritativeTransaction,
            FinanceExecuteRetryBehaviors.ReadBeforeRetry, "not_required_for_internal_reversible_state",
            "restore_the_previous_category_through_the_same_authoritative_command", "FinanceTransactionDto", 1,
            "versioned_finance_categorization_policy", false, ["transactionId", "category"]);
        yield return Contract(FinanceGuardedCommandToolIds.CategorizeTransactions,
            "IFinanceGuardedCommandService.CategorizeTransactionsAsync", FinancePermissions.Edit,
            "bounded_finance_transaction_items", "expected_category_per_item_rechecked_before_each_change",
            "request_idempotency_key_plus_idempotent_requested_state", FinanceExecuteTransactionBehaviors.PerItemAuthoritativeTransaction,
            FinanceExecuteRetryBehaviors.ReadBeforeRetry, "not_required_for_internal_reversible_state",
            "restore_each_previous_category_through_the_same_authoritative_command", "GuardedCategorizationBatchResultDto",
            FinanceGuardedCommandContract.MaximumCategorizationBatchSize, "versioned_finance_categorization_policy", true,
            ["idempotencyKey", "items"]);
        yield return Contract("approve_invoice", "IFinanceToolProvider.UpdateInvoiceApprovalStatusAsync",
            FinancePermissions.Approve, "finance_invoice_id", "owning_invoice_transition_rechecked_immediately_before_change",
            "tool_execution_attempt_plus_authoritative_transition", FinanceExecuteTransactionBehaviors.SingleAuthoritativeTransaction,
            FinanceExecuteRetryBehaviors.ReadBeforeRetry, "not_required_for_internal_approval_state",
            "use_the_owning_invoice_review_workflow_for_a_permitted_correction", "FinanceInvoiceDto", 1,
            "finance_approval_policy_and_invoice_amount", false, ["invoiceId"]);
        yield return Contract("post_paid_supplier_bill_expense", "IFinanceToolProvider.PostPaidSupplierBillExpenseAsync",
            FinancePermissions.AccountingAdmin, "supplier_bill_id", "paid_bill_eligibility_recomputed_immediately_before_posting",
            "owning_paid_bill_draft_action_receipt", FinanceExecuteTransactionBehaviors.OwningWorkflowTransaction,
            FinanceExecuteRetryBehaviors.ReconciliationBeforeRetry, "provider_ambiguity_uses_owning_reconciliation_state",
            "governed_reversal_or_provider_reconciliation_only", "PaidSupplierBillExpensePostingDto", 1,
            "authoritative_bill_amount_and_accounting_posting_policy", false, ["billId"]);
        yield return Contract(FinanceAccountingDraftAgentToolIds.SubmitForApproval,
            "IFinanceAccountingDraftAgentService.ExecuteAsync", FinancePermissions.AccountingAdmin,
            "accounting_draft_id", "expected_draft_version_and_proposal_hash",
            "business_idempotency_key_and_owning_submission_receipt", FinanceExecuteTransactionBehaviors.OwningWorkflowTransaction,
            FinanceExecuteRetryBehaviors.OwningIdempotencyReceipt, "approval_workflow_state_is_authoritative",
            "withdraw_or_supersede_in_the_owning_draft_workflow", "accountingDraftSubmission", 1,
            "draft_control_totals_and_materiality_policy", false,
            ["draftId", "expectedVersion", "expectedPayloadHash", "idempotencyKey", "reviewed"]);

        foreach (var tool in FinanceOperationalProposalAgentToolIds.ExecuteTools)
        {
            var idempotency = tool == FinanceOperationalProposalAgentToolIds.RequestEvidence
                ? "proposal_hash_scoped_proactive_task_deduplication"
                : "business_idempotency_key_and_owning_workflow_receipt";
            yield return Contract(tool, "IFinanceOperationalProposalAgentService.ExecuteAsync",
                FinancePermissions.AccountingAdmin, "typed_operational_proposal_target",
                "expected_target_version_and_proposal_hash", idempotency,
                FinanceExecuteTransactionBehaviors.OwningWorkflowTransaction, FinanceExecuteRetryBehaviors.OwningIdempotencyReceipt,
                "owning_workflow_exposes_failure_and_recovery_state", "cancel_or_correct_through_the_owning_workflow",
                "proposalExecution", 1, "owning_materiality_and_segregation_policy", false,
                tool == FinanceOperationalProposalAgentToolIds.SubmitForApproval
                    ? ["proposalKind", "targetId", "expectedVersion", "expectedProposalHash", "idempotencyKey", "reviewed"]
                    : tool == FinanceOperationalProposalAgentToolIds.AssignCloseTask
                        ? ["closeInstanceId", "closeTaskId", "ownerUserId", "expectedVersion", "expectedProposalHash", "idempotencyKey", "reviewed"]
                        : tool == FinanceOperationalProposalAgentToolIds.RequestEvidence
                            ? ["scopeType", "targetId", "title", "description", "expectedProposalHash", "reviewed"]
                            : ["fiscalPeriodId", "expectedProposalHash", "idempotencyKey", "reviewed"]);
        }

        foreach (var tool in AccountingProviderSwitchAgentToolIds.ExecuteTools)
        {
            IReadOnlyList<string> required = tool switch
            {
                AccountingProviderSwitchAgentToolIds.StartPreparation or
                    AccountingProviderSwitchAgentToolIds.RequestPlanApproval =>
                    ["switchId", "expectedSwitchVersion", "idempotencyKey", "planId"],
                AccountingProviderSwitchAgentToolIds.ApplyApprovedMapping =>
                    ["switchId", "expectedSwitchVersion", "idempotencyKey", "stagedRecordId", "mappingDecisionId", "expectedRecordVersion", "disposition"],
                AccountingProviderSwitchAgentToolIds.CreateFollowUpTask =>
                    ["switchId", "expectedSwitchVersion", "idempotencyKey", "title", "description", "priority"],
                AccountingProviderSwitchAgentToolIds.StartApprovedFreeze or
                    AccountingProviderSwitchAgentToolIds.RequestActivationApproval or
                    AccountingProviderSwitchAgentToolIds.ResumeRecovery =>
                    ["switchId", "expectedSwitchVersion", "idempotencyKey", "cutoverExecutionId", "expectedExecutionVersion"],
                _ => ["switchId", "expectedSwitchVersion", "idempotencyKey"]
            };
            yield return Contract(tool, "IAccountingProviderSwitchAgentService", FinancePermissions.ManageIntegrations,
                "accounting_provider_switch_id", "expected_switch_version_plus_command_specific_target_version",
                "business_idempotency_key_and_migration_operation_receipt", FinanceExecuteTransactionBehaviors.OwningWorkflowTransaction,
                FinanceExecuteRetryBehaviors.ReconciliationBeforeRetry, "ambiguous_provider_outcomes_block_until_reconciled",
                "resume_only_from_the_current_classified_recovery_action", "AccountingProviderSwitchAgentCommandResultDto", 1,
                "migration_integrity_and_approval_policy", false, required);
        }
    }

    private static FinanceExecuteToolReadinessContract Contract(
        string toolName, string owner, string permission, string target, string version, string idempotency,
        string transaction, string retry, string reconciliation, string recovery, string afterState, int maxBatch,
        string materiality, bool perItem, IReadOnlyList<string> requiredFields)
    {
        var risk = FinanceToolRiskPolicyCatalog.GetRequired(toolName);
        return new(toolName, FinanceGuardedCommandContract.Version, owner, permission, risk.RiskTier,
            risk.Reversibility, risk.DefaultApprovalBehavior, target, version, idempotency, transaction,
            risk.ExternalSideEffectClassification, retry, reconciliation,
            "agent_execution_attempt_plus_authoritative_owner_audit_with_correlation", recovery, afterState,
            maxBatch, materiality, perItem, requiredFields);
    }
}

public sealed record GuardedTransactionCategorizationItem(
    Guid TransactionId,
    string ExpectedCategory,
    string Category);

public sealed record CategorizeTransactionsGuardedCommand(
    Guid CompanyId,
    Guid ActorUserId,
    Guid AgentId,
    string IdempotencyKey,
    IReadOnlyList<GuardedTransactionCategorizationItem> Items,
    string CorrelationId);

public sealed record GuardedCategorizationItemDecisionDto(
    int Index,
    Guid TransactionId,
    string ExpectedCategory,
    string RequestedCategory,
    string? ActualCategory,
    decimal? AbsoluteAmount,
    string? Currency,
    string Outcome,
    string ReasonCode,
    string Explanation,
    bool Mutated,
    FinanceTransactionDto? AfterState);

public sealed record GuardedCategorizationBatchResultDto(
    string ContractVersion,
    string IdempotencyKey,
    int RequestedCount,
    int EligibleCount,
    int MutatedCount,
    int RejectedCount,
    decimal AbsoluteAmountExposure,
    IReadOnlyList<GuardedCategorizationItemDecisionDto> Items,
    bool PartiallyApplied,
    string Summary);

public interface IFinanceGuardedCommandService
{
    Task<GuardedCategorizationBatchResultDto> CategorizeTransactionsAsync(
        CategorizeTransactionsGuardedCommand command,
        CancellationToken cancellationToken);
}
