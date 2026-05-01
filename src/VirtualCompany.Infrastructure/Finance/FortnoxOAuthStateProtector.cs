using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public interface IFortnoxOAuthStateProtector
{
    string Protect(FortnoxOAuthState state);
    FortnoxOAuthState Unprotect(string protectedState);
}

public sealed class DataProtectionFortnoxOAuthStateProtector : IFortnoxOAuthStateProtector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector;

    public DataProtectionFortnoxOAuthStateProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("VirtualCompany.FortnoxOAuthState.v1");
    }

    public string Protect(FortnoxOAuthState state)
    {
        ValidateState(state);
        return _protector.Protect(JsonSerializer.Serialize(state, SerializerOptions));
    }

    public FortnoxOAuthState Unprotect(string protectedState)
    {
        if (string.IsNullOrWhiteSpace(protectedState))
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid.");
        }

        try
        {
            var json = _protector.Unprotect(protectedState);
            var state = JsonSerializer.Deserialize<FortnoxOAuthState>(json, SerializerOptions)
                ?? throw new InvalidOperationException("Fortnox OAuth state was invalid.");

            ValidateState(state);
            return state;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            throw new UnauthorizedAccessException("Fortnox OAuth state was invalid.", ex);
        }
    }

    private static void ValidateState(FortnoxOAuthState state)
    {
        if (state.CompanyId == Guid.Empty ||
            state.UserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(state.Nonce) ||
            state.IssuedUtc == default ||
            state.ExpiresUtc == default ||
            state.ExpiresUtc <= state.IssuedUtc)
        {
            throw new InvalidOperationException("Fortnox OAuth state was invalid.");
        }
    }
}
