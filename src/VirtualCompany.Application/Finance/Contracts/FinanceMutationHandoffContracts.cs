using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Finance;

public static class FinanceMutationHandoffVersions
{
    public const string ContractV1 = "finance-mutation-handoff-v1";
    public const string ConfirmationV1 = "finance-mutation-confirmation-v1";
}

public static class FinanceMutationPreviewStates
{
    public const string Ready = "ready_for_confirmation";
    public const string ApprovalRequired = "approval_required_after_confirmation";
    public const string Unsupported = "unsupported";
    public const string Failed = "failed";
}

public static class FinanceMutationConfirmationStates
{
    public const string ApprovalRequired = "approval_required";
    public const string Queued = "queued";
    public const string Reconciling = "reconciling";
    public const string Executed = "executed";
    public const string Failed = "failed";
    public const string Denied = "denied";
    public const string Expired = "expired";
    public const string Stale = "stale";
    public const string Invalid = "invalid";
}

public sealed record PreviewFinanceMutationRequest(
    Guid CompanyId,
    Guid AgentId,
    string UserRequest,
    IReadOnlyList<FinanceToolPlanContextItem>? Context = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReference>? References = null);

public sealed record FinanceMutationTargetState(
    string EntityType,
    Guid? EntityId,
    bool Exists,
    string Version,
    IReadOnlyDictionary<string, JsonNode?> State,
    DateTime? UpdatedUtc);

public sealed record FinanceMutationStepPreview(
    string StepId,
    int Order,
    string ToolName,
    string ToolVersion,
    string ActionType,
    string Scope,
    FinanceMutationTargetState Target,
    IReadOnlyDictionary<string, JsonNode?> ProposedChange,
    string ExpectedEffect,
    string Reversibility,
    string RiskTier,
    string PolicyOutcome,
    string RequiredPermission,
    string ApprovalPath,
    int EvidenceAgeSeconds,
    string ConfirmationToken,
    DateTime ExpiresUtc);

public sealed record FinanceMutationPreviewResult(
    Guid PreviewId,
    string ContractVersion,
    Guid PlanId,
    int PlanRevision,
    string State,
    string ReasonCode,
    string SafeExplanation,
    IReadOnlyList<FinanceMutationStepPreview> Steps,
    string EffectiveAuthorityVersion,
    string EffectiveAuthorityHash,
    string PlanningContextHash,
    DateTime CreatedUtc);

public sealed record ConfirmFinanceMutationRequest(
    Guid CompanyId,
    Guid AgentId,
    string ConfirmationToken,
    string? CorrelationId = null);

public sealed record ReconcileFinanceMutationRequest(
    Guid CompanyId,
    Guid AgentId,
    string ConfirmationToken);

public sealed record FinanceMutationConfirmationResult(
    Guid ConfirmationId,
    string ContractVersion,
    Guid PlanId,
    string StepId,
    string ToolName,
    string State,
    string ReasonCode,
    string SafeExplanation,
    Guid? ExecutionId,
    Guid? ApprovalRequestId,
    string PolicyOutcome,
    FinanceMutationTargetState? AuthoritativeState,
    bool IsDuplicate,
    DateTime CompletedUtc);

public interface IFinanceMutationHandoffService
{
    Task<FinanceMutationPreviewResult> PreviewAsync(
        PreviewFinanceMutationRequest request,
        CancellationToken cancellationToken);

    Task<FinanceMutationConfirmationResult> ConfirmAsync(
        ConfirmFinanceMutationRequest request,
        CancellationToken cancellationToken);

    Task<FinanceMutationConfirmationResult> ReconcileAsync(
        ReconcileFinanceMutationRequest request,
        CancellationToken cancellationToken);
}
