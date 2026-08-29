using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CsvBankStatementParser : IBankStatementFileParser
{
    public const string CurrentParserVersion = "csv-profile-v1";
    private const int MaximumRecordCharacters = 64 * 1024;

    public bool Supports(string fileName, string? contentType) => fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(contentType, "text/csv", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(contentType, "application/csv", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedBankStatement> ParseAsync(BankStatementParseRequest request, Stream content,
        CancellationToken cancellationToken)
    {
        var profile = request.CsvProfile ?? throw new BankStatementImportOperationException(
            BankStatementImportReasonCodes.MissingMappingProfile, "Select a CSV mapping profile before previewing this file.");
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(profile.CultureName); }
        catch (CultureNotFoundException exception) { throw InvalidMapping("The mapping profile culture is invalid.", exception); }
        if (content.CanSeek) content.Position = 0;
        using var reader = new StreamReader(content, new UTF8Encoding(false, true), true, 81920, leaveOpen: true);
        var first = await ReadRecordAsync(reader, cancellationToken) ??
            throw new BankStatementImportOperationException(BankStatementImportReasonCodes.MalformedFile, "The CSV file is empty.");
        var firstFields = ParseFields(first, profile.Delimiter);
        var headers = profile.HasHeader ? firstFields : Enumerable.Range(1, firstFields.Count).Select(x => x.ToString(CultureInfo.InvariantCulture)).ToList();
        if (headers.Count != headers.Distinct(StringComparer.OrdinalIgnoreCase).Count()) throw InvalidMapping("The CSV header contains duplicate column names.");
        var indexes = ResolveIndexes(headers, profile);
        var rows = new List<ParsedBankStatementRow>();
        string? accountIdentifier = null;
        var rowNumber = 0;
        var record = profile.HasHeader ? await ReadRecordAsync(reader, cancellationToken) : first;
        while (record is not null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            var fields = ParseFields(record, profile.Delimiter);
            var issues = new List<BankStatementImportIssueDto>();
            string? Field(int? index) => index.HasValue && index.Value < fields.Count ? fields[index.Value].Trim() : null;
            var bookingText = Field(indexes.BookingDate);
            var booking = ParseDate(bookingText, profile.DateFormat, culture);
            if (booking is null) issues.Add(Issue("Booking date is missing or invalid.", rowNumber));
            var value = indexes.ValueDate.HasValue ? ParseDate(Field(indexes.ValueDate), profile.DateFormat, culture) : booking;
            var amount = ParseAmount(indexes, fields, culture, out var amountIssue);
            if (amountIssue is not null) issues.Add(Issue(amountIssue, rowNumber));
            var currency = (Field(indexes.Currency) ?? profile.DefaultCurrency ?? request.AccountCurrency).ToUpperInvariant();
            if (!string.Equals(currency, request.AccountCurrency, StringComparison.OrdinalIgnoreCase))
                issues.Add(new(BankStatementImportReasonCodes.CurrencyMismatch, BankStatementImportIssueSeverities.Error,
                    $"Row currency {currency} does not match account currency {request.AccountCurrency}.", rowNumber));
            var rowAccount = Field(indexes.AccountIdentifier);
            accountIdentifier ??= rowAccount;
            if (!AccountMatches(rowAccount, request.MaskedAccountNumber, request.ExternalAccountCode))
                issues.Add(new(BankStatementImportReasonCodes.AccountMismatch, BankStatementImportIssueSeverities.Error,
                    "The row account does not match the selected bank account.", rowNumber));
            var reference = Field(indexes.Reference) ?? string.Empty;
            var counterparty = Field(indexes.Counterparty) ?? string.Empty;
            var external = Field(indexes.ExternalReference);
            var identity = !string.IsNullOrWhiteSpace(external) ? external : Hash($"{booking:O}|{value:O}|{amount}|{currency}|{reference}|{counterparty}");
            rows.Add(new(rowNumber, Limit(identity, 128), booking, value, amount, currency,
                Limit(reference, 500), Limit(counterparty, 240), Limit(external, 160), null, issues));
            record = await ReadRecordAsync(reader, cancellationToken);
        }
        return new ParsedBankStatement(BankStatementImportFormats.Csv, $"profile-{profile.Version}", CurrentParserVersion,
            Hash($"{request.FileName}|{rows.Count}|{string.Join('|', rows.Select(x => x.RowIdentity))}")[..32], accountIdentifier,
            request.AccountCurrency, null, null, rows, [], false);
    }

    private static CsvIndexes ResolveIndexes(IReadOnlyList<string> headers, BankStatementCsvMappingProfileDto profile)
    {
        int Required(string name) => Index(name) ?? throw InvalidMapping($"Mapped column '{name}' was not found in the CSV file.");
        int? Index(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            for (var i = 0; i < headers.Count; i++) if (string.Equals(headers[i].Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
            return null;
        }
        return new(Required(profile.BookingDateColumn), Index(profile.ValueDateColumn), Index(profile.AmountColumn),
            Index(profile.DebitColumn), Index(profile.CreditColumn), Index(profile.CurrencyColumn),
            Required(profile.ReferenceColumn), Index(profile.CounterpartyColumn), Index(profile.ExternalReferenceColumn),
            Index(profile.AccountIdentifierColumn));
    }
    private static decimal? ParseAmount(CsvIndexes indexes, IReadOnlyList<string> fields, CultureInfo culture, out string? issue)
    {
        issue = null;
        string? Field(int? index) => index.HasValue && index.Value < fields.Count ? fields[index.Value].Trim() : null;
        const NumberStyles styles = NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowLeadingSign | NumberStyles.AllowParentheses;
        if (indexes.Amount.HasValue)
        {
            if (decimal.TryParse(Field(indexes.Amount), styles, culture, out var amount)) return Money(amount);
            issue = "Amount is missing or invalid."; return null;
        }
        var debitText = Field(indexes.Debit); var creditText = Field(indexes.Credit);
        var debit = string.IsNullOrWhiteSpace(debitText) ? 0m : decimal.TryParse(debitText, styles, culture, out var d) ? d : decimal.MinValue;
        var credit = string.IsNullOrWhiteSpace(creditText) ? 0m : decimal.TryParse(creditText, styles, culture, out var c) ? c : decimal.MinValue;
        if (debit == decimal.MinValue || credit == decimal.MinValue || debit != 0m && credit != 0m)
        { issue = "Debit and credit columns contain an invalid or ambiguous amount."; return null; }
        if (debit == 0m && credit == 0m) { issue = "Amount is missing or zero."; return null; }
        return Money(credit != 0m ? Math.Abs(credit) : -Math.Abs(debit));
    }
    private static DateTime? ParseDate(string? value, string format, CultureInfo culture)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParseExact(value, format, culture, DateTimeStyles.AllowWhiteSpaces, out var exact))
            return DateTime.SpecifyKind(exact.Date, DateTimeKind.Utc);
        return DateTime.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            ? DateTime.SpecifyKind(parsed.Date, DateTimeKind.Utc) : null;
    }
    private static async Task<string?> ReadRecordAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var quoted = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) return builder.Length == 0 ? null : quoted
                ? throw new BankStatementImportOperationException(BankStatementImportReasonCodes.MalformedFile, "The CSV file contains an unterminated quoted field.")
                : builder.ToString();
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(line);
            for (var i = 0; i < line.Length; i++) if (line[i] == '"')
            { if (quoted && i + 1 < line.Length && line[i + 1] == '"') i++; else quoted = !quoted; }
            if (builder.Length > MaximumRecordCharacters)
                throw new BankStatementImportOperationException(BankStatementImportReasonCodes.MalformedFile, "A CSV record exceeds the supported size limit.");
            if (!quoted) return builder.ToString();
        }
    }
    private static List<string> ParseFields(string record, char delimiter)
    {
        var fields = new List<string>(); var builder = new StringBuilder(); var quoted = false;
        for (var i = 0; i < record.Length; i++)
        {
            var ch = record[i];
            if (ch == '"') { if (quoted && i + 1 < record.Length && record[i + 1] == '"') { builder.Append('"'); i++; } else quoted = !quoted; }
            else if (ch == delimiter && !quoted) { fields.Add(builder.ToString()); builder.Clear(); }
            else builder.Append(ch);
        }
        if (quoted) throw new BankStatementImportOperationException(BankStatementImportReasonCodes.MalformedFile, "The CSV file contains an unterminated quoted field.");
        fields.Add(builder.ToString()); return fields;
    }
    private static bool AccountMatches(string? source, string masked, string? external)
    {
        if (string.IsNullOrWhiteSpace(source)) return true;
        static string Alnum(string? value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var normalized = Alnum(source); var ext = Alnum(external); var digits = new string(masked.Where(char.IsDigit).ToArray());
        return ext.Length > 0 && normalized == ext || digits.Length >= 4 && normalized.EndsWith(digits[^4..], StringComparison.Ordinal);
    }
    private static BankStatementImportIssueDto Issue(string message, int row) => new(BankStatementImportReasonCodes.RowInvalid,
        BankStatementImportIssueSeverities.Error, message, row);
    private static BankStatementImportOperationException InvalidMapping(string message, Exception? inner = null) =>
        new(BankStatementImportReasonCodes.InvalidMapping, message, false, inner);
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private sealed record CsvIndexes(int BookingDate, int? ValueDate, int? Amount, int? Debit, int? Credit,
        int? Currency, int Reference, int? Counterparty, int? ExternalReference, int? AccountIdentifier);
}
