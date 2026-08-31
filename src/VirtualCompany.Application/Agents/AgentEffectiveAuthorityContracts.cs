using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Agents;

public static class AgentEffectiveAuthorityVersions
{
    public const string V1 = "agent-effective-authority-v1";
}

public static class AgentAuthorityGrantSources
{
    public const string Configured = "configured";
    public const string CompatibilityRolePolicy = "compatibility_role_policy";
}

public static class AgentAuthorityReasonCodes
{
    public const string Available = "authority_available";
    public const string ApprovalRequired = "authority_approval_required";
    public const string AgentInactive = "authority_agent_inactive";
    public const string ExplicitlyDenied = "authority_explicitly_denied";
    public const string ActionDenied = "authority_action_denied";
    public const string ScopeDenied = "authority_scope_denied";
    public const string ConfigurationRequired = "authority_configuration_required";
    public const string IntegrationUnavailable = "authority_integration_unavailable";
    public const string NotImplemented = "authority_not_implemented";
    public const string Stale = "effective_authority_stale";
}

public sealed record AgentAuthorityGrantDto(
    string ToolName,
    string ToolVersion,
    string ActionType,
    string Scope,
    string Source,
    string SourceVersion,
    string Reason);

public sealed record EffectiveAgentToolAuthorityDto(
    string ToolName,
    string ToolVersion,
    string ActionType,
    string Scope,
    string State,
    string ReasonCode,
    string Explanation,
    string? GrantSource,
    string? GrantSourceVersion,
    IReadOnlyList<string> RequiredCompanyPolicies,
    IReadOnlyList<string> RequiredFinancePermissions)
{
    public bool IsUsable => State is AgentCapabilityStates.Available or AgentCapabilityStates.ApprovalRequired;
    public string ActorPermission { get; init; } = string.Empty;
    public string ApprovalBehavior { get; init; } = string.Empty;
    public string IntegrationState { get; init; } = string.Empty;
}

public sealed record AgentEffectiveAuthorityDto(
    Guid CompanyId,
    Guid AgentId,
    string AgentName,
    string Department,
    string AgentStatus,
    bool CanReceiveAssignments,
    string AutonomyLevel,
    string AuthorityVersion,
    string AuthorityHash,
    IReadOnlyList<AgentAuthorityGrantDto> ConfiguredGrants,
    IReadOnlyList<AgentAuthorityGrantDto> CompatibilityGrants,
    IReadOnlyList<EffectiveAgentToolAuthorityDto> Tools,
    DateTime GeneratedUtc)
{
    public EffectiveAgentToolAuthorityDto? Find(string toolName, ToolActionType actionType, string? scope)
    {
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? string.Empty : scope.Trim();
        return Tools.FirstOrDefault(item =>
            string.Equals(item.ToolName, toolName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.ActionType, actionType.ToStorageValue(), StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(normalizedScope) || string.Equals(item.Scope, normalizedScope, StringComparison.OrdinalIgnoreCase)));
    }
}

public interface IAgentEffectiveAuthorityResolver
{
    Task<AgentEffectiveAuthorityDto> ResolveAsync(
        Guid companyId,
        Guid agentId,
        CancellationToken cancellationToken);
}
