using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;
using Xunit;

namespace VirtualCompany.Finance.Tests;

public sealed class BankFeedSynchronizationTests
{
    [Fact]
    public async Task Overlapping_pages_and_repeated_polling_create_one_booked_transaction_per_stable_identity()
    {
        await using var fixture = await FeedFixture.CreateAsync();
        var transaction = Booked("entry-1", 125.50m);
        fixture.Provider.ResolvePage = request => request.TransactionStatus switch
        {
            BankFeedProviderTransactionStatuses.Pending => Page(),
            _ when request.ContinuationToken is null => Page([transaction], "overlap-1"),
            _ => Page([transaction])
        };

        await fixture.Runner.RunDueAsync(default);
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await fixture.Runner.RunDueAsync(default);

        var sources = await fixture.Db.BankFeedSourceTransactions.IgnoreQueryFilters().ToListAsync();
        Assert.Single(sources);
        Assert.Equal(BankFeedSourceTransactionStatuses.Booked, sources[0].Status);
        Assert.Single(await fixture.Db.BankTransactions.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(4, fixture.Provider.TransactionRequests.Count(x =>
            x.TransactionStatus == BankFeedProviderTransactionStatuses.Booked));
    }

    [Fact]
    public async Task Expired_lease_resumes_from_committed_cursor_after_interrupted_page_without_gap_or_duplicate()
    {
        await using var fixture = await FeedFixture.CreateAsync();
        using var interrupted = new CancellationTokenSource();
        var crashOnce = true;
        fixture.Provider.ResolvePage = request =>
        {
            if (request.TransactionStatus == BankFeedProviderTransactionStatuses.Pending) return Page();
            if (request.ContinuationToken is null) return Page([Booked("entry-1", 10m)], "resume-here");
            if (crashOnce)
            {
                crashOnce = false;
                interrupted.Cancel();
                throw new OperationCanceledException(interrupted.Token);
            }
            return Page([Booked("entry-2", 20m)]);
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Runner.RunDueAsync(interrupted.Token));
        fixture.Db.ChangeTracker.Clear();
        var interruptedCheckpoint = await fixture.Db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(BankFeedCheckpointStatuses.Running, interruptedCheckpoint.Status);
        Assert.NotNull(interruptedCheckpoint.ContinuationTokenEnvelope);
        Assert.Single(await fixture.Db.BankTransactions.IgnoreQueryFilters().ToListAsync());

        fixture.Clock.Advance(TimeSpan.FromSeconds(31));
        await fixture.CreateRunner().RunDueAsync(default);

        Assert.Equal(2, await fixture.Db.BankTransactions.IgnoreQueryFilters().CountAsync());
        Assert.Equal(2, await fixture.Db.BankFeedSourceTransactions.IgnoreQueryFilters().CountAsync());
        Assert.Contains(fixture.Provider.TransactionRequests, x => x.ContinuationToken == "resume-here");
        var recovered = await fixture.Db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(BankFeedCheckpointStatuses.Ready, recovered.Status);
        Assert.Empty(await fixture.Db.BankFeedGaps.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Pending_observation_creates_no_final_transaction_and_later_promotes_to_one_booked_row()
    {
        await using var fixture = await FeedFixture.CreateAsync();
        var firstPoll = true;
        fixture.Provider.ResolvePage = request =>
        {
            if (firstPoll)
                return request.TransactionStatus == BankFeedProviderTransactionStatuses.Pending
                    ? Page([Pending("entry-pending", 45m)])
                    : Page();
            return request.TransactionStatus == BankFeedProviderTransactionStatuses.Booked
                ? Page([Booked("entry-pending", 45m)])
                : Page();
        };

        await fixture.Runner.RunDueAsync(default);
        Assert.Empty(await fixture.Db.BankTransactions.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(BankFeedSourceTransactionStatuses.Pending,
            (await fixture.Db.BankFeedSourceTransactions.IgnoreQueryFilters().SingleAsync()).Status);

        firstPoll = false;
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await fixture.Runner.RunDueAsync(default);

        var source = await fixture.Db.BankFeedSourceTransactions.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(BankFeedSourceTransactionStatuses.Booked, source.Status);
        Assert.NotNull(source.BankTransactionId);
        Assert.Single(await fixture.Db.BankTransactions.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Repeated_cursor_requires_attention_and_retains_protected_source_evidence()
    {
        await using var fixture = await FeedFixture.CreateAsync();
        fixture.Provider.ResolvePage = request => request.TransactionStatus == BankFeedProviderTransactionStatuses.Pending
            ? Page()
            : request.ContinuationToken is null
                ? Page([Booked("entry-1", 10m)], "cursor-1")
                : Page([Booked("entry-2", 20m)], "cursor-1");

        await fixture.Runner.RunDueAsync(default);

        var checkpoint = await fixture.Db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(BankFeedCheckpointStatuses.AttentionRequired, checkpoint.Status);
        Assert.Equal(BankFeedReasonCodes.CursorRegression, checkpoint.ReasonCode);
        var gap = await fixture.Db.BankFeedGaps.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(BankFeedGapKinds.CursorRegression, gap.Kind);
        Assert.Equal(BankFeedGapStatuses.Open, gap.Status);
        var evidence = await fixture.Db.BankFeedRawSourceObjects.IgnoreQueryFilters().ToListAsync();
        Assert.True(evidence.Count >= 3);
        Assert.All(evidence, item =>
        {
            Assert.Equal(64, item.Checksum.Length);
            Assert.DoesNotContain("entry-", item.EncryptedPayload!, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Rate_limit_uses_provider_retry_after_and_completes_on_the_next_due_attempt()
    {
        await using var fixture = await FeedFixture.CreateAsync();
        var rateLimited = true;
        fixture.Provider.ResolvePage = request =>
        {
            if (rateLimited)
            {
                rateLimited = false;
                throw new BankProviderSafeException(BankFeedReasonCodes.RateLimited,
                    "The provider asked the worker to retry later.", true, retryAfter: TimeSpan.FromMinutes(5));
            }
            return Page();
        };

        await fixture.Runner.RunDueAsync(default);
        var retry = await fixture.Db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(BankFeedCheckpointStatuses.Failed, retry.Status);
        Assert.Equal(BankFeedReasonCodes.RateLimited, retry.ReasonCode);
        Assert.True(retry.NextAttemptUtc >= fixture.Clock.GetUtcNow().UtcDateTime.AddMinutes(5));

        fixture.Clock.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
        await fixture.Runner.RunDueAsync(default);
        Assert.Equal(BankFeedCheckpointStatuses.Ready,
            (await fixture.Db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync()).Status);
    }

    [Fact]
    public async Task Changed_booked_payload_under_the_same_identity_is_not_overwritten()
    {
        await using var fixture = await FeedFixture.CreateAsync();
        var amount = 10m;
        fixture.Provider.ResolvePage = request => request.TransactionStatus == BankFeedProviderTransactionStatuses.Booked
            ? Page([Booked("immutable-entry", amount)])
            : Page();
        await fixture.Runner.RunDueAsync(default);

        amount = 11m;
        fixture.Clock.Advance(TimeSpan.FromMinutes(16));
        await fixture.Runner.RunDueAsync(default);

        Assert.Equal(10m, (await fixture.Db.BankTransactions.IgnoreQueryFilters().SingleAsync()).Amount);
        var checkpoint = await fixture.Db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(BankFeedCheckpointStatuses.AttentionRequired, checkpoint.Status);
        Assert.Equal(BankFeedReasonCodes.PayloadConflict, checkpoint.ReasonCode);
        Assert.Equal(BankFeedGapKinds.PayloadConflict,
            (await fixture.Db.BankFeedGaps.IgnoreQueryFilters().SingleAsync()).Kind);
    }

    [Fact]
    public async Task Missing_balance_marker_opens_a_gap_and_authorized_bounded_recovery_resolves_it()
    {
        await using var fixture = await FeedFixture.CreateAsync(addMembership: true);
        fixture.Provider.LastCommittedIdentity = "missing-entry";
        fixture.Provider.ResolvePage = _ => Page();

        await fixture.Runner.RunDueAsync(default);
        var checkpoint = await fixture.Db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync();
        var gap = await fixture.Db.BankFeedGaps.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(BankFeedCheckpointStatuses.AttentionRequired, checkpoint.Status);
        Assert.Equal(BankFeedGapKinds.MissingRange, gap.Kind);

        fixture.Provider.ResolvePage = request =>
            request.TransactionStatus == BankFeedProviderTransactionStatuses.Booked
                ? Page([Booked("missing-entry", 88m)])
                : Page();
        var service = fixture.CreateFeedService();
        await service.RequestBackfillAsync(new(fixture.CompanyId, checkpoint.Id, gap.Id, gap.DateFrom,
            gap.DateTo, fixture.UserId, checkpoint.Version, "Recover provider balance marker", "recovery-1"), default);
        await fixture.Runner.RunDueAsync(default);

        var resolved = await fixture.Db.BankFeedGaps.IgnoreQueryFilters().SingleAsync(x => x.Id == gap.Id);
        Assert.Equal(BankFeedGapStatuses.Resolved, resolved.Status);
        Assert.Equal(fixture.UserId, resolved.ResolvedByUserId);
        Assert.Single(await fixture.Db.BankTransactions.IgnoreQueryFilters().ToListAsync());
        Assert.Contains(await fixture.Db.BankConnectionAuditEvents.IgnoreQueryFilters().ToListAsync(),
            x => x.EventType == "bank_feed_backfill_requested" && x.CorrelationId == "recovery-1");
    }

    [Fact]
    public async Task Expired_raw_payload_is_purged_while_checksum_and_normalized_trace_remain()
    {
        await using var fixture = await FeedFixture.CreateAsync();
        fixture.Provider.ResolvePage = request => request.TransactionStatus == BankFeedProviderTransactionStatuses.Booked
            ? Page([Booked("retained-trace", 12m)])
            : Page();
        await fixture.Runner.RunDueAsync(default);
        var sourceId = (await fixture.Db.BankFeedRawSourceObjects.IgnoreQueryFilters()
            .OrderBy(x => x.CreatedUtc).FirstAsync()).Id;

        fixture.Clock.Advance(TimeSpan.FromDays(91));
        await fixture.Runner.RunDueAsync(default);

        var expired = await fixture.Db.BankFeedRawSourceObjects.IgnoreQueryFilters().SingleAsync(x => x.Id == sourceId);
        Assert.Null(expired.EncryptedPayload);
        Assert.NotNull(expired.PayloadPurgedUtc);
        Assert.Equal(64, expired.Checksum.Length);
        Assert.Single(await fixture.Db.BankFeedSourceTransactions.IgnoreQueryFilters()
            .Where(x => x.StableIdentity == "retained-trace").ToListAsync());
    }

    [Fact]
    public async Task Manual_operations_are_member_authorized_and_company_scoped()
    {
        await using var fixture = await FeedFixture.CreateAsync(addMembership: true);
        await fixture.Runner.RunDueAsync(default);
        var checkpoint = await fixture.Db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync();
        var service = fixture.CreateFeedService();

        var otherCompany = Guid.NewGuid();
        fixture.Db.Companies.Add(new Company(otherCompany, "Other Company"));
        fixture.Db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), otherCompany, fixture.UserId,
            CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        await fixture.Db.SaveChangesAsync();
        var crossCompany = await Assert.ThrowsAsync<KeyNotFoundException>(() => service.RequestSynchronizationAsync(
            new(otherCompany, checkpoint.Id, fixture.UserId, "cross-company"), default));
        Assert.Contains("not found", crossCompany.Message, StringComparison.OrdinalIgnoreCase);

        var unauthorized = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.RequestSynchronizationAsync(
            new(fixture.CompanyId, checkpoint.Id, Guid.NewGuid(), "unauthorized"), default));
        Assert.Contains("active company member", unauthorized.Message, StringComparison.OrdinalIgnoreCase);

        var queued = await service.RequestSynchronizationAsync(
            new(fixture.CompanyId, checkpoint.Id, fixture.UserId, "authorized"), default);
        Assert.Equal(1, queued.QueuedAccountCount);
        var health = await service.GetHealthAsync(otherCompany, default);
        Assert.Empty(health.Accounts);
    }

    private static BankFeedProviderTransaction Booked(string identity, decimal amount) => new(identity,
        BankFeedProviderTransactionStatuses.Booked, Utc(2026, 8, 27), Utc(2026, 8, 27),
        Utc(2026, 8, 27), amount, "SEK", $"Reference {identity}", "Counterparty", $"tx-{identity}");

    private static BankFeedProviderTransaction Pending(string identity, decimal amount) => new(identity,
        BankFeedProviderTransactionStatuses.Pending, null, null, Utc(2026, 8, 27), amount, "SEK",
        $"Reference {identity}", "Counterparty", $"pending-{identity}");

    private static BankFeedProviderPage Page(IReadOnlyList<BankFeedProviderTransaction>? transactions = null,
        string? next = null) => new(transactions ?? [], next,
        System.Text.Encoding.UTF8.GetBytes($"{{\"page\":\"{next ?? "last"}\",\"count\":{transactions?.Count ?? 0}}}"),
        "application/json", "request-1");

    private static DateTime Utc(int year, int month, int day, int hour = 0) =>
        new(year, month, day, hour, 0, 0, DateTimeKind.Utc);

    private sealed class FeedFixture : IAsyncDisposable
    {
        private readonly IDataProtectionProvider _protection = new EphemeralDataProtectionProvider();
        private readonly DataProtectionFieldEncryptionService _encryption;
        private readonly ProtectedBankCredentialStore _credentials;
        private readonly BankConnectionService _connections;
        private readonly IOptions<BankFeedSynchronizationOptions> _options;

        private FeedFixture(VirtualCompanyDbContext db, ScriptedProvider provider, MutableTimeProvider clock)
        {
            Db = db;
            Provider = provider;
            Clock = clock;
            _encryption = new DataProtectionFieldEncryptionService(_protection);
            _credentials = new ProtectedBankCredentialStore(Db, _encryption);
            _connections = new BankConnectionService(Db, new BankConnectionProviderRegistry([Provider]),
                new DataProtectionBankConsentStateProtector(_protection), _credentials,
                new BankConnectionTelemetry(NullLogger<BankConnectionTelemetry>.Instance), Clock);
            _options = Options.Create(new BankFeedSynchronizationOptions
            {
                LeaseSeconds = 30,
                SynchronizationIntervalMinutes = 15,
                InitialLookbackDays = 30,
                OverlapDays = 3,
                MaximumAttempts = 3,
                BaseRetryDelaySeconds = 1,
                MaximumRetryDelaySeconds = 600,
                MaximumPagesPerRun = 10
            });
            Runner = CreateRunner();
        }

        public VirtualCompanyDbContext Db { get; }
        public ScriptedProvider Provider { get; }
        public MutableTimeProvider Clock { get; }
        public BankFeedSynchronizationRunner Runner { get; }
        public Guid CompanyId { get; private set; }
        public Guid UserId { get; private set; }

        public static async Task<FeedFixture> CreateAsync(bool addMembership = false)
        {
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseSqlite("Data Source=:memory:;Foreign Keys=False").Options);
            await db.Database.OpenConnectionAsync();
            await db.Database.EnsureCreatedAsync();
            var fixture = new FeedFixture(db, new ScriptedProvider(),
                new MutableTimeProvider(Utc(2026, 8, 28, 10)));
            await fixture.InitializeAsync(addMembership);
            return fixture;
        }

        public BankFeedSynchronizationRunner CreateRunner() => new(Db, _connections,
            new BankFeedProviderRegistry([Provider]), _credentials, _encryption, _options,
            new BankFeedTelemetry(), Clock, NullLogger<BankFeedSynchronizationRunner>.Instance);

        public BankFeedService CreateFeedService() => new(Db, _options, Clock);

        private async Task InitializeAsync(bool addMembership)
        {
            CompanyId = Guid.NewGuid();
            UserId = Guid.NewGuid();
            await _connections.StartAsync(new(CompanyId, UserId, ScriptedProvider.Key, "SE|Test Bank",
                new Uri("https://api.example.test/finance/bank-connections/test/callback"), null,
                [BankProviderCapabilities.Accounts, BankProviderCapabilities.AccountOwnership,
                    BankProviderCapabilities.Balances, BankProviderCapabilities.Transactions]), default);
            var completed = await _connections.CompleteCallbackAsync(new(CompanyId, UserId, ScriptedProvider.Key,
                Provider.LastStart!.ProtectedState, "authorization-code", null), default);
            var financeAccount = new FinanceAccount(Guid.NewGuid(), CompanyId, "1930", "Operating bank",
                "asset", "SEK", 0, Clock.GetUtcNow().UtcDateTime);
            var bankAccount = new CompanyBankAccount(Guid.NewGuid(), CompanyId, financeAccount.Id,
                "Operating account", "Test Bank", "•••• 1111", "SEK");
            Db.FinanceAccounts.Add(financeAccount);
            Db.CompanyBankAccounts.Add(bankAccount);
            if (addMembership)
            {
                Db.Users.Add(new User(UserId, "finance@example.test", "Finance Operator", "test", $"subject-{UserId:N}"));
                Db.Companies.Add(new Company(CompanyId, "Feed Test Company"));
                Db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), CompanyId, UserId,
                    CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            }
            await Db.SaveChangesAsync();
            var status = await _connections.GetStatusAsync(CompanyId, default);
            var connection = Assert.Single(status.Connections);
            var discovered = Assert.Single(connection.Accounts);
            await _connections.MapAccountAsync(new(CompanyId, completed.ConnectionId, discovered.Id,
                bankAccount.Id, UserId, connection.Version, "Explicit verified mapping"), default);
        }

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class ScriptedProvider : IBankConnectionProvider, IBankFeedProvider
    {
        public const string Key = "test-feed";
        public BankProviderDescriptor Descriptor { get; } = new(Key, "Test feed provider",
            [BankProviderCapabilities.Accounts, BankProviderCapabilities.AccountOwnership,
                BankProviderCapabilities.Balances, BankProviderCapabilities.Transactions], true);
        string IBankFeedProvider.ProviderKey => Key;
        public BankProviderConsentStartRequest? LastStart { get; private set; }
        public Func<BankFeedProviderPageRequest, BankFeedProviderPage> ResolvePage { get; set; } = _ => Page();
        public List<BankFeedProviderPageRequest> TransactionRequests { get; } = [];
        public string? LastCommittedIdentity { get; set; }

        public Task<IReadOnlyList<BankInstitutionDescriptor>> GetInstitutionsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankInstitutionDescriptor>>([new("SE|Test Bank", "Test Bank", "SE", Descriptor.Capabilities)]);
        public Task<BankProviderConsentStartResult> StartConsentAsync(BankProviderConsentStartRequest request,
            CancellationToken cancellationToken)
        {
            LastStart = request;
            return Task.FromResult(new BankProviderConsentStartResult(new Uri("https://provider.example.test/auth"),
                "provider-session", Utc(2026, 8, 28, 11)));
        }
        public Task<BankProviderConsentResult> CompleteConsentAsync(BankProviderCallbackRequest request,
            CancellationToken cancellationToken) => Task.FromResult(new BankProviderConsentResult("consent-1",
            "Test Bank", Utc(2026, 12, 31), Descriptor.Capabilities,
            new BankProviderCredentialBundle("access", "refresh", "session", Utc(2026, 12, 31))));
        public Task<IReadOnlyList<BankProviderDiscoveredAccount>> DiscoverAccountsAsync(Guid companyId,
            string providerConsentId, BankProviderCredentialBundle credentials, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BankProviderDiscoveredAccount>>([new("stable-account-1", "Operating account",
                "•••• 1111", "SEK", BankAccountOwnershipStatuses.Verified, "Verified holder", "access-account-1")]);
        public Task<BankProviderHealthResult> GetHealthAsync(Guid companyId, string providerConsentId,
            BankProviderCredentialBundle credentials, CancellationToken cancellationToken) =>
            Task.FromResult(new BankProviderHealthResult(BankConnectionHealthStatuses.Healthy, null, null));
        public Task RevokeConsentAsync(Guid companyId, string providerConsentId,
            BankProviderCredentialBundle credentials, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<BankFeedProviderBalances> GetBalancesAsync(Guid companyId, string providerConsentId,
            BankProviderCredentialBundle credentials, string providerAccountAccessReference,
            CancellationToken cancellationToken)
        {
            var evidence = System.Text.Encoding.UTF8.GetBytes(
                $"{{\"balances\":[{{\"last_committed_transaction\":{System.Text.Json.JsonSerializer.Serialize(LastCommittedIdentity)}}}]}}");
            return Task.FromResult(new BankFeedProviderBalances(
                [new BankFeedProviderBalance("CLBD", 1000m, "SEK", null, new DateOnly(2026, 8, 28), LastCommittedIdentity)],
                evidence, "application/json", "balance-request"));
        }
        public Task<BankFeedProviderPage> GetTransactionsAsync(Guid companyId, string providerConsentId,
            BankProviderCredentialBundle credentials, BankFeedProviderPageRequest request,
            CancellationToken cancellationToken)
        {
            TransactionRequests.Add(request);
            return Task.FromResult(ResolvePage(request));
        }
    }

    private sealed class MutableTimeProvider(DateTime now) : TimeProvider
    {
        private DateTime _now = now;
        public override DateTimeOffset GetUtcNow() => new(_now);
        public void Advance(TimeSpan value) => _now = _now.Add(value);
    }
}
