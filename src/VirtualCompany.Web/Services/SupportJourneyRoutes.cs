using System.Web;

namespace VirtualCompany.Web.Services;

public static class SupportJourneyRoutes
{
    public static string? NormalizeReturn(string? value, Guid companyId)
    {
        var local = ReturnUrlNavigation.NormalizeLocalReturnUrl(value);
        if (local is null || local.Length > 12000) return null;
        var uri = new Uri("https://local.test" + local);
        if (!(uri.AbsolutePath == "/support" || uri.AbsolutePath.StartsWith("/support/", StringComparison.Ordinal))) return null;
        var query = HttpUtility.ParseQueryString(uri.Query);
        return Guid.TryParse(query["companyId"], out var id) && id == companyId ? local : null;
    }

    public static string Build(string path, Guid companyId, string? overview, string? supportReturn = null)
    {
        var route = DashboardRoutes.EnsureWorkspaceContext(path, companyId,
            DashboardRoutes.NormalizeOverviewPath(overview, companyId) ?? DashboardRoutes.BuildTodayPath(companyId, "customers"));
        var uri = new Uri("https://local.test" + route);
        var query = HttpUtility.ParseQueryString(uri.Query);
        query["supportReturnUrl"] = NormalizeReturn(supportReturn, companyId);
        return uri.AbsolutePath + "?" + query + uri.Fragment;
    }

    private static string? BoundedReturn(string path, Guid companyId, int depth)
    {
        var local = NormalizeReturn(path, companyId);
        if (local is null) return null;
        var uri = new Uri("https://local.test" + local);
        var query = HttpUtility.ParseQueryString(uri.Query);
        var parent = query["supportReturnUrl"];
        if (!string.IsNullOrEmpty(parent)) {
            var bounded = depth == 0 ? null : BoundedReturn(parent, companyId, depth - 1);
            if (bounded != parent) { query["supportReturnUrl"] = bounded; return uri.AbsolutePath + "?" + query; }
        }
        return local;
    }

    public static string Record(string path, Guid companyId, string location)
    {
        var uri = new Uri(location);
        var origin = HttpUtility.ParseQueryString(uri.Query);
        // Keep one bounded parent. A sibling history case returns to the same parent.
        var currentIsCase = uri.AbsolutePath.StartsWith("/support/cases/", StringComparison.Ordinal);
        var targetIsCase = path.StartsWith("/support/cases/", StringComparison.Ordinal);
        var parent = currentIsCase && targetIsCase
            ? NormalizeReturn(origin["supportReturnUrl"], companyId)
            : currentIsCase || targetIsCase ? BoundedReturn(uri.PathAndQuery, companyId, 2)
            : NormalizeReturn(origin["supportReturnUrl"], companyId) ?? NormalizeReturn(uri.PathAndQuery, companyId);
        var result = Build(path, companyId, origin["returnUrl"], parent);
        var target = new Uri("https://local.test" + result);
        var query = HttpUtility.ParseQueryString(target.Query);
        if (uri.AbsolutePath is "/support" or "/support/cases" && target.AbsolutePath == "/support/reports")
            foreach (var key in new[] { "q", "status", "priority", "category", "owner" })
                if (!string.IsNullOrWhiteSpace(origin[key])) query[key] = origin[key];
        if (DashboardRoutes.NormalizePriorityPath(origin["priorityReturnUrl"], companyId) is { } priority) query["priorityReturnUrl"] = priority;
        if (DashboardRoutes.NormalizeHealthPath(origin["healthReturnUrl"], companyId) is { } health) query["healthReturnUrl"] = health;
        return target.AbsolutePath + "?" + query;
    }
}
