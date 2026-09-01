using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Finance;

public static class FinanceConversationRunContractVersions
{
    public const string V1 = "finance-conversation-run-v1";
}

public sealed record StartFinanceConversationRunRequest(
    Guid CompanyId,
    Guid AgentId,
    string UserRequest,
    string IdempotencyKey,
    IReadOnlyList<FinanceToolPlanContextItem>? Context = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    Guid? WorkflowInstanceId = null,
    Guid? DelegationAuthorityId = null,
    string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReference>? References = null);

public sealed record ConfirmFinanceConversationRunStepRequest(
    Guid CompanyId,
    Guid AgentId,
    Guid RunId,
    string StepId,
    long ExpectedStepVersion);

public sealed record CancelFinanceConversationRunRequest(
    Guid CompanyId,
    Guid AgentId,
    Guid RunId,
    string Reason);

public sealed record SupersedeFinanceConversationRunRequest(
    Guid CompanyId,
    Guid AgentId,
    Guid RunId,
    StartFinanceConversationRunRequest Replacement,
    string Reason);

public sealed record FinanceConversationRunStepDto(
    Guid Id,
    string StepId,
    int Order,
    IReadOnlyList<string> Dependencies,
    string ToolName,
    string ToolVersion,
    string ActionType,
    string Scope,
    string ExpectedEffect,
    string Status,
    string BusinessIdempotencyKey,
    int AttemptCount,
    int MaxAttempts,
    Guid? ToolExecutionAttemptId,
    Guid? ApprovalRequestId,
    DateTime? ConfirmedUtc,
    DateTime? LeaseExpiresUtc,
    string? FailureCode,
    string? SafeFailureSummary,
    IReadOnlyDictionary<string, JsonNode?>? ResultSummary,
    long Version,
    DateTime UpdatedUtc);

public sealed record FinanceConversationRunRevisionDto(
    int Revision,
    Guid PlanId,
    string PlanState,
    string ReasonCode,
    string PlanningContextHash,
    IReadOnlyList<FinancePlanningEvidenceReference> Evidence,
    DateTime CreatedUtc);

public sealed record FinanceConversationRunDto(
    Guid Id,
    string ContractVersion,
    Guid CompanyId,
    Guid AgentId,
    Guid InitiatingUserId,
    string IdempotencyKey,
    string CorrelationId,
    string Status,
    string SafeSummary,
    string? FinalOutcomeCode,
    Guid? SupersededByRunId,
    DateTime? CancelledUtc,
    DateTime? LeaseExpiresUtc,
    DateTime RetainUntilUtc,
    DateTime? RedactedUtc,
    long Version,
    IReadOnlyList<FinanceConversationRunRevisionDto> Revisions,
    IReadOnlyList<FinanceConversationRunStepDto> Steps,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? CompletedUtc);

public sealed record FinanceConversationRunListResult(
    IReadOnlyList<FinanceConversationRunDto> Items,
    int TotalCount);

public sealed record FinanceConversationRunProcessResult(
    int ClaimedRuns,
    int CompletedRuns,
    int WaitingRuns,
    int RetriedRuns,
    int FailedRuns);

public interface IFinanceConversationRunService
{
    Task<FinanceConversationRunDto> StartAsync(StartFinanceConversationRunRequest request, CancellationToken cancellationToken);
    Task<FinanceConversationRunDto> GetAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken);
    Task<FinanceConversationRunListResult> ListAsync(Guid companyId, Guid agentId, int take, CancellationToken cancellationToken);
    Task<FinanceConversationRunDto> ConfirmStepAsync(ConfirmFinanceConversationRunStepRequest request, CancellationToken cancellationToken);
    Task<FinanceConversationRunDto> CancelAsync(CancelFinanceConversationRunRequest request, CancellationToken cancellationToken);
    Task<FinanceConversationRunDto> SupersedeAsync(SupersedeFinanceConversationRunRequest request, CancellationToken cancellationToken);
}

public interface IFinanceConversationRunProcessor
{
    Task<FinanceConversationRunProcessResult> RunOnceAsync(int batchSize, CancellationToken cancellationToken);
    Task ProcessRunAsync(Guid companyId, Guid runId, CancellationToken cancellationToken);
    Task<int> RedactExpiredAsync(int batchSize, CancellationToken cancellationToken);
}
