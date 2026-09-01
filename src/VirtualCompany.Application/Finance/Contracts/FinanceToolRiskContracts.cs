using VirtualCompany.Domain.Enums;
using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public static class FinanceToolRiskPolicyVersions
{
    public const string V1 = "finance-tool-risk-policy-v1";
}

public static class FinanceApprovalContinuationReasonCodes
{
    public const string BindingMissing = "finance_approval_binding_missing";
    public const string BindingMismatch = "finance_approval_binding_mismatch";
    public const string Expired = "finance_approval_expired";
    public const string AuthorityStale = "finance_approval_authority_stale";
    public const string TargetStale = "finance_approval_target_stale";
    public const string PolicyStale = "finance_approval_policy_stale";
    public const string IntegrationStale = "finance_approval_integration_stale";
    public const string EligibilityFailed = "finance_approval_eligibility_failed";
    public const string SelfApprovalRejected = "finance_approval_self_approval_rejected";
}

public static class FinanceToolRiskTiers
{
    public const string Low = "low";
    public const string High = "high";
    public const string Critical = "critical";
}

public static class FinanceToolReversibility
{
    public const string Reversible = "reversible";
    public const string ConditionallyReversible = "conditionally_reversible";
    public const string Irreversible = "irreversible";
}

public static class FinanceToolApprovalBehaviors
{
    public const string AlwaysReview = "always_review";
    public const string ReviewUnlessBoundedCategorizationException = "review_unless_bounded_categorization_exception";
}

public static class FinanceToolExternalSideEffects
{
    public const string InternalStateChange = "internal_state_change";
    public const string AccountingPosting = "accounting_posting";
    public const string ApprovalStateChange = "approval_state_change";
    public const string ProviderWrite = "provider_write";
    public const string PaymentAction = "payment_action";
    public const string ComplianceSubmission = "compliance_submission";
    public const string PeriodCloseOrLock = "period_close_or_lock";
    public const string YearEnd = "year_end";
    public const string MigrationExecution = "migration_execution";

    public static IReadOnlySet<string> SensitiveByDefault { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        AccountingPosting,
        ApprovalStateChange,
        ProviderWrite,
        PaymentAction,
        ComplianceSubmission,
        PeriodCloseOrLock,
        YearEnd,
        MigrationExecution
    };
}

public sealed record FinanceToolRiskClassification(
    string ToolName,
    string PolicyVersion,
    string RiskTier,
    string Reversibility,
    string RequiredActorPermission,
    string DefaultApprovalBehavior,
    string ThresholdCategory,
    bool RequiresSegregation,
    string ExternalSideEffectClassification)
{
    public bool IsSensitiveByDefault =>
        !string.Equals(DefaultApprovalBehavior, "allow", StringComparison.OrdinalIgnoreCase);
}

public sealed record FinanceToolRiskEvaluationContext(
    decimal? Amount,
    int ItemCount,
    string? CurrentState,
    string? RequestedCategory,
    bool BackendVerified,
    string EvidenceSource);

