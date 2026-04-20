using System.Web;

namespace VirtualCompany.Web.Services;

public static class DashboardRoutes
{
    private static readonly string[] CompanyScopedPrefixes =
    [
        "/dashboard",
        "/approvals",
        "/tasks",
        "/workflows",
        "/agents",
        "/queue",
        "/finance",
        "/activity-feed",
        "/briefing-preferences"
    ];

    public const string DashboardSource = "dashboard";
    public const string FilterQueryKey = "filter";
    public const string StatusQueryKey = "status";
    public const string SourceQueryKey = "source";
    public const string ActionQueryKey = "action";
    public const string RangeQueryKey = "range";
    public const string ViewQueryKey = "view";

    public static string BuildApprovalsPath(Guid? companyId, string? filter = "pending", Guid? approvalId = null, string? source = DashboardSource)
    {
        var normalizedFilter = NormalizeApprovalFilter(filter) ?? "pending";
        return WithQuery(
            "/approvals",
            ("companyId", companyId?.ToString("D")),
            (FilterQueryKey, normalizedFilter),
            (StatusQueryKey, MapApprovalFilterToStatus(normalizedFilter)),
            ("approvalId", approvalId?.ToString("D")),
            (SourceQueryKey, source));
    }

    public static string BuildTasksPath(
        Guid? companyId,
        string? filter = null,
        string? status = null,
        Guid? taskId = null,
        string? view = null,
        Guid? assignedAgentId = null,
        string? returnUrl = null,
        string? source = DashboardSource)
    {
        var normalizedFilter = NormalizeTaskFilter(filter);
        var normalizedStatus = NormalizeTaskStatus(status, normalizedFilter);
        if (normalizedFilter is null && normalizedStatus is null && taskId is null && assignedAgentId is null)
        {
            normalizedFilter = "today";
            normalizedStatus = MapTaskFilterToStatus(normalizedFilter);
        }

        return WithQuery("/tasks",
            ("companyId", companyId?.ToString("D")),
            (FilterQueryKey, normalizedFilter),
            (StatusQueryKey, normalizedStatus),
            ("assignedAgentId", assignedAgentId?.ToString("D")),
            ("taskId", taskId?.ToString("D")),
            (ViewQueryKey, view),
            ("returnUrl", ReturnUrlNavigation.NormalizeLocalReturnUrl(returnUrl)),
            (SourceQueryKey, source));
    }

    public static string BuildFinancePath(Guid? companyId, string path, string? action = null, string? range = null, string? source = DashboardSource) =>
        WithQuery(EnsureLeadingSlash(path), ("companyId", companyId?.ToString("D")), (ActionQueryKey, action), (RangeQueryKey, range), (SourceQueryKey, source));

    public static string NormalizeFocusTarget(string? route, Guid? companyId)
    {
        var fallback = companyId is Guid resolvedCompanyId
            ? $"/dashboard?companyId={resolvedCompanyId:D}"
            : "/dashboard";

        return EnsureCompanyContext(NormalizeKnownRoute(route, companyId), companyId, fallback);
    }

    public static string EnsureCompanyContext(string? route, Guid? companyId, string fallbackRoute)
    {
        var candidate = string.IsNullOrWhiteSpace(route) ? fallbackRoute : route.Trim();
        if (!candidate.StartsWith("/", StringComparison.Ordinal))
        {
            candidate = "/" + candidate.TrimStart('/');
        }

        candidate = NormalizeKnownRoute(candidate, companyId);
        candidate = EnsureCompanyId(candidate, companyId);
        return EnsureDashboardContext(candidate);
    }

