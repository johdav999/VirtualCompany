namespace VirtualCompany.Web.Features.Agents;

public static class AgentIdentityPresentation
{
    public static string GetDepartmentTone(string? department) =>
        department?.Trim().ToLowerInvariant() switch
        {
            "finance" => "finance",
            "sales" => "sales",
            "support" => "support",
            _ => "default"
        };

    public static string GetInitials(string displayName)
    {
        var words = displayName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();

        return words.Length == 0
            ? "AI"
            : string.Concat(words.Select(word => char.ToUpperInvariant(word[0])));
    }

    public static string GetStatusBadgeCss(string? status) =>
        status?.Trim().ToLowerInvariant() switch
        {
            "active" => "text-bg-success",
            "paused" => "text-bg-warning",
            "restricted" => "text-bg-secondary",
            "archived" => "text-bg-dark",
            _ => "text-bg-light"
        };

    public static string GetHealthCss(string? healthStatus) =>
        healthStatus?.Trim().ToLowerInvariant() switch
        {
            "healthy" => "agent-health-healthy",
            "unknown" => "text-body-secondary",
            "archived" => "agent-health-archived",
            _ => "agent-health-attention"
        };

    public static bool IsDirectChatEligible(string? status) =>
        !string.Equals(status, "paused", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(status, "archived", StringComparison.OrdinalIgnoreCase);

    public static string BuildChatUri(Guid companyId, Guid agentId)
    {
        return $"/agents/{agentId}/chat?companyId={companyId}";
    }


}
