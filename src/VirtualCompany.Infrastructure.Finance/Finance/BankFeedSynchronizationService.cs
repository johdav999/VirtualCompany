using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class BankFeedSynchronizationOptions
{
    public const string SectionName = "BankFeedSynchronization";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 60;
    public int ClaimBatchSize { get; set; } = 10;
    public int LeaseSeconds { get; set; } = 180;
    public int SynchronizationIntervalMinutes { get; set; } = 15;
    public int InitialLookbackDays { get; set; } = 90;
    public int OverlapDays { get; set; } = 3;
    public int MaximumBackfillDays { get; set; } = 366;
    public int MaximumAttempts { get; set; } = 5;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int MaximumRetryDelaySeconds { get; set; } = 1800;
    public int MaximumPagesPerRun { get; set; } = 100;
    public int RawEvidenceRetentionDays { get; set; } = 90;
}

public sealed class BankFeedService : IBankFeedService
{
    private readonly VirtualCompanyDbContext _db;
    private readonly BankFeedSynchronizationOptions _options;
    private readonly TimeProvider _clock;

    public BankFeedService(VirtualCompanyDbContext db, IOptions<BankFeedSynchronizationOptions> options,
        TimeProvider clock) { _db = db; _options = options.Value; _clock = clock; }

    public async Task<BankFeedHealthResult> GetHealthAsync(Guid companyId, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId));
        var checkpoints = await (from checkpoint in _db.BankFeedCheckpoints.IgnoreQueryFilters().AsNoTracking()
            join connection in _db.BankConnections.IgnoreQueryFilters().AsNoTracking() on new { checkpoint.CompanyId, Id = checkpoint.ConnectionId } equals new { connection.CompanyId, connection.Id }
            join discovered in _db.BankDiscoveredAccounts.IgnoreQueryFilters().AsNoTracking() on new { checkpoint.CompanyId, Id = checkpoint.DiscoveredAccountId } equals new { discovered.CompanyId, discovered.Id }
            join account in _db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking() on new { checkpoint.CompanyId, Id = checkpoint.CompanyBankAccountId } equals new { account.CompanyId, account.Id }
            where checkpoint.CompanyId == companyId
            orderby connection.InstitutionName, account.DisplayName
            select new { Checkpoint = checkpoint, Connection = connection, Discovered = discovered, Account = account })
            .ToListAsync(cancellationToken);
        var ids = checkpoints.Select(x => x.Checkpoint.Id).ToArray();
        var gaps = await _db.BankFeedGaps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && ids.Contains(x.CheckpointId))
            .OrderByDescending(x => x.DetectedUtc).ToListAsync(cancellationToken);
        var now = Now();
        var items = checkpoints.Select(x =>
        {
            var accountGaps = gaps.Where(g => g.CheckpointId == x.Checkpoint.Id).Select(MapGap).ToArray();
            var lag = x.Checkpoint.LastSuccessfulSyncUtc.HasValue
                ? Math.Max(0, (int)Math.Ceiling((now - x.Checkpoint.LastSuccessfulSyncUtc.Value).TotalMinutes))
                : Math.Max(0, _options.InitialLookbackDays * 24 * 60);
            return new BankFeedAccountHealthItem(x.Checkpoint.Id, x.Checkpoint.ConnectionId,
                x.Checkpoint.DiscoveredAccountId, x.Checkpoint.CompanyBankAccountId, x.Connection.InstitutionName,
                x.Account.DisplayName, x.Account.MaskedAccountNumber, x.Account.Currency, x.Checkpoint.Status,
                x.Checkpoint.ReasonCode, x.Checkpoint.FailureSummary, x.Checkpoint.CoverageFrom,
                x.Checkpoint.CoverageThrough, x.Checkpoint.LastSuccessfulSyncUtc, x.Checkpoint.LastAttemptUtc,
                x.Checkpoint.NextAttemptUtc, lag, x.Checkpoint.Version, accountGaps);
        }).ToArray();
        var healthy = items.Count(x => x.Status == BankFeedCheckpointStatuses.Ready && x.Gaps.All(g => g.Status == BankFeedGapStatuses.Resolved));
        var attention = items.Count(x => x.Status == BankFeedCheckpointStatuses.AttentionRequired || x.Gaps.Any(g => g.Status == BankFeedGapStatuses.Open));
        var coverage = items.Where(x => x.CoverageThrough.HasValue).Select(x => x.CoverageThrough!.Value)
            .OrderBy(x => x).FirstOrDefault();
        return new BankFeedHealthResult(healthy, attention,
            coverage == default ? null : DateTime.SpecifyKind(coverage.ToDateTime(TimeOnly.MaxValue), DateTimeKind.Utc),
            items.Length == 0 ? 0 : items.Max(x => x.LagMinutes), items);
    }

    public async Task<BankFeedRequestResult> RequestSynchronizationAsync(
        RequestBankFeedSynchronizationCommand command, CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        await BankFeedSynchronizationRunner.EnsureCheckpointsAsync(_db, Now(), cancellationToken);
        var query = _db.BankFeedCheckpoints.IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId);
        if (command.CheckpointId.HasValue) query = query.Where(x => x.Id == command.CheckpointId.Value);
        var rows = await query.ToListAsync(cancellationToken);
        if (command.CheckpointId.HasValue && rows.Count == 0) throw new KeyNotFoundException("The bank feed account was not found.");
        var today = DateOnly.FromDateTime(Now());
        foreach (var row in rows)
        {
            var from = row.CoverageThrough.HasValue
                ? row.CoverageThrough.Value.AddDays(-Math.Clamp(_options.OverlapDays, 1, 30))
                : today.AddDays(-Math.Clamp(_options.InitialLookbackDays, 1, 366));
            try { row.Queue(from, today, command.ActorUserId, null, command.CorrelationId, Now()); }
            catch (InvalidOperationException) when (row.Status == BankFeedCheckpointStatuses.Running) { }
        }
        await _db.SaveChangesAsync(cancellationToken);
        return new BankFeedRequestResult(rows.Count, "queued", rows.Count == 0
            ? "No mapped bank accounts are ready for synchronization."
            : $"Synchronization was queued for {rows.Count} bank account(s).");
    }

    public async Task<BankFeedRequestResult> RequestBackfillAsync(RequestBankFeedBackfillCommand command,
        CancellationToken cancellationToken)
    {
        await EnsureActorAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("A recovery reason is required.", nameof(command.Reason));
        if (command.DateTo < command.DateFrom || command.DateTo.DayNumber - command.DateFrom.DayNumber + 1 > _options.MaximumBackfillDays)
            throw new ArgumentOutOfRangeException(nameof(command.DateTo), $"Bank feed recovery is limited to {_options.MaximumBackfillDays} days.");
        var checkpoint = await _db.BankFeedCheckpoints.IgnoreQueryFilters().SingleOrDefaultAsync(
            x => x.CompanyId == command.CompanyId && x.Id == command.CheckpointId, cancellationToken)
            ?? throw new KeyNotFoundException("The bank feed account was not found.");
        try { checkpoint.EnsureVersion(command.ExpectedCheckpointVersion); }
        catch (InvalidOperationException) { throw new BankConnectionOperationException(BankConnectionReasonCodes.ConcurrencyConflict, "The bank feed changed. Reload it before requesting recovery.", true); }
        var gap = await _db.BankFeedGaps.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
            x.CheckpointId == checkpoint.Id && x.Id == command.GapId && x.Status == BankFeedGapStatuses.Open, cancellationToken)
            ?? throw new KeyNotFoundException("The open bank feed gap was not found.");
        if (command.DateFrom < gap.DateFrom || command.DateTo > gap.DateTo)
            throw new ArgumentOutOfRangeException(nameof(command.DateFrom), "Recovery must stay within the retained missing range.");
        checkpoint.Queue(command.DateFrom, command.DateTo, command.ActorUserId, gap.Id, command.CorrelationId, Now());
        _db.BankConnectionAuditEvents.Add(new BankConnectionAuditEvent(Guid.NewGuid(), command.CompanyId,
            checkpoint.ConnectionId, command.ActorUserId, "bank_feed_backfill_requested", "succeeded",
            $"Bank feed recovery was queued for {command.DateFrom:yyyy-MM-dd} through {command.DateTo:yyyy-MM-dd}.",
            gap.ReasonCode, command.CorrelationId, gap.Status, "queued", Now()));
        await _db.SaveChangesAsync(cancellationToken);
        return new BankFeedRequestResult(1, "queued", "The bounded missing-range recovery was queued.");
    }

    private async Task EnsureActorAsync(Guid companyId, Guid actorUserId, CancellationToken cancellationToken)
    {
        Require(companyId, nameof(companyId)); Require(actorUserId, nameof(actorUserId));
        if (!await _db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            x.UserId == actorUserId && x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw new UnauthorizedAccessException("An active company member is required for bank feed operations.");
    }
    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private static BankFeedGapItem MapGap(BankFeedGap x) => new(x.Id, x.Kind, x.DateFrom, x.DateTo, x.Status,
        x.ReasonCode, x.Summary, x.DetectedUtc, x.ResolvedUtc);
    private static void Require(Guid value, string name) { if (value == Guid.Empty) throw new ArgumentException($"{name} is required.", name); }
}

