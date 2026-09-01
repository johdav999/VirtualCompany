using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Authorization;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAgentCoverageCatalogueTests
{
    [Fact]
    public void Baseline_has_complete_unique_permission_and_risk_consistent_tool_ownership()
    {
        var registry = new StaticCompanyToolRegistry();
        var manifests = FinanceAgentCoverageCatalogue.Manifests;

        EffectiveFinanceAgentCoverageCatalogue.Validate(registry, manifests);

        var operations = manifests.SelectMany(capability => capability.Operations).ToArray();
        var toolOperations = operations.Where(operation => operation.ToolName is not null).ToArray();
        var registeredFinanceTools = registry.ListTools()
            .Where(tool => tool.Scopes.Contains("finance"))
            .Select(tool => tool.ToolName)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(18, manifests.Count);
        Assert.Equal(107, operations.Length);
        Assert.Equal(97, toolOperations.Length);
        Assert.Equal(registeredFinanceTools, toolOperations.Select(operation => operation.ToolName!)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(toolOperations.Length, toolOperations.Select(operation => operation.ToolName)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(toolOperations, operation =>
        {
            var definition = Assert.Single(registry.ListToolDefinitions(), item => item.ToolName == operation.ToolName);
            Assert.Equal(definition.ActionType.ToStorageValue(), operation.ActionClass);
            Assert.NotNull(definition.SelectionMetadata);
        });
        Assert.All(operations, operation =>
        {
            Assert.NotEmpty(operation.Integrations);
            Assert.NotEmpty(operation.SourceTypes);
        });
    }

    [Fact]
    public void Registered_finance_tool_without_catalogue_owner_fails_validation()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            EffectiveFinanceAgentCoverageCatalogue.Validate(
                new RegistryWithAdditionalFinanceTool("finance.unclassified_read"),
                FinanceAgentCoverageCatalogue.Manifests));

        Assert.Contains("Unowned registered tools: [finance.unclassified_read]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Planner_metadata_mismatch_fails_catalogue_validation()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            EffectiveFinanceAgentCoverageCatalogue.Validate(
                new RegistryWithPlannerActionMismatch("get_cash_balance"),
                FinanceAgentCoverageCatalogue.Manifests));

        Assert.Contains("registry, planner, and coverage metadata are inconsistent", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Effective_projection_combines_support_baseline_with_P0_authority_without_granting_gaps()
    {
        var registry = new StaticCompanyToolRegistry();
        var authority = AgentEffectiveAuthorityResolver.Resolve(Laura(), registry);
        var service = new EffectiveFinanceAgentCoverageCatalogue(new FixedAuthorityResolver(authority), registry);

        var result = await service.GetEffectiveCoverageAsync(authority.CompanyId, authority.AgentId, default);

        Assert.Equal(FinanceAgentCoverageVersions.V1, result.CatalogueVersion);
        Assert.Equal(authority.AuthorityVersion, result.AuthorityVersion);
        Assert.Equal(authority.AuthorityHash, result.AuthorityHash);
        Assert.Equal(18, result.Counts.TotalCapabilities);
        Assert.Equal(107, result.Counts.TotalOperations);
        Assert.Equal(97, result.Counts.RegisteredTools);
        Assert.Equal(45, result.Counts.ImplementedRead);
        Assert.Equal(34, result.Counts.ImplementedRecommendDraft);
        Assert.Equal(18, result.Counts.ImplementedExecute);
        Assert.Equal(2, result.Counts.ConfigurationDependent);
        Assert.Equal(2, result.Counts.Unsupported);
        Assert.Equal(6, result.Counts.HumanOnly);
        Assert.Equal(79, result.Counts.EffectiveAvailable);
        Assert.Equal(18, result.Counts.EffectiveApprovalRequired);
        Assert.Equal(10, result.Counts.EffectiveGaps);

        var selfApproval = result.Capabilities.SelectMany(capability => capability.Operations)
            .Single(operation => operation.Id == "self_approval");
        Assert.Equal(FinanceAgentCoverageSupportStates.HumanOnly, selfApproval.SupportState);
        Assert.Equal(FinanceAgentCoverageSupportStates.HumanOnly, selfApproval.EffectiveState);
        Assert.Null(selfApproval.ToolName);
        Assert.Contains(result.Gaps, gap => gap.OperationId == selfApproval.Id &&
                                            gap.ReasonCode == FinanceAgentCoverageAvailabilityReasons.SegregationOfDuties);
    }

    [Fact]
    public async Task Effective_projection_rejects_cross_role_agent()
    {
        var registry = new StaticCompanyToolRegistry();
        var authority = AgentEffectiveAuthorityResolver.Resolve(
            new Agent(Guid.NewGuid(), Guid.NewGuid(), "sales", "Sam", "Sales", "Sales", null,
                AgentSeniority.Senior, AgentStatus.Active, AgentAutonomyLevel.Guided),
            registry);
        var service = new EffectiveFinanceAgentCoverageCatalogue(new FixedAuthorityResolver(authority), registry);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.GetEffectiveCoverageAsync(authority.CompanyId, authority.AgentId, default));
    }

    [Fact]
    public void Finance_coverage_endpoint_requires_finance_view_authorization()
    {
        var method = typeof(AgentsController).GetMethod(nameof(AgentsController.GetFinanceCoverageAsync),
            BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(method);
        var authorization = Assert.Single(method!.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(CompanyPolicies.FinanceView, authorization.Policy);
    }

    [Theory]
    [InlineData("Please initiate payment to this supplier", "payment_initiation")]
    [InlineData("Change credentials for the bank", "provider_credentials")]
    [InlineData("File VAT for August", "final_statutory_filing")]
    [InlineData("Close period August", "final_close_year_end_authority")]
    [InlineData("Approve your own journal", "self_approval")]
    [InlineData("Force provider outcome to success", "ambiguous_provider_resolution")]
    public void Permanent_human_boundaries_are_deterministically_classified(string request, string operationId)
    {
        var boundary = FinanceAgentCoverageCatalogue.MatchHumanOnlyOperation(request);

        Assert.NotNull(boundary);
        Assert.Equal(operationId, boundary!.Id);
        Assert.Equal(FinanceAgentCoverageSupportStates.HumanOnly, boundary.SupportState);
        Assert.False(string.IsNullOrWhiteSpace(boundary.SafeAlternative));
    }

    [Theory]
    [InlineData("Show close period readiness for August")]
    [InlineData("What is the close period status?")]
    [InlineData("Explain the close period blockers")]
    public void Close_read_requests_are_not_misclassified_as_final_human_authority(string request)
    {
        Assert.Null(FinanceAgentCoverageCatalogue.MatchHumanOnlyOperation(request));
    }

    private static Agent Laura() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "laura-finance-agent", "Laura", "Finance Manager", "Finance", null,
            AgentSeniority.Senior, AgentStatus.Active, AgentAutonomyLevel.Guided,
            tools: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            scopes: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase));

    private sealed class FixedAuthorityResolver(AgentEffectiveAuthorityDto authority) : IAgentEffectiveAuthorityResolver
    {
        public Task<AgentEffectiveAuthorityDto> ResolveAsync(
            Guid companyId,
            Guid agentId,
            CancellationToken cancellationToken) => Task.FromResult(authority);
    }

    private sealed class RegistryWithAdditionalFinanceTool : ICompanyToolRegistry
    {
        private readonly StaticCompanyToolRegistry _inner = new();
        private readonly TrustedToolRegistration _registration;
        private readonly ToolDefinitionManifest _definition;

        public RegistryWithAdditionalFinanceTool(string toolName)
        {
            var schema = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() };
            _registration = new TrustedToolRegistration(toolName, new HashSet<ToolActionType> { ToolActionType.Read },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "finance" }, "1.0.0", schema, schema);
            _definition = new ToolDefinitionManifest(toolName, "1.0.0", ToolActionType.Read, schema, schema,
                SelectionMetadata: new ToolSelectionMetadata("Test", "read", [], "None", [], 60, "none", "none", "Test", [], []));
        }

        public bool TryGetTool(string toolName, out TrustedToolRegistration registration)
        {
            if (string.Equals(toolName, _registration.ToolName, StringComparison.OrdinalIgnoreCase))
            {
                registration = _registration;
                return true;
            }

            return _inner.TryGetTool(toolName, out registration!);
        }

        public IReadOnlyList<TrustedToolRegistration> ListTools() => [.. _inner.ListTools(), _registration];

        public bool TryGetToolDefinition(string toolName, out ToolDefinitionManifest definition)
        {
            if (string.Equals(toolName, _definition.ToolName, StringComparison.OrdinalIgnoreCase))
            {
                definition = _definition;
                return true;
            }

            return _inner.TryGetToolDefinition(toolName, out definition!);
        }

        public IReadOnlyList<ToolDefinitionManifest> ListToolDefinitions() => [.. _inner.ListToolDefinitions(), _definition];
    }

    private sealed class RegistryWithPlannerActionMismatch : ICompanyToolRegistry
    {
        private readonly StaticCompanyToolRegistry _inner = new();
        private readonly string _toolName;
        private readonly ToolDefinitionManifest _replacement;

        public RegistryWithPlannerActionMismatch(string toolName)
        {
            _toolName = toolName;
            Assert.True(_inner.TryGetToolDefinition(toolName, out var definition));
            Assert.NotNull(definition.SelectionMetadata);
            _replacement = definition with
            {
                SelectionMetadata = definition.SelectionMetadata! with { ActionClass = ToolActionType.Execute.ToStorageValue() }
            };
        }

        public bool TryGetTool(string toolName, out TrustedToolRegistration registration) =>
            _inner.TryGetTool(toolName, out registration!);

        public IReadOnlyList<TrustedToolRegistration> ListTools() => _inner.ListTools();

        public bool TryGetToolDefinition(string toolName, out ToolDefinitionManifest definition)
        {
            if (string.Equals(toolName, _toolName, StringComparison.OrdinalIgnoreCase))
            {
                definition = _replacement;
                return true;
            }

            return _inner.TryGetToolDefinition(toolName, out definition!);
        }

        public IReadOnlyList<ToolDefinitionManifest> ListToolDefinitions() => _inner.ListToolDefinitions()
            .Select(definition => string.Equals(definition.ToolName, _toolName, StringComparison.OrdinalIgnoreCase)
                ? _replacement
                : definition)
            .ToArray();
    }
}
