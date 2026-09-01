using System.Text.Json.Nodes;

namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    public Task<FinanceConversationRunViewModel> StartConversationRunAsync(Guid companyId, Guid agentId,
        StartFinanceConversationRunApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<StartFinanceConversationRunApiRequest, FinanceConversationRunViewModel>(
            companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/agents/{agentId:D}/finance/tool-plans/runs", request, cancellationToken);
    }

    public Task<FinanceConversationRunViewModel?> GetConversationRunAsync(Guid companyId, Guid agentId, Guid runId,
        CancellationToken cancellationToken = default) => _useOfflineMode
        ? Task.FromResult<FinanceConversationRunViewModel?>(null)
        : GetAsync<FinanceConversationRunViewModel>(companyId,
            $"api/companies/{companyId:D}/agents/{agentId:D}/finance/tool-plans/runs/{runId:D}",
            allowNotFound: true, cancellationToken);

    public Task<FinanceConversationRunListViewModel?> ListConversationRunsAsync(Guid companyId, Guid agentId,
        int take = 20, CancellationToken cancellationToken = default) => _useOfflineMode
        ? Task.FromResult<FinanceConversationRunListViewModel?>(null)
        : GetAsync<FinanceConversationRunListViewModel>(companyId,
            $"api/companies/{companyId:D}/agents/{agentId:D}/finance/tool-plans/runs?take={Math.Clamp(take, 1, 100)}",
            allowNotFound: false, cancellationToken);

    public Task<FinanceConversationRunViewModel> ConfirmConversationRunStepAsync(Guid companyId, Guid agentId,
        Guid runId, string stepId, long expectedStepVersion, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<ConfirmFinanceConversationRunStepApiRequest, FinanceConversationRunViewModel>(
            companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/agents/{agentId:D}/finance/tool-plans/runs/{runId:D}/steps/{Uri.EscapeDataString(stepId)}/confirm",
            new ConfirmFinanceConversationRunStepApiRequest(expectedStepVersion), cancellationToken);
    }

    public Task<FinanceConversationRunViewModel> CancelConversationRunAsync(Guid companyId, Guid agentId,
        Guid runId, string reason, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CancelFinanceConversationRunApiRequest, FinanceConversationRunViewModel>(
            companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/agents/{agentId:D}/finance/tool-plans/runs/{runId:D}/cancel",
            new CancelFinanceConversationRunApiRequest(reason), cancellationToken);
    }

    public Task<FinanceConversationRunViewModel> SupersedeConversationRunAsync(Guid companyId, Guid agentId,
        Guid runId, SupersedeFinanceConversationRunApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<SupersedeFinanceConversationRunApiRequest, FinanceConversationRunViewModel>(
            companyId, HttpMethod.Post,
            $"api/companies/{companyId:D}/agents/{agentId:D}/finance/tool-plans/runs/{runId:D}/supersede",
            request, cancellationToken);
    }
}

public sealed record FinancePlanningReferenceApiRequest(string Type, string Value);
public sealed record StartFinanceConversationRunApiRequest(
    string UserRequest,
    string IdempotencyKey,
    IReadOnlyList<FinanceToolPlanContextApiItem>? Context = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    Guid? WorkflowInstanceId = null,
    Guid? DelegationAuthorityId = null,
    string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReferenceApiRequest>? References = null);
public sealed record FinanceToolPlanContextApiItem(Guid CompanyId, string SourceId, string SourceType,
    string Title, string Content, string? RecordId = null, string? RecordVersion = null, DateTime? UpdatedUtc = null);
public sealed record ConfirmFinanceConversationRunStepApiRequest(long ExpectedStepVersion);
public sealed record CancelFinanceConversationRunApiRequest(string Reason);
public sealed record SupersedeFinanceConversationRunApiRequest(
    string UserRequest,
    string IdempotencyKey,
    string Reason,
    IReadOnlyList<FinanceToolPlanContextApiItem>? Context = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    Guid? WorkflowInstanceId = null,
    Guid? DelegationAuthorityId = null,
    string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReferenceApiRequest>? References = null);

public sealed record FinanceConversationRunListViewModel(
    IReadOnlyList<FinanceConversationRunViewModel> Items,
    int TotalCount);
public sealed record FinanceConversationRunViewModel(
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
    IReadOnlyList<FinanceConversationRunRevisionViewModel> Revisions,
    IReadOnlyList<FinanceConversationRunStepViewModel> Steps,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? CompletedUtc);
public sealed record FinanceConversationRunRevisionViewModel(
    int Revision,
    Guid PlanId,
    string PlanState,
    string ReasonCode,
    string PlanningContextHash,
    IReadOnlyList<FinancePlanningEvidenceViewModel> Evidence,
    DateTime CreatedUtc);
public sealed record FinancePlanningEvidenceViewModel(
    string SourceId,
    string SourceVersion,
    string EntityType,
    string EntityId,
    string SafeLabel,
    DateTime UpdatedUtc,
    bool IsFresh);
public sealed record FinanceConversationRunStepViewModel(
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
