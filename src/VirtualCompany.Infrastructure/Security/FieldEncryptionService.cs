using Microsoft.AspNetCore.DataProtection;

namespace VirtualCompany.Infrastructure.Security;

public interface IFieldEncryptionService
{
    string Encrypt(Guid companyId, string purpose, string plaintext);
    string Decrypt(Guid companyId, string purpose, string ciphertext);
}

public sealed class DataProtectionFieldEncryptionService : IFieldEncryptionService
{
    private readonly IDataProtectionProvider _dataProtectionProvider;

    public DataProtectionFieldEncryptionService(IDataProtectionProvider dataProtectionProvider)
    {
        _dataProtectionProvider = dataProtectionProvider;
    }

    public string Encrypt(Guid companyId, string purpose, string plaintext)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("Encryption purpose is required.", nameof(purpose));
        }

        if (plaintext is null)
        {
            throw new ArgumentNullException(nameof(plaintext));
        }

        return CreateProtector(companyId, purpose).Protect(plaintext);
    }

    public string Decrypt(Guid companyId, string purpose, string ciphertext)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (string.IsNullOrWhiteSpace(purpose))
        {
            throw new ArgumentException("Encryption purpose is required.", nameof(purpose));
        }

        if (ciphertext is null)
        {
            throw new ArgumentNullException(nameof(ciphertext));
        }

        return CreateProtector(companyId, purpose).Unprotect(ciphertext);
    }

    private IDataProtector CreateProtector(Guid companyId, string purpose) =>
        _dataProtectionProvider.CreateProtector("VirtualCompany.FieldEncryption", companyId.ToString("D"), purpose.Trim());
}
