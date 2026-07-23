namespace VirtualCompany.Application.Security;

public interface IFieldEncryptionService
{
    string Encrypt(Guid companyId, string purpose, string plaintext);
    string Decrypt(Guid companyId, string purpose, string ciphertext);
}
