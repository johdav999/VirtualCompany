using System.Web;

namespace VirtualCompany.Web.Services;

public static class FinanceJourneyRoutes
{
    public static string? Normalize(string? path, Guid company)
    {
        var local = ReturnUrlNavigation.NormalizeLocalReturnUrl(path);
        if (local is null || local.Length > 4096) return null;
        var uri = new Uri("https://local.test" + local);
        var route = local.Split('?', '#')[0];
        if (route.Contains('%') || route.Split('/').Any(x => x is "." or "..") ||
            !(route == "/finance" || route.StartsWith("/finance/", StringComparison.Ordinal))) return null;
        var query = HttpUtility.ParseQueryString(uri.Query);
        return Guid.TryParse(query["companyId"], out var id) && id == company ? local : null;
    }
    public static string Build(string path, Guid company, string location)
    {
        var current = new Uri(location); var source = HttpUtility.ParseQueryString(current.Query);
        var result = DashboardRoutes.EnsureWorkspaceContext(path, company,
            DashboardRoutes.OverviewPathFromLocation(location, company) ?? DashboardRoutes.BuildTodayPath(company, "finance"));
        var fragment = result.Contains('#') ? result[result.IndexOf('#')..] : "";
        result = result.Split('#')[0]; var index = result.IndexOf('?');
        var query = HttpUtility.ParseQueryString(index < 0 ? "" : result[(index + 1)..]);
        if (query["financeSource"] is null && source["financeSource"] is "operational" or "fortnox") query["financeSource"] = source["financeSource"];
        var origin = Normalize(source["financeReturnUrl"], company);
        if (origin is null && current.AbsolutePath is "/finance" or "/finance/invoices" or "/finance/supplier-bills" or "/finance/payments" or "/finance/transactions" or "/finance/reviews" or "/finance/accounting/reconciliation")
            origin = Normalize(current.PathAndQuery, company);
        if (current.AbsolutePath is "/finance/receivables-aging" or "/finance/payables-aging" or "/finance/cash-forecast")
        {
            var clean = HttpUtility.ParseQueryString(current.Query); clean.Remove("financeReturnUrl"); clean.Remove("recordReturnUrl");
            origin = Normalize(current.AbsolutePath + "?" + clean, company);
        }
        if (origin is not null) query["financeReturnUrl"] = origin;
        if (current.AbsolutePath.StartsWith("/finance/", StringComparison.Ordinal) && Guid.TryParse(current.AbsolutePath.Split('/')[^1], out _))
        {
            var clean = HttpUtility.ParseQueryString(current.Query); clean.Remove("recordReturnUrl");
            query["recordReturnUrl"] = Normalize(current.AbsolutePath + "?" + clean, company);
        }
        else if (Normalize(source["recordReturnUrl"], company) is { } record) query["recordReturnUrl"] = record;
        if (DashboardRoutes.NormalizePriorityPath(source["priorityReturnUrl"], company) is { } priority) query["priorityReturnUrl"] = priority;
        if (DashboardRoutes.NormalizeHealthPath(source["healthReturnUrl"], company) is { } health) query["healthReturnUrl"] = health;
        return (index < 0 ? result : result[..index]) + "?" + query + fragment;
    }
    public static string Back(string location, Guid company)
    {
        var query = HttpUtility.ParseQueryString(new Uri(location).Query);
        return AccountingJourneyRoutes.Normalize(query["accountingReturnUrl"], company) ??
            Normalize(query["recordReturnUrl"], company) ?? Normalize(query["financeReturnUrl"], company) ??
            DashboardRoutes.NormalizePriorityPath(query["priorityReturnUrl"], company) ?? DashboardRoutes.NormalizeHealthPath(query["healthReturnUrl"], company) ??
            DashboardRoutes.OverviewPathFromLocation(location, company) ?? DashboardRoutes.BuildTodayPath(company, "finance");
    }
}
