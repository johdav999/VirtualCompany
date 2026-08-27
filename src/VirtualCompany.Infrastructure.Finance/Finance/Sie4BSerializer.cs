using System.Globalization;
using System.Text;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed record Sie4BCompany(
    string LegalName,
    string OrganisationNumber,
    string AddressLine,
    string PostalAddress,
    string CountryCode,
    string Currency);

internal sealed record Sie4BDimension(int Number, string Name, IReadOnlyList<Sie4BObject> Objects);
internal sealed record Sie4BObject(string Code, string Name);

internal sealed record Sie4BAccount(
    string Code,
    string Name,
    string AccountClass,
    decimal OpeningBalance,
    decimal ClosingBalance,
    IReadOnlyDictionary<string, decimal> PeriodMovements);

internal sealed record Sie4BTransaction(
    string AccountCode,
    decimal Amount,
    DateOnly Date,
    string? Text,
    IReadOnlyDictionary<int, string> Objects);

internal sealed record Sie4BVoucher(
    string Series,
    long Number,
    DateOnly Date,
    string? Text,
    DateOnly RegistrationDate,
    IReadOnlyList<Sie4BTransaction> Transactions);

internal sealed record Sie4BSource(
    Sie4BCompany Company,
    DateOnly FinancialYearStart,
    DateOnly FinancialYearEnd,
    DateOnly GeneratedDate,
    IReadOnlyList<Sie4BDimension> Dimensions,
    IReadOnlyList<Sie4BAccount> Accounts,
    IReadOnlyList<Sie4BVoucher> Vouchers);

internal sealed record Sie4BSerializationResult(byte[] Content, int AccountCount, int VoucherCount,
    int TransactionCount, decimal DebitTotal, decimal CreditTotal);

internal sealed class Sie4BValidationException(string reasonCode, string message) : AccountingExportException(reasonCode, message)
{
}

internal static class Sie4BReasonCodes
{
    public const string MissingStatutoryIdentity = "sie_missing_statutory_identity";
    public const string UnsupportedCurrency = "sie_unsupported_currency";
    public const string UnsupportedDimension = "sie_unsupported_dimension";
    public const string InvalidAccount = "sie_invalid_account";
    public const string MissingVoucherIdentity = "sie_missing_voucher_identity";
    public const string UnbalancedVoucher = "sie_unbalanced_voucher";
    public const string UnsupportedPrecision = "sie_unsupported_precision";
    public const string UnsupportedCharacter = "sie_unsupported_character";
    public const string IncompletePolicyHistory = "sie_incomplete_policy_history";
    public const string IncompletePeriod = "sie_incomplete_period";
}

internal sealed class Sie4BSerializer
{
    public const string SpecificationVersion = "SIE 4B 2008-09-30";
    public const string EncodingName = "IBM PC8 (code page 437)";

    private static readonly Encoding Pc8 = CreateEncoding();

