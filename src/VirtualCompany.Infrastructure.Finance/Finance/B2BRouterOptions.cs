using Microsoft.Extensions.Options;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class B2BRouterOptions
{
    public const string SectionName = "FinanceIntegrations:B2BRouter";
    public const string HttpClientName = "B2BRouter";
    public const string SandboxEnvironment = "sandbox";
    public const string ProductionEnvironment = "production";
    public const string ProviderKey = "b2brouter";
    public const string VerifiedApiVersion = "2026-06-26";
    public const string SandboxApiHost = "api-staging.b2brouter.net";
    public const string ProductionApiHost = "api.b2brouter.net";
    public const string PeppolBisBillingProfile = "peppol-bis-billing-3";
    public const string PeppolBisBillingVersion = "3.0";
    public const string InvoiceDocumentTypeCode = "xml.ubl.invoice.bis3";
    public const string CreditNoteDocumentTypeCode = "xml.ubl.creditnote.bis3";

    public bool Enabled { get; set; }
    public string Environment { get; set; } = SandboxEnvironment;
    public string ApiBaseUrl { get; set; } = "https://api-staging.b2brouter.net/";
    public string ApiVersion { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public Dictionary<string, string> CompanyAccountIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ApiKey { get; set; } = string.Empty;
    public string? PaymentAccountId { get; set; }
    public string? PaymentAccountName { get; set; }
    public string? PaymentServiceProviderId { get; set; }
    public bool WebhooksEnabled { get; set; }
    public string WebhookSecret { get; set; } = string.Empty;
    public int WebhookToleranceSeconds { get; set; } = 300;
    public int ReconciliationPollingSeconds { get; set; } = 60;
    public int MaximumReconciliationAttempts { get; set; } = 20;
    public int MaximumAttachmentBytes { get; set; } = 2_000_000;
}

public sealed class B2BRouterOptionsValidator : IValidateOptions<B2BRouterOptions>
{
    public ValidateOptionsResult Validate(string? name, B2BRouterOptions options)
    {
        if (!options.Enabled)
        {
            return ValidateOptionsResult.Success;
        }

        var failures = new List<string>();
        var environment = options.Environment?.Trim().ToLowerInvariant();

        if (environment is not B2BRouterOptions.SandboxEnvironment and not B2BRouterOptions.ProductionEnvironment)
        {
            failures.Add($"{B2BRouterOptions.SectionName}:Environment must be 'sandbox' or 'production'.");
        }

        if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var apiBaseUri) ||
            apiBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add($"{B2BRouterOptions.SectionName}:ApiBaseUrl must be an absolute HTTPS URI.");
        }
        else if (!string.IsNullOrEmpty(apiBaseUri.UserInfo) || !string.IsNullOrEmpty(apiBaseUri.Query) ||
                 !string.IsNullOrEmpty(apiBaseUri.Fragment) || apiBaseUri.AbsolutePath != "/")
        {
            failures.Add($"{B2BRouterOptions.SectionName}:ApiBaseUrl must be the provider HTTPS origin without credentials, query, fragment, or path.");
        }
        else if (environment == B2BRouterOptions.SandboxEnvironment &&
                 !string.Equals(apiBaseUri.Host, B2BRouterOptions.SandboxApiHost, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{B2BRouterOptions.SectionName}:ApiBaseUrl must use B2Brouter's staging host in sandbox mode.");
        }
        else if (environment == B2BRouterOptions.ProductionEnvironment &&
                 !string.Equals(apiBaseUri.Host, B2BRouterOptions.ProductionApiHost, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add($"{B2BRouterOptions.SectionName}:ApiBaseUrl must use B2Brouter's production API host in production mode.");
        }

        if (!string.Equals(options.ApiVersion, B2BRouterOptions.VerifiedApiVersion, StringComparison.Ordinal))
        {
            failures.Add($"{B2BRouterOptions.SectionName}:ApiVersion must be the verified version {B2BRouterOptions.VerifiedApiVersion}.");
        }

        if (string.IsNullOrWhiteSpace(options.AccountId) && options.CompanyAccountIds.Count == 0)
        {
            failures.Add($"{B2BRouterOptions.SectionName}:AccountId or CompanyAccountIds is required when B2Brouter is enabled.");
        }

        foreach (var mapping in options.CompanyAccountIds)
        {
            if (!Guid.TryParse(mapping.Key, out _) || string.IsNullOrWhiteSpace(mapping.Value))
                failures.Add($"{B2BRouterOptions.SectionName}:CompanyAccountIds must map company GUIDs to non-empty B2Brouter account ids.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            failures.Add($"{B2BRouterOptions.SectionName}:ApiKey is required when B2Brouter is enabled. Store it in user-secrets or the deployment secret provider.");
        }

        if (options.WebhooksEnabled && string.IsNullOrWhiteSpace(options.WebhookSecret))
        {
            failures.Add($"{B2BRouterOptions.SectionName}:WebhookSecret is required when signed webhooks are enabled. Store it in the deployment secret provider.");
        }

        if (options.WebhookToleranceSeconds is < 60 or > 900)
            failures.Add($"{B2BRouterOptions.SectionName}:WebhookToleranceSeconds must be between 60 and 900.");
        if (options.ReconciliationPollingSeconds is < 15 or > 3600)
            failures.Add($"{B2BRouterOptions.SectionName}:ReconciliationPollingSeconds must be between 15 and 3600.");
        if (options.MaximumReconciliationAttempts is < 1 or > 100)
            failures.Add($"{B2BRouterOptions.SectionName}:MaximumReconciliationAttempts must be between 1 and 100.");
        if (options.MaximumAttachmentBytes is < 1024 or > 10_000_000)
            failures.Add($"{B2BRouterOptions.SectionName}:MaximumAttachmentBytes must be between 1024 and 10000000.");

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
