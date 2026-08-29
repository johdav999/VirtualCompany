using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;
using Xunit;

namespace VirtualCompany.Finance.Tests;

public sealed class BankConnectionServiceTests
{
    [Fact]
    public async Task Callback_is_company_bound_one_time_and_credentials_are_not_stored_in_business_rows()
    {
        await using var db = CreateDb(); var provider = new FakeProvider(); var clock = new FixedTimeProvider(Utc(2026, 8, 28, 8));
        var service = CreateService(db, provider, clock); var company = Guid.NewGuid(); var otherCompany = Guid.NewGuid(); var user = Guid.NewGuid();
        await service.StartAsync(new(company, user, "test-bank", "bank-1", new("https://api.example.test/finance/bank-connections/test-bank/callback"),
            new("https://app.example.test/finance/settings/bank-connections"), ["accounts", "transactions"]), default);
        var state = provider.LastStart!.ProtectedState;

        var crossCompany = await Assert.ThrowsAsync<BankConnectionOperationException>(() => service.CompleteCallbackAsync(
            new(otherCompany, user, "test-bank", state, "code", null), default));
        Assert.Equal(BankConnectionReasonCodes.CallbackStateInvalid, crossCompany.ReasonCode);
        Assert.Equal(0, provider.CompleteCalls);

        var completed = await service.CompleteCallbackAsync(new(company, user, "test-bank", state, "code", null), default);
        Assert.Equal(BankConnectionStatuses.Active, completed.Status);
        Assert.Equal(1, provider.CompleteCalls);
        var replay = await Assert.ThrowsAsync<BankConnectionOperationException>(() => service.CompleteCallbackAsync(
            new(company, user, "test-bank", state, "code", null), default));
        Assert.Equal(BankConnectionReasonCodes.CallbackReplay, replay.ReasonCode);
        Assert.Equal(1, provider.CompleteCalls);

        var connection = await db.BankConnections.IgnoreQueryFilters().SingleAsync();
        var credential = await db.BankConnectionCredentials.IgnoreQueryFilters().SingleAsync();
        Assert.DoesNotContain("access-secret", credential.EncryptedEnvelope, StringComparison.Ordinal);
        Assert.DoesNotContain("refresh-secret", credential.EncryptedEnvelope, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", connection.InstitutionName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mapping_requires_verified_ownership_and_is_versioned_audited_and_company_scoped()
    {
        await using var db = CreateDb(); var provider = new FakeProvider(); var clock = new FixedTimeProvider(Utc(2026, 8, 28, 9));
        var service = CreateService(db, provider, clock); var company = Guid.NewGuid(); var user = Guid.NewGuid();
        var connection = await Connect(service, provider, company, user);
        var financeAccount = new FinanceAccount(Guid.NewGuid(), company, "1930", "Operating bank", "asset", "SEK", 0, clock.GetUtcNow().UtcDateTime);
        var bankAccount = new CompanyBankAccount(Guid.NewGuid(), company, financeAccount.Id, "Operating account", "Internal bank", "•••• 1111", "SEK");
        db.FinanceAccounts.Add(financeAccount); db.CompanyBankAccounts.Add(bankAccount); await db.SaveChangesAsync();
        var status = await service.GetStatusAsync(company, default); var discovered = Assert.Single(Assert.Single(status.Connections).Accounts);

        var mapped = await service.MapAccountAsync(new(company, connection.ConnectionId, discovered.Id, bankAccount.Id, user,
            Assert.Single(status.Connections).Version, "Verified explicit mapping"), default);
        Assert.Equal(1, mapped.MappingVersion);
        var row = await db.BankAccountMappings.IgnoreQueryFilters().SingleAsync();
        Assert.True(row.IsCurrent); Assert.Equal(company, row.CompanyId); Assert.Equal(bankAccount.Id, row.CompanyBankAccountId);
        Assert.Contains(await db.BankConnectionAuditEvents.IgnoreQueryFilters().ToListAsync(), x => x.EventType == "account_mapped");

        var crossCompany = await Assert.ThrowsAsync<BankConnectionOperationException>(() => service.MapAccountAsync(
            new(Guid.NewGuid(), connection.ConnectionId, discovered.Id, bankAccount.Id, user, mapped.ConnectionVersion, "cross company"), default));
        Assert.Equal("bank_connection_not_found", crossCompany.ReasonCode);
    }

    [Fact]
    public async Task Expired_or_disconnected_consent_blocks_provider_calls_and_disconnect_queues_revocation()
    {
        await using var db = CreateDb(); var provider = new FakeProvider { ConsentExpiresUtc = Utc(2026, 8, 28, 7) };
        var clock = new FixedTimeProvider(Utc(2026, 8, 28, 10)); var service = CreateService(db, provider, clock);
        var company = Guid.NewGuid(); var user = Guid.NewGuid(); var completed = await Connect(service, provider, company, user);
        var access = await service.GetSynchronizationAccessAsync(company, completed.ConnectionId, default);
        Assert.False(access.Allowed); Assert.Equal(BankConnectionReasonCodes.ExpiredConsent, access.ReasonCode); Assert.True(access.RenewalRequired);
        await Assert.ThrowsAsync<BankConnectionOperationException>(() => service.RefreshAsync(new(company, completed.ConnectionId, user, 2), default));
        Assert.Equal(0, provider.HealthCalls);

        var status = await service.GetStatusAsync(company, default); var current = Assert.Single(status.Connections);
        await service.DisconnectAsync(new(company, current.Id, user, current.Version, "Compromised consent"), default);
        Assert.Equal(0, provider.RevokeCalls);
        Assert.Single(await db.BankConsentRevocationTasks.IgnoreQueryFilters().ToListAsync());
        var disconnected = await service.GetSynchronizationAccessAsync(company, current.Id, default);
        Assert.False(disconnected.Allowed); Assert.Equal(BankConnectionReasonCodes.Disconnected, disconnected.ReasonCode);
    }

    [Fact]
    public async Task Provider_errors_are_translated_to_safe_stable_reason_codes()
    {
        await using var db = CreateDb(); var provider = new FakeProvider { StartFailure = new("provider_outage", "The provider is temporarily unavailable.", true, new InvalidOperationException("raw transport detail")) };
        var service = CreateService(db, provider, new FixedTimeProvider(Utc(2026, 8, 28, 11)));
        var exception = await Assert.ThrowsAsync<BankConnectionOperationException>(() => service.StartAsync(new(Guid.NewGuid(), Guid.NewGuid(),
            "test-bank", "bank-1", new("https://api.example.test/finance/bank-connections/test-bank/callback"), null, []), default));
        Assert.Equal("provider_outage", exception.ReasonCode); Assert.Equal("The provider is temporarily unavailable.", exception.SafeMessage);
        Assert.DoesNotContain("raw transport", exception.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mapping_rejects_an_account_without_verified_ownership_before_persisting_it()
    {
        await using var db = CreateDb();
        var provider = new FakeProvider { OwnershipStatus = BankAccountOwnershipStatuses.Mismatch };
        var clock = new FixedTimeProvider(Utc(2026, 8, 28, 11));
        var service = CreateService(db, provider, clock); var company = Guid.NewGuid(); var user = Guid.NewGuid();
        var connection = await Connect(service, provider, company, user);
        var financeAccount = new FinanceAccount(Guid.NewGuid(), company, "1930", "Operating bank", "asset", "SEK", 0, clock.GetUtcNow().UtcDateTime);
        var bankAccount = new CompanyBankAccount(Guid.NewGuid(), company, financeAccount.Id, "Operating account", "Internal bank", "•••• 1111", "SEK");
        db.FinanceAccounts.Add(financeAccount); db.CompanyBankAccounts.Add(bankAccount); await db.SaveChangesAsync();
        var status = await service.GetStatusAsync(company, default); var discovered = Assert.Single(Assert.Single(status.Connections).Accounts);

        var error = await Assert.ThrowsAsync<BankConnectionOperationException>(() => service.MapAccountAsync(
            new(company, connection.ConnectionId, discovered.Id, bankAccount.Id, user, Assert.Single(status.Connections).Version, "Explicit mapping"), default));

        Assert.Equal(BankConnectionReasonCodes.OwnershipMismatch, error.ReasonCode);
        Assert.Empty(await db.BankAccountMappings.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Stale_connection_version_is_reported_as_a_safe_concurrency_conflict()
    {
        await using var db = CreateDb(); var provider = new FakeProvider(); var clock = new FixedTimeProvider(Utc(2026, 8, 28, 11));
        var service = CreateService(db, provider, clock); var company = Guid.NewGuid(); var user = Guid.NewGuid();
        var completed = await Connect(service, provider, company, user);

        var error = await Assert.ThrowsAsync<BankConnectionOperationException>(() => service.SuspendAsync(
            new(company, completed.ConnectionId, user, 1, "Review requested"), default));

        Assert.Equal(BankConnectionReasonCodes.ConcurrencyConflict, error.ReasonCode);
        Assert.True(error.IsConflict);
        Assert.Equal(BankConnectionStatuses.Active, Assert.Single((await service.GetStatusAsync(company, default)).Connections).Status);
    }

    private static async Task<BankConsentCallbackResult> Connect(BankConnectionService service, FakeProvider provider, Guid company, Guid user)
    {
        await service.StartAsync(new(company, user, "test-bank", "bank-1", new("https://api.example.test/finance/bank-connections/test-bank/callback"), null,
            ["accounts", "account_ownership", "transactions"]), default);
        return await service.CompleteCallbackAsync(new(company, user, "test-bank", provider.LastStart!.ProtectedState, "code", null), default);
    }
    private static BankConnectionService CreateService(VirtualCompanyDbContext db, FakeProvider provider, TimeProvider clock)
    {
        var protection = new EphemeralDataProtectionProvider();
        return new BankConnectionService(db, new BankConnectionProviderRegistry([provider]), new DataProtectionBankConsentStateProtector(protection),
            new ProtectedBankCredentialStore(db, new DataProtectionFieldEncryptionService(protection)),
            new BankConnectionTelemetry(NullLogger<BankConnectionTelemetry>.Instance), clock);
    }
    private static VirtualCompanyDbContext CreateDb()
    {
        var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
    private static DateTime Utc(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider { public override DateTimeOffset GetUtcNow() => new(now); }
    private sealed class FakeProvider : IBankConnectionProvider
    {
        public BankProviderDescriptor Descriptor { get; } = new("test-bank", "Test bank provider", ["accounts", "account_ownership", "transactions"], true);
        public BankProviderConsentStartRequest? LastStart { get; private set; }
        public int CompleteCalls { get; private set; } public int HealthCalls { get; private set; } public int RevokeCalls { get; private set; }
        public DateTime? ConsentExpiresUtc { get; set; } = Utc(2026, 12, 31, 0); public BankProviderSafeException? StartFailure { get; set; }
        public string OwnershipStatus { get; set; } = BankAccountOwnershipStatuses.Verified;
        public Task<IReadOnlyList<BankInstitutionDescriptor>> GetInstitutionsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BankInstitutionDescriptor>>([new("bank-1", "Test Institution", "SE", Descriptor.Capabilities)]);
        public Task<BankProviderConsentStartResult> StartConsentAsync(BankProviderConsentStartRequest request, CancellationToken cancellationToken)
        { if (StartFailure is not null) throw StartFailure; LastStart = request; return Task.FromResult(new BankProviderConsentStartResult(new("https://provider.example.test/authorize"), "provider-session", request.CallbackUri is not null ? Utc(2026, 8, 28, 12) : default)); }
        public Task<BankProviderConsentResult> CompleteConsentAsync(BankProviderCallbackRequest request, CancellationToken cancellationToken)
        { CompleteCalls++; return Task.FromResult(new BankProviderConsentResult("consent-1", "Test Institution", ConsentExpiresUtc, Descriptor.Capabilities, new("access-secret", "refresh-secret", null, ConsentExpiresUtc))); }
        public Task<IReadOnlyList<BankProviderDiscoveredAccount>> DiscoverAccountsAsync(Guid companyId, string providerConsentId, BankProviderCredentialBundle credentials, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<BankProviderDiscoveredAccount>>([new("provider-account-1", "Operating account", "•••• 1111", "SEK", OwnershipStatus, "Ownership result supplied by provider.")]);
        public Task<BankProviderHealthResult> GetHealthAsync(Guid companyId, string providerConsentId, BankProviderCredentialBundle credentials, CancellationToken cancellationToken) { HealthCalls++; return Task.FromResult(new BankProviderHealthResult(BankConnectionHealthStatuses.Healthy, null, null)); }
        public Task RevokeConsentAsync(Guid companyId, string providerConsentId, BankProviderCredentialBundle credentials, CancellationToken cancellationToken) { RevokeCalls++; return Task.CompletedTask; }
    }
}
