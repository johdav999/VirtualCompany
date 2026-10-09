using VirtualCompany.Domain.Entities;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class PaymentDomainModelTests
{
    [Fact]
    public void Payment_constructor_normalizes_supported_values()
    {
        var payment = new Payment(
            Guid.Empty,
            Guid.NewGuid(),
            "Incoming",
            2450.25m,
            "usd",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            "bank-transfer",
            "Completed",
            "ACME-001");

        Assert.Equal("incoming", payment.PaymentType);
        Assert.Equal("USD", payment.Currency);
        Assert.Equal("bank_transfer", payment.Method);
        Assert.Equal("completed", payment.Status);
    }

    [Fact]
    public void Payment_constructor_rejects_non_positive_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Payment(
            Guid.Empty,
            Guid.NewGuid(),
            "incoming",
            0m,
            "USD",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            "bank_transfer",
            "completed",
            "ACME-002"));
    }

    [Fact]
    public void Payment_constructor_rejects_invalid_payment_type()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Payment(
            Guid.Empty,
            Guid.NewGuid(),
            "sideways",
            12m,
            "USD",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            "bank_transfer",
            "completed",
            "ACME-003"));
    }
}