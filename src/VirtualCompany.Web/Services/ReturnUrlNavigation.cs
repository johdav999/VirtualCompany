namespace VirtualCompany.Web.Services;

public static class ReturnUrlNavigation
{
    public static string? NormalizeLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        var normalized = returnUrl.Trim();
        var path = normalized.Split('?', '#')[0];
        var decodedPath = Uri.UnescapeDataString(path);
        if (!normalized.StartsWith("/", StringComparison.Ordinal) ||
            decodedPath.StartsWith("//", StringComparison.Ordinal) ||
            decodedPath.Contains('\\') || decodedPath.Any(char.IsControl) || normalized.Any(char.IsControl))
        {
            return null;
        }

        return normalized;
    }

    public static string AppendReturnUrl(string path, string? returnUrl)
    {
        var normalizedReturnUrl = NormalizeLocalReturnUrl(returnUrl);
        if (normalizedReturnUrl is null)
        {
            return path;
        }

        var fragmentIndex = path.IndexOf('#');
        var route = fragmentIndex < 0 ? path : path[..fragmentIndex];
        var fragment = fragmentIndex < 0 ? string.Empty : path[fragmentIndex..];
        var queryIndex = route.IndexOf('?');
        var query = System.Web.HttpUtility.ParseQueryString(queryIndex < 0 ? string.Empty : route[(queryIndex + 1)..]);
        query["returnUrl"] = normalizedReturnUrl;
        return $"{(queryIndex < 0 ? route : route[..queryIndex])}?{query}{fragment}";
    }
}
