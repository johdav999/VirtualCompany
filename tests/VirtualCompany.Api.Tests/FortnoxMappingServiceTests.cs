using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
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
        Assert.Equal(1250.50m, result.PaidAmount);
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
}
