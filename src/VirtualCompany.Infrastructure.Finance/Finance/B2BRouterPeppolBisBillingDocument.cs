using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed record B2BRouterPeppolDocumentBuildResult(byte[] Content,
    CustomerInvoiceElectronicDocumentValidation Validation);

internal sealed record B2BRouterInvoiceRoute(bool Supported, string SafeMessage, string? ParticipantScheme,
    string? ParticipantIdentifier, string? DocumentType);

internal static class B2BRouterInvoiceSnapshot
{
    public static B2BRouterInvoiceRoute ReadRoute(string snapshotJson, string issuedDocumentType)
    {
        try
        {
            using var document = JsonDocument.Parse(snapshotJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("buyer", out var buyer))
                return Unsupported("The immutable invoice does not contain a buyer snapshot.");
            var identifier = Text(buyer, "eInvoiceIdentifier");
            var type = Text(buyer, "eInvoiceIdentifierType");
            if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(type))
                return Unsupported("The immutable invoice does not contain a Peppol participant identifier and scheme.");
            var scheme = NormalizeScheme(type, identifier, out var normalizedIdentifier);
            if (scheme is null)
                return Unsupported("The recipient identifier scheme is not supported. Use Peppol scheme 0007 or 0088.");
            var documentType = issuedDocumentType switch
            {
                StatutoryDocumentTypes.CustomerInvoice => "invoice",
                StatutoryDocumentTypes.CustomerCredit => "credit_note",
                _ => null
            };
            return documentType is null ? Unsupported("Only native customer invoices and credit notes can use this Peppol profile.")
                : new(true, "The retained Peppol route is supported.", scheme, normalizedIdentifier, documentType);
        }
        catch (JsonException)
        { return Unsupported("The immutable invoice snapshot cannot be read safely."); }
    }

    private static string? NormalizeScheme(string type, string identifier, out string normalizedIdentifier)
    {
        var normalizedType = type.Trim().ToLowerInvariant().Replace("peppol:", string.Empty, StringComparison.Ordinal);
        normalizedIdentifier = identifier.Trim();
        var colon = normalizedIdentifier.IndexOf(':');
        if (colon == 4 && normalizedIdentifier[..colon].All(char.IsDigit))
        {
            if (normalizedType is "peppol" or "participant" or "endpoint") normalizedType = normalizedIdentifier[..colon];
            normalizedIdentifier = normalizedIdentifier[(colon + 1)..].Trim();
        }
        var scheme = normalizedType switch
        {
            "0007" or "se:orgnr" or "se_orgnr" or "swedish_organisation_number" => "0007",
            "0088" or "gln" or "ean" => "0088",
            _ => null
        };
        if (scheme == "0007") normalizedIdentifier = Digits(normalizedIdentifier);
        if (scheme == "0088") normalizedIdentifier = Digits(normalizedIdentifier);
        if (scheme == "0007" && normalizedIdentifier.Length != 10) return null;
        if (scheme == "0088" && normalizedIdentifier.Length != 13) return null;
        return scheme;
    }

    private static B2BRouterInvoiceRoute Unsupported(string message) => new(false, message, null, null, null);
    internal static string? Text(JsonElement element, string name) => element.TryGetProperty(name, out var property) &&
        property.ValueKind != JsonValueKind.Null ? property.ToString().Trim() : null;
    internal static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
}

internal static class B2BRouterPeppolBisBillingDocument
{
    private const string CustomizationId = "urn:cen.eu:en16931:2017#compliant#urn:fdc:peppol.eu:2017:poacc:billing:3.0";
    private const string ProfileId = "urn:fdc:peppol.eu:2017:poacc:billing:01:1.0";
    private static readonly XNamespace Cbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace Cac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace InvoiceNs = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace CreditNs = "urn:oasis:names:specification:ubl:schema:xsd:CreditNote-2";

