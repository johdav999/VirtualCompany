using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Finance.Tests;

public sealed class CustomerInvoiceDraftTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Calculation_is_deterministic_and_retains_discount_tax_account_and_box_facts()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Calculation.CalculateAsync(fixture.CompanyId, fixture.Input(), default);
        var second = await fixture.Calculation.CalculateAsync(fixture.CompanyId, fixture.Input(), default);

        Assert.Equal(first.InputHash, second.InputHash);
        Assert.Equal(first.ResultHash, second.ResultHash);
        Assert.Empty(first.Blockers);
        Assert.Equal(180m, first.NetTotal);
        Assert.Equal(20m, first.DiscountTotal);
        Assert.Equal(45m, first.TaxTotal);
        Assert.Equal(225m, first.GrossTotal);
        var line = Assert.Single(first.Lines);
        Assert.Equal(AccountingAccountRoleKeys.TaxOutput25, line.TaxAccountRoleKey);
        Assert.Equal(["05", "10"], line.VatBoxMappings);
    }

    [Fact]
    public async Task Create_is_idempotent_and_cross_company_customer_is_not_exposed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var command = new CreateCustomerInvoiceDraftCommand(fixture.CompanyId, fixture.Input(), "draft-create-1",
            fixture.ActorId, "draft-test");

        var created = await fixture.Service.CreateAsync(command, default);
        var replay = await fixture.Service.CreateAsync(command, default);
        var changed = await Assert.ThrowsAsync<CustomerInvoiceDraftException>(() => fixture.Service.CreateAsync(
            command with { Draft = fixture.Input() with { Notes = "different" } }, default));
        var hidden = await Assert.ThrowsAsync<CustomerInvoiceDraftException>(() => fixture.Service.CreateAsync(
            command with { Draft = fixture.Input() with { CustomerId = fixture.OtherCompanyCustomerId }, IdempotencyKey = "draft-create-cross-company" }, default));

        Assert.Equal(created.Id, replay.Id);
        Assert.Equal(CustomerInvoiceDraftReasonCodes.IdempotencyConflict, changed.ReasonCode);
        Assert.Equal(CustomerInvoiceDraftReasonCodes.CustomerNotFound, hidden.ReasonCode);
        Assert.Single(await fixture.Db.CustomerInvoiceDrafts.ToListAsync());
    }

    [Fact]
    public async Task Edit_after_approval_invalidates_exact_version_and_readiness_reports_stale_approval()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(new(fixture.CompanyId, fixture.Input(), "draft-create-approval",
            fixture.ActorId, "draft-test"), default);
        var submitted = await fixture.Service.SubmitAsync(new(fixture.CompanyId, created.Id, created.Version,
            "draft-submit-approval", fixture.ActorId, "draft-test"), default);
        var approval = await fixture.Db.ApprovalRequests.Include(x => x.Steps)
            .SingleAsync(x => x.Id == submitted.ApprovalRequestId);
        approval.ApproveCurrentStep(approval.Steps.Single().Id, fixture.ActorId, "Approved for issue preparation.");
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var approvedReadiness = await fixture.Service.GetReadinessAsync(new(fixture.CompanyId, created.Id, created.Version), default);
        Assert.True(approvedReadiness.IsAllowed);

        var updated = await fixture.Service.UpdateAsync(new(fixture.CompanyId, created.Id, created.Version,
            fixture.Input() with { Notes = "Changed after approval" }, "draft-update-after-approval",
            fixture.ActorId, "draft-test"), default);
        var staleReadiness = await fixture.Service.GetReadinessAsync(new(fixture.CompanyId, updated.Id, updated.Version), default);

        Assert.False(staleReadiness.IsAllowed);
        Assert.Contains(staleReadiness.Blockers, x => x.ReasonCode == CustomerInvoiceDraftReasonCodes.ApprovalStale);
        Assert.NotNull(updated.Approval);
        Assert.False(updated.Approval!.IsCurrent);
        Assert.Empty(await fixture.Db.FinanceInvoices.ToListAsync());
        Assert.Empty(await fixture.Db.LedgerEntries.ToListAsync());
    }

    [Fact]
    public async Task Persistence_model_has_company_scoped_concurrency_and_bounded_list_indexes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var draft = fixture.Db.Model.FindEntityType(typeof(CustomerInvoiceDraft))!;
        var operation = fixture.Db.Model.FindEntityType(typeof(CustomerInvoiceDraftOperation))!;
        var invoice = fixture.Db.Model.FindEntityType(typeof(FinanceInvoice))!;

        Assert.True(draft.FindProperty(nameof(CustomerInvoiceDraft.Version))!.IsConcurrencyToken);
        Assert.Contains(draft.GetIndexes(), index => index.Properties.Select(x => x.Name)
            .SequenceEqual([nameof(CustomerInvoiceDraft.CompanyId), nameof(CustomerInvoiceDraft.Status), nameof(CustomerInvoiceDraft.UpdatedUtc)]));
        Assert.Contains(draft.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name)
            .SequenceEqual([nameof(CustomerInvoiceDraft.CompanyId), nameof(CustomerInvoiceDraft.IssuedInvoiceId)]));
        Assert.Contains(operation.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name)
            .SequenceEqual([nameof(CustomerInvoiceDraftOperation.CompanyId), nameof(CustomerInvoiceDraftOperation.IdempotencyKey)]));
        Assert.NotNull(invoice.FindProperty(nameof(FinanceInvoice.Authority)));
        Assert.Contains(invoice.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name)
            .SequenceEqual([nameof(FinanceInvoice.CompanyId), nameof(FinanceInvoice.SourceDraftId)]));
        Assert.NotNull(fixture.Db.Model.FindEntityType(typeof(StatutoryDocumentSeries))!.GetQueryFilter());
        Assert.NotNull(fixture.Db.Model.FindEntityType(typeof(StatutoryDocumentNumberAllocation))!.GetQueryFilter());
        Assert.NotNull(fixture.Db.Model.FindEntityType(typeof(IssuedStatutoryDocument))!.GetQueryFilter());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly ServiceProvider _services;
        private Fixture(SqliteConnection connection, ServiceProvider services, VirtualCompanyDbContext db,
            Guid companyId, Guid customerId, Guid otherCompanyCustomerId, Guid actorId,
            CustomerInvoiceDraftCalculationPolicy calculation, CustomerInvoiceDraftService service)
        {
            _connection = connection; _services = services; Db = db; CompanyId = companyId;
            CustomerId = customerId; OtherCompanyCustomerId = otherCompanyCustomerId; ActorId = actorId;
            Calculation = calculation; Service = service;
        }

        public VirtualCompanyDbContext Db { get; }
        public Guid CompanyId { get; }
        public Guid CustomerId { get; }
        public Guid OtherCompanyCustomerId { get; }
        public Guid ActorId { get; }
        public CustomerInvoiceDraftCalculationPolicy Calculation { get; }
        public CustomerInvoiceDraftService Service { get; }

        public CustomerInvoiceDraftInput Input() => new(CustomerId,
            CustomerInvoiceDraftDocumentTypes.Invoice, new DateOnly(2026, 8, 25), new DateOnly(2026, 8, 25),
            new DateOnly(2026, 9, 24), "SEK", CustomerBillingPaymentTermKinds.FixedDays, 30,
            "BUYER-42", "SELLER-7", null, CustomerBillingDeliveryChannels.Email,
            CustomerInvoiceDraftSourceKinds.User, "test", [new CustomerInvoiceDraftLineInput(1,
                "Domestic consulting", 2m, "hour", 100m, 10m,
                SwedishCandidateAccountingPolicyPack.DomesticSales25RuleKey, "standard_goods_or_services",
                [new("operator_classified_domestic_standard_25", "contract")],
                new Dictionary<string, string> { ["cost_center"] = "STOCKHOLM" }, "revenue",
                "sales-order-line-1", "ORDER-42")], []);

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
            await using (var schema = new VirtualCompanyDbContext(options)) await schema.Database.EnsureCreatedAsync();
            var companyId = Guid.NewGuid(); var otherCompanyId = Guid.NewGuid(); var actorId = Guid.NewGuid();
            var customerId = Guid.NewGuid(); var otherCustomerId = Guid.NewGuid();
            var pack = new SwedishCandidateAccountingPolicyPack();
            await using (var seed = new VirtualCompanyDbContext(options))
            {
                seed.Companies.AddRange(new Company(companyId, "Invoice Draft Company"), new Company(otherCompanyId, "Other Company"));
                seed.FinanceCounterparties.AddRange(new FinanceCounterparty(customerId, companyId, "Domestic Customer", "customer"),
                    new FinanceCounterparty(otherCustomerId, otherCompanyId, "Hidden Customer", "customer"));
                seed.CustomerBillingProfiles.Add(new CustomerBillingProfile(Guid.NewGuid(), companyId, customerId,
                    CustomerValues(), actorId, NowUtc));
                seed.CompanyStatutoryProfiles.Add(new CompanyStatutoryProfile(Guid.NewGuid(), companyId,
                    StatutoryValues(), actorId, NowUtc));
                var configuration = new AccountingConfiguration(Guid.NewGuid(), companyId, "SEK", 1, 1,
                    pack.Definition.PackKey, pack.Definition.Version, new DateOnly(2026, 1, 1), 2,
                    AccountingRoundingModeValues.MidpointToEven, actorId, NowUtc);
                configuration.SetSetupState(AccountingSetupStateValues.Ready, actorId, NowUtc);
                seed.AccountingConfigurations.Add(configuration);
                seed.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(Guid.NewGuid(), companyId,
                    "SEK", 1000m, 1000m, true));
                await seed.SaveChangesAsync();
            }

            var accessor = new TestAccessor(companyId, actorId);
            var db = new VirtualCompanyDbContext(options, accessor);
            var resolver = new AccountingPolicyPackResolver([pack]);
            var calculation = new CustomerInvoiceDraftCalculationPolicy(db, resolver, new AccountingTaxDecisionPolicy());
            var readiness = new CustomerInvoiceDraftReadinessPolicy(db, resolver, calculation);
            var services = new ServiceCollection().AddMetrics().BuildServiceProvider();
            var telemetry = new CustomerInvoiceDraftTelemetry(services.GetRequiredService<System.Diagnostics.Metrics.IMeterFactory>());
            var service = new CustomerInvoiceDraftService(db, calculation, readiness, new AuditEventWriter(db),
                telemetry, new FixedTimeProvider());
            return new Fixture(connection, services, db, companyId, customerId, otherCustomerId, actorId, calculation, service);
        }

        private static CustomerBillingProfileValues CustomerValues() => new("Domestic Customer AB", "Domestic Customer",
            CustomerBillingPartyKinds.Organization, "5560160680", "SE556016068001",
            CustomerBillingValidationStates.UserAttested, "Kundgatan 1", null, "11122", "Stockholm", null,
            "SE", null, null, null, null, null, null, "sv-SE", "SEK",
            CustomerBillingPaymentTermKinds.FixedDays, 30, "bank_transfer", CustomerBillingDeliveryChannels.Email,
            "billing@example.test", "BUYER-42", null, null, 100000m, CustomerBillingCreditStatuses.Active,
            "1510", "STOCKHOLM", new DateOnly(2026, 1, 1), null, CustomerBillingSourceKinds.User, "test",
            NowUtc, null, null);

        private static CompanyStatutoryProfileValues StatutoryValues() => new("Invoice Draft Company AB",
            "556016-0680", "SE556016068001", StatutoryVatRegistrationStatusValues.Registered,
            "Bolagsgatan 1", null, "11122", "Stockholm", "SE", null, null, null, null, null,
            "SE", "SEK", StatutoryFiscalYearBasisValues.CalendarYear, StatutoryBookkeepingMethodValues.Accrual,
            new DateOnly(2000, 1, 1), new DateOnly(2000, 1, 1), null, true,
            StatutoryVerificationStatusValues.Unverified, StatutoryProfileSourceKindValues.UserEntry,
            "test-attestation", NowUtc, null, null);

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _services.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    private sealed class TestAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(NowUtc, TimeSpan.Zero);
    }
}
