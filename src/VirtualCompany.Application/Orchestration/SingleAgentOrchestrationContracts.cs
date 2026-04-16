using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Tasks;

namespace VirtualCompany.Application.Orchestration;

public sealed record SingleAgentOrchestrationRequest(
    Guid CompanyId,
    Guid? TaskId,
    Guid? AgentId = null,
    Guid? InitiatingActorId = null,
    string? InitiatingActorType = null,
    string? CorrelationId = null,
    string? Intent = null,
    IReadOnlyList<ToolInvocationRequest>? ToolInvocations = null);

public sealed record OrchestrationRequest(
    Guid CompanyId,
    Guid? AgentId = null,
    Guid? TaskId = null,
    Guid? ConversationId = null,
    string? UserInput = null,
    Guid? InitiatingActorId = null,
    string? InitiatingActorType = null,
    string? CorrelationId = null,
    string? IntentHint = null,
    Dictionary<string, JsonNode?>? ActorMetadata = null);

public sealed record ResolvedAgentContext(
    Guid AgentId,
    Guid CompanyId,
    string DisplayName,
    string RoleName,
    string Department,
    string Status,
    string AutonomyLevel,
    string? RoleBrief,
    Dictionary<string, JsonNode?> ToolPermissions,
    Dictionary<string, JsonNode?> DataScopes,
    bool CanReceiveAssignments,
    DateTime UpdatedAtUtc);

public sealed record ResolvedIntent(
    string Name,
    string TaskType,
    string Source,
    bool IsDeterministic,
    decimal Confidence);

public sealed record TaskRuntimeContext(
    Guid TaskId,
    string Type,
    string Title,
    string? Description,
    string Priority,
    string Status,
    Guid? AssignedAgentId,
    Guid? ParentTaskId,
    Guid? WorkflowInstanceId,
    Dictionary<string, JsonNode?> InputPayload);

public sealed record ConversationRuntimeContext(
    Guid ConversationId,
    string ChannelType,
    string? Subject,
    Guid CreatedByUserId,
    Guid? AgentId,
    DateTime UpdatedAtUtc);

public sealed record ActorRuntimeContext(
    string? ActorType,
    Guid? ActorId,
    string? UserInput,
    string CorrelationId,
    DateTime RequestedAtUtc,
    Dictionary<string, JsonNode?> Metadata);

public sealed record PolicyRuntimeContext(
    string AutonomyLevel,
    Dictionary<string, JsonNode?> ToolPermissionSnapshot,
    Dictionary<string, JsonNode?> DataScopeSnapshot);

public sealed record RuntimeContext(
    Guid OrchestrationId,
    CompanyRuntimeContext Company,
    ResolvedAgentContext Agent,
    ResolvedIntent Intent,
    ActorRuntimeContext Actor,
    TaskRuntimeContext? Task,
    ConversationRuntimeContext? Conversation,
    PolicyRuntimeContext Policy);

public sealed record OrchestrationResolutionResult(
    bool Succeeded,
    ResolvedAgentContext? Agent,
    ResolvedIntent? Intent,
    RuntimeContext? RuntimeContext,
    string CorrelationId,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public static OrchestrationResolutionResult Success(RuntimeContext runtimeContext) =>
        new(true, runtimeContext.Agent, runtimeContext.Intent, runtimeContext, runtimeContext.Actor.CorrelationId, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));

    public static OrchestrationResolutionResult Failure(string correlationId, string fieldName, string errorCode, string message) =>
        new(false, null, null, null, correlationId, new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [fieldName] = [$"{errorCode}: {message}"]
        });
}

public interface ISingleAgentOrchestrationResolver
{
    Task<OrchestrationResolutionResult> ResolveAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken);
}

public static class OrchestrationResolutionSources
{
    public const string Explicit = "explicit";
    public const string Task = "task";
    public const string Conversation = "conversation";
    public const string Heuristic = "heuristic";
}

public static class OrchestrationResolutionErrorCodes
{
    public const string MissingCompanyContext = "missing_company_context";
    public const string AgentNotFound = "agent_not_found";
    public const string TaskNotFound = "task_not_found";
    public const string ConversationNotFound = "conversation_not_found";
    public const string NoResolvableTargetAgent = "no_resolvable_target_agent";
    public const string AmbiguousTargetAgent = "ambiguous_target_agent";
    public const string AgentStatusNotExecutable = "agent_status_not_executable";
}

public sealed record SingleAgentRuntimeContext(
    Guid OrchestrationId,
    string CorrelationId,
    TaskDetailDto Task,
    AgentRuntimeProfileDto Agent,
    CompanyRuntimeContext Company,
    GroundedPromptContextDto? GroundedContext,
    IReadOnlyList<ToolMetadataDto> AvailableTools,
    string Intent)
{
    public Guid? InitiatingActorId { get; init; }

    public string? InitiatingActorType { get; init; }
}

public sealed record CompanyRuntimeContext(
    Guid CompanyId,
    string Name,
    string? Industry,
    string? BusinessType,
    string? Timezone,
    string? Currency,
    string? Language,
    string? ComplianceRegion,
    PromptIdentityPolicyDto? IdentityPolicy = null);