public sealed class BankFeedSynchronizationRunner : IBankFeedSynchronizationRunner
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IBankConnectionService _connections;
    private readonly IBankFeedProviderRegistry _providers;
    private readonly IProtectedBankCredentialStore _credentials;
    private readonly IFieldEncryptionService _encryption;
    private readonly IOptions<BankFeedSynchronizationOptions> _options;
    private readonly BankFeedTelemetry _telemetry;
    private readonly TimeProvider _clock;
    private readonly ILogger<BankFeedSynchronizationRunner> _logger;

    public BankFeedSynchronizationRunner(VirtualCompanyDbContext db, IBankConnectionService connections,
        IBankFeedProviderRegistry providers, IProtectedBankCredentialStore credentials,
        IFieldEncryptionService encryption, IOptions<BankFeedSynchronizationOptions> options,
        BankFeedTelemetry telemetry, TimeProvider clock, ILogger<BankFeedSynchronizationRunner> logger)
    {
        _db = db; _connections = connections; _providers = providers; _credentials = credentials;
        _encryption = encryption; _options = options; _telemetry = telemetry; _clock = clock; _logger = logger;
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled) return 0;
        var now = Now();
        await PurgeExpiredEvidenceAsync(now, cancellationToken);
        await EnsureCheckpointsAsync(_db, now, cancellationToken);
        await QueueScheduledAsync(now, cancellationToken);
        var candidates = await _db.BankFeedCheckpoints.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == BankFeedCheckpointStatuses.Queued || x.Status == BankFeedCheckpointStatuses.Failed ||
                         x.Status == BankFeedCheckpointStatuses.Running && x.LeaseExpiresUtc <= now) &&
                        (x.NextAttemptUtc == null || x.NextAttemptUtc <= now))
            .OrderBy(x => x.NextAttemptUtc).ThenBy(x => x.CompanyId).ThenBy(x => x.Id)
            .Select(x => new { x.CompanyId, x.Id }).Take(Math.Clamp(_options.Value.ClaimBatchSize, 1, 100))
            .ToArrayAsync(cancellationToken);
        var handled = 0;
        foreach (var candidate in candidates)
        {
            var claim = await ClaimAsync(candidate.CompanyId, candidate.Id, cancellationToken);
            if (claim is null) continue;
            handled++;
            try { await ProcessAsync(claim, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (BankProviderSafeException exception) { await HandleFailureAsync(claim, exception.ReasonCode, exception.SafeMessage, exception.IsTransient, exception.RetryAfter, cancellationToken); }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Bank feed synchronization failed for checkpoint {CheckpointId}.", claim.CheckpointId);
                await HandleFailureAsync(claim, BankFeedReasonCodes.ProviderUnavailable,
                    "Bank feed synchronization did not complete.", true, null, cancellationToken);
            }
            finally { _db.ChangeTracker.Clear(); }
        }
        return handled;
    }

    private async Task PurgeExpiredEvidenceAsync(DateTime now, CancellationToken cancellationToken)
    {
        var expired = await _db.BankFeedRawSourceObjects.IgnoreQueryFilters()
            .Where(x => x.EncryptedPayload != null && x.RetentionExpiresUtc <= now)
            .OrderBy(x => x.RetentionExpiresUtc)
            .Take(250)
            .ToListAsync(cancellationToken);
        foreach (var source in expired) source.PurgePayload(now);
        if (expired.Count > 0) await _db.SaveChangesAsync(cancellationToken);
    }

    internal static async Task EnsureCheckpointsAsync(VirtualCompanyDbContext db, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var mapped = await (from mapping in db.BankAccountMappings.IgnoreQueryFilters()
            join discovered in db.BankDiscoveredAccounts.IgnoreQueryFilters() on new { mapping.CompanyId, Id = mapping.DiscoveredAccountId } equals new { discovered.CompanyId, discovered.Id }
            join connection in db.BankConnections.IgnoreQueryFilters() on new { discovered.CompanyId, Id = discovered.ConnectionId } equals new { connection.CompanyId, connection.Id }
            where mapping.IsCurrent && discovered.IsAvailable && discovered.ProviderAccessReference != null &&
                  (connection.Status == BankConnectionStatuses.Active || connection.Status == BankConnectionStatuses.AttentionRequired)
            select new { Mapping = mapping, Discovered = discovered, Connection = connection }).ToListAsync(cancellationToken);
        var keys = mapped.Select(x => x.Discovered.Id).ToArray();
        var existing = await db.BankFeedCheckpoints.IgnoreQueryFilters().Where(x => keys.Contains(x.DiscoveredAccountId))
            .ToDictionaryAsync(x => new { x.CompanyId, x.DiscoveredAccountId }, cancellationToken);
        foreach (var item in mapped)
        {
            var key = new { item.Mapping.CompanyId, DiscoveredAccountId = item.Discovered.Id };
            if (existing.TryGetValue(key, out var checkpoint))
            {
                if (checkpoint.AccountMappingId != item.Mapping.Id || checkpoint.AccountMappingVersion != item.Mapping.Version ||
                    checkpoint.CompanyBankAccountId != item.Mapping.CompanyBankAccountId ||
                    !string.Equals(checkpoint.ProviderAccountAccessReference, item.Discovered.ProviderAccessReference, StringComparison.Ordinal))
                    checkpoint.ApplyMapping(item.Mapping.Id, item.Mapping.Version, item.Mapping.CompanyBankAccountId,
                        item.Discovered.ProviderAccessReference!, nowUtc);
            }
            else db.BankFeedCheckpoints.Add(new BankFeedCheckpoint(Guid.NewGuid(), item.Mapping.CompanyId,
                item.Connection.Id, item.Discovered.Id, item.Mapping.Id, item.Mapping.Version,
                item.Mapping.CompanyBankAccountId, item.Connection.ProviderKey, item.Discovered.ProviderAccountId,
                item.Discovered.ProviderAccessReference!, nowUtc));
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task QueueScheduledAsync(DateTime now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now);
        var due = await _db.BankFeedCheckpoints.IgnoreQueryFilters().Where(x => x.Status == BankFeedCheckpointStatuses.Ready &&
            (x.NextAttemptUtc == null || x.NextAttemptUtc <= now)).ToListAsync(cancellationToken);
        foreach (var checkpoint in due)
        {
            var from = checkpoint.CoverageThrough.HasValue
                ? checkpoint.CoverageThrough.Value.AddDays(-Math.Clamp(_options.Value.OverlapDays, 1, 30))
                : today.AddDays(-Math.Clamp(_options.Value.InitialLookbackDays, 1, 366));
            checkpoint.Queue(from, today, null, null, null, now);
        }
        if (due.Count > 0) await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<Claim?> ClaimAsync(Guid companyId, Guid checkpointId, CancellationToken cancellationToken)
    {
        var now = Now();
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        try
        {
            var checkpoint = await _db.BankFeedCheckpoints.IgnoreQueryFilters().SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Id == checkpointId, cancellationToken);
            if (checkpoint is null) return null;
            var owner = $"bank-feed:{Environment.MachineName}:{Guid.NewGuid():N}";
            if (!checkpoint.TryClaim(owner, now, TimeSpan.FromSeconds(Math.Clamp(_options.Value.LeaseSeconds, 30, 900)))) return null;
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            _telemetry.Synchronization("claimed", checkpoint.ProviderKey, checkpoint.Phase, null);
            return new Claim(companyId, checkpoint.Id, checkpoint.ConnectionId, owner, checkpoint.SynchronizationRunId!.Value);
        }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); return null; }
    }

    private async Task ProcessAsync(Claim claim, CancellationToken cancellationToken)
    {
        var access = await _connections.GetSynchronizationAccessAsync(claim.CompanyId, claim.ConnectionId, cancellationToken);
        if (!access.Allowed)
        {
            await MarkAttentionAsync(claim, access.ReasonCode ?? BankFeedReasonCodes.RecoveryRequired,
                access.Explanation, null, cancellationToken);
            return;
        }
        var connection = await _db.BankConnections.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.ConnectionId, cancellationToken);
        var consent = await _db.BankConsentVersions.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == claim.CompanyId &&
            x.ConnectionId == claim.ConnectionId && x.Status == BankConsentStatuses.Active).OrderByDescending(x => x.Version).FirstAsync(cancellationToken);
        var credentials = await _credentials.GetAsync(claim.CompanyId, claim.ConnectionId, cancellationToken)
            ?? throw new BankProviderSafeException(BankConnectionReasonCodes.MissingConsent, "Renew bank consent before synchronizing.", false);
        var provider = _providers.GetRequired(connection.ProviderKey);

        var initial = await _db.BankFeedCheckpoints.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.CheckpointId, cancellationToken);
        if (initial.Phase == BankFeedSynchronizationPhases.Booked && initial.PageNumber == 0 && initial.ContinuationTokenEnvelope is null)
        {
            var balances = await provider.GetBalancesAsync(claim.CompanyId, consent.ProviderConsentId, credentials,
                initial.ProviderAccountAccessReference, cancellationToken);
            await PersistBalancesAsync(claim, balances, cancellationToken);
        }

        for (var page = 0; page < Math.Clamp(_options.Value.MaximumPagesPerRun, 1, 1000); page++)
        {
            var checkpoint = await _db.BankFeedCheckpoints.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.CheckpointId, cancellationToken);
            if (!checkpoint.IsClaimedBy(claim.Owner, Now()) || checkpoint.SynchronizationRunId != claim.RunId) return;
            var continuation = DecryptContinuation(checkpoint);
            var result = await provider.GetTransactionsAsync(claim.CompanyId, consent.ProviderConsentId, credentials,
                new BankFeedProviderPageRequest(checkpoint.ProviderAccountAccessReference, checkpoint.WindowFrom!.Value,
                    checkpoint.WindowTo!.Value, checkpoint.Phase, continuation), cancellationToken);
            var completed = await PersistPageAsync(claim, result, cancellationToken);
            if (completed) return;
        }
        await MarkAttentionAsync(claim, BankFeedReasonCodes.CursorRegression,
            "The provider returned more pages than the safe synchronization bound. Recover the retained range after reviewing provider pagination.",
            BankFeedGapKinds.CursorRegression, cancellationToken);
    }

    private async Task PersistBalancesAsync(Claim claim, BankFeedProviderBalances result,
        CancellationToken cancellationToken)
    {
        var now = Now();
        var checkpoint = await _db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.CheckpointId, cancellationToken);
        if (!checkpoint.IsClaimedBy(claim.Owner, now) || checkpoint.SynchronizationRunId != claim.RunId) return;
        var raw = CreateRaw(checkpoint, "balances", $"balances:{checkpoint.SynchronizationRunId:N}",
            result.SourceEvidence, result.ContentType, now);
        if (!await _db.BankFeedRawSourceObjects.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == claim.CompanyId && x.CheckpointId == checkpoint.Id && x.SourceIdentity == raw.SourceIdentity, cancellationToken))
        {
            _db.BankFeedRawSourceObjects.Add(raw);
            foreach (var balance in result.Balances) _db.BankFeedBalanceSnapshots.Add(new BankFeedBalanceSnapshot(Guid.NewGuid(), claim.CompanyId,
                checkpoint.Id, raw.Id, balance.BalanceType, balance.Amount, balance.Currency, balance.ObservedUtc,
                balance.ReferenceDate, balance.LastCommittedTransactionIdentity, now));
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> PersistPageAsync(Claim claim, BankFeedProviderPage page,
        CancellationToken cancellationToken)
    {
        var now = Now();
        await using var transaction = _db.Database.IsRelational()
            ? await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken) : null;
        var checkpoint = await _db.BankFeedCheckpoints.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.CheckpointId, cancellationToken);
        if (!checkpoint.IsClaimedBy(claim.Owner, now) || checkpoint.SynchronizationRunId != claim.RunId) return true;
        var phase = checkpoint.Phase;
        var rawIdentity = $"{claim.RunId:N}:{phase}:{checkpoint.PageNumber}:{checkpoint.ContinuationTokenHash ?? "first"}";
        var raw = CreateRaw(checkpoint, "transactions", rawIdentity, page.SourceEvidence, page.ContentType, now);
        _db.BankFeedRawSourceObjects.Add(raw);

        var stableIds = page.Transactions.Select(x => x.StableIdentity).Distinct(StringComparer.Ordinal).ToArray();
        if (stableIds.Length != page.Transactions.Count)
            return await ConflictAsync(checkpoint, claim, BankFeedGapKinds.PayloadConflict, BankFeedReasonCodes.PayloadConflict,
                "The provider page contained a duplicate stable transaction identity.", raw, transaction, cancellationToken);
        var existing = await _db.BankFeedSourceTransactions.IgnoreQueryFilters().Where(x => x.CompanyId == claim.CompanyId &&
            x.CheckpointId == checkpoint.Id && stableIds.Contains(x.StableIdentity)).ToDictionaryAsync(x => x.StableIdentity, StringComparer.Ordinal, cancellationToken);
        var booked = 0; var pending = 0;
        foreach (var source in page.Transactions)
        {
            var hash = ContentHash(source);
            if (existing.TryGetValue(source.StableIdentity, out var row))
            {
                if (row.Status == BankFeedSourceTransactionStatuses.Booked)
                {
                    if (!string.Equals(row.ContentHash, hash, StringComparison.OrdinalIgnoreCase))
                        return await ConflictAsync(checkpoint, claim, BankFeedGapKinds.PayloadConflict, BankFeedReasonCodes.PayloadConflict,
                            "The provider returned different booked data under an existing stable identity. Existing booked data was retained.", raw, transaction, cancellationToken);
                    continue;
                }
                if (source.Status == BankFeedProviderTransactionStatuses.Pending) { row.ObservePending(hash, raw.Id, now); pending++; continue; }
                if (!source.BookingDateUtc.HasValue || !source.ValueDateUtc.HasValue)
                    return await ConflictAsync(checkpoint, claim, BankFeedGapKinds.MalformedSource, BankFeedReasonCodes.MalformedSource,
                        "A booked provider transaction did not include booking and value dates.", raw, transaction, cancellationToken);
                var bankTransaction = CreateBankTransaction(checkpoint, source, hash, now);
                _db.BankTransactions.Add(bankTransaction);
                row.PromoteToBooked(source.BookingDateUtc.Value, source.ValueDateUtc.Value, source.TransactionDateUtc,
                    source.Amount, source.Currency, source.ReferenceText, source.Counterparty,
                    source.ProviderTransactionReference, hash, raw.Id, bankTransaction.Id, now);
                booked++;
            }
            else
            {
                var status = source.Status == BankFeedProviderTransactionStatuses.Booked ? BankFeedSourceTransactionStatuses.Booked : BankFeedSourceTransactionStatuses.Pending;
                if (status == BankFeedSourceTransactionStatuses.Booked &&
                    (!source.BookingDateUtc.HasValue || !source.ValueDateUtc.HasValue))
                    return await ConflictAsync(checkpoint, claim, BankFeedGapKinds.MalformedSource,
                        BankFeedReasonCodes.MalformedSource,
                        "A booked provider transaction did not include booking and value dates.", raw,
                        transaction, cancellationToken);
                var rowToAdd = new BankFeedSourceTransaction(Guid.NewGuid(), claim.CompanyId, checkpoint.Id,
                    source.StableIdentity, status, source.BookingDateUtc, source.ValueDateUtc, source.TransactionDateUtc,
                    source.Amount, source.Currency, source.ReferenceText, source.Counterparty,
                    source.ProviderTransactionReference, hash, raw.Id, now);
                _db.BankFeedSourceTransactions.Add(rowToAdd);
                if (status == BankFeedSourceTransactionStatuses.Booked)
                {
                    var bankTransaction = CreateBankTransaction(checkpoint, source, hash, now);
                    _db.BankTransactions.Add(bankTransaction);
                    rowToAdd.PromoteToBooked(source.BookingDateUtc!.Value, source.ValueDateUtc!.Value, source.TransactionDateUtc,
                        source.Amount, source.Currency, source.ReferenceText, source.Counterparty,
                        source.ProviderTransactionReference, hash, raw.Id, bankTransaction.Id, now);
                    booked++;
                }
                else pending++;
            }
        }

        if (!string.IsNullOrWhiteSpace(page.NextContinuationToken))
        {
            var nextHash = Hash(page.NextContinuationToken);
            var repeated = string.Equals(nextHash, checkpoint.ContinuationTokenHash, StringComparison.OrdinalIgnoreCase) ||
                await _db.BankFeedCursorObservations.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == claim.CompanyId &&
                    x.CheckpointId == checkpoint.Id && x.SynchronizationRunId == claim.RunId && x.Phase == phase &&
                    x.CursorHash == nextHash, cancellationToken);
            if (repeated)
                return await ConflictAsync(checkpoint, claim, BankFeedGapKinds.CursorRegression, BankFeedReasonCodes.CursorRegression,
                    "The provider repeated an earlier pagination cursor. The retained range requires recovery before the checkpoint can advance.", raw, transaction, cancellationToken);
            _db.BankFeedCursorObservations.Add(new BankFeedCursorObservation(Guid.NewGuid(), claim.CompanyId,
                checkpoint.Id, claim.RunId, phase, nextHash, checkpoint.PageNumber, now));
            checkpoint.ContinuePage(claim.Owner, EncryptContinuation(checkpoint, page.NextContinuationToken), nextHash, booked, pending, now);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            _telemetry.Page(checkpoint.ProviderKey, phase, booked, pending, "committed");
            return false;
        }

        checkpoint.ContinuePage(claim.Owner, null, null, booked, pending, now);
        if (phase == BankFeedSynchronizationPhases.Booked)
        {
            checkpoint.BeginPendingPhase(claim.Owner, now);
            await _db.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            _telemetry.Page(checkpoint.ProviderKey, phase, booked, pending, "phase_completed");
            return false;
        }

        var recoveryGapId = checkpoint.RecoveryGapId;
        var actorId = checkpoint.RequestedByUserId;
        var lastCommittedIdentity = await _db.BankFeedBalanceSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == claim.CompanyId && x.CheckpointId == checkpoint.Id &&
                        x.LastCommittedTransactionIdentity != null)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => x.LastCommittedTransactionIdentity)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(lastCommittedIdentity) &&
            !await _db.BankFeedSourceTransactions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.CompanyId == claim.CompanyId && x.CheckpointId == checkpoint.Id &&
                x.StableIdentity == lastCommittedIdentity, cancellationToken))
            return await ConflictAsync(checkpoint, claim, BankFeedGapKinds.MissingRange,
                BankFeedReasonCodes.RecoveryRequired,
                "The provider balance references a last committed transaction that is absent from the retained synchronization range.",
                raw, transaction, cancellationToken);
        var completedWindow = $"{checkpoint.WindowFrom:yyyy-MM-dd}..{checkpoint.WindowTo:yyyy-MM-dd}";
        var completedSummary = $"Bank feed synchronization committed {checkpoint.ImportedBookedCount + booked} booked and {checkpoint.ObservedPendingCount + pending} pending source row(s).";
        await AddAuditAsync(checkpoint, "bank_feed_synchronization_completed", "succeeded",
            completedSummary, null, checkpoint.Status, $"ready:{completedWindow}", cancellationToken);
        checkpoint.Complete(claim.Owner, now, TimeSpan.FromMinutes(Math.Clamp(_options.Value.SynchronizationIntervalMinutes, 1, 1440)));
        if (recoveryGapId.HasValue)
        {
            var gap = await _db.BankFeedGaps.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == claim.CompanyId && x.Id == recoveryGapId.Value && x.Status == BankFeedGapStatuses.Open, cancellationToken);
            gap?.Resolve(actorId, now);
        }
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        _telemetry.Page(checkpoint.ProviderKey, phase, booked, pending, "synchronization_completed");
        return true;
    }

    private async Task<bool> ConflictAsync(BankFeedCheckpoint checkpoint, Claim claim, string gapKind,
        string reasonCode, string summary, BankFeedRawSourceObject raw,
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction,
        CancellationToken cancellationToken)
    {
        var now = Now();
        checkpoint.RequireAttention(claim.Owner, reasonCode, summary, now);
        _db.BankFeedGaps.Add(new BankFeedGap(Guid.NewGuid(), claim.CompanyId, checkpoint.Id, gapKind,
            checkpoint.WindowFrom!.Value, checkpoint.WindowTo!.Value, reasonCode, summary, now));
        await AddAuditAsync(checkpoint, "bank_feed_synchronization_attention_required", "failed",
            summary, reasonCode, BankFeedCheckpointStatuses.Running,
            BankFeedCheckpointStatuses.AttentionRequired, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
        _telemetry.Synchronization("attention_required", checkpoint.ProviderKey, checkpoint.Phase, reasonCode);
        return true;
    }

    private async Task HandleFailureAsync(Claim claim, string reasonCode, string summary, bool transient,
        TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var checkpoint = await _db.BankFeedCheckpoints.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.CheckpointId, cancellationToken);
        if (checkpoint is null || !checkpoint.IsClaimedBy(claim.Owner, Now())) return;
        if (transient && checkpoint.AttemptCount < Math.Clamp(_options.Value.MaximumAttempts, 1, 20))
        {
            var delaySeconds = Math.Min(_options.Value.MaximumRetryDelaySeconds,
                _options.Value.BaseRetryDelaySeconds * Math.Pow(2, Math.Max(0, checkpoint.AttemptCount - 1)));
            var delay = retryAfter.HasValue && retryAfter.Value.TotalSeconds > delaySeconds ? retryAfter.Value : TimeSpan.FromSeconds(delaySeconds);
            checkpoint.Retry(claim.Owner, reasonCode, summary, Now(), delay);
            await AddAuditAsync(checkpoint, "bank_feed_synchronization_retry_scheduled", "retry_scheduled",
                summary, reasonCode, BankFeedCheckpointStatuses.Running,
                BankFeedCheckpointStatuses.Failed, cancellationToken);
        }
        else
        {
            checkpoint.RequireAttention(claim.Owner, reasonCode, summary, Now());
            if (checkpoint.WindowFrom.HasValue && checkpoint.WindowTo.HasValue)
                _db.BankFeedGaps.Add(new BankFeedGap(Guid.NewGuid(), claim.CompanyId, checkpoint.Id,
                    reasonCode == BankFeedReasonCodes.MalformedSource ? BankFeedGapKinds.MalformedSource : BankFeedGapKinds.MissingRange,
                    checkpoint.WindowFrom.Value, checkpoint.WindowTo.Value, reasonCode, summary, Now()));
            await AddAuditAsync(checkpoint, "bank_feed_synchronization_attention_required", "failed",
                summary, reasonCode, BankFeedCheckpointStatuses.Running,
                BankFeedCheckpointStatuses.AttentionRequired, cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Synchronization(transient ? "retry" : "attention_required", checkpoint.ProviderKey, checkpoint.Phase, reasonCode);
    }

    private async Task MarkAttentionAsync(Claim claim, string reasonCode, string summary, string? gapKind,
        CancellationToken cancellationToken)
    {
        var checkpoint = await _db.BankFeedCheckpoints.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.CheckpointId, cancellationToken);
        if (checkpoint is null || !checkpoint.IsClaimedBy(claim.Owner, Now())) return;
        checkpoint.RequireAttention(claim.Owner, reasonCode, summary, Now());
        if (gapKind is not null && checkpoint.WindowFrom.HasValue && checkpoint.WindowTo.HasValue)
            _db.BankFeedGaps.Add(new BankFeedGap(Guid.NewGuid(), claim.CompanyId, checkpoint.Id, gapKind,
                checkpoint.WindowFrom.Value, checkpoint.WindowTo.Value, reasonCode, summary, Now()));
        await AddAuditAsync(checkpoint, "bank_feed_synchronization_attention_required", "failed",
            summary, reasonCode, BankFeedCheckpointStatuses.Running,
            BankFeedCheckpointStatuses.AttentionRequired, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.Synchronization("attention_required", checkpoint.ProviderKey, checkpoint.Phase, reasonCode);
    }

    private BankFeedRawSourceObject CreateRaw(BankFeedCheckpoint checkpoint, string kind, string identity,
        ReadOnlyMemory<byte> evidence, string contentType, DateTime now)
    {
        var id = Guid.NewGuid(); var checksum = Convert.ToHexString(SHA256.HashData(evidence.Span)).ToLowerInvariant();
        var encrypted = _encryption.Encrypt(checkpoint.CompanyId, $"bank-feed:{checkpoint.Id:D}:{id:D}:raw:v1", Convert.ToBase64String(evidence.Span));
        return new BankFeedRawSourceObject(id, checkpoint.CompanyId, checkpoint.Id, checkpoint.SynchronizationRunId!.Value,
            identity, kind, checksum, encrypted, contentType, now.AddDays(Math.Clamp(_options.Value.RawEvidenceRetentionDays, 1, 3650)), now);
    }

    private async Task AddAuditAsync(BankFeedCheckpoint checkpoint, string eventType, string outcome,
        string summary, string? reasonCode, string? beforeState, string? afterState,
        CancellationToken cancellationToken)
    {
        var actorId = checkpoint.RequestedByUserId ?? await _db.BankConnections.IgnoreQueryFilters()
            .Where(x => x.CompanyId == checkpoint.CompanyId && x.Id == checkpoint.ConnectionId)
            .Select(x => x.ConnectedByUserId)
            .SingleAsync(cancellationToken);
        _db.BankConnectionAuditEvents.Add(new BankConnectionAuditEvent(Guid.NewGuid(), checkpoint.CompanyId,
            checkpoint.ConnectionId, actorId, eventType, outcome, summary, reasonCode,
            checkpoint.CorrelationId, beforeState, afterState, Now()));
    }

    private static BankTransaction CreateBankTransaction(BankFeedCheckpoint checkpoint,
        BankFeedProviderTransaction source, string contentHash, DateTime now)
    {
        var stableHash = Hash($"{checkpoint.ProviderKey}|{checkpoint.StableProviderAccountId}|{source.StableIdentity}");
        return new BankTransaction(Guid.NewGuid(), checkpoint.CompanyId, checkpoint.CompanyBankAccountId,
            source.BookingDateUtc!.Value, source.ValueDateUtc!.Value, source.Amount, source.Currency,
            source.ReferenceText, source.Counterparty, $"feed:{stableHash}", checkpoint.ProviderKey,
            createdUtc: now, updatedUtc: now, rowIdentity: stableHash, rowContentHash: contentHash);
    }

    private string? DecryptContinuation(BankFeedCheckpoint checkpoint) => checkpoint.ContinuationTokenEnvelope is null ? null :
        _encryption.Decrypt(checkpoint.CompanyId, CursorPurpose(checkpoint), checkpoint.ContinuationTokenEnvelope);
    private string EncryptContinuation(BankFeedCheckpoint checkpoint, string value) =>
        _encryption.Encrypt(checkpoint.CompanyId, CursorPurpose(checkpoint), value);
    private static string CursorPurpose(BankFeedCheckpoint checkpoint) =>
        $"bank-feed:{checkpoint.Id:D}:{checkpoint.SynchronizationRunId:D}:{checkpoint.Phase}:cursor:v1";
    private static string ContentHash(BankFeedProviderTransaction source) => Hash(string.Join("|", source.StableIdentity,
        source.Status, source.BookingDateUtc?.ToString("O"), source.ValueDateUtc?.ToString("O"), source.TransactionDateUtc.ToString("O"),
        source.Amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), source.Currency.ToUpperInvariant(),
        source.ReferenceText, source.Counterparty, source.ProviderTransactionReference));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private sealed record Claim(Guid CompanyId, Guid CheckpointId, Guid ConnectionId, string Owner, Guid RunId);
}

public sealed class BankFeedSynchronizationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<BankFeedSynchronizationOptions> _options;
    private readonly ILogger<BankFeedSynchronizationBackgroundService> _logger;
    public BankFeedSynchronizationBackgroundService(IServiceScopeFactory scopes,
        IOptions<BankFeedSynchronizationOptions> options, ILogger<BankFeedSynchronizationBackgroundService> logger)
    { _scopes = scopes; _options = options; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IBankFeedSynchronizationRunner>().RunDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Bank feed synchronization cycle failed."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.Value.PollIntervalSeconds, 10, 3600)), stoppingToken);
        }
    }
}