    private static string EnsureCompanyId(string candidate, Guid? companyId)
    {
        if (companyId is not Guid resolvedCompanyId ||
            candidate.Contains("companyId=", StringComparison.OrdinalIgnoreCase) ||
            !CompanyScopedPrefixes.Any(prefix => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return candidate;
        }

        var separator = candidate.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{candidate}{separator}companyId={resolvedCompanyId:D}";
    }

    private static string NormalizeKnownRoute(string? route, Guid? companyId)
    {
        var candidate = string.IsNullOrWhiteSpace(route) ? "/dashboard" : route.Trim();

        if (candidate.StartsWith("/dashboard/briefings", StringComparison.OrdinalIgnoreCase))
        {
            return "/briefing-preferences";
        }

        if (TryExtractGuid(candidate, "/tasks/detail/", out var taskId))
        {
            return BuildTasksPath(companyId, taskId: taskId, view: "detail");
        }

        if (TryExtractGuid(candidate, "/finance/invoices/review/", out var invoiceId))
        {
            return FinanceRoutes.BuildInvoiceReviewDetailPath(invoiceId, companyId);
        }

        return candidate;
    }

    private static string EnsureDashboardContext(string route)
    {
        if (route.StartsWith("/approvals", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureApprovalContext(route);
        }

        if (route.StartsWith("/tasks", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureTaskContext(route);
        }

        if (route.StartsWith("/finance", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureFinanceContext(route);
        }

        if (route.StartsWith("/queue", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("/activity-feed", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("/agents", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("/workflows", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("/dashboard", StringComparison.OrdinalIgnoreCase) ||
            route.StartsWith("/briefing-preferences", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureSourceContext(route);
        }

        return route;
    }

    private static string EnsureApprovalContext(string route)
    {
        var filter = NormalizeApprovalFilter(GetQueryValue(route, FilterQueryKey) ?? GetQueryValue(route, StatusQueryKey)) ?? "pending";
        return WithQuery(
            route,
            (FilterQueryKey, filter),
            (StatusQueryKey, GetQueryValue(route, StatusQueryKey) ?? MapApprovalFilterToStatus(filter)),
            (SourceQueryKey, GetQueryValue(route, SourceQueryKey) ?? DashboardSource));
    }

    private static string EnsureTaskContext(string route)
    {
        var filter = NormalizeTaskFilter(GetQueryValue(route, FilterQueryKey));
        var status = NormalizeTaskStatus(GetQueryValue(route, StatusQueryKey), filter);
        var hasTaskSelection = !string.IsNullOrWhiteSpace(GetQueryValue(route, "taskId")) || !string.IsNullOrWhiteSpace(GetQueryValue(route, "assignedAgentId"));

        if (filter is null && status is null && !hasTaskSelection)
        {
            filter = "today";
            status = MapTaskFilterToStatus(filter);
        }

        return WithQuery(
            route,
            (FilterQueryKey, filter),
            (StatusQueryKey, status),
            (SourceQueryKey, GetQueryValue(route, SourceQueryKey) ?? DashboardSource));
    }

    private static string EnsureFinanceContext(string route)
    {
        var path = GetPath(route);
        var action = GetQueryValue(route, ActionQueryKey);
        var range = GetQueryValue(route, RangeQueryKey);

        if (string.IsNullOrWhiteSpace(action))
        {
            action =
                path.StartsWith(FinanceRoutes.Reviews, StringComparison.OrdinalIgnoreCase) ? "review" :
                path.StartsWith(FinanceRoutes.Anomalies, StringComparison.OrdinalIgnoreCase) ? "investigate" :
                path.StartsWith(FinanceRoutes.CashPosition, StringComparison.OrdinalIgnoreCase) ? "view-cash-position" :
                string.Equals(path, FinanceRoutes.Home, StringComparison.OrdinalIgnoreCase) ? "open-workspace" :
                "open";
        }

        if (string.IsNullOrWhiteSpace(range) &&
            (string.Equals(path, FinanceRoutes.Home, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(FinanceRoutes.CashPosition, StringComparison.OrdinalIgnoreCase) ||
             path.StartsWith(FinanceRoutes.MonthlySummary, StringComparison.OrdinalIgnoreCase)))
        {
            range = "this-month";
        }

        return WithQuery(
            route,
            (ActionQueryKey, action),
            (RangeQueryKey, range),
            (SourceQueryKey, GetQueryValue(route, SourceQueryKey) ?? DashboardSource));
    }

    private static string EnsureSourceContext(string route) =>
        WithQuery(
            route,
            (SourceQueryKey, GetQueryValue(route, SourceQueryKey) ?? DashboardSource));

    private static string? NormalizeApprovalFilter(string? filter) =>
        string.IsNullOrWhiteSpace(filter)
            ? null
            : filter.Trim().ToLowerInvariant() switch
            {
                "approved" => "approved",
                "rejected" => "rejected",
                _ => "pending"
            };

    private static string? NormalizeTaskFilter(string? filter) =>
        string.IsNullOrWhiteSpace(filter)
            ? null
            : filter.Trim().ToLowerInvariant() switch
            {
                "blocked" => "blocked",
                "awaiting_approval" or "awaiting-approval" => "awaiting-approval",
                "today" => "today",
                _ => filter.Trim().ToLowerInvariant()
            };

    private static string? NormalizeTaskStatus(string? status, string? filter)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            return status.Trim();
        }

        return MapTaskFilterToStatus(filter);
    }

    private static string MapApprovalFilterToStatus(string filter) =>
        filter.Equals("approved", StringComparison.OrdinalIgnoreCase) ? "approved" :
        filter.Equals("rejected", StringComparison.OrdinalIgnoreCase) ? "rejected" :
        "pending";

    private static string? MapTaskFilterToStatus(string? filter) =>
        filter?.Trim().ToLowerInvariant() switch
        {
            "blocked" => "blocked",
            "awaiting-approval" => "awaiting_approval",
            "today" => "pending",
            _ => null
        };

    private static string GetPath(string route)
    {
        var uri = new Uri($"http://localhost{EnsureLeadingSlash(route)}", UriKind.Absolute);
        return uri.AbsolutePath;
    }

    private static string? GetQueryValue(string route, string key)
    {
        var uri = new Uri($"http://localhost{EnsureLeadingSlash(route)}", UriKind.Absolute);
        var query = HttpUtility.ParseQueryString(uri.Query);
        return query[key];
    }

    private static bool TryExtractGuid(string route, string prefix, out Guid value) =>
        Guid.TryParse(route.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? route[prefix.Length..].Split(['/', '?', '#'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            : null, out value);

    private static string WithQuery(string route, params (string Key, string? Value)[] parameters)
    {
        var uri = new Uri($"http://localhost{EnsureLeadingSlash(route)}", UriKind.Absolute);
        var query = HttpUtility.ParseQueryString(uri.Query);

        foreach (var (key, value) in parameters)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                query[key] = value;
            }
        }

        var queryString = query.ToString();
        return string.IsNullOrWhiteSpace(queryString)
            ? $"{uri.AbsolutePath}{uri.Fragment}"
            : $"{uri.AbsolutePath}?{queryString}{uri.Fragment}";
    }

    private static string EnsureLeadingSlash(string route) =>
        route.StartsWith("/", StringComparison.Ordinal) ? route : "/" + route.TrimStart('/');
}