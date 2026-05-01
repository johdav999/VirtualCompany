using Microsoft.Extensions.Options;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxOptionsValidationTests
{
    [Theory]
    [InlineData(nameof(FortnoxOptions.ClientId), "FinanceIntegrations:Fortnox:ClientId")]
    [InlineData(nameof(FortnoxOptions.ClientSecret), "FinanceIntegrations:Fortnox:ClientSecret")]
    [InlineData(nameof(FortnoxOptions.RedirectUri), "FinanceIntegrations:Fortnox:RedirectUri")]
    [InlineData(nameof(FortnoxOptions.TokenUrl), "FinanceIntegrations:Fortnox:TokenUrl")]
    [InlineData(nameof(FortnoxOptions.AuthorizationUrl), "FinanceIntegrations:Fortnox:AuthorizationUrl")]
    [InlineData(nameof(FortnoxOptions.ApiBaseUrl), "FinanceIntegrations:Fortnox:ApiBaseUrl")]
    public void Enabled_integration_requires_required_keys(string propertyName, string expectedConfigPath)
    {
        var options = ValidOptions();
        SetStringProperty(options, propertyName, string.Empty);

        var result = Validate(options);

        Assert.True(result.Failed);
        var failure = Assert.Single(result.Failures);
        Assert.Contains(expectedConfigPath, failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(FortnoxOptions.RedirectUri), "not-a-uri")]
    [InlineData(nameof(FortnoxOptions.TokenUrl), "ftp://apps.fortnox.se/oauth-v1/token")]
    [InlineData(nameof(FortnoxOptions.ApiBaseUrl), "/relative")]
    public void Enabled_integration_requires_absolute_http_uris(string propertyName, string value)
    {
        var options = ValidOptions();
        SetStringProperty(options, propertyName, value);

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains("absolute HTTP or HTTPS URI", string.Join(Environment.NewLine, result.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_integration_does_not_require_secret_values()
    {
        var options = new FortnoxOptions
        {
            Enabled = false,
            ClientId = string.Empty,
            ClientSecret = string.Empty,
            RedirectUri = string.Empty,
            TokenUrl = string.Empty,
            ApiBaseUrl = string.Empty
        };

        var result = Validate(options);

        Assert.False(result.Failed);
    }

    [Fact]
    public void Validation_messages_do_not_echo_secret_values()
    {
        var options = ValidOptions();
        options.RedirectUri = "not-a-uri";
        options.ClientSecret = "super-secret-client-value";

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.DoesNotContain(options.ClientSecret, string.Join(Environment.NewLine, result.Failures), StringComparison.Ordinal);
    }

    private static ValidateOptionsResult Validate(FortnoxOptions options) =>
        new FortnoxOptionsValidator().Validate(Options.DefaultName, options);

    private static FortnoxOptions ValidOptions() =>
        new()
        {
            Enabled = true,
            ClientId = "client-id",
            ClientSecret = "client-secret",
            RedirectUri = "https://localhost:7136/finance/integrations/fortnox/callback",
            AuthorizationUrl = "https://apps.fortnox.se/oauth-v1/auth",
            TokenUrl = "https://apps.fortnox.se/oauth-v1/token",
            ApiBaseUrl = "https://api.fortnox.se/3/"
        };

    private static void SetStringProperty(FortnoxOptions options, string propertyName, string value)
    {
        var property = typeof(FortnoxOptions).GetProperty(propertyName);
        Assert.NotNull(property);
        property.SetValue(options, value);
    }
}
