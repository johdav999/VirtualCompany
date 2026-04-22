using System.Security.Cryptography;
using System.Text;

namespace VirtualCompany.Infrastructure.Finance;

public static class FinanceSimulationDeterministicIdentity
{
    public static Guid CreateScopedGuid(Guid companyId, string scope)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (string.IsNullOrWhiteSpace(scope))
        {
            throw new ArgumentException("Scope is required.", nameof(scope));
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{companyId:N}:{scope.Trim()}")));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    public static Guid CreateCashDeltaRecordId(Guid companyId, Guid simulationEventRecordId) =>
        CreateScopedGuid(companyId, $"simulation-cash-delta:{simulationEventRecordId:N}");
}
