using Microsoft.Extensions.Localization;
using VirtualCompany.Web.Localization.Agents;

namespace VirtualCompany.Web.Services;

public static class AgentCapabilityPresenter
{
    public static bool RequiresRemediation(AgentCapabilityViewModel capability) =>
        IsState(capability, "permission_denied") || IsState(capability, "configuration_required");

    public static string GetExplanation(AgentCapabilityViewModel capability)
    {
        var missing = capability.MissingRequirements
            .Where(requirement => !string.IsNullOrWhiteSpace(requirement))
            .Select(FormatRequirement)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return missing.Length == 0
            ? GetReasonFallback(capability)
            : string.Join(" ", missing);
    }

    public static string GetActionLabel(AgentCapabilityViewModel capability) =>
        IsToolRegistrationMissing(capability) ? "Configure tools" : "Manage access";

    public static string GetExplanation(
        AgentCapabilityViewModel capability,
        IStringLocalizer<AgentsResources> localizer)
    {
        var missing = capability.MissingRequirements
            .Where(requirement => !string.IsNullOrWhiteSpace(requirement))
            .Select(requirement => FormatRequirement(requirement, localizer))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return missing.Length == 0
            ? GetReasonFallback(capability, localizer)
            : string.Join(" ", missing);
    }

    public static string GetActionLabel(
        AgentCapabilityViewModel capability,
        IStringLocalizer<AgentsResources> localizer) =>
        localizer[IsToolRegistrationMissing(capability)
            ? "CapabilityActionConfigureTools"
            : "CapabilityActionManageAccess"];

    public static string GetStateExplanation(
        AgentCapabilityViewModel capability,
        IStringLocalizer<AgentsResources> localizer) =>
        capability.ReasonCode.ToLowerInvariant() switch
        {
            "capability_available" => localizer["CapabilityAvailableExplanation"],
            "approval_required" => localizer["CapabilityApprovalRequiredExplanation"],
            "capability_not_implemented" => localizer["CapabilityNotImplementedExplanation"],
            "ai_provider_unavailable" => localizer["CapabilitySharedAiProvider"],
            _ => capability.Explanation
        };

    public static string GetActionHref(
        AgentCapabilityViewModel capability,
        Guid companyId,
        Guid agentId) =>
        IsToolRegistrationMissing(capability)
            ? $"/system/admin/tool-registry?companyId={companyId:D}"
            : $"/agents/manage?companyId={companyId:D}&agentId={agentId:D}#agent-access-configuration";

    private static bool IsToolRegistrationMissing(AgentCapabilityViewModel capability) =>
        string.Equals(capability.ReasonCode, "required_tool_unregistered", StringComparison.OrdinalIgnoreCase);

    private static bool IsState(AgentCapabilityViewModel capability, string expected) =>
        string.Equals(capability.State, expected, StringComparison.OrdinalIgnoreCase);

    private static string GetReasonFallback(AgentCapabilityViewModel capability) =>
        capability.ReasonCode.ToLowerInvariant() switch
        {
            "agent_not_active" => "Set this agent to Active and allow it to receive work.",
            "role_scope_mismatch" => "Use an agent whose role matches this capability.",
            _ => "Review this capability's access and configuration."
        };

    private static string GetReasonFallback(
        AgentCapabilityViewModel capability,
        IStringLocalizer<AgentsResources> localizer) =>
        capability.ReasonCode.ToLowerInvariant() switch
        {
            "agent_not_active" => localizer["CapabilityActivateAgent"],
            "role_scope_mismatch" => localizer["CapabilityUseMatchingRole"],
            _ => localizer["CapabilityReviewConfiguration"]
        };

    private static string FormatRequirement(string requirement)
    {
        var parts = requirement.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return "Review this capability's configuration.";
        }

        if (parts.Length == 2 && parts[0].Equals("tool", StringComparison.OrdinalIgnoreCase))
        {
            return $"Register the {FormatResource(parts[1])} trusted tool.";
        }