    public Sie4BSerializationResult Serialize(Sie4BSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Validate(source);

        var rows = new List<string>
        {
            "#FLAGGA 0",
            $"#PROGRAM {Quoted("Virtual Company")} {Quoted("1")}",
            "#FORMAT PC8",
            $"#GEN {Date(source.GeneratedDate)} {Quoted("accounting-export-worker")}",
            "#SIETYP 4",
            $"#ORGNR {FormatOrganisationNumber(source.Company.OrganisationNumber)}",
            $"#ADRESS {Quoted(string.Empty)} {Quoted(source.Company.AddressLine)} {Quoted(source.Company.PostalAddress)} {Quoted(string.Empty)}",
            $"#FNAMN {Quoted(source.Company.LegalName)}",
            $"#RAR 0 {Date(source.FinancialYearStart)} {Date(source.FinancialYearEnd)}",
            "#KPTYP EUBAS97",
            $"#VALUTA {source.Company.Currency}"
        };

        foreach (var dimension in source.Dimensions.OrderBy(x => x.Number))
        {
            rows.Add($"#DIM {dimension.Number} {Quoted(dimension.Name)}");
            foreach (var item in dimension.Objects.OrderBy(x => x.Code, StringComparer.Ordinal))
                rows.Add($"#OBJEKT {dimension.Number} {Quoted(item.Code)} {Quoted(item.Name)}");
        }

        foreach (var account in source.Accounts.OrderBy(x => NumericAccount(x.Code)).ThenBy(x => x.Code, StringComparer.Ordinal))
        {
            rows.Add($"#KONTO {account.Code} {Quoted(account.Name)}");
            rows.Add($"#KTYP {account.Code} {AccountType(account.AccountClass)}");
        }

        foreach (var account in source.Accounts.OrderBy(x => NumericAccount(x.Code)).ThenBy(x => x.Code, StringComparer.Ordinal))
        {
            if (IsBalanceSheet(account.AccountClass))
            {
                if (account.OpeningBalance != 0m) rows.Add($"#IB 0 {account.Code} {Amount(account.OpeningBalance)}");
                if (account.ClosingBalance != 0m) rows.Add($"#UB 0 {account.Code} {Amount(account.ClosingBalance)}");
            }
            else if (account.ClosingBalance != 0m)
            {
                rows.Add($"#RES 0 {account.Code} {Amount(account.ClosingBalance)}");
            }

            foreach (var movement in account.PeriodMovements.OrderBy(x => x.Key, StringComparer.Ordinal))
                if (movement.Value != 0m)
                    rows.Add($"#PSALDO 0 {movement.Key} {account.Code} {{}} {Amount(movement.Value)}");
        }

        foreach (var voucher in source.Vouchers
                     .OrderBy(x => x.Series, StringComparer.Ordinal)
                     .ThenBy(x => x.Number))
        {
            rows.Add($"#VER {Quoted(voucher.Series)} {voucher.Number.ToString(CultureInfo.InvariantCulture)} {Date(voucher.Date)} {Quoted(voucher.Text ?? string.Empty)} {Date(voucher.RegistrationDate)} {Quoted("accounting-export-worker")}");
            rows.Add("{");
            foreach (var transaction in voucher.Transactions)
            {
                var objects = transaction.Objects.Count == 0
                    ? "{}"
                    : "{" + string.Join(' ', transaction.Objects.OrderBy(x => x.Key)
                        .SelectMany(x => new[] { x.Key.ToString(CultureInfo.InvariantCulture), Quoted(x.Value) })) + "}";
                rows.Add($"#TRANS {transaction.AccountCode} {objects} {Amount(transaction.Amount)} {Date(transaction.Date)} {Quoted(transaction.Text ?? string.Empty)}");
            }
            rows.Add("}");
        }

        byte[] content;
        try
        {
            content = Pc8.GetBytes(string.Join('\n', rows) + "\n");
        }
        catch (EncoderFallbackException exception)
        {
            throw new Sie4BValidationException(Sie4BReasonCodes.UnsupportedCharacter,
                $"Export text contains a character that cannot be represented in SIE 4B PC8 encoding: {exception.Message}");
        }
        return new Sie4BSerializationResult(content, source.Accounts.Count, source.Vouchers.Count,
            source.Vouchers.Sum(x => x.Transactions.Count),
            source.Vouchers.SelectMany(x => x.Transactions).Where(x => x.Amount > 0m).Sum(x => x.Amount),
            -source.Vouchers.SelectMany(x => x.Transactions).Where(x => x.Amount < 0m).Sum(x => x.Amount));
    }

