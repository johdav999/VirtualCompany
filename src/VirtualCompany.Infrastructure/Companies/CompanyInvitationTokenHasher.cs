using System.Security.Cryptography;
using System.Text;

namespace VirtualCompany.Infrastructure.Companies;

internal static class CompanyInvitationTokenHasher
{
    public static string ComputeHash(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Invitation token is required.", nameof(token));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token.Trim()));
        return Convert.ToHexString(bytes);
    }


}
