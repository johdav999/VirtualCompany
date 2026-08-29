using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class DataProtectionBankConsentStateProtector : IBankConsentStateProtector
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector;
    public DataProtectionBankConsentStateProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("VirtualCompany.BankConsent.CallbackState.v1");
    public string Protect(BankConsentCallbackState state)
    {
        Validate(state);
        return _protector.Protect(JsonSerializer.Serialize(state, JsonOptions));
    }
    public BankConsentCallbackState Unprotect(string protectedState)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(protectedState)) throw new InvalidOperationException();
            var state = JsonSerializer.Deserialize<BankConsentCallbackState>(_protector.Unprotect(protectedState), JsonOptions)
                ?? throw new InvalidOperationException();
            Validate(state);
            return state;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or InvalidOperationException or ArgumentException)
        {
            throw new BankConnectionOperationException("callback_state_invalid", "Bank authorization state was invalid or expired.", true);
        }
    }
    private static void Validate(BankConsentCallbackState state)
    {
        if (state.SessionId == Guid.Empty || state.CompanyId == Guid.Empty || state.UserId == Guid.Empty ||
            string.IsNullOrWhiteSpace(state.ProviderKey) || string.IsNullOrWhiteSpace(state.Nonce) || state.ExpiresUtc <= state.IssuedUtc)
            throw new InvalidOperationException();
    }
}
