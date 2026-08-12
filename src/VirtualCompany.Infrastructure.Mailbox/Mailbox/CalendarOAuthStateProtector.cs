using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Application.Mailbox;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class DataProtectionCalendarOAuthStateProtector : ICalendarOAuthStateProtector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector;

    public DataProtectionCalendarOAuthStateProtector(IDataProtectionProvider provider) =>
        _protector = provider.CreateProtector("VirtualCompany.CalendarOAuthState.v1");

    public string Protect(CalendarOAuthState state)
    {
        Validate(state);
        return _protector.Protect(JsonSerializer.Serialize(state, SerializerOptions));
    }

    public CalendarOAuthState Unprotect(string protectedState)
    {
        if (string.IsNullOrWhiteSpace(protectedState))
            throw new UnauthorizedAccessException("Calendar OAuth state was invalid.");
        try
        {
            var state = JsonSerializer.Deserialize<CalendarOAuthState>(
                _protector.Unprotect(protectedState), SerializerOptions)
                ?? throw new InvalidOperationException("Calendar OAuth state was invalid.");
            Validate(state);
            return state;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or ArgumentException or InvalidOperationException)
        {
            throw new UnauthorizedAccessException("Calendar OAuth state was invalid.", ex);
        }
    }

    private static void Validate(CalendarOAuthState state)
    {
        if (state.CompanyId == Guid.Empty || state.UserId == Guid.Empty ||
            state.ExpiresUtc == default || string.IsNullOrWhiteSpace(state.Nonce) ||
            state.RequestedScopes is null || state.RequestedScopes.Count == 0 ||
            !Enum.IsDefined(state.Provider))
            throw new InvalidOperationException("Calendar OAuth state was invalid.");
    }
}
