using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class InternalCompanyToolContract : IInternalCompanyToolContract
{
    private readonly ICompanyTaskQueryService _taskQueryService;
    private readonly ICompanyTaskCommandService _taskCommandService;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly ICompanyKnowledgeSearchService _knowledgeSearchService;

    public InternalCompanyToolContract(
        ICompanyTaskQueryService taskQueryService,
        ICompanyTaskCommandService taskCommandService,
        IApprovalRequestService approvalRequestService,
        ICompanyKnowledgeSearchService knowledgeSearchService)
    {
        _taskQueryService = taskQueryService;
        _taskCommandService = taskCommandService;
        _approvalRequestService = approvalRequestService;
        _knowledgeSearchService = knowledgeSearchService;
    }

    public async Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Failed("company_context_required", "Tool execution requires a company context.");
        }

        if (request.AgentId == Guid.Empty)
        {
            return Failed("agent_context_required", "Tool execution requires an agent context.");
        }

        if (request.ExecutionId == Guid.Empty)
        {
            return Failed("execution_context_required", "Tool execution requires an execution context.");
        }

        try
        {
            return request.ToolName.Trim().ToLowerInvariant() switch
            {
                "tasks.get" => await ExecuteTaskGetAsync(request, cancellationToken),
                "tasks.list" => await ExecuteTaskListAsync(request, cancellationToken),
                "tasks.update_status" => await ExecuteTaskStatusUpdateAsync(request, cancellationToken),
                "approvals.create_request" => await ExecuteApprovalCreateRequestAsync(request, cancellationToken),
                "knowledge.search" => await ExecuteKnowledgeSearchAsync(request, cancellationToken),
                _ => Failed("unsupported_internal_tool", "The requested internal tool is not available.")
            };
        }
        catch (TaskValidationException)
        {
            return Failed("task_validation_failed", "The task tool request was not valid.");
        }
        catch (ApprovalValidationException)
        {
            return Failed("approval_validation_failed", "The approval request was not valid.");
        }
        catch (CompanyKnowledgeSearchValidationException)
        {
            return Failed("knowledge_search_validation_failed", "The knowledge search request was not valid.");
        }
        catch (KeyNotFoundException)
        {
            return Failed("tool_target_not_found", "The requested internal record was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failed("tool_access_denied", "The requested internal tool could not access the requested company record.");
        }
    }

    private async Task<InternalToolExecutionResponse> ExecuteTaskGetAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var taskId = ReadGuid(request.Payload, "taskId") ?? request.TaskId;
        if (!taskId.HasValue)
        {
            return Failed("task_id_required", "A task id is required to read task details.");
        }

        var task = await _taskQueryService.GetByIdAsync(
            request.CompanyId,
            new GetTaskByIdQuery(taskId.Value),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Task details were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["task"] = Serialize(task),
                ["taskId"] = JsonValue.Create(task.Id),
                ["status"] = JsonValue.Create(task.Status)
            },
            Metadata(request, "task_query_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteTaskListAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var result = await _taskQueryService.ListAsync(
            request.CompanyId,
            new ListTasksQuery(
                ReadString(request.Payload, "status"),
                ReadGuid(request.Payload, "assignedAgentId"),
                ReadGuid(request.Payload, "parentTaskId"),
                ReadDateTime(request.Payload, "dueBefore"),
                ReadDateTime(request.Payload, "dueAfter"),
                ReadInt(request.Payload, "skip"),
                ReadInt(request.Payload, "take")),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Tasks were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["tasks"] = Serialize(result.Items),
                ["totalCount"] = JsonValue.Create(result.TotalCount),
                ["skip"] = JsonValue.Create(result.Skip),
                ["take"] = JsonValue.Create(result.Take)
            },
            Metadata(request, "task_query_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteTaskStatusUpdateAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure))
        {
            return actionFailure;
        }

        var taskId = ReadGuid(request.Payload, "taskId") ?? request.TaskId;
        if (!taskId.HasValue)
        {
            return Failed("task_id_required", "A task id is required to update task status.");
        }

        var status = ReadString(request.Payload, "status");
        if (string.IsNullOrWhiteSpace(status))
        {
            return Failed("task_status_required", "A target task status is required.");
        }

        var result = await _taskCommandService.UpdateStatusAsync(
            request.CompanyId,
            taskId.Value,
            new UpdateTaskStatusCommand(
                status,
                ReadObject(request.Payload, "outputPayload"),
                ReadString(request.Payload, "rationaleSummary"),
                ReadDecimal(request.Payload, "confidenceScore")),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Task status was updated.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["taskId"] = JsonValue.Create(result.Id),
                ["companyId"] = JsonValue.Create(result.CompanyId),
                ["status"] = JsonValue.Create(result.Status),
                ["updatedAt"] = JsonValue.Create(result.UpdatedAt)
            },
            Metadata(request, "task_command_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteApprovalCreateRequestAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure))
        {
            return actionFailure;
        }

        var targetEntityType = ReadString(request.Payload, "targetEntityType") ?? "task";
        var targetEntityId = ReadGuid(request.Payload, "targetEntityId") ?? request.TaskId;
        if (!targetEntityId.HasValue)
        {
            return Failed("approval_target_required", "An approval target is required.");
        }

        var requiredUserId = ReadGuid(request.Payload, "requiredUserId");
        var requiredRole = ReadString(request.Payload, "requiredRole");
        var steps = ReadApprovalSteps(request.Payload);
        if (requiredUserId is null && string.IsNullOrWhiteSpace(requiredRole) && steps.Count == 0)
        {
            return Failed("approval_route_required", "An approval request requires at least one approver.");
        }

        var thresholdContext = ReadObject(request.Payload, "thresholdContext") ??
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["toolName"] = JsonValue.Create(request.ToolName),
                ["actionType"] = JsonValue.Create(request.ActionType),
                ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
                ["executionId"] = JsonValue.Create(request.ExecutionId),
                ["correlationId"] = string.IsNullOrWhiteSpace(request.CorrelationId) ? null : JsonValue.Create(request.CorrelationId)
            };

        var approval = await _approvalRequestService.CreateAsync(
            request.CompanyId,
            new CreateApprovalRequestCommand(
                targetEntityType,
                targetEntityId.Value,
                ReadString(request.Payload, "requestedByActorType") ?? "agent",
                ReadGuid(request.Payload, "requestedByActorId") ?? request.AgentId,
                ReadString(request.Payload, "approvalType") ?? "tool_execution",
                thresholdContext,
                requiredRole,
                requiredUserId,
                steps),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Approval request was created.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["approvalRequest"] = Serialize(approval),
                ["approvalRequestId"] = JsonValue.Create(approval.Id),
                ["approvalStatus"] = JsonValue.Create(approval.Status)
            },
            Metadata(request, "approval_request_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteKnowledgeSearchAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Context.ActionType is not ToolActionType.Read and not ToolActionType.Recommend)
        {
            return Failed("unsupported_action_type", "Knowledge search only supports read or recommend actions.");
        }

        var queryText = ReadString(request.Payload, "query") ?? ReadString(request.Payload, "queryText");
        if (string.IsNullOrWhiteSpace(queryText))
        {
            return Failed("knowledge_query_required", "A knowledge search query is required.");
        }

        var topN = Math.Clamp(ReadInt(request.Payload, "topN") ?? 5, 1, 20);
        var results = await _knowledgeSearchService.SearchAsync(
            new CompanyKnowledgeSemanticSearchQuery(
                request.CompanyId,
                queryText,
                topN,
                new CompanyKnowledgeAccessContext(
                    request.CompanyId,
                    AgentId: request.AgentId)),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Knowledge search completed.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["results"] = Serialize(results),
                ["resultCount"] = JsonValue.Create(results.Count),
                ["query"] = JsonValue.Create(queryText)
            },
            Metadata(request, "knowledge_search_service"));
    }

    private static bool EnsureAction(
        InternalToolExecutionRequest request,
        ToolActionType expectedAction,
        out InternalToolExecutionResponse failure)
    {
        if (request.Context.ActionType == expectedAction)
        {
            failure = null!;
            return true;
        }

        failure = Failed(
            "unsupported_action_type",
            $"The {request.ToolName} tool does not support the requested action type.");
        return false;
    }

    private static InternalToolExecutionResponse Failed(string errorCode, string userSafeSummary) =>
        InternalToolExecutionResponse.Failed("failed", errorCode, userSafeSummary);

    private static Dictionary<string, JsonNode?> Metadata(InternalToolExecutionRequest request, string contractName) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["contractName"] = JsonValue.Create(contractName),
            ["companyId"] = JsonValue.Create(request.CompanyId),
            ["agentId"] = JsonValue.Create(request.AgentId),
            ["executionId"] = JsonValue.Create(request.ExecutionId),
            ["toolName"] = JsonValue.Create(request.ToolName),
            ["actionType"] = JsonValue.Create(request.ActionType),
            ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
            ["typedBoundary"] = JsonValue.Create(true)
        };

    private static JsonNode? Serialize<T>(T value) =>
        JsonSerializer.SerializeToNode(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        return null;
    }

    private static Guid? ReadGuid(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<Guid>(out var guid) && guid != Guid.Empty)
            {
                return guid;
            }

            if (value.TryGetValue<string>(out var text) && Guid.TryParse(text, out guid) && guid != Guid.Empty)
            {
                return guid;
            }
        }

        return null;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out number)
            ? number
            : null;
    }

    private static decimal? ReadDecimal(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && decimal.TryParse(text, out number)
            ? number
            : null;
    }

    private static DateTime? ReadDateTime(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<DateTime>(out var dateTime))
        {
            return dateTime;
        }

        return value.TryGetValue<string>(out var text) && DateTime.TryParse(text, out dateTime)
            ? dateTime
            : null;
    }

    private static Dictionary<string, JsonNode?>? ReadObject(IReadOnlyDictionary<string, JsonNode?> payload, string key)
    {
        if (!payload.TryGetValue(key, out var node) || node is not JsonObject jsonObject)
        {
            return null;
        }

        return jsonObject.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<CreateApprovalStepInput> ReadApprovalSteps(IReadOnlyDictionary<string, JsonNode?> payload)
    {
        if (!payload.TryGetValue("steps", out var node) || node is not JsonArray stepsArray)
        {
            return [];
        }

        var steps = new List<CreateApprovalStepInput>();
        foreach (var stepNode in stepsArray.OfType<JsonObject>())
        {
            var stepPayload = stepNode.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var sequenceNo = ReadInt(stepPayload, "sequenceNo") ?? 0;
            var approverType = ReadString(stepPayload, "approverType");
            var approverRef = ReadString(stepPayload, "approverRef");
            if (sequenceNo > 0 && !string.IsNullOrWhiteSpace(approverType) && !string.IsNullOrWhiteSpace(approverRef))
            {
                steps.Add(new CreateApprovalStepInput(sequenceNo, approverType, approverRef));
            }
        }

        return steps;
    }
}
