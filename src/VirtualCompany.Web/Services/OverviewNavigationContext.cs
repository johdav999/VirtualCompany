namespace VirtualCompany.Web.Services;

/// <summary>
/// Circuit-local return navigation, not an access decision or a saved role preference.
/// URLs persist the context on reload; this covers retained module links within a circuit.
/// </summary>
public sealed class OverviewNavigationContext
{
    private Guid? companyId;
    private string? overviewPath;

    public string Resolve(string location, Guid? activeCompanyId)
    {
        if (companyId != activeCompanyId)
        {
            companyId = activeCompanyId;
            overviewPath = null;
        }

        if (activeCompanyId is not Guid id || id == Guid.Empty) return "/dashboard";
        overviewPath = DashboardRoutes.OverviewPathFromLocation(location, id) ?? overviewPath;
        return overviewPath ?? DashboardRoutes.BuildTodayPath(id);
    }
}
