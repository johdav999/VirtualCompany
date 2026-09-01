using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentEffectiveAuthorityResolverTests
{
    [Fact]
    public void Resolver_preserves_configured_and_versioned_compatibility_grants_separately()
    {
        var agent = Laura(
            tools: Values(
                ("allowed", new JsonArray("get_cash_balance")),
                ("actions", new JsonArray("read")),
                ("denied", new JsonArray(AccountingProviderSwitchAgentToolIds.ReadBriefing))),
            scopes: Values(("read", new JsonArray("finance"))));
        var registry = new StaticCompanyToolRegistry();

        var first = AgentEffectiveAuthorityResolver.Resolve(agent, registry);
        var second = AgentEffectiveAuthorityResolver.Resolve(agent, registry);

        var configured = Assert.Single(first.ConfiguredGrants, grant => grant.ToolName == "get_cash_balance");
        Assert.Equal(AgentAuthorityGrantSources.Configured, configured.Source);
        var compatibility = Assert.Single(first.CompatibilityGrants,
            grant => grant.ToolName == AccountingProviderSwitchAgentToolIds.ReadBriefing);
        Assert.Equal(AgentAuthorityGrantSources.CompatibilityRolePolicy, compatibility.Source);
        Assert.StartsWith("laura-finance-role-policy-", compatibility.SourceVersion);
        Assert.True(first.Find(AccountingProviderSwitchAgentToolIds.ReadBriefing, ToolActionType.Read, "finance")!.IsUsable);
        Assert.Equal(first.AuthorityVersion, second.AuthorityVersion);
        Assert.Equal(first.AuthorityHash, second.AuthorityHash);
        Assert.Equal(64, first.AuthorityHash.Length);
    }

    [Fact]
    public void Newly_registered_finance_tool_without_coverage_metadata_is_not_projected_to_Laura()
    {
        const string newTool = "finance.new_sensitive_write";
        var authority = AgentEffectiveAuthorityResolver.Resolve(
            Laura(tools: Values(("allowed", new JsonArray("get_cash_balance", newTool)), ("actions", new JsonArray("read", "execute"))),
                scopes: Values(("read", new JsonArray("finance")), ("execute", new JsonArray("finance")))),
            new RegistryWithAdditionalFinanceTool(newTool, ToolActionType.Execute, sensitive: true));

        Assert.Null(authority.Find(newTool, ToolActionType.Execute, "finance"));
        Assert.DoesNotContain(authority.Tools, tool => tool.ToolName == newTool);
    }

    [Fact]
    public void Resolver_emits_deterministic_capability_states()
    {
        var authority = AgentEffectiveAuthorityResolver.Resolve(
            Laura(
                tools: Values(
                    ("allowed", new JsonArray("get_cash_balance", "list_transactions", "categorize_transaction", "finance.removed_tool")),
                    ("actions", new JsonArray("read", "execute")),
                    ("denied", new JsonArray("list_transactions")),
                    ("integrationAvailability", new JsonObject { ["get_cash_balance"] = false })),
                scopes: Values(("read", new JsonArray("finance")), ("execute", new JsonArray("finance"))),
                thresholds: Values(("financePolicy", new JsonObject { ["requireApprovalForExecute"] = true }))),
            new StaticCompanyToolRegistry());

        Assert.Equal(AgentCapabilityStates.IntegrationUnavailable,
            authority.Find("get_cash_balance", ToolActionType.Read, "finance")!.State);
        var governedExecution = authority.Find("categorize_transaction", ToolActionType.Execute, "finance")!;
        Assert.Equal(AgentCapabilityStates.ApprovalRequired, governedExecution.State);
        Assert.Equal(FinancePermissions.Edit, governedExecution.ActorPermission);
        Assert.Equal("required", governedExecution.ApprovalBehavior);
        Assert.Equal("ready", governedExecution.IntegrationState);
        Assert.Equal(AgentCapabilityStates.PermissionDenied,
            authority.Find("list_transactions", ToolActionType.Read, "finance")!.State);
        Assert.DoesNotContain(authority.Tools, item => item.ToolName == "finance.removed_tool");
        Assert.Contains(authority.Tools, item => item.State == AgentCapabilityStates.Available);
    }

    [Fact]
    public void Authority_hash_changes_when_effective_permissions_change()
    {
        var registry = new StaticCompanyToolRegistry();
        var before = AgentEffectiveAuthorityResolver.Resolve(
            Laura(tools: Values(("allowed", new JsonArray("get_cash_balance")), ("actions", new JsonArray("read"))),
                scopes: Values(("read", new JsonArray("finance")))), registry);
        var after = AgentEffectiveAuthorityResolver.Resolve(
            Laura(tools: Values(("allowed", new JsonArray("get_cash_balance")), ("actions", new JsonArray("read")),
                    ("integrationAvailability", new JsonObject { ["get_cash_balance"] = false })),
                scopes: Values(("read", new JsonArray("finance")))), registry);

        Assert.NotEqual(before.AuthorityHash, after.AuthorityHash);
    }

    private static Agent Laura(
        Dictionary<string, JsonNode?> tools,
        Dictionary<string, JsonNode?> scopes,
        Dictionary<string, JsonNode?>? thresholds = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "finance", "Laura", "Finance Manager", "Finance", null,
            AgentSeniority.Senior, AgentStatus.Active, AgentAutonomyLevel.Guided, tools: tools, scopes: scopes,
            thresholds: thresholds);

    private static Dictionary<string, JsonNode?> Values(params (string Key, JsonNode? Value)[] values) =>
        values.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);

    private sealed class RegistryWithAdditionalFinanceTool : ICompanyToolRegistry
    {
        private readonly StaticCompanyToolRegistry _inner = new();
        private readonly TrustedToolRegistration _registration;
        private readonly ToolDefinitionManifest _definition;

        public RegistryWithAdditionalFinanceTool(string toolName, ToolActionType action, bool sensitive)
        {
            var input = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
            var output = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
            _registration = new TrustedToolRegistration(toolName, new HashSet<ToolActionType> { action },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "finance" }, "1.0.0", input, output, sensitive);
            _definition = new ToolDefinitionManifest(toolName, "1.0.0", action, input, output, sensitive);
        }

        public bool TryGetTool(string toolName, out TrustedToolRegistration registration)
        {
            if (toolName.Equals(_registration.ToolName, StringComparison.OrdinalIgnoreCase))
            {
                registration = _registration;
                return true;
            }
            return _inner.TryGetTool(toolName, out registration!);
        }

        public IReadOnlyList<TrustedToolRegistration> ListTools() => [.. _inner.ListTools(), _registration];

        public bool TryGetToolDefinition(string toolName, out ToolDefinitionManifest definition)
        {
            if (toolName.Equals(_definition.ToolName, StringComparison.OrdinalIgnoreCase))
            {
                definition = _definition;
                return true;
            }
            return _inner.TryGetToolDefinition(toolName, out definition!);
        }

        public IReadOnlyList<ToolDefinitionManifest> ListToolDefinitions() =>
            [.. _inner.ListToolDefinitions(), _definition];
    }
}
