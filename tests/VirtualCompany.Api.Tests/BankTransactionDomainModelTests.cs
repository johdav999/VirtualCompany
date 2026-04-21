using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BankTransactionDomainModelTests
{
    [Fact]
    public void ApplyReconciliation_transitions_between_supported_statuses()
    {
        var transaction = new BankTransaction(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            150m,
            "USD",
            "Customer remittance",
            "Northwind Analytics");

        Assert.Equal(BankTransactionReconciliationStatuses.Unreconciled, transaction.Status);
        Assert.Equal(0m, transaction.ReconciledAmount);

        transaction.ApplyReconciliation(100m, new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(BankTransactionReconciliationStatuses.PartiallyReconciled, transaction.Status);
        Assert.Equal(100m, transaction.ReconciledAmount);

        transaction.ApplyReconciliation(150m, new DateTime(2026, 4, 22, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(BankTransactionReconciliationStatuses.Reconciled, transaction.Status);
        Assert.Equal(150m, transaction.ReconciledAmount);
    }

    [Fact]
    public void ApplyReconciliation_rejects_amounts_above_transaction_total()
    {
        var transaction = new BankTransaction(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            -80m,
            "USD",
            "Supplier payout",
            "Contoso Supplies");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            transaction.ApplyReconciliation(80.01m, new DateTime(2026, 4, 21, 0, 0, 0, DateTimeKind.Utc)));
    }
}