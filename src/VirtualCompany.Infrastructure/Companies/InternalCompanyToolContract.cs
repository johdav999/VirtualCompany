using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class InternalCompanyToolContract : IInternalCompanyToolContract
{
    private readonly ICompanyTaskQueryService _taskQueryService;
    private readonly ICompanyTaskCommandService _taskCommandService;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly ICompanyKnowledgeSearchService _knowledgeSearchService;
    private readonly IFinanceToolProvider _financeToolProvider;
    private readonly IFinanceTransactionAnomalyDetectionService _financeAnomalyDetectionService;

    public InternalCompanyToolContract(
        ICompanyTaskQueryService taskQueryService,
        ICompanyTaskCommandService taskCommandService,
        IApprovalRequestService approvalRequestService,
        ICompanyKnowledgeSearchService knowledgeSearchService,
        IFinanceToolProvider financeToolProvider,
        IFinanceTransactionAnomalyDetectionService financeAnomalyDetectionService)
    {
        _taskQueryService = taskQueryService;
        _taskCommandService = taskCommandService;
        _approvalRequestService = approvalRequestService;
        _knowledgeSearchService = knowledgeSearchService;
        _financeToolProvider = financeToolProvider;
        _financeAnomalyDetectionService = financeAnomalyDetectionService;
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
                "get_cash_balance" => await ExecuteGetCashBalanceAsync(request, cancellationToken),
                "list_transactions" => await ExecuteListTransactionsAsync(request, cancellationToken),
                "list_uncategorized_transactions" => await ExecuteListUncategorizedTransactionsAsync(request, cancellationToken),
                "list_invoices_awaiting_approval" => await ExecuteListInvoicesAwaitingApprovalAsync(request, cancellationToken),
                "get_profit_and_loss_summary" => await ExecuteGetProfitAndLossSummaryAsync(request, cancellationToken),
                "recommend_transaction_category" => await ExecuteRecommendTransactionCategoryAsync(request, cancellationToken),
                "recommend_invoice_approval_decision" => await ExecuteRecommendInvoiceApprovalDecisionAsync(request, cancellationToken),
                "evaluate_transaction_anomaly" => await ExecuteEvaluateTransactionAnomalyAsync(request, cancellationToken),
                "categorize_transaction" => await ExecuteCategorizeTransactionAsync(request, cancellationToken),
                "approve_invoice" => await ExecuteApproveInvoiceAsync(request, cancellationToken),
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
        catch (ArgumentException)
        {
            return Failed("finance_tool_validation_failed", "The finance tool request was not valid.");
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

    private async Task<InternalToolExecutionResponse> ExecuteGetCashBalanceAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var balance = await _financeToolProvider.GetCashBalanceAsync(
            new GetFinanceCashBalanceQuery(request.CompanyId, ReadDateTime(request.Payload, "asOfUtc")),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Cash balance was retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["cashBalance"] = Serialize(balance),
                ["amount"] = JsonValue.Create(balance.Amount),
                ["currency"] = JsonValue.Create(balance.Currency),
                ["asOfUtc"] = JsonValue.Create(balance.AsOfUtc)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteListTransactionsAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var transactions = await _financeToolProvider.GetTransactionsAsync(
            new GetFinanceTransactionsQuery(
                request.CompanyId,
                ReadDateTime(request.Payload, "startUtc"),
                ReadDateTime(request.Payload, "endUtc"),
                ReadInt(request.Payload, "limit") ?? 100),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Finance transactions were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transactions"] = Serialize(transactions),
                ["count"] = JsonValue.Create(transactions.Count)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteListUncategorizedTransactionsAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var transactions = await _financeToolProvider.GetTransactionsAsync(
            new GetFinanceTransactionsQuery(
                request.CompanyId,
                ReadDateTime(request.Payload, "startUtc"),
                ReadDateTime(request.Payload, "endUtc"),
                ReadInt(request.Payload, "limit") ?? 100),
            cancellationToken);

        var uncategorized = transactions
            .Where(transaction =>
                string.IsNullOrWhiteSpace(transaction.TransactionType) ||
                string.Equals(transaction.TransactionType, "uncategorized", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return InternalToolExecutionResponse.Succeeded(
            "Uncategorized finance transactions were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transactions"] = Serialize(uncategorized),
                ["count"] = JsonValue.Create(uncategorized.Count)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteListInvoicesAwaitingApprovalAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var invoices = await _financeToolProvider.GetInvoicesAsync(
            new GetFinanceInvoicesQuery(
                request.CompanyId,
                ReadDateTime(request.Payload, "startUtc"),
                ReadDateTime(request.Payload, "endUtc"),
                ReadInt(request.Payload, "limit") ?? 100),
            cancellationToken);

        var awaitingApproval = invoices
            .Where(invoice =>
                string.Equals(invoice.Status, "awaiting_approval", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(invoice.Status, "pending_approval", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(invoice.Status, "pending", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return InternalToolExecutionResponse.Succeeded(
            "Invoices awaiting approval were retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoices"] = Serialize(awaitingApproval),
                ["count"] = JsonValue.Create(awaitingApproval.Count)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteGetProfitAndLossSummaryAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Read, out var actionFailure))
        {
            return actionFailure;
        }

        var summary = await _financeToolProvider.GetMonthlyProfitAndLossAsync(
            new GetFinanceMonthlyProfitAndLossQuery(
                request.CompanyId,
                ReadInt(request.Payload, "year") ?? DateTime.UtcNow.Year,
                ReadInt(request.Payload, "month") ?? DateTime.UtcNow.Month),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Profit and loss summary was retrieved.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["profitAndLossSummary"] = Serialize(summary),
                ["revenue"] = JsonValue.Create(summary.Revenue),
                ["expenses"] = JsonValue.Create(summary.Expenses),
                ["netResult"] = JsonValue.Create(summary.NetResult),
                ["currency"] = JsonValue.Create(summary.Currency)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteRecommendTransactionCategoryAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var actionFailure))
        {
            return actionFailure;
        }

        var transactionId = ReadGuid(request.Payload, "transactionId");
        if (!transactionId.HasValue)
        {
            return Failed("transaction_id_required", "A transaction id is required to recommend a category.");
        }

        var recommendation = await _financeToolProvider.RecommendTransactionCategoryAsync(request, cancellationToken);
        return InternalToolExecutionResponse.Succeeded(
            "Transaction category recommendation was prepared.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["recommendation"] = new JsonObject
                {
                    ["transactionId"] = JsonValue.Create(recommendation.TransactionId),
                    ["recommendedCategory"] = JsonValue.Create(recommendation.RecommendedCategory),
                    ["confidence"] = JsonValue.Create(recommendation.Confidence)
                }
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteRecommendInvoiceApprovalDecisionAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var actionFailure))
        {
            return actionFailure;
        }

        var invoiceId = ReadGuid(request.Payload, "invoiceId");
        if (!invoiceId.HasValue)
        {
            return Failed("invoice_id_required", "An invoice id is required to recommend an approval decision.");
        }

        var recommendation = await _financeToolProvider.RecommendInvoiceApprovalDecisionAsync(request, cancellationToken);
        return InternalToolExecutionResponse.Succeeded(
            "Invoice approval recommendation was prepared.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["recommendation"] = new JsonObject
                {
                    ["invoiceId"] = JsonValue.Create(recommendation.InvoiceId),
                    ["recommendedStatus"] = JsonValue.Create(recommendation.RecommendedStatus),
                    ["confidence"] = JsonValue.Create(recommendation.Confidence)
                }
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteEvaluateTransactionAnomalyAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Recommend, out var actionFailure))
        {
            return actionFailure;
        }

        var transactionId = ReadGuid(request.Payload, "transactionId");
        if (!transactionId.HasValue)
        {
            return Failed("transaction_id_required", "A transaction id is required to evaluate anomalies.");
        }

        var evaluation = await _financeAnomalyDetectionService.EvaluateAsync(
            new EvaluateFinanceTransactionAnomalyCommand(
                request.CompanyId,
                transactionId.Value,
                ReadGuid(request.Payload, "workflowInstanceId"),
                request.AgentId),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Transaction anomaly evaluation was completed.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["anomalyEvaluation"] = Serialize(evaluation),
                ["isAnomalous"] = JsonValue.Create(evaluation.IsAnomalous),
                ["anomalyCount"] = JsonValue.Create(evaluation.Anomalies.Count)
            },
            Metadata(request, "finance_anomaly_detection_service"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteCategorizeTransactionAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure))
        {
            return actionFailure;
        }

        var transactionId = ReadGuid(request.Payload, "transactionId");
        var category = ReadString(request.Payload, "category");
        if (!transactionId.HasValue || string.IsNullOrWhiteSpace(category))
        {
            return Failed("transaction_category_required", "A transaction id and category are required.");
        }

        var transaction = await _financeToolProvider.UpdateTransactionCategoryAsync(
            new UpdateFinanceTransactionCategoryCommand(request.CompanyId, transactionId.Value, category),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Transaction category was updated.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transaction"] = Serialize(transaction),
                ["transactionId"] = JsonValue.Create(transaction.Id),
                ["category"] = JsonValue.Create(transaction.TransactionType)
            },
            Metadata(request, "finance_tool_provider"));
    }

    private async Task<InternalToolExecutionResponse> ExecuteApproveInvoiceAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (!EnsureAction(request, ToolActionType.Execute, out var actionFailure))
        {
            return actionFailure;
        }

        var invoiceId = ReadGuid(request.Payload, "invoiceId");
        var status = ReadString(request.Payload, "status") ?? "approved";
        if (!invoiceId.HasValue)
        {
            return Failed("invoice_id_required", "An invoice id is required to approve an invoice.");
        }

        var invoice = await _financeToolProvider.UpdateInvoiceApprovalStatusAsync(
            new UpdateFinanceInvoiceApprovalStatusCommand(request.CompanyId, invoiceId.Value, status),
            cancellationToken);

        return InternalToolExecutionResponse.Succeeded(
            "Invoice approval status was updated.",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoice"] = Serialize(invoice),
                ["invoiceId"] = JsonValue.Create(invoice.Id),
                ["status"] = JsonValue.Create(invoice.Status)
            },
            Metadata(request, "finance_tool_provider"));
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
            ["toolVersion"] = string.IsNullOrWhiteSpace(request.ToolVersion) ? null : JsonValue.Create(request.ToolVersion),
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
