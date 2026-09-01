using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Finance;

public static class FinanceConversationExecutionVersions
{
    public const string ContractV1 = "finance-conversation-execution-v1";
    public const string PromptV1 = "finance-conversation-synthesis-v1";
    public const string CapabilityV1 = "1.0.0";
}

public static class FinanceConversationRunStates
{
    public const string Completed = "completed";
    public const string PartiallyCompleted = "partially_completed";
    public const string NeedsClarification = "needs_clarification";
    public const string Unsupported = "unsupported";
    public const string Failed = "failed";
    public const string TimedOut = "timed_out";
    public const string Cancelled = "cancelled";
}

public static class FinanceConversationStepStates
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string Skipped = "skipped";
    public const string TimedOut = "timed_out";
    public const string Cancelled = "cancelled";
}

public sealed record ExecuteFinanceConversationRequest(
    Guid CompanyId,
    Guid AgentId,
    string UserRequest,
    string IdempotencyKey,
    IReadOnlyList<FinanceToolPlanContextItem>? Context = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    string? CorrelationId = null,
    IReadOnlyList<FinancePlanningReference>? References = null,
    int? TimeoutSeconds = null);

public sealed record FinanceConversationPlanRevision(
    Guid PlanId,
    int Revision,
    string State,
    string ReasonCode,
    string PlanningContextHash,
    DateTime CreatedUtc);

public sealed record FinanceConversationSourceReference(
    string SourceId,
    string SourceType,
    string Title,
    DateTime? AsOfUtc,
    string? Currency,
    bool IsFresh,
    string? Link = null);

public sealed record FinanceConversationStepResult(
    string StepId,
    string ToolName,
    string ToolVersion,
    string ActionType,
    string State,
    int AttemptCount,
    Guid? ExecutionId,
    bool OutputSchemaValid,
    bool EvidenceFresh,
    bool Truncated,
    string? ErrorCode,
    string SafeSummary,
    IReadOnlyDictionary<string, JsonNode?>? ValidatedOutput,
    IReadOnlyList<string> DependencyStepIds,
    DateTime StartedUtc,
    DateTime CompletedUtc);

public sealed record FinanceConversationAnswer(
    string Summary,
    IReadOnlyList<AgentAiClaim> Facts,
    IReadOnlyList<AgentAiClaim> Inferences,
    IReadOnlyList<string> Unknowns,
    IReadOnlyList<FinanceConversationSourceReference> Sources,
    decimal Confidence);

public sealed record FinanceConversationExecutionMetrics(
    long ElapsedMilliseconds,
    int PlannerCalls,
    int SynthesisCalls,
    int ToolCalls,
    int RetryCount,
    decimal EstimatedCost = 0m);

public sealed record FinanceConversationExecutionResult(
    Guid RunId,
    string ContractVersion,
    string State,
    string ReasonCode,
    string SafeExplanation,
    string IdempotencyKey,
    string CorrelationId,
    bool IsDuplicate,
    IReadOnlyList<FinanceConversationPlanRevision> PlanRevisions,
    IReadOnlyList<FinanceConversationStepResult> Steps,
    FinanceConversationAnswer? Answer,
    IReadOnlyList<string> MissingEvidence,
    FinanceConversationExecutionMetrics Metrics,
    DateTime StartedUtc,
    DateTime CompletedUtc);

public interface IFinanceConversationExecutionService
{
    Task<FinanceConversationExecutionResult> ExecuteAsync(
        ExecuteFinanceConversationRequest request,
        CancellationToken cancellationToken);
}
