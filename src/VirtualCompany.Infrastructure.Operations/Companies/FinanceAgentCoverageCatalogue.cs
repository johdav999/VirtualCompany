using System.Diagnostics;
using System.Diagnostics.Metrics;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class EffectiveFinanceAgentCoverageCatalogue : IFinanceAgentCoverageCatalogue
{
    private readonly IAgentEffectiveAuthorityResolver _authorityResolver;
    private readonly IReadOnlyList<FinanceAgentCoverageCapabilityManifest> _manifests;

    public EffectiveFinanceAgentCoverageCatalogue(
        IAgentEffectiveAuthorityResolver authorityResolver,
        ICompanyToolRegistry toolRegistry)
    {
        _authorityResolver = authorityResolver;
        _manifests = VirtualCompany.Application.Finance.FinanceAgentCoverageCatalogue.Manifests;
        Validate(toolRegistry, _manifests);
    }

    public IReadOnlyList<FinanceAgentCoverageCapabilityManifest> ListManifests() => _manifests;

    public async Task<FinanceAgentEffectiveCoverageDto> GetEffectiveCoverageAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (agentId == Guid.Empty) throw new ArgumentException("AgentId is required.", nameof(agentId));

        var authority = await _authorityResolver.ResolveAsync(companyId, agentId, cancellationToken);
        if (!string.Equals(authority.Department, "Finance", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Finance agent coverage is available only for Finance agents.");
        }

        var capabilities = _manifests.Select(capability => Resolve(capability, authority)).ToArray();
        var operations = capabilities.SelectMany(capability => capability.Operations).ToArray();
        var gaps = capabilities.SelectMany(capability => capability.Operations
                .Where(operation => operation.EffectiveState is not AgentCapabilityStates.Available and not AgentCapabilityStates.ApprovalRequired)
                .Select(operation => new FinanceAgentCoverageGapDto(
                    capability.Id,
                    operation.Id,
                    operation.SupportState,
                    operation.AvailabilityReasonCode,
                    operation.Explanation,
                    operation.SafeAlternative,
                    operation.NavigationPath)))
            .OrderBy(gap => gap.CapabilityId, StringComparer.Ordinal)
            .ThenBy(gap => gap.OperationId, StringComparer.Ordinal)
            .ToArray();

        var counts = new FinanceAgentCoverageCountsDto(
            capabilities.Length,
            operations.Length,
            operations.Count(operation => operation.ToolName is not null),
            operations.Count(operation => operation.SupportState == FinanceAgentCoverageSupportStates.ImplementedRead),
            operations.Count(operation => operation.SupportState == FinanceAgentCoverageSupportStates.ImplementedRecommendDraft),
            operations.Count(operation => operation.SupportState == FinanceAgentCoverageSupportStates.ImplementedExecute),
            operations.Count(operation => operation.SupportState == FinanceAgentCoverageSupportStates.ConfigurationDependent),
            operations.Count(operation => operation.SupportState == FinanceAgentCoverageSupportStates.Unsupported),
            operations.Count(operation => operation.SupportState == FinanceAgentCoverageSupportStates.HumanOnly),
            operations.Count(operation => operation.EffectiveState == AgentCapabilityStates.Available),
            operations.Count(operation => operation.EffectiveState == AgentCapabilityStates.ApprovalRequired),
            gaps.Length);

        var result = new FinanceAgentEffectiveCoverageDto(
            FinanceAgentCoverageVersions.V1,
            companyId,
            agentId,
            authority.AgentName,
            authority.AgentStatus,
            authority.AutonomyLevel,
            counts,
            capabilities,
            gaps,
            authority.GeneratedUtc,
            authority.AuthorityVersion,
            authority.AuthorityHash);
        FinanceAgentCoverageTelemetry.RecordProjection(result);
        return result;
    }

    internal static void Validate(
        ICompanyToolRegistry toolRegistry,
        IReadOnlyList<FinanceAgentCoverageCapabilityManifest> manifests)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(manifests);

        var duplicateCapability = manifests.GroupBy(capability => capability.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicateCapability is not null)
            throw new InvalidOperationException($"Finance coverage capability '{duplicateCapability.Key}' is declared more than once.");

        var operations = manifests.SelectMany(capability => capability.Operations
            .Select(operation => (Capability: capability, Operation: operation))).ToArray();
        var duplicateOperation = operations.GroupBy(item => item.Operation.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicateOperation is not null)
            throw new InvalidOperationException($"Finance coverage operation '{duplicateOperation.Key}' is declared more than once.");

        var toolOperations = operations.Where(item => item.Operation.ToolName is not null).ToArray();
        var duplicateTool = toolOperations.GroupBy(item => item.Operation.ToolName!, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicateTool is not null)
            throw new InvalidOperationException($"Finance tool '{duplicateTool.Key}' has more than one coverage owner.");

        var registeredFinanceTools = toolRegistry.ListToolDefinitions()
            .Where(definition => toolRegistry.TryGetTool(definition.ToolName, out var registration) &&
                                 registration.Scopes.Contains("finance"))
            .ToDictionary(definition => definition.ToolName, StringComparer.OrdinalIgnoreCase);
        var ownedTools = toolOperations.ToDictionary(item => item.Operation.ToolName!, StringComparer.OrdinalIgnoreCase);
        var unowned = registeredFinanceTools.Keys.Except(ownedTools.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray();
        var missing = ownedTools.Keys.Except(registeredFinanceTools.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(value => value).ToArray();
        if (unowned.Length > 0 || missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Finance agent coverage is incomplete. Unowned registered tools: [{string.Join(", ", unowned)}]. " +
                $"Owned tools without registration: [{string.Join(", ", missing)}].");
        }

        var matrix = FinanceAgentAuthorityMatrix.Build(toolRegistry)
            .ToDictionary(entry => entry.ToolName, StringComparer.OrdinalIgnoreCase);
        foreach (var (toolName, item) in ownedTools)
        {
            var definition = registeredFinanceTools[toolName];
            var operation = item.Operation;
            var expectedSupport = definition.ActionType switch
            {
                ToolActionType.Read => FinanceAgentCoverageSupportStates.ImplementedRead,
                ToolActionType.Recommend => FinanceAgentCoverageSupportStates.ImplementedRecommendDraft,
                ToolActionType.Execute => FinanceAgentCoverageSupportStates.ImplementedExecute,
                _ => throw new ArgumentOutOfRangeException(nameof(definition.ActionType))
            };
            if (!string.Equals(operation.ActionClass, definition.ActionType.ToStorageValue(), StringComparison.Ordinal) ||
                !string.Equals(operation.SupportState, expectedSupport, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Finance tool '{toolName}' coverage action/support metadata is inconsistent with its registry definition.");
            }

            var authority = matrix[toolName];
            if (!authority.RequiredActorPermissions.Contains(operation.RequiredPermission, StringComparer.Ordinal) ||
                !string.Equals(authority.RiskTier, operation.RiskTier, StringComparison.Ordinal) ||
                !string.Equals(authority.ApprovalBehavior, operation.ApprovalBehavior, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Finance tool '{toolName}' coverage permission/risk/approval metadata is inconsistent with the authority matrix.");
            }
        }

        foreach (var item in operations)
        {
            var operation = item.Operation;
            if (!FinanceAgentCoverageSupportStates.All.Contains(operation.SupportState) ||
                string.IsNullOrWhiteSpace(operation.RequiredPermission) ||
                string.IsNullOrWhiteSpace(operation.RequiredScope) ||
                string.IsNullOrWhiteSpace(operation.RiskTier) ||
                string.IsNullOrWhiteSpace(operation.ApprovalBehavior) ||
                string.IsNullOrWhiteSpace(operation.AvailabilityReasonCode) ||
                string.IsNullOrWhiteSpace(operation.SafeExplanation) ||
                string.IsNullOrWhiteSpace(operation.SafeAlternative))
            {
                throw new InvalidOperationException($"Finance coverage operation '{operation.Id}' has incomplete support or safety metadata.");
            }
        }
    }

    private static FinanceAgentEffectiveCoverageCapabilityDto Resolve(
        FinanceAgentCoverageCapabilityManifest manifest,
        AgentEffectiveAuthorityDto authority)
    {
        var operations = manifest.Operations.Select(operation => Resolve(operation, authority)).ToArray();
        return new FinanceAgentEffectiveCoverageCapabilityDto(
            manifest.Id,
            manifest.Version,
            manifest.DomainWorkflow,
            manifest.Purpose,
            operations.Select(operation => operation.Id).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            operations.Select(operation => operation.RequiredPermission).Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
            operations.Select(operation => operation.RequiredScope).Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
            operations.Select(operation => operation.RiskTier).Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
            operations.Select(operation => operation.ApprovalBehavior).Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
            operations.SelectMany(operation => operation.Integrations).Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
            operations.SelectMany(operation => operation.SourceTypes).Distinct(StringComparer.Ordinal).OrderBy(value => value).ToArray(),
            operations);
    }

    private static FinanceAgentEffectiveCoverageOperationDto Resolve(
        FinanceAgentCoverageOperationManifest operation,
        AgentEffectiveAuthorityDto authority)
    {
        if (operation.ToolName is null)
        {
            var effectiveState = operation.SupportState switch
            {
                FinanceAgentCoverageSupportStates.ConfigurationDependent => AgentCapabilityStates.ConfigurationRequired,
                FinanceAgentCoverageSupportStates.HumanOnly => FinanceAgentCoverageSupportStates.HumanOnly,
                _ => AgentCapabilityStates.NotImplemented
            };
            return ToDto(operation, effectiveState, operation.AvailabilityReasonCode, operation.SafeExplanation);
        }

        if (!ToolActionTypeValues.TryParse(operation.ActionClass, out var actionType))
            return ToDto(operation, AgentCapabilityStates.NotImplemented, "invalid_action_class", "Coverage metadata is invalid; the tool is unavailable.");

        var effective = authority.Find(operation.ToolName, actionType, operation.RequiredScope);
        return effective is null
            ? ToDto(operation, AgentCapabilityStates.ConfigurationRequired, AgentAuthorityReasonCodes.ConfigurationRequired,
                "The tool is catalogued but not present in the agent's current effective authority.")
            : ToDto(operation, effective.State, effective.ReasonCode, effective.Explanation);
    }

    private static FinanceAgentEffectiveCoverageOperationDto ToDto(
        FinanceAgentCoverageOperationManifest operation,
        string effectiveState,
        string reasonCode,
        string explanation) =>
        new(
            operation.Id,
            operation.Name,
            operation.ActionClass,
            operation.SupportState,
            effectiveState,
            operation.RequiredPermission,
            operation.RequiredScope,
            operation.RiskTier,
            operation.ApprovalBehavior,
            operation.Integrations,
            operation.SourceTypes,
            reasonCode,
            explanation,
            operation.SafeAlternative,
            operation.NavigationPath,
            operation.ToolName);
}

internal static class FinanceAgentCoverageTelemetry
{
    internal const string MeterName = "VirtualCompany.Finance.AgentCoverage";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Projections = Meter.CreateCounter<long>("finance.agent_coverage.projections");
    private static readonly Histogram<int> GapCount = Meter.CreateHistogram<int>("finance.agent_coverage.gaps");

    public static void RecordProjection(FinanceAgentEffectiveCoverageDto coverage)
    {
        var tags = new TagList
        {
            { "catalogue.version", coverage.CatalogueVersion },
            { "agent.status", coverage.AgentStatus }
        };
        Projections.Add(1, tags);
        GapCount.Record(coverage.Counts.EffectiveGaps, tags);
    }
}
