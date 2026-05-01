using System.Globalization;
using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FortnoxMappingService : IFortnoxMappingService
{
    public FortnoxCounterpartySyncModel MapCustomer(FortnoxCustomer customer)
    {
        var externalId = Required(customer.CustomerNumber, "Fortnox customer number");
        return new FortnoxCounterpartySyncModel(
            externalId,
            externalId,
            Required(customer.Name, "Fortnox customer name"),
            "customer",
            customer.Email,
            customer.OrganisationNumber,
            ParseDateTime(customer.LastModified));
    }

    public FortnoxCounterpartySyncModel MapSupplier(FortnoxSupplier supplier)
    {
        var externalId = Required(supplier.SupplierNumber, "Fortnox supplier number");
        return new FortnoxCounterpartySyncModel(
            externalId,
            externalId,
            Required(supplier.Name, "Fortnox supplier name"),
            "supplier",
            supplier.Email,
            supplier.OrganisationNumber,
            ParseDateTime(supplier.LastModified));
    }

    public FortnoxAccountSyncModel MapAccount(FortnoxAccount account)
    {
        var number = account.Number?.ToString(CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("Fortnox account number is required.");
        return new FortnoxAccountSyncModel(
            number,
            number,
            number,
            Required(account.Description, "Fortnox account description"),
            NormalizeAccountType(account.Type, account.Number),
            ParseDateTime(account.LastModified));
    }

    public FortnoxArticleSyncModel MapArticle(FortnoxArticle article)
    {
        var externalId = Required(article.ArticleNumber, "Fortnox article number");
        return new FortnoxArticleSyncModel(
            externalId,
            externalId,
            Required(article.Description, "Fortnox article description"),
            article.SalesPrice ?? 0m,
            ParseDateTime(article.LastModified));
    }

    public FortnoxProjectSyncModel MapProject(FortnoxProject project)
    {
        var externalId = Required(project.ProjectNumber, "Fortnox project number");
        return new FortnoxProjectSyncModel(
            externalId,
            externalId,
            Required(project.Description, "Fortnox project description"),
            string.IsNullOrWhiteSpace(project.Status) ? "active" : project.Status.Trim().ToLowerInvariant(),
            ParseDateTime(project.LastModified));
    }

    public FortnoxInvoiceSyncModel MapInvoice(FortnoxInvoice invoice)
    {
        var externalId = Required(invoice.DocumentNumber, "Fortnox invoice number");
        var amount = invoice.Total ?? 0m;
        var balance = invoice.Balance ?? ReadDecimal(invoice.AdditionalData, "Balance") ?? amount;
        var paidAmount = Math.Max(0m, amount - balance);
        var status = ResolveDocumentStatus(invoice.Cancelled, invoice.Booked, invoice.FullyPaid ?? balance <= 0m);

        return new FortnoxInvoiceSyncModel(
            externalId,
            externalId,
            Required(invoice.CustomerNumber, "Fortnox invoice customer number"),
            string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Fortnox customer" : invoice.CustomerName.Trim(),
            ParseDate(invoice.InvoiceDate) ?? DateTime.UtcNow.Date,
            ParseDate(invoice.DueDate) ?? ParseDate(invoice.InvoiceDate) ?? DateTime.UtcNow.Date,
            amount,
            NormalizeCurrency(invoice.Currency),
            status,
            status == "paid" ? FinanceSettlementStatuses.Paid : FinanceSettlementStatuses.Unpaid,
            paidAmount,
            ParseDateTime(invoice.LastModified));
    }

    public FortnoxSupplierInvoiceSyncModel MapSupplierInvoice(FortnoxSupplierInvoice invoice)
    {
        var externalId = Required(invoice.GivenNumber, "Fortnox supplier invoice number");
        var amount = invoice.Total ?? 0m;
        var balance = invoice.Balance ?? ReadDecimal(invoice.AdditionalData, "Balance") ?? amount;
        var paidAmount = Math.Max(0m, amount - balance);
        var status = ResolveDocumentStatus(invoice.Cancelled, invoice.Booked, invoice.FullyPaid ?? balance <= 0m);

        return new FortnoxSupplierInvoiceSyncModel(
            externalId,
            externalId,
            Required(invoice.SupplierNumber, "Fortnox supplier invoice supplier number"),
            string.IsNullOrWhiteSpace(invoice.SupplierName) ? "Fortnox supplier" : invoice.SupplierName.Trim(),
            ParseDate(invoice.InvoiceDate) ?? DateTime.UtcNow.Date,
            ParseDate(invoice.DueDate) ?? ParseDate(invoice.InvoiceDate) ?? DateTime.UtcNow.Date,
            amount,
            NormalizeCurrency(invoice.Currency),
            status,
            status == "paid" ? FinanceSettlementStatuses.Paid : FinanceSettlementStatuses.Unpaid,
            paidAmount,
            ParseDateTime(invoice.LastModified));
    }

    public FortnoxVoucherSyncModel MapVoucher(FortnoxVoucher voucher)
    {
        var series = Required(voucher.VoucherSeries, "Fortnox voucher series");
        var number = voucher.VoucherNumber?.ToString(CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("Fortnox voucher number is required.");
        var externalId = $"{series}-{number}";

        return new FortnoxVoucherSyncModel(
            externalId,
            externalId,
            ParseDate(voucher.VoucherDate) ?? DateTime.UtcNow.Date,
            string.IsNullOrWhiteSpace(voucher.Description) ? $"Fortnox voucher {externalId}" : voucher.Description.Trim(),
            Math.Abs(voucher.Total ?? ReadDecimal(voucher.AdditionalData, "Total") ?? 0m),
            ParseDateTime(voucher.LastModified));
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name} is required.") : value.Trim();

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "SEK" : currency.Trim().ToUpperInvariant();

    private static string ResolveDocumentStatus(bool? cancelled, bool? booked, bool fullyPaid) =>
        cancelled == true ? "void" : fullyPaid ? "paid" : booked == true ? "approved" : "open";

    private static string NormalizeAccountType(string? type, int? number)
    {
        if (!string.IsNullOrWhiteSpace(type)) return type.Trim().ToLowerInvariant();
        return number switch
        {
            >= 1000 and < 2000 => "asset",
            >= 2000 and < 3000 => "liability",
            >= 3000 and < 4000 => "revenue",
            _ => "expense"
        };
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var result)
            ? DateTime.SpecifyKind(result.Date, DateTimeKind.Utc)
            : null;

    private static DateTime? ParseDateTime(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var result)
            ? result.UtcDateTime
            : ParseDate(value);

    private static decimal? ReadDecimal(Dictionary<string, JsonElement>? data, string name) =>
        data is not null &&
        data.TryGetValue(name, out var element) &&
        element.ValueKind is JsonValueKind.Number &&
        element.TryGetDecimal(out var value)
            ? value
            : null;
}