    private static void Validate(Sie4BSource source)
    {
        if (string.IsNullOrWhiteSpace(source.Company.LegalName) || string.IsNullOrWhiteSpace(source.Company.OrganisationNumber) ||
            string.IsNullOrWhiteSpace(source.Company.AddressLine) || string.IsNullOrWhiteSpace(source.Company.PostalAddress))
            throw new Sie4BValidationException(Sie4BReasonCodes.MissingStatutoryIdentity,
                "A complete statutory legal name, organisation number, and registered address are required for SIE export.");
        if (!string.Equals(source.Company.CountryCode, "SE", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(source.Company.Currency, "SEK", StringComparison.OrdinalIgnoreCase))
            throw new Sie4BValidationException(Sie4BReasonCodes.UnsupportedCurrency,
                "SIE 4B statutory export currently supports Swedish books in SEK only.");
        if (source.FinancialYearEnd < source.FinancialYearStart)
            throw new Sie4BValidationException(Sie4BReasonCodes.IncompletePeriod, "The exported financial year boundaries are invalid.");

        foreach (var dimension in source.Dimensions)
        {
            if (dimension.Number is not (1 or 6))
                throw new Sie4BValidationException(Sie4BReasonCodes.UnsupportedDimension,
                    $"Accounting dimension {dimension.Number} cannot be represented by the supported SIE 4B dimension mapping.");
        }

        var accountCodes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var account in source.Accounts)
        {
            if (account.Code.Length == 0 || account.Code.Any(character => character is < '0' or > '9') || !accountCodes.Add(account.Code))
                throw new Sie4BValidationException(Sie4BReasonCodes.InvalidAccount,
                    "Every exported account must have one unique numeric SIE account number.");
            AssertPrecision(account.OpeningBalance);
            AssertPrecision(account.ClosingBalance);
            foreach (var movement in account.PeriodMovements.Values) AssertPrecision(movement);
        }

        foreach (var voucher in source.Vouchers)
        {
            if (string.IsNullOrWhiteSpace(voucher.Series) || voucher.Number <= 0)
                throw new Sie4BValidationException(Sie4BReasonCodes.MissingVoucherIdentity,
                    "Every SIE voucher requires a durable series and positive sequence number.");
            if (voucher.Transactions.Count < 2 || voucher.Transactions.Any(x => !accountCodes.Contains(x.AccountCode)) ||
                decimal.Round(voucher.Transactions.Sum(x => x.Amount), 2, MidpointRounding.ToEven) != 0m)
                throw new Sie4BValidationException(Sie4BReasonCodes.UnbalancedVoucher,
                    "Every SIE voucher must contain balanced transactions mapped to exported accounts.");
            foreach (var transaction in voucher.Transactions) AssertPrecision(transaction.Amount);
        }

    }

    private static void AssertPrecision(decimal value)
    {
        if (decimal.Round(value, 2, MidpointRounding.ToEven) != value)
            throw new Sie4BValidationException(Sie4BReasonCodes.UnsupportedPrecision,
                "SIE 4B supports at most two decimal places; the source ledger must be corrected without export-time rounding.");
    }

    private static string Quoted(string value) => $"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    private static string Date(DateOnly value) => value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    private static string Amount(decimal value) => value.ToString(value == decimal.Truncate(value) ? "0" : "0.00", CultureInfo.InvariantCulture);
    private static long NumericAccount(string code) => long.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : long.MaxValue;
    private static bool IsBalanceSheet(string accountClass) => accountClass is FinanceAccountClassValues.Asset or FinanceAccountClassValues.Liability or FinanceAccountClassValues.Equity;
    private static string AccountType(string accountClass) => accountClass switch
    {
        FinanceAccountClassValues.Asset => "T",
        FinanceAccountClassValues.Liability or FinanceAccountClassValues.Equity => "S",
        FinanceAccountClassValues.Expense => "K",
        FinanceAccountClassValues.Income => "I",
        _ => throw new Sie4BValidationException(Sie4BReasonCodes.InvalidAccount, $"Account class '{accountClass}' has no SIE 4B mapping.")
    };

    private static string FormatOrganisationNumber(string value)
    {
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 10 ? $"{digits[..6]}-{digits[6..]}" : value;
    }

    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
