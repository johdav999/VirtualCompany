namespace VirtualCompany.Web.Services;

public static class QueueRoutes
{
    public const string Home = "/queue";
    public const string CompanyIdQueryKey = "companyId";
    public const string PageQueryKey = "page";
    public const string PageSizeQueryKey = "pageSize";

    public static string BuildPath(Guid? companyId, int? pageNumber = null, int? pageSize = null)
    {
        var parameters = new List<string>();

        if (companyId is Guid resolvedCompanyId)
        {
            parameters.Add($"{CompanyIdQueryKey}={resolvedCompanyId:D}");
        }

        if (pageNumber.HasValue) parameters.Add($"{PageQueryKey}={Math.Max(1, pageNumber.Value)}");
        if (pageSize.HasValue) parameters.Add($"{PageSizeQueryKey}={Math.Max(1, pageSize.Value)}");

        return parameters.Count == 0
            ? Home
            : $"{Home}?{string.Join("&", parameters)}";
    }
}
