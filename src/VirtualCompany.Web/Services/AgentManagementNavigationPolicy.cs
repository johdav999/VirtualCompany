namespace VirtualCompany.Web.Services;

public static class AgentManagementNavigationPolicy
{
    public static bool IsManagementRoute(string? baseRelativeUri)
    {
        if (string.IsNullOrWhiteSpace(baseRelativeUri))
        {
            return false;
        }

        var path = baseRelativeUri
            .Split('?', '#')[0]
            .Trim('/');

        return string.Equals(path, "agents/manage", StringComparison.OrdinalIgnoreCase);
    }
}
