using System.Text.Json;
using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Policies;

public static class MemoryContentSafetyPolicy
{
    private static readonly string[] UnsafeFieldNames =
    [
        "analysis",
        "chainofthought",
        "cot",
        "deliberation",
        "hiddenreasoning",
        "internalnotes",
        "internalreasoning",
        "privatenotes",
        "privatereasoning",
        "rawreasoning",
        "reasoning",
        "scratchpad",
        "thoughtprocess",
        "thinking",
        "workingnotes"
    ];

    private static readonly string[] UnsafeContentMarkers =
    [
        "chain of thought",
        "chain-of-thought",
        "hidden reasoning",
        "internal reasoning",
        "private reasoning",
        "private notes",
        "raw reasoning",
        "scratchpad",
        "thought process",
        "working notes",
        "<thinking>",
        "</thinking>"
    ];

    public static bool TryValidateSummary(string? summary, out string error)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            error = "Summary is required.";
            return false;
        }

        if (ContainsUnsafeContent(summary))
        {
            error = "Summary must be a sanitized memory summary and must not contain raw chain-of-thought, hidden reasoning, or scratchpad content.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static bool TryValidateMetadata(IReadOnlyDictionary<string, JsonNode?>? metadata, out string error)
    {
        if (metadata is null || metadata.Count == 0)
        {
            error = string.Empty;
            return true;
        }

        foreach (var pair in metadata)
        {
            var normalizedKey = pair.Key?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedKey))
            {
                continue;
            }

            if (IsUnsafeFieldName(normalizedKey))
            {
                error = $"Metadata field '{normalizedKey}' is not allowed because memory persistence must not store raw reasoning or scratchpad content.";
                return false;
            }

            if (TryFindUnsafeNode(pair.Value, normalizedKey, out var unsafePath))
            {
                error = $"Metadata field '{unsafePath}' contains content that looks like raw reasoning or scratchpad text and cannot be persisted.";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    public static IReadOnlyList<string> FindUnsafeAdditionalProperties(IReadOnlyDictionary<string, JsonElement>? additionalProperties)
    {
        if (additionalProperties is null || additionalProperties.Count == 0)
        {
            return Array.Empty<string>();
        }

        return additionalProperties.Keys
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key.Trim())
            .Where(IsUnsafeFieldName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryFindUnsafeNode(JsonNode? node, string path, out string unsafePath)
    {
        switch (node)
        {
            case null:
                unsafePath = string.Empty;
                return false;

            case JsonObject jsonObject:
                foreach (var pair in jsonObject)
                {
                    var childKey = pair.Key?.Trim();
                    if (string.IsNullOrWhiteSpace(childKey))
                    {
                        continue;
                    }

                    var childPath = AppendPath(path, childKey);
                    if (IsUnsafeFieldName(childKey))
                    {
                        unsafePath = childPath;
                        return true;
                    }

                    if (TryFindUnsafeNode(pair.Value, childPath, out unsafePath))
                    {
                        return true;
                    }
                }

                break;

            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    if (TryFindUnsafeNode(jsonArray[index], $"{path}[{index}]", out unsafePath))
                    {
                        return true;
                    }
                }

                break;

            case JsonValue jsonValue:
                if (TryGetString(jsonValue, out var stringValue) && ContainsUnsafeContent(stringValue))
                {
                    unsafePath = path;
                    return true;
                }

                break;
        }

        unsafePath = string.Empty;
        return false;
    }

    private static bool TryGetString(JsonValue value, out string result)
    {
        try
        {
            result = value.GetValue<string>();
            return true;
        }
        catch (InvalidOperationException)
        {
            result = string.Empty;
            return false;
        }
    }

    private static bool ContainsUnsafeContent(string value) =>
        UnsafeContentMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static bool IsUnsafeFieldName(string key)
    {
        var normalized = NormalizeKey(key);
        return UnsafeFieldNames.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeKey(string key) =>
        string.Concat(key.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static string AppendPath(string path, string key) =>
        string.IsNullOrWhiteSpace(path)
            ? key
            : $"{path}.{key}";
}
