using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class SupplierSubscriptionServiceTests
{
    [Fact]
    public void Monthly_subscription_advances_from_month_end_without_drifting()
    {
        var subscription = CreateSubscription(nextExpectedBillDateUtc: new DateTime(2026, 1, 31, 12, 0, 0, DateTimeKind.Utc));
        subscription.Activate();

        subscription.AdvanceAfterConfirmedBill(new DateTime(2026, 1, 31, 8, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc), subscription.NextExpectedBillDateUtc);

        subscription.AdvanceAfterConfirmedBill(new DateTime(2026, 2, 28, 8, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc), subscription.NextExpectedBillDateUtc);
    }

    [Theory]
    [InlineData(0, 0, 5, "monthly")]
    [InlineData(100, -1, 5, "monthly")]
    [InlineData(100, 0, -1, "monthly")]
    [InlineData(100, 0, 5, "weekly")]
    public void Invalid_terms_are_rejected(decimal expectedAmount, decimal amountTolerance, int dateToleranceDays, string cadence)
    {
        Assert.ThrowsAny<ArgumentException>(() => CreateSubscription(
            expectedAmount: expectedAmount,
            amountTolerance: amountTolerance,
            dateToleranceDays: dateToleranceDays,
            cadence: cadence));
    }

    [Fact]
    public async Task Eligible_bill_is_confirmed_once_and_advances_schedule_once()
    {
        await using var fixture = await SupplierSubscriptionFixture.CreateAsync();
        var companyId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        await fixture.SeedCompanySupplierBillAndSubscriptionAsync(companyId, supplierId, billId, active: true);

        var service = fixture.CreateService();
        var first = await service.EvaluateBillAsync(new EvaluateSupplierSubscriptionBillCommand(companyId, billId, Guid.NewGuid(), "Finance user"), CancellationToken.None);
        var second = await service.EvaluateBillAsync(new EvaluateSupplierSubscriptionBillCommand(companyId, billId, Guid.NewGuid(), "Finance user"), CancellationToken.None);

        Assert.Equal("confirmed", first.Status);
        Assert.Equal("confirmed", second.Status);
        Assert.NotNull(first.Match);

        var matches = await fixture.DbContext.SupplierSubscriptionBillMatches.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.BillId == billId).ToListAsync();
        Assert.Single(matches);
        Assert.Equal(SupplierSubscriptionMatchStatuses.Confirmed, matches[0].Status);

        var subscription = await fixture.DbContext.SupplierSubscriptions.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);
        Assert.Equal(new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc), subscription.NextExpectedBillDateUtc);

        var bill = await fixture.DbContext.FinanceBills.IgnoreQueryFilters().SingleAsync(x => x.Id == billId);
        Assert.Equal("pending_approval", bill.Status);
        Assert.Equal(FinanceDocumentPostingStatuses.Draft, bill.PostingStatus);
    }

    [Fact]
    public async Task Ambiguous_bill_creates_suggestions_without_advancing_schedule()
    {
        await using var fixture = await SupplierSubscriptionFixture.CreateAsync();
        var companyId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        await fixture.SeedCompanySupplierBillAndSubscriptionAsync(companyId, supplierId, billId, active: true, subscriptionName: "Cloud platform");
        fixture.DbContext.SupplierSubscriptions.Add(CreateSubscription(companyId, supplierId, "Cloud platform secondary", status: SupplierSubscriptionStatuses.Active));
        await fixture.DbContext.SaveChangesAsync();

        var result = await fixture.CreateService().EvaluateBillAsync(new EvaluateSupplierSubscriptionBillCommand(companyId, billId, Guid.NewGuid(), "Finance user"), CancellationToken.None);

        Assert.Equal("needs_review", result.Status);
        Assert.Equal(2, result.Suggestions.Count);
        Assert.All(result.Suggestions, suggestion => Assert.Equal(SupplierSubscriptionMatchStatuses.Suggested, suggestion.Status));

        var subscriptions = await fixture.DbContext.SupplierSubscriptions.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync();
        Assert.All(subscriptions, subscription => Assert.Equal(new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), subscription.NextExpectedBillDateUtc));
    }

    [Fact]
    public async Task Confirming_one_suggestion_rejects_competing_suggestions_and_advances_once()
    {
        await using var fixture = await SupplierSubscriptionFixture.CreateAsync();
        var companyId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        await fixture.SeedCompanySupplierBillAndSubscriptionAsync(companyId, supplierId, billId, active: true, subscriptionName: "Cloud platform");
        fixture.DbContext.SupplierSubscriptions.Add(CreateSubscription(companyId, supplierId, "Cloud platform secondary", status: SupplierSubscriptionStatuses.Active));
        await fixture.DbContext.SaveChangesAsync();

        var service = fixture.CreateService();
        var evaluated = await service.EvaluateBillAsync(new EvaluateSupplierSubscriptionBillCommand(companyId, billId, Guid.NewGuid(), "Finance user"), CancellationToken.None);
        var confirmed = await service.DecideMatchAsync(new DecideSupplierSubscriptionMatchCommand(companyId, evaluated.Suggestions[0].Id, true, Guid.NewGuid(), "Finance user"), CancellationToken.None);

        Assert.Equal("confirmed", confirmed.Status);
        var matches = await fixture.DbContext.SupplierSubscriptionBillMatches.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.BillId == billId).ToListAsync();
        Assert.Single(matches, x => x.Status == SupplierSubscriptionMatchStatuses.Confirmed);
        Assert.Single(matches, x => x.Status == SupplierSubscriptionMatchStatuses.Rejected);
    }

    [Fact]
    public async Task Cross_company_supplier_is_rejected_without_disclosure()
    {
        await using var fixture = await SupplierSubscriptionFixture.CreateAsync();
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var supplierBId = Guid.NewGuid();
        fixture.DbContext.Companies.Add(new Company(companyAId, "Company A"));
        fixture.DbContext.Companies.Add(new Company(companyBId, "Company B"));
        fixture.DbContext.FinanceCounterparties.Add(new FinanceCounterparty(supplierBId, companyBId, "Other supplier", "supplier"));
        await fixture.DbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateService().CreateAsync(
            new CreateSupplierSubscriptionCommand(companyAId, supplierBId, "Other contract", "SEK", 100m, "monthly", 1, DateTime.UtcNow, DateTime.UtcNow, 0m, 5, null, null, null, 30, false, null, Guid.NewGuid(), "Finance user"),
            CancellationToken.None));
    }

    [Fact]
    public async Task Query_filters_scope_subscriptions_and_matches_by_company()
    {
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using (var seedContext = CreateContext(connection, new TestCompanyContextAccessor(null, null)))
        {
            await seedContext.Database.EnsureCreatedAsync();
            var supplierAId = Guid.NewGuid();
            var supplierBId = Guid.NewGuid();
            var billAId = Guid.NewGuid();
            var billBId = Guid.NewGuid();
            var subscriptionA = CreateSubscription(companyAId, supplierAId, "A", status: SupplierSubscriptionStatuses.Active);
            var subscriptionB = CreateSubscription(companyBId, supplierBId, "B", status: SupplierSubscriptionStatuses.Active);
            seedContext.Companies.Add(new Company(companyAId, "Company A"));
            seedContext.Companies.Add(new Company(companyBId, "Company B"));
            seedContext.FinanceCounterparties.Add(new FinanceCounterparty(supplierAId, companyAId, "Supplier A", "supplier"));
            seedContext.FinanceCounterparties.Add(new FinanceCounterparty(supplierBId, companyBId, "Supplier B", "supplier"));
            seedContext.FinanceBills.Add(new FinanceBill(billAId, companyAId, supplierAId, "A-1", new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc), 100m, "SEK", "pending_approval"));
            seedContext.FinanceBills.Add(new FinanceBill(billBId, companyBId, supplierBId, "B-1", new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc), 100m, "SEK", "pending_approval"));
            seedContext.SupplierSubscriptions.Add(subscriptionA);
            seedContext.SupplierSubscriptions.Add(subscriptionB);
            seedContext.SupplierSubscriptionBillMatches.Add(new SupplierSubscriptionBillMatch(Guid.NewGuid(), companyAId, subscriptionA.Id, billAId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), 100m, 100m, SupplierSubscriptionMatchStatuses.Suggested, SupplierSubscriptionMatchMethods.Automatic, 100, "Company A match"));
            seedContext.SupplierSubscriptionBillMatches.Add(new SupplierSubscriptionBillMatch(Guid.NewGuid(), companyBId, subscriptionB.Id, billBId, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), 100m, 100m, SupplierSubscriptionMatchStatuses.Suggested, SupplierSubscriptionMatchMethods.Automatic, 100, "Company B match"));
            await seedContext.SaveChangesAsync();
        }

        var accessor = new TestCompanyContextAccessor(companyAId, Guid.NewGuid());
        await using var dbContext = CreateContext(connection, accessor);
        Assert.Single(await dbContext.SupplierSubscriptions.ToListAsync());
        Assert.Single(await dbContext.SupplierSubscriptionBillMatches.ToListAsync());
        accessor.SetCompanyId(companyBId);
        Assert.Single(await dbContext.SupplierSubscriptions.ToListAsync());
        Assert.Single(await dbContext.SupplierSubscriptionBillMatches.ToListAsync());
    }

    [Fact]
    public async Task Receipt_evidence_link_creates_reviewable_match_without_advancing_or_approving_bill()
    {
        await using var fixture = await SupplierSubscriptionFixture.CreateAsync();
        var companyId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        var billId = Guid.NewGuid();
        await fixture.SeedCompanySupplierBillAndSubscriptionAsync(companyId, supplierId, billId, active: true);
        var subscription = await fixture.DbContext.SupplierSubscriptions.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId);

        var service = fixture.CreateService();
        var first = await service.LinkReceiptEvidenceAsync(new LinkSupplierSubscriptionReceiptEvidenceCommand(companyId, subscription.Id, billId, "Receipt for recurring cloud service.", Guid.NewGuid(), "Finance user"), CancellationToken.None);
        var second = await service.LinkReceiptEvidenceAsync(new LinkSupplierSubscriptionReceiptEvidenceCommand(companyId, subscription.Id, billId, "Receipt for recurring cloud service.", Guid.NewGuid(), "Finance user"), CancellationToken.None);

        Assert.Equal("needs_review", first.Status);
        Assert.Equal("needs_review", second.Status);
        Assert.Single(first.Suggestions);
        Assert.Equal(SupplierSubscriptionMatchStatuses.Suggested, first.Suggestions[0].Status);
        Assert.Equal(SupplierSubscriptionMatchMethods.ReceiptEvidence, first.Suggestions[0].MatchMethod);

        var matches = await fixture.DbContext.SupplierSubscriptionBillMatches.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.BillId == billId).ToListAsync();
        Assert.Single(matches);
        Assert.Equal(SupplierSubscriptionMatchStatuses.Suggested, matches[0].Status);

        var storedSubscription = await fixture.DbContext.SupplierSubscriptions.IgnoreQueryFilters().SingleAsync(x => x.Id == subscription.Id);
        Assert.Equal(new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc), storedSubscription.NextExpectedBillDateUtc);

        var bill = await fixture.DbContext.FinanceBills.IgnoreQueryFilters().SingleAsync(x => x.Id == billId);
        Assert.Equal("pending_approval", bill.Status);
        Assert.Equal(FinanceDocumentPostingStatuses.Draft, bill.PostingStatus);
    }
    private static SupplierSubscription CreateSubscription(
        Guid? companyId = null,
        Guid? counterpartyId = null,
        string name = "Cloud platform",
        decimal expectedAmount = 100m,
        decimal amountTolerance = 0m,
        int dateToleranceDays = 5,
        string cadence = "monthly",
        DateTime? nextExpectedBillDateUtc = null,
        string status = SupplierSubscriptionStatuses.Draft) =>
        new(
            Guid.NewGuid(),
            companyId ?? Guid.NewGuid(),
            counterpartyId ?? Guid.NewGuid(),
            name,
            "SEK",
            expectedAmount,
            cadence,
            31,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            nextExpectedBillDateUtc ?? new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            amountTolerance,
            dateToleranceDays,
            null,
            null,
            null,
            30,
            true,
            null,
            status);

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection, ICompanyContextAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options, accessor);

    private sealed class SupplierSubscriptionFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        public VirtualCompanyDbContext DbContext { get; }
        private RecordingAuditWriter Audit { get; } = new();

        private SupplierSubscriptionFixture(SqliteConnection connection, VirtualCompanyDbContext dbContext)
        {
            _connection = connection;
            DbContext = dbContext;
        }

        public static async Task<SupplierSubscriptionFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var dbContext = CreateContext(connection, new TestCompanyContextAccessor(null, null));
            await dbContext.Database.EnsureCreatedAsync();
            return new SupplierSubscriptionFixture(connection, dbContext);
        }

        public SupplierSubscriptionService CreateService() =>
            new(DbContext, Audit, NullLogger<SupplierSubscriptionService>.Instance);

        public async Task SeedCompanySupplierBillAndSubscriptionAsync(Guid companyId, Guid supplierId, Guid billId, bool active, string subscriptionName = "Cloud platform")
        {
            DbContext.Companies.Add(new Company(companyId, "Test company"));
            DbContext.FinanceCounterparties.Add(new FinanceCounterparty(supplierId, companyId, "Cloud Supplier", "supplier"));
            DbContext.FinanceBills.Add(new FinanceBill(
                billId,
                companyId,
                supplierId,
                "BILL-1",
                new DateTime(2026, 1, 31, 10, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 2, 15, 10, 0, 0, DateTimeKind.Utc),
                100m,
                "SEK",
                "pending_approval",
                postingStatus: FinanceDocumentPostingStatuses.Draft));
            DbContext.SupplierSubscriptions.Add(CreateSubscription(companyId, supplierId, subscriptionName, status: active ? SupplierSubscriptionStatuses.Active : SupplierSubscriptionStatuses.Draft));
            await DbContext.SaveChangesAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await DbContext.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class RecordingAuditWriter : IAuditEventWriter
    {
        public List<AuditEventWriteRequest> Events { get; } = [];
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid? companyId, Guid? userId)
        {
            CompanyId = companyId;
            UserId = userId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; }
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
}

