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
        "/marketing",
        "/app/sales",
        "/support",
        "/work",
        "/history",
        "/company-operation",
        "/settings",
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
    public const string LensQueryKey = "lens";
    public const string PeriodQueryKey = "period";

    /// <summary>Carry an authorized workspace's origin into an existing business route.</summary>
    public static string EnsureWorkspaceContext(string? route, Guid companyId, string overviewPath)
    {
        var localRoute = ReturnUrlNavigation.NormalizeLocalReturnUrl(route);
        var origin = NormalizeOverviewPath(overviewPath, companyId) ?? BuildTodayPath(companyId);
        if (localRoute is null) return origin;
        var candidate = EnsureCompanyContext(localRoute ?? origin, companyId, origin);
        var path = GetPath(candidate);
        if (!IsCompanyScopedPath(path)) return origin;

        // A stale link must not carry another company's context into this workspace.
        candidate = WithQuery(candidate, ("companyId", companyId.ToString("D")));
        return string.Equals(path, "/dashboard", StringComparison.OrdinalIgnoreCase)
            ? candidate
            : ReturnUrlNavigation.AppendReturnUrl(candidate, origin);
    }

    public static string? NormalizeOverviewPath(string? path, Guid companyId)
    {
        var local = ReturnUrlNavigation.NormalizeLocalReturnUrl(path);
        if (local is null || !string.Equals(GetPath(local), "/dashboard", StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParse(GetQueryValue(local, "companyId"), out var routeCompany) || routeCompany != companyId)
            return null;

        var lens = TodayWorkspaceLensValues.Normalize(GetQueryValue(local, LensQueryKey));
        if (lens.Length > 0 && !TodayWorkspaceLensValues.All.Contains(lens)) return null;
        return local;
    }

    public static string? OverviewPathFromLocation(string location, Guid companyId)
    {
        var uri = new Uri(location, UriKind.Absolute);
        var direct = NormalizeOverviewPath(uri.PathAndQuery, companyId);
        return direct ?? NormalizeOverviewPath(HttpUtility.ParseQueryString(uri.Query)["returnUrl"], companyId);
    }

    private static bool IsCompanyScopedPath(string path) => CompanyScopedPrefixes.Any(prefix =>
        string.Equals(path, prefix, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));

    public static string BuildTodayPath(Guid companyId, string? lens = null) =>
        WithQuery(
            "/dashboard",
            ("companyId", companyId == Guid.Empty ? null : companyId.ToString("D")),
            (LensQueryKey, TodayWorkspaceLensValues.Normalize(lens)));

    public static string BuildHealthPath(Guid companyId, string? department = null, string? returnUrl = null) =>
        WithQuery("/dashboard/company-health", ("companyId", companyId.ToString("D")),
            ("department", department), ("returnUrl", NormalizeOverviewPath(returnUrl, companyId) ?? BuildTodayPath(companyId, "company")));
    public static string? NormalizeHealthPath(string? path, Guid companyId)
    {
        var local = ReturnUrlNavigation.NormalizeLocalReturnUrl(path);
        var department = local is null ? null : GetQueryValue(local, "department");
        return local is not null && GetPath(local) == "/dashboard/company-health" &&
            Guid.TryParse(GetQueryValue(local, "companyId"), out var id) && id == companyId &&
            (string.IsNullOrEmpty(department) || TodayWorkspaceLensValues.All.Contains(department)) ? local : null;
    }

    public static string BuildPriorityPath(Guid companyId, string lens, string key, string? returnUrl = null) =>
        WithQuery("/dashboard/priorities", ("companyId", companyId.ToString("D")),
            (LensQueryKey, TodayWorkspaceLensValues.Normalize(lens)), ("key", key),
            ("returnUrl", NormalizeOverviewPath(returnUrl, companyId) ?? BuildTodayPath(companyId, lens)));

    public static string? NormalizePriorityPath(string? path, Guid companyId)
    {
        var local = ReturnUrlNavigation.NormalizeLocalReturnUrl(path);
        return local is not null && GetPath(local) == "/dashboard/priorities" &&
            Guid.TryParse(GetQueryValue(local, "companyId"), out var id) && id == companyId &&
            TodayWorkspaceLensValues.All.Contains(GetQueryValue(local, "lens") ?? "") &&
            !string.IsNullOrWhiteSpace(GetQueryValue(local, "key")) ? local : null;
    }

    public static string BuildMonthlyPath(Guid companyId, string? lens = null, int? year = null, int? month = null) =>
        WithQuery(
            "/dashboard",
            ("companyId", companyId == Guid.Empty ? null : companyId.ToString("D")),
            (PeriodQueryKey, "month"),
            (LensQueryKey, TodayWorkspaceLensValues.Normalize(lens)),
            ("year", year?.ToString()),
            ("month", month?.ToString()));

    public static string BuildResponsibilitySettingsPath(Guid? companyId) =>
        WithQuery("/settings/responsibilities", ("companyId", companyId?.ToString("D")));

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
            !IsCompanyScopedPath(GetPath(candidate)))
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
                (path.StartsWith(FinanceRoutes.Anomalies, StringComparison.OrdinalIgnoreCase) || path.StartsWith(FinanceRoutes.Issues, StringComparison.OrdinalIgnoreCase)) ? "investigate" :
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

    public static string WithQuery(string route, params (string Key, string? Value)[] parameters)
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
