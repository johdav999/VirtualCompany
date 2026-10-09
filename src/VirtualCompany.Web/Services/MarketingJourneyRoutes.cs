namespace VirtualCompany.Web.Services;

public static class MarketingJourneyRoutes
{
    public static string Review(Guid companyId, Guid? campaignId, Guid? briefId, string? returnUrl, Guid? variantId = null, Guid? assetId = null) =>
        DashboardRoutes.EnsureWorkspaceContext($"/marketing/review?campaignId={campaignId}&briefId={briefId}&variantId={variantId}&assetId={assetId}", companyId, returnUrl ?? DashboardRoutes.BuildTodayPath(companyId, "marketing"));
    public static string Report(Guid companyId, string kind, string? returnUrl, Guid? campaignId = null, string? location = null)
    {
        var path = DashboardRoutes.WithQuery($"/marketing/reports/{kind}", ("campaignId", campaignId?.ToString("D")));
        return location is null
            ? DashboardRoutes.EnsureWorkspaceContext(path, companyId, returnUrl ?? DashboardRoutes.BuildTodayPath(companyId, "marketing"))
            : Build(path, companyId, location, returnToCurrent: true);
    }

    public static string Build(string path, Guid companyId, string location, bool returnToCurrent = false)
    {
        var current = new Uri(location);
        var source = System.Web.HttpUtility.ParseQueryString(current.Query);
        var result = DashboardRoutes.EnsureWorkspaceContext(path, companyId,
            DashboardRoutes.OverviewPathFromLocation(location, companyId) ?? DashboardRoutes.BuildTodayPath(companyId, "marketing"));
        var target = new Uri("https://local.test" + result);
        var query = System.Web.HttpUtility.ParseQueryString(target.Query);
        query["recordReturnUrl"] = BoundedReturn(returnToCurrent ? current.PathAndQuery : source["recordReturnUrl"], companyId, 2);
        query["healthReturnUrl"] = DashboardRoutes.NormalizeHealthPath(source["healthReturnUrl"], companyId);
        foreach (var key in new[] { "recordReturnUrl", "healthReturnUrl" })
            if (string.IsNullOrEmpty(query[key])) query.Remove(key);
        return target.AbsolutePath + "?" + query + target.Fragment;
    }

    private static string? BoundedReturn(string? path, Guid companyId, int depth)
    {
        var local = NormalizeRecordReturn(path, companyId);
        if (local is null) return null;
        var uri = new Uri("https://local.test" + local);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        if (query["recordReturnUrl"] is { } parent)
            query["recordReturnUrl"] = depth == 0 ? null : BoundedReturn(parent, companyId, depth - 1);
        if (string.IsNullOrEmpty(query["recordReturnUrl"])) query.Remove("recordReturnUrl");
        return uri.AbsolutePath + "?" + query + uri.Fragment;
    }
    public static string? NormalizeRecordReturn(string? path, Guid companyId)
    {
        var local = ReturnUrlNavigation.NormalizeLocalReturnUrl(path);
        if (local is null || local.Length > 12000) return null;
        var uri = new Uri("https://local.test" + local);
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        return Guid.TryParse(query["companyId"], out var id) && id == companyId ? local : null;
    }
    public static string State(string value) => value switch
    {
        "awaiting_approval" => "Waiting for launch approval", "dispatched" => "Sent to provider — confirmation pending",
        "delivered" => "Provider confirmed", "ambiguous" => "Delivery uncertain — reconcile first", "retry_scheduled" => "Retry scheduled",
        "submitted" => "Content needs review", "rejected" => "Revision needed", "proposed" => "Prepared", _ => value.Replace('_', ' ')
    };
}
