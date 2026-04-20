using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceToolDefinitionManifestTests : IClassFixture<TestWebApplicationFactory>
{
    private static readonly IReadOnlyDictionary<string, ToolActionType> FinanceTools = new Dictionary<string, ToolActionType>(StringComparer.OrdinalIgnoreCase)
    [
        ["get_cash_balance"] = ToolActionType.Read,
        ["list_transactions"] = ToolActionType.Read,
        ["list_uncategorized_transactions"] = ToolActionType.Read,
        ["list_invoices_awaiting_approval"] = ToolActionType.Read,
        ["get_profit_and_loss_summary"] = ToolActionType.Read,
        ["recommend_transaction_category"] = ToolActionType.Recommend,
        ["recommend_invoice_approval_decision"] = ToolActionType.Recommend,
        ["categorize_transaction"] = ToolActionType.Execute,
        ["approve_invoice"] = ToolActionType.Execute
    ];

    private readonly TestWebApplicationFactory _factory;

    public FinanceToolDefinitionManifestTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void Registry_exposes_finance_tool_definitions_with_versioned_schemas()
    {
        using var scope = _factory.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<ICompanyToolRegistry>();

        var definitions = registry.ListToolDefinitions();

        foreach (var (toolName, actionType) in FinanceTools)
        {
            var definition = Assert.Single(definitions.Where(x => x.ToolName == toolName));
            Assert.Equal("1.0.0", definition.Version);
            Assert.Equal(actionType, definition.ActionType);
            Assert.Equal("object", definition.InputSchema["type"]!.GetValue<string>());
            Assert.Equal("object", definition.OutputSchema["type"]!.GetValue<string>());

            Assert.True(registry.TryGetTool(toolName, out var registration));
            Assert.Equal(definition.Version, registration.Version);
            Assert.Equal(actionType, Assert.Single(registration.SupportedActions));
            Assert.Equal("finance", Assert.Single(registration.Scopes));
            Assert.True(registration.Supports(actionType, "finance"));
            Assert.False(registration.Supports(actionType == ToolActionType.Execute ? ToolActionType.Read : ToolActionType.Execute, "finance"));
        }
    }

    [Theory]
    [MemberData(nameof(ValidFinanceRequests))]
    public async Task Finance_tool_executor_accepts_payloads_matching_declared_input_schema(
        string toolName,
        ToolActionType actionType,
        Dictionary<string, JsonNode?> payload)
    {
        var contract = new StubInternalToolContract(validOutput: true);
        var executor = new NoOpCompanyToolExecutor(new StaticCompanyToolRegistry(), contract);

        var result = await executor.ExecuteAsync(CreateRequest(toolName, actionType, payload), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("executed", result.Status);
        Assert.Equal(1, contract.ExecutionCount);
    }

    [Theory]
    [MemberData(nameof(InvalidFinanceRequests))]
    public async Task Finance_tool_executor_rejects_payloads_that_do_not_match_declared_input_schema(
        string toolName,
        ToolActionType actionType,
        Dictionary<string, JsonNode?> payload)
    {
        var contract = new StubInternalToolContract(validOutput: true);
        var executor = new NoOpCompanyToolExecutor(new StaticCompanyToolRegistry(), contract);

        var result = await executor.ExecuteAsync(CreateRequest(toolName, actionType, payload), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("input_payload_schema_validation_failed", result.ErrorCode);
        Assert.Equal(0, contract.ExecutionCount);
    }

    [Theory]
    [MemberData(nameof(ValidFinanceRequests))]
    public async Task Finance_tool_executor_rejects_responses_that_do_not_match_declared_output_schema(
        string toolName,
        ToolActionType actionType,
        Dictionary<string, JsonNode?> payload)
    {
        var contract = new StubInternalToolContract(validOutput: false);
        var executor = new NoOpCompanyToolExecutor(new StaticCompanyToolRegistry(), contract);

        var result = await executor.ExecuteAsync(CreateRequest(toolName, actionType, payload), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("output_payload_schema_validation_failed", result.ErrorCode);
        Assert.Equal(1, contract.ExecutionCount);
    }

    public static IEnumerable<object[]> ValidFinanceRequests()
    {
        yield return
        [
            "get_cash_balance",
            ToolActionType.Read,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["asOfUtc"] = JsonValue.Create("2026-04-16T00:00:00Z")
            }
        ];

        yield return
        [
            "list_transactions",
            ToolActionType.Read,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["startUtc"] = JsonValue.Create("2026-04-01T00:00:00Z"),
                ["endUtc"] = JsonValue.Create("2026-04-16T00:00:00Z"),
                ["limit"] = JsonValue.Create(25)
            }
        ];

        yield return
        [
            "list_uncategorized_transactions",
            ToolActionType.Read,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["limit"] = JsonValue.Create(10)
            }
        ];

        yield return
        [
            "list_invoices_awaiting_approval",
            ToolActionType.Read,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["limit"] = JsonValue.Create(10)
            }
        ];

        yield return
        [
            "recommend_transaction_category",
            ToolActionType.Recommend,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transactionId"] = JsonValue.Create(Guid.NewGuid()),
                ["candidateCategory"] = JsonValue.Create("software")
            }
        ];

        yield return
        [
            "recommend_invoice_approval_decision",
            ToolActionType.Recommend,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoiceId"] = JsonValue.Create(Guid.NewGuid()),
                ["candidateStatus"] = JsonValue.Create("approved")
            }
        ];

        yield return
        [
            "categorize_transaction",
            ToolActionType.Execute,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transactionId"] = JsonValue.Create(Guid.NewGuid()),
                ["category"] = JsonValue.Create("software")
            }
        ];

        yield return
        [
            "approve_invoice",
            ToolActionType.Execute,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoiceId"] = JsonValue.Create(Guid.NewGuid()),
                ["status"] = JsonValue.Create("approved")
            }
        ];
    }

    public static IEnumerable<object[]> InvalidFinanceRequests()
    {
        yield return
        [
            "get_cash_balance",
            ToolActionType.Read,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["asOfUtc"] = JsonValue.Create("not-a-date")
            }
        ];

        yield return
        [
            "list_transactions",
            ToolActionType.Read,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["limit"] = JsonValue.Create(0)
            }
        ];

        yield return
        [
            "list_uncategorized_transactions",
            ToolActionType.Read,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["unexpected"] = JsonValue.Create(true)
            }
        ];

        yield return
        [
            "list_invoices_awaiting_approval",
            ToolActionType.Read,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["limit"] = JsonValue.Create(501)
            }
        ];

        yield return
        [
            "get_profit_and_loss_summary",
            ToolActionType.Read,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["year"] = JsonValue.Create(2026),
                ["month"] = JsonValue.Create(13)
            }
        ];

        yield return
        [
            "recommend_transaction_category",
            ToolActionType.Recommend,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["candidateCategory"] = JsonValue.Create("software")
            }
        ];

        yield return
        [
            "recommend_invoice_approval_decision",
            ToolActionType.Recommend,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoiceId"] = JsonValue.Create(Guid.NewGuid()),
                ["candidateStatus"] = JsonValue.Create("needs_more_context")
            }
        ];

        yield return
        [
            "categorize_transaction",
            ToolActionType.Execute,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transactionId"] = JsonValue.Create(Guid.NewGuid())
            }
        ];

        yield return
        [
            "approve_invoice",
            ToolActionType.Execute,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["invoiceId"] = JsonValue.Create(Guid.NewGuid()),
                ["status"] = JsonValue.Create("pending_review")
            }
        ];
    }

    private static ToolExecutionRequest CreateRequest(string toolName, ToolActionType actionType, Dictionary<string, JsonNode?> payload) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            toolName,
            actionType,
            "finance",
            payload,
            ExecutionId: Guid.NewGuid());

    private sealed class StubInternalToolContract : IInternalCompanyToolContract
    {
        private readonly bool _validOutput;

        public StubInternalToolContract(bool validOutput)
        {
            _validOutput = validOutput;
        }

        public int ExecutionCount { get; private set; }

        public Task<InternalToolExecutionResponse> ExecuteAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken)
        {
            ExecutionCount++;
            var data = _validOutput
                ? ValidData(request.ToolName)
                : new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["unexpected"] = JsonValue.Create(true)
                };

            return Task.FromResult(InternalToolExecutionResponse.Succeeded("Finance tool completed.", data));
        }

        private static Dictionary<string, JsonNode?> ValidData(string toolName) =>
            toolName switch
            {
                "get_cash_balance" => new(StringComparer.OrdinalIgnoreCase)
                {
                    ["cashBalance"] = new JsonObject { ["amount"] = JsonValue.Create(123.45m), ["currency"] = JsonValue.Create("USD") }
                },
                "list_transactions" or "list_uncategorized_transactions" => new(StringComparer.OrdinalIgnoreCase)
                {
                    ["transactions"] = new JsonArray()
                },
                "list_invoices_awaiting_approval" => new(StringComparer.OrdinalIgnoreCase)
                {
                    ["invoices"] = new JsonArray()
                },
                "get_profit_and_loss_summary" => new(StringComparer.OrdinalIgnoreCase)
                {
                    ["profitAndLossSummary"] = new JsonObject { ["netResult"] = JsonValue.Create(42m) }
                },
                "recommend_transaction_category" or "recommend_invoice_approval_decision" => new(StringComparer.OrdinalIgnoreCase)
                {
                    ["recommendation"] = new JsonObject
                    {
                        ["confidence"] = JsonValue.Create(0.8m)
                    }
                },
                "categorize_transaction" => new(StringComparer.OrdinalIgnoreCase)
                {
                    ["transaction"] = new JsonObject { ["category"] = JsonValue.Create("software") }
                },
                "approve_invoice" => new(StringComparer.OrdinalIgnoreCase)
                {
                    ["invoice"] = new JsonObject
                    {
                        ["status"] = JsonValue.Create("approved")
                    }
                },
                _ => []
            };
    }
}