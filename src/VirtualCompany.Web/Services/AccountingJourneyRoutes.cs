using System.Web;

namespace VirtualCompany.Web.Services;

public static class AccountingJourneyRoutes
{
    public static string? Normalize(string? path, Guid company)
    {
        var local = FinanceJourneyRoutes.Normalize(path, company);
        return local?.Split('?', '#')[0].StartsWith("/finance/accounting/", StringComparison.Ordinal) == true ? local : null;
    }

    public static string Build(string path, Guid company, string location)
    {
        var current = new Uri(location);
        var source = HttpUtility.ParseQueryString(current.Query);
        var target = path.Split('?', '#')[0];
        var result = new Uri("https://local.test" + (target == "/accountant/portfolio" || target.StartsWith("/documents", StringComparison.Ordinal)
            ? FinanceRoutes.WithCompanyContext(path, company) : FinanceJourneyRoutes.Build(path, company, location)));
        var query = HttpUtility.ParseQueryString(result.Query);
        if (query["periodId"] is null && Guid.TryParse(source["periodId"], out var period)) query["periodId"] = period.ToString("D");
        // Keep one close parent beneath a statement/source origin, but strip any
        // other ancestry so repeated drill-downs cannot grow a recursive chain.
        var clean = HttpUtility.ParseQueryString(current.Query);
        clean.Remove("accountingReturnUrl");
        if (current.AbsolutePath != FinanceRoutes.AccountingCloseWorkspace &&
            Normalize(source["accountingReturnUrl"], company) is { } parent &&
            new Uri("https://local.test" + parent).AbsolutePath == FinanceRoutes.AccountingCloseWorkspace)
        {
            var parentUri = new Uri("https://local.test" + parent);
            var parentQuery = HttpUtility.ParseQueryString(parentUri.Query);
            parentQuery.Remove("accountingReturnUrl");
            clean["accountingReturnUrl"] = parentUri.AbsolutePath + "?" + parentQuery;
        }
        var origin = Normalize(current.AbsolutePath + "?" + clean, company);
        if (origin is not null && target == "/work") query["financeReturnUrl"] = origin;
        var changesReportView = target == FinanceRoutes.AccountingReports && current.AbsolutePath == target &&
            !string.Equals(query["view"], source["view"], StringComparison.OrdinalIgnoreCase);
        if (origin is not null && (current.AbsolutePath != result.AbsolutePath || changesReportView)) query["accountingReturnUrl"] = origin;
        else if (Normalize(source["accountingReturnUrl"], company) is { } retained) query["accountingReturnUrl"] = retained;
        return result.AbsolutePath + "?" + query + result.Fragment;
    }
}
