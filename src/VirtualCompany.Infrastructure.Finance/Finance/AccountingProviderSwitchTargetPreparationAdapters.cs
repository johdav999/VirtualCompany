using System.Globalization;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxAccountingProviderSwitchTargetPreparationAdapter : IAccountingProviderSwitchTargetPreparationAdapter
{
    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public AccountingProviderSwitchTargetOperation Map(AccountingProviderSwitchTargetMappingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!string.Equals(request.TargetProviderKey, ProviderKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The target provider does not match this adapter.", nameof(request));

        var record = request.Record;
        var source = JsonNode.Parse(record.NormalizedDataJson) as JsonObject ?? new JsonObject();
        return record.Dataset switch
        {
            AccountingProviderSwitchStagingDatasets.Accounts => Command(record.Dataset,
                AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting, "create_account",
                "The account is master data and can be prepared without posting a financial transaction.", ["bookkeeping"],
                "migration_account_create", "POST", "accounts", "Account", Account(source, record.SourceIdentity)),

            AccountingProviderSwitchStagingDatasets.Counterparties => Counterparty(record, source),

            AccountingProviderSwitchStagingDatasets.Dimensions => Dimension(record, source),

            AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates => Command(record.Dataset,
                AccountingProviderSwitchTargetOperationModes.FinalAuthoritative, "create_opening_balance_voucher",
                "Opening balances create authoritative accounting entries and remain held for cutover.", ["bookkeeping"],
                "migration_opening_balance", "POST", "vouchers", "Voucher", Voucher(source, record, opening: true)),

            AccountingProviderSwitchStagingDatasets.Journals => Command(record.Dataset,
                AccountingProviderSwitchTargetOperationModes.FinalAuthoritative, "create_historical_voucher",
                "Historical vouchers create accounting entries and remain held for cutover.", ["bookkeeping"],
                "migration_historical_voucher", "POST", "vouchers", "Voucher", Voucher(source, record, opening: false)),

            AccountingProviderSwitchStagingDatasets.Invoices or AccountingProviderSwitchStagingDatasets.OpenItems or
                AccountingProviderSwitchStagingDatasets.Credits => Document(record, source),

            AccountingProviderSwitchStagingDatasets.Payments or AccountingProviderSwitchStagingDatasets.Allocations => Payment(record, source),

            AccountingProviderSwitchStagingDatasets.TaxTreatments or AccountingProviderSwitchStagingDatasets.JournalLines or
                AccountingProviderSwitchStagingDatasets.BankState or AccountingProviderSwitchStagingDatasets.Currencies or
                AccountingProviderSwitchStagingDatasets.ExchangeRates => Preview(record.Dataset,
                    "The normalized record is validated as supporting evidence; Fortnox has no independent non-posting object for this dataset."),

            AccountingProviderSwitchStagingDatasets.Documents => Unsupported(record.Dataset,
                "The current production adapter has no verified Fortnox Inbox upload contract for migration evidence. Keep the source archive available or resolve the capability gap."),

            _ => Unsupported(record.Dataset, "The current Fortnox target adapter does not support this migration dataset.")
        };
    }

    private static AccountingProviderSwitchTargetOperation Counterparty(AccountingProviderSwitchTargetRecord record, JsonObject source)
    {
        var type = Text(source, "counterpartyType", "type")?.ToLowerInvariant();
        var supplier = type is "supplier" or "vendor";
        var path = supplier ? "suppliers" : "customers";
        var wrapper = supplier ? "Supplier" : "Customer";
        var payload = new JsonObject
        {
            [supplier ? "SupplierNumber" : "CustomerNumber"] = Text(source, "number", "code", "supplierNumber", "customerNumber") ?? record.SourceIdentity,
            ["Name"] = Text(source, "name", "displayName") ?? record.SourceIdentity,
            ["OrganisationNumber"] = Text(source, "organisationNumber", "organizationNumber"),
            ["Email"] = Text(source, "email"), ["Phone1"] = Text(source, "phone"),
            ["Address1"] = Text(source, "address1", "address"), ["ZipCode"] = Text(source, "zipCode", "postalCode"),
            ["City"] = Text(source, "city"), ["CountryCode"] = Text(source, "countryCode"),
            ["Currency"] = Text(source, "currency")
        };
        return Command(record.Dataset, AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting,
            supplier ? "create_supplier" : "create_customer",
            $"The {type ?? "customer"} is master data and can be prepared without posting a financial transaction.",
            [supplier ? "supplier" : "customer"], supplier ? "migration_supplier_create" : "migration_customer_create",
            "POST", path, wrapper, payload);
    }

    private static AccountingProviderSwitchTargetOperation Dimension(AccountingProviderSwitchTargetRecord record, JsonObject source)
    {
        var type = Text(source, "dimensionType", "type")?.ToLowerInvariant() ?? "project";
        var number = Text(source, "number", "code", "key") ?? record.SourceIdentity;
        var description = Text(source, "description", "name") ?? number;
        return type switch
        {
            "project" => Command(record.Dataset, AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting,
                "create_project", "A Fortnox project can be prepared without posting a transaction.", ["project"],
                "migration_project_create", "POST", "projects", "Project", new JsonObject { ["ProjectNumber"] = number, ["Description"] = description }),
            "cost_center" or "costcentre" => Command(record.Dataset, AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting,
                "create_cost_center", "A Fortnox cost center can be prepared without posting a transaction.", ["costcenter"],
                "migration_cost_center_create", "POST", "costcenters", "CostCenter", new JsonObject { ["Code"] = number, ["Description"] = description }),
            _ => Unsupported(record.Dataset, $"Fortnox target preparation does not support the '{type}' dimension type.")
        };
    }

    private static AccountingProviderSwitchTargetOperation Document(AccountingProviderSwitchTargetRecord record, JsonObject source)
    {
        var supplier = IsSupplier(source);
        var credit = record.Dataset == AccountingProviderSwitchStagingDatasets.Credits || Bool(source, "credit");
        var wrapper = supplier ? "SupplierInvoice" : "Invoice";
        var path = supplier ? "supplierinvoices" : "invoices";
        var scope = supplier ? "supplierinvoice" : "invoice";
        var payload = new JsonObject
        {
            [supplier ? "SupplierNumber" : "CustomerNumber"] = Text(source, "counterpartyNumber", supplier ? "supplierNumber" : "customerNumber"),
            [supplier ? "InvoiceNumber" : "DocumentNumber"] = Text(source, "documentNumber", "invoiceNumber", "number") ?? record.SourceIdentity,
            ["InvoiceDate"] = Text(source, "invoiceDate", "documentDate", "date"), ["DueDate"] = Text(source, "dueDate"),
            ["Currency"] = Text(source, "currency") ?? record.Currency, ["Credit"] = credit,
            ["Comments"] = Text(source, "description", "comments") ?? $"Migration {record.SourceIdentity}"
        };
        return Command(record.Dataset, AccountingProviderSwitchTargetOperationModes.FinalAuthoritative,
            credit ? "create_credit" : supplier ? "create_supplier_invoice" : "create_customer_invoice",
            "Open items and credits affect the provider subledger and remain held for final cutover.", [scope],
            credit ? "migration_credit_create" : supplier ? "migration_supplier_invoice_create" : "migration_invoice_create",
            "POST", path, wrapper, payload);
    }

    private static AccountingProviderSwitchTargetOperation Payment(AccountingProviderSwitchTargetRecord record, JsonObject source)
    {
        var supplier = IsSupplier(source);
        var wrapper = supplier ? "SupplierInvoicePayment" : "InvoicePayment";
        var path = supplier ? "supplierinvoicepayments" : "invoicepayments";
        var payload = new JsonObject
        {
            ["InvoiceNumber"] = Text(source, "invoiceNumber", "documentNumber"),
            ["PaymentDate"] = Text(source, "paymentDate", "date"),
            ["Amount"] = Number(source, "amount") ?? record.FinancialAmount,
            ["Currency"] = Text(source, "currency") ?? record.Currency,
            ["ExternalInvoiceReference1"] = record.SourceIdentity
        };
        return Command(record.Dataset, AccountingProviderSwitchTargetOperationModes.FinalAuthoritative,
            record.Dataset == AccountingProviderSwitchStagingDatasets.Allocations ? "allocate_payment" : "create_payment",
            "Payments and allocations affect the provider subledger and remain held for final cutover.", ["payment"],
            supplier ? "migration_supplier_payment" : "migration_customer_payment", "POST", path, wrapper, payload);
    }

    private static JsonObject Account(JsonObject source, string identity) => new()
    {
        ["Number"] = Integer(source, "number", "accountNumber", "code") ?? (int.TryParse(identity, out var number) ? number : null),
        ["Description"] = Text(source, "description", "name") ?? identity,
        ["Active"] = Bool(source, "active", true), ["VATCode"] = Text(source, "vatCode", "taxCode")
    };

    private static JsonObject Voucher(JsonObject source, AccountingProviderSwitchTargetRecord record, bool opening)
    {
        var rows = source["rows"]?.DeepClone() ?? source["lines"]?.DeepClone() ?? new JsonArray();
        return new JsonObject
        {
            ["VoucherSeries"] = Text(source, "voucherSeries", "series") ?? "A",
            ["VoucherDate"] = Text(source, "postingDate", "date"),
            ["Description"] = Text(source, "description") ?? (opening ? "Migration opening balances" : $"Migrated voucher {record.SourceIdentity}"),
            ["ReferenceNumber"] = record.SourceIdentity, ["VoucherRows"] = rows
        };
    }

    private static AccountingProviderSwitchTargetOperation Command(string dataset, string mode, string action,
        string explanation, IReadOnlyList<string> scopes, string commandType, string method, string path,
        string wrapper, JsonObject body)
    {
        RemoveNulls(body);
        var payload = new JsonObject { [wrapper] = body };
        var command = new AccountingProviderCommand(ProviderKeyStatic, commandType, method, path, action,
            FortnoxWritePayloadSanitizer.CreateSummary(payload), FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
            FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload), $"AccountingMigration{wrapper}");
        return new(true, dataset, mode, action, explanation, scopes, command);
    }

    private static AccountingProviderSwitchTargetOperation Preview(string dataset, string explanation) =>
        new(true, dataset, AccountingProviderSwitchTargetOperationModes.PreviewOnly, "validate_only", explanation, [], null);
    private static AccountingProviderSwitchTargetOperation Unsupported(string dataset, string explanation) =>
        new(false, dataset, AccountingProviderSwitchTargetOperationModes.PreviewOnly, "unsupported", explanation, [], null);
    private const string ProviderKeyStatic = "fortnox";

    private static bool IsSupplier(JsonObject source) =>
        string.Equals(Text(source, "counterpartyType", "documentType", "openItemType"), "supplier", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Text(source, "openItemType"), "payable", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Text(source, "documentType"), "supplier_invoice", StringComparison.OrdinalIgnoreCase);
    private static string? Text(JsonObject source, params string[] names)
    {
        foreach (var pair in source)
            if (names.Contains(pair.Key, StringComparer.OrdinalIgnoreCase) && pair.Value is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)) return text.Trim();
        return null;
    }
    private static decimal? Number(JsonObject source, string name)
    {
        foreach (var pair in source)
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) && pair.Value is JsonValue value && value.TryGetValue<decimal>(out var number)) return number;
        return null;
    }
    private static int? Integer(JsonObject source, params string[] names)
    {
        foreach (var name in names) if (int.TryParse(Text(source, name), out var number)) return number;
        return null;
    }
    private static bool Bool(JsonObject source, string name, bool fallback = false)
    {
        foreach (var pair in source)
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) && pair.Value is JsonValue value && value.TryGetValue<bool>(out var result)) return result;
        return fallback;
    }
    private static void RemoveNulls(JsonObject value)
    {
        foreach (var key in value.Where(x => x.Value is null).Select(x => x.Key).ToArray()) value.Remove(key);
    }
}
