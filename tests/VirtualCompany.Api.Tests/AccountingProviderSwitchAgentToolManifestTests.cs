using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchAgentToolManifestTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public void Registry_exposes_every_migration_tool_with_bounded_schema_and_sensitive_execute_classification()
    {
        var registry = new StaticCompanyToolRegistry();

        Assert.Equal(AccountingProviderSwitchAgentToolIds.All.Count,
            registry.ListToolDefinitions().Count(x => AccountingProviderSwitchAgentToolIds.All.Contains(x.ToolName)));

        foreach (var toolName in AccountingProviderSwitchAgentToolIds.All)
        {
            Assert.True(registry.TryGetToolDefinition(toolName, out var definition));
            Assert.True(registry.TryGetTool(toolName, out var registration));
            Assert.Equal("1.0.0", definition.Version);
            Assert.Equal("object", definition.InputSchema["type"]!.GetValue<string>());
            Assert.False(definition.InputSchema["additionalProperties"]!.GetValue<bool>());
            Assert.Equal("object", definition.OutputSchema["type"]!.GetValue<string>());
            Assert.Equal("finance", Assert.Single(registration.Scopes));
            Assert.Equal(definition.ActionType, Assert.Single(registration.SupportedActions));

            var execute = AccountingProviderSwitchAgentToolIds.ExecuteTools.Contains(toolName);
            Assert.Equal(execute, definition.SensitiveAction);
            Assert.Equal(execute, registration.SensitiveAction);
        }

        foreach (var toolName in AccountingProviderSwitchAgentToolIds.ExecuteTools)
        {
            var definition = registry.ListToolDefinitions().Single(x => x.ToolName == toolName);
            var required = definition.InputSchema["required"]!.AsArray()
                .Select(x => x!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("switchId", required);
            Assert.Contains("expectedSwitchVersion", required);
            Assert.Contains("idempotencyKey", required);
        }
    }

    [Fact]
    public void Registry_does_not_expose_prohibited_migration_powers()
    {
        var registry = new StaticCompanyToolRegistry();

        Assert.False(registry.TryGetTool("finance.migration.activate_authority", out _));
        Assert.False(registry.TryGetTool("finance.migration.approve_own_request", out _));
        Assert.False(registry.TryGetTool("finance.migration.mark_reconciliation_successful", out _));
        Assert.False(registry.TryGetTool("finance.migration.supply_credentials", out _));
        Assert.False(registry.TryGetTool("finance.migration.retry_ambiguous_provider_outcome", out _));
    }

    [Fact]
    public async Task Execute_schema_rejects_missing_switch_version_before_contract_routing()
    {
        var contract = new CountingContract();
        var executor = new NoOpCompanyToolExecutor(new StaticCompanyToolRegistry(), contract);

        var result = await executor.ExecuteAsync(new ToolExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), AccountingProviderSwitchAgentToolIds.StartAssessment,
            ToolActionType.Execute, "finance",
            new Dictionary<string, JsonNode?>
            {
                ["switchId"] = JsonValue.Create(Guid.NewGuid()),
                ["idempotencyKey"] = JsonValue.Create("migration-assessment-v1")
            }), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("input_payload_schema_validation_failed", result.ErrorCode);
        Assert.Equal(0, contract.Count);
    }

    [Fact]
    public async Task Existing_Laura_runtime_profile_receives_migration_tools_without_unrelated_sales_tools()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await _factory.SeedAsync(db =>
        {
            db.Companies.Add(new Company(companyId, "Profile compatibility company"));
            db.Agents.Add(new Agent(agentId, companyId, "finance", "Laura", "Finance Manager", "Finance", null,
                AgentSeniority.Senior, AgentStatus.Active, AgentAutonomyLevel.Guided,
                tools: new Dictionary<string, JsonNode?>
                {
                    ["allowed"] = new JsonArray("get_cash_balance"),
                    ["actions"] = new JsonArray("read"),
                    ["denied"] = new JsonArray(AccountingProviderSwitchAgentToolIds.ReadBriefing, "sales.list_prospects")
                },
                scopes: new Dictionary<string, JsonNode?> { ["read"] = new JsonArray("finance") }));
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IAgentRuntimeProfileResolver>();
        var profile = await resolver.GetCurrentProfileAsync(companyId, agentId, CancellationToken.None);
        var allowed = Strings(profile.ToolPermissions, "allowed");
        var denied = Strings(profile.ToolPermissions, "denied");

        Assert.All(AccountingProviderSwitchAgentToolIds.All, tool => Assert.Contains(tool, allowed));
        Assert.DoesNotContain(AccountingProviderSwitchAgentToolIds.ReadBriefing, denied);
        Assert.DoesNotContain("sales.list_prospects", allowed);
        Assert.Contains("finance", Strings(profile.DataScopes, "execute"));
    }

    private static HashSet<string> Strings(IReadOnlyDictionary<string, JsonNode?> values, string key) =>
        values.TryGetValue(key, out var node) && node is JsonArray array
            ? array.OfType<JsonValue>().Select(x => x.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private sealed class CountingContract : IInternalCompanyToolContract
    {
        public int Count { get; private set; }

        public Task<InternalToolExecutionResponse> ExecuteAsync(InternalToolExecutionRequest request, CancellationToken cancellationToken)
        {
            Count++;
            return Task.FromResult(InternalToolExecutionResponse.Succeeded("ok", new Dictionary<string, JsonNode?>
            {
                ["commandResult"] = new JsonObject()
            }));
        }
    }
}