public static class FinanceToolRiskPolicyCatalog
{
    private static readonly IReadOnlyDictionary<string, FinanceToolRiskClassification> Classifications =
        Build().ToDictionary(item => item.ToolName, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<FinanceToolRiskClassification> All { get; } =
        Classifications.Values.OrderBy(item => item.ToolName, StringComparer.OrdinalIgnoreCase).ToArray();

    public static bool TryGet(string toolName, out FinanceToolRiskClassification classification) =>
        Classifications.TryGetValue(toolName, out classification!);

    public static FinanceToolRiskClassification GetRequired(string toolName) =>
        TryGet(toolName, out var classification)
            ? classification
            : throw new InvalidOperationException($"Finance execute tool '{toolName}' has no explicit risk classification in {FinanceToolRiskPolicyVersions.V1}.");

    private static IEnumerable<FinanceToolRiskClassification> Build()
    {
        yield return Risk(
            "categorize_transaction",
            FinanceToolRiskTiers.Low,
            FinanceToolReversibility.Reversible,
            FinancePermissions.Edit,
            FinanceToolApprovalBehaviors.ReviewUnlessBoundedCategorizationException,
            "finance_categorization",
            false,
            FinanceToolExternalSideEffects.InternalStateChange);
        yield return Risk(
            FinanceGuardedCommandToolIds.CategorizeTransactions,
            FinanceToolRiskTiers.Low,
            FinanceToolReversibility.Reversible,
            FinancePermissions.Edit,
            FinanceToolApprovalBehaviors.ReviewUnlessBoundedCategorizationException,
            "finance_categorization",
            false,
            FinanceToolExternalSideEffects.InternalStateChange);
        yield return Risk(
            "approve_invoice",
            FinanceToolRiskTiers.High,
            FinanceToolReversibility.ConditionallyReversible,
            FinancePermissions.Approve,
            FinanceToolApprovalBehaviors.AlwaysReview,
            "invoice_approval",
            true,
            FinanceToolExternalSideEffects.ApprovalStateChange);
        yield return Risk(
            "post_paid_supplier_bill_expense",
            FinanceToolRiskTiers.Critical,
            FinanceToolReversibility.Irreversible,
            FinancePermissions.AccountingAdmin,
            FinanceToolApprovalBehaviors.AlwaysReview,
            "accounting_posting",
            true,
            FinanceToolExternalSideEffects.AccountingPosting);
        yield return Risk(
            FinanceAccountingDraftAgentToolIds.SubmitForApproval,
            FinanceToolRiskTiers.High,
            FinanceToolReversibility.Reversible,
            FinancePermissions.AccountingAdmin,
            FinanceToolApprovalBehaviors.AlwaysReview,
            "accounting_draft_submission",
            true,
            FinanceToolExternalSideEffects.ApprovalStateChange);
        yield return Risk(
            FinanceOperationalProposalAgentToolIds.SubmitForApproval,
            FinanceToolRiskTiers.High, FinanceToolReversibility.Reversible,
            FinancePermissions.AccountingAdmin, FinanceToolApprovalBehaviors.AlwaysReview,
            "operational_proposal_submission", true,
            FinanceToolExternalSideEffects.ApprovalStateChange);
        yield return Risk(
            FinanceOperationalProposalAgentToolIds.AssignCloseTask,
            FinanceToolRiskTiers.High, FinanceToolReversibility.Reversible,
            FinancePermissions.AccountingAdmin, FinanceToolApprovalBehaviors.AlwaysReview,
            "close_task_assignment", true,
            FinanceToolExternalSideEffects.InternalStateChange);
        yield return Risk(
            FinanceOperationalProposalAgentToolIds.RequestEvidence,
            FinanceToolRiskTiers.Low, FinanceToolReversibility.Reversible,
            FinancePermissions.AccountingAdmin, FinanceToolApprovalBehaviors.AlwaysReview,
            "finance_evidence_request", false,
            FinanceToolExternalSideEffects.InternalStateChange);
        yield return Risk(
            FinanceOperationalProposalAgentToolIds.RequestAuditPackageGeneration,
            FinanceToolRiskTiers.High, FinanceToolReversibility.ConditionallyReversible,
            FinancePermissions.AccountingAdmin, FinanceToolApprovalBehaviors.AlwaysReview,
            "audit_package_generation", true,
            FinanceToolExternalSideEffects.ApprovalStateChange);

        foreach (var toolName in AccountingProviderSwitchAgentToolIds.ExecuteTools)
        {
            yield return Risk(
                toolName,
                FinanceToolRiskTiers.Critical,
                FinanceToolReversibility.ConditionallyReversible,
                FinancePermissions.ManageIntegrations,
                FinanceToolApprovalBehaviors.AlwaysReview,
                "accounting_migration",
                true,
                FinanceToolExternalSideEffects.MigrationExecution);
        }
    }

    private static FinanceToolRiskClassification Risk(
        string toolName,
        string riskTier,
        string reversibility,
        string requiredActorPermission,
        string defaultApprovalBehavior,
        string thresholdCategory,
        bool requiresSegregation,
        string externalSideEffectClassification) =>
        new(
            toolName,
            FinanceToolRiskPolicyVersions.V1,
            riskTier,
            reversibility,
            requiredActorPermission,
            defaultApprovalBehavior,
            thresholdCategory,
            requiresSegregation,
            externalSideEffectClassification);
}
