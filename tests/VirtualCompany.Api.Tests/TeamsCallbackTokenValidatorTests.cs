using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class TeamsCallbackTokenValidatorTests
{
    [Fact]
    public async Task Validates_signature_issuer_audience_lifetime_and_tenant()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var options = TeamsPresenterPackageBuilderTests.ValidOptions();
        var configuration = new OpenIdConnectConfiguration { Issuer = options.CallbackIssuer };
        configuration.SigningKeys.Add(key);
        var validator = new TeamsCallbackTokenValidator(
            Options.Create(options),
            new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
        var tenantId = Guid.NewGuid();
        var token = CreateToken(options.CallbackIssuer, options.BotApplicationId, tenantId, key, DateTime.UtcNow.AddMinutes(5));

        var actual = await validator.ValidateAsync(token, CancellationToken.None);

        Assert.Equal(tenantId, actual);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expired")]
    public async Task Rejects_invalid_signed_callback_tokens(string scenario)
    {
        using var trustedRsa = RSA.Create(2048);
        var trustedKey = new RsaSecurityKey(trustedRsa) { KeyId = "trusted" };
        var options = TeamsPresenterPackageBuilderTests.ValidOptions();
        var configuration = new OpenIdConnectConfiguration { Issuer = options.CallbackIssuer };
        configuration.SigningKeys.Add(trustedKey);
        var validator = new TeamsCallbackTokenValidator(
            Options.Create(options),
            new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));
        var token = CreateToken(
            scenario == "issuer" ? "https://untrusted.invalid" : options.CallbackIssuer,
            scenario == "audience" ? Guid.NewGuid().ToString("D") : options.BotApplicationId,
            Guid.NewGuid(),
            trustedKey,
            scenario == "expired" ? DateTime.UtcNow.AddMinutes(-5) : DateTime.UtcNow.AddMinutes(5));

        var exception = await Assert.ThrowsAsync<TeamsIdentityException>(() =>
            validator.ValidateAsync(token, CancellationToken.None));

        Assert.Equal(TeamsIdentityFailureCodes.CallbackUnauthorized, exception.Code);
        Assert.DoesNotContain(token, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_malformed_callback_token_with_safe_error()
    {
        using var rsa = RSA.Create(2048);
        var options = TeamsPresenterPackageBuilderTests.ValidOptions();
        var configuration = new OpenIdConnectConfiguration { Issuer = options.CallbackIssuer };
        configuration.SigningKeys.Add(new RsaSecurityKey(rsa) { KeyId = "trusted" });
        var validator = new TeamsCallbackTokenValidator(
            Options.Create(options),
            new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration));

        var exception = await Assert.ThrowsAsync<TeamsIdentityException>(() =>
            validator.ValidateAsync("not-a-jwt", CancellationToken.None));

        Assert.Equal(TeamsIdentityFailureCodes.CallbackUnauthorized, exception.Code);
        Assert.DoesNotContain("not-a-jwt", exception.Message, StringComparison.Ordinal);
    }

    private static string CreateToken(
        string issuer,
        string audience,
        Guid tenantId,
        SecurityKey key,
        DateTime expiresUtc)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity([new Claim("tid", tenantId.ToString("D"))]),
            NotBefore = expiresUtc > DateTime.UtcNow
                ? DateTime.UtcNow.AddMinutes(-1)
                : expiresUtc.AddMinutes(-5),
            Expires = expiresUtc,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
        };
        return new JwtSecurityTokenHandler().CreateEncodedJwt(descriptor);
    }
}
