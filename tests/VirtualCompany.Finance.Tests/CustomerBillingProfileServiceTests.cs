using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Finance.Tests;

public sealed class CustomerBillingProfileServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 25, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Structured_profile_normalizes_identity_address_and_delivery_facts()
    {
        var values = ToValues(Input() with { TaxIdentifier = " SE-556 016 0680 ", VatIdentifier = "se 556016068001" });
        var profile = new CustomerBillingProfile(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), values, Guid.NewGuid(), NowUtc);

        Assert.Equal("SE5560160680", profile.NormalizedTaxIdentifier);
        Assert.Equal("SE556016068001", profile.NormalizedVatIdentifier);
        Assert.Equal("SE", profile.BillingCountryCode);
        Assert.Equal("sv-SE", profile.LanguageCode);
        Assert.Equal("billing@example.test", profile.NormalizedInvoiceDeliveryEmail);
        Assert.Equal(CustomerBillingValidationStates.UserAttested, profile.IdentityValidationState);
    }

    [Fact]
    public void Validation_state_requires_matching_attestation_or_verification_provenance()
    {
        var companyId = Guid.NewGuid(); var counterpartyId = Guid.NewGuid(); var actorId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new CustomerBillingProfile(Guid.NewGuid(), companyId, counterpartyId,
            ToValues(Input() with { UserAttestedUtc = null }), actorId, NowUtc));
        Assert.Throws<ArgumentException>(() => new CustomerBillingProfile(Guid.NewGuid(), companyId, counterpartyId,
            ToValues(Input() with
            {
                IdentityValidationState = CustomerBillingValidationStates.ProviderSourced,
                SourceKind = CustomerBillingSourceKinds.Provider,
                SourceReference = null,
                UserAttestedUtc = null
            }), actorId, NowUtc));

        var verified = new CustomerBillingProfile(Guid.NewGuid(), companyId, counterpartyId, ToValues(Input() with
        {
            IdentityValidationState = CustomerBillingValidationStates.ExternallyVerified,
            UserAttestedUtc = null,
            ExternallyVerifiedUtc = NowUtc,
            VerificationSource = "approved-registry-check"
        }), actorId, NowUtc);
        Assert.Equal(CustomerBillingValidationStates.ExternallyVerified, verified.IdentityValidationState);
        Assert.Equal("approved-registry-check", verified.VerificationSource);
    }

    [Fact]
    public async Task Save_retains_version_provenance_audit_and_rejects_stale_update()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.UpsertAsync(new(fixture.CompanyA, fixture.CustomerA, Input(), null,
            fixture.ActorId, "create"), default);
        var updatedInput = Input() with { BuyerReference = "PO-42" };
        var updated = await fixture.Service.UpsertAsync(new(fixture.CompanyA, fixture.CustomerA, updatedInput,
            created.Version, fixture.ActorId, "update"), default);

        var stale = await Assert.ThrowsAsync<CustomerBillingException>(() => fixture.Service.UpsertAsync(new(
            fixture.CompanyA, fixture.CustomerA, Input() with { BuyerReference = "stale" }, created.Version,
            fixture.ActorId, "stale"), default));

        Assert.Equal(CustomerBillingReasonCodes.ConcurrencyConflict, stale.ReasonCode);
        Assert.Equal(2, updated.Version);
        Assert.Equal(2, await fixture.Db.CustomerBillingProfileVersions.CountAsync());
        Assert.Equal(2, await fixture.Db.AuditEvents.CountAsync());
        var history = await fixture.Service.GetHistoryAsync(new(fixture.CompanyA, fixture.CustomerA), default);
        Assert.Equal([2L, 1L], history.Select(x => x.ProfileVersion));
        Assert.Contains("BuyerReference", history[0].ChangedFields);
    }

    [Fact]
    public async Task Duplicate_detection_is_company_scoped_and_requires_explicit_keep_separate_decision()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Service.UpsertAsync(new(fixture.CompanyA, fixture.CustomerA, Input(), null, fixture.ActorId, null), default);
        await fixture.Service.UpsertAsync(new(fixture.CompanyA, fixture.CustomerB,
            Input() with { LegalName = "Example Alternate AB", InvoiceDeliveryEmail = "other@example.test" }, null,
            fixture.ActorId, null), default);

        var candidates = await fixture.Service.GetDuplicateCandidatesAsync(new(fixture.CompanyA), default);
        var candidate = Assert.Single(candidates);
        Assert.Equal(CustomerDuplicateDecisionStatuses.Pending, candidate.Status);
        Assert.Contains(candidate.Evidence, x => x.Fact == "tax_identity");

        var decided = await fixture.Service.DecideDuplicateAsync(new(fixture.CompanyA, candidate.Id, candidate.Version,
            CustomerDuplicateDecisions.KeepSeparate, null, null, "Separate legal billing relationships.", fixture.ActorId, null), default);
        var replay = await fixture.Service.DecideDuplicateAsync(new(fixture.CompanyA, candidate.Id, candidate.Version,
            CustomerDuplicateDecisions.KeepSeparate, null, null, "Separate legal billing relationships.", fixture.ActorId, null), default);
        Assert.Equal(CustomerDuplicateDecisionStatuses.KeptSeparate, decided.Status);
        Assert.Equal(decided.Id, replay.Id);

        fixture.Accessor.SetCompanyId(fixture.CompanyB);
        var otherCompanyCandidates = await fixture.Service.GetDuplicateCandidatesAsync(new(fixture.CompanyB), default);
        Assert.Empty(otherCompanyCandidates);
    }

    [Fact]
    public async Task Approved_merge_preserves_invoice_snapshot_links_and_tombstone()
    {
        await using var fixture = await Fixture.CreateAsync(includeInvoice: true);
        await fixture.Service.UpsertAsync(new(fixture.CompanyA, fixture.CustomerA, Input(), null, fixture.ActorId, null), default);
        await fixture.Service.UpsertAsync(new(fixture.CompanyA, fixture.CustomerB,
            Input() with { DisplayName = "Example billing duplicate" }, null, fixture.ActorId, null), default);
        var candidate = Assert.Single(await fixture.Service.GetDuplicateCandidatesAsync(new(fixture.CompanyA), default));

        var result = await fixture.Service.DecideDuplicateAsync(new(fixture.CompanyA, candidate.Id, candidate.Version,
            CustomerDuplicateDecisions.Merge, fixture.CustomerA, fixture.CustomerB, "Approved duplicate consolidation.",
            fixture.ActorId, null), default);

        fixture.Db.ChangeTracker.Clear();
        Assert.Equal(CustomerDuplicateDecisionStatuses.Merged, result.Status);
        Assert.Equal(fixture.CustomerB, (await fixture.Db.FinanceInvoices.IgnoreQueryFilters().SingleAsync()).CounterpartyId);
        Assert.Equal(fixture.CustomerB, (await fixture.Db.FinanceCounterparties.IgnoreQueryFilters().SingleAsync(x => x.Id == fixture.CustomerA)).MergedIntoCounterpartyId);
        Assert.Equal(fixture.CustomerB, (await fixture.Db.CustomerCounterpartyRedirects.IgnoreQueryFilters().SingleAsync()).TargetCounterpartyId);
        var snapshot = await fixture.Db.CustomerInvoiceCustomerSnapshots.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(fixture.CustomerA, snapshot.CounterpartyId);
        Assert.Contains("Example A", snapshot.SnapshotJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_update_creates_visible_conflict_instead_of_overwriting_user_profile()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.UpsertAsync(new(fixture.CompanyA, fixture.CustomerA, Input(), null,
            fixture.ActorId, null), default);
        var guard = (ICustomerBillingProviderSyncGuard)fixture.Service;

        var canOverwrite = await guard.ApplyOrDetectConflictAsync(fixture.CompanyA,
            await fixture.Db.FinanceCounterparties.SingleAsync(x => x.Id == fixture.CustomerA), "Provider Name AB",
            "provider@example.test", "SE9999999999", "fortnox:test:customer-1", NowUtc.AddMinutes(1), default);
        await fixture.Db.SaveChangesAsync();
        var status = await fixture.Service.GetAsync(new(fixture.CompanyA, fixture.CustomerA), default);

        Assert.False(canOverwrite);
        Assert.Equal("Example AB", status!.Profile.LegalName);
        Assert.Equal("needs_review", status.ConflictState);
        var conflict = Assert.Single(status.Conflicts);
        Assert.Equal(CustomerBillingSourceKinds.Provider, conflict.IncomingSourceKind);
        Assert.Contains("LegalName", conflict.ChangedFields);
        Assert.True(status.Version > created.Version);
    }

    [Fact]
    public async Task Persistence_model_uses_tenant_indexes_without_unique_shared_email_constraint()
    {
        await using var fixture = await Fixture.CreateAsync();
        var entity = fixture.Db.Model.FindEntityType(typeof(CustomerBillingProfile))!;
        Assert.True(entity.FindProperty(nameof(CustomerBillingProfile.Version))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name)
            .SequenceEqual([nameof(CustomerBillingProfile.CompanyId), nameof(CustomerBillingProfile.CounterpartyId)]));
        Assert.DoesNotContain(entity.GetIndexes(), x => x.IsUnique && x.Properties.Any(p => p.Name == nameof(CustomerBillingProfile.NormalizedInvoiceDeliveryEmail)));
    }

    private static CustomerBillingProfileInputDto Input() => new("Example AB", "Example", CustomerBillingPartyKinds.Organization,
        "SE5560160680", "SE556016068001", CustomerBillingValidationStates.UserAttested,
        new CustomerBillingAddressDto("Examplegatan 1", null, "111 22", "Stockholm", null, "SE"), null,
        "sv-SE", "SEK", CustomerBillingPaymentTermKinds.FixedDays, 30, "bank_transfer",
        CustomerBillingDeliveryChannels.Email, "billing@example.test", "Buyer-1", null, null, 100000m,
        CustomerBillingCreditStatuses.Active, "1510", "STOCKHOLM", new DateOnly(2026, 1, 1), null,
        CustomerBillingSourceKinds.User, "finance-customer-form", NowUtc, null, null);

    private static CustomerBillingProfileValues ToValues(CustomerBillingProfileInputDto x) => new(x.LegalName, x.DisplayName,
        x.PartyKind, x.TaxIdentifier, x.VatIdentifier, x.IdentityValidationState, x.BillingAddress.Line1,
        x.BillingAddress.Line2, x.BillingAddress.PostalCode, x.BillingAddress.City, x.BillingAddress.Region,
        x.BillingAddress.CountryCode, x.DeliveryAddress?.Line1, x.DeliveryAddress?.Line2, x.DeliveryAddress?.PostalCode,
        x.DeliveryAddress?.City, x.DeliveryAddress?.Region, x.DeliveryAddress?.CountryCode, x.LanguageCode, x.CurrencyCode,
        x.PaymentTermKind, x.PaymentTermDays, x.PaymentMethod, x.InvoiceDeliveryChannel, x.InvoiceDeliveryEmail,
        x.BuyerReference, x.EInvoiceIdentifier, x.EInvoiceIdentifierType, x.CreditLimit, x.CreditStatus,
        x.DefaultAccountMapping, x.DefaultDimensionCode, x.EffectiveFrom, x.EffectiveTo, x.SourceKind, x.SourceReference,
        x.UserAttestedUtc, x.ExternallyVerifiedUtc, x.VerificationSource);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private Fixture(SqliteConnection connection, VirtualCompanyDbContext db, TestAccessor accessor,
            CustomerBillingProfileService service, Guid companyA, Guid companyB, Guid customerA, Guid customerB, Guid actorId)
        { _connection = connection; Db = db; Accessor = accessor; Service = service; CompanyA = companyA; CompanyB = companyB;
            CustomerA = customerA; CustomerB = customerB; ActorId = actorId; }
        public VirtualCompanyDbContext Db { get; } public TestAccessor Accessor { get; }
        public CustomerBillingProfileService Service { get; } public Guid CompanyA { get; } public Guid CompanyB { get; }
        public Guid CustomerA { get; } public Guid CustomerB { get; } public Guid ActorId { get; }

        public static async Task<Fixture> CreateAsync(bool includeInvoice = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True"); await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options;
            await using (var schema = new VirtualCompanyDbContext(options)) { await schema.Database.EnsureCreatedAsync(); }
            var companyA = Guid.NewGuid(); var companyB = Guid.NewGuid(); var customerA = Guid.NewGuid();
            var customerB = Guid.NewGuid(); var actor = Guid.NewGuid();
            await using (var seed = new VirtualCompanyDbContext(options))
            {
                seed.Companies.AddRange(new Company(companyA, "Company A"), new Company(companyB, "Company B"));
                seed.FinanceCounterparties.AddRange(new FinanceCounterparty(customerA, companyA, "Example A", "customer"),
                    new FinanceCounterparty(customerB, companyA, "Example B", "customer"));
                if (includeInvoice) seed.FinanceInvoices.Add(new FinanceInvoice(Guid.NewGuid(), companyA, customerA,
                    "INV-1", NowUtc.AddDays(-5), NowUtc.AddDays(25), 100m, "SEK", "open"));
                await seed.SaveChangesAsync();
            }
            var accessor = new TestAccessor(companyA, actor); var db = new VirtualCompanyDbContext(options, accessor);
            var service = new CustomerBillingProfileService(db, accessor, new AuditEventWriter(db),
                new CustomerBillingTelemetry(NullLogger<CustomerBillingTelemetry>.Instance), new FixedTimeProvider(NowUtc));
            return new Fixture(connection, db, accessor, service, companyA, companyB, customerA, customerB, actor);
        }
        public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await _connection.DisposeAsync(); }
    }

    private sealed class FixedTimeProvider(DateTime nowUtc) : TimeProvider
    { public override DateTimeOffset GetUtcNow() => new(nowUtc, TimeSpan.Zero); }
    private sealed class TestAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId; public Guid? UserId => userId; public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null; public void SetCompanyId(Guid? value) => CompanyId = value;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? value) => CompanyId = value?.CompanyId;
    }
}
