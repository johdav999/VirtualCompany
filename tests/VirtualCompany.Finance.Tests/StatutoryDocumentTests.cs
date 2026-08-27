using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Finance.Tests;

public sealed class StatutoryDocumentTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Series_formats_numbers_and_never_reuses_an_allocation()
    {
        var series = new StatutoryDocumentSeries(Guid.NewGuid(), Guid.NewGuid(), "ci",
            StatutoryDocumentTypes.CustomerInvoice, new(2026, 1, 1), new(2026, 12, 31),
            "INV-", 6, 1, Guid.NewGuid(), NowUtc);

        var first = series.Allocate(Guid.NewGuid(), NowUtc.AddMinutes(1));
        var second = series.Allocate(Guid.NewGuid(), NowUtc.AddMinutes(2));

        Assert.Equal("INV-000001", series.Format(first));
        Assert.Equal("INV-000002", series.Format(second));
        Assert.Equal(3, series.NextNumber);
        Assert.Equal(3, series.Version);
    }

    [Fact]
    public async Task Invalid_document_is_blocked_before_number_allocation()
    {
        await using var fixture = await Fixture.CreateAsync();
        var series = await fixture.CreateSeriesAsync();
        var invalid = fixture.ValidDocument() with { CounterpartyLegalName = "" };

        var error = await Assert.ThrowsAsync<StatutoryDocumentException>(() => fixture.Service.IssueNativeCustomerAsync(
            new(fixture.CompanyId, series.Id, "issue-invalid", invalid, fixture.ActorId), default));

        Assert.Equal(StatutoryDocumentReasonCodes.RequiredFieldMissing, error.ReasonCode);
        Assert.Empty(await fixture.Db.StatutoryDocumentNumberAllocations.ToListAsync());
        Assert.Empty(await fixture.Db.IssuedStatutoryDocuments.ToListAsync());
        Assert.Empty(await fixture.Db.FinanceInvoices.ToListAsync());
        Assert.Equal(1, (await fixture.Db.StatutoryDocumentSeries.SingleAsync()).NextNumber);
    }

    [Fact]
    public async Task Native_issue_is_atomic_immutable_and_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var series = await fixture.CreateSeriesAsync();
        var command = new IssueNativeCustomerDocumentCommand(fixture.CompanyId, series.Id, "order-1001-v1",
            fixture.ValidDocument(), fixture.ActorId, "test-correlation");

        var first = await fixture.Service.IssueNativeCustomerAsync(command, default);
        var replay = await fixture.Service.IssueNativeCustomerAsync(command, default);
        var second = await fixture.Service.IssueNativeCustomerAsync(command with
        {
            BusinessKey = "order-1002-v1",
            Document = fixture.ValidDocument() with { ExplanatoryText = "Second supply" }
        }, default);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal("INV-000001", first.DocumentNumber);
        Assert.Equal("INV-000002", second.DocumentNumber);
        Assert.True(first.IsImmutable);
        Assert.Equal(64, first.SnapshotHash.Length);
        Assert.Equal(SwedishStatutoryDocumentCandidatePack.Version, first.PolicyPackVersion);
        Assert.Equal(2, await fixture.Db.IssuedStatutoryDocuments.CountAsync());
        Assert.Equal(2, await fixture.Db.StatutoryDocumentNumberAllocations.CountAsync());
        Assert.Equal(2, await fixture.Db.FinanceInvoices.CountAsync());
        Assert.Equal(3, (await fixture.Db.StatutoryDocumentSeries.SingleAsync()).NextNumber);
        Assert.All(await fixture.Db.StatutoryDocumentNumberAllocations.ToListAsync(), x => Assert.Equal(StatutoryDocumentAllocationStatuses.Issued, x.Status));

        var attached = await fixture.Service.AttachEvidenceAsync(new(fixture.CompanyId, first.Id,
            first.EvidenceVersion, "objects/rendered/inv-1.pdf", "delivery/provider-message-1", fixture.ActorId), default);
        var stale = await Assert.ThrowsAsync<StatutoryDocumentException>(() => fixture.Service.AttachEvidenceAsync(new(
            fixture.CompanyId, first.Id, first.EvidenceVersion, "objects/rendered/replacement.pdf", null, fixture.ActorId), default));
        Assert.Equal(first.SnapshotHash, attached.SnapshotHash);
        Assert.Equal(2, attached.EvidenceVersion);
        Assert.Equal(StatutoryDocumentReasonCodes.VersionConflict, stale.ReasonCode);
    }

    [Fact]
    public async Task Operator_gap_is_durable_visible_and_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var series = await fixture.CreateSeriesAsync();
        var command = new RecordStatutoryDocumentGapCommand(fixture.CompanyId, series.Id, "printer-damage-1", 1,
            "Printed copy was damaged before delivery; number retained as unused.", fixture.ActorId);

        var first = await fixture.Service.RecordGapAsync(command, default);
        var replay = await fixture.Service.RecordGapAsync(command, default);

        Assert.Equal(first.Id, replay.Id);
        Assert.Equal("INV-000001", first.FormattedNumber);
        Assert.Equal(StatutoryDocumentAllocationStatuses.Gap, first.Status);
        Assert.NotNull(first.GapReason);
        Assert.Single(await fixture.Service.ListAllocationsAsync(fixture.CompanyId, series.Id, default));
    }

    [Fact]
    public async Task Provider_supplier_document_keeps_original_number_and_authority()
    {
        await using var fixture = await Fixture.CreateAsync();
        var supplierId = Guid.NewGuid(); var billId = Guid.NewGuid();
        fixture.Db.FinanceCounterparties.Add(new FinanceCounterparty(supplierId, fixture.CompanyId, "Supplier AB", "supplier", taxId: "SE556016068001"));
        fixture.Db.FinanceBills.Add(new FinanceBill(billId, fixture.CompanyId, supplierId, "FTX-7788", NowUtc,
            NowUtc.AddDays(30), 125m, "SEK", "approved", documentKind: FinanceDocumentKinds.SupplierInvoice));
        await fixture.Db.SaveChangesAsync();
        var document = fixture.ValidDocument() with
        {
            DocumentType = StatutoryDocumentTypes.SupplierInvoice,
            Authority = StatutoryDocumentAuthorities.Provider,
            CounterpartyId = supplierId,
            CounterpartyLegalName = "Supplier AB",
            ProviderDocumentNumber = "FTX-7788"
        };

        var registered = await fixture.Service.RegisterImportedAsync(new(fixture.CompanyId, billId,
            "fortnox-bill-7788-v1", document, fixture.ActorId), default);
        var replay = await fixture.Service.RegisterImportedAsync(new(fixture.CompanyId, billId,
            "fortnox-bill-7788-v1", document, fixture.ActorId), default);

        Assert.Equal(registered.Id, replay.Id);
        Assert.Equal(StatutoryDocumentAuthorities.Provider, registered.Authority);
        Assert.Equal("FTX-7788", registered.DocumentNumber);
        Assert.Equal("FTX-7788", (await fixture.Db.FinanceBills.SingleAsync(x => x.Id == billId)).BillNumber);
        Assert.Null(registered.SeriesId);
        Assert.Empty(await fixture.Db.StatutoryDocumentNumberAllocations.ToListAsync());
    }

    [Fact]
    public async Task Series_and_issued_reads_are_tenant_scoped()
    {
        await using var fixture = await Fixture.CreateAsync();
        var series = await fixture.CreateSeriesAsync();
        var issued = await fixture.Service.IssueNativeCustomerAsync(new(fixture.CompanyId, series.Id, "tenant-a",
            fixture.ValidDocument(), fixture.ActorId), default);

        Assert.Empty(await fixture.Service.ListSeriesAsync(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<StatutoryDocumentException>(() => fixture.Service.GetIssuedAsync(Guid.NewGuid(), issued.Id, default));
    }

    [Fact]
    public async Task Model_enforces_company_series_number_and_source_uniqueness()
    {
        await using var fixture = await Fixture.CreateAsync();
        var issued = fixture.Db.Model.FindEntityType(typeof(IssuedStatutoryDocument));
        var allocation = fixture.Db.Model.FindEntityType(typeof(StatutoryDocumentNumberAllocation));
        Assert.NotNull(issued); Assert.NotNull(allocation);
        Assert.Contains(issued.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(IssuedStatutoryDocument.CompanyId), nameof(IssuedStatutoryDocument.SourceRecordId), nameof(IssuedStatutoryDocument.SourceVersion)]));
        Assert.Contains(allocation.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(StatutoryDocumentNumberAllocation.CompanyId), nameof(StatutoryDocumentNumberAllocation.SeriesId), nameof(StatutoryDocumentNumberAllocation.FiscalYearKey), nameof(StatutoryDocumentNumberAllocation.Number)]));
    }

    [Fact]
    public void Document_candidate_hash_is_deterministic_and_stays_unvalidated()
    {
        var first = new SwedishStatutoryDocumentCandidatePack();
        var second = new SwedishStatutoryDocumentCandidatePack();
        Assert.Equal(first.DefinitionHash, second.DefinitionHash);
        Assert.False(first.Definition.IsStatutoryComplianceValidated);
        Assert.Equal("supported_unvalidated_limited_scope", first.Definition.CapabilityStates!["native_statutory_invoice_issuance"]);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext db, Guid companyId, Guid actorId,
            Guid counterpartyId, StatutoryDocumentService service)
        { _connection = connection; Db = db; CompanyId = companyId; ActorId = actorId; CounterpartyId = counterpartyId; Service = service; }
        public VirtualCompanyDbContext Db { get; }
        public Guid CompanyId { get; }
        public Guid ActorId { get; }
        public Guid CounterpartyId { get; }
        public StatutoryDocumentService Service { get; }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
            var companyId = Guid.NewGuid(); var actorId = Guid.NewGuid(); var counterpartyId = Guid.NewGuid();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options,
                new TestCompanyContextAccessor(companyId, actorId));
            await db.Database.EnsureCreatedAsync();
            db.Companies.Add(new Company(companyId, "Swedish Documents AB"));
            db.FinanceCounterparties.Add(new FinanceCounterparty(counterpartyId, companyId, "Buyer AB", "customer"));
            db.CompanyStatutoryProfiles.Add(new CompanyStatutoryProfile(Guid.NewGuid(), companyId, Profile(), actorId, NowUtc));
            var config = new AccountingConfiguration(Guid.NewGuid(), companyId, "SEK", 1, 1,
                AccountingPolicyPackDefaults.SwedishCandidatePackKey, SwedishStatutoryDocumentCandidatePack.Version,
                new(2026, 1, 1), 2, AccountingRoundingModeValues.MidpointToEven, actorId, NowUtc);
            config.SetSetupState(AccountingSetupStateValues.Ready, actorId, NowUtc);
            db.AccountingConfigurations.Add(config); await db.SaveChangesAsync();
            var resolver = new AccountingPolicyPackResolver([new SwedishStatutoryDocumentCandidatePack()]);
            var telemetry = new AccountingOperationsTelemetry(NullLogger<AccountingOperationsTelemetry>.Instance);
            var policy = new StatutoryDocumentPolicy(db, resolver);
            var service = new StatutoryDocumentService(db, policy, resolver, new AuditEventWriter(db), telemetry, new FixedTimeProvider(NowUtc));
            return new(connection, db, companyId, actorId, counterpartyId, service);
        }

        public Task<StatutoryDocumentSeriesDto> CreateSeriesAsync() => Service.CreateSeriesAsync(new(CompanyId, "CI",
            StatutoryDocumentTypes.CustomerInvoice, new(2026, 1, 1), new(2026, 12, 31), "INV-", 6, 1, ActorId), default);

        public StatutoryDocumentInput ValidDocument() => new(StatutoryDocumentTypes.CustomerInvoice,
            StatutoryDocumentAuthorities.Native, CounterpartyId, "Buyer AB", "Buyergatan 1", "11122", "Stockholm", "SE",
            "SE556016068001", new(2026, 8, 24), new(2026, 8, 24), new(2026, 8, 24), new(2026, 9, 23), "SEK",
            "30 days", "Domestic standard-rated supply", 100m, 25m, 125m,
            [new("Consulting", 1m, 100m, 100m, .25m, 25m)], TaxFactsJson: "{\"rule\":\"se_domestic_sales_25\"}", SourceVersion: 1);

        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
        private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider { public override DateTimeOffset GetUtcNow() => value; }
        private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
        {
            public Guid? CompanyId { get; private set; } = companyId;
            public Guid? UserId => userId;
            public bool IsResolved => true;
            public ResolvedCompanyMembershipContext? Membership => null;
            public void SetCompanyId(Guid? value) => CompanyId = value;
            public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
        }
    }

    private static CompanyStatutoryProfileValues Profile() => new("Swedish Documents AB", "556016-0680", "SE556016068001",
        StatutoryVatRegistrationStatusValues.Registered, "Sellergatan 1", null, "11122", "Stockholm", "SE",
        null, null, null, null, null, "SE", "SEK", StatutoryFiscalYearBasisValues.CalendarYear,
        StatutoryBookkeepingMethodValues.Accrual, new(2000, 1, 1), new(2000, 1, 1), null, true,
        StatutoryVerificationStatusValues.Unverified, StatutoryProfileSourceKindValues.UserEntry, "test", NowUtc, null, null);
}
