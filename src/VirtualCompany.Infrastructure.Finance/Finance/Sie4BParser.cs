using System.Globalization;
using System.Text;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed record ParsedSie4BTransaction(string AccountCode, decimal Amount);
internal sealed record ParsedSie4BVoucher(string Series, long Number, DateOnly Date, IReadOnlyList<ParsedSie4BTransaction> Transactions);
internal sealed record ParsedSie4BFile(string CompanyName, string OrganisationNumber, DateOnly FinancialYearStart,
    DateOnly FinancialYearEnd, IReadOnlyDictionary<string, string> Accounts, IReadOnlyList<ParsedSie4BVoucher> Vouchers,
    IReadOnlyDictionary<string, decimal> OpeningBalances, IReadOnlyDictionary<string, decimal> ClosingBalances);

internal sealed class Sie4BParser
{
    private static readonly Encoding Pc8 = CreateEncoding();

    public ParsedSie4BFile Parse(byte[] content)
    {
        var lines = Pc8.GetString(content).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0 || !lines[0].StartsWith("#FLAGGA ", StringComparison.Ordinal) ||
            !lines.Contains("#FORMAT PC8") || !lines.Contains("#SIETYP 4"))
            throw new FormatException("The content is not a supported SIE 4B export file.");

        var accounts = new Dictionary<string, string>(StringComparer.Ordinal);
        var opening = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var closing = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var vouchers = new List<ParsedSie4BVoucher>();
        var currentTransactions = new List<ParsedSie4BTransaction>();
        string? currentSeries = null;
        long currentNumber = 0;
        DateOnly currentDate = default;
        string? name = null;
        string? organisation = null;
        DateOnly start = default;
        DateOnly end = default;

        foreach (var line in lines)
        {
            var fields = Tokenize(line);
            if (fields.Count == 0) continue;
            switch (fields[0])
            {
                case "#FNAMN": name = fields.ElementAtOrDefault(1); break;
                case "#ORGNR": organisation = fields.ElementAtOrDefault(1); break;
                case "#RAR" when fields.Count >= 4 && fields[1] == "0":
                    start = DateOnly.ParseExact(fields[2], "yyyyMMdd", CultureInfo.InvariantCulture);
                    end = DateOnly.ParseExact(fields[3], "yyyyMMdd", CultureInfo.InvariantCulture);
                    break;
                case "#KONTO" when fields.Count >= 3: accounts.Add(fields[1], fields[2]); break;
                case "#IB" when fields.Count >= 4 && fields[1] == "0": opening[fields[2]] = Decimal(fields[3]); break;
                case "#UB" when fields.Count >= 4 && fields[1] == "0": closing[fields[2]] = Decimal(fields[3]); break;
                case "#RES" when fields.Count >= 4 && fields[1] == "0": closing[fields[2]] = Decimal(fields[3]); break;
                case "#VER" when fields.Count >= 4:
                    if (currentSeries is not null) throw new FormatException("Nested SIE vouchers are invalid.");
                    currentSeries = fields[1];
                    currentNumber = long.Parse(fields[2], CultureInfo.InvariantCulture);
                    currentDate = DateOnly.ParseExact(fields[3], "yyyyMMdd", CultureInfo.InvariantCulture);
                    currentTransactions = [];
                    break;
                case "#TRANS" when currentSeries is not null:
                    var amountIndex = fields.FindIndex(2, field => field == "}" || field == "{}");
                    if (amountIndex < 0 || amountIndex + 1 >= fields.Count) throw new FormatException("SIE transaction object list is malformed.");
                    currentTransactions.Add(new ParsedSie4BTransaction(fields[1], Decimal(fields[amountIndex + 1])));
                    break;
                case "}" when currentSeries is not null:
                    vouchers.Add(new ParsedSie4BVoucher(currentSeries, currentNumber, currentDate, currentTransactions.ToArray()));
                    currentSeries = null;
                    break;
            }
        }

        if (name is null || organisation is null || start == default || end == default || currentSeries is not null)
            throw new FormatException("Required SIE 4B identification or voucher data is missing.");
        foreach (var voucher in vouchers)
            if (decimal.Round(voucher.Transactions.Sum(x => x.Amount), 2) != 0m)
                throw new FormatException("A parsed SIE voucher is not balanced.");
        return new ParsedSie4BFile(name, organisation, start, end, accounts, vouchers, opening, closing);
    }

    private static List<string> Tokenize(string line)
    {
        if (line == "{" || line == "}") return [line];
        var result = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        var escaped = false;
        foreach (var character in line)
        {
            if (escaped) { value.Append(character); escaped = false; continue; }
            if (character == '\\' && quoted) { escaped = true; continue; }
            if (character == '"') { quoted = !quoted; continue; }
            if (!quoted && char.IsWhiteSpace(character))
            {
                if (value.Length > 0) { result.Add(value.ToString()); value.Clear(); }
                continue;
            }
            if (!quoted && character is '{' or '}')
            {
                if (value.Length > 0) { result.Add(value.ToString()); value.Clear(); }
                result.Add(character.ToString());
                continue;
            }
            value.Append(character);
        }
        if (quoted || escaped) throw new FormatException("SIE field quoting is malformed.");
        if (value.Length > 0) result.Add(value.ToString());
        return result;
    }

    private static decimal Decimal(string value) => decimal.Parse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    private static Encoding CreateEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }
}
