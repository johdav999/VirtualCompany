using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class Iso20022BankStatementParser : IBankStatementFileParser
{
    public const string CurrentParserVersion = "iso20022-v1";
    private const string Prefix = "urn:iso:std:iso:20022:tech:xsd:";
    private static readonly IReadOnlyDictionary<string, (string Format, string Container)> SupportedNamespaces =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            [$"{Prefix}camt.052.001.02"] = (BankStatementImportFormats.Camt052, "Rpt"),
            [$"{Prefix}camt.052.001.08"] = (BankStatementImportFormats.Camt052, "Rpt"),
            [$"{Prefix}camt.053.001.02"] = (BankStatementImportFormats.Camt053, "Stmt"),
            [$"{Prefix}camt.053.001.08"] = (BankStatementImportFormats.Camt053, "Stmt"),
            [$"{Prefix}camt.054.001.02"] = (BankStatementImportFormats.Camt054, "Ntfctn"),
            [$"{Prefix}camt.054.001.08"] = (BankStatementImportFormats.Camt054, "Ntfctn"),
            [$"{Prefix}pain.002.001.03"] = (BankStatementImportFormats.Pain002, "CstmrPmtStsRpt"),
            [$"{Prefix}pain.002.001.10"] = (BankStatementImportFormats.Pain002, "CstmrPmtStsRpt")
        };

    public bool Supports(string fileName, string? contentType) =>
        fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        fileName.Contains("camt.", StringComparison.OrdinalIgnoreCase) ||
        fileName.Contains("pain.002", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(contentType, "application/xml", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(contentType, "text/xml", StringComparison.OrdinalIgnoreCase);

    public async Task<ParsedBankStatement> ParseAsync(BankStatementParseRequest request, Stream content,
        CancellationToken cancellationToken)
    {
        if (content.CanSeek) content.Position = 0;
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            MaxCharactersInDocument = 25 * 1024 * 1024
        };
        try
        {
            using var reader = XmlReader.Create(content, settings);
            await reader.MoveToContentAsync();
            if (!string.Equals(reader.LocalName, "Document", StringComparison.Ordinal))
                throw Malformed("The XML root element must be ISO 20022 Document.");
            var ns = reader.NamespaceURI;
            if (!SupportedNamespaces.TryGetValue(ns, out var descriptor))
            {
                if (ns.StartsWith(Prefix, StringComparison.Ordinal))
                    throw new BankStatementImportOperationException(BankStatementImportReasonCodes.UnsupportedVersion,
                        "This ISO 20022 message version is not supported. Supported CAMT versions are .001.02 and .001.08; supported PAIN.002 versions are .001.03 and .001.10.");
                throw new BankStatementImportOperationException(BankStatementImportReasonCodes.UnsupportedFormat,
                    "The uploaded XML is not a supported ISO 20022 statement or payment-status message.");
            }
            return descriptor.Format == BankStatementImportFormats.Pain002
                ? await ParsePainAsync(reader, ns, cancellationToken)
                : await ParseCamtAsync(reader, ns, descriptor, request, cancellationToken);
        }
        catch (BankStatementImportOperationException) { throw; }
        catch (XmlException exception)
        {
            throw Malformed("The XML file is malformed or contains prohibited content.", exception);
        }
    }

    private static async Task<ParsedBankStatement> ParseCamtAsync(XmlReader reader, string ns,
        (string Format, string Container) descriptor, BankStatementParseRequest request,
        CancellationToken cancellationToken)
    {
        var rows = new List<ParsedBankStatementRow>();
        var issues = new List<BankStatementImportIssueDto>();
        string? statementIdentity = null;
        string? accountIdentifier = null;
        string? currency = null;
        decimal? opening = null;
        decimal? closing = null;
        var rowNumber = 0;
        var containerDepth = -1;
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.LocalName == descriptor.Container) { containerDepth = reader.Depth; continue; }
            if (containerDepth < 0) continue;
            if (reader.LocalName == "Id" && reader.Depth == containerDepth + 1 && statementIdentity is null)
            {
                statementIdentity = await reader.ReadElementContentAsStringAsync();
                continue;
            }
            if (reader.LocalName == "Acct" && reader.Depth == containerDepth + 1)
            {
                var element = await ReadElementAsync(reader, cancellationToken);
                accountIdentifier = FirstValue(element, "IBAN") ??
                    element.Descendants().FirstOrDefault(x => x.Name.LocalName == "Othr")?.Descendants()
                        .FirstOrDefault(x => x.Name.LocalName == "Id")?.Value;
                currency = FirstValue(element, "Ccy")?.Trim().ToUpperInvariant();
                continue;
            }
            if (reader.LocalName == "Bal" && reader.Depth == containerDepth + 1)
            {
                var element = await ReadElementAsync(reader, cancellationToken);
                var code = element.Descendants().FirstOrDefault(x => x.Name.LocalName == "Cd")?.Value;
                var amountElement = element.Descendants().FirstOrDefault(x => x.Name.LocalName == "Amt");
                if (amountElement is not null && TryDecimal(amountElement.Value, out var balance))
                {
                    var signed = Sign(balance, FirstValue(element, "CdtDbtInd"));
                    if (code is "OPBD" or "PRCD") opening ??= signed;
                    if (code is "CLBD" or "ITBD") closing = signed;
                }
                continue;
            }
            if (reader.LocalName == "Ntry")
            {
                var entry = await ReadElementAsync(reader, cancellationToken);
                foreach (var parsed in ParseEntry(entry, request, ref rowNumber)) rows.Add(parsed);
            }
        }
        if (string.IsNullOrWhiteSpace(statementIdentity))
            issues.Add(new(BankStatementImportReasonCodes.RowInvalid, BankStatementImportIssueSeverities.Error,
                "The statement has no statement identifier."));
        if (!AccountMatches(accountIdentifier, request.MaskedAccountNumber, request.ExternalAccountCode))
            issues.Add(new(BankStatementImportReasonCodes.AccountMismatch, BankStatementImportIssueSeverities.Error,
                "The account in the file does not match the selected bank account."));
        if (!string.IsNullOrWhiteSpace(currency) && !string.Equals(currency, request.AccountCurrency, StringComparison.OrdinalIgnoreCase))
            issues.Add(new(BankStatementImportReasonCodes.CurrencyMismatch, BankStatementImportIssueSeverities.Error,
                $"The statement currency {currency} does not match the selected account currency {request.AccountCurrency}."));
        if (opening.HasValue && closing.HasValue)
        {
            var calculated = Money(opening.Value + rows.Where(x => x.Amount > 0).Sum(x => x.Amount!.Value) +
                rows.Where(x => x.Amount < 0).Sum(x => x.Amount!.Value));
            if (calculated != Money(closing.Value))
                issues.Add(new(BankStatementImportReasonCodes.ControlTotalMismatch, BankStatementImportIssueSeverities.Error,
                    "Opening balance plus statement transactions does not equal the closing balance."));
        }
        var version = ns[Prefix.Length..];
        return new ParsedBankStatement(descriptor.Format, version, CurrentParserVersion,
            Limit(statementIdentity ?? Sha256(ns), 128)!, accountIdentifier, currency ?? request.AccountCurrency,
            opening, closing, rows, issues, false);
    }

    private static IReadOnlyList<ParsedBankStatementRow> ParseEntry(XElement entry, BankStatementParseRequest request,
        ref int rowNumber)
    {
        var parsedRows = new List<ParsedBankStatementRow>();
        var entryAmountElement = entry.Elements().FirstOrDefault(x => x.Name.LocalName == "Amt");
        var amountCurrency = entryAmountElement?.Attribute("Ccy")?.Value?.Trim().ToUpperInvariant();
        var hasEntryAmount = TryDecimal(entryAmountElement?.Value, out var entryAmount);
        var signedEntryAmount = hasEntryAmount ? Sign(entryAmount, FirstDirectValue(entry, "CdtDbtInd")) : (decimal?)null;
        var bookingDate = ParseDate(FirstDate(entry, "BookgDt"));
        var valueDate = ParseDate(FirstDate(entry, "ValDt")) ?? bookingDate;
        var details = entry.Descendants().Where(x => x.Name.LocalName == "TxDtls").ToArray();
        if (details.Length == 0) details = [entry];
        foreach (var detail in details)
        {
            rowNumber++;
            var rowIssues = new List<BankStatementImportIssueDto>();
            decimal? amount = signedEntryAmount;
            var detailAmountElement = detail.Descendants().FirstOrDefault(x => x.Name.LocalName == "TxAmt")?
                .Descendants().FirstOrDefault(x => x.Name.LocalName == "Amt");
            if (detailAmountElement is not null && TryDecimal(detailAmountElement.Value, out var detailAmount))
            {
                amount = Sign(detailAmount, FirstValue(detail, "CdtDbtInd") ?? FirstDirectValue(entry, "CdtDbtInd"));
                amountCurrency = detailAmountElement.Attribute("Ccy")?.Value?.Trim().ToUpperInvariant() ?? amountCurrency;
            }
            else if (details.Length > 1)
            {
                amount = null;
                rowIssues.Add(new(BankStatementImportReasonCodes.RowInvalid, BankStatementImportIssueSeverities.Error,
                    "A grouped entry contains multiple transactions without individual amounts.", rowNumber));
            }
            var identity = FirstDirectValue(entry, "AcctSvcrRef") ?? FirstValue(detail, "AcctSvcrRef") ?? FirstValue(detail, "TxId") ??
                FirstValue(detail, "EndToEndId") ?? FirstDirectValue(entry, "NtryRef");
            var externalReference = FirstValue(detail, "EndToEndId") ?? FirstValue(detail, "InstrId") ?? identity;
            var reference = string.Join(" ", detail.Descendants().Where(x => x.Name.LocalName == "Ustrd")
                .Select(x => x.Value.Trim()).Where(x => x.Length > 0));
            if (string.IsNullOrWhiteSpace(reference)) reference = externalReference ?? FirstDirectValue(entry, "AddtlNtryInf") ?? string.Empty;
            var counterparty = ResolveCounterparty(detail, amount);
            if (bookingDate is null) rowIssues.Add(new(BankStatementImportReasonCodes.RowInvalid,
                BankStatementImportIssueSeverities.Error, "Booking date is missing or invalid.", rowNumber));
            if (amount is null) rowIssues.Add(new(BankStatementImportReasonCodes.RowInvalid,
                BankStatementImportIssueSeverities.Error, "Amount is missing or invalid.", rowNumber));
            var rowCurrency = amountCurrency ?? request.AccountCurrency;
            if (!string.Equals(rowCurrency, request.AccountCurrency, StringComparison.OrdinalIgnoreCase))
                rowIssues.Add(new(BankStatementImportReasonCodes.CurrencyMismatch, BankStatementImportIssueSeverities.Error,
                    $"Row currency {rowCurrency} does not match account currency {request.AccountCurrency}.", rowNumber));
            identity = string.IsNullOrWhiteSpace(identity)
                ? Sha256($"{bookingDate:O}|{valueDate:O}|{amount}|{rowCurrency}|{reference}|{counterparty}")
                : identity.Trim();
            parsedRows.Add(new ParsedBankStatementRow(rowNumber, Limit(identity, 128)!, bookingDate, valueDate,
                amount.HasValue ? Money(amount.Value) : null, rowCurrency, Limit(reference, 500),
                Limit(counterparty, 240), Limit(externalReference, 160), null, rowIssues));
        }
        return parsedRows;
    }

    private static async Task<ParsedBankStatement> ParsePainAsync(XmlReader reader, string ns,
        CancellationToken cancellationToken)
    {
        var rows = new List<ParsedBankStatementRow>();
        string? messageId = null;
        var rowNumber = 0;
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType != XmlNodeType.Element) continue;
            if (reader.LocalName == "MsgId" && messageId is null)
            { messageId = await reader.ReadElementContentAsStringAsync(); continue; }
            if (reader.LocalName != "TxInfAndSts") continue;
            var status = await ReadElementAsync(reader, cancellationToken);
            rowNumber++;
            var identity = FirstValue(status, "OrgnlEndToEndId") ?? FirstValue(status, "OrgnlInstrId") ??
                FirstValue(status, "StsId") ?? $"status-{rowNumber}";
            var paymentStatus = FirstValue(status, "TxSts") ?? "UNKNOWN";
            var amountElement = status.Descendants().FirstOrDefault(x => x.Name.LocalName == "InstdAmt");
            decimal? amount = TryDecimal(amountElement?.Value, out var parsedAmount) ? parsedAmount : null;
            rows.Add(new(rowNumber, Limit(identity, 128)!, null, null, amount,
                amountElement?.Attribute("Ccy")?.Value, FirstValue(status, "AddtlInf"),
                FirstValue(status, "Nm"), FirstValue(status, "OrgnlInstrId"), paymentStatus, []));
        }
        return new ParsedBankStatement(BankStatementImportFormats.Pain002, ns[Prefix.Length..],
            CurrentParserVersion, Limit(messageId ?? Sha256(ns), 128)!, null, null, null, null,
            rows, [], true);
    }

    private static async Task<XElement> ReadElementAsync(XmlReader reader, CancellationToken cancellationToken)
    {
        using var subtree = reader.ReadSubtree();
        var element = await XElement.LoadAsync(subtree, LoadOptions.None, cancellationToken);
        reader.Skip();
        return element;
    }
    private static string? ResolveCounterparty(XElement detail, decimal? amount)
    {
        var party = amount >= 0 ? "Dbtr" : "Cdtr";
        return detail.Descendants().FirstOrDefault(x => x.Name.LocalName == party)?
            .Descendants().FirstOrDefault(x => x.Name.LocalName == "Nm")?.Value?.Trim()
            ?? detail.Descendants().FirstOrDefault(x => x.Name.LocalName == "Nm")?.Value?.Trim();
    }
    private static string? FirstDirectValue(XElement element, string name) => element.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value?.Trim();
    private static string? FirstValue(XElement element, string name) => element.Descendants().FirstOrDefault(x => x.Name.LocalName == name)?.Value?.Trim();
    private static string? FirstDate(XElement element, string container) => element.Descendants().FirstOrDefault(x => x.Name.LocalName == container)?
        .Descendants().FirstOrDefault(x => x.Name.LocalName is "Dt" or "DtTm")?.Value;
    private static DateTime? ParseDate(string? value) => DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed) ? parsed.UtcDateTime : null;
    private static bool TryDecimal(string? value, out decimal result) => decimal.TryParse(value,
        NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
    private static decimal Sign(decimal value, string? indicator) => string.Equals(indicator, "DBIT", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(value) : Math.Abs(value);
    private static bool AccountMatches(string? source, string masked, string? external)
    {
        if (string.IsNullOrWhiteSpace(source)) return true;
        static string Alnum(string? value) => new((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        var normalized = Alnum(source);
        var externalNormalized = Alnum(external);
        var maskedDigits = new string(masked.Where(char.IsDigit).ToArray());
        return externalNormalized.Length > 0 && string.Equals(normalized, externalNormalized, StringComparison.Ordinal) ||
            maskedDigits.Length >= 4 && normalized.EndsWith(maskedDigits[^4..], StringComparison.Ordinal);
    }
    private static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero);
    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string? Limit(string? value, int max) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static BankStatementImportOperationException Malformed(string message, Exception? inner = null) =>
        new(BankStatementImportReasonCodes.MalformedFile, message, false, inner);
}
