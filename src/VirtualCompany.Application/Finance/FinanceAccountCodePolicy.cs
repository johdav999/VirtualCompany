using System.Globalization;

namespace VirtualCompany.Application.Finance;

public static class FinanceAccountCodePolicy
{
    public const string DefaultSupplierExpenseAccount = "4010";

    public static bool IsSupplierExpenseAccount(string? accountCode)
    {
        var normalized = NormalizeAccountCode(accountCode);
        return normalized is not null &&
            int.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var accountNumber) &&
            accountNumber is >= 4000 and <= 8999;
    }

    public static string? NormalizeAccountCode(string? accountCode)
    {
        if (string.IsNullOrWhiteSpace(accountCode))
        {
            return null;
        }

        var trimmed = accountCode.Trim();
        return trimmed.All(char.IsDigit) ? trimmed : null;
    }
}
