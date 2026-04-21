using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

// Adapter boundary: policy-enforced orchestration calls this executor, which then invokes typed application contracts.
public sealed class NoOpCompanyToolExecutor : ICompanyToolExecutor
{
    private readonly ICompanyToolRegistry _toolRegistry;
    private readonly IInternalCompanyToolContract _internalToolContract;

    public NoOpCompanyToolExecutor(
        ICompanyToolRegistry toolRegistry,
        IInternalCompanyToolContract internalToolContract)
    {
        _toolRegistry = toolRegistry;
        _internalToolContract = internalToolContract;
    }

    public async Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var executionId = request.ExecutionId == Guid.Empty ? Guid.NewGuid() : request.ExecutionId;
        if (!_toolRegistry.TryGetTool(request.ToolName, out var registration))
        {
            return CreateRegistryDeniedResult(request, executionId, "unregistered_tool", "The requested tool is not registered for trusted execution.");
        }

        if (!registration.Supports(request.ActionType, request.Scope))
        {
            return CreateRegistryDeniedResult(request, executionId, "unsupported_tool_action", "The requested tool action is not registered for trusted execution.");
        }

        if (registration.InputSchema is not null &&
            !ToolJsonSchemaValidator.Validate(request.RequestPayload, registration.InputSchema, out var inputErrors))
        {
            return CreateSchemaDeniedResult(
                request,
                executionId,
                "input_payload_schema_validation_failed",
                "The requested tool payload did not match the registered input schema.",
                inputErrors);
        }

        var context = new InternalToolExecutionContext(
            request.CompanyId,
            request.AgentId,
            executionId,
            request.ActionType,
            request.Scope,
            request.TaskId,
            request.WorkflowInstanceId,
            request.CorrelationId,
            request.ToolVersion ?? registration.Version);

        var response = await _internalToolContract.ExecuteAsync(
            new InternalToolExecutionRequest(request.ToolName, context, request.RequestPayload),
            cancellationToken);

        var payload = response.ToSafePayload();
        if (registration.OutputSchema is not null &&
            !ToolJsonSchemaValidator.Validate(payload, registration.OutputSchema, out var outputErrors))
        {
            return CreateSchemaDeniedResult(
                request,
                executionId,
                "output_payload_schema_validation_failed",
                "The tool response did not match the registered output schema.",
                outputErrors,
                payload);
        }

        var metadata = CloneNodes(response.Metadata);
        metadata["contractSchemaVersion"] = JsonValue.Create(InternalToolExecutionResponse.SchemaVersion);
        metadata["executionId"] = JsonValue.Create(executionId);
        metadata["toolVersion"] = JsonValue.Create(registration.Version);