    public static B2BRouterPeppolDocumentBuildResult Build(string snapshotJson,
        CustomerInvoiceElectronicDelivery delivery, string attachmentFileName, byte[] attachment,
        string? paymentAccountId, string? paymentAccountName, string? paymentServiceProviderId,
        string? originalDocumentNumber = null)
    {
        var failures = new List<(string Code, string Message)>();
        JsonDocument parsed;
        try { parsed = JsonDocument.Parse(snapshotJson); }
        catch (JsonException)
        {
            return Invalid([], "snapshot_invalid", "The immutable invoice snapshot is not valid JSON.");
        }
        using (parsed)
        {
            var root = parsed.RootElement;
            if (!root.TryGetProperty("draft", out var draft) || !root.TryGetProperty("seller", out var seller) ||
                !root.TryGetProperty("buyer", out var buyer) || !root.TryGetProperty("lines", out var linesElement) ||
                linesElement.ValueKind != JsonValueKind.Array)
                return Invalid([], "snapshot_incomplete", "The immutable invoice snapshot is incomplete for Peppol BIS Billing 3.");

            var documentNumber = Text(root, "documentNumber");
            var issueDate = Date(draft, "issueDate");
            var dueDate = Date(draft, "dueDate");
            var currency = Text(draft, "currency")?.ToUpperInvariant();
            var buyerReference = Text(draft, "buyerReference") ?? Text(buyer, "buyerReference");
            var netTotal = Money(draft, "netTotal");
            var taxTotal = Money(draft, "taxTotal");
            var grossTotal = Money(draft, "grossTotal");
            var rounding = Money(draft, "roundingAmount") ?? 0m;
            Required(documentNumber, "document_number_missing", "The invoice number is missing.", failures);
            if (issueDate is null) failures.Add(("issue_date_missing", "The issue date is missing."));
            if (dueDate is null && delivery.DocumentType == "invoice") failures.Add(("due_date_missing", "The payment due date is missing."));
            if (currency != "SEK") failures.Add(("currency_unsupported", "This provider profile currently supports SEK invoices only."));
            Required(buyerReference, "buyer_reference_missing", "A buyer reference is required for Peppol BIS Billing 3.", failures);
            if (netTotal is null || taxTotal is null || grossTotal is null)
                failures.Add(("totals_missing", "Invoice totals are missing."));
            if (delivery.DocumentType == "invoice" && string.IsNullOrWhiteSpace(paymentAccountId))
                failures.Add(("payment_account_missing", "Configure the seller payment account identifier before Peppol delivery."));
            if (delivery.DocumentType == "credit_note" && string.IsNullOrWhiteSpace(originalDocumentNumber))
                failures.Add(("original_invoice_reference_missing",
                    "The credit note must retain the original invoice number for Peppol billing reference."));
            if (attachment.Length == 0 || !attachment.AsSpan().StartsWith("%PDF"u8))
                failures.Add(("attachment_invalid", "The immutable invoice attachment is not a valid PDF."));

            var sellerParty = ReadParty(seller, true, delivery, failures);
            var buyerParty = ReadParty(buyer, false, delivery, failures);
            var lines = ReadLines(linesElement, currency ?? "SEK", failures);
            if (lines.Count == 0) failures.Add(("lines_missing", "At least one invoice line is required."));
            if (netTotal.HasValue && !SameMoney(lines.Sum(x => x.NetAmount), netTotal.Value))
                failures.Add(("net_total_mismatch", "The retained net total does not match the Peppol invoice lines."));
            if (taxTotal.HasValue && !SameMoney(lines.Sum(x => x.TaxAmount), taxTotal.Value))
                failures.Add(("tax_total_mismatch", "The retained VAT total does not match the Peppol invoice lines."));
            if (netTotal.HasValue && taxTotal.HasValue && grossTotal.HasValue &&
                !SameMoney(netTotal.Value + taxTotal.Value + rounding, grossTotal.Value))
                failures.Add(("gross_total_mismatch", "The retained gross total does not reconcile to net, VAT, and rounding."));
            if (failures.Count > 0)
                return Invalid(failures);

            var isCredit = delivery.DocumentType == "credit_note";
            var rootNs = isCredit ? CreditNs : InvoiceNs;
            var rootName = isCredit ? "CreditNote" : "Invoice";
            var document = new XDocument(new XDeclaration("1.0", "UTF-8", null),
                new XElement(rootNs + rootName,
                    new XAttribute(XNamespace.Xmlns + "cac", Cac),
                    new XAttribute(XNamespace.Xmlns + "cbc", Cbc),
                    El("CustomizationID", CustomizationId), El("ProfileID", ProfileId),
                    El("ID", documentNumber!), El("IssueDate", issueDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    !isCredit && dueDate.HasValue ? El("DueDate", dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null,
                    isCredit ? El("CreditNoteTypeCode", "381") : El("InvoiceTypeCode", "380"),
                    El("DocumentCurrencyCode", currency!), El("BuyerReference", buyerReference!),
                    isCredit ? BillingReference(originalDocumentNumber!) : null,
                    Attachment(documentNumber!, attachmentFileName, attachment),
                    Party("AccountingSupplierParty", sellerParty), Party("AccountingCustomerParty", buyerParty),
                    PaymentMeans(documentNumber!, dueDate, paymentAccountId, paymentAccountName, paymentServiceProviderId, isCredit),
                    TaxTotal(lines, currency!),
                    MonetaryTotal(netTotal!.Value, taxTotal!.Value, grossTotal!.Value, rounding, currency!),
                    lines.Select((line, index) => Line(line, index + 1, currency!, isCredit))));
            using var memory = new MemoryStream();
            var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false,
                OmitXmlDeclaration = false, NewLineHandling = NewLineHandling.None };
            using (var writer = XmlWriter.Create(memory, settings)) document.Save(writer);
            var bytes = memory.ToArray();
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            return new(bytes, new(true, B2BRouterOptions.PeppolBisBillingProfile,
                B2BRouterOptions.PeppolBisBillingVersion, hash, [], []));
        }
    }

    private static PartyData ReadParty(JsonElement element, bool seller,
        CustomerInvoiceElectronicDelivery delivery, List<(string Code, string Message)> failures)
    {
        var legalName = Text(element, "legalName");
        var address = Text(element, seller ? "registeredAddressLine1" : "billingAddressLine1");
        var address2 = Text(element, seller ? "registeredAddressLine2" : "billingAddressLine2");
        var postal = Text(element, seller ? "registeredPostalCode" : "billingPostalCode");
        var city = Text(element, seller ? "registeredCity" : "billingCity");
        var country = Text(element, seller ? "registeredCountryCode" : "billingCountryCode")?.ToUpperInvariant();
        var vat = Text(element, seller ? "vatRegistrationNumber" : "vatIdentifier");
        var endpointScheme = seller ? "0007" : delivery.ParticipantScheme;
        var endpoint = seller ? B2BRouterInvoiceSnapshot.Digits(Text(element, "swedishOrganisationNumber") ?? "")
            : delivery.ParticipantIdentifier;
        Required(legalName, seller ? "seller_name_missing" : "buyer_name_missing", $"The {(seller ? "seller" : "buyer")} legal name is missing.", failures);
        Required(address, seller ? "seller_address_missing" : "buyer_address_missing", $"The {(seller ? "seller" : "buyer")} address is missing.", failures);
        Required(postal, seller ? "seller_postal_missing" : "buyer_postal_missing", $"The {(seller ? "seller" : "buyer")} postal code is missing.", failures);
        Required(city, seller ? "seller_city_missing" : "buyer_city_missing", $"The {(seller ? "seller" : "buyer")} city is missing.", failures);
        if (country != "SE") failures.Add((seller ? "seller_country_unsupported" : "buyer_country_unsupported",
            "This launch profile supports Swedish domestic Peppol invoices only."));
        Required(endpoint, seller ? "seller_endpoint_missing" : "buyer_endpoint_missing", $"The {(seller ? "seller" : "buyer")} Peppol endpoint is missing.", failures);
        if (seller && endpoint.Length != 10) failures.Add(("seller_endpoint_invalid", "The seller Swedish organisation number must contain 10 digits."));
        Required(vat, seller ? "seller_vat_missing" : "buyer_vat_missing", $"The {(seller ? "seller" : "buyer")} VAT identifier is missing.", failures);
        return new(legalName!, address!, address2, postal!, city!, country!, vat!, endpointScheme, endpoint);
    }

    private static List<LineData> ReadLines(JsonElement lines, string currency,
        List<(string Code, string Message)> failures)
    {
        var result = new List<LineData>();
        foreach (var element in lines.EnumerateArray())
        {
            var description = Text(element, "description");
            var quantity = Decimal(element, "quantity");
            var unitPrice = Money(element, "unitPrice");
            var discountAmount = Money(element, "discountAmount") ?? 0m;
            var discountPercent = Decimal(element, "discountPercent") ?? 0m;
            var net = Money(element, "netAmount");
            var taxRate = Decimal(element, "taxRate");
            var tax = Money(element, "taxAmount");
            var unit = UnitCode(Text(element, "unit"));
            if (string.IsNullOrWhiteSpace(description) || quantity is null or <= 0 || unitPrice is null or < 0 ||
                net is null or < 0 || taxRate is null or <= 0 || tax is null or < 0 || unit is null)
            {
                failures.Add(("line_invalid", "Each Peppol line requires a description, supported unit, positive quantity, price, net amount, and positive Swedish VAT rate."));
                continue;
            }
            result.Add(new(description, quantity.Value, unit, unitPrice.Value, discountAmount,
                discountPercent, net.Value, taxRate.Value, tax.Value));
        }
        return result;
    }

    private static XElement Party(string name, PartyData party) => new(Cac + name,
        new XElement(Cac + "Party", El("EndpointID", party.Endpoint, new XAttribute("schemeID", party.EndpointScheme)),
            new XElement(Cac + "PartyIdentification", El("ID", party.Endpoint,
                new XAttribute("schemeID", party.EndpointScheme))),
            new XElement(Cac + "PostalAddress", El("StreetName", party.AddressLine1),
                string.IsNullOrWhiteSpace(party.AddressLine2) ? null : El("AdditionalStreetName", party.AddressLine2),
                El("CityName", party.City), El("PostalZone", party.PostalCode),
                new XElement(Cac + "Country", El("IdentificationCode", party.CountryCode))),
            new XElement(Cac + "PartyTaxScheme", El("CompanyID", party.VatIdentifier),
                new XElement(Cac + "TaxScheme", El("ID", "VAT"))),
            new XElement(Cac + "PartyLegalEntity", El("RegistrationName", party.LegalName),
                El("CompanyID", party.Endpoint, new XAttribute("schemeID", party.EndpointScheme)))));

    private static XElement Attachment(string documentNumber, string fileName, byte[] bytes) =>
        new(Cac + "AdditionalDocumentReference", El("ID", $"{documentNumber}-pdf"), El("DocumentType", "Invoice PDF"),
            new XElement(Cac + "Attachment", El("EmbeddedDocumentBinaryObject", Convert.ToBase64String(bytes),
                new XAttribute("mimeCode", "application/pdf"), new XAttribute("filename", fileName))));

    private static XElement BillingReference(string originalDocumentNumber) =>
        new(Cac + "BillingReference", new XElement(Cac + "InvoiceDocumentReference",
            El("ID", originalDocumentNumber)));

    private static XElement? PaymentMeans(string documentNumber, DateOnly? dueDate, string? accountId,
        string? accountName, string? providerId, bool isCredit)
    {
        if (isCredit || string.IsNullOrWhiteSpace(accountId)) return null;
        return new(Cac + "PaymentMeans", El("PaymentMeansCode", "30"),
            dueDate.HasValue ? El("PaymentDueDate", dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null,
            El("PaymentID", documentNumber), new XElement(Cac + "PayeeFinancialAccount", El("ID", accountId),
                string.IsNullOrWhiteSpace(accountName) ? null : El("Name", accountName),
                string.IsNullOrWhiteSpace(providerId) ? null : new XElement(Cac + "FinancialInstitutionBranch", El("ID", providerId))));
    }

    private static XElement TaxTotal(IReadOnlyCollection<LineData> lines, string currency) =>
        new(Cac + "TaxTotal", Amount("TaxAmount", lines.Sum(x => x.TaxAmount), currency),
            lines.GroupBy(x => x.TaxRate).OrderBy(x => x.Key).Select(group =>
                new XElement(Cac + "TaxSubtotal", Amount("TaxableAmount", group.Sum(x => x.NetAmount), currency),
                    Amount("TaxAmount", group.Sum(x => x.TaxAmount), currency),
                    new XElement(Cac + "TaxCategory", El("ID", "S"), El("Percent", DecimalText(group.Key)),
                        new XElement(Cac + "TaxScheme", El("ID", "VAT"))))));

    private static XElement MonetaryTotal(decimal net, decimal tax, decimal gross, decimal rounding, string currency) =>
        new(Cac + "LegalMonetaryTotal", Amount("LineExtensionAmount", net, currency), Amount("TaxExclusiveAmount", net, currency),
            Amount("TaxInclusiveAmount", gross, currency), rounding == 0 ? null : Amount("PayableRoundingAmount", rounding, currency),
            Amount("PayableAmount", gross, currency));

    private static XElement Line(LineData line, int index, string currency, bool credit) =>
        new(Cac + (credit ? "CreditNoteLine" : "InvoiceLine"), El("ID", index.ToString(CultureInfo.InvariantCulture)),
            El(credit ? "CreditedQuantity" : "InvoicedQuantity", DecimalText(line.Quantity),
                new XAttribute("unitCode", line.UnitCode)), Amount("LineExtensionAmount", line.NetAmount, currency),
            line.DiscountAmount == 0 ? null : new XElement(Cac + "AllowanceCharge", El("ChargeIndicator", "false"),
                El("MultiplierFactorNumeric", DecimalText(line.DiscountPercent / 100m)),
                Amount("Amount", line.DiscountAmount, currency),
                Amount("BaseAmount", line.Quantity * line.UnitPrice, currency)),
            new XElement(Cac + "Item", El("Name", line.Description),
                new XElement(Cac + "ClassifiedTaxCategory", El("ID", "S"), El("Percent", DecimalText(line.TaxRate)),
                    new XElement(Cac + "TaxScheme", El("ID", "VAT")))),
            new XElement(Cac + "Price", Amount("PriceAmount", line.UnitPrice, currency),
                El("BaseQuantity", "1", new XAttribute("unitCode", line.UnitCode))));

    private static XElement Amount(string name, decimal value, string currency) =>
        El(name, MoneyText(value), new XAttribute("currencyID", currency));
    private static XElement El(string name, object content, params object[] attributes) =>
        new(Cbc + name, attributes, content);
    private static string MoneyText(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture);
    private static string DecimalText(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static bool SameMoney(decimal left, decimal right) =>
        decimal.Round(left, 2, MidpointRounding.AwayFromZero) ==
        decimal.Round(right, 2, MidpointRounding.AwayFromZero);
    private static string? UnitCode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "st" or "pcs" or "piece" or "pieces" or "each" or "unit" => "C62",
        "h" or "hr" or "hour" or "hours" => "HUR",
        "day" or "days" => "DAY",
        "kg" or "kilogram" or "kilograms" => "KGM",
        _ => null
    };
    private static string? Text(JsonElement element, string name) => B2BRouterInvoiceSnapshot.Text(element, name);
    private static decimal? Money(JsonElement element, string name) => Decimal(element, name);
    private static decimal? Decimal(JsonElement element, string name) => element.TryGetProperty(name, out var property) &&
        decimal.TryParse(property.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
    private static DateOnly? Date(JsonElement element, string name) => element.TryGetProperty(name, out var property) &&
        DateOnly.TryParse(property.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var value) ? value : null;
    private static void Required(string? value, string code, string message, ICollection<(string Code, string Message)> failures)
    { if (string.IsNullOrWhiteSpace(value)) failures.Add((code, message)); }
    private static B2BRouterPeppolDocumentBuildResult Invalid(IReadOnlyCollection<(string Code, string Message)> failures,
        string? code = null, string? message = null)
    {
        var all = failures.ToList(); if (code is not null && message is not null) all.Add((code, message));
        return new([], new(false, B2BRouterOptions.PeppolBisBillingProfile,
            B2BRouterOptions.PeppolBisBillingVersion, new string('0', 64), all.Select(x => x.Code).Distinct().ToArray(),
            all.Select(x => x.Message).Distinct().ToArray()));
    }
    private sealed record PartyData(string LegalName, string AddressLine1, string? AddressLine2, string PostalCode,
        string City, string CountryCode, string VatIdentifier, string EndpointScheme, string Endpoint);
    private sealed record LineData(string Description, decimal Quantity, string UnitCode, decimal UnitPrice,
        decimal DiscountAmount, decimal DiscountPercent, decimal NetAmount, decimal TaxRate, decimal TaxAmount);
}
