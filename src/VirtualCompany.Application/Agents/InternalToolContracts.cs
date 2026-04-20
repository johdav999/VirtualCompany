using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public sealed record InternalToolExecutionContext(
    Guid CompanyId,
    Guid AgentId,
    Guid ExecutionId,
    ToolActionType ActionType,
    string? Scope,
    Guid? TaskId = null,
    Guid? WorkflowInstanceId = null,
    string? CorrelationId = null,
    string? ToolVersion = null);

public sealed record InternalToolExecutionRequest(
    string ToolName,
    InternalToolExecutionContext Context,
    IReadOnlyDictionary<string, JsonNode?> Payload)
{
    public Guid CompanyId => Context.CompanyId;
    public Guid AgentId => Context.AgentId;
    public Guid ExecutionId => Context.ExecutionId;
    public ToolActionType ActionKind => Context.ActionType;
    public string ActionType => Context.ActionType.ToStorageValue();
    public string? Scope => Context.Scope;
    public Guid? TaskId => Context.TaskId;
    public Guid? WorkflowInstanceId => Context.WorkflowInstanceId;
    public string? CorrelationId => Context.CorrelationId;
    public string? ToolVersion => Context.ToolVersion;
}

public sealed record InternalToolExecutionResponse(
    bool Success,
    string Status,
    string UserSafeSummary,
    Dictionary<string, JsonNode?> Data,
    Dictionary<string, JsonNode?> Metadata,
    string? ErrorCode = null)
{
    public const string SchemaVersion = "2026-04-13";

    public static InternalToolExecutionResponse Succeeded(
        string userSafeSummary,
        Dictionary<string, JsonNode?> data,
        Dictionary<string, JsonNode?>? metadata = null) =>
        new(
            true,
            "executed",
            string.IsNullOrWhiteSpace(userSafeSummary) ? "Tool execution completed." : userSafeSummary.Trim(),
            CloneNodes(data),
            CloneNodes(metadata));

    public static InternalToolExecutionResponse Failed(
        string status,
        string errorCode,
        string userSafeSummary,
        Dictionary<string, JsonNode?>? data = null,
        Dictionary<string, JsonNode?>? metadata = null) =>
        new(
            false,
            string.IsNullOrWhiteSpace(status) ? "failed" : status.Trim(),
            string.IsNullOrWhiteSpace(userSafeSummary) ? "Tool execution failed." : userSafeSummary.Trim(),
            CloneNodes(data),
            CloneNodes(metadata),
            string.IsNullOrWhiteSpace(errorCode) ? "internal_tool_failed" : errorCode.Trim());

    public Dictionary<string, JsonNode?> ToSafePayload()
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["schemaVersion"] = JsonValue.Create(SchemaVersion),
            ["status"] = JsonValue.Create(Status),
            ["success"] = JsonValue.Create(Success),
            ["userSafeSummary"] = JsonValue.Create(UserSafeSummary),
            ["errorCode"] = string.IsNullOrWhiteSpace(ErrorCode) ? null : JsonValue.Create(ErrorCode),
            ["data"] = ToJsonObject(Data),
            ["metadata"] = ToJsonObject(Metadata)
        };

        foreach (var (key, value) in Data)
        {
            payload.TryAdd(key, value?.DeepClone());
        }

        return payload;
    }

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, JsonNode?> values) =>
        new(CloneNodes(values).Select(pair => KeyValuePair.Create(pair.Key, pair.Value)).ToArray());

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?>? nodes) =>
        nodes is null || nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}

public sealed record TrustedToolRegistration(
    string ToolName,
    IReadOnlySet<ToolActionType> SupportedActions,
    IReadOnlySet<string> Scopes,
    string Version = "1.0.0",
    JsonObject? InputSchema = null,
    JsonObject? OutputSchema = null)
{
    private static readonly Regex SemanticVersionPattern = new(
        @"^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$",
        RegexOptions.Compiled);

    public bool Supports(ToolActionType actionType, string? scope)
    {
        if (!SupportedActions.Contains(actionType))
        {
            return false;
        }

        if (!SemanticVersionPattern.IsMatch(Version))
        {
            return false;
        }

        return Scopes.Count == 0 ||
            (!string.IsNullOrWhiteSpace(scope) && Scopes.Contains(scope.Trim()));
    }
}

public sealed record ToolDefinitionManifest(
    string ToolName,
    string Version,
    ToolActionType ActionType,
    JsonObject InputSchema,
    JsonObject OutputSchema);

public interface ICompanyToolRegistry
{
    bool TryGetTool(string toolName, out TrustedToolRegistration registration);

    IReadOnlyList<TrustedToolRegistration> ListTools();

    bool TryGetToolDefinition(string toolName, out ToolDefinitionManifest definition);

    IReadOnlyList<ToolDefinitionManifest> ListToolDefinitions();
}

public interface IInternalCompanyToolContract
{
    Task<InternalToolExecutionResponse> ExecuteAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken);
}