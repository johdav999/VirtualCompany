using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Observability;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class SingleAgentOrchestrationService : ISingleAgentOrchestrationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAgentRuntimeProfileResolver _agentRuntimeProfileResolver;
    private readonly IGroundedPromptContextService _groundedPromptContextService;
    private readonly IPromptBuilder _promptBuilder;
    private readonly IToolExecutor _toolExecutor;
    private readonly IOrchestrationAuditWriter _auditWriter;

    public SingleAgentOrchestrationService(
        VirtualCompanyDbContext dbContext,
        IAgentRuntimeProfileResolver agentRuntimeProfileResolver,
        IGroundedPromptContextService groundedPromptContextService,
        IPromptBuilder promptBuilder,
        IToolExecutor toolExecutor,
        IOrchestrationAuditWriter auditWriter)
    {
        _dbContext = dbContext;
        _agentRuntimeProfileResolver = agentRuntimeProfileResolver;
        _groundedPromptContextService = groundedPromptContextService;
        _promptBuilder = promptBuilder;
        _toolExecutor = toolExecutor;
        _auditWriter = auditWriter;
    }

    public async Task<OrchestrationResult> ExecuteAsync(
        SingleAgentOrchestrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        return await ExecuteCoreAsync(
            new OrchestrationRequest(
                request.CompanyId,
                request.AgentId,
                request.TaskId,
                UserInput: null,
                InitiatingActorId: request.InitiatingActorId,
                InitiatingActorType: request.InitiatingActorType,
                CorrelationId: request.CorrelationId,
                IntentHint: request.Intent),
            request.ToolInvocations,
            persistTaskResult: true,
            cancellationToken);
    }

    public async Task<OrchestrationResult> ExecuteAsync(
        OrchestrationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        return await ExecuteCoreAsync(request, explicitToolInvocations: null, persistTaskResult: request.TaskId.HasValue, cancellationToken);
    }

    private async Task<OrchestrationResult> ExecuteCoreAsync(
        OrchestrationRequest request,
        IReadOnlyList<ToolInvocationRequest>? explicitToolInvocations,
        bool persistTaskResult,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startedAtUtc = DateTime.UtcNow;
        var correlationId = EnsureCorrelationId(request.CorrelationId);
        // One shared engine path: role/persona differences enter only through persisted agent configuration.
        {
            var persistedTask = request.TaskId.HasValue
                ? await GetTaskAsync(request.CompanyId, request.TaskId.Value, cancellationToken)
                : null;
            var agentId = ResolveAgentId(request, persistedTask);
            var agent = await _agentRuntimeProfileResolver.GetCurrentProfileAsync(request.CompanyId, agentId, cancellationToken);
            var company = await GetCompanyAsync(request.CompanyId, cancellationToken);
            var task = persistedTask ?? CreateTransientTask(request, agent, company);

            if (!agent.CanReceiveAssignments)
            {
                throw new OrchestrationValidationException(
                    new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        [nameof(request.AgentId)] = ["The selected agent cannot execute single-agent tasks in its current status."]
                    });
            }

            GroundedPromptContextDto? groundedContext = null;
            try
            {
                groundedContext = await _groundedPromptContextService.PrepareAsync(
                    new GroundedPromptContextRequest(
                        request.CompanyId,
                        agent.Id,
                        BuildRetrievalQuery(request, task),
                        request.InitiatingActorId,
                        persistedTask?.Id,
                        persistedTask?.Title ?? task.Title,
                        persistedTask?.Description ?? task.Description,
                        Limits: ResolveRetrievalLimits(request),
                        CorrelationId: correlationId,
                        RetrievalPurpose: ResolveRetrievalPurpose(request)),
                    cancellationToken);
            }
            catch (GroundedContextRetrievalValidationException)
            {
                groundedContext = null;
            }

            var runtimeContext = new SingleAgentRuntimeContext(
                Guid.NewGuid(),
                correlationId,
                task,
                agent,
                company,
                groundedContext,
                ResolveAvailableTools(agent),
                NormalizeIntent(request.IntentHint, request.ConversationId))
            {
                InitiatingActorId = request.InitiatingActorId,
                InitiatingActorType = request.InitiatingActorType
            };

            var prompt = _promptBuilder.Build(new PromptBuildRequest(runtimeContext));
            var toolRequests = ResolveToolInvocations(explicitToolInvocations, task.InputPayload);
            var toolResults = new List<ToolInvocationResult>(toolRequests.Count);

            foreach (var toolRequest in toolRequests)
            {
                toolResults.Add(await _toolExecutor.ExecuteAsync(runtimeContext, toolRequest, cancellationToken));
            }

            var completedAtUtc = DateTime.UtcNow;
            var status = toolResults.Any(x => string.Equals(x.Status, ToolExecutionStatus.AwaitingApproval.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
                ? OrchestrationStatusValues.AwaitingApproval
                : OrchestrationStatusValues.Completed;
            var userFacingOutput = BuildUserFacingOutput(runtimeContext, toolResults);
            var rationale = BuildRationaleSummary(runtimeContext, groundedContext, toolResults);
            var confidence = toolResults.Any(x => string.Equals(x.Status, ToolExecutionStatus.Failed.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
                ? 0.4m
                : 0.76m;
            var userOutput = new OrchestrationUserOutput(userFacingOutput);
            var sourceReferences = BuildSourceReferences(groundedContext);
            var toolExecutionReferences = BuildToolExecutionReferences(toolResults);
            var structuredOutput = BuildStructuredOutput(runtimeContext, prompt, toolResults, userFacingOutput, rationale, confidence, sourceReferences, toolExecutionReferences);
            var metadata = BuildMetadata(runtimeContext, prompt, toolResults, startedAtUtc, completedAtUtc);
            var taskArtifact = BuildTaskArtifact(runtimeContext, status, structuredOutput, rationale, confidence, sourceReferences, toolExecutionReferences);
            var auditArtifacts = BuildAuditArtifacts(runtimeContext, metadata, status, rationale, sourceReferences, completedAtUtc);
            var finalResult = new OrchestrationCompositeFinalResult(userOutput, taskArtifact, auditArtifacts, rationale, sourceReferences, toolExecutionReferences, correlationId);

            var artifacts = BuildArtifacts(prompt, structuredOutput, toolResults, groundedContext, taskArtifact, auditArtifacts);

            var result = new OrchestrationResult(
                runtimeContext.OrchestrationId,
                request.CompanyId,
                task.Id,
                agent.Id,
                status,
                userFacingOutput,
                structuredOutput,
                rationale,
                confidence,
                toolResults,
                artifacts,
                metadata,
                correlationId);
            result = result with
            {
                UserOutput = userOutput,
                TaskArtifact = taskArtifact,
                AuditArtifacts = auditArtifacts,
                SourceReferences = sourceReferences,
                ToolExecutionReferences = toolExecutionReferences,
                FinalResult = finalResult
            };
            if (persistTaskResult)
            {
                await PersistTaskResultAsync(task.Id, result, cancellationToken);
            }
            await _auditWriter.WriteAsync(new OrchestrationAuditWriteRequest(runtimeContext, prompt, result), cancellationToken);
            _dbContext.ChangeTracker.DetectChanges();

            await _dbContext.SaveChangesAsync(cancellationToken);

            return result;
        }
    }

    private static void Validate(SingleAgentOrchestrationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.CompanyId == Guid.Empty)
        {
            errors[nameof(request.CompanyId)] = ["CompanyId is required."];
        }

        if (!request.TaskId.HasValue || request.TaskId.Value == Guid.Empty)
        {
            errors[nameof(request.TaskId)] = ["TaskId is required."];
        }

        if (request.AgentId == Guid.Empty)
        {
            errors[nameof(request.AgentId)] = ["AgentId cannot be empty."];
        }

        if (request.InitiatingActorId == Guid.Empty)
        {
            errors[nameof(request.InitiatingActorId)] = ["InitiatingActorId cannot be empty."];
        }

        if (errors.Count > 0)
        {
            throw new OrchestrationValidationException(errors);
        }
    }

    private static void Validate(OrchestrationRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        if (request.CompanyId == Guid.Empty)
        {
            errors[nameof(request.CompanyId)] = ["CompanyId is required."];
        }

        if (request.AgentId == Guid.Empty)
        {
            errors[nameof(request.AgentId)] = ["AgentId cannot be empty."];
        }

        if (request.TaskId == Guid.Empty)
        {
            errors[nameof(request.TaskId)] = ["TaskId cannot be empty."];
        }

        if (request.ConversationId == Guid.Empty)
        {
            errors[nameof(request.ConversationId)] = ["ConversationId cannot be empty."];
        }

        if (errors.Count > 0)
        {
            throw new OrchestrationValidationException(errors);
        }
    }

    private async Task<TaskDetailDto> GetTaskAsync(Guid companyId, Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.AssignedAgent)
            .Include(x => x.ParentTask)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == taskId, cancellationToken);

        if (task is null)
        {
            throw new KeyNotFoundException("Task not found.");
        }

        return new TaskDetailDto(
            task.Id,
            task.CompanyId,
            task.Type,
            task.Title,
            task.Description,
            task.Priority.ToStorageValue(),
            task.Status.ToStorageValue(),
            task.DueUtc,
            task.AssignedAgentId,
            task.ParentTaskId,
            task.WorkflowInstanceId,
            task.CreatedByActorType,
            task.SourceType,
            task.OriginatingAgentId,
            task.TriggerSource,
            task.CreationReason,
            task.TriggerEventId,
            task.CreatedByActorId,
            CloneNodes(task.InputPayload),
            CloneNodes(task.OutputPayload),
            task.RationaleSummary,
            task.ConfidenceScore,
            task.CreatedUtc,
            task.UpdatedUtc,
            task.CompletedUtc,
            task.AssignedAgent is null ? null : new TaskAgentSummaryDto(task.AssignedAgent.Id, task.AssignedAgent.DisplayName, task.AssignedAgent.Status.ToStorageValue()),
            task.ParentTask is null ? null : new TaskParentSummaryDto(task.ParentTask.Id, task.ParentTask.Title, task.ParentTask.Status.ToStorageValue()),
            task.CorrelationId,
            []);
    }

    private async Task<CompanyRuntimeContext> GetCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);

        if (company is null)
        {
            throw new KeyNotFoundException("Company not found.");
        }

        return new CompanyRuntimeContext(
            company.Id,
            company.Name,
            company.Industry,
            company.BusinessType,
            company.Timezone,
            company.Currency,
            company.Language,
            company.ComplianceRegion);
    }

    private static Guid ResolveAgentId(OrchestrationRequest request, TaskDetailDto? task)
    {
        var agentId = request.AgentId ?? task?.AssignedAgentId;
        if (agentId is null)
        {
            throw new OrchestrationValidationException(
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(request.AgentId)] = ["AgentId is required when the task has no assigned agent."]
                });
        }

        if (task?.AssignedAgentId is Guid assignedAgentId && request.AgentId.HasValue && assignedAgentId != request.AgentId.Value)
        {
            throw new OrchestrationValidationException(
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
                {
                    [nameof(request.AgentId)] = ["AgentId must match the task's assigned agent."]
                });
        }

        return agentId.Value;
    }

    private static TaskDetailDto CreateTransientTask(
        OrchestrationRequest request,
        AgentRuntimeProfileDto agent,
        CompanyRuntimeContext company)
    {
        var now = DateTime.UtcNow;
        var title = string.Equals(NormalizeIntent(request.IntentHint, request.ConversationId), OrchestrationIntentValues.Chat, StringComparison.OrdinalIgnoreCase)
            ? "Direct agent chat"
            : "Single-agent request";

        return new TaskDetailDto(
            Guid.NewGuid(),
            company.CompanyId,
            string.Equals(NormalizeIntent(request.IntentHint, request.ConversationId), OrchestrationIntentValues.Chat, StringComparison.OrdinalIgnoreCase)
                ? "direct_agent_chat"
                : "single_agent_request",
            title,
            request.UserInput,
            WorkTaskPriority.Normal.ToStorageValue(),
            WorkTaskStatus.InProgress.ToStorageValue(),
            null,
            agent.Id,
            null,
            null,
            request.InitiatingActorType ?? "user",
            WorkTaskSourceTypes.Agent,
            agent.Id,
            null,
            request.UserInput,
            null,
            request.InitiatingActorId,
            BuildTransientInputPayload(request),
            [],
            null,
            null,
            now,
            now,
            null,
            new TaskAgentSummaryDto(agent.Id, agent.DisplayName, agent.Status),
            null,
            request.CorrelationId,
            []);
    }

    private async Task PersistTaskResultAsync(
        Guid taskId,
        OrchestrationResult result,
        CancellationToken cancellationToken)
    {
        var task = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == result.CompanyId && x.Id == taskId, cancellationToken);

        var taskStatus = string.Equals(result.Status, OrchestrationStatusValues.AwaitingApproval, StringComparison.OrdinalIgnoreCase)
            ? WorkTaskStatus.AwaitingApproval
            : WorkTaskStatus.Completed;

        task.SetCorrelationId(result.CorrelationId);
        task.UpdateStatus(taskStatus, result.TaskArtifact?.OutputPayload ?? result.StructuredOutput, result.RationaleSummary, result.ConfidenceScore);
    }

    private static Dictionary<string, JsonNode?> BuildTransientInputPayload(OrchestrationRequest request)
    {
        var payload = request.ActorMetadata is null || request.ActorMetadata.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : CloneNodes(request.ActorMetadata);

        if (!string.IsNullOrWhiteSpace(request.UserInput))
        {
            payload["userInput"] = JsonValue.Create(request.UserInput.Trim());
        }

        if (request.ConversationId.HasValue)
        {
            payload["conversationId"] = JsonValue.Create(request.ConversationId.Value.ToString("N"));
        }

        return payload;
    }

    private static string NormalizeIntent(string? intent, Guid? conversationId = null) =>
        string.IsNullOrWhiteSpace(intent)
            ? conversationId.HasValue ? OrchestrationIntentValues.Chat : OrchestrationIntentValues.ExecuteTask
            : intent.Trim();

    private static RetrievalSourceLimitOptions ResolveRetrievalLimits(OrchestrationRequest request) =>
        string.Equals(NormalizeIntent(request.IntentHint, request.ConversationId), OrchestrationIntentValues.Chat, StringComparison.OrdinalIgnoreCase)
            ? new RetrievalSourceLimitOptions(3, 3, 3, 2)
            : new RetrievalSourceLimitOptions(5, 5, 5, 3);

    private static string ResolveRetrievalPurpose(OrchestrationRequest request) =>
        string.Equals(NormalizeIntent(request.IntentHint, request.ConversationId), OrchestrationIntentValues.Chat, StringComparison.OrdinalIgnoreCase)
            ? "direct_agent_chat"
            : "single_agent_task_execution";

    private static string EnsureCorrelationId(string? requestedCorrelationId)
    {
        if (!string.IsNullOrWhiteSpace(requestedCorrelationId))
        {
            return requestedCorrelationId.Trim();
        }

        return Activity.Current?.Id ?? Guid.NewGuid().ToString("N");
    }

    private static string BuildRetrievalQuery(OrchestrationRequest request, TaskDetailDto task) =>
        !string.IsNullOrWhiteSpace(request.UserInput)
            ? request.UserInput.Trim()
            : string.IsNullOrWhiteSpace(task.Description)
            ? task.Title
            : $"{task.Title}\n{task.Description}";

    private static IReadOnlyList<ToolMetadataDto> ResolveAvailableTools(AgentRuntimeProfileDto agent)
    {
        var tools = new List<ToolMetadataDto>();
        if (agent.ToolPermissions.TryGetValue("allowed", out var allowedNode) && allowedNode is JsonArray allowedArray)
        {
            foreach (var item in allowedArray)
            {
                var name = item is JsonValue value && value.TryGetValue<string>(out var text)
                    ? text
                    : null;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    tools.Add(new ToolMetadataDto(
                        name.Trim(),
                        ["read", "write", "execute"],
                        ResolveScopes(agent.DataScopes),
                        CloneNodes(agent.ToolPermissions)));
                }
            }
        }

        if (tools.Count == 0)
        {
            foreach (var key in agent.ToolPermissions.Keys.Where(static key => !string.IsNullOrWhiteSpace(key)))
            {
                tools.Add(new ToolMetadataDto(key, ["read", "write", "execute"], ResolveScopes(agent.DataScopes), CloneNodes(agent.ToolPermissions)));
            }
        }

        return tools;
    }

    private static IReadOnlyList<string> ResolveScopes(IReadOnlyDictionary<string, JsonNode?> dataScopes)
    {
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, node) in dataScopes)
        {
            if (node is JsonArray array)
            {
                foreach (var item in array)
                {
                    if (item is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
                    {
                        scopes.Add(text.Trim());
                    }
                }
            }
        }

        return scopes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<ToolInvocationRequest> ResolveToolInvocations(
        IReadOnlyList<ToolInvocationRequest>? explicitRequests,
        IReadOnlyDictionary<string, JsonNode?> taskInputPayload)
    {
        if (explicitRequests is not null && explicitRequests.Count > 0)
        {
            return explicitRequests;
        }

        if (!taskInputPayload.TryGetValue("toolInvocations", out var invocationsNode) || invocationsNode is not JsonArray invocations)
        {
            return Array.Empty<ToolInvocationRequest>();
        }

        var requests = new List<ToolInvocationRequest>();
        foreach (var item in invocations.OfType<JsonObject>())
        {
            var toolName = GetString(item, "toolName");
            var actionType = GetString(item, "actionType");
            if (string.IsNullOrWhiteSpace(toolName) || string.IsNullOrWhiteSpace(actionType))
            {
                continue;
            }

            requests.Add(new ToolInvocationRequest(
                toolName,
                actionType,
                GetString(item, "scope"),
                CloneObject(item["requestPayload"] as JsonObject),
                GetString(item, "thresholdCategory"),
                GetString(item, "thresholdKey"),
                GetDecimal(item, "thresholdValue"),
                GetBoolean(item, "sensitiveAction") ?? false));
        }

        return requests;
    }

    private static string BuildUserFacingOutput(
        SingleAgentRuntimeContext context,
        IReadOnlyList<ToolInvocationResult> toolResults)
    {
        if (string.Equals(context.Intent, OrchestrationIntentValues.Chat, StringComparison.OrdinalIgnoreCase))
        {
            return BuildChatUserFacingOutput(context);
        }

        var builder = new StringBuilder();
        builder.Append(context.Agent.DisplayName);
        builder.Append(" completed task '");
        builder.Append(context.Task.Title);
        builder.Append("'.");

        if (toolResults.Count > 0)
        {
            builder.Append(' ');
            builder.Append(toolResults.Count);
            builder.Append(toolResults.Count == 1 ? " tool invocation was processed." : " tool invocations were processed.");
        }

        var deniedResults = toolResults
            .Where(x => string.Equals(x.Status, ToolExecutionStatus.Denied.ToStorageValue(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (deniedResults.Count > 0)
        {
            builder.Append(' ');
            builder.Append(deniedResults.Count == 1
                ? "One action was blocked by policy and was not executed."
                : "One or more actions were blocked by policy and were not executed.");
            if (deniedResults.Count == 1 && !string.IsNullOrWhiteSpace(deniedResults[0].Message))
            {
                builder.Append(' ').Append(Trim(deniedResults[0].Message, 240));
            }
        }

        if (toolResults.Any(x => string.Equals(x.Status, ToolExecutionStatus.AwaitingApproval.ToStorageValue(), StringComparison.OrdinalIgnoreCase)))
        {
            builder.Append(" One or more actions are awaiting approval before execution can continue.");
        }

        return builder.ToString();
    }

    private static string BuildChatUserFacingOutput(SingleAgentRuntimeContext context)
    {
        var builder = new StringBuilder();
        builder.Append(context.Agent.DisplayName);
        builder.Append(" here");
        builder.Append(" (");
        builder.Append(context.Agent.RoleName);
        builder.Append("). ");

        if (!string.IsNullOrWhiteSpace(context.Agent.RoleBrief))
        {
            builder.Append("I am handling this from my role brief: ");
            builder.Append(Trim(context.Agent.RoleBrief, 240));
            builder.Append(' ');
        }

        var personalitySummary = ExtractSummary(context.Agent.Personality);
        if (!string.IsNullOrWhiteSpace(personalitySummary))
        {
            builder.Append("My persona is ");
            builder.Append(Trim(personalitySummary, 160));
            builder.Append(' ');
        }

        if (context.GroundedContext is not null && context.GroundedContext.Context.Counts.SourceReferences > 0)
        {
            builder.Append("I found ");
            builder.Append(context.GroundedContext.Context.Counts.SourceReferences);
            builder.Append(" scoped context reference(s) to keep this tenant-specific. ");
        }

        builder.Append("For your message: ");
        builder.Append(Trim(context.Task.Description ?? context.Task.Title, 600));
        builder.Append(" My first-pass response is to clarify the desired outcome, identify the next concrete action, and create a task when you want this tracked.");

        return builder.ToString();
    }

    private static string BuildRationaleSummary(
        SingleAgentRuntimeContext context,
        GroundedPromptContextDto? groundedContext,
        IReadOnlyList<ToolInvocationResult> toolResults)
    {
        if (string.Equals(context.Intent, OrchestrationIntentValues.Chat, StringComparison.OrdinalIgnoreCase))
        {
            return "Used the selected agent profile, role brief, scoped context counts, and recent direct-chat history.";
        }

        var sourceCount = groundedContext?.Context.Counts.SourceReferences ?? 0;
        return $"Resolved agent '{context.Agent.DisplayName}', task intent '{context.Intent}', {sourceCount} context reference(s), and {toolResults.Count} structured tool execution result(s).";
    }

    private static Dictionary<string, JsonNode?> BuildStructuredOutput(
        SingleAgentRuntimeContext context,
        PromptBuildResult prompt,
        IReadOnlyList<ToolInvocationResult> toolResults,
        string userFacingOutput,
        string rationale,
        decimal confidence,
        IReadOnlyList<OrchestrationSourceReference> sourceReferences,
        IReadOnlyList<OrchestrationToolExecutionReference> toolExecutionReferences)
    {
        var output = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create("2026-04-13"),
            ["orchestrationId"] = JsonValue.Create(context.OrchestrationId.ToString("N")),
            ["promptId"] = JsonValue.Create(prompt.PromptId.ToString("N")),
            ["correlationId"] = JsonValue.Create(context.CorrelationId),
            ["agentId"] = JsonValue.Create(context.Agent.Id.ToString("N")),
            ["agentDisplayName"] = JsonValue.Create(context.Agent.DisplayName),
            ["agent_role_name"] = JsonValue.Create(context.Agent.RoleName),
            ["taskId"] = JsonValue.Create(context.Task.Id.ToString("N")),
            ["intent"] = JsonValue.Create(context.Intent),
            ["shared_engine"] = JsonValue.Create("single_agent_orchestration"),
            ["userFacingOutput"] = JsonValue.Create(userFacingOutput),
            ["rationaleSummary"] = JsonValue.Create(rationale),
            ["rationale_summary"] = JsonValue.Create(rationale),
            ["confidenceScore"] = JsonValue.Create(confidence),
            ["toolExecutions"] = JsonSerializer.SerializeToNode(toolResults.Select(ToToolExecutionSummary).ToList()),
            ["toolExecutionReferences"] = JsonSerializer.SerializeToNode(toolExecutionReferences),
            ["sourceReferences"] = JsonSerializer.SerializeToNode(sourceReferences),
            ["sourceReferenceCount"] = JsonValue.Create(sourceReferences.Count)
        };

        AppendChatPayload(context, output);
        return output;
    }

    private static IReadOnlyList<OrchestrationArtifact> BuildArtifacts(
        PromptBuildResult prompt,
        Dictionary<string, JsonNode?> structuredOutput,
        IReadOnlyList<ToolInvocationResult> toolResults,
        GroundedPromptContextDto? groundedContext,
        OrchestrationTaskArtifact taskArtifact,
        IReadOnlyList<OrchestrationAuditArtifact> auditArtifacts)
    {
        var artifacts = new List<OrchestrationArtifact>
        {
            new(OrchestrationArtifactTypes.Prompt, "prompt_payload", CloneNodes(prompt.Payload)),
            new(OrchestrationArtifactTypes.TaskOutput, "task_output", CloneNodes(structuredOutput))
        };

        artifacts.Add(new OrchestrationArtifact(
            OrchestrationArtifactTypes.TaskOutput,
            "task_artifact",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["taskId"] = JsonValue.Create(taskArtifact.TaskId.ToString("N")),
                ["status"] = JsonValue.Create(taskArtifact.Status),
                ["outputPayload"] = JsonSerializer.SerializeToNode(taskArtifact.OutputPayload),
                ["rationaleSummary"] = JsonValue.Create(taskArtifact.RationaleSummary),
                ["confidenceScore"] = JsonValue.Create(taskArtifact.ConfidenceScore),
                ["correlationId"] = JsonValue.Create(taskArtifact.CorrelationId)
            }));

        if (toolResults.Count > 0)
        {
            artifacts.Add(new OrchestrationArtifact(
                OrchestrationArtifactTypes.ToolExecution,
                "tool_execution_summaries",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["items"] = JsonSerializer.SerializeToNode(toolResults.Select(ToToolExecutionSummary).ToList())
                }));
        }

        if (groundedContext is not null)
        {
            artifacts.Add(new OrchestrationArtifact(
                OrchestrationArtifactTypes.ContextReferences,
                "context_references",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["retrievalId"] = JsonValue.Create(groundedContext.RetrievalId.ToString("N")),
                    ["sourceReferences"] = JsonSerializer.SerializeToNode(groundedContext.Context.SourceReferences)
                }));
        }

        artifacts.Add(new OrchestrationArtifact(
            OrchestrationArtifactTypes.AuditEvent,
            "audit_artifacts",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["items"] = JsonSerializer.SerializeToNode(auditArtifacts)
            }));

        return artifacts;
    }

    private static IReadOnlyList<OrchestrationSourceReference> BuildSourceReferences(GroundedPromptContextDto? groundedContext) =>
        groundedContext?.Context.SourceReferences.Select(static reference =>
            new OrchestrationSourceReference(
                reference.SourceType,
                reference.SourceId,
                reference.Title,
                reference.ParentSourceType,
                reference.ParentSourceId,
                reference.SectionId,
                reference.Locator,
                reference.Rank,
                reference.Score,
                string.IsNullOrWhiteSpace(reference.Snippet) ? null : reference.Snippet,
                new Dictionary<string, string?>(reference.Metadata, StringComparer.OrdinalIgnoreCase)))
            .ToList() ?? [];

    private static IReadOnlyList<OrchestrationToolExecutionReference> BuildToolExecutionReferences(IReadOnlyList<ToolInvocationResult> toolResults) =>
        toolResults.Select(static result =>
            new OrchestrationToolExecutionReference(
                result.ExecutionId,
                result.ToolName,
                result.ActionType,
                result.Scope,
                result.Status,
                result.ApprovalRequestId,
                result.PolicyDecision.Outcome,
                result.CorrelationId))
            .ToList();

    private static OrchestrationTaskArtifact BuildTaskArtifact(
        SingleAgentRuntimeContext context,
        string orchestrationStatus,
        Dictionary<string, JsonNode?> outputPayload,
        string? rationale,
        decimal? confidence,
        IReadOnlyList<OrchestrationSourceReference> sourceReferences,
        IReadOnlyList<OrchestrationToolExecutionReference> toolExecutionReferences)
    {
        var taskStatus = string.Equals(orchestrationStatus, OrchestrationStatusValues.AwaitingApproval, StringComparison.OrdinalIgnoreCase)
            ? WorkTaskStatus.AwaitingApproval.ToStorageValue()
            : WorkTaskStatus.Completed.ToStorageValue();

        return new OrchestrationTaskArtifact(
            context.Task.Id,
            taskStatus,
            CloneNodes(outputPayload),
            rationale,
            confidence,
            sourceReferences,
            toolExecutionReferences,
            context.CorrelationId);
    }

    private static IReadOnlyList<OrchestrationAuditArtifact> BuildAuditArtifacts(
        SingleAgentRuntimeContext context,
        OrchestrationMetadata metadata,
        string status,
        string? rationale,
        IReadOnlyList<OrchestrationSourceReference> sourceReferences,
        DateTime occurredUtc)
    {
        var dataSources = new List<string>
        {
            "single_agent_orchestration",
            "prompt_builder",
            "tool_executor"
        };

        if (sourceReferences.Count > 0)
        {
            dataSources.Add("grounded_context");
        }

        var auditMetadata = new Dictionary<string, string?>(metadata.AuditMetadata, StringComparer.OrdinalIgnoreCase)
        {
            ["orchestrationId"] = metadata.OrchestrationId.ToString("N"),
            ["promptId"] = metadata.PromptId.ToString("N"),
            ["correlationId"] = metadata.CorrelationId,
            ["status"] = status
        };

        return
        [
            new OrchestrationAuditArtifact(
                context.Company.CompanyId,
                AuditActorTypes.Agent,
                context.Agent.Id,
                AuditEventActions.SingleAgentTaskOrchestrationExecuted,
                AuditTargetTypes.WorkTask,
                context.Task.Id.ToString("N"),
                string.Equals(status, OrchestrationStatusValues.Completed, StringComparison.OrdinalIgnoreCase) ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Pending,
                rationale,
                dataSources,
                auditMetadata,
                context.CorrelationId,
                occurredUtc)
        ];
    }

    private static OrchestrationMetadata BuildMetadata(
        SingleAgentRuntimeContext context,
        PromptBuildResult prompt,
        IReadOnlyList<ToolInvocationResult> toolResults,
        DateTime startedAtUtc,
        DateTime completedAtUtc)
    {
        return new OrchestrationMetadata(
            context.OrchestrationId,
            prompt.PromptId,
            context.Company.CompanyId,
            context.Task.Id,
            context.Agent.Id,
            context.CorrelationId,
            context.Intent,
            startedAtUtc,
            completedAtUtc,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["toolExecutionCount"] = toolResults.Count.ToString(),
                ["sourceReferenceCount"] = (context.GroundedContext?.Context.Counts.SourceReferences ?? 0).ToString(),
                ["agentRoleName"] = context.Agent.RoleName,
                ["agentAutonomyLevel"] = context.Agent.AutonomyLevel,
                ["taskType"] = context.Task.Type,
                ["initiatingActorType"] = context.InitiatingActorType,
                ["initiatingActorId"] = context.InitiatingActorId?.ToString("N"),
                ["policyInterceptionPoint"] = "tool_executor"
            });
    }

    private static Dictionary<string, object?> ToToolExecutionSummary(ToolInvocationResult result) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["executionId"] = result.ExecutionId,
            ["toolName"] = result.ToolName,
            ["actionType"] = result.ActionType,
            ["scope"] = result.Scope,
            ["status"] = result.Status,
            ["approvalRequestId"] = result.ApprovalRequestId,
            ["policyOutcome"] = result.PolicyDecision.Outcome,
            ["message"] = result.Message,
            ["correlationId"] = result.CorrelationId,
            ["startedAtUtc"] = result.StartedAtUtc,
            ["completedAtUtc"] = result.CompletedAtUtc
        };

    private static void AppendChatPayload(SingleAgentRuntimeContext context, Dictionary<string, JsonNode?> output)
    {
        output["agent_display_name"] = JsonValue.Create(context.Agent.DisplayName);
        if (!string.IsNullOrWhiteSpace(context.Agent.RoleBrief))
        {
            output["agent_role_brief"] = JsonValue.Create(context.Agent.RoleBrief);
        }

        var personalitySummary = ExtractSummary(context.Agent.Personality);
        if (!string.IsNullOrWhiteSpace(personalitySummary))
        {
            output["agent_personality_summary"] = JsonValue.Create(personalitySummary);
        }

        if (context.Agent.DataScopes.Count > 0)
        {
            output["agent_data_scope_count"] = JsonValue.Create(context.Agent.DataScopes.Count);
        }

        if (context.GroundedContext is not null)
        {
            output["retrieval_id"] = JsonValue.Create(context.GroundedContext.RetrievalId.ToString());
            output["source_reference_count"] = JsonValue.Create(context.GroundedContext.Context.SourceReferences.Count);
        }
    }

    private static string? ExtractSummary(IReadOnlyDictionary<string, JsonNode?> values) =>
        values.TryGetValue("summary", out var node) &&
        node is JsonValue value &&
        value.TryGetValue<string>(out var summary)
            ? summary
            : null;

    private static string Trim(string value, int maxLength)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength ? normalized : string.Concat(normalized.AsSpan(0, maxLength - 3), "...");
    }

    private static string? GetString(JsonObject jsonObject, string propertyName) =>
        jsonObject.TryGetPropertyValue(propertyName, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    private static decimal? GetDecimal(JsonObject jsonObject, string propertyName)
    {
        if (!jsonObject.TryGetPropertyValue(propertyName, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return value.TryGetValue<double>(out var doubleNumber) ? Convert.ToDecimal(doubleNumber) : null;
    }

    private static bool? GetBoolean(JsonObject jsonObject, string propertyName) =>
        jsonObject.TryGetPropertyValue(propertyName, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<bool>(out var boolean)
            ? boolean
            : null;

    private static Dictionary<string, JsonNode?> CloneObject(JsonObject? jsonObject)
    {
        if (jsonObject is null)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        }

        return jsonObject.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class AgentToolOrchestrationExecutor : IToolExecutor
{
    private readonly IAgentToolExecutionService _agentToolExecutionService;

    public AgentToolOrchestrationExecutor(IAgentToolExecutionService agentToolExecutionService)
    {
        _agentToolExecutionService = agentToolExecutionService;
    }

    public async Task<ToolInvocationResult> ExecuteAsync(
        SingleAgentRuntimeContext runtimeContext,
        ToolInvocationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        ArgumentNullException.ThrowIfNull(request);

        var startedAtUtc = DateTime.UtcNow;
        var result = await _agentToolExecutionService.ExecuteAsync(
            runtimeContext.Company.CompanyId,
            runtimeContext.Agent.Id,
            new ExecuteAgentToolCommand(
                request.ToolName,
                request.ActionType,
                request.Scope,
                request.RequestPayload,
                request.ThresholdCategory,
                request.ThresholdKey,
                request.ThresholdValue,
                request.SensitiveAction,
                runtimeContext.Task.Id,
                runtimeContext.Task.WorkflowInstanceId,
                runtimeContext.CorrelationId),
            cancellationToken);

        return new ToolInvocationResult(
            result.ExecutionId,
            request.ToolName,
            request.ActionType,
            request.Scope,
            result.Status,
            result.ApprovalRequestId,
            result.PolicyDecision,
            result.ExecutionResult,
            result.Message,
            startedAtUtc,
            DateTime.UtcNow,
            runtimeContext.CorrelationId);
    }
}

public sealed class OrchestrationAuditWriter : IOrchestrationAuditWriter
{
    private readonly IAuditEventWriter _auditEventWriter;

    public OrchestrationAuditWriter(IAuditEventWriter auditEventWriter)
    {
        _auditEventWriter = auditEventWriter;
    }

    public Task WriteAsync(OrchestrationAuditWriteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var artifact = request.Result.AuditArtifacts.FirstOrDefault();

        return _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                artifact?.CompanyId ?? request.RuntimeContext.Company.CompanyId,
                artifact?.ActorType ?? AuditActorTypes.Agent,
                artifact?.ActorId ?? request.RuntimeContext.Agent.Id,
                artifact?.Action ?? AuditEventActions.SingleAgentTaskOrchestrationExecuted,
                artifact?.TargetType ?? AuditTargetTypes.WorkTask,
                artifact?.TargetId ?? request.RuntimeContext.Task.Id.ToString("N"),
                artifact?.Outcome ?? ResolveAuditOutcome(request.Result.Status),
                artifact?.RationaleSummary ?? request.Result.RationaleSummary,
                artifact?.DataSources ?? ["single_agent_orchestration", "prompt_builder", "tool_executor"],
                artifact?.Metadata ?? BuildFallbackAuditMetadata(request),
                artifact?.CorrelationId ?? request.Result.CorrelationId,
                artifact?.OccurredUtc ?? request.Result.Metadata.CompletedAtUtc,
                BuildDataSourcesUsed(request)),
            cancellationToken);
    }

    private static IReadOnlyCollection<AuditDataSourceUsed> BuildDataSourcesUsed(OrchestrationAuditWriteRequest request) =>
        request.Result.SourceReferences
            .Select(source => new AuditDataSourceUsed(
                source.SourceType,
                source.SourceId,
                source.Title,
                source.Locator))
            .ToArray();

    private static string ResolveAuditOutcome(string status) =>
        string.Equals(status, OrchestrationStatusValues.Completed, StringComparison.OrdinalIgnoreCase)
            ? AuditEventOutcomes.Succeeded
            : AuditEventOutcomes.Pending;

    private static IReadOnlyDictionary<string, string?> BuildFallbackAuditMetadata(OrchestrationAuditWriteRequest request) =>
        new Dictionary<string, string?>(request.Result.Metadata.AuditMetadata, StringComparer.OrdinalIgnoreCase)
        {
            ["orchestrationId"] = request.Result.OrchestrationId.ToString("N"),
            ["promptId"] = request.Prompt.PromptId.ToString("N"),
            ["correlationId"] = request.Result.CorrelationId,
            ["status"] = request.Result.Status
        };
}
