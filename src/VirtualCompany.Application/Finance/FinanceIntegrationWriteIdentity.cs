using System.Security.Cryptography;
using System.Text;

namespace VirtualCompany.Application.Finance;

public static class FinanceIntegrationWriteIdentity
{
    public static Guid CustomerInvoice(string action, Guid invoiceId, string? documentNumber)
    {
        var seed = string.IsNullOrWhiteSpace(documentNumber)
            ? $"fortnox-customer-invoice:{action}:{invoiceId:N}"
            : $"fortnox-customer-invoice:{action}:{invoiceId:N}:{documentNumber.Trim()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