        return response.Success
            ? ToolExecutionResult.Succeeded(request.ToolName, request.ActionType, payload, metadata)
            : ToolExecutionResult.Failed(
                request.ToolName,
                request.ActionType,
                response.Status,
                string.IsNullOrWhiteSpace(response.ErrorCode) ? "internal_tool_failed" : response.ErrorCode,
                response.UserSafeSummary,
                payload,
                metadata);
    }

    private static ToolExecutionResult CreateRegistryDeniedResult(
        ToolExecutionRequest request,
        Guid executionId,
        string errorCode,
        string userSafeSummary)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create(ToolExecutionResult.SchemaVersion),
            ["status"] = JsonValue.Create(ToolExecutionStatus.Denied.ToStorageValue()),
            ["success"] = JsonValue.Create(false),
            ["toolName"] = JsonValue.Create(request.ToolName),
            ["actionType"] = JsonValue.Create(request.ActionType.ToStorageValue()),
            ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
            ["userSafeSummary"] = JsonValue.Create(userSafeSummary),
            ["executionId"] = JsonValue.Create(executionId)
        };

        var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["executionBoundary"] = JsonValue.Create("policy_enforced_tool_executor"),
            ["registryDecision"] = JsonValue.Create("deny"),
            ["registryReason"] = JsonValue.Create(errorCode),
            ["modelOutputTrusted"] = JsonValue.Create(false),
            ["executionId"] = JsonValue.Create(executionId)
        };

        return ToolExecutionResult.Failed(request.ToolName, request.ActionType, ToolExecutionStatus.Denied.ToStorageValue(), errorCode, userSafeSummary, payload, metadata);
    }

    private static ToolExecutionResult CreateSchemaDeniedResult(
        ToolExecutionRequest request,
        Guid executionId,
        string errorCode,
        string userSafeSummary,
        IReadOnlyList<string> schemaErrors,
        Dictionary<string, JsonNode?>? responsePayload = null)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create(ToolExecutionResult.SchemaVersion),
            ["status"] = JsonValue.Create(ToolExecutionStatus.Denied.ToStorageValue()),
            ["success"] = JsonValue.Create(false),
            ["toolName"] = JsonValue.Create(request.ToolName),
            ["actionType"] = JsonValue.Create(request.ActionType.ToStorageValue()),
            ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
            ["userSafeSummary"] = JsonValue.Create(userSafeSummary),
            ["executionId"] = JsonValue.Create(executionId),
            ["schemaErrors"] = new JsonArray(schemaErrors.Select(static error => (JsonNode?)JsonValue.Create(error)).ToArray())
        };

        if (responsePayload is not null)
        {
            payload["responsePayload"] = new JsonObject(CloneNodes(responsePayload)
                .Select(pair => KeyValuePair.Create(pair.Key, pair.Value))
                .ToArray());
        }

        var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["executionBoundary"] = JsonValue.Create("policy_enforced_tool_executor"),
            ["registryDecision"] = JsonValue.Create("deny"),
            ["registryReason"] = JsonValue.Create(errorCode),
            ["modelOutputTrusted"] = JsonValue.Create(false),
            ["executionId"] = JsonValue.Create(executionId)
        };

        return ToolExecutionResult.Failed(request.ToolName, request.ActionType, ToolExecutionStatus.Denied.ToStorageValue(), errorCode, userSafeSummary, payload, metadata);
    }

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed class NoOpInternalCompanyToolContract : IInternalCompanyToolContract
{
    public Task<InternalToolExecutionResponse> ExecuteAsync(
        InternalToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Context.CompanyId == Guid.Empty)
        {
            return Task.FromResult(InternalToolExecutionResponse.Failed(
                "failed",
                "company_context_required",
                "Tool execution requires a company context."));
        }

        if (request.Context.AgentId == Guid.Empty)
        {
            return Task.FromResult(InternalToolExecutionResponse.Failed(
                "failed",
                "agent_context_required",
                "Tool execution requires an agent context."));
        }

        if (request.Context.ExecutionId == Guid.Empty)
        {
            return Task.FromResult(InternalToolExecutionResponse.Failed(
                "failed",
                "execution_context_required",
                "Tool execution requires an execution context."));
        }

        if (TryCreateFinanceToolData(request.ToolName, out var financeData))
        {
            return Task.FromResult(InternalToolExecutionResponse.Succeeded(
                "Finance tool execution completed through the internal contract boundary.",
                financeData,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["contractName"] = JsonValue.Create(nameof(NoOpInternalCompanyToolContract))
                }));
        }

        var data = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = JsonValue.Create("contract_stub"),
            ["toolName"] = JsonValue.Create(request.ToolName),
            ["actionType"] = JsonValue.Create(request.ActionType),
            ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
            ["companyId"] = JsonValue.Create(request.CompanyId),
            ["agentId"] = JsonValue.Create(request.AgentId),
            ["executionId"] = JsonValue.Create(request.ExecutionId),
            ["executedAtUtc"] = JsonValue.Create(DateTime.UtcNow)
        };

        return Task.FromResult(InternalToolExecutionResponse.Succeeded(
            "Tool execution completed through the internal contract boundary.",
            data,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["contractName"] = JsonValue.Create(nameof(NoOpInternalCompanyToolContract))
            }));
    }

    private static bool TryCreateFinanceToolData(string toolName, out Dictionary<string, JsonNode?> data)
    {
        data = toolName switch
        {
            "get_cash_balance" => new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["cashBalance"] = new JsonObject
                {
                    ["amount"] = JsonValue.Create(0m),
                    ["currency"] = JsonValue.Create("USD"),
                    ["accounts"] = new JsonArray()
                }
            },
            "resolve_finance_agent_query" => new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["result"] = new JsonObject
                {
                    ["intent"] = JsonValue.Create(FinanceAgentQueryIntents.WhatShouldIPayThisWeek),
                    ["summary"] = JsonValue.Create("Selected 0 payable item(s) for the current company week.")
                }
            },
            "list_transactions" or "list_uncategorized_transactions" => new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transactions"] = new JsonArray()
            },
            "list_invoices_awaiting_approval" => new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoices"] = new JsonArray()
            },
            "get_profit_and_loss_summary" => new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["profitAndLossSummary"] = new JsonObject
                {
                    ["revenue"] = JsonValue.Create(0m),
                    ["expenses"] = JsonValue.Create(0m),
                    ["netResult"] = JsonValue.Create(0m),
                    ["currency"] = JsonValue.Create("USD")
                }
            },
            _ => []
        };

        return data.Count > 0;
    }
}
