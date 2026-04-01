using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class NoOpCompanyToolExecutor : ICompanyToolExecutor
{
    public Task<ToolExecutionResult> ExecuteAsync(ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = JsonValue.Create("stub"),
            ["toolName"] = JsonValue.Create(request.ToolName),
            ["actionType"] = JsonValue.Create(request.ActionType),
            ["scope"] = string.IsNullOrWhiteSpace(request.Scope) ? null : JsonValue.Create(request.Scope),
            ["executedAtUtc"] = JsonValue.Create(DateTime.UtcNow)
        };

        return Task.FromResult(new ToolExecutionResult(
            $"Executed '{request.ToolName}' using the default infrastructure stub.",
            payload));
    }
}