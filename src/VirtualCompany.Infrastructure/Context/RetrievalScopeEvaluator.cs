using System.Text.Json.Nodes;
using VirtualCompany.Application.Context;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Context;

public sealed class RetrievalScopeEvaluator : IRetrievalScopeEvaluator
{
    private static readonly IReadOnlyList<string> RoleKeys = ["roles", "allowed_roles", "membership_roles"];
    private static readonly IReadOnlyList<string> ScopeKeys = ["scope", "scopes", "data_scope", "data_scopes", "read_scope", "read", "recommend", "execute", "write"];
    private static readonly IReadOnlyList<string> AgentKeys = ["agent_id", "agent_ids", "agents"];
    private static readonly IReadOnlyList<string> UserKeys = ["user_id", "user_ids", "users"];
    private static readonly IReadOnlyList<string> CompanyKeys = ["company_id", "company_ids", "companies"];
    private static readonly IReadOnlyList<string> RestrictedKeys = ["restricted", "is_restricted"];
    private static readonly IReadOnlyList<string> PrivateKeys = ["private", "is_private"];

    public RetrievalAccessDecision Evaluate(RetrievalAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var agentReadScopes = ResolveReadScopes(context.AgentDataScopes);
        var hasHumanActor = context.ActorUserId.HasValue && context.ActorUserId.Value != Guid.Empty;
        var membershipResolved = !hasHumanActor || (context.ActorMembershipId.HasValue && context.ActorMembershipRole.HasValue);
        var canRetrieve = !hasHumanActor || membershipResolved;
        var effectiveReadScopes = canRetrieve ? agentReadScopes : Array.Empty<string>();

        return new RetrievalAccessDecision(
            context.CompanyId,
            context.AgentId,
            context.ActorUserId,
            context.ActorMembershipId,
            context.ActorMembershipRole,
            agentReadScopes,
            effectiveReadScopes,
            membershipResolved,
            canRetrieve);
    }

    public CompanyKnowledgeAccessContext BuildKnowledgeAccessContext(RetrievalAccessDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        return new CompanyKnowledgeAccessContext(
            decision.CompanyId,
            decision.ActorMembershipId,
            decision.ActorUserId,
            decision.ActorMembershipRole?.ToStorageValue(),
            decision.EffectiveReadScopes,
            decision.AgentId);
    }

    public bool CanAccessMemory(RetrievalAccessDecision decision, MemoryItem item)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(item);

        if (!decision.CanRetrieve || item.CompanyId != decision.CompanyId)
        {
            return false;
        }

        var metadata = item.Metadata;

        if (!EvaluateIdentifierConstraint(metadata, CompanyKeys, decision.CompanyId.ToString("D")))
        {
            return false;
        }

        if (!EvaluateGuidConstraint(metadata, UserKeys, decision.ActorUserId))
        {
            return false;
        }

        if (!EvaluateIdentifierConstraint(metadata, RoleKeys, decision.ActorMembershipRole?.ToStorageValue()))
        {
            return false;
        }

        if (!EvaluateIdentifierConstraint(metadata, AgentKeys, decision.AgentId.ToString("D")))
        {
            return false;
        }

        if (!EvaluateScopeConstraint(metadata, ScopeKeys, decision.EffectiveReadScopes))
        {
            return false;
        }

        var hasExplicitConstraint =
            HasConfiguredIdentifiers(metadata, CompanyKeys) ||
            HasConfiguredIdentifiers(metadata, UserKeys) ||
            HasConfiguredIdentifiers(metadata, RoleKeys) ||
            HasConfiguredIdentifiers(metadata, AgentKeys) ||
            HasConfiguredIdentifiers(metadata, ScopeKeys);

        if ((HasTrueBoolean(metadata, RestrictedKeys) || HasTrueBoolean(metadata, PrivateKeys)) &&
            !hasExplicitConstraint)
        {
            return false;
        }

