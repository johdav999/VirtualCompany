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

public sealed class CompanyStatutoryProfileTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 24, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Swedish_identifiers_are_normalized_without_claiming_external_verification()
    {
        var profile = new CompanyStatutoryProfile(
            Guid.NewGuid(), Guid.NewGuid(), CreateValues(), Guid.NewGuid(), NowUtc);

        Assert.Equal("5560160680", profile.SwedishOrganisationNumber);
        Assert.Equal("SE556016068001", profile.VatRegistrationNumber);
        Assert.True(profile.IsFormatComplete);
        Assert.True(profile.IsUserAttested);
        Assert.Equal(StatutoryVerificationStatusValues.Unverified, profile.VerificationStatus);
        Assert.Null(profile.ExternallyVerifiedUtc);
    }

    [Fact]
    public void Invalid_identifier_checksum_is_rejected_as_structure_not_registry_status()
    {
        var values = CreateValues() with { SwedishOrganisationNumber = "556016-0681", VatRegistrationNumber = null };

        var exception = Assert.Throws<ArgumentException>(() => new CompanyStatutoryProfile(
            Guid.NewGuid(), Guid.NewGuid(), values, Guid.NewGuid(), NowUtc));

        Assert.Contains("format checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_update_and_stale_version_are_atomic_and_audited()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var accessor = new TestCompanyContextAccessor(companyId, actorId);
        await using var db = CreateContext(connection, accessor);
        db.Companies.Add(new Company(companyId, "Swedish company"));
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var created = await service.CreateAsync(new CreateCompanyStatutoryProfileCommand(
            companyId, CreateInput(), actorId, "create-correlation"), default);
        var updated = await service.UpdateAsync(new UpdateCompanyStatutoryProfileCommand(
            companyId, created.Profile!.Version, CreateInput() with { LegalName = "Updated Legal AB" }, actorId,
            "update-correlation"), default);

        var stale = await Assert.ThrowsAsync<CompanyStatutoryProfileException>(() => service.UpdateAsync(
            new UpdateCompanyStatutoryProfileCommand(companyId, created.Profile.Version,
                CreateInput() with { LegalName = "Stale Legal AB" }, actorId), default));
        Assert.Equal(CompanyStatutoryProfileReasonCodes.ConcurrencyConflict, stale.ReasonCode);
        Assert.Equal("Updated Legal AB", updated.Profile!.LegalName);
        Assert.Equal(2, updated.Profile.Version);
        Assert.Equal(2, await db.AuditEvents.CountAsync());
        Assert.All(await db.AuditEvents.ToListAsync(), audit =>
        {
            Assert.NotNull(audit.PayloadDiffJson);
            Assert.Contains("after", audit.PayloadDiffJson, StringComparison.Ordinal);
        });
        Assert.Equal("Updated Legal AB", (await db.CompanyStatutoryProfiles.SingleAsync()).LegalName);
    }

    [Fact]
    public async Task Persistence_model_has_tenant_unique_key_and_concurrency_token()
    {
        await using var connection = await OpenConnectionAsync();
        await using var db = CreateContext(connection);
        var entity = db.Model.FindEntityType(typeof(CompanyStatutoryProfile));

        Assert.NotNull(entity);
        Assert.Equal("company_statutory_profiles", entity.GetTableName());
        Assert.True(entity.FindProperty(nameof(CompanyStatutoryProfile.Version))!.IsConcurrencyToken);
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual([nameof(CompanyStatutoryProfile.CompanyId)]));
    }

    [Fact]
    public async Task Query_filter_does_not_disclose_another_company_profile()
    {
        await using var connection = await OpenConnectionAsync();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await using (var seed = CreateContext(connection))
        {
            seed.Companies.AddRange(new Company(companyA, "A"), new Company(companyB, "B"));
            seed.CompanyStatutoryProfiles.Add(new CompanyStatutoryProfile(
                Guid.NewGuid(), companyA, CreateValues(), actorId, NowUtc));
            await seed.SaveChangesAsync();
        }

        var accessor = new TestCompanyContextAccessor(companyB, actorId);
        await using var scoped = CreateContext(connection, accessor);
        var status = await CreateService(scoped).GetAsync(new GetCompanyStatutoryProfileQuery(companyB), default);

        Assert.False(status.Exists);
        Assert.Null(status.Profile);
        Assert.Empty(await scoped.CompanyStatutoryProfiles.ToListAsync());
    }

    [Fact]
    public void Swedish_foundation_hash_is_deterministic_and_capabilities_are_honestly_unvalidated()
    {
        var first = new SwedishFoundationAccountingPolicyPack();
        var second = new SwedishFoundationAccountingPolicyPack();

        Assert.Equal(first.DefinitionHash, second.DefinitionHash);
        Assert.Equal(64, first.DefinitionHash.Length);
        Assert.False(first.Definition.IsStatutoryComplianceValidated);
        Assert.Equal("SE", first.Definition.CountryOrRegion);
        Assert.Equal("unsupported", first.Definition.CapabilityStates![AccountingPolicyCapabilityKeys.CountrySpecificTax]);
        Assert.DoesNotContain(AccountingPolicyCapabilityKeys.CountrySpecificTax, first.Definition.SupportedCapabilities);
        Assert.Empty(first.Definition.TaxRules);
    }

    private static CompanyStatutoryProfileValues CreateValues() => new(
        "Example Legal AB",
        "556016-0680",
        "SE 5560160680 01",
        StatutoryVatRegistrationStatusValues.Registered,
        "Examplegatan 1",
        null,
        "111 22",
        "Stockholm",
        "SE",
        null,
        null,
        null,
        null,
        null,
        "SE",
        "SEK",
        StatutoryFiscalYearBasisValues.CalendarYear,
        StatutoryBookkeepingMethodValues.Accrual,
        new DateOnly(2000, 1, 1),
        new DateOnly(2000, 1, 1),
        null,
        true,
        StatutoryVerificationStatusValues.Unverified,
        "user_entry",
        "internal-profile-form",
        NowUtc,
        null,
        null);

    private static CompanyStatutoryProfileInput CreateInput() => new(
        "Example Legal AB",
        "556016-0680",
        "SE556016068001",
        StatutoryVatRegistrationStatusValues.Registered,
        new StatutoryAddressDto("Examplegatan 1", null, "111 22", "Stockholm", "SE"),
        null,
        "SE",
        "SEK",
        StatutoryFiscalYearBasisValues.CalendarYear,
        StatutoryBookkeepingMethodValues.Accrual,
        new DateOnly(2000, 1, 1),
        new DateOnly(2000, 1, 1),
        null,
        true,
        StatutoryVerificationStatusValues.Unverified,
        "user_entry",
        "internal-profile-form",
        NowUtc,
        null,
        null);

    private static CompanyStatutoryProfileService CreateService(VirtualCompanyDbContext db) => new(
        db,
        new AuditEventWriter(db),
        new AccountingOperationsTelemetry(NullLogger<AccountingOperationsTelemetry>.Instance),
        new FixedTimeProvider(NowUtc));

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
        await connection.OpenAsync();
        await using var context = CreateContext(connection);
        await context.Database.EnsureCreatedAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(
        SqliteConnection connection,
        ICompanyContextAccessor? accessor = null) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options, accessor);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => true;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) => CompanyId = resolvedCompanyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }
}
