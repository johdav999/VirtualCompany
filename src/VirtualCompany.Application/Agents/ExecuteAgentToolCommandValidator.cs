using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public static class ExecuteAgentToolCommandValidator
{
    private static readonly HashSet<string> DirectExternalExecutionPayloadKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "endpoint",
        "endpointUrl",
        "url",
        "uri",
        "httpMethod",
        "headers",
        "authorization",
        "token",
        "accessToken",
        "apiKey",
        "connectionString",
        "sql",
        "sqlText",
        "rawSql",
        "typeName",
        "methodName",
        "serviceType",
        "adapterType",
        "filePath"
    };

    public static void ValidateAndThrow(ExecuteAgentToolCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(command.ToolName))
        {
            AddError(errors, nameof(command.ToolName), "ToolName is required.");
        }
        else if (command.ToolName.Trim().Length > 100)
        {
            AddError(errors, nameof(command.ToolName), "ToolName must be 100 characters or fewer.");
        }

        if (!ToolActionTypeValues.TryParse(command.ActionType, out _))
        {
            AddError(errors, nameof(command.ActionType), ToolActionTypeValues.BuildValidationMessage(command.ActionType));
        }

        if (!string.IsNullOrWhiteSpace(command.Scope) && command.Scope.Trim().Length > 100)
        {
            AddError(errors, nameof(command.Scope), "Scope must be 100 characters or fewer.");
        }

        var hasThresholdCategory = !string.IsNullOrWhiteSpace(command.ThresholdCategory);
        var hasThresholdKey = !string.IsNullOrWhiteSpace(command.ThresholdKey);
        var hasThresholdValue = command.ThresholdValue.HasValue;

        if (hasThresholdValue && command.ThresholdValue < 0)
        {
            AddError(errors, nameof(command.ThresholdValue), "ThresholdValue must be non-negative.");
        }

        if (hasThresholdCategory || hasThresholdKey || hasThresholdValue)
        {
            if (!hasThresholdCategory)
            {
                AddError(errors, nameof(command.ThresholdCategory), "ThresholdCategory is required when threshold context is provided.");
            }

            if (!hasThresholdKey)
            {
                AddError(errors, nameof(command.ThresholdKey), "ThresholdKey is required when threshold context is provided.");
            }

            if (!hasThresholdValue)
            {
                AddError(errors, nameof(command.ThresholdValue), "ThresholdValue is required when threshold context is provided.");
            }
        }

        if (command.RequestPayload is not null)
        {
            foreach (var path in FindDirectExternalExecutionPayloadPaths(command.RequestPayload))
            {
                AddError(errors, $"{nameof(command.RequestPayload)}.{path}", "Tool requests cannot specify direct external execution details.");
            }
        }

        if (errors.Count > 0)
        {
            throw new AgentExecutionValidationException(
                errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static IEnumerable<string> FindDirectExternalExecutionPayloadPaths(IReadOnlyDictionary<string, JsonNode?> payload)
    {
        foreach (var (key, value) in payload)
        {
            foreach (var path in FindDirectExternalExecutionPayloadPaths(key, value))
            {
                yield return path;
            }
        }
    }

    private static IEnumerable<string> FindDirectExternalExecutionPayloadPaths(string path, JsonNode? node)
    {
        var key = path.Split('.', '[')[^1];
        if (DirectExternalExecutionPayloadKeys.Contains(key))
        {
            yield return path;
        }

        if (node is JsonObject jsonObject)
        {
            foreach (var (childKey, childValue) in jsonObject)
            {
                foreach (var childPath in FindDirectExternalExecutionPayloadPaths($"{path}.{childKey}", childValue))
                {
                    yield return childPath;
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            for (var index = 0; index < jsonArray.Count; index++)
            {
                foreach (var childPath in FindDirectExternalExecutionPayloadPaths($"{path}[{index}]", jsonArray[index]))
                {
                    yield return childPath;
                }
            }
        }
    }

    private static void AddError(
        IDictionary<string, List<string>> errors,
        string key,
        string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }
}