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
        if (!normalized.StartsWith("/", StringComparison.Ordinal) || normalized.StartsWith("//", StringComparison.Ordinal))
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

        var separator = path.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return $"{path}{separator}returnUrl={Uri.EscapeDataString(normalizedReturnUrl)}";
    }
}