        return true;
    }

    public bool CanAccessTaskScope(RetrievalAccessDecision decision, string? scope)
    {
        ArgumentNullException.ThrowIfNull(decision);

        if (!decision.CanRetrieve)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            return true;
        }

        if (decision.EffectiveReadScopes.Count == 0)
        {
            return false;
        }

        return decision.EffectiveReadScopes.Contains(scope.Trim(), StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ResolveReadScopes(IReadOnlyDictionary<string, JsonNode?> dataScopes)
    {
        if (!TryReadConfiguredIdentifiers(dataScopes, "read", out var scopes))
        {
            return Array.Empty<string>();
        }

        return scopes;
    }

    private static bool TryReadConfiguredIdentifiers(
        IReadOnlyDictionary<string, JsonNode?> nodes,
        string key,
        out string[] values)
    {
        values = Array.Empty<string>();

        if (!nodes.TryGetValue(key, out var node) || node is null)
        {
            return true;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var singleValue))
        {
            values = string.IsNullOrWhiteSpace(singleValue)
                ? Array.Empty<string>()
                : [singleValue.Trim()];
            return true;
        }

        if (node is not JsonArray array)
        {
            return false;
        }

        var results = new List<string>(array.Count);
        foreach (var item in array)
        {
            if (item is not JsonValue itemValue || !itemValue.TryGetValue<string>(out var textValue) || string.IsNullOrWhiteSpace(textValue))
            {
                values = Array.Empty<string>();
                return false;
            }

            results.Add(textValue.Trim());
        }

        values = results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return true;
    }

    private static bool EvaluateGuidConstraint(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys,
        Guid? currentValue) =>
        EvaluateIdentifierConstraint(metadata, keys, currentValue?.ToString("D"));

    private static bool EvaluateIdentifierConstraint(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys,
        string? currentValue)
    {
        if (!TryGetConfiguredIdentifiers(metadata, keys, out var configuredValues, out var exists))
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
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys,
        IReadOnlyList<string> readScopes)
    {
        if (!TryGetConfiguredIdentifiers(metadata, keys, out var configuredScopes, out var exists))
        {
            return false;
        }

        if (!exists)
        {
            return true;
        }

        if (readScopes.Count == 0)
        {
            return false;
        }

        return readScopes.Any(configuredScopes.Contains);
    }

    private static bool HasConfiguredIdentifiers(IReadOnlyDictionary<string, JsonNode?> metadata, IReadOnlyList<string> keys) =>
        TryGetConfiguredIdentifiers(metadata, keys, out var configuredValues, out var exists) &&
        exists &&
        configuredValues.Count > 0;

    private static bool TryGetConfiguredIdentifiers(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys,
        out HashSet<string> configuredValues,
        out bool exists)
    {
        configuredValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        exists = false;

        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var node) || node is null)
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

    private static bool TryAppendIdentifiers(JsonNode node, ISet<string> identifiers)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var singleValue))
        {
            if (string.IsNullOrWhiteSpace(singleValue))
            {
                return false;
            }

            identifiers.Add(singleValue.Trim());
            return true;
        }

        if (node is not JsonArray array)
        {
            return false;
        }

        foreach (var item in array)
        {
            if (item is not JsonValue itemValue || !itemValue.TryGetValue<string>(out var textValue) || string.IsNullOrWhiteSpace(textValue))
            {
                return false;
            }

            identifiers.Add(textValue.Trim());
        }

        return true;
    }

    private static bool HasTrueBoolean(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        IReadOnlyList<string> keys)
    {
        foreach (var key in keys)
        {
            if (!metadata.TryGetValue(key, out var node) || node is not JsonValue jsonValue)
            {
                continue;
            }

            if ((jsonValue.TryGetValue<bool>(out var boolValue) && boolValue) ||
                (jsonValue.TryGetValue<string>(out var textValue) &&
                 bool.TryParse(textValue, out var parsed) &&
                 parsed))
            {
                return true;
            }
        }

        return false;
    }
}