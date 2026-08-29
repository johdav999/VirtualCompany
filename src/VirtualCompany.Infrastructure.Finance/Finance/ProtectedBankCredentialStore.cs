using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class ProtectedBankCredentialStore : IProtectedBankCredentialStore
{
    private const string KeyId = "tenant-scoped-data-protection";
    private readonly VirtualCompanyDbContext _db;
    private readonly IFieldEncryptionService _encryption;
    public ProtectedBankCredentialStore(VirtualCompanyDbContext db, IFieldEncryptionService encryption) { _db = db; _encryption = encryption; }
    public async Task StoreAsync(Guid companyId, Guid connectionId, BankProviderCredentialBundle credentials, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(credentials);
        var encrypted = _encryption.Encrypt(companyId, Purpose(connectionId), json);
        var row = await _db.BankConnectionCredentials.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ConnectionId == connectionId, cancellationToken);
        if (row is null) _db.BankConnectionCredentials.Add(new BankConnectionCredential(Guid.NewGuid(), companyId, connectionId, encrypted, KeyId, credentials.ExpiresUtc, nowUtc));
        else row.Replace(encrypted, KeyId, credentials.ExpiresUtc, nowUtc);
    }
    public async Task<BankProviderCredentialBundle?> GetAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        var row = await _db.BankConnectionCredentials.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ConnectionId == connectionId, cancellationToken);
        if (row is null) return null;
        try { return JsonSerializer.Deserialize<BankProviderCredentialBundle>(_encryption.Decrypt(companyId, Purpose(connectionId), row.EncryptedEnvelope)); }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        { throw new BankConnectionOperationException(BankConnectionReasonCodes.MissingConsent, "Stored bank credentials can no longer be read. Renew bank consent to continue."); }
    }
    public async Task ClearAsync(Guid companyId, Guid connectionId, CancellationToken cancellationToken)
    {
        var row = await _db.BankConnectionCredentials.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ConnectionId == connectionId, cancellationToken);
        if (row is not null) _db.BankConnectionCredentials.Remove(row);
    }
    private static string Purpose(Guid connectionId) => $"bank-connection:{connectionId:D}:credential-envelope:v1";
}
