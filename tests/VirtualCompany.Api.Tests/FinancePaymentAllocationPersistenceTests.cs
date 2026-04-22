using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePaymentAllocationPersistenceTests
{
    [Fact]
    public async Task EnsureCreated_maps_payment_allocations_and_settlement_status_columns()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var allocationColumns = await ReadColumnsAsync(connection, "payment_allocations");
        Assert.Contains("company_id", allocationColumns);
        Assert.Contains("payment_id", allocationColumns);
        Assert.Contains("invoice_id", allocationColumns);
        Assert.Contains("bill_id", allocationColumns);
        Assert.Contains("source_simulation_event_record_id", allocationColumns);
        Assert.Contains("payment_source_simulation_event_record_id", allocationColumns);
        Assert.Contains("target_source_simulation_event_record_id", allocationColumns);
        Assert.Contains("allocated_amount", allocationColumns);
        Assert.Contains("currency", allocationColumns);

        var allocationIndexes = await ReadIndexesAsync(connection, "payment_allocations");
        Assert.Contains("IX_payment_allocations_company_id_payment_id", allocationIndexes);
        Assert.Contains("IX_payment_allocations_company_id_invoice_id", allocationIndexes);
        Assert.Contains("IX_payment_allocations_company_id_bill_id", allocationIndexes);
        Assert.Contains("IX_payment_allocations_company_id_source_simulation_event_record_id", allocationIndexes);
        Assert.Contains("IX_payment_allocations_company_id_payment_source_simulation_event_record_id", allocationIndexes);
        Assert.Contains("IX_payment_allocations_company_id_target_source_simulation_event_record_id", allocationIndexes);

        var invoiceColumns = await ReadColumnsAsync(connection, "finance_invoices");
        var billColumns = await ReadColumnsAsync(connection, "finance_bills");
        Assert.Contains("settlement_status", invoiceColumns);
        Assert.Contains("settlement_status", billColumns);

        var cashDeltaColumns = await ReadColumnsAsync(connection, "simulation_cash_delta_records");
        Assert.Contains("company_id", cashDeltaColumns);
        Assert.Contains("simulation_event_record_id", cashDeltaColumns);
        Assert.Contains("simulation_date_at", cashDeltaColumns);
        Assert.Contains("source_entity_type", cashDeltaColumns);
        Assert.Contains("source_entity_id", cashDeltaColumns);
        Assert.Contains("cash_before", cashDeltaColumns);
        Assert.Contains("cash_delta", cashDeltaColumns);
        Assert.Contains("cash_after", cashDeltaColumns);

        var cashDeltaIndexes = await ReadIndexesAsync(connection, "simulation_cash_delta_records");
        Assert.Contains("IX_simulation_cash_delta_records_company_id_simulation_date_at", cashDeltaIndexes);
        Assert.Contains("IX_simulation_cash_delta_records_company_id_simulation_event_record_id", cashDeltaIndexes);
        Assert.Contains("IX_simulation_cash_delta_records_company_id_source_entity_type_source_entity_id", cashDeltaIndexes);
    }

    [Fact]
    public async Task EnsureCreated_enforces_single_target_check_constraint()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(Guid.NewGuid(), "Allocation Persistence Company");
        var counterparty = new FinanceCounterparty(Guid.NewGuid(), company.Id, "Persistence Counterparty", "customer", "counterparty@example.com");
        var payment = new Payment(
            Guid.NewGuid(),
            company.Id,
            PaymentTypes.Incoming,
            100m,
            "USD",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            "bank_transfer",
            "completed",
            "ALLOC-PERSIST-001");
        var invoice = new FinanceInvoice(
            Guid.NewGuid(),
            company.Id,
            counterparty.Id,
            "INV-PERSIST-001",
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            100m,
            "USD",
            "open");
        var bill = new FinanceBill(
            Guid.NewGuid(),
            company.Id,
            counterparty.Id,
            "BILL-PERSIST-001",
            new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            100m,
            "USD",
            "open");

        dbContext.Companies.Add(company);
        dbContext.FinanceCounterparties.Add(counterparty);
        dbContext.Payments.Add(payment);
        dbContext.FinanceInvoices.Add(invoice);
        dbContext.FinanceBills.Add(bill);
        await dbContext.SaveChangesAsync();

        var exception = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO payment_allocations (
                    id,
                    company_id,
                    payment_id,
                    invoice_id,
                    bill_id,
                    allocated_amount,
                    currency,
                    created_at,
                    updated_at)
                VALUES ($id, $companyId, $paymentId, $invoiceId, $billId, $amount, $currency, $createdAt, $updatedAt);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid());
            command.Parameters.AddWithValue("$companyId", company.Id);
            command.Parameters.AddWithValue("$paymentId", payment.Id);
            command.Parameters.AddWithValue("$invoiceId", invoice.Id);
            command.Parameters.AddWithValue("$billId", bill.Id);
            command.Parameters.AddWithValue("$amount", 50m);
            command.Parameters.AddWithValue("$currency", "USD");
            command.Parameters.AddWithValue("$createdAt", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
            command.Parameters.AddWithValue("$updatedAt", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
            await command.ExecuteNonQueryAsync();
        });

        Assert.Contains("CK_payment_allocations_single_target", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Allocation_trace_resolves_payment_target_document_and_originating_events()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(Guid.NewGuid(), "Allocation Trace Company");
        var counterparty = new FinanceCounterparty(Guid.NewGuid(), company.Id, "Trace Counterparty", "customer", "trace@example.com");
        var invoiceEvent = new SimulationEventRecord(Guid.NewGuid(), company.Id, Guid.NewGuid(), 73, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc), "finance.invoice.generated", "finance_invoice", Guid.NewGuid(), "INV-TRACE-001", null, 1, "inv-trace", null, null, null);
        var paymentEvent = new SimulationEventRecord(Guid.NewGuid(), company.Id, invoiceEvent.SimulationSessionId, 73, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc), "finance.cash_movement.generated", "finance_payment", Guid.NewGuid(), "PAY-TRACE-001", invoiceEvent.Id, 2, "pay-trace", 1000m, 100m, 1100m);
        var invoice = new FinanceInvoice(
            Guid.NewGuid(),
            company.Id,
            counterparty.Id,
            "INV-TRACE-001",
            new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            100m,
            "USD",
            "open",
            settlementStatus: FinanceSettlementStatuses.PartiallyPaid,
            sourceSimulationEventRecordId: invoiceEvent.Id);
        var payment = new Payment(
            Guid.NewGuid(),
            company.Id,
            PaymentTypes.Incoming,
            100m,
            "USD",
            new DateTime(2026, 4, 6, 0, 0, 0, DateTimeKind.Utc),
            "bank_transfer",
            PaymentStatuses.Completed,
            invoice.InvoiceNumber,
            sourceSimulationEventRecordId: paymentEvent.Id);
        var allocation = new PaymentAllocation(
            Guid.NewGuid(),
            company.Id,
            payment.Id,
            invoice.Id,
            null,
            100m,
            "USD",
            sourceSimulationEventRecordId: paymentEvent.Id,
            paymentSourceSimulationEventRecordId: paymentEvent.Id,
            targetSourceSimulationEventRecordId: invoiceEvent.Id);

        dbContext.Companies.Add(company);
        dbContext.SimulationEventRecords.AddRange(invoiceEvent, paymentEvent);
        dbContext.FinanceCounterparties.Add(counterparty);
        dbContext.FinanceInvoices.Add(invoice);
        dbContext.Payments.Add(payment);
        dbContext.PaymentAllocations.Add(allocation);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceReadService(dbContext);
        var trace = await service.GetAllocationTraceAsync(
            new GetFinancePaymentAllocationTraceQuery(company.Id, allocation.Id),
            CancellationToken.None);

        Assert.NotNull(trace);
        Assert.Equal(payment.Id, trace!.Payment.Id);
        Assert.Equal("invoice", trace.TargetDocument.TargetDocumentType);
        Assert.Equal(invoice.Id, trace.TargetDocument.Id);
        Assert.Equal(paymentEvent.Id, trace.PaymentSourceEvent!.Id);
        Assert.Equal(invoiceEvent.Id, trace.TargetSourceEvent!.Id);
        Assert.Equal(1000m, trace.PaymentSourceEvent.CashBefore);
        Assert.Equal(100m, trace.PaymentSourceEvent.CashDelta);
        Assert.Equal(1100m, trace.PaymentSourceEvent.CashAfter);
        Assert.Equal(paymentEvent.Id, trace.OriginatingSourceEvent!.Id);
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static async Task<HashSet<string>> ReadIndexesAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);
}