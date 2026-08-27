using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingAdministrationServiceTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly InitialFiscalYearStart = new(2026, 1, 1);

    [Fact]
    public async Task Complete_setup_is_atomic_idempotent_and_applies_the_country_neutral_template()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        var command = fixture.CreateSetupCommand();

        var first = await fixture.Service.CompleteSetupAsync(command, CancellationToken.None);
        var replay = await fixture.Service.CompleteSetupAsync(command, CancellationToken.None);

        Assert.False(first.WasAlreadyApplied);
        Assert.True(replay.WasAlreadyApplied);
        Assert.True(first.SetupStatus.IsReady);
        Assert.False(first.SetupStatus.IsCountrySpecificComplianceConfigured);
        Assert.Equal(6, await fixture.Context.FinanceAccounts.CountAsync());
        Assert.Equal(6, await fixture.Context.AccountingConfigurationAccountRoles.CountAsync());
        Assert.Equal(12, await fixture.Context.FiscalPeriods.CountAsync());
        Assert.Equal(5, await fixture.Context.VoucherSeries.CountAsync());
        Assert.Single(await fixture.Context.AccountingConfigurations.ToListAsync());

        var auditActions = await fixture.Context.AuditEvents.Select(audit => audit.Action).ToListAsync();
        Assert.Contains(AuditEventActions.AccountingSetupCompleted, auditActions);
        Assert.Contains(AuditEventActions.AccountingFiscalYearCreated, auditActions);
    }

    [Fact]
    public async Task Complete_setup_preserves_imported_account_collisions_and_uses_safe_internal_codes()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        fixture.Context.FinanceAccounts.AddRange(
            ImportedAccount(fixture.CompanyId, "3000", "Försäljning inom Sverige", "revenue"),
            ImportedAccount(fixture.CompanyId, "4000", "Inköp av handelsvaror (gruppkonto)", "expense"),
            ImportedAccount(fixture.CompanyId, "5000", "Lokalkostnader (gruppkonto)", "expense"));
        await fixture.Context.SaveChangesAsync();

        var command = fixture.CreateSetupCommand() with { BaseCurrency = "SEK" };
        var preview = await fixture.Service.PreviewSetupAsync(
            new PreviewAccountingSetupQuery(
                command.CompanyId,
                command.BaseCurrency,
                command.FiscalYearStart,
                command.PolicyPackKey,
                command.PolicyPackVersion,
                command.ChartTemplateKey),
            CancellationToken.None);

        Assert.True(preview.IsValid);
        Assert.Contains(preview.Accounts, account => account.Code == "VC-3000" && account.AccountClass == "Equity");
        Assert.Contains(preview.Accounts, account => account.Code == "VC-4000" && account.AccountClass == "Income");
        Assert.Contains(preview.Accounts, account => account.Code == "5000" && account.AccountClass == "Expense");
        Assert.Equal(2, preview.Warnings.Count(warning => warning.ReasonCode == AccountingConfigurationReasonCodes.SetupConflict));

        var completion = await fixture.Service.CompleteSetupAsync(command, CancellationToken.None);
        var replay = await fixture.Service.CompleteSetupAsync(command, CancellationToken.None);

        Assert.True(completion.SetupStatus.IsReady);
        Assert.True(replay.WasAlreadyApplied);
        Assert.Equal("revenue", (await fixture.Context.FinanceAccounts.SingleAsync(account => account.Code == "3000")).AccountType);
        Assert.Equal("expense", (await fixture.Context.FinanceAccounts.SingleAsync(account => account.Code == "4000")).AccountType);
        var compatibleExpense = await fixture.Context.FinanceAccounts.SingleAsync(account => account.Code == "5000");
        Assert.Equal(FinanceAccountClassValues.Expense, compatibleExpense.AccountClass);
        Assert.Equal(FinanceNormalBalanceValues.Debit, compatibleExpense.NormalBalance);
        Assert.True(compatibleExpense.IsPostingEnabled);
        Assert.NotNull(await fixture.Context.FinanceAccounts.SingleOrDefaultAsync(account => account.Code == "VC-3000"));
        Assert.NotNull(await fixture.Context.FinanceAccounts.SingleOrDefaultAsync(account => account.Code == "VC-4000"));
    }

    [Fact]
    public async Task Fiscal_year_creation_completes_missing_months_and_then_replays_without_duplicates()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        await fixture.Service.CompleteSetupAsync(fixture.CreateSetupCommand(), CancellationToken.None);
        var nextYearStart = InitialFiscalYearStart.AddYears(1);
        fixture.Context.FiscalPeriods.Add(new FiscalPeriod(
            Guid.NewGuid(),
            fixture.CompanyId,
            "Jan 2027",
            ToUtc(nextYearStart),
            ToUtc(nextYearStart.AddMonths(1)),
            createdUtc: NowUtc,
            updatedUtc: NowUtc));
        await fixture.Context.SaveChangesAsync();

        var command = new CreateAccountingFiscalYearCommand(
            fixture.CompanyId,
            nextYearStart,
            fixture.ActorId,
            $"fiscal-year:{fixture.CompanyId:N}:{nextYearStart:yyyyMMdd}");
        var completed = await fixture.Service.CreateFiscalYearAsync(command, CancellationToken.None);
        var replay = await fixture.Service.CreateFiscalYearAsync(command, CancellationToken.None);

        Assert.False(completed.WasAlreadyPresent);
        Assert.True(replay.WasAlreadyPresent);
        Assert.Equal(12, completed.FiscalYear.Periods.Count);
        Assert.Equal(12, replay.FiscalYear.Periods.Count);
        Assert.Equal(12, await fixture.Context.FiscalPeriods.CountAsync(period =>
            period.StartUtc >= ToUtc(nextYearStart) && period.StartUtc < ToUtc(nextYearStart.AddYears(1))));
    }

    [Fact]
    public async Task Protected_setup_account_cannot_be_deactivated()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        await fixture.Service.CompleteSetupAsync(fixture.CreateSetupCommand(), CancellationToken.None);
        var cash = await fixture.Context.FinanceAccounts.SingleAsync(account => account.Code == "1000");

        var exception = await Assert.ThrowsAsync<AccountingConfigurationException>(() =>
            fixture.Service.DeactivateAccountAsync(
                new DeactivateAccountingAccountCommand(
                    fixture.CompanyId,
                    cash.Id,
                    new DateOnly(2026, 12, 31),
                    cash.UpdatedUtc,
                    fixture.ActorId),
                CancellationToken.None));

        Assert.Equal(AccountingConfigurationReasonCodes.AccountProtected, exception.ReasonCode);
        fixture.Context.ChangeTracker.Clear();
        Assert.True((await fixture.Context.FinanceAccounts.SingleAsync(account => account.Id == cash.Id)).IsPostingEnabled);
    }

    [Fact]
    public void Bas_2026_catalogue_preserves_the_complete_workbook_and_ambiguous_source_name()
    {
        var catalog = new Bas2026AccountingChartCatalog();

        Assert.Equal(1282, catalog.Accounts.Count);
        Assert.Equal(Bas2026AccountingChartCatalog.ExpectedSourceSha256, catalog.SourceSha256);
        Assert.Equal(26, catalog.Accounts.Count(account => !account.IsK2Allowed));
        Assert.True(catalog.TryGetAccount("1510", out var receivables));
        Assert.Equal("Kundfordringar", receivables!.NameSv);
        Assert.Equal(FinanceAccountClassValues.Asset, receivables.SuggestedAccountClass);
        Assert.True(catalog.TryGetAccount("2087", out var ambiguous));
        Assert.Equal(["Bunden överkursfond", "Insatsemission"], ambiguous!.NameVariantsSv);
        Assert.True(catalog.TryGetAccount("8310", out var classEight));
        Assert.Null(classEight!.SuggestedAccountClass);
        Assert.Null(classEight.SuggestedNormalBalance);
    }

    [Fact]
    public async Task Catalogue_search_and_account_creation_use_checked_in_BAS_values()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();
        var page = await fixture.Service.GetChartCatalogAsync(new GetAccountingChartCatalogQuery(
            fixture.CompanyId,
            AccountingChartCatalogDefaults.Bas2026CatalogKey,
            AccountingChartCatalogDefaults.Bas2026CatalogVersion,
            Search: "1510"), CancellationToken.None);

        Assert.Equal(1282, page.TotalAccountCount);
        var catalogAccount = Assert.Single(page.Accounts);
        Assert.Equal("Kundfordringar", catalogAccount.NameSv);
        Assert.True(catalogAccount.RequiresSemanticsConfirmation);
        Assert.True(catalogAccount.RequiresCompanySuitabilityConfirmation);
        Assert.False(catalogAccount.IsAlreadyAdded);

        await fixture.Service.CompleteSetupAsync(fixture.CreateSetupCommand(), CancellationToken.None);
        var created = await fixture.Service.CreateAccountFromCatalogAsync(new CreateAccountingAccountFromCatalogCommand(
            fixture.CompanyId,
            AccountingChartCatalogDefaults.Bas2026CatalogKey,
            AccountingChartCatalogDefaults.Bas2026CatalogVersion,
            "1510",
            NameSv: null,
            AccountClass: null,
            NormalBalance: null,
            AccountingSemanticsConfirmed: true,
            CompanySuitabilityConfirmed: true,
            EffectiveFrom: InitialFiscalYearStart,
            ActorUserId: fixture.ActorId,
            CorrelationId: "bas-catalog-test"), CancellationToken.None);

        Assert.Equal("1510", created.Code);
        Assert.Equal("Kundfordringar", created.Name);
        Assert.Equal("Asset", created.AccountClass);
        Assert.Equal("Debit", created.NormalBalance);
        var audit = await fixture.Context.AuditEvents.SingleAsync(item =>
            item.Action == AuditEventActions.AccountingAccountCreated && item.TargetId == created.Id.ToString("D"));
        Assert.Equal(AccountingChartCatalogDefaults.Bas2026CatalogKey, audit.Metadata["sourceCatalogKey"]);
        Assert.Equal(Bas2026AccountingChartCatalog.ExpectedSourceSha256, audit.Metadata["sourceCatalogSha256"]);
        Assert.Equal("true", audit.Metadata["accountingSemanticsConfirmed"]);
        Assert.Equal("true", audit.Metadata["companySuitabilityConfirmed"]);
    }

    [Fact]
    public async Task Catalogue_creation_requires_accountant_input_for_ambiguous_names_and_class_eight_semantics()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();

        var ambiguousName = await Assert.ThrowsAsync<AccountingConfigurationException>(() =>
            fixture.Service.CreateAccountFromCatalogAsync(new CreateAccountingAccountFromCatalogCommand(
                fixture.CompanyId,
                AccountingChartCatalogDefaults.Bas2026CatalogKey,
                AccountingChartCatalogDefaults.Bas2026CatalogVersion,
                "2087",
                null,
                null,
                null,
                true,
                true,
                InitialFiscalYearStart,
                fixture.ActorId), CancellationToken.None));
        Assert.Equal(AccountingConfigurationReasonCodes.ChartCatalogNameSelectionRequired, ambiguousName.ReasonCode);

        var missingSemantics = await Assert.ThrowsAsync<AccountingConfigurationException>(() =>
            fixture.Service.CreateAccountFromCatalogAsync(new CreateAccountingAccountFromCatalogCommand(
                fixture.CompanyId,
                AccountingChartCatalogDefaults.Bas2026CatalogKey,
                AccountingChartCatalogDefaults.Bas2026CatalogVersion,
                "8310",
                null,
                null,
                null,
                true,
                true,
                InitialFiscalYearStart,
                fixture.ActorId), CancellationToken.None));
        Assert.Equal(AccountingConfigurationReasonCodes.ChartCatalogSemanticsRequired, missingSemantics.ReasonCode);
    }

    [Fact]
    public async Task Catalogue_creation_requires_explicit_semantics_and_company_suitability_confirmations()
    {
        await using var fixture = await AdministrationFixture.CreateAsync();

        var missingSemanticsConfirmation = await Assert.ThrowsAsync<AccountingConfigurationException>(() =>
            fixture.Service.CreateAccountFromCatalogAsync(new CreateAccountingAccountFromCatalogCommand(
                fixture.CompanyId,
                AccountingChartCatalogDefaults.Bas2026CatalogKey,
                AccountingChartCatalogDefaults.Bas2026CatalogVersion,
                "1510",
                null,
                "asset",
                "debit",
                false,
                true,
                InitialFiscalYearStart,
                fixture.ActorId), CancellationToken.None));
        Assert.Equal(AccountingConfigurationReasonCodes.ChartCatalogSemanticsConfirmationRequired, missingSemanticsConfirmation.ReasonCode);

        var missingSuitabilityConfirmation = await Assert.ThrowsAsync<AccountingConfigurationException>(() =>
            fixture.Service.CreateAccountFromCatalogAsync(new CreateAccountingAccountFromCatalogCommand(
                fixture.CompanyId,
                AccountingChartCatalogDefaults.Bas2026CatalogKey,
                AccountingChartCatalogDefaults.Bas2026CatalogVersion,
                "1510",
                null,
                "asset",
                "debit",
                true,
                false,
                InitialFiscalYearStart,
                fixture.ActorId), CancellationToken.None));
        Assert.Equal(AccountingConfigurationReasonCodes.ChartCatalogCompanySuitabilityConfirmationRequired, missingSuitabilityConfirmation.ReasonCode);
    }

    private static DateTime ToUtc(DateOnly value) => value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    private static FinanceAccount ImportedAccount(Guid companyId, string code, string name, string accountType) =>
        new(Guid.NewGuid(), companyId, code, name, accountType, "SEK", 0m, NowUtc.AddDays(-30));

    private sealed class AdministrationFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private AdministrationFixture(
            SqliteConnection connection,
            VirtualCompanyDbContext context,
            AccountingAdministrationService service,
            Guid companyId,
            Guid actorId)
        {
            _connection = connection;
            Context = context;
            Service = service;
            CompanyId = companyId;
            ActorId = actorId;
        }

        public VirtualCompanyDbContext Context { get; }
        public AccountingAdministrationService Service { get; }
        public Guid CompanyId { get; }
        public Guid ActorId { get; }

        public CompleteAccountingSetupCommand CreateSetupCommand() => new(
            CompanyId,
            "USD",
            InitialFiscalYearStart,
            AccountingPolicyPackDefaults.CountryNeutralPackKey,
            AccountingPolicyPackDefaults.CountryNeutralVersion,
            "generic-accrual",
            AccountRoleCodeAssignments: null,
            ActorId,
            $"accounting-setup:{CompanyId:N}",
            "accounting-administration-test");

        public static async Task<AdministrationFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:;Foreign Keys=True");
            await connection.OpenAsync();
            var companyId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var accessor = new TestCompanyContextAccessor(companyId, actorId);
            var context = new VirtualCompanyDbContext(
                new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite(connection).Options,
                accessor);
            await context.Database.EnsureCreatedAsync();
            context.Companies.Add(new Company(companyId, "Accounting administration company"));
            await context.SaveChangesAsync();

            var pack = new CountryNeutralAccountingPolicyPack();
            var resolver = new AccountingPolicyPackResolver([pack]);
            var chartCatalogResolver = new AccountingChartCatalogResolver([new Bas2026AccountingChartCatalog()]);
            var clock = new FixedTimeProvider(new DateTimeOffset(NowUtc));
            var auditWriter = new AuditEventWriter(context);
            var configurationService = new AccountingConfigurationService(context, resolver, auditWriter, clock);
            var service = new AccountingAdministrationService(context, resolver, chartCatalogResolver, configurationService, auditWriter, clock);
            return new AdministrationFixture(connection, context, service, companyId, actorId);
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

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
}
