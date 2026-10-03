using System.Web;

namespace VirtualCompany.Web.Services;

public static class AgentWorkRoutes
{
    public static string? Local(string? path, Guid company)
    {
        var normalized = ReturnUrlNavigation.NormalizeLocalReturnUrl(path);
        if (normalized is null || normalized.Length > 12000) return null;
        var uri = new Uri("https://local.test" + normalized);
        return Guid.TryParse(HttpUtility.ParseQueryString(uri.Query)["companyId"], out var found) && found == company ? normalized : null;
    }
    public static string Detail(string kind, Guid id, Guid company, string current)
    {
        var uri = new Uri(current); var query = HttpUtility.ParseQueryString(uri.Query);
        return DashboardRoutes.WithQuery($"/agents/work/{kind}/{id:D}", ("companyId", company.ToString("D")),
            ("boardReturnUrl", Board(uri.PathAndQuery, company) ?? Board(query["boardReturnUrl"], company)),
            ("recordReturnUrl", BoundedReturn(uri.PathAndQuery, company, 2)),
            ("returnUrl", DashboardRoutes.OverviewPathFromLocation(current, company)));
    }
    public static string? Board(string? path, Guid company)
    {
        var local = Local(path, company);
        return local is not null && new Uri("https://local.test" + local).AbsolutePath == "/agents/staff" ? local : null;
    }
    public static string? Record(string? path, Guid company)
    {
        var local = Local(path, company); if (local is null) return null;
        var route = new Uri("https://local.test" + local).AbsolutePath;
        var segments = route.Split('/');
        return route is "/agents/staff" or "/agents/staff/summary" ||
            segments.Length == 5 && segments[1] == "agents" && segments[2] == "work" &&
            new[] { "task", "initiative", "case", "deal" }.Contains(segments[3]) && Guid.TryParse(segments[4],out _)
            ? local : null;
    }
    public static string? Back(string location, Guid company)
    {
        var query = HttpUtility.ParseQueryString(new Uri(location).Query);
        return Record(query["agentWorkReturnUrl"], company) ?? Record(query["recordReturnUrl"], company);
    }
    public static string Related(string path, Guid company, string location)
    {
        var current = new Uri(location); var query = HttpUtility.ParseQueryString(current.Query);
        return DashboardRoutes.WithQuery(path,
            ("boardReturnUrl", Board(current.PathAndQuery, company) ?? Board(query["boardReturnUrl"], company)),
            ("agentWorkReturnUrl", Record(BoundedReturn(current.PathAndQuery, company, 2), company) ?? Record(query["agentWorkReturnUrl"],company)),
            ("recordReturnUrl", BoundedReturn(current.PathAndQuery, company, 2)),
            ("returnUrl", DashboardRoutes.OverviewPathFromLocation(location, company)));
    }
    private static string? BoundedReturn(string? path, Guid company, int depth)
    {
        var local = Local(path, company); if (local is null) return null;
        var uri = new Uri("https://local.test" + local); var query = HttpUtility.ParseQueryString(uri.Query);
        foreach (var key in new[] { "recordReturnUrl", "agentWorkReturnUrl" })
        {
            if (query[key] is { } parent) query[key] = depth == 0 ? null : BoundedReturn(parent, company, depth - 1);
            if (string.IsNullOrEmpty(query[key])) query.Remove(key);
        }
        return uri.AbsolutePath + "?" + query + uri.Fragment;
    }
    public static string State(string value) => value switch { "planned" => "Planned", "in_progress" => "In progress",
        "awaiting_approval" => "Waiting for approval", "completed" => "Completed", "blocked" => "Blocked", "failed" => "Failed", "paused" => "Paused", _ => "State unavailable" };
    public static string Area(string value) => value switch { "company" => "Company", "finance" => "Finance", "sales" => "Sales", "marketing" => "Marketing", "support" => "Customer Support", _ => "Responsibility not recorded" };
}
