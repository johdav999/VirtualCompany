using System.Security.Claims;
using VirtualCompany.Application.Auth;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ClaimsPrincipalExternalUserIdentityFactoryTests
{
    private readonly ClaimsPrincipalExternalUserIdentityFactory _factory = new();

    [Fact]
    public void Create_maps_standard_oidc_style_claims_without_virtual_company_claims()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim("sub", "oidc-subject"),
                new Claim("email", "alice@example.com"),
                new Claim("name", "Alice Oidc")
            },
            "oidc"));

        var identity = _factory.Create(principal);

        Assert.NotNull(identity);
        Assert.Equal("oidc", identity!.Provider);
        Assert.Equal("oidc-subject", identity.Subject);
        Assert.Equal("alice@example.com", identity.Email);
        Assert.Equal("Alice Oidc", identity.DisplayName);
    }

    [Fact]
    public void Create_prefers_normalized_virtual_company_claims_when_present()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(CurrentUserClaimTypes.AuthProvider, "entra-id"),
                new Claim(CurrentUserClaimTypes.AuthSubject, "provider-subject"),
                new Claim(ClaimTypes.NameIdentifier, "framework-subject"),
                new Claim(ClaimTypes.Email, "alice@example.com"),
                new Claim(ClaimTypes.Name, "Alice")
            },
            "oidc"));

        var identity = _factory.Create(principal);

        Assert.NotNull(identity);
        Assert.Equal("entra-id", identity!.Provider);
        Assert.Equal("provider-subject", identity.Subject);
    }
}