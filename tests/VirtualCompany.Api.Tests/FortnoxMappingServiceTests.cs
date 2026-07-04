using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using System.Text.Json;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FortnoxMappingServiceTests
{
    private readonly FortnoxMappingService _mapper = new();

    [Fact]
    public void Customer_mapping_normalizes_required_fields_optional_values_and_utc_cursor()
    {
        var result = _mapper.MapCustomer(new FortnoxCustomer
        {
            CustomerNumber = " 100 ",
            Name = " Acme AB ",
            Email = "finance@example.test",
            OrganisationNumber = "556677-8899",
            LastModified = "2026-04-30T08:15:00+02:00"
        });

        Assert.Equal("100", result.ExternalId);
        Assert.Equal("100", result.ExternalNumber);
        Assert.Equal("Acme AB", result.Name);
        Assert.Equal("customer", result.CounterpartyType);
        Assert.Equal("finance@example.test", result.Email);
        Assert.Equal("556677-8899", result.TaxId);
        Assert.Equal(new DateTime(2026, 4, 30, 6, 15, 0, DateTimeKind.Utc), result.ExternalUpdatedUtc);
    }

    [Fact]
    public void Invoice_mapping_handles_defaults_dates_decimal_currency_and_settlement_status()
    {
        var result = _mapper.MapInvoice(new FortnoxInvoice
        {
            DocumentNumber = "INV-1001",
            CustomerNumber = "C-1",
            CustomerName = null,
            InvoiceDate = "2026-04-01",
            DueDate = "2026-04-30",
            Total = 1250.50m,
            Balance = 0m,
            Currency = " sek ",
            Booked = true,
            LastModified = "2026-04-30 08:15"
        });

        Assert.Equal("INV-1001", result.ExternalId);
        Assert.Equal("Fortnox customer", result.CustomerName);
        Assert.Equal(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), result.IssuedUtc);
        Assert.Equal(new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc), result.DueUtc);
        Assert.Equal(1250.50m, result.Amount);
        Assert.Equal("SEK", result.Currency);
        Assert.Equal("paid", result.Status);
        Assert.Equal(FinanceSettlementStatuses.Paid, result.SettlementStatus);
        Assert.Equal(FinanceDocumentPostingStatuses.Booked, result.PostingStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, result.DueStatus);
        Assert.Equal(FinanceDocumentKinds.Invoice, result.DocumentKind);
        Assert.Equal(FinanceDocumentProcessingStatuses.None, result.ProcessingStatus);
        Assert.Contains("booked=true", result.ProviderStatus);
        Assert.Contains("fullyPaid=null", result.ProviderStatus);
        Assert.Equal(1250.50m, result.PaidAmount);
    }

    [Fact]
    public void Invoice_mapping_normalizes_draft_booked_overdue_partial_cancelled_and_credit_states()
    {
        var draft = _mapper.MapInvoice(new FortnoxInvoice
        {
            DocumentNumber = "INV-DRAFT",
            CustomerNumber = "C-1",
            InvoiceDate = "2099-04-01",
            DueDate = "2099-04-30",
            Total = 188m,
            Balance = 188m,
            Booked = false,
            FullyPaid = false
        });
        Assert.Equal("open", draft.Status);
        Assert.Equal(FinanceDocumentPostingStatuses.Draft, draft.PostingStatus);
        Assert.Equal(FinanceSettlementStatuses.Unpaid, draft.SettlementStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, draft.DueStatus);
        Assert.Equal(FinanceDocumentKinds.Invoice, draft.DocumentKind);

        var overdue = _mapper.MapInvoice(new FortnoxInvoice
        {
            DocumentNumber = "INV-OVERDUE",
            CustomerNumber = "C-1",
            InvoiceDate = "2020-04-01",
            DueDate = "2020-04-30",
            Total = 188m,
            Balance = 188m,
            Booked = true,
            FullyPaid = false
        });
        Assert.Equal("approved", overdue.Status);
        Assert.Equal(FinanceDocumentPostingStatuses.Booked, overdue.PostingStatus);
        Assert.Equal(FinanceSettlementStatuses.Unpaid, overdue.SettlementStatus);
        Assert.Equal(FinanceDocumentDueStatuses.Overdue, overdue.DueStatus);

        var partiallyPaid = _mapper.MapInvoice(new FortnoxInvoice
        {
            DocumentNumber = "INV-PART",
            CustomerNumber = "C-1",
            InvoiceDate = "2099-04-01",
            DueDate = "2099-04-30",
            Total = 1000m,
            Balance = 400m,
            Booked = true,
            FullyPaid = false
        });
        Assert.Equal(FinanceSettlementStatuses.PartiallyPaid, partiallyPaid.SettlementStatus);
        Assert.Equal(600m, partiallyPaid.PaidAmount);

        var cancelled = _mapper.MapInvoice(new FortnoxInvoice
        {
            DocumentNumber = "INV-CANCELLED",
            CustomerNumber = "C-1",
            InvoiceDate = "2099-04-01",
            DueDate = "2099-04-30",
            Total = 188m,
            Balance = 188m,
            Booked = true,
            Cancelled = true
        });
        Assert.Equal("void", cancelled.Status);
        Assert.Equal(FinanceDocumentPostingStatuses.Cancelled, cancelled.PostingStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, cancelled.DueStatus);

        var credit = _mapper.MapInvoice(new FortnoxInvoice
        {
            DocumentNumber = "INV-CREDIT",
            CustomerNumber = "C-1",
            InvoiceDate = "2099-04-01",
            DueDate = "2099-04-30",
            Total = -188m,
            Balance = -188m,
            Booked = true,
            AdditionalData = JsonData("""{"Credit":true}""")
        });
        Assert.Equal(FinanceDocumentKinds.CreditNote, credit.DocumentKind);
        Assert.Equal(FinanceSettlementStatuses.Credited, credit.SettlementStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, credit.DueStatus);
        Assert.Contains("credit=true", credit.ProviderStatus);
    }

    [Fact]
    public void Invoice_payment_mapping_keeps_unbooked_manual_payment_as_pending_allocation_signal()
    {
        var result = _mapper.MapInvoicePayment(new FortnoxInvoicePayment
        {
            Number = "8",
            InvoiceNumber = "5",
            Amount = 50000m,
            Currency = "sek",
            Booked = false,
            PaymentDate = "2026-05-19",
            LastModified = "2026-05-20T04:56:31Z"
        });

        Assert.Equal("8", result.ExternalId);
        Assert.Equal("5", result.InvoiceNumber);
        Assert.Equal(50000m, result.Amount);
        Assert.Equal("SEK", result.Currency);
        Assert.Equal(PaymentStatuses.Pending, result.Status);
        Assert.Equal(new DateTime(2026, 5, 19, 0, 0, 0, DateTimeKind.Utc), result.PaymentUtc);
        Assert.Equal(new DateTime(2026, 5, 20, 4, 56, 31, DateTimeKind.Utc), result.ExternalUpdatedUtc);
    }

    [Fact]
    public void Voucher_mapping_uses_invariant_numbers_and_safe_description_default()
    {
        var result = _mapper.MapVoucher(new FortnoxVoucher
        {
            VoucherSeries = "A",
            VoucherNumber = 42,
            VoucherDate = "2026-04-15",
            Total = -99.95m
        });

        Assert.Equal("A-42", result.ExternalId);
        Assert.Equal(new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc), result.TransactionUtc);
        Assert.Equal("Fortnox voucher A-42", result.Description);
        Assert.Equal(99.95m, result.Amount);
    }

    [Fact]
    public void Supplier_invoice_mapping_normalizes_paid_status_and_supplier_identity()
    {
        var result = _mapper.MapSupplierInvoice(new FortnoxSupplierInvoice
        {
            GivenNumber = "SI-42",
            SupplierNumber = "S-1",
            SupplierName = " Office AB ",
            InvoiceDate = "2026-04-10",
            DueDate = "2099-05-10",
            Total = 500m,
            Balance = 500m,
            Currency = "eur",
            Booked = true,
            LastModified = "2026-04-30T10:00:00Z"
        });

        Assert.Equal("SI-42", result.ExternalId);
        Assert.Equal("S-1", result.SupplierNumber);
        Assert.Equal("Office AB", result.SupplierName);
        Assert.Equal("EUR", result.Currency);
        Assert.Equal("approved", result.Status);
        Assert.Equal(FinanceSettlementStatuses.Unpaid, result.SettlementStatus);
        Assert.Equal(FinanceDocumentPostingStatuses.Booked, result.PostingStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, result.DueStatus);
        Assert.Equal(FinanceDocumentKinds.SupplierInvoice, result.DocumentKind);
        Assert.Equal(FinanceDocumentProcessingStatuses.None, result.ProcessingStatus);
        Assert.Equal(new DateTime(2026, 4, 30, 10, 0, 0, DateTimeKind.Utc), result.ExternalUpdatedUtc);
    }

    [Fact]
    public void Supplier_invoice_mapping_normalizes_credit_notes()
    {
        var result = _mapper.MapSupplierInvoice(new FortnoxSupplierInvoice
        {
            GivenNumber = "SI-CREDIT",
            SupplierNumber = "S-1",
            InvoiceDate = "2099-04-10",
            DueDate = "2099-05-10",
            Total = -500m,
            Balance = -500m,
            Currency = "SEK",
            Booked = true,
            AdditionalData = JsonData("""{"CreditInvoice":true}""")
        });

        Assert.Equal(FinanceDocumentKinds.SupplierCreditNote, result.DocumentKind);
        Assert.Equal(FinanceSettlementStatuses.Credited, result.SettlementStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, result.DueStatus);
        Assert.Contains("credit=true", result.ProviderStatus);
    }

    [Fact]
    public void Supplier_invoice_mapping_normalizes_lifecycle_states_and_preserves_raw_provider_metadata()
    {
        var posted = _mapper.MapSupplierInvoice(new FortnoxSupplierInvoice
        {
            GivenNumber = "SI-POSTED",
            SupplierNumber = "S-1",
            InvoiceDate = "2099-04-10",
            DueDate = "2099-05-10",
            Total = 500m,
            Balance = 500m,
            Currency = "SEK",
            Booked = true,
            FullyPaid = false
        });
        Assert.Equal("approved", posted.Status);
        Assert.Equal(FinanceDocumentPostingStatuses.Booked, posted.PostingStatus);
        Assert.Equal(FinanceSettlementStatuses.Unpaid, posted.SettlementStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, posted.DueStatus);
        Assert.Equal("fortnox", posted.ProviderMetadata?["provider"]?.GetValue<string>());
        Assert.Equal(false, posted.ProviderMetadata?["rawFullyPaid"]?.GetValue<bool>());

        var paid = _mapper.MapSupplierInvoice(new FortnoxSupplierInvoice
        {
            GivenNumber = "SI-PAID",
            SupplierNumber = "S-1",
            InvoiceDate = "2099-04-10",
            DueDate = "2099-05-10",
            Total = 500m,
            Balance = 0m,
            Currency = "SEK",
            Booked = true,
            FullyPaid = true
        });
        Assert.Equal("paid", paid.Status);
        Assert.Equal(FinanceSettlementStatuses.Paid, paid.SettlementStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, paid.DueStatus);
        Assert.Equal(500m, paid.PaidAmount);

        var overdue = _mapper.MapSupplierInvoice(new FortnoxSupplierInvoice
        {
            GivenNumber = "SI-OVERDUE",
            SupplierNumber = "S-1",
            InvoiceDate = "2020-04-10",
            DueDate = "2020-05-10",
            Total = 500m,
            Balance = 500m,
            Currency = "SEK",
            Booked = true,
            FullyPaid = false
        });
        Assert.Equal(FinanceSettlementStatuses.Unpaid, overdue.SettlementStatus);
        Assert.Equal(FinanceDocumentDueStatuses.Overdue, overdue.DueStatus);

        var cancelled = _mapper.MapSupplierInvoice(new FortnoxSupplierInvoice
        {
            GivenNumber = "SI-CANCELLED",
            SupplierNumber = "S-1",
            InvoiceDate = "2099-04-10",
            DueDate = "2099-05-10",
            Total = 500m,
            Balance = 500m,
            Currency = "SEK",
            Booked = true,
            Cancelled = true
        });
        Assert.Equal("void", cancelled.Status);
        Assert.Equal(FinanceDocumentPostingStatuses.Cancelled, cancelled.PostingStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, cancelled.DueStatus);
        Assert.Equal(true, cancelled.ProviderMetadata?["rawCancelled"]?.GetValue<bool>());

        var credited = _mapper.MapSupplierInvoice(new FortnoxSupplierInvoice
        {
            GivenNumber = "SI-CREDITED",
            SupplierNumber = "S-1",
            InvoiceDate = "2099-04-10",
            DueDate = "2099-05-10",
            Total = -500m,
            Balance = -500m,
            Currency = "SEK",
            Booked = true,
            AdditionalData = JsonData("""{"CreditInvoice":true,"Status":"CREDIT"}""")
        });
        Assert.Equal(FinanceDocumentKinds.SupplierCreditNote, credited.DocumentKind);
        Assert.Equal(FinanceSettlementStatuses.Credited, credited.SettlementStatus);
        Assert.Equal(FinanceDocumentDueStatuses.NotDue, credited.DueStatus);
        Assert.Equal(true, credited.ProviderMetadata?["rawCreditInvoice"]?.GetValue<bool>());
        Assert.Equal("CREDIT", credited.ProviderMetadata?["rawStatus"]?.GetValue<string>());
    }

    [Fact]
    public void Supplier_invoice_mapping_promotes_pending_payment_and_authorization_modes_to_processing_status()
    {
        var pendingPayment = _mapper.MapSupplierInvoice(new FortnoxSupplierInvoice
        {
            GivenNumber = "SI-PAYMENT",
            SupplierNumber = "S-1",
            InvoiceDate = "2099-04-10",
            DueDate = "2099-05-10",
            Total = 500m,
            Balance = 500m,
            Currency = "SEK",
            Booked = true,
            PaymentPending = true
        });

        Assert.Equal(FinanceDocumentProcessingStatuses.PaymentPending, pendingPayment.ProcessingStatus);
        Assert.Contains("processing=payment_pending", pendingPayment.ProviderStatus);

        var pendingAuthorization = _mapper.MapSupplierInvoice(new FortnoxSupplierInvoice
        {
            GivenNumber = "SI-AUTH",
            SupplierNumber = "S-1",
            InvoiceDate = "2099-04-10",
            DueDate = "2099-05-10",
            Total = 500m,
            Balance = 500m,
            Currency = "SEK",
            Booked = true,
            AdditionalData = JsonData("""{"AuthorizePending":true,"PaymentPending":true,"AuthorizerName":"Alex Approver"}""")
        });

        Assert.Equal(FinanceDocumentProcessingStatuses.AuthorizationPending, pendingAuthorization.ProcessingStatus);
        Assert.Contains("processing=authorization_pending", pendingAuthorization.ProviderStatus);
        Assert.Contains("authorizer=present", pendingAuthorization.ProviderStatus);
    }

    private static Dictionary<string, JsonElement> JsonData(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
}
