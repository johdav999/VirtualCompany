using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Finance;

public static class FinanceOperationalProposalAgentToolIds
{
    public const string ProposeCloseTaskAssignment = "finance.operational_proposals.close_task_assignment";
    public const string ProposeEvidenceRequest = "finance.operational_proposals.evidence_request";
    public const string ProposeComplianceChecklist = "finance.operational_proposals.compliance_evidence_checklist";
    public const string PreviewAuditPackage = "finance.operational_proposals.audit_package_preview";
    public const string ProposeAccountingSchedule = "finance.operational_proposals.accounting_schedule";
    public const string PreviewCurrencyRevaluation = "finance.operational_proposals.currency_revaluation";
    public const string ProposeFixedAssetAddition = "finance.operational_proposals.fixed_asset_addition";
    public const string ProposeFixedAssetDisposal = "finance.operational_proposals.fixed_asset_disposal";
    public const string PreviewFixedAssetDepreciation = "finance.operational_proposals.fixed_asset_depreciation";

    public const string SubmitForApproval = "finance.operational_proposals.submit_for_approval";
    public const string AssignCloseTask = "finance.operational_proposals.assign_close_task";
    public const string RequestEvidence = "finance.operational_proposals.request_evidence";
    public const string RequestAuditPackageGeneration = "finance.operational_proposals.request_audit_package_generation";

    public static IReadOnlyList<string> RecommendationTools { get; } =
    [
        ProposeCloseTaskAssignment, ProposeEvidenceRequest, ProposeComplianceChecklist,
        PreviewAuditPackage, ProposeAccountingSchedule, PreviewCurrencyRevaluation,
        ProposeFixedAssetAddition, ProposeFixedAssetDisposal, PreviewFixedAssetDepreciation
    ];

    public static IReadOnlyList<string> ExecuteTools { get; } =
        [SubmitForApproval, AssignCloseTask, RequestEvidence, RequestAuditPackageGeneration];
    public static IReadOnlyList<string> All { get; } = [.. RecommendationTools, .. ExecuteTools];
    public static bool Contains(string toolName) => All.Contains(toolName, StringComparer.OrdinalIgnoreCase);
}

public static class FinanceOperationalProposalAgentContract
{
    public const string Version = "2026-09-01.prompt6.v1";
    public const int MaximumEvidenceItems = 100;
    public const string AuthorityNotice =
        "Operational proposals remain unposted and cannot close or reopen periods, sign off evidence, approve their own work, make statutory claims, deliver externally, or change provider credentials.";
}

public static class FinanceOperationalProposalKinds
{
    public const string CloseTaskAssignment = "close_task_assignment";
    public const string EvidenceRequest = "evidence_request";
    public const string ComplianceChecklist = "compliance_evidence_checklist";
    public const string AuditPackage = "audit_package";
    public const string AccountingSchedule = "accounting_schedule";
    public const string CurrencyRevaluation = "currency_revaluation";
    public const string FixedAssetAddition = "fixed_asset_addition";
    public const string FixedAssetDisposal = "fixed_asset_disposal";
    public const string FixedAssetDepreciation = "fixed_asset_depreciation";
}

public sealed record FinanceOperationalProposalDto(
    string ProposalKind, string ProposalHash, string TargetType, Guid TargetId,
    long TargetVersion, IReadOnlyList<string> SourceEvidence, object ProposedChanges,
    IReadOnlyList<string> Blockers, IReadOnlyList<string> RequiredApprovals,
    IReadOnlyList<string> ExpectedDownstreamEffects, IReadOnlyList<string> AllowedActions,
    bool Posted, bool EvidenceCompleted, string AuthorityNotice);

public interface IFinanceOperationalProposalAgentService
{
    Task<InternalToolExecutionResponse> ExecuteAsync(InternalToolExecutionRequest request,
        CancellationToken cancellationToken);
}
