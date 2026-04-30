using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class DataProtectionMailboxOAuthStateProtector : IMailboxOAuthStateProtector
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector _protector;

    public DataProtectionMailboxOAuthStateProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector("VirtualCompany.MailboxOAuthState.v1");
    }

    public string Protect(MailboxOAuthState state)
    {
        if (state.CompanyId == Guid.Empty || state.UserId == Guid.Empty)
        {
            throw new ArgumentException("Mailbox OAuth state requires tenant and user identifiers.", nameof(state));
        }

        ValidateState(state);

        return _protector.Protect(JsonSerializer.Serialize(state, SerializerOptions));
    }

    public MailboxOAuthState Unprotect(string protectedState)
    {
        if (string.IsNullOrWhiteSpace(protectedState))
        {
            throw new UnauthorizedAccessException("Mailbox OAuth state was invalid.");
        }

        try
        {
            var json = _protector.Unprotect(protectedState);
            var state = JsonSerializer.Deserialize<MailboxOAuthState>(json, SerializerOptions)
                ?? throw new InvalidOperationException("Mailbox OAuth state was invalid.");

            ValidateState(state);
            return state;
        }
        catch (Exception ex) when (IsInvalidStateException(ex))
        {
            throw new UnauthorizedAccessException("Mailbox OAuth state was invalid.", ex);
        }
    }

    private static void ValidateState(MailboxOAuthState state)
    {
        if (state.CompanyId == Guid.Empty || state.UserId == Guid.Empty)
        {
            throw new InvalidOperationException("Mailbox OAuth state was invalid.");
        }

        MailboxProviderValues.EnsureSupported(state.Provider, nameof(state.Provider));

        if (state.ConfiguredFolders is null || state.ExpiresUtc == default)
        {
            throw new InvalidOperationException("Mailbox OAuth state was invalid.");
        }
    }

    private static bool IsInvalidStateException(Exception exception) =>
        exception is
            CryptographicException or
            JsonException or
            NotSupportedException or
            ArgumentException or
            InvalidOperationException;
}
