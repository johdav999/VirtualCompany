using System.Web;

namespace VirtualCompany.Web.Services;

public static class SalesJourneyRoutes
{
    public static string? Normalize(string? path, Guid company)
    {
        var local = ReturnUrlNavigation.NormalizeLocalReturnUrl(path);
        if (local is null) return null;
        var route = local.Split('?', '#')[0];
        var decoded = Uri.UnescapeDataString(route);
        if (decoded.Split('/').Any(x => x is "." or "..") || route.Contains('%')) return null;
        var query = HttpUtility.ParseQueryString(local.Contains('?') ? local.Split('?', 2)[1].Split('#')[0] : "");
        if (!(route == "/app/sales" || route.StartsWith("/app/sales/", StringComparison.Ordinal)) ||
            !Guid.TryParse(query["companyId"], out var id) || id != company || local.Length > 4096) return null;
        return local;
    }

    public static string Build(string path, Guid company, string location)
    {
        var current = new Uri(location);
        var source = HttpUtility.ParseQueryString(current.Query);
        var result = DashboardRoutes.EnsureWorkspaceContext(path, company,
            DashboardRoutes.OverviewPathFromLocation(location, company) ?? DashboardRoutes.BuildTodayPath(company, "sales"));
        var queryIndex = result.IndexOf('?');
        var query = HttpUtility.ParseQueryString(result[(queryIndex + 1)..]);
        var salesReturn = Normalize(source["salesReturnUrl"], company);
        if (current.AbsolutePath is "/app/sales/pipeline/report" or "/app/sales/activities" or "/app/sales/forecast" or "/app/sales/reports/management")
        {
            var origin = HttpUtility.ParseQueryString(current.Query);
            origin.Remove("salesReturnUrl"); origin.Remove("recordReturnUrl");
            salesReturn = Normalize(source["salesReturnUrl"] is null && source["recordReturnUrl"] is null
                ? current.PathAndQuery : current.AbsolutePath + "?" + origin, company);
        }
        if (salesReturn is null && current.AbsolutePath is "/app/sales" or "/app/sales/pipeline" or "/app/sales/pipeline/report" or "/app/sales/activities" or "/app/sales/forecast" or "/app/sales/reports/management" or "/app/sales/prospects")
            salesReturn = Normalize(current.PathAndQuery, company);
        if (salesReturn is not null) query["salesReturnUrl"] = salesReturn;
        if (current.AbsolutePath.StartsWith("/app/sales/deals/", StringComparison.Ordinal) && path.StartsWith("/app/sales/contacts/", StringComparison.Ordinal) ||
            (current.AbsolutePath.StartsWith("/app/sales/meeting-invitations/", StringComparison.Ordinal) || current.AbsolutePath.StartsWith("/app/sales/rooms/", StringComparison.Ordinal)) &&
            (path.StartsWith("/app/sales/presentation-presets", StringComparison.Ordinal) || path.StartsWith("/work", StringComparison.Ordinal)))
            query["recordReturnUrl"] = Normalize(current.PathAndQuery, company);
        // Evidence and report returns are independently validated by their owning shared helpers.
        var health = DashboardRoutes.NormalizeHealthPath(source["healthReturnUrl"], company);
        if (health is not null) query["healthReturnUrl"] = health;
        return result[..queryIndex] + "?" + query;
    }
    public static string Back(string location, Guid company)
    {
        var query = HttpUtility.ParseQueryString(new Uri(location).Query);
        return Normalize(query["recordReturnUrl"], company) ?? Normalize(query["salesReturnUrl"], company) ??
            DashboardRoutes.NormalizeHealthPath(query["healthReturnUrl"], company) ??
            DashboardRoutes.OverviewPathFromLocation(location, company) ?? DashboardRoutes.BuildTodayPath(company, "sales");
    }
}
