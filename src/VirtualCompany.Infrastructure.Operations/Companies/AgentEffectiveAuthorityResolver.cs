using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class AgentEffectiveAuthorityResolver : IAgentEffectiveAuthorityResolver
{
    private const string LauraRolePolicyVersion = "laura-finance-role-policy-v1";

    private static readonly IReadOnlySet<string> LauraRolePolicyTools = new HashSet<string>(
        new[]
        {
            "get_cash_balance",
            "resolve_finance_agent_query",
            "list_transactions",
            "list_uncategorized_transactions",
            "list_invoices_awaiting_approval",
            "get_profit_and_loss_summary",
            "recommend_transaction_category",
            "recommend_invoice_approval_decision",
            "evaluate_transaction_anomaly",
            "categorize_transaction",
            "approve_invoice",
            "post_paid_supplier_bill_expense"
        }.Concat(AccountingProviderSwitchAgentToolIds.All),
        StringComparer.OrdinalIgnoreCase);

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyToolRegistry _toolRegistry;

    public AgentEffectiveAuthorityResolver(VirtualCompanyDbContext dbContext, ICompanyToolRegistry toolRegistry)
    {
        _dbContext = dbContext;
        _toolRegistry = toolRegistry;
    }

    public async Task<AgentEffectiveAuthorityDto> ResolveAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (agentId == Guid.Empty) throw new ArgumentException("AgentId is required.", nameof(agentId));

        var agent = await _dbContext.Agents.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == agentId, cancellationToken)
            ?? throw new KeyNotFoundException("Agent not found.");

        return Resolve(agent, _toolRegistry);
    }

    internal static AgentEffectiveAuthorityDto Resolve(Agent agent, ICompanyToolRegistry toolRegistry)
    {
        var configuredTools = ReadStrings(agent.Tools, "allowed");
        var deniedTools = ReadStrings(agent.Tools, "denied");
        var configuredActions = ReadStrings(agent.Tools, "actions", out var actionsConfigured);
        var deniedActions = ReadStrings(agent.Tools, "deniedActions");
        var isLaura = IsLaura(agent);
        var relevantScope = DepartmentScope(agent.Department);
        var configuredSourceVersion = $"agent-profile-{agent.UpdatedUtc.ToUniversalTime():yyyyMMddHHmmssfffffff}";

        var definitions = toolRegistry.ListToolDefinitions()
            .Where(definition =>
                configuredTools.Contains(definition.ToolName) ||
                (toolRegistry.TryGetTool(definition.ToolName, out var registration) &&
                 registration.Scopes.Contains(relevantScope)))
            .ToDictionary(x => x.ToolName, StringComparer.OrdinalIgnoreCase);

        var candidateTools = definitions.Keys
            .Concat(configuredTools.Where(tool => IsRelevantConfiguredTool(tool, isLaura, toolRegistry)))
            .Concat(isLaura ? LauraRolePolicyTools : [])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var configuredGrants = new List<AgentAuthorityGrantDto>();
        var compatibilityGrants = new List<AgentAuthorityGrantDto>();
        var authorities = new List<EffectiveAgentToolAuthorityDto>();

        foreach (var toolName in candidateTools)
        {
            toolRegistry.TryGetToolDefinition(toolName, out var definition);
            toolRegistry.TryGetTool(toolName, out var registration);
            var registered = registration is not null;
            var action = ResolveAction(definition, registration, configuredActions);
            var actionName = action.ToStorageValue();
            var scope = registered
                ? registration!.Scopes.FirstOrDefault(value => value.Equals(relevantScope, StringComparison.OrdinalIgnoreCase))
                  ?? registration.Scopes.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).FirstOrDefault()
                  ?? relevantScope
                : relevantScope;
            var configured = configuredTools.Contains(toolName);
            var compatibility = !configured && isLaura && LauraRolePolicyTools.Contains(toolName);
            var grantSource = configured ? AgentAuthorityGrantSources.Configured
                : compatibility ? AgentAuthorityGrantSources.CompatibilityRolePolicy : null;
            var grantVersion = configured ? configuredSourceVersion
                : compatibility ? LauraRolePolicyVersion : null;
            var toolVersion = definition?.Version ?? registration?.Version ?? "unregistered";

            if (configured)
            {
                configuredGrants.Add(new AgentAuthorityGrantDto(toolName, toolVersion, actionName, scope,
                    AgentAuthorityGrantSources.Configured, configuredSourceVersion,
                    "The tool is explicitly present in the persisted agent profile."));
            }
            else if (compatibility)
            {
                compatibilityGrants.Add(new AgentAuthorityGrantDto(toolName, toolVersion, actionName, scope,
                    AgentAuthorityGrantSources.CompatibilityRolePolicy, LauraRolePolicyVersion,
                    "A versioned Laura compatibility policy preserves this previously shipped Finance capability."));
            }

            var requirements = relevantScope == "finance"
                ? FinanceAgentAuthorizationService.ResolveRequirements(toolName, action)
                : null;
            var state = ResolveState(agent, toolName, actionName, scope, registered, configured, compatibility,
                deniedTools, configuredActions, actionsConfigured, deniedActions, registration);

            var riskClassification = registration?.FinanceRiskClassification;
            authorities.Add(new EffectiveAgentToolAuthorityDto(
                toolName,
                toolVersion,
                actionName,
                scope,
                state.State,
                state.ReasonCode,
                state.Explanation,
                grantSource,
                grantVersion,
                requirements?.Policies ?? [],
                requirements?.Permissions ?? [])
            {
                ActorPermission = riskClassification?.RequiredActorPermission
                    ?? requirements?.Permissions.LastOrDefault()
                    ?? string.Empty,
                ApprovalBehavior = state.State == AgentCapabilityStates.ApprovalRequired
                    ? "required"
                    : riskClassification?.DefaultApprovalBehavior ?? "not_required",
                IntegrationState = state.State switch
                {
                    AgentCapabilityStates.IntegrationUnavailable => "unavailable",
                    AgentCapabilityStates.ConfigurationRequired => "setup_required",
                    AgentCapabilityStates.NotImplemented => "not_available",
                    _ => "ready"
                }
            });
        }

        var orderedAuthorities = authorities
            .OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ActionType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Scope, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var authorityHash = ComputeHash(orderedAuthorities);

        return new AgentEffectiveAuthorityDto(
            agent.CompanyId,
            agent.Id,
            agent.DisplayName,
            agent.Department,
            agent.Status.ToStorageValue(),
            agent.CanReceiveAssignments,
            agent.AutonomyLevel.ToStorageValue(),
            AgentEffectiveAuthorityVersions.V1,
            authorityHash,
            configuredGrants.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToArray(),
            compatibilityGrants.OrderBy(x => x.ToolName, StringComparer.OrdinalIgnoreCase).ToArray(),
            orderedAuthorities,
            DateTime.UtcNow);
    }

    private static AuthorityState ResolveState(
        Agent agent,
        string toolName,
        string action,
        string scope,
        bool registered,
        bool configured,
        bool compatibility,
        IReadOnlySet<string> deniedTools,
        IReadOnlySet<string> configuredActions,
        bool actionsConfigured,
        IReadOnlySet<string> deniedActions,
        TrustedToolRegistration? registration)
    {
        if (!registered)
            return new(AgentCapabilityStates.NotImplemented, AgentAuthorityReasonCodes.NotImplemented,
                "This configured authority has no implemented trusted tool in the current product version.");
        if (!agent.CanReceiveAssignments || agent.Status != AgentStatus.Active)
            return new(AgentCapabilityStates.PermissionDenied, AgentAuthorityReasonCodes.AgentInactive,
                "The agent must be active and able to receive work.");
        if (deniedTools.Contains(toolName) && !compatibility)
            return new(AgentCapabilityStates.PermissionDenied, AgentAuthorityReasonCodes.ExplicitlyDenied,
                "The persisted agent profile explicitly denies this tool.");
        if (!configured && !compatibility)
            return new(AgentCapabilityStates.ConfigurationRequired, AgentAuthorityReasonCodes.ConfigurationRequired,
                "This registered tool is not granted by the agent profile or a versioned role policy.");
        if (deniedActions.Contains(action) || (configured && actionsConfigured && !configuredActions.Contains(action)))
            return new(AgentCapabilityStates.PermissionDenied, AgentAuthorityReasonCodes.ActionDenied,
                "The agent authority does not include this action class.");

        var scopes = ReadStrings(agent.Scopes, action);
        if (!scopes.Contains(scope) && !compatibility)
            return new(AgentCapabilityStates.PermissionDenied, AgentAuthorityReasonCodes.ScopeDenied,
                "The agent authority does not include this data scope.");
        if (!IsIntegrationAvailable(agent.Tools, toolName))
            return new(AgentCapabilityStates.IntegrationUnavailable, AgentAuthorityReasonCodes.IntegrationUnavailable,
                "A required integration is unavailable for this tool.");

        var approvalRequired = registration!.SensitiveAction ||
                               (action == ToolActionType.Execute.ToStorageValue() && RequiresApprovalForExecute(agent.Thresholds)) ||
                               RequiresApprovalForTool(agent.TriggerLogic, toolName);
        return approvalRequired
            ? new(AgentCapabilityStates.ApprovalRequired, AgentAuthorityReasonCodes.ApprovalRequired,
                "The authority is effective, but policy requires approval before execution.")
            : new(AgentCapabilityStates.Available, AgentAuthorityReasonCodes.Available,
                "The authority is effective for this tool, action, and scope.");
    }

    private static bool RequiresApprovalForExecute(IReadOnlyDictionary<string, JsonNode?> thresholds) =>
        thresholds.TryGetValue("financePolicy", out var node) && node is JsonObject policy &&
        policy["requireApprovalForExecute"] is JsonValue value && value.TryGetValue<bool>(out var required) && required;

    private static bool RequiresApprovalForTool(IReadOnlyDictionary<string, JsonNode?> triggerLogic, string toolName) =>
        triggerLogic.TryGetValue("workflowCapabilities", out var node) && node is JsonObject workflow &&
        workflow["requiresApproval"] is JsonArray required && required.OfType<JsonValue>().Any(value =>
            value.TryGetValue<string>(out var text) && string.Equals(text, toolName, StringComparison.OrdinalIgnoreCase));

    private static bool IsIntegrationAvailable(IReadOnlyDictionary<string, JsonNode?> tools, string toolName)
    {
        if (!tools.TryGetValue("integrationAvailability", out var node) || node is not JsonObject availability)
            return true;
        return availability[toolName] is not JsonValue value || !value.TryGetValue<bool>(out var available) || available;
    }

    private static string ComputeHash(IReadOnlyList<EffectiveAgentToolAuthorityDto> authorities)
    {
        var canonical = JsonSerializer.Serialize(authorities.Select(item => new
        {
            item.ToolName,
            item.ToolVersion,
            item.ActionType,
            item.Scope,
            item.State,
            item.ReasonCode,
            item.GrantSource,
            item.GrantSourceVersion,
            item.ActorPermission,
            item.ApprovalBehavior,
            item.IntegrationState,
            Policies = item.RequiredCompanyPolicies.OrderBy(x => x, StringComparer.Ordinal),
            Permissions = item.RequiredFinancePermissions.OrderBy(x => x, StringComparer.Ordinal)
        }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static bool IsLaura(Agent agent) =>
        string.Equals(agent.TemplateId, LauraFinanceAgentSeedData.TemplateId, StringComparison.OrdinalIgnoreCase) ||
        (string.Equals(agent.DisplayName, "Laura", StringComparison.OrdinalIgnoreCase) &&
         string.Equals(agent.Department, "Finance", StringComparison.OrdinalIgnoreCase));

    private static string DepartmentScope(string department) => department.Trim().ToLowerInvariant() switch
    {
        "customer support" => "support",
        "" => "general",
        var value => value
    };

    private static bool IsRelevantConfiguredTool(string toolName, bool isLaura, ICompanyToolRegistry registry) =>
        registry.TryGetTool(toolName, out _) ||
        (isLaura && (LauraRolePolicyTools.Contains(toolName) || toolName.StartsWith("finance.", StringComparison.OrdinalIgnoreCase)));

    private static ToolActionType ResolveAction(
        ToolDefinitionManifest? definition,
        TrustedToolRegistration? registration,
        IReadOnlySet<string> configuredActions)
    {
        if (definition is not null) return definition.ActionType;

        foreach (var action in new[] { ToolActionType.Read, ToolActionType.Recommend, ToolActionType.Execute })
        {
            if (registration?.SupportedActions.Contains(action) == true &&
                (configuredActions.Count == 0 || configuredActions.Contains(action.ToStorageValue())))
            {
                return action;
            }
        }

        if (configuredActions.Contains("execute")) return ToolActionType.Execute;
        if (configuredActions.Contains("recommend")) return ToolActionType.Recommend;
        return ToolActionType.Read;
    }

    private static HashSet<string> ReadStrings(IReadOnlyDictionary<string, JsonNode?> values, string key) =>
        ReadStrings(values, key, out _);

    private static HashSet<string> ReadStrings(
        IReadOnlyDictionary<string, JsonNode?> values,
        string key,
        out bool configured)
    {
        configured = values.TryGetValue(key, out var node) && node is JsonArray;
        return node is JsonArray array
            ? array.OfType<JsonValue>()
                .Select(value => value.TryGetValue<string>(out var text) ? text?.Trim() : null)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record AuthorityState(string State, string ReasonCode, string Explanation);
}
