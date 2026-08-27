using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingConfigurationPersistenceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 19, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Service_persists_configuration_upgrade_history_and_audit_atomically()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await SeedCompanyAndAccountsAsync(connection, companyId);
        var accessor = new TestCompanyContextAccessor(companyId, actorId);
        await using var dbContext = CreateContext(connection, accessor);
        var basePack = new CountryNeutralAccountingPolicyPack();
        var upgradedPack = new TestPack(basePack.Definition with { Version = "1.1.0" }, new string('b', 64));
        var resolver = new AccountingPolicyPackResolver([basePack, upgradedPack]);
        var service = new AccountingConfigurationService(
            dbContext,
            resolver,
            new AuditEventWriter(dbContext),
            new FixedTimeProvider(new DateTimeOffset(NowUtc)));
        var accountAssignments = await dbContext.FinanceAccounts
            .ToDictionaryAsync(account => account.Name, account => account.Id, StringComparer.OrdinalIgnoreCase);
        var roles = basePack.Definition.AccountRoles.ToDictionary(
            role => role.Key,
            role => accountAssignments[role.DisplayName],
            StringComparer.OrdinalIgnoreCase);
        var effectiveFrom = DateOnly.FromDateTime(NowUtc);

        var created = await service.CreateInitialAsync(
            new CreateInitialAccountingConfigurationCommand(
                companyId,
                "USD",
                1,
                1,
                basePack.Definition.PackKey,
                basePack.Definition.Version,
                effectiveFrom,
                2,
                AccountingRoundingModeValues.MidpointToEven,
                roles,
                actorId,
                "create-correlation"),
            CancellationToken.None);
        var preview = await service.PreviewPolicyPackSelectionAsync(
            new PreviewAccountingPolicyPackSelectionQuery(
                companyId,
                upgradedPack.Definition.PackKey,
                upgradedPack.Definition.Version,
                effectiveFrom.AddDays(1)),
            CancellationToken.None);
        var applied = await service.ApplyPolicyPackSelectionAsync(
            new ApplyAccountingPolicyPackSelectionCommand(
                companyId,
                upgradedPack.Definition.PackKey,
                upgradedPack.Definition.Version,
                effectiveFrom.AddDays(1),
                created.Configuration!.Version,
                new Dictionary<string, Guid>(),
                actorId,
                "upgrade-correlation"),
            CancellationToken.None);

        Assert.True(created.IsReady);
        Assert.True(preview.IsAllowed);
        Assert.True(applied.IsReady);
        Assert.Equal("1.1.0", applied.Configuration!.PolicyPackVersion);
        Assert.Equal(2, applied.Configuration.PolicyPackHistory.Count);
        Assert.Equal(effectiveFrom, applied.Configuration.PolicyPackHistory[0].EffectiveTo);
        Assert.Equal(basePack.DefinitionHash, applied.Configuration.PolicyPackHistory[0].DefinitionHash);
        Assert.Equal(upgradedPack.DefinitionHash, applied.Configuration.PolicyPackHistory[1].DefinitionHash);
        Assert.Equal(3, await dbContext.AuditEvents.CountAsync());
    }

    [Fact]
    public async Task Company_unique_configuration_and_optimistic_concurrency_are_enforced()
    {
        await using var connection = await OpenConnectionAsync();
        var companyId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await SeedCompanyAndAccountsAsync(connection, companyId);
        var accessor = new TestCompanyContextAccessor(companyId, actorId);

        await using (var setup = CreateContext(connection, accessor))
        {
            setup.AccountingConfigurations.Add(CreateConfiguration(companyId, actorId, Guid.NewGuid()));
            await setup.SaveChangesAsync();
            setup.ChangeTracker.Clear();
            setup.AccountingConfigurations.Add(CreateConfiguration(companyId, actorId, Guid.NewGuid()));
            await Assert.ThrowsAsync<DbUpdateException>(() => setup.SaveChangesAsync());
        }

        await using var first = CreateContext(connection, accessor);
        await using var second = CreateContext(connection, accessor);
        var firstCopy = await first.AccountingConfigurations.SingleAsync();
        var secondCopy = await second.AccountingConfigurations.SingleAsync();
        firstCopy.ApplyPolicyPack("country-neutral", "1.1.0", new DateOnly(2026, 8, 20), actorId, NowUtc.AddMinutes(1));
        secondCopy.ApplyPolicyPack("country-neutral", "1.2.0", new DateOnly(2026, 8, 21), actorId, NowUtc.AddMinutes(2));

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Tenant_filter_and_composite_account_foreign_key_prevent_cross_company_access()
    {
        await using var connection = await OpenConnectionAsync();
        var companyA = Guid.NewGuid();
        var companyB = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        await SeedCompanyAndAccountsAsync(connection, companyA);
        await SeedCompanyAndAccountsAsync(connection, companyB);

        await using (var seed = CreateContext(connection))
        {
            seed.AccountingConfigurations.Add(CreateConfiguration(companyA, actorId, Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }

        await using var companyBContext = CreateContext(connection, new TestCompanyContextAccessor(companyB, actorId));
        Assert.Empty(await companyBContext.AccountingConfigurations.ToListAsync());

        await using var companyAContext = CreateContext(connection, new TestCompanyContextAccessor(companyA, actorId));
        var configuration = await companyAContext.AccountingConfigurations.SingleAsync();
        var companyBAccountId = await companyAContext.FinanceAccounts
            .IgnoreQueryFilters()
            .Where(account => account.CompanyId == companyB)
            .Select(account => account.Id)
            .FirstAsync();
        companyAContext.AccountingConfigurationAccountRoles.Add(new AccountingConfigurationAccountRole(
            Guid.NewGuid(),
            companyA,
            configuration.Id,
            "cash",
            companyBAccountId,
            NowUtc));

        await Assert.ThrowsAsync<DbUpdateException>(() => companyAContext.SaveChangesAsync());
    }

    private static AccountingConfiguration CreateConfiguration(Guid companyId, Guid actorId, Guid id) =>
        new(
            id,
            companyId,
            "USD",
            1,
            1,
            AccountingPolicyPackDefaults.CountryNeutralPackKey,
            AccountingPolicyPackDefaults.CountryNeutralVersion,
            new DateOnly(2026, 8, 19),
            2,
            AccountingRoundingModeValues.MidpointToEven,
            actorId,
            NowUtc);

    private static async Task SeedCompanyAndAccountsAsync(SqliteConnection connection, Guid companyId)
    {
        await using var context = CreateContext(connection);
        if (!await context.Companies.AnyAsync(company => company.Id == companyId))
        {
            context.Companies.Add(new Company(companyId, $"Company {companyId:N}"));
            var pack = new CountryNeutralAccountingPolicyPack();
            foreach (var role in pack.Definition.AccountRoles)
            {
                var templateAccount = pack.Definition.ChartTemplates.SelectMany(chart => chart.Accounts)
                    .FirstOrDefault(account => string.Equals(account.DefaultRoleKey, role.Key, StringComparison.OrdinalIgnoreCase));
                context.FinanceAccounts.Add(new FinanceAccount(
                    Guid.NewGuid(),
                    companyId,
                    $"{context.ChangeTracker.Entries<FinanceAccount>().Count() + 1000}",
                    role.DisplayName,
                    role.Key,
                    "USD",
                    0m,
                    NowUtc,
                    accountClass: templateAccount?.AccountClass,
                    normalBalance: templateAccount?.NormalBalance,
                    effectiveFrom: DateOnly.FromDateTime(NowUtc),
                    isPostingEnabled: true));
            }

            await context.SaveChangesAsync();
        }
    }

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
        new(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite(connection)
                .Options,
            accessor);

    private sealed class TestCompanyContextAccessor(Guid companyId, Guid userId) : ICompanyContextAccessor
    {
        public Guid? CompanyId { get; private set; } = companyId;
        public Guid? UserId => userId;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? resolvedCompanyId) => CompanyId = resolvedCompanyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) => CompanyId = companyContext?.CompanyId;
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestPack(AccountingPolicyPackDefinition definition, string definitionHash) : IAccountingPolicyPack
    {
        public AccountingPolicyPackDefinition Definition { get; } = definition;
        public string DefinitionHash { get; } = definitionHash;
    }
}
