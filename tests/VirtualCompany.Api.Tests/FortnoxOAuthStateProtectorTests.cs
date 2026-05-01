using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxOAuthStateProtectorTests
{
    [Fact]
    public void Protected_state_roundtrips_company_user_nonce_and_expiry()
    {
        var protector = CreateProtector();
        var state = new FortnoxOAuthState(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "nonce",
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(10),
            Reconnect: true,
            new Uri("https://localhost/finance/integrations/fortnox"));

        var protectedState = protector.Protect(state);
        var unprotected = protector.Unprotect(protectedState);

        Assert.Equal(state.CompanyId, unprotected.CompanyId);
        Assert.Equal(state.UserId, unprotected.UserId);
        Assert.Equal(state.Nonce, unprotected.Nonce);
        Assert.True(unprotected.Reconnect);
    }

    [Fact]
    public void Invalid_state_is_rejected_without_echoing_payload()
    {
        var protector = CreateProtector();

        var exception = Assert.Throws<UnauthorizedAccessException>(() => protector.Unprotect("not-valid"));

        Assert.DoesNotContain("not-valid", exception.Message, StringComparison.Ordinal);
    }

    private static DataProtectionFortnoxOAuthStateProtector CreateProtector() =>
        new(new EphemeralDataProtectionProvider());
}
