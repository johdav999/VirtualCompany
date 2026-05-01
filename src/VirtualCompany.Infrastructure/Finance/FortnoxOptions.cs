using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxOptions
{
    public const string SectionName = "FinanceIntegrations:Fortnox";

    public bool Enabled { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string AuthorizationUrl { get; set; } = "https://apps.fortnox.se/oauth-v1/auth";
    public string TokenUrl { get; set; } = "https://apps.fortnox.se/oauth-v1/token";
    public string ApiBaseUrl { get; set; } = "https://api.fortnox.se/3";
    public int ApiMaxRetries { get; set; } = 3;
    public int ApiRetryBaseDelayMilliseconds { get; set; } = 200;
    public int ApiMaxRetryDelaySeconds { get; set; } = 30;
    public string[] Scopes { get; set; } = [];
}

public sealed class FortnoxOptionsValidator : IValidateOptions<FortnoxOptions>
{
    public ValidateOptionsResult Validate(string? name, FortnoxOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();

        RequireNonEmpty(options.ClientId, $"{FortnoxOptions.SectionName}:ClientId", failures);
        RequireNonEmpty(options.ClientSecret, $"{FortnoxOptions.SectionName}:ClientSecret", failures);
        RequireAbsoluteHttpUri(options.RedirectUri, $"{FortnoxOptions.SectionName}:RedirectUri", failures);
        RequireAbsoluteHttpUri(options.TokenUrl, $"{FortnoxOptions.SectionName}:TokenUrl", failures);
        RequireAbsoluteHttpUri(options.ApiBaseUrl, $"{FortnoxOptions.SectionName}:ApiBaseUrl", failures);
        RequireAbsoluteHttpUri(options.AuthorizationUrl, $"{FortnoxOptions.SectionName}:AuthorizationUrl", failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequireNonEmpty(string? value, string configPath, ICollection<string> failures)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            failures.Add($"{configPath} is required when Fortnox is enabled.");
        }
    }

    private static void RequireAbsoluteHttpUri(string? value, string configPath, ICollection<string> failures)
    {
        RequireNonEmpty(value, configPath, failures);
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            failures.Add($"{configPath} must be an absolute HTTP or HTTPS URI when Fortnox is enabled.");
        }
    }
}