public sealed record ToolMetadataDto(
    string Name,
    IReadOnlyList<string> SupportedActions,
    IReadOnlyList<string> Scopes,
    IReadOnlyDictionary<string, JsonNode?> PolicyMetadata);

public sealed record MemorySnippet(
    string SourceId,
    string Title,
    string Content,
    string? MemoryType,
    string? Scope,
    double? RelevanceScore);

public sealed record PolicyInstruction(
    string Id,
    string Content,
    string Source,
    int Priority);

public sealed record ToolSchemaDefinition(
    string Name,
    IReadOnlyList<string> SupportedActions,
    IReadOnlyList<string> Scopes,
    IReadOnlyDictionary<string, JsonNode?> Schema);

public sealed record PromptSectionDto(
    string Id,
    string Title,
    int Order,
    string Content,
    Dictionary<string, JsonNode?> Metadata);

public sealed record PromptIdentityPolicyDto(
    string? Role = null,
    string? Seniority = null,
    string? BusinessResponsibility = null,
    IReadOnlyList<string>? CollaborationNorms = null,
    IReadOnlyList<string>? PersonalityTraits = null,
    string? AdditionalNotes = null);

public sealed record PromptIdentitySectionDto(
    string Role,
    string Seniority,
    string BusinessResponsibility,
    IReadOnlyList<string> CollaborationNorms,
    IReadOnlyList<string> PersonalityTraits,
    IReadOnlyList<string> AdditionalNotes,
    IReadOnlyDictionary<string, string> Sources);

public sealed record PromptIdentityTaskOverrides(
    string? Role = null,
    string? Seniority = null,
    string? BusinessResponsibility = null,
    IReadOnlyList<string>? CollaborationNorms = null,
    IReadOnlyList<string>? PersonalityTraits = null,
    string? AdditionalNotes = null);

public sealed record PromptBuildRequest(
    SingleAgentRuntimeContext RuntimeContext,
    IReadOnlyList<MemorySnippet>? MemorySnippets = null,
    IReadOnlyList<PolicyInstruction>? PolicyInstructions = null,
    IReadOnlyList<ToolSchemaDefinition>? ToolSchemas = null,
    PromptDebugMode DebugMode = PromptDebugMode.Suppressed);

public enum PromptDebugMode
{
    Suppressed = 0,
    NonProduction = 1
}

public static class PromptIdentityPayloadKeys
{
    public const string Role = "identityRole";
    public const string Seniority = "identitySeniority";
    public const string BusinessResponsibility = "identityBusinessResponsibility";
    public const string CollaborationNorms = "identityCollaborationNorms";
    public const string PersonalityTraits = "identityPersonalityTraits";
    public const string AdditionalNotes = "identityAdditionalNotes";
}

public sealed record PromptBuildResult(
    Guid PromptId,
    string CorrelationId,
    IReadOnlyList<PromptMessageDto> Messages,
    Dictionary<string, JsonNode?> Payload,
    string SystemPrompt,
    IReadOnlyList<PromptSectionDto> Sections,
    PromptIdentitySectionDto ResolvedIdentity,
    IReadOnlyList<ToolSchemaDefinition> ToolSchemas,
    IReadOnlyList<string> SourceReferenceIds,
    DateTime BuiltAtUtc);

public sealed record PromptMessageDto(
    string Role,
    string Name,
    string Content,
    Dictionary<string, JsonNode?> Metadata);

public sealed record ToolInvocationRequest(
    string ToolName,
    string ActionType,
    string? Scope,
    Dictionary<string, JsonNode?>? RequestPayload = null,
    string? ThresholdCategory = null,
    string? ThresholdKey = null,
    decimal? ThresholdValue = null,
    bool SensitiveAction = false);

public sealed record ToolInvocationResult(
    Guid ExecutionId,
    string ToolName,
    string ActionType,
    string? Scope,
    string Status,
    Guid? ApprovalRequestId,
    ToolExecutionDecisionDto PolicyDecision,
    Dictionary<string, JsonNode?>? ResultPayload,
    string Message,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string CorrelationId);

public sealed record OrchestrationArtifact(
    string ArtifactType,
    string Name,
    Dictionary<string, JsonNode?> Payload);

public sealed record OrchestrationUserOutput(
    string DisplayMessage,
    string ContentType = "text/plain");

public sealed record OrchestrationSourceReference(
    string SourceType,
    string SourceId,
    string Title,
    string? ParentSourceType,
    string? ParentSourceId,
    string? SectionId,
    string? Locator,
    int Rank,
    double? Score,
    string? Snippet,
    IReadOnlyDictionary<string, string?> Metadata);

public sealed record OrchestrationToolExecutionReference(
    Guid ExecutionId,
    string ToolName,
    string ActionType,
    string? Scope,
    string Status,
    Guid? ApprovalRequestId,
    string PolicyOutcome,
    string CorrelationId);

public sealed record OrchestrationTaskArtifact(
    Guid TaskId,
    string Status,
    Dictionary<string, JsonNode?> OutputPayload,
    string? RationaleSummary,
    decimal? ConfidenceScore,
    IReadOnlyList<OrchestrationSourceReference> SourceReferences,
    IReadOnlyList<OrchestrationToolExecutionReference> ToolExecutionReferences,
    string CorrelationId);

