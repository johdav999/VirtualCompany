using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Finance.Tests;

public sealed class BankStatementParserTests
{
    [Fact]
    public async Task Camt053_golden_file_preserves_balances_identities_dates_references_currency_and_totals()
    {
        await using var stream = File.OpenRead(Fixture("camt053-001-08.xml"));
        var parsed = await new Iso20022BankStatementParser().ParseAsync(Request("statement.camt.053.xml"), stream, default);
        Assert.Equal(BankStatementImportFormats.Camt053, parsed.Format);
        Assert.Equal("camt.053.001.08", parsed.MessageVersion);
        Assert.Equal("STATEMENT-2026-08-28", parsed.StatementIdentity);
        Assert.Equal("SEK", parsed.Currency); Assert.Equal(1000m, parsed.OpeningBalance); Assert.Equal(1200m, parsed.ClosingBalance);
        Assert.Empty(parsed.FileIssues); Assert.Collection(parsed.Rows,
            row => { Assert.Equal("TX-001", row.RowIdentity); Assert.Equal(250m, row.Amount); Assert.Equal("Invoice 1001", row.ReferenceText); Assert.Equal("Acme AB", row.Counterparty); Assert.Equal(new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc), row.BookingDateUtc); },
            row => { Assert.Equal("TX-002", row.RowIdentity); Assert.Equal(-50m, row.Amount); Assert.Equal("Bank fee", row.ReferenceText); Assert.Equal("Example Bank", row.Counterparty); });
    }

    [Fact]
    public async Task Pain002_is_status_only_and_never_claims_a_bank_transaction_import()
    {
        await using var stream = File.OpenRead(Fixture("pain002-001-10.xml"));
        var parsed = await new Iso20022BankStatementParser().ParseAsync(Request("payment.pain.002.xml"), stream, default);
        Assert.True(parsed.IsPaymentStatusMessage); Assert.Equal(BankStatementImportFormats.Pain002, parsed.Format);
        var row = Assert.Single(parsed.Rows); Assert.Equal("E2E-1", row.RowIdentity); Assert.Equal("ACSC", row.PaymentStatus); Assert.Null(row.BookingDateUtc);
    }

    [Fact]
    public async Task Unsupported_namespace_and_hostile_dtd_are_rejected_with_stable_safe_codes()
    {
        await using var unsupported = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.07\"/>"));
        var version = await Assert.ThrowsAsync<BankStatementImportOperationException>(() =>
            new Iso20022BankStatementParser().ParseAsync(Request("unsupported.xml"), unsupported, default));
        Assert.Equal(BankStatementImportReasonCodes.UnsupportedVersion, version.ReasonCode);
        await using var hostile = File.OpenRead(Fixture("hostile-dtd.xml"));
        var malformed = await Assert.ThrowsAsync<BankStatementImportOperationException>(() =>
            new Iso20022BankStatementParser().ParseAsync(Request("hostile.xml"), hostile, default));
        Assert.Equal(BankStatementImportReasonCodes.MalformedFile, malformed.ReasonCode);
        Assert.DoesNotContain("passwd", malformed.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Csv_profile_uses_swedish_locale_and_signed_amounts_without_formula_execution()
    {
        var profile = new BankStatementCsvMappingProfileDto(Guid.NewGuid(), "Swedish bank", 1, ';', "sv-SE", "yyyy-MM-dd", true,
            "Bokföringsdag", "Valutadag", "Belopp", null, null, "Valuta", "Referens", "Motpart", "Externt id", "Konto", "SEK", DateTime.UtcNow);
        await using var stream = File.OpenRead(Fixture("swedish-bank.csv"));
        var parsed = await new CsvBankStatementParser().ParseAsync(Request("swedish-bank.csv", profile), stream, default);
        Assert.Equal(1250.50m, parsed.Rows[0].Amount); Assert.Equal(-49.90m, parsed.Rows[1].Amount);
        Assert.All(parsed.Rows, row => Assert.Empty(row.Issues)); Assert.Equal("CSV-1", parsed.Rows[0].RowIdentity);
    }

    [Fact]
    public async Task Hostile_csv_rejects_unterminated_quotes_and_oversized_records_with_safe_errors()
    {
        var profile = new BankStatementCsvMappingProfileDto(Guid.NewGuid(), "Hostile input", 1, ';', "sv-SE", "yyyy-MM-dd", true,
            "Datum", null, "Belopp", null, null, "Valuta", "Referens", null, "Id", "Konto", "SEK", DateTime.UtcNow);
        var header = "Datum;Belopp;Valuta;Referens;Id;Konto\n";
        await using var unterminated = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            header + "2026-08-28;10,00;SEK;\"ofullständig;CSV-1;0003"));
        var quoteError = await Assert.ThrowsAsync<BankStatementImportOperationException>(() =>
            new CsvBankStatementParser().ParseAsync(Request("hostile.csv", profile), unterminated, default));
        Assert.Equal(BankStatementImportReasonCodes.MalformedFile, quoteError.ReasonCode);
        Assert.DoesNotContain("ofullständig", quoteError.SafeMessage, StringComparison.Ordinal);

        await using var oversized = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            header + "2026-08-28;10,00;SEK;" + new string('x', (64 * 1024) + 1) + ";CSV-2;0003"));
        var sizeError = await Assert.ThrowsAsync<BankStatementImportOperationException>(() =>
            new CsvBankStatementParser().ParseAsync(Request("oversized.csv", profile), oversized, default));
        Assert.Equal(BankStatementImportReasonCodes.MalformedFile, sizeError.ReasonCode);
        Assert.Contains("size limit", sizeError.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static BankStatementParseRequest Request(string file, BankStatementCsvMappingProfileDto? profile = null) =>
        new(file, file.EndsWith(".csv") ? "text/csv" : "application/xml", profile, "SEK", "•••• 0003", null);
    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "BankStatements", name);
}
