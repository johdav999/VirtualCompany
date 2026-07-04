using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
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
            TrimTo(string.IsNullOrWhiteSpace(account.Description) ? $"Account {number}" : account.Description, 160),
            NormalizeAccountType(account.Type, account.Number),
            ResolveAccountBalance(account),
            ParseDateTime(account.LastModified),
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
        var paidAmount = CalculatePaidAmount(amount, balance);
        var isCreditNote = IsCreditDocument(amount, invoice.AdditionalData);
        var fullyPaid = invoice.FullyPaid ?? IsSettled(balance);
        var status = ResolveDocumentStatus(invoice.Cancelled, invoice.Booked, fullyPaid);
        var settlementStatus = ResolveSettlementStatus(isCreditNote, fullyPaid, paidAmount, balance);
        var dueUtc = ParseDate(invoice.DueDate) ?? ParseDate(invoice.InvoiceDate) ?? DateTime.UtcNow.Date;

        return new FortnoxInvoiceSyncModel(
            externalId,
            externalId,
            Required(invoice.CustomerNumber, "Fortnox invoice customer number"),
            string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Fortnox customer" : invoice.CustomerName.Trim(),
            ParseDate(invoice.InvoiceDate) ?? DateTime.UtcNow.Date,
            dueUtc,
            amount,
            NormalizeCurrency(invoice.Currency),
            status,
            settlementStatus,
            ResolvePostingStatus(invoice.Cancelled, invoice.Booked),
            ResolveDueStatus(dueUtc, settlementStatus, invoice.Cancelled),
            isCreditNote ? FinanceDocumentKinds.CreditNote : FinanceDocumentKinds.Invoice,
            BuildProviderStatus(invoice.Cancelled, invoice.Booked, invoice.FullyPaid, balance, isCreditNote, null, null, invoice.Sent),
            FinanceDocumentProcessingStatuses.None,
            paidAmount,
            ParseDateTime(invoice.LastModified));
    }

    public FortnoxSupplierInvoiceSyncModel MapSupplierInvoice(FortnoxSupplierInvoice invoice)
    {
        var externalId = Required(invoice.GivenNumber, "Fortnox supplier invoice number");
        var amount = invoice.Total ?? 0m;
        var balance = invoice.Balance ?? ReadDecimal(invoice.AdditionalData, "Balance") ?? amount;
        var paidAmount = CalculatePaidAmount(amount, balance);
        var isCreditNote = IsCreditDocument(amount, invoice.AdditionalData);
        var fullyPaid = invoice.FullyPaid ?? IsSettled(balance);
        var status = ResolveDocumentStatus(invoice.Cancelled, invoice.Booked, fullyPaid);
        var settlementStatus = ResolveSettlementStatus(isCreditNote, fullyPaid, paidAmount, balance);
        var dueUtc = ParseDate(invoice.DueDate) ?? ParseDate(invoice.InvoiceDate) ?? DateTime.UtcNow.Date;
        var processingStatus = ResolveProcessingStatus(invoice.PaymentPending, invoice.AuthorizePending, invoice.AuthorizerName, invoice.AdditionalData);

        return new FortnoxSupplierInvoiceSyncModel(
            externalId,
            externalId,
            Required(invoice.SupplierNumber, "Fortnox supplier invoice supplier number"),
            string.IsNullOrWhiteSpace(invoice.SupplierName) ? "Fortnox supplier" : invoice.SupplierName.Trim(),
            ParseDate(invoice.InvoiceDate) ?? DateTime.UtcNow.Date,
            dueUtc,
            amount,
            NormalizeCurrency(invoice.Currency),
            status,
            settlementStatus,
            ResolvePostingStatus(invoice.Cancelled, invoice.Booked),
            ResolveDueStatus(dueUtc, settlementStatus, invoice.Cancelled),
            isCreditNote ? FinanceDocumentKinds.SupplierCreditNote : FinanceDocumentKinds.SupplierInvoice,
            BuildProviderStatus(invoice.Cancelled, invoice.Booked, invoice.FullyPaid, balance, isCreditNote, processingStatus, invoice.AuthorizerName, null),
            processingStatus,
            paidAmount,
            ParseDateTime(invoice.LastModified),
            BuildSupplierInvoiceProviderMetadata(invoice, balance, isCreditNote, processingStatus));
    }

    public FortnoxInvoicePaymentSyncModel MapInvoicePayment(FortnoxInvoicePayment payment)
    {
        var externalId = Required(payment.Number, "Fortnox invoice payment number");
        var invoiceNumber = Required(payment.InvoiceNumber, "Fortnox invoice payment invoice number");
        return new FortnoxInvoicePaymentSyncModel(
            externalId,
            externalId,
            invoiceNumber,
            Math.Abs(payment.Amount ?? ReadDecimal(payment.AdditionalData, "Amount") ?? 0m),
            NormalizeCurrency(payment.Currency),
            ParseDate(payment.PaymentDate) ?? DateTime.UtcNow.Date,
            payment.Booked == true ? PaymentStatuses.Completed : PaymentStatuses.Pending,
            ParseDateTime(payment.LastModified));
    }

    public FortnoxSupplierInvoicePaymentSyncModel MapSupplierInvoicePayment(FortnoxSupplierInvoicePayment payment)
    {
        var externalId = Required(payment.Number, "Fortnox supplier invoice payment number");
        var invoiceNumber = Required(payment.InvoiceNumber, "Fortnox supplier invoice payment invoice number");
        return new FortnoxSupplierInvoicePaymentSyncModel(
            externalId,
            externalId,
            invoiceNumber,
            Math.Abs(payment.Amount ?? ReadDecimal(payment.AdditionalData, "Amount") ?? 0m),
            NormalizeCurrency(payment.Currency),
            ParseDate(payment.PaymentDate) ?? DateTime.UtcNow.Date,
            payment.Booked == true ? PaymentStatuses.Completed : PaymentStatuses.Pending,
            ParseDateTime(payment.LastModified));
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
            NormalizeOptional(voucher.ReferenceNumber) ?? ReadString(voucher.AdditionalData, "ReferenceNumber"),
            ParseDate(voucher.VoucherDate) ?? DateTime.UtcNow.Date,
            string.IsNullOrWhiteSpace(voucher.Description) ? $"Fortnox voucher {externalId}" : voucher.Description.Trim(),
            Math.Abs(voucher.Total ?? ReadDecimal(voucher.AdditionalData, "Total") ?? 0m),
            ParseDateTime(voucher.LastModified));
    }

    private static string Required(string? value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new InvalidOperationException($"{name} is required.") : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string TrimTo(string value, int maxLength)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string NormalizeCurrency(string? currency) =>
        string.IsNullOrWhiteSpace(currency) ? "SEK" : currency.Trim().ToUpperInvariant();

    private static string ResolveDocumentStatus(bool? cancelled, bool? booked, bool fullyPaid) =>
        cancelled == true ? "void" : fullyPaid ? "paid" : booked == true ? "approved" : "open";

    private static string ResolvePostingStatus(bool? cancelled, bool? booked) =>
        cancelled == true
            ? FinanceDocumentPostingStatuses.Cancelled
            : booked == true
                ? FinanceDocumentPostingStatuses.Booked
                : FinanceDocumentPostingStatuses.Draft;

    private static string ResolveSettlementStatus(bool isCreditNote, bool fullyPaid, decimal paidAmount, decimal balance)
    {
        if (isCreditNote)
        {
            return FinanceSettlementStatuses.Credited;
        }

        if (fullyPaid || IsSettled(balance))
        {
            return FinanceSettlementStatuses.Paid;
        }

        return paidAmount > 0m
            ? FinanceSettlementStatuses.PartiallyPaid
            : FinanceSettlementStatuses.Unpaid;
    }

    private static string ResolveDueStatus(DateTime dueUtc, string settlementStatus, bool? cancelled)
    {
        if (cancelled == true ||
            settlementStatus is FinanceSettlementStatuses.Paid or FinanceSettlementStatuses.Credited)
        {
            return FinanceDocumentDueStatuses.NotDue;
        }

        var today = DateTime.UtcNow.Date;
        if (dueUtc.Date < today)
        {
            return FinanceDocumentDueStatuses.Overdue;
        }

        return dueUtc.Date <= today.AddDays(7)
            ? FinanceDocumentDueStatuses.DueSoon
            : FinanceDocumentDueStatuses.NotDue;
    }

    private static decimal CalculatePaidAmount(decimal amount, decimal balance) =>
        Math.Max(0m, Math.Abs(amount) - Math.Abs(balance));

    private static bool IsSettled(decimal balance) =>
        Math.Abs(balance) <= 0.01m;

    private static bool IsCreditDocument(decimal amount, Dictionary<string, JsonElement>? data) =>
        amount < 0m ||
        ReadBool(data, "Credit") == true ||
        ReadBool(data, "CreditInvoice") == true ||
        ReadBool(data, "IsCredit") == true ||
        !string.IsNullOrWhiteSpace(ReadString(data, "CreditInvoiceReference"));

    private static string ResolveProcessingStatus(
        bool? paymentPending,
        bool? authorizePending,
        string? authorizerName,
        Dictionary<string, JsonElement>? data)
    {
        var effectiveAuthorizePending =
            authorizePending ??
            ReadBool(data, "AuthorizePending") ??
            ReadBool(data, "AuthorizationPending") ??
            ReadBool(data, "ApprovalPending") ??
            IsPendingAuthorizationText(ReadString(data, "AuthorizationStatus")) ??
            IsPendingAuthorizationText(ReadString(data, "ApprovalStatus")) ??
            IsPendingAuthorizationText(ReadString(data, "PaymentApprovalStatus"));

        if (effectiveAuthorizePending == true)
        {
            return FinanceDocumentProcessingStatuses.AuthorizationPending;
        }

        var effectivePaymentPending =
            paymentPending ??
            ReadBool(data, "PaymentPending") ??
            ReadBool(data, "PendingPayment");

        if (effectivePaymentPending == true)
        {
            return FinanceDocumentProcessingStatuses.PaymentPending;
        }

        return FinanceDocumentProcessingStatuses.None;
    }

    private static bool? IsPendingAuthorizationText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return normalized.Contains("pending", StringComparison.Ordinal) &&
            (normalized.Contains("author", StringComparison.Ordinal) || normalized.Contains("approv", StringComparison.Ordinal));
    }

    private static string BuildProviderStatus(
        bool? cancelled,
        bool? booked,
        bool? fullyPaid,
        decimal balance,
        bool isCreditNote,
        string? processingStatus,
        string? authorizerName,
        bool? sent)
    {
        var balanceText = balance.ToString("0.##", CultureInfo.InvariantCulture);
        var status = $"booked={FormatNullableBool(booked)};cancelled={FormatNullableBool(cancelled)};fullyPaid={FormatNullableBool(fullyPaid)};credit={isCreditNote.ToString().ToLowerInvariant()};balance={balanceText}";

        if (!string.IsNullOrWhiteSpace(processingStatus) &&
            !string.Equals(processingStatus, FinanceDocumentProcessingStatuses.None, StringComparison.OrdinalIgnoreCase))
        {
            status += $";processing={processingStatus}";
        }

        if (!string.IsNullOrWhiteSpace(authorizerName))
        {
            status += ";authorizer=present";
        }

        if (sent.HasValue)
        {
            status += $";sent={sent.Value.ToString().ToLowerInvariant()}";
        }

        return status;
    }

    private static JsonObject BuildSupplierInvoiceProviderMetadata(
        FortnoxSupplierInvoice invoice,
        decimal balance,
        bool isCreditNote,
        string processingStatus)
    {
        var metadata = new JsonObject
        {
            ["provider"] = FinanceIntegrationProviderKeys.Fortnox,
            ["entityType"] = "supplier_invoice",
            ["rawCancelled"] = JsonValue.Create(invoice.Cancelled),
            ["rawBooked"] = JsonValue.Create(invoice.Booked),
            ["rawFullyPaid"] = JsonValue.Create(invoice.FullyPaid),
            ["rawPaymentPending"] = JsonValue.Create(invoice.PaymentPending),
            ["rawAuthorizePending"] = JsonValue.Create(invoice.AuthorizePending),
            ["rawBalance"] = balance,
            ["isCreditNote"] = isCreditNote,
            ["normalizedProcessingStatus"] = processingStatus
        };

        AddRawString(metadata, invoice.AdditionalData, "Status", "rawStatus");
        AddRawString(metadata, invoice.AdditionalData, "ApprovalStatus", "rawApprovalStatus");
        AddRawString(metadata, invoice.AdditionalData, "AuthorizationStatus", "rawAuthorizationStatus");
        AddRawString(metadata, invoice.AdditionalData, "PaymentApprovalStatus", "rawPaymentApprovalStatus");
        AddRawString(metadata, invoice.AdditionalData, "CreditInvoiceReference", "rawCreditInvoiceReference");
        AddRawBool(metadata, invoice.AdditionalData, "Credit", "rawCredit");
        AddRawBool(metadata, invoice.AdditionalData, "CreditInvoice", "rawCreditInvoice");

        return metadata;
    }

    private static void AddRawString(JsonObject metadata, Dictionary<string, JsonElement>? data, string sourceName, string targetName)
    {
        var value = ReadString(data, sourceName);
        if (!string.IsNullOrWhiteSpace(value))
        {
            metadata[targetName] = value;
        }
    }

    private static void AddRawBool(JsonObject metadata, Dictionary<string, JsonElement>? data, string sourceName, string targetName)
    {
        var value = ReadBool(data, sourceName);
        if (value.HasValue)
        {
            metadata[targetName] = value.Value;
        }
    }

    private static string FormatNullableBool(bool? value) =>
        value.HasValue ? value.Value.ToString().ToLowerInvariant() : "null";

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

    private static decimal? ResolveAccountBalance(FortnoxAccount account) =>
        account.CurrentBalance ??
        account.Balance ??
        account.BalanceCarriedForward ??
        account.ClosingBalance ??
        ReadFlexibleDecimal(account.AdditionalData, "CurrentBalance") ??
        ReadFlexibleDecimal(account.AdditionalData, "Balance") ??
        ReadFlexibleDecimal(account.AdditionalData, "BalanceCarriedForward") ??
        ReadFlexibleDecimal(account.AdditionalData, "ClosingBalance") ??
        account.BalanceBroughtForward ??
        account.OpeningBalance ??
        ReadFlexibleDecimal(account.AdditionalData, "BalanceBroughtForward") ??
        ReadFlexibleDecimal(account.AdditionalData, "OpeningBalance");

    private static decimal? ReadFlexibleDecimal(Dictionary<string, JsonElement>? data, string name)
    {
        if (data is null || !data.TryGetValue(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.Number when element.TryGetDecimal(out var value) => value,
            JsonValueKind.String when decimal.TryParse(element.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) => value,
            _ => null
        };
    }

    private static bool? ReadBool(Dictionary<string, JsonElement>? data, string name)
    {
        if (data is null || !data.TryGetValue(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(element.GetString(), out var value) => value,
            _ => null
        };
    }

    private static string? ReadString(Dictionary<string, JsonElement>? data, string name)
    {
        if (data is null || !data.TryGetValue(name, out var element))
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => NormalizeOptional(element.GetString()),
            JsonValueKind.Number => element.TryGetInt64(out var integer)
                ? integer.ToString(CultureInfo.InvariantCulture)
                : element.GetDecimal().ToString(CultureInfo.InvariantCulture),
            _ => null
        };
    }
}