public sealed record OrchestrationAuditArtifact(
    Guid CompanyId,
    string ActorType,
    Guid? ActorId,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    string? RationaleSummary,
    IReadOnlyCollection<string> DataSources,
    IReadOnlyDictionary<string, string?> Metadata,
    string CorrelationId,
    DateTime OccurredUtc);

public sealed record OrchestrationCompositeFinalResult(
    OrchestrationUserOutput UserOutput,
    OrchestrationTaskArtifact TaskArtifact,
    IReadOnlyList<OrchestrationAuditArtifact> AuditArtifacts,
    string? RationaleSummary,
    IReadOnlyList<OrchestrationSourceReference> SourceReferences,
    IReadOnlyList<OrchestrationToolExecutionReference> ToolExecutionReferences,
    string CorrelationId);

public sealed record OrchestrationMetadata(
    Guid OrchestrationId,
    Guid PromptId,
    Guid CompanyId,
    Guid TaskId,
    Guid AgentId,
    string CorrelationId,
    string Intent,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    IReadOnlyDictionary<string, string?> AuditMetadata);

public sealed record OrchestrationResult(
    Guid OrchestrationId,
    Guid CompanyId,
    Guid TaskId,
    Guid AgentId,
    string Status,
    string UserFacingOutput,
    Dictionary<string, JsonNode?> StructuredOutput,
    string? RationaleSummary,
    decimal? ConfidenceScore,
    IReadOnlyList<ToolInvocationResult> ToolExecutions,
    IReadOnlyList<OrchestrationArtifact> Artifacts,
    OrchestrationMetadata Metadata,
    string CorrelationId,
    string? FailureReason = null)
{
    public OrchestrationUserOutput UserOutput { get; init; } = new(UserFacingOutput);

    public OrchestrationTaskArtifact? TaskArtifact { get; init; }

    public IReadOnlyList<OrchestrationAuditArtifact> AuditArtifacts { get; init; } = Array.Empty<OrchestrationAuditArtifact>();

    public IReadOnlyList<OrchestrationSourceReference> SourceReferences { get; init; } = Array.Empty<OrchestrationSourceReference>();

    public IReadOnlyList<OrchestrationToolExecutionReference> ToolExecutionReferences { get; init; } = Array.Empty<OrchestrationToolExecutionReference>();

    public OrchestrationCompositeFinalResult? FinalResult { get; init; }

    public OrchestrationAction? Action { get; init; }
}

public sealed record OrchestrationAction(
    string ActionType,
    string? TargetAgentRole,
    Guid? TargetAgentId,
    string Reason,
    string RequestedDomain,
    string MatchedRule);

public sealed record OrchestrationAuditWriteRequest(
    SingleAgentRuntimeContext RuntimeContext,
    PromptBuildResult Prompt,
    OrchestrationResult Result);

public interface ISingleAgentOrchestrationService
{
    Task<OrchestrationResult> ExecuteAsync(
        SingleAgentOrchestrationRequest request,
        CancellationToken cancellationToken);

    Task<OrchestrationResult> ExecuteAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken);
}

public interface IPromptBuilder
{
    PromptBuildResult Build(PromptBuildRequest request);
}

public interface IToolExecutor
{
    Task<ToolInvocationResult> ExecuteAsync(
        SingleAgentRuntimeContext runtimeContext,
        ToolInvocationRequest request,
        CancellationToken cancellationToken);
}

public interface IOrchestrationAuditWriter
{
    Task WriteAsync(OrchestrationAuditWriteRequest request, CancellationToken cancellationToken);
}

public sealed record CommunicationStyleRuleViolation(
    string RuleId,
    string RuleType,
    string Message);

public sealed record CommunicationStyleRuleCheckResult(
    bool Passed,
    IReadOnlyList<CommunicationStyleRuleViolation> Violations,
    IReadOnlyList<string> RuleIds);

public interface ICommunicationStyleRuleChecker
{
    CommunicationStyleRuleCheckResult Check(string? generatedText, AgentCommunicationProfileDto profile);
}

public sealed class OrchestrationValidationException : Exception
{
    public OrchestrationValidationException(IDictionary<string, string[]> errors)
        : base("Orchestration validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public static class OrchestrationStatusValues
{
    public const string Completed = "completed";
    public const string Failed = "failed";
    public const string AwaitingApproval = "awaiting_approval";
}

public static class OrchestrationIntentValues
{
    public const string ExecuteTask = "execute_task";
    public const string TaskExecution = "task_execution";
    public const string Chat = "chat";
    public const string GeneralAgentRequest = "general_agent_request";
}

public static class PromptRoles
{
    public const string System = "system";
    public const string User = "user";
    public const string Tool = "tool";
}

public static class OrchestrationArtifactTypes
{
    public const string Prompt = "prompt";
    public const string TaskOutput = "task_output";
    public const string ToolExecution = "tool_execution";
    public const string ContextReferences = "context_references";
    public const string AuditEvent = "audit_event";
}
