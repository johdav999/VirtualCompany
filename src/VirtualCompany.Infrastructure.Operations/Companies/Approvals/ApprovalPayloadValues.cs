using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

internal static class ApprovalPayloadValues
{
    internal static string? TryReadString(IReadOnlyDictionary<string, JsonNode?> nodes, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!nodes.TryGetValue(key, out var node) || node is null)
            {
                continue;
            }

            if (node is JsonValue value && value.TryGetValue<string>(out var stringValue) && !string.IsNullOrWhiteSpace(stringValue))
            {
                return stringValue.Trim();
            }

            var text = node.ToJsonString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim().Trim('"');
            }
        }

        return null;
    }

    internal static decimal? TryGetDecimal(IReadOnlyDictionary<string, JsonNode?>? nodes, string key)
    {
        if (nodes is null || !nodes.TryGetValue(key, out var node) || node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && decimal.TryParse(text, out number) ? number : null;
    }

    internal static string? GetRejectionComment(ApprovalRequest approval) => approval.Steps.FirstOrDefault(step => step.Status == ApprovalStepStatus.Rejected)?.Comment;
    internal static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?> nodes) => nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);
}
