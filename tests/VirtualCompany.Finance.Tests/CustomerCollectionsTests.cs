using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class CustomerCollectionsTests
{
    [Fact]
    public void Policy_and_case_domain_rules_disable_unsupported_charges_and_retain_explicit_workflow_state()
    {
        var now = new DateTime(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);
        var policy = new CustomerCollectionPolicy(Guid.NewGuid(), Guid.NewGuid(), 3, 10m, "sv-SE", true, now);
        Assert.Throws<InvalidOperationException>(() => policy.Update(3, 10m, "sv-SE", true, true, false, now));

        var collectionCase = new CustomerCollectionCase(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), now);
        collectionCase.RecordDispute(50m, "Quantity is disputed.", Guid.NewGuid(), now.AddDays(2), now);
        Assert.True(collectionCase.IsOnHold);
        Assert.Equal(CustomerCollectionCaseStatuses.Disputed, collectionCase.Status);
        collectionCase.ResolveDispute("Customer accepted the corrected evidence.", now.AddHours(1));
        collectionCase.RecordPromise(50m, new DateOnly(2026, 8, 30), Guid.NewGuid(), now.AddDays(4), now.AddHours(2));
        collectionCase.ResolvePromise(false, "The promised date passed without payment.", now.AddDays(5));

        Assert.False(collectionCase.IsOnHold);
        Assert.Equal("broken", collectionCase.PromiseStatus);
        Assert.Equal(CustomerCollectionCaseStatuses.Open, collectionCase.Status);
        Assert.Equal(4, collectionCase.Version);
    }

    [Fact]
    public void Worker_lease_is_exclusive_retries_with_a_backoff_and_requires_an_explicit_reset_after_blocking()
    {
        var now = new DateTime(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);
        var lease = new CustomerCollectionWorkerLease(Guid.NewGuid(), Guid.NewGuid(), now);

        Assert.True(lease.TryClaim("worker-a", now, TimeSpan.FromMinutes(2)));
        Assert.False(lease.TryClaim("worker-b", now.AddMinutes(1), TimeSpan.FromMinutes(2)));
        lease.Retry("worker-a", "transient_failure", "Mailbox dependency was unavailable.", now.AddMinutes(5), false, now.AddMinutes(1));
        Assert.False(lease.TryClaim("worker-b", now.AddMinutes(4), TimeSpan.FromMinutes(2)));
        Assert.True(lease.TryClaim("worker-b", now.AddMinutes(5), TimeSpan.FromMinutes(2)));
        lease.Retry("worker-b", "retry_exhausted", "The bounded retry budget was exhausted.", now.AddMinutes(10), true, now.AddMinutes(6));
        Assert.True(lease.IsBlocked);
        Assert.False(lease.TryClaim("worker-a", now.AddHours(1), TimeSpan.FromMinutes(2)));

        lease.Reset(now.AddHours(1));
        Assert.False(lease.IsBlocked);
        Assert.True(lease.TryClaim("worker-a", now.AddHours(1), TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public async Task Aging_and_statement_reconcile_allocations_and_do_not_cross_company_scope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using var db = new VirtualCompanyDbContext(options); await db.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid(); var otherCompanyId = Guid.NewGuid(); var customerId = Guid.NewGuid(); var otherCustomerId = Guid.NewGuid(); var actorId = Guid.NewGuid();
        var issued = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc); var due = issued.AddDays(14);
        var invoice = new FinanceInvoice(Guid.NewGuid(), companyId, customerId, "INV-100", issued, due, 100m, "SEK", "approved", authority: "native");
        var hiddenInvoice = new FinanceInvoice(Guid.NewGuid(), otherCompanyId, otherCustomerId, "OTHER-100", issued, due, 900m, "SEK", "approved", authority: "native");
        var payment = new Payment(Guid.NewGuid(), companyId, PaymentTypes.Incoming, 25m, "SEK", issued.AddDays(20), PaymentMethods.BankTransfer, PaymentStatuses.Completed, "BANK-25");
        db.Companies.AddRange(new Company(companyId, "Collections Company"), new Company(otherCompanyId, "Other Company"));
        db.FinanceCounterparties.AddRange(new FinanceCounterparty(customerId, companyId, "Northwind", "customer", "billing@northwind.example"), new FinanceCounterparty(otherCustomerId, otherCompanyId, "Hidden", "customer"));
        db.FinanceInvoices.AddRange(invoice, hiddenInvoice); db.Payments.Add(payment);
        db.PaymentAllocations.Add(new PaymentAllocation(Guid.NewGuid(), companyId, payment.Id, invoice.Id, null, 25m, "SEK"));
        await db.SaveChangesAsync();
        var service = new CustomerCollectionsService(db, new AccountingStub(companyId), new OutboxStub(), new AuditStub());

        var aging = await service.GetAgingAsync(new(companyId, new DateOnly(2026, 8, 26), "UTC"), default);
        var statement = await service.GenerateStatementAsync(new(companyId, customerId, new DateOnly(2026, 6, 1),
            new DateOnly(2026, 8, 26), "UTC", "en-US", "SEK", "statement-1", actorId), default);
        var replay = await service.GenerateStatementAsync(new(companyId, customerId, new DateOnly(2026, 6, 1),
            new DateOnly(2026, 8, 26), "UTC", "en-US", "SEK", "statement-1", actorId), default);

        var item = Assert.Single(aging.Items);
        Assert.Equal(invoice.Id, item.InvoiceId); Assert.Equal(25m, item.AllocatedAmount); Assert.Equal(75m, item.OpenAmount);
        Assert.Equal(75m, aging.TotalOpen); Assert.DoesNotContain(aging.Items, x => x.InvoiceId == hiddenInvoice.Id);
        Assert.Null(item.FunctionalOpenAmount); Assert.Null(aging.FunctionalTotalOpen);
        Assert.Equal(0m, statement.OpeningBalance); Assert.Equal(100m, statement.InvoiceActivity); Assert.Equal(25m, statement.AllocationActivity); Assert.Equal(75m, statement.ClosingBalance);
        Assert.Equal("legacy_or_imported_unavailable", statement.FunctionalEvidenceStatus);
        Assert.Null(statement.FunctionalClosingBalance);
        Assert.All(statement.Items, row => Assert.Null(row.FunctionalRunningBalance));
        Assert.Equal(statement.Checksum, replay.Checksum); Assert.True(replay.IsIdempotentReplay);
        var hidden = await Assert.ThrowsAsync<CustomerCollectionException>(() => service.GetStatementAsync(new(otherCompanyId, statement.Id), default));
        Assert.Equal(CustomerCollectionReasonCodes.NotFound, hidden.ReasonCode);

        var createdPolicy = await service.UpsertPolicyAsync(new(companyId, null, 0, 1m, "en-US", false, false, false,
            [new CustomerCollectionPolicyStageInput(1, 1, "email", "polite-reminder", false)], actorId), default);
        var draft = await service.PrepareReminderAsync(new(companyId, invoice.Id, 1, statement.Id, "reminder-1", actorId), default);
        var finalPayment = new Payment(Guid.NewGuid(), companyId, PaymentTypes.Incoming, 75m, "SEK", issued.AddDays(30), PaymentMethods.BankTransfer, PaymentStatuses.Completed, "BANK-75");
        db.Payments.Add(finalPayment); db.PaymentAllocations.Add(new PaymentAllocation(Guid.NewGuid(), companyId,
            finalPayment.Id, invoice.Id, null, 75m, "SEK")); await db.SaveChangesAsync();
        var stale = await Assert.ThrowsAsync<CustomerCollectionException>(() => service.SendReminderAsync(new(companyId,
            draft.Id, draft.Version, draft.SourceHash, "send-1", actorId), default));
        Assert.Equal(CustomerCollectionReasonCodes.StaleEvidence, stale.ReasonCode);
        Assert.Empty(await db.CustomerReminderDeliveries.IgnoreQueryFilters().ToListAsync());
        await service.UpsertPolicyAsync(new(companyId, createdPolicy.Version, 0, 1m, "en-US", false, false, false,
            [new CustomerCollectionPolicyStageInput(1, 1, "email", "polite-reminder", false)], actorId, null,
            [new CustomerCollectionPolicyExceptionInput(customerId, "Managed directly by legal counsel.")]), default);
        var excepted = await Assert.ThrowsAsync<CustomerCollectionException>(() => service.PrepareReminderAsync(new(companyId,
            invoice.Id, 1, statement.Id, "reminder-excepted", actorId), default));
        Assert.Equal(CustomerCollectionReasonCodes.CollectionOnHold, excepted.ReasonCode);
    }

    [Fact]
    public async Task Worker_repeat_run_prepares_one_governed_draft_and_task_without_sending_or_crossing_tenants()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
        await using var db = new VirtualCompanyDbContext(options); await db.Database.EnsureCreatedAsync();
        var companyId = Guid.NewGuid(); var otherCompanyId = Guid.NewGuid(); var actorId = Guid.NewGuid();
        var customerId = Guid.NewGuid(); var otherCustomerId = Guid.NewGuid();
        db.Users.Add(new User(actorId, "collections-owner@example.test", "Collections owner", "test", actorId.ToString("N")));
        db.Companies.AddRange(new Company(companyId, "Worker company"), new Company(otherCompanyId, "Other company"));
        db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, actorId,
            CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        db.FinanceCounterparties.AddRange(
            new FinanceCounterparty(customerId, companyId, "Worker customer", "customer", "billing@worker.example"),
            new FinanceCounterparty(otherCustomerId, otherCompanyId, "Other customer", "customer", "billing@other.example"));
        var issued = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        db.FinanceInvoices.AddRange(
            new FinanceInvoice(Guid.NewGuid(), companyId, customerId, "WORK-100", issued, issued.AddDays(14), 100m, "SEK", "approved", authority: "native"),
            new FinanceInvoice(Guid.NewGuid(), otherCompanyId, otherCustomerId, "OTHER-900", issued, issued.AddDays(14), 900m, "SEK", "approved", authority: "native"));
        var policy = new CustomerCollectionPolicy(Guid.NewGuid(), companyId, 0, 1m, "en-US", false, issued);
        db.CustomerCollectionPolicies.Add(policy);
        db.CustomerCollectionPolicyStages.Add(new CustomerCollectionPolicyStage(Guid.NewGuid(), companyId, policy.Id,
            1, 1, "email", "polite-reminder", false));
        await db.SaveChangesAsync();
        var service = new CustomerCollectionsService(db, new AccountingStub(companyId), new OutboxStub(), new AuditStub());
        var runner = new CustomerCollectionWorkerRunner(db, service,
            Options.Create(new CustomerCollectionWorkerOptions { Enabled = true, BatchSize = 100 }),
            NullLogger<CustomerCollectionWorkerRunner>.Instance);
        var asOf = new DateTime(2026, 8, 26, 8, 0, 0, DateTimeKind.Utc);

        var first = await runner.RunAsync(new(asOf, 100, companyId), default);
        var second = await runner.RunAsync(new(asOf, 100, companyId), default);

        Assert.Equal(1, first.DraftsPrepared); Assert.Equal(1, first.TasksCreated); Assert.Equal(1, first.CasesCreated);
        Assert.Equal(0, second.DraftsPrepared); Assert.Equal(0, second.TasksCreated); Assert.Equal(0, second.CasesCreated);
        Assert.Single(await db.CustomerReminderDrafts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync());
        Assert.Single(await db.WorkTasks.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync());
        Assert.Empty(await db.CustomerReminderDeliveries.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await db.CustomerCollectionCases.IgnoreQueryFilters().Where(x => x.CompanyId == otherCompanyId).ToListAsync());
    }

    private sealed class AuditStub : IAuditEventWriter { public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask; }
    private sealed class OutboxStub : ICompanyOutboxEnqueuer
    { public void Enqueue(Guid companyId, string topic, object payload, string? correlationId = null, DateTime? availableAtUtc = null, string? idempotencyKey = null, string? messageType = null, string? causationId = null, IReadOnlyDictionary<string, string?>? headers = null) { } }
    private sealed class AccountingStub(Guid companyId) : ICustomerInvoiceAccountingService
    {
        public Task<CustomerInvoiceReceivableReconciliationDto> ReconcileAsync(GetCustomerInvoiceReceivableReconciliationQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new CustomerInvoiceReceivableReconciliationDto(companyId, "SEK", 100m, 100m, 25m, 75m, 0m, true, DateTime.UtcNow));
        public Task<CustomerInvoiceAccountingPreviewDto> PreviewAsync(PreviewCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerInvoiceAccountingReferenceDataDto> GetReferenceDataAsync(GetCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerInvoiceAccountingSubmissionResult> SubmitAsync(SubmitCustomerInvoiceAccountingCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerInvoiceAccountingPostingResult> PostAsync(PostCustomerInvoiceAccountingCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerInvoiceAccountingStateDto> GetAsync(GetCustomerInvoiceAccountingQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CustomerInvoiceAccountingStateDto> CreateCreditNoteAsync(CreateCustomerCreditNoteCommand command, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
