using System.Text.Json.Nodes;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class KnowledgeAccessPolicyEvaluator : IKnowledgeAccessPolicyEvaluator
{
    // The PostgreSQL semantic retrieval query mirrors these keys so tenant and access
    // scope predicates are enforced before vector ordering and LIMIT are applied.
    internal static IReadOnlyList<string> RestrictedKeys { get; } = ["restricted", "is_restricted"];
    internal static IReadOnlyList<string> PrivateKeys { get; } = ["private", "is_private"];
    internal static IReadOnlyList<string> RoleKeys { get; } = ["roles", "allowed_roles", "membership_roles"];
    internal static IReadOnlyList<string> ScopeKeys { get; } = ["scopes", "data_scopes", "allowed_scopes", "read", "recommend", "execute", "write"];
    internal static IReadOnlyList<string> AgentKeys { get; } = ["agent_ids", "agents"];

    public bool CanAccess(CompanyKnowledgeAccessContext accessContext, CompanyKnowledgeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (accessContext.CompanyId == Guid.Empty || document.CompanyId != accessContext.CompanyId)
        {
            return false;
        }

        var accessScope = document.AccessScope;
        if (accessScope is null ||
            !string.Equals(accessScope.Visibility, CompanyKnowledgeDocumentAccessScope.CompanyVisibility, StringComparison.OrdinalIgnoreCase) ||
            (accessScope.CompanyId != Guid.Empty && accessScope.CompanyId != accessContext.CompanyId))
        {
            return false;
        }

        var properties = accessScope.AdditionalProperties;

        if (!EvaluateIdentifierConstraint(properties, RoleKeys, accessContext.MembershipRole))
        {
            return false;
        }

        if (!EvaluateScopeConstraint(properties, accessContext.DataScopes))
        {
            return false;
        }

        if (!EvaluateAgentConstraint(properties, accessContext.AgentId))
        {
            return false;
        }

        var hasExplicitConstraint =
            HasConfiguredIdentifiers(properties, RoleKeys) ||
            HasConfiguredIdentifiers(properties, ScopeKeys) ||
            HasConfiguredIdentifiers(properties, AgentKeys);

        if ((HasTrueBoolean(properties, RestrictedKeys) || HasTrueBoolean(properties, PrivateKeys)) && !hasExplicitConstraint)
        {
            return false;
        }

        return true;
    }

    private static bool EvaluateIdentifierConstraint(
        IDictionary<string, JsonNode?> properties,
        IReadOnlyList<string> keys,
        string? currentValue)
    {
        if (!TryGetConfiguredIdentifiers(properties, keys, out var configuredValues, out var exists))
        {
            return false;
        }

        if (!exists)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(currentValue) && configuredValues.Contains(currentValue.Trim());
    }

    private static bool EvaluateScopeConstraint(
        IDictionary<string, JsonNode?> properties,
        IReadOnlyList<string>? currentScopes)
    {
        if (!TryGetConfiguredIdentifiers(properties, ScopeKeys, out var configuredScopes, out var exists))
        {
            return false;
        }

        if (!exists)
        {
            return true;
        }

        if (currentScopes is null || currentScopes.Count == 0)
        {
            return false;
        }

        return currentScopes.Any(configuredScopes.Contains);
    }

    private static bool EvaluateAgentConstraint(
        IDictionary<string, JsonNode?> properties,
        Guid? agentId)
    {
        if (!TryGetConfiguredIdentifiers(properties, AgentKeys, out var configuredAgents, out var exists))
        {
            return false;
        }

        if (!exists)
        {
            return true;
        }

        return agentId.HasValue && configuredAgents.Contains(agentId.Value.ToString("D"));
    }

    private static bool HasConfiguredIdentifiers(IDictionary<string, JsonNode?> properties, IReadOnlyList<string> keys) =>
        TryGetConfiguredIdentifiers(properties, keys, out var configuredValues, out var exists) && exists && configuredValues.Count > 0;

    private static bool TryGetConfiguredIdentifiers(
        IDictionary<string, JsonNode?> properties,
        IReadOnlyList<string> keys,
        out HashSet<string> configuredValues,
        out bool exists)
    {
        configuredValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        exists = false;

        foreach (var key in keys)
        {
            if (!properties.TryGetValue(key, out var node) || node is null)
            {
                continue;
            }

            exists = true;
            if (!TryAppendIdentifiers(node, configuredValues))
            {
                configuredValues.Clear();
                return false;
            }
        }

        return true;
    }

    private static bool TryAppendIdentifiers(JsonNode node, ISet<string> results)
    {
        switch (node)
        {
            case JsonValue jsonValue when jsonValue.TryGetValue<string>(out var singleValue) && !string.IsNullOrWhiteSpace(singleValue):
                results.Add(singleValue.Trim());
                return true;
            case JsonArray array:
                foreach (var item in array)
                {
                    if (item is not JsonValue itemValue || !itemValue.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text))
                    {
                        return false;
                    }

                    results.Add(text.Trim());
                }

                return true;
            default:
                return false;
        }
    }

    private static bool HasTrueBoolean(IDictionary<string, JsonNode?> properties, IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (!properties.TryGetValue(key, out var node) || node is not JsonValue value)
            {
                continue;
            }

            if ((value.TryGetValue<bool>(out var booleanValue) && booleanValue) ||
                (value.TryGetValue<string>(out var textValue) && bool.TryParse(textValue, out var parsedBoolean) && parsedBoolean))
            {
                return true;
            }
        }

        return false;
    }
}