        if (parts.Length == 3 &&
            parts[0].Equals("permission", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals("tool", StringComparison.OrdinalIgnoreCase))
        {
            return $"Allow this agent to use the {FormatResource(parts[2])} tool.";
        }

        if (parts.Length == 3 &&
            parts[0].Equals("permission", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals("action", StringComparison.OrdinalIgnoreCase))
        {
            return $"Allow this agent to {FormatAction(parts[2])}.";
        }

        if (parts.Length == 4 &&
            parts[0].Equals("permission", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals("scope", StringComparison.OrdinalIgnoreCase))
        {
            return $"Grant this agent {FormatAction(parts[2])} access to {FormatResource(parts[3])}.";
        }

        if (parts.Length == 2 && parts[0].Equals("autonomy", StringComparison.OrdinalIgnoreCase))
        {
            return $"Set this agent's autonomy to {FormatAutonomy(parts[1])} or higher.";
        }

        if (parts.Length == 2 && parts[0].Equals("configuration", StringComparison.OrdinalIgnoreCase))
        {
            return FormatConfiguration(parts[1]);
        }

        if (parts.Length == 2 && parts[0].Equals("role", StringComparison.OrdinalIgnoreCase))
        {
            return $"Use a {FormatResource(parts[1])} agent for this capability.";
        }

        return $"Review the {FormatResource(requirement)} setting.";
    }

    private static string FormatRequirement(
        string requirement,
        IStringLocalizer<AgentsResources> localizer)
    {
        var parts = requirement.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return localizer["CapabilityReviewConfiguration"];
        }

        if (parts.Length == 2 && parts[0].Equals("tool", StringComparison.OrdinalIgnoreCase))
        {
            return localizer["CapabilityRegisterTool", FormatResource(parts[1], localizer)];
        }

        if (parts.Length == 3 && parts[0].Equals("permission", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("tool", StringComparison.OrdinalIgnoreCase))
        {
            return localizer["CapabilityGrantTool", FormatResource(parts[2], localizer)];
        }

        if (parts.Length == 3 && parts[0].Equals("permission", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("action", StringComparison.OrdinalIgnoreCase))
        {
            return localizer["CapabilityGrantAction", FormatAction(parts[2], localizer)];
        }

        if (parts.Length == 4 && parts[0].Equals("permission", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("scope", StringComparison.OrdinalIgnoreCase))
        {
            return localizer["CapabilityGrantScope", FormatAction(parts[2], localizer), FormatResource(parts[3], localizer)];
        }

        if (parts.Length == 2 && parts[0].Equals("autonomy", StringComparison.OrdinalIgnoreCase))
        {
            return localizer["CapabilitySetAutonomy", FormatAutonomy(parts[1])];
        }

        if (parts.Length == 2 && parts[0].Equals("configuration", StringComparison.OrdinalIgnoreCase))
        {
            return FormatConfiguration(parts[1], localizer);
        }

        if (parts.Length == 2 && parts[0].Equals("role", StringComparison.OrdinalIgnoreCase))
        {
            return localizer["CapabilityUseRole", FormatResource(parts[1], localizer)];
        }

        return localizer["CapabilityReviewConfiguration"];
    }

    private static string FormatAction(string value, IStringLocalizer<AgentsResources> localizer) =>
        value.ToLowerInvariant() switch
        {
            "read" => localizer["CapabilityActionRead"],
            "recommend" => localizer["CapabilityActionRecommend"],
            "execute" => localizer["CapabilityActionExecute"],
            _ => FormatResource(value, localizer)
        };

    private static string FormatAction(string value) => value.ToLowerInvariant() switch
    {
        "read" => "read",
        "recommend" => "make recommendations",
        "execute" => "perform approved actions",
        _ => FormatResource(value)
    };

    private static string FormatAutonomy(string value) => value.ToLowerInvariant() switch
    {
        "level_0" => "Level 0",
        "level_1" => "Level 1",
        "level_2" => "Level 2",
        "level_3" => "Level 3",
        _ => FormatResource(value)
    };

    private static string FormatConfiguration(string value) => value.ToLowerInvariant() switch
    {
        "knowledge_indexing_enabled" => "Enable document indexing.",
        "briefing_scheduler_enabled" => "Enable the briefing scheduler.",
        "shared_ai_provider" => "Connect the shared AI provider.",
        _ => $"Configure {FormatResource(value)}."
    };

    private static string FormatConfiguration(string value, IStringLocalizer<AgentsResources> localizer) =>
        value.ToLowerInvariant() switch
        {
            "knowledge_indexing_enabled" => localizer["CapabilityEnableKnowledgeIndexing"],
            "briefing_scheduler_enabled" => localizer["CapabilityEnableBriefingScheduler"],
            "shared_ai_provider" => localizer["CapabilitySharedAiProvider"],
            _ => localizer["CapabilityReviewConfiguration"]
        };

    private static string FormatResource(string value)
    {
        var normalized = value
            .Replace('.', ' ')
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();

        return normalized.ToLowerInvariant() switch
        {
            "tasks list" => "task list",
            "knowledge search" => "company knowledge search",
            "knowledge" => "company knowledge",
            _ => normalized
        };
    }

    private static string FormatResource(string value, IStringLocalizer<AgentsResources> localizer)
    {
        var normalized = value.Replace('.', ' ').Replace('_', ' ').Replace('-', ' ').Trim().ToLowerInvariant();
        return normalized switch
        {
            "tasks list" => localizer["ResourceTaskList"],
            "knowledge search" => localizer["ResourceKnowledgeSearch"],
            "knowledge" => localizer["ResourceCompanyKnowledge"],
            _ => normalized
        };
    }
}
