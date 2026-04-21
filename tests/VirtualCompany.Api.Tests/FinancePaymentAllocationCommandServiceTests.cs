using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePaymentAllocationCommandServiceTests
{
    [Fact]
    public async Task Create_allocation_rejects_payment_over_allocation()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedAllocationScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);
        await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceAId, null, 70m, "USD")),
            CancellationToken.None);

        await Assert.ThrowsAsync<FinanceValidationException>(() =>
            service.CreateAllocationAsync(
                new CreateFinancePaymentAllocationCommand(
                    companyId,
                    new CreateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceBId, null, 31m, "USD")),
                CancellationToken.None));
    }

    [Fact]
    public async Task Create_allocation_rejects_target_over_allocation_and_currency_mismatch()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedAllocationScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);
        await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceAId, null, 70m, "USD")),
            CancellationToken.None);

        await Assert.ThrowsAsync<FinanceValidationException>(() =>
            service.CreateAllocationAsync(
                new CreateFinancePaymentAllocationCommand(
                    companyId,
                    new CreateFinancePaymentAllocationDto(seed.IncomingPaymentBId, seed.InvoiceAId, null, 1m, "USD")),
                CancellationToken.None));

        await Assert.ThrowsAsync<FinanceValidationException>(() =>
            service.CreateAllocationAsync(
                new CreateFinancePaymentAllocationCommand(
                    companyId,
                    new CreateFinancePaymentAllocationDto(seed.OutgoingPaymentId, null, seed.BillId, 20m, "EUR")),
                CancellationToken.None));
    }

    [Fact]
    public async Task Allocation_create_update_and_delete_recalculate_settlement_status()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedAllocationScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        var created = await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceAId, null, 40.25m, "usd")),
            CancellationToken.None);

        Assert.Equal(40.25m, created.AllocatedAmount);
        Assert.Equal(
            FinanceSettlementStatuses.PartiallyPaid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceAId).Select(x => x.SettlementStatus).SingleAsync());

        await service.UpdateAllocationAsync(
            new UpdateFinancePaymentAllocationCommand(
                companyId,
                created.Id,
                new UpdateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceAId, null, 70m, "USD")),
            CancellationToken.None);

        Assert.Equal(
            FinanceSettlementStatuses.Paid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceAId).Select(x => x.SettlementStatus).SingleAsync());

        await service.DeleteAllocationAsync(new DeleteFinancePaymentAllocationCommand(companyId, created.Id), CancellationToken.None);

        Assert.Equal(
            FinanceSettlementStatuses.Unpaid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceAId).Select(x => x.SettlementStatus).SingleAsync());
    }

    [Fact]
    public async Task Allocations_support_one_payment_to_many_documents_and_many_payments_to_one_document()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedAllocationScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceAId, null, 70m, "USD")),
            CancellationToken.None);

        await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceBId, null, 30m, "USD")),
            CancellationToken.None);

        await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.IncomingPaymentBId, seed.InvoiceBId, null, 30m, "USD")),
            CancellationToken.None);

        await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.OutgoingPaymentId, null, seed.BillId, 50m, "USD")),
            CancellationToken.None);

        Assert.Equal(4, await dbContext.PaymentAllocations.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId));
        Assert.Equal(
            FinanceSettlementStatuses.Paid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceAId).Select(x => x.SettlementStatus).SingleAsync());
        Assert.Equal(
            FinanceSettlementStatuses.Paid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceBId).Select(x => x.SettlementStatus).SingleAsync());
        Assert.Equal(
            FinanceSettlementStatuses.Paid,
            await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.Id == seed.BillId).Select(x => x.SettlementStatus).SingleAsync());
    }

    [Fact]
    public async Task Partial_allocations_persist_exact_amounts_when_reloaded()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedAllocationScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        var invoiceAllocation = await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceAId, null, 40.25m, "usd")),
            CancellationToken.None);

        var billAllocation = await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.OutgoingPaymentId, null, seed.BillId, 20.10m, "usd")),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var persistedAllocations = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync();

        Assert.Contains(
            persistedAllocations,
            allocation => allocation.Id == invoiceAllocation.Id &&
                          allocation.InvoiceId == seed.InvoiceAId &&
                          allocation.BillId is null &&
                          allocation.AllocatedAmount == 40.25m &&
                          allocation.Currency == "USD");
        Assert.Contains(
            persistedAllocations,
            allocation => allocation.Id == billAllocation.Id &&
                          allocation.BillId == seed.BillId &&
                          allocation.InvoiceId is null &&
                          allocation.AllocatedAmount == 20.10m &&
                          allocation.Currency == "USD");
        Assert.Equal(
            FinanceSettlementStatuses.PartiallyPaid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceAId).Select(x => x.SettlementStatus).SingleAsync());
        Assert.Equal(
            FinanceSettlementStatuses.Paid,
            await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.Id == seed.BillId).Select(x => x.SettlementStatus).SingleAsync());
    }

    [Fact]
    public async Task Allocation_updates_preserve_exact_partial_amounts_and_recalculate_bill_status()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedAllocationScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        var created = await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.OutgoingPaymentId, null, seed.BillId, 20.10m, "usd")),
            CancellationToken.None);

        Assert.Equal(20.10m, created.AllocatedAmount);
        Assert.Equal(
            FinanceSettlementStatuses.PartiallyPaid,
            await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.Id == seed.BillId).Select(x => x.SettlementStatus).SingleAsync());

        await service.UpdateAllocationAsync(
            new UpdateFinancePaymentAllocationCommand(
                companyId,
                created.Id,
                new UpdateFinancePaymentAllocationDto(seed.OutgoingPaymentId, null, seed.BillId, 15.55m, "USD")),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var persisted = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Id == created.Id);

        Assert.Equal(15.55m, persisted.AllocatedAmount);
        Assert.Equal("USD", persisted.Currency);
        Assert.Equal(seed.BillId, persisted.BillId);
        Assert.Null(persisted.InvoiceId);
        Assert.Equal(
            FinanceSettlementStatuses.PartiallyPaid,
            await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.Id == seed.BillId).Select(x => x.SettlementStatus).SingleAsync());

        await service.UpdateAllocationAsync(
            new UpdateFinancePaymentAllocationCommand(
                companyId,
                movedAllocation.Id,
                new UpdateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceAId, null, 40m, "usd")),
            CancellationToken.None);

        dbContext.ChangeTracker.Clear();

        var persistedMovedAllocation = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.Id == movedAllocation.Id);

        Assert.Equal(seed.InvoiceAId, persistedMovedAllocation.InvoiceId);
        Assert.Null(persistedMovedAllocation.BillId);
        Assert.Equal(40m, persistedMovedAllocation.AllocatedAmount);
        Assert.Equal("USD", persistedMovedAllocation.Currency);
        Assert.Equal(
            FinanceSettlementStatuses.Paid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceAId).Select(x => x.SettlementStatus).SingleAsync());
        Assert.Equal(
            FinanceSettlementStatuses.Unpaid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == seed.InvoiceBId).Select(x => x.SettlementStatus).SingleAsync());
        Assert.Equal(
            70m,
            await dbContext.PaymentAllocations.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.InvoiceId == seed.InvoiceAId).SumAsync(x => x.AllocatedAmount));
        Assert.Equal(
            0m,
            await dbContext.PaymentAllocations.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.InvoiceId == seed.InvoiceBId).SumAsync(x => x.AllocatedAmount));
    }

    [Fact]
    public async Task Updating_allocation_target_recalculates_previous_and_new_document_statuses()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();
        var seed = SeedAllocationScenario(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.IncomingPaymentBId, seed.InvoiceAId, null, 30m, "USD")),
            CancellationToken.None);

        var movedAllocation = await service.CreateAllocationAsync(
            new CreateFinancePaymentAllocationCommand(
                companyId,
                new CreateFinancePaymentAllocationDto(seed.IncomingPaymentId, seed.InvoiceBId, null, 40m, "USD")),
            CancellationToken.None);

        Assert.Equal(
            FinanceSettlementStatuses.PartiallyPaid,
            await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.Id == seed.BillId).Select(x => x.SettlementStatus).SingleAsync());
    }

    [Fact]
    public async Task Backfill_creates_allocations_for_matching_and_synthesized_payments_and_is_idempotent()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Backfill Allocation Company");
        var counterpartyId = Guid.NewGuid();
        var paidInvoiceId = Guid.NewGuid();
        var paidBillId = Guid.NewGuid();
        var openInvoiceId = Guid.NewGuid();
        var matchedPaymentId = Guid.NewGuid();

        dbContext.Companies.Add(company);
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(counterpartyId, companyId, "Backfill Counterparty", "customer", "finance@example.com"));
        dbContext.FinanceInvoices.AddRange(
            new FinanceInvoice(
                paidInvoiceId,
                companyId,
                counterpartyId,
                "INV-BACKFILL-001",
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                125m,
                "USD",
                "paid"),
            new FinanceInvoice(
                openInvoiceId,
                companyId,
                counterpartyId,
                "INV-BACKFILL-OPEN",
                new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
                50m,
                "USD",
                "open"));
        dbContext.FinanceBills.Add(new FinanceBill(
            paidBillId,
            companyId,
            counterpartyId,
            "BILL-BACKFILL-001",
            new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            80m,
            "USD",
            "paid"));
        dbContext.Payments.Add(new Payment(
            matchedPaymentId,
            companyId,
            PaymentTypes.Incoming,
            125m,
            "USD",
            new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc),
            "bank_transfer",
            "completed",
            "INV-BACKFILL-001"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        var first = await service.BackfillAllocationsAsync(
            new BackfillFinancePaymentAllocationsCommand(companyId, true),
            CancellationToken.None);
        var second = await service.BackfillAllocationsAsync(
            new BackfillFinancePaymentAllocationsCommand(companyId, true),
            CancellationToken.None);

        Assert.Equal(2, first.CreatedAllocationCount);
        Assert.Equal(1, first.CreatedPaymentCount);
        Assert.Equal(0, second.CreatedAllocationCount);
        Assert.Equal(0, second.CreatedPaymentCount);

        var allocations = await dbContext.PaymentAllocations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();
        var invoiceAllocation = Assert.Single(allocations.Where(x => x.InvoiceId == paidInvoiceId));
        var billAllocation = Assert.Single(allocations.Where(x => x.BillId == paidBillId));
        var payments = await dbContext.Payments
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.CreatedUtc)
            .ToListAsync();
        var synthesizedPayment = Assert.Single(payments.Where(x => x.Id != matchedPaymentId));

        Assert.Equal(matchedPaymentId, invoiceAllocation.PaymentId);
        Assert.Equal(125m, invoiceAllocation.AllocatedAmount);
        Assert.Equal("USD", invoiceAllocation.Currency);
        Assert.Equal(synthesizedPayment.Id, billAllocation.PaymentId);
        Assert.Equal(80m, billAllocation.AllocatedAmount);
        Assert.Equal("USD", billAllocation.Currency);
        Assert.Equal(PaymentTypes.Outgoing, synthesizedPayment.PaymentType);
        Assert.Equal(80m, synthesizedPayment.Amount);
        Assert.Equal("BILL-BACKFILL-001", synthesizedPayment.CounterpartyReference);
        Assert.Equal(2, await dbContext.PaymentAllocations.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId));
        Assert.Equal(2, await dbContext.Payments.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId));
        Assert.Equal(
            FinanceSettlementStatuses.Paid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == paidInvoiceId).Select(x => x.SettlementStatus).SingleAsync());
        Assert.Equal(
            FinanceSettlementStatuses.Paid,
            await dbContext.FinanceBills.IgnoreQueryFilters().Where(x => x.Id == paidBillId).Select(x => x.SettlementStatus).SingleAsync());
        Assert.Equal(
            FinanceSettlementStatuses.Unpaid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == openInvoiceId).Select(x => x.SettlementStatus).SingleAsync());
    }

    [Fact]
    public async Task Backfill_uses_multiple_matching_payments_before_synthesizing_missing_payments()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        var accessor = new TestCompanyContextAccessor(companyId);
        await using var dbContext = CreateContext(connection, accessor);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Backfill Multi Payment Company");
        var counterpartyId = Guid.NewGuid();
        var paidInvoiceId = Guid.NewGuid();

        dbContext.Companies.Add(company);
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(counterpartyId, companyId, "Backfill Counterparty", "customer", "finance@example.com"));
        dbContext.FinanceInvoices.Add(new FinanceInvoice(
            paidInvoiceId,
            companyId,
            counterpartyId,
            "INV-BACKFILL-SPLIT-001",
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
            125m,
            "USD",
            "paid"));
        dbContext.Payments.AddRange(
            new Payment(
                Guid.NewGuid(),
                companyId,
                PaymentTypes.Incoming,
                75m,
                "USD",
                new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                "completed",
                "INV-BACKFILL-SPLIT-001"),
            new Payment(
                Guid.NewGuid(),
                companyId,
                PaymentTypes.Incoming,
                50m,
                "USD",
                new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                "completed",
                "INV-BACKFILL-SPLIT-001"));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceCommandService(dbContext, accessor);

        var result = await service.BackfillAllocationsAsync(
            new BackfillFinancePaymentAllocationsCommand(companyId, true),
            CancellationToken.None);

        Assert.Equal(2, result.CreatedAllocationCount);
        Assert.Equal(0, result.CreatedPaymentCount);
        Assert.Equal(125m, await dbContext.PaymentAllocations.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.InvoiceId == paidInvoiceId).SumAsync(x => x.AllocatedAmount));
        Assert.Equal(
            FinanceSettlementStatuses.Paid,
            await dbContext.FinanceInvoices.IgnoreQueryFilters().Where(x => x.Id == paidInvoiceId).Select(x => x.SettlementStatus).SingleAsync());
    }

    private static AllocationSeed SeedAllocationScenario(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        var counterpartyId = Guid.NewGuid();
        var invoiceAId = Guid.NewGuid();
        var invoiceBId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        var incomingPaymentId = Guid.NewGuid();
        var incomingPaymentBId = Guid.NewGuid();
        var outgoingPaymentId = Guid.NewGuid();

        dbContext.Companies.Add(new Company(companyId, "Allocation Command Company"));
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(counterpartyId, companyId, "Allocation Counterparty", "customer", "counterparty@example.com"));
        dbContext.FinanceInvoices.AddRange(
            new FinanceInvoice(
                invoiceAId,
                companyId,
                counterpartyId,
                "INV-ALLOC-001",
                new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                70m,
                "USD",
                "open"),
            new FinanceInvoice(
                invoiceBId,
                companyId,
                counterpartyId,
                "INV-ALLOC-002",
                new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
                60m,
                "USD",
                "open"));
        dbContext.FinanceBills.Add(new FinanceBill(
            billId,
            companyId,
            counterpartyId,
            "BILL-ALLOC-001",
            new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            50m,
            "USD",
            "open"));
        dbContext.Payments.AddRange(
            new Payment(
                incomingPaymentId,
                companyId,
                PaymentTypes.Incoming,
                100m,
                "USD",
                new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                "completed",
                "ALLOCATION-INCOMING-A"),
            new Payment(
                incomingPaymentBId,
                companyId,
                PaymentTypes.Incoming,
                30m,
                "USD",
                new DateTime(2026, 4, 11, 0, 0, 0, DateTimeKind.Utc),
                "bank_transfer",
                "completed",
                "ALLOCATION-INCOMING-B"),
            new Payment(
                outgoingPaymentId,
                companyId,
                PaymentTypes.Outgoing,
                50m,
                "USD",
                new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc),
                "ach",
                "completed",
                "ALLOCATION-OUTGOING-A"));

        return new AllocationSeed(invoiceAId, invoiceBId, billId, incomingPaymentId, incomingPaymentBId, outgoingPaymentId);
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection, ICompanyContextAccessor accessor) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options,
            accessor);

    private sealed record AllocationSeed(
        Guid InvoiceAId,
        Guid InvoiceBId,
        Guid BillId,
        Guid IncomingPaymentId,
        Guid IncomingPaymentBId,
        Guid OutgoingPaymentId);

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid companyId)
        {
            CompanyId = companyId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IsResolved => Membership is not null;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }

        public void SetCompanyId(Guid? companyId)
        {
            CompanyId = companyId;
            Membership = null;
            UserId = null;
        }

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}