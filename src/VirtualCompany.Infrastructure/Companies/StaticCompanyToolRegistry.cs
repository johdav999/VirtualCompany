using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class StaticCompanyToolRegistry : ICompanyToolRegistry
{
    private static readonly IReadOnlySet<ToolActionType> StandardActions = new HashSet<ToolActionType>
    {
        ToolActionType.Read,
        ToolActionType.Recommend,
        ToolActionType.Execute
    };

    private readonly IReadOnlyDictionary<string, TrustedToolRegistration> _tools;
    private readonly IReadOnlyDictionary<string, ToolDefinitionManifest> _definitions;

    public StaticCompanyToolRegistry()
    {
        var taskScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "tasks" };
        var approvalScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "approvals" };
        var knowledgeScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "knowledge" };
        var paymentsScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "payments" };
        var financeScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "finance" };

        var registrations = new[]
        {
            Register("tasks.get", new HashSet<ToolActionType> { ToolActionType.Read }, taskScopes),
            Register("tasks.list", new HashSet<ToolActionType> { ToolActionType.Read }, taskScopes),
            Register("tasks.update_status", new HashSet<ToolActionType> { ToolActionType.Execute }, taskScopes),
            Register("approvals.create_request", new HashSet<ToolActionType> { ToolActionType.Execute }, approvalScopes),
            Register("knowledge.search", new HashSet<ToolActionType> { ToolActionType.Read, ToolActionType.Recommend }, knowledgeScopes),
            Register("erp", new HashSet<ToolActionType> { ToolActionType.Execute }, paymentsScopes)
        }.Concat(FinanceToolDefinitions.Select(definition =>
            Register(
                definition.ToolName,
                new HashSet<ToolActionType> { definition.ActionType },
                financeScopes,
                definition.Version,
                definition.InputSchema,
                definition.OutputSchema)));

        _tools = registrations.ToDictionary(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
        _definitions = FinanceToolDefinitions.ToDictionary(x => x.ToolName, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ToolDefinitionManifest> FinanceToolDefinitions { get; } =
    [
        FinanceDefinition("get_cash_balance", ToolActionType.Read, FinanceInputSchemas.AsOfDate(), FinanceOutputSchemas.WithDataProperty("cashBalance")),
        FinanceDefinition("list_transactions", ToolActionType.Read, FinanceInputSchemas.ListRange(), FinanceOutputSchemas.WithDataProperty("transactions")),
        FinanceDefinition("resolve_finance_agent_query", ToolActionType.Read, FinanceInputSchemas.AgentQuery(), FinanceOutputSchemas.WithDataProperty("result")),
        FinanceDefinition("list_uncategorized_transactions", ToolActionType.Read, FinanceInputSchemas.ListRange(), FinanceOutputSchemas.WithDataProperty("transactions")),
        FinanceDefinition("list_invoices_awaiting_approval", ToolActionType.Read, FinanceInputSchemas.ListRange(), FinanceOutputSchemas.WithDataProperty("invoices")),
        FinanceDefinition("get_profit_and_loss_summary", ToolActionType.Read, FinanceInputSchemas.ProfitAndLoss(), FinanceOutputSchemas.WithDataProperty("profitAndLossSummary")),
        FinanceDefinition("recommend_transaction_category", ToolActionType.Recommend, FinanceInputSchemas.TransactionRecommendation(), FinanceOutputSchemas.WithDataProperty("recommendation")),
        FinanceDefinition("recommend_invoice_approval_decision", ToolActionType.Recommend, FinanceInputSchemas.InvoiceRecommendation(), FinanceOutputSchemas.WithDataProperty("recommendation")),
        FinanceDefinition("evaluate_transaction_anomaly", ToolActionType.Recommend, FinanceInputSchemas.TransactionAnomalyEvaluation(), FinanceOutputSchemas.WithDataProperty("anomalyEvaluation")),
        FinanceDefinition("categorize_transaction", ToolActionType.Execute, FinanceInputSchemas.CategorizeTransaction(), FinanceOutputSchemas.WithDataProperty("transaction")),
        FinanceDefinition("approve_invoice", ToolActionType.Execute, FinanceInputSchemas.ApproveInvoice(), FinanceOutputSchemas.WithDataProperty("invoice"))
    ];

    public bool TryGetToolDefinition(string toolName, out ToolDefinitionManifest definition)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            definition = default!;
            return false;
        }

        return _definitions.TryGetValue(toolName.Trim(), out definition!);
    }

    public bool TryGetTool(string toolName, out TrustedToolRegistration registration)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            registration = default!;
            return false;
        }

        return _tools.TryGetValue(toolName.Trim(), out registration!);
    }

    public IReadOnlyList<TrustedToolRegistration> ListTools() =>
        _tools.Values.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToArray();

    public IReadOnlyList<ToolDefinitionManifest> ListToolDefinitions() =>
        _definitions.Values.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToArray();

    private static TrustedToolRegistration Register(
        string toolName,
        IReadOnlySet<ToolActionType>? supportedActions = null,
        IReadOnlySet<string>? scopes = null,
        string version = "1.0.0",
        JsonObject? inputSchema = null,
        JsonObject? outputSchema = null) =>
        new(
            toolName,
            supportedActions ?? StandardActions,
            scopes ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            version,
            inputSchema?.DeepClone().AsObject(),
            outputSchema?.DeepClone().AsObject());

    private static ToolDefinitionManifest FinanceDefinition(
        string toolName,
        ToolActionType actionType,
        JsonObject inputSchema,
        JsonObject outputSchema) =>
        new(toolName, "1.0.0", actionType, inputSchema, outputSchema);

    private static class FinanceInputSchemas
    {
        public static JsonObject AsOfDate() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "asOfUtc": { "type": "string", "format": "date-time" }
                  }
                }
                """);

        public static JsonObject ListRange() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "properties": {
                    "startUtc": { "type": "string", "format": "date-time" },
                    "endUtc": { "type": "string", "format": "date-time" },
                    "limit": { "type": "integer", "minimum": 1, "maximum": 500 }
                  }
                }
                """);

        public static JsonObject AgentQuery() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "queryText" ],
                  "properties": {
                    "queryText": { "type": "string", "minLength": 1, "maxLength": 200 },
                    "asOfUtc": { "type": "string", "format": "date-time" }
                  }
                }
                """);

        public static JsonObject ProfitAndLoss() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "year", "month" ],
                  "properties": {
                    "year": { "type": "integer", "minimum": 2000, "maximum": 2100 },
                    "month": { "type": "integer", "minimum": 1, "maximum": 12 }
                  }
                }
                """);

        public static JsonObject TransactionRecommendation() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "transactionId" ],
                  "properties": {
                    "transactionId": { "type": "string", "format": "uuid" },
                    "candidateCategory": { "type": "string", "minLength": 1, "maxLength": 64 }
                  }
                }
                """);

        public static JsonObject InvoiceRecommendation() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "invoiceId" ],
                  "properties": {
                    "invoiceId": { "type": "string", "format": "uuid" },
                    "candidateStatus": {
                      "type": "string",
                      "enum": [ "approved", "rejected" ]
                    }
                  }
                }
                """);

        public static JsonObject TransactionAnomalyEvaluation() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "transactionId" ],
                  "properties": {
                    "transactionId": { "type": "string", "format": "uuid" },
                    "workflowInstanceId": { "type": "string", "format": "uuid" }
                  }
                }
                """);

        public static JsonObject CategorizeTransaction() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "transactionId", "category" ],
                  "properties": {
                    "transactionId": { "type": "string", "format": "uuid" },
                    "category": { "type": "string", "minLength": 1, "maxLength": 64 }
                  }
                }
                """);

        public static JsonObject ApproveInvoice() =>
            ParseSchema(
                """
                {
                  "type": "object",
                  "additionalProperties": false,
                  "required": [ "invoiceId" ],
                  "properties": {
                    "invoiceId": { "type": "string", "format": "uuid" },
                    "status": {
                      "type": "string",
                      "enum": [ "approved", "rejected" ]
                    }
                  }
                }
                """);
    }

    private static class FinanceOutputSchemas
    {
        public static JsonObject WithDataProperty(string dataProperty) =>
            ParseSchema(
                $$"""
                {
                  "type": "object",
                  "required": [ "schemaVersion", "status", "success", "userSafeSummary", "data" ],
                  "properties": {
                    "schemaVersion": { "type": "string" },
                    "status": { "type": "string" },
                    "success": { "type": "boolean" },
                    "userSafeSummary": { "type": "string" },
                    "data": {
                      "type": "object",
                      "required": [ "{{dataProperty}}" ],
                      "properties": {
                        "{{dataProperty}}": { "type": [ "object", "array" ] }
                      }
                    }
                  }
                }
                """);
    }

    private static JsonObject ParseSchema(string schema) =>
        JsonNode.Parse(schema)!.AsObject();
}