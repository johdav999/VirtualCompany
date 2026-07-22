namespace VirtualCompany.Application.Agents;

public static class AgentAiRunStatuses
{
    public const string Completed = "completed";
    public const string NeedsReview = "needs_review";
    public const string Blocked = "blocked";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}

public sealed class AgentAiConflictException : Exception
{
    public AgentAiConflictException(string message) : base(message) { }
}

public sealed record AgentAiSource(string Id, string Type, string Title, string Snippet, DateTime? UpdatedUtc = null);
public sealed record AgentAiClaim(string Text, string Type, decimal Confidence, IReadOnlyList<string> SourceIds);
public sealed record AgentAiNextAction(string Title, string ActionType, string? ToolName, bool RequiresApproval);

public sealed record AgentReasoningRequest(
    Guid CompanyId, Guid AgentId, string CapabilityId, string CapabilityVersion, string PromptVersion,
    string SchemaVersion, string Instruction, IReadOnlyList<AgentAiSource> Sources,
    IReadOnlyList<string> AllowedActionTypes, IReadOnlyList<string> AllowedTools,
    Guid? ActorUserId = null, Guid? TaskId = null, Guid? ConversationId = null, string? CorrelationId = null,
    bool IncludeClaims = true);

public sealed record AgentReasoningResult(
    Guid RunId, string Status, string ResultVersion, string Summary, IReadOnlyList<AgentAiClaim> Claims,
    decimal Confidence, IReadOnlyList<string> Uncertainty, IReadOnlyList<string> MissingEvidence,
    IReadOnlyList<AgentAiNextAction> NextActions, IReadOnlyList<string> SourceIds,
    string? FailureCode = null, string? FailureMessage = null);

public interface IAgentReasoningGateway
{
    Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken cancellationToken);
    Task<AgentReasoningResult?> GetRunAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken);
}

public sealed record AskAgentQuestionCommand(string Question, string? RequestedDomain = null, Guid? TaskId = null, Guid? ConversationId = null);
public sealed record AgentQuestionAnswerDto(Guid RunId, string State, string Answer, IReadOnlyList<AgentAiClaim> Claims,
    decimal Confidence, IReadOnlyList<AgentAiSource> Sources, IReadOnlyList<string> MissingInformation,
    bool RequiresReview, IReadOnlyList<AgentAiNextAction> NextActions);
public interface IAgentQuestionAnsweringService
{
    Task<AgentQuestionAnswerDto> AskAsync(Guid companyId, Guid agentId, AskAgentQuestionCommand command, CancellationToken cancellationToken);
}

public sealed record AgentRoleBriefingDto(Guid RunId, string Cadence, string Narrative,
    IReadOnlyList<AgentAiClaim> Findings, IReadOnlyList<AgentAiSource> Sources, bool RequiresReview, DateTime GeneratedUtc);
public interface IAgentRoleBriefingService
{
    Task<AgentRoleBriefingDto> GenerateAsync(Guid companyId, Guid agentId, string cadence, CancellationToken cancellationToken);
}

public sealed record AgentWorkPriorityItem(string SourceType, string SourceId, string Title, string Status,
    DateTime? DueUtc, int DeterministicScore, IReadOnlyList<string> ReasonCodes, string AiRationale,
    decimal Confidence, DateTime CalculatedUtc, DateTime SourceUpdatedUtc);
public interface IAgentWorkPrioritizationService
{
    Task<IReadOnlyList<AgentWorkPriorityItem>> PrioritizeAsync(Guid companyId, Guid agentId, int take, CancellationToken cancellationToken);
}

public sealed record GenerateAgentPlanCommand(string Objective, DateTime? TargetUtc = null, int MaximumSteps = 8);
public sealed record AgentPlanStepDto(int Order, string Title, string Description, Guid? OwnerAgentId, DateTime? DueUtc,
    IReadOnlyList<int> Dependencies, bool RequiresApproval, string CompletionEvidence, Guid? CommittedTaskId = null);
public sealed record AgentPlanDto(Guid RunId, string Status, string Objective, IReadOnlyList<string> Assumptions,
    IReadOnlyList<AgentPlanStepDto> Steps, IReadOnlyList<string> ValidationErrors, bool RequiresReview);
public interface IAgentPlanningService
{
    Task<AgentPlanDto> GenerateAsync(Guid companyId, Guid agentId, GenerateAgentPlanCommand command, CancellationToken cancellationToken);
    Task<AgentPlanDto> CommitAsync(Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken);
}

public sealed record AgentExceptionInterpretationDto(Guid RunId, Guid ExceptionId, string Classification,
    IReadOnlyList<string> ConfirmedFacts, IReadOnlyList<AgentAiClaim> Hypotheses,
    IReadOnlyList<string> AllowedNextActions, decimal Confidence, bool RequiresReview);
public interface IAgentExceptionInterpretationService
{
    Task<AgentExceptionInterpretationDto> InterpretAsync(Guid companyId, Guid agentId, Guid exceptionId, CancellationToken cancellationToken);
}

public static class AgentHandoffTypes
{
    public const string WonDealInvoiceReadiness = "won_deal_invoice_readiness";
    public const string CustomerPaymentRisk = "customer_payment_risk";
    public const string RefundCreditDispute = "refund_credit_invoice_dispute";
    public const string ChurnRisk = "churn_retention_risk";
    public const string DocumentationGap = "product_documentation_gap";
    public const string InternalRequest = "reviewed_internal_request";
}
public sealed record CreateAgentHandoffCommand(string Type, Guid ReceivingAgentId, string Objective, string RequestedOutcome,
    DateTime? DueUtc, IReadOnlyList<string>? SourceIds = null);
public sealed record TransitionAgentHandoffCommand(string Status, string? Summary = null, decimal? Confidence = null);
public sealed record AgentHandoffDto(Guid Id, string Type, Guid RequestingAgentId, Guid ReceivingAgentId, string Objective,
    string RequestedOutcome, string Status, DateTime? DueUtc, Guid? RelatedTaskId, string? CompletionSummary, DateTime UpdatedUtc);
public interface IAgentHandoffService
{
    Task<AgentHandoffDto> CreateAsync(Guid companyId, Guid requestingAgentId, CreateAgentHandoffCommand command, CancellationToken cancellationToken);
    Task<AgentHandoffDto> TransitionAsync(Guid companyId, Guid handoffId, TransitionAgentHandoffCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentHandoffDto>> ListAsync(Guid companyId, Guid? agentId, CancellationToken cancellationToken);
}

public sealed record ProposeAgentMemoryCommand(string MemoryType, string Scope, string Content, IReadOnlyList<string> SourceIds,
    decimal Confidence, string Sensitivity = "internal", int RetentionDays = 90, Guid? OrchestrationRunId = null);
public sealed record ReviewAgentMemoryCommand(bool Approve, string? Reason = null);
public sealed record AgentMemoryCandidateDto(Guid Id, Guid ProposingAgentId, string MemoryType, string Scope, string Content,
    decimal Confidence, string Sensitivity, string Status, DateTime ExpiresUtc, Guid? ActivatedMemoryItemId, DateTime UpdatedUtc);
public interface IAgentMemoryCandidateService
{
    Task<AgentMemoryCandidateDto> ProposeAsync(Guid companyId, Guid agentId, ProposeAgentMemoryCommand command, CancellationToken cancellationToken);
    Task<AgentMemoryCandidateDto> ReviewAsync(Guid companyId, Guid candidateId, ReviewAgentMemoryCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AgentMemoryCandidateDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken);
    Task<int> ExpireAsync(Guid companyId, CancellationToken cancellationToken);
}

public static class AgentAiQualityEventTypes
{
    public const string RecommendationProduced = "recommendation_produced";
    public const string Viewed = "viewed"; public const string Accepted = "accepted"; public const string Rejected = "rejected";
    public const string Corrected = "corrected"; public const string Expired = "expired";
    public const string ApprovalRequested = "approval_requested"; public const string ApprovalApproved = "approval_approved"; public const string ApprovalRejected = "approval_rejected";
    public const string ValidationFailed = "validation_failed"; public const string PolicyBlocked = "policy_blocked";
    public const string ToolExecuted = "tool_executed"; public const string ToolFailed = "tool_failed"; public const string ToolReconciled = "tool_reconciled";
    public const string HandoffCompleted = "handoff_completed"; public const string KnowledgeGapCreated = "knowledge_gap_created"; public const string KnowledgeGapClosed = "knowledge_gap_closed";
    public const string MemoryApproved = "memory_approved"; public const string MemoryRejected = "memory_rejected";
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { RecommendationProduced, Viewed, Accepted, Rejected, Corrected, Expired, ApprovalRequested, ApprovalApproved,
      ApprovalRejected, ValidationFailed, PolicyBlocked, ToolExecuted, ToolFailed, ToolReconciled, HandoffCompleted,
      KnowledgeGapCreated, KnowledgeGapClosed, MemoryApproved, MemoryRejected };
}
public sealed record RecordAgentAiFeedbackCommand(Guid AgentId, string CapabilityId, Guid? RunId, string EventType,
    string EventIdentity, string? ReasonCode = null, string? Comment = null, decimal? Confidence = null);
public sealed record AgentAiQualityMetricsDto(DateTime FromUtc, DateTime ToUtc, int SampleSize, int Produced, int Accepted,
    int Rejected, int Corrected, int ValidationFailures, int PolicyBlocks, decimal? AcceptanceRate,
    bool HasSufficientEvidence, bool RecommendAutonomyReview);
public interface IAgentAiQualityService
{
    Task RecordAsync(Guid companyId, RecordAgentAiFeedbackCommand command, CancellationToken cancellationToken);
    Task<AgentAiQualityMetricsDto> GetMetricsAsync(Guid companyId, DateTime fromUtc, DateTime toUtc,
        Guid? agentId, string? capabilityId, CancellationToken cancellationToken);
}
