using System.Web;

namespace VirtualCompany.Web.Services;

public static class DecisionReviewRoutes
{
    public static string? Local(string? path, Guid company)
    {
        var local = AgentWorkRoutes.Local(path, company);
        if (local is null) return null;
        var uri = new Uri("https://local.test" + local); var query = HttpUtility.ParseQueryString(uri.Query);
        return uri.AbsolutePath == "/work" && query["tab"] == "approvals" && Guid.TryParse(query["itemId"], out var workId) && workId != Guid.Empty ||
            uri.AbsolutePath == "/approvals" && Guid.TryParse(query["approvalId"], out var approval) && approval != Guid.Empty ? local : null;
    }
    public static string? Back(string location, Guid company) => Local(HttpUtility.ParseQueryString(new Uri(location).Query)["decisionReturnUrl"], company);
}
