using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingMigrationWorkerOptions
{
    public const string SectionName = "AccountingMigration";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 15;
    public int BatchSize { get; set; } = 50;
    public int ClaimBatchSize { get; set; } = 4;
    public int LeaseSeconds { get; set; } = 60;
    public int MaximumAttempts { get; set; } = 3;
}

public sealed class AccountingHistoricalMigrationService : IAccountingMigrationService, IAccountingMigrationJobRunner
{
    internal const string TargetVersion = "native-ledger-v1";
    private static readonly Regex VoucherNumberPattern = new(
        "^(?<prefix>[A-Z0-9]{1,16})-(?<year>[0-9]{4})-(?<number>[0-9]{6})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _auditWriter;
    private readonly AccountingOperationsTelemetry _telemetry;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<AccountingMigrationWorkerOptions> _options;
    private readonly ILogger<AccountingHistoricalMigrationService> _logger;

    public AccountingHistoricalMigrationService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter auditWriter,
        AccountingOperationsTelemetry telemetry,
        TimeProvider timeProvider,
        IOptions<AccountingMigrationWorkerOptions> options,
        ILogger<AccountingHistoricalMigrationService> logger)
    {
        _dbContext = dbContext;
        _auditWriter = auditWriter;
        _telemetry = telemetry;
        _timeProvider = timeProvider;
        _options = options;
        _logger = logger;
    }

    public async Task<AccountingMigrationRunDto?> GetLatestAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var run = await LoadLatestAsync(companyId, cancellationToken);
        return run is null ? null : Map(run);
    }

    public async Task<AccountingMigrationRunDto> StartAsync(
        StartAccountingMigrationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCompany(command.CompanyId);
        if (command.ActorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(command));
        var idempotencyKey = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 200);

        var companyExists = await _dbContext.Companies.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.Id == command.CompanyId, cancellationToken);
        if (!companyExists) throw new KeyNotFoundException("Company was not found.");

        var replay = await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters()
            .Include(x => x.Conflicts).Include(x => x.Reports).ThenInclude(x => x.FiscalPeriod)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (replay is not null) return Map(replay);

        var active = await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters()
            .Include(x => x.Conflicts).Include(x => x.Reports).ThenInclude(x => x.FiscalPeriod)
            .OrderByDescending(x => x.RequestedUtc)
            .FirstOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
                (x.Status == AccountingMigrationRunStatuses.Queued || x.Status == AccountingMigrationRunStatuses.Running), cancellationToken);
        if (active is not null) return Map(active);

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var run = new AccountingMigrationRun(Guid.NewGuid(), command.CompanyId, TargetVersion, idempotencyKey,
            command.ActorUserId, command.CorrelationId, nowUtc);
        _dbContext.AccountingMigrationRuns.Add(run);

        if (!await RequiresMigrationAsync(command.CompanyId, cancellationToken))
            run.MarkNotRequired(nowUtc);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // The database constraints are the final concurrency boundary when two operators
            // request the same replay, or two workers attempt to create an active target run.
            _dbContext.ChangeTracker.Clear();
            var concurrentRun = await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters()
                .Include(x => x.Conflicts).Include(x => x.Reports).ThenInclude(x => x.FiscalPeriod)
                .OrderByDescending(x => x.RequestedUtc)
                .FirstOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
                    (x.IdempotencyKey == idempotencyKey ||
                     (x.TargetVersion == TargetVersion &&
                      (x.Status == AccountingMigrationRunStatuses.Queued ||
                       x.Status == AccountingMigrationRunStatuses.Running))), cancellationToken);
            if (concurrentRun is null) throw;
            return Map(concurrentRun);
        }
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingMigrationRequested, run.Id, AuditEventOutcomes.Requested,
            run.Status == AccountingMigrationRunStatuses.NotRequired
                ? "Historical accounting migration was checked and no legacy backfill was required."
                : "Historical accounting migration was queued for bounded background processing.",
            command.CorrelationId, new Dictionary<string, string?>
            {
                ["targetVersion"] = TargetVersion,
                ["idempotencyKey"] = idempotencyKey,
                ["status"] = run.Status
            }, cancellationToken);

        if (run.Status == AccountingMigrationRunStatuses.Queued)
            _telemetry.MigrationStarted(command.CompanyId, run.Id, command.CorrelationId);

        return Map(run);
    }

    public async Task<AccountingMigrationRunDto> ResolveConflictAsync(
        ResolveAccountingMigrationConflictCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCompany(command.CompanyId);
        if (command.ActorUserId == Guid.Empty) throw new ArgumentException("ActorUserId is required.", nameof(command));

        var conflict = await _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ConflictId, cancellationToken)
            ?? throw new KeyNotFoundException("Accounting migration conflict was not found.");
        try
        {
            conflict.Resolve(command.ResolutionSummary, command.ActorUserId, command.ExpectedVersion,
                _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (InvalidOperationException exception)
        {
            throw new AccountingAuthorityException(AccountingOperationsReasonCodes.MigrationConflictStale,
                exception.Message, isConflict: true);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingMigrationConflictResolved, conflict.Id, AuditEventOutcomes.Succeeded,
            "An accounting migration conflict was marked resolved after operator review.", command.CorrelationId,
            new Dictionary<string, string?>
            {
                ["migrationRunId"] = conflict.MigrationRunId.ToString("D"),
                ["reasonCode"] = conflict.ReasonCode,
                ["entityType"] = conflict.EntityType,
                ["entityId"] = conflict.EntityId
            }, cancellationToken);

        var run = await LoadAsync(command.CompanyId, conflict.MigrationRunId, cancellationToken)
            ?? throw new KeyNotFoundException("Accounting migration was not found.");
        return Map(run);
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.Enabled) return 0;
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var maximumAttempts = Math.Max(1, _options.Value.MaximumAttempts);
        const string leaseFailureCode = "accounting_migration_lease_recovery_exhausted";
        var exhaustedRuns = await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == AccountingMigrationRunStatuses.Running &&
                (x.LeaseExpiresUtc == null || x.LeaseExpiresUtc <= nowUtc) &&
                x.AttemptCount >= maximumAttempts)
            .Select(x => new { x.Id, x.CompanyId, x.CorrelationId })
            .ToArrayAsync(cancellationToken);
        if (exhaustedRuns.Length > 0)
        {
            var exhaustedIds = exhaustedRuns.Select(x => x.Id).ToArray();
            await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters()
                .Where(x => exhaustedIds.Contains(x.Id) &&
                    x.Status == AccountingMigrationRunStatuses.Running &&
                    (x.LeaseExpiresUtc == null || x.LeaseExpiresUtc <= nowUtc) &&
                    x.AttemptCount >= maximumAttempts)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AccountingMigrationRunStatuses.Failed)
                    .SetProperty(x => x.FailureCode, leaseFailureCode)
                    .SetProperty(x => x.FailureSummary,
                        "The migration worker lease expired repeatedly. Review worker health and safely start a new idempotent run.")
                    .SetProperty(x => x.LeaseOwner, (string?)null)
                    .SetProperty(x => x.LeaseExpiresUtc, (DateTime?)null)
                    .SetProperty(x => x.CompletedUtc, nowUtc)
                    .SetProperty(x => x.UpdatedUtc, nowUtc)
                    .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            foreach (var exhausted in exhaustedRuns)
                _telemetry.MigrationLeaseExhausted(exhausted.CompanyId, exhausted.Id, leaseFailureCode,
                    exhausted.CorrelationId);
        }

        var candidateIds = await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.AttemptCount < maximumAttempts &&
                (x.Status == AccountingMigrationRunStatuses.Queued ||
                (x.Status == AccountingMigrationRunStatuses.Running &&
                 (x.LeaseExpiresUtc == null || x.LeaseExpiresUtc <= nowUtc))))
            .OrderBy(x => x.RequestedUtc)
            .Take(Math.Max(1, _options.Value.ClaimBatchSize))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);
        var handled = 0;

        foreach (var runId in candidateIds)
        {
            var leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
            var leaseExpiresUtc = nowUtc.AddSeconds(Math.Max(15, _options.Value.LeaseSeconds));
            var claimed = await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters()
                .Where(x => x.Id == runId &&
                    x.AttemptCount < maximumAttempts &&
                    (x.Status == AccountingMigrationRunStatuses.Queued ||
                     (x.Status == AccountingMigrationRunStatuses.Running &&
                      (x.LeaseExpiresUtc == null || x.LeaseExpiresUtc <= nowUtc))))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AccountingMigrationRunStatuses.Running)
                    .SetProperty(x => x.LeaseOwner, leaseOwner)
                    .SetProperty(x => x.LeaseExpiresUtc, leaseExpiresUtc)
                    .SetProperty(x => x.StartedUtc, x => x.StartedUtc ?? nowUtc)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.UpdatedUtc, nowUtc)
                    .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            if (claimed == 0) continue;

            handled++;
            _dbContext.ChangeTracker.Clear();
            var run = await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == runId && x.LeaseOwner == leaseOwner, cancellationToken);
            var attemptNumber = run.AttemptCount;
            try
            {
                await ProcessBatchAsync(run, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var safeSummary = Safe(exception.Message, 1000);
                const string failureCode = "accounting_migration_batch_failed";
                if (attemptNumber < maximumAttempts)
                    run.ScheduleRetry(failureCode, safeSummary, attemptNumber,
                        _timeProvider.GetUtcNow().UtcDateTime);
                else
                    run.Fail(failureCode, safeSummary, attemptNumber, _timeProvider.GetUtcNow().UtcDateTime);
                await _dbContext.SaveChangesAsync(cancellationToken);
                _telemetry.MigrationFailed(run.CompanyId, run.Id, failureCode, run.CorrelationId, exception);
            }
        }

        return handled;
    }

    private async Task ProcessBatchAsync(AccountingMigrationRun run, CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(_options.Value.BatchSize, 1, 500);
        switch (run.Phase)
        {
            case AccountingMigrationPhases.Inventory:
                await ProcessInventoryAsync(run, batchSize, cancellationToken);
                break;
            case AccountingMigrationPhases.Accounts:
                await ProcessAccountsAsync(run, batchSize, cancellationToken);
                break;
            case AccountingMigrationPhases.Journals:
                await ProcessJournalsAsync(run, batchSize, cancellationToken);
                break;
            case AccountingMigrationPhases.Reports:
                await ProcessReportsAsync(run, batchSize, cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unsupported accounting migration phase '{run.Phase}'.");
        }
    }

    private async Task ProcessInventoryAsync(
        AccountingMigrationRun run,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var scanned = 0;
        var conflictsCreated = 0;
        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == run.CompanyId, cancellationToken);
        scanned++;
        if (configuration is null)
        {
            conflictsCreated += await AddConflictAsync(run, "accounting_configuration", run.CompanyId.ToString("D"), null,
                AccountingMigrationConflictReasonCodes.ConfigurationMissing,
                "Historical Finance records exist, but no authoritative accounting configuration is recorded.",
                JsonSerializer.Serialize(new { run.CompanyId, run.TargetVersion }),
                "Complete accounting setup using locally reviewed currency, fiscal-year, authority, account-role, and policy-pack facts before cutover.",
                cancellationToken);
        }

        var existingReconciliationIds = await ConflictEntityIdsAsync(run.CompanyId, run.Id,
            "bank_reconciliation", cancellationToken);
        var reconciliationRows = await _dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.PostingState == BankTransactionPostingStates.Conflict &&
                !existingReconciliationIds.Contains(x.Id))
            .OrderBy(x => x.UpdatedUtc).ThenBy(x => x.Id)
            .Take(batchSize)
            .Select(x => new ReconciliationInventoryRow(x.Id, x.BankTransactionId, x.ConflictCode,
                x.ConflictDetails, x.SourceVersion))
            .ToArrayAsync(cancellationToken);
        foreach (var row in reconciliationRows)
        {
            scanned++;
            conflictsCreated += await AddConflictAsync(run, "bank_reconciliation", row.Id.ToString("D"), null,
                AccountingMigrationConflictReasonCodes.ReconciliationStateConflict,
                "A historical bank transaction has an unresolved reconciliation or posting-state conflict.",
                JsonSerializer.Serialize(row),
                "Review the bank transaction, linked payments, source mapping, and any suspense posting before confirming its accounting treatment.",
                cancellationToken);
        }

        var remainingCapacity = Math.Max(0, batchSize - reconciliationRows.Length);
        var existingProviderIds = await ConflictEntityIdsAsync(run.CompanyId, run.Id,
            "accounting_provider_export", cancellationToken);
        ProviderInventoryRow[] providerRows = remainingCapacity == 0
            ? []
            : await _dbContext.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == run.CompanyId &&
                    x.Status == AccountingProviderExportStatuses.ReconciliationRequired &&
                    !existingProviderIds.Contains(x.Id))
                .OrderBy(x => x.UpdatedUtc).ThenBy(x => x.Id)
                .Take(remainingCapacity)
                .Select(x => new ProviderInventoryRow(x.Id, x.LedgerEntryId, x.ProviderKey, x.SourceType,
                    x.SourceId, x.SourceVersion, x.FailureCategory, x.SafeSummary, x.AttemptCount))
                .ToArrayAsync(cancellationToken);
        foreach (var row in providerRows)
        {
            scanned++;
            conflictsCreated += await AddConflictAsync(run, "accounting_provider_export", row.Id.ToString("D"), null,
                AccountingMigrationConflictReasonCodes.ProviderOutcomeAmbiguous,
                "A historical provider export has an unknown external outcome and cannot be treated as completed.",
                JsonSerializer.Serialize(row),
                "Check the provider using the stable source identity, then reconcile the export as sent or not sent before retrying.",
                cancellationToken);
        }

        if (conflictsCreated > 0) await _dbContext.SaveChangesAsync(cancellationToken);
        var inventoriedReconciliationIds = existingReconciliationIds.Concat(reconciliationRows.Select(row => row.Id)).ToArray();
        var inventoriedProviderIds = existingProviderIds.Concat(providerRows.Select(row => row.Id)).ToArray();
        var hasMoreReconciliation = await _dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == run.CompanyId && x.PostingState == BankTransactionPostingStates.Conflict &&
                !inventoriedReconciliationIds.Contains(x.Id), cancellationToken);
        var hasMoreProviders = await _dbContext.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == run.CompanyId &&
                x.Status == AccountingProviderExportStatuses.ReconciliationRequired &&
                !inventoriedProviderIds.Contains(x.Id), cancellationToken);
        var nextPhase = hasMoreReconciliation || hasMoreProviders
            ? AccountingMigrationPhases.Inventory
            : AccountingMigrationPhases.Accounts;
        run.RecordBatch(nextPhase, scanned, 0,
            await CountOpenConflictsAsync(run.CompanyId, run.Id, cancellationToken),
            await CountReportsAsync(run.CompanyId, run.Id, cancellationToken), nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _telemetry.MigrationBatch(run.CompanyId, run.Id, AccountingMigrationPhases.Inventory,
            scanned, 0, conflictsCreated);
    }

    private async Task ProcessAccountsAsync(AccountingMigrationRun run, int batchSize, CancellationToken cancellationToken)
    {
        var processedIds = (await _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.MigrationRunId == run.Id && x.EntityType == "finance_account")
            .Select(x => x.EntityId).ToArrayAsync(cancellationToken))
            .Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty).Where(x => x != Guid.Empty).ToArray();
        var accounts = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId &&
                (x.AccountClass == null || x.NormalBalance == null || !x.IsPostingEnabled) &&
                !processedIds.Contains(x.Id))
            .OrderBy(x => x.Code).ThenBy(x => x.Id)
            .Take(batchSize)
            .Select(x => new { x.Id, x.Code, x.Name, x.AccountType, x.AccountClass, x.NormalBalance, x.IsPostingEnabled })
            .ToArrayAsync(cancellationToken);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var updated = 0;
        var conflictsCreated = 0;

        foreach (var account in accounts)
        {
            if (!TryMapAccountSemantics(account.AccountType, out var accountClass, out var normalBalance))
            {
                conflictsCreated += await AddConflictAsync(run, "finance_account", account.Id.ToString("D"), null,
                    AccountingMigrationConflictReasonCodes.AmbiguousAccountSemantics,
                    $"Account {account.Code} cannot be classified safely from its historical account type.",
                    JsonSerializer.Serialize(new { account.Id, account.Code, account.Name, account.AccountType }),
                    "Classify the account in the chart of accounts, confirm its normal balance, and start a new migration run.",
                    cancellationToken);
                continue;
            }

            updated += await _dbContext.FinanceAccounts.IgnoreQueryFilters()
                .Where(x => x.CompanyId == run.CompanyId && x.Id == account.Id &&
                    (x.AccountClass == null || x.NormalBalance == null || !x.IsPostingEnabled))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.AccountClass, accountClass)
                    .SetProperty(x => x.NormalBalance, normalBalance)
                    .SetProperty(x => x.IsPostingEnabled, true)
                    .SetProperty(x => x.UpdatedUtc, nowUtc), cancellationToken);
        }

        if (conflictsCreated > 0) await _dbContext.SaveChangesAsync(cancellationToken);
        var conflictAccountIds = (await _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CompanyId == run.CompanyId && c.MigrationRunId == run.Id && c.EntityType == "finance_account")
            .Select(c => c.EntityId).ToArrayAsync(cancellationToken))
            .Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty).Where(x => x != Guid.Empty).ToArray();
        var hasRemaining = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == run.CompanyId &&
                (x.AccountClass == null || x.NormalBalance == null || !x.IsPostingEnabled) &&
                !conflictAccountIds.Contains(x.Id),
                cancellationToken);
        var nextPhase = hasRemaining ? AccountingMigrationPhases.Accounts : AccountingMigrationPhases.Journals;
        var openConflicts = await CountOpenConflictsAsync(run.CompanyId, run.Id, cancellationToken);
        run.RecordBatch(nextPhase, accounts.Length, updated, openConflicts,
            await CountReportsAsync(run.CompanyId, run.Id, cancellationToken), nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _telemetry.MigrationBatch(run.CompanyId, run.Id, AccountingMigrationPhases.Accounts,
            accounts.Length, updated, conflictsCreated);
    }

    private async Task ProcessJournalsAsync(AccountingMigrationRun run, int batchSize, CancellationToken cancellationToken)
    {
        var processedIds = (await _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.MigrationRunId == run.Id && x.EntityType == "ledger_entry")
            .Select(x => x.EntityId).ToArrayAsync(cancellationToken))
            .Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty).Where(x => x != Guid.Empty).ToArray();
        var entries = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.Status == LedgerEntryStatuses.Posted &&
                (x.VoucherSeriesId == null || x.VoucherSequenceNumber == null || x.VoucherFiscalYear == null ||
                 x.PostingDate == null || x.BaseCurrency == null || x.PostingType == null ||
                 x.SourceVersion == null || x.IdempotencyKey == null || x.PolicyPackKey == null || x.PolicyPackVersion == null) &&
                !processedIds.Contains(x.Id))
            .OrderBy(x => x.EntryUtc).ThenBy(x => x.EntryNumber).ThenBy(x => x.Id)
            .Take(batchSize)
            .Select(x => new HistoricalJournalRow(x.Id, x.FiscalPeriodId, x.EntryNumber, x.EntryUtc, x.PostedAtUtc,
                x.SourceType, x.SourceId, x.VoucherSeriesId, x.VoucherSequenceNumber, x.VoucherFiscalYear,
                x.PostingDate, x.BaseCurrency, x.PostingType, x.SourceVersion, x.IdempotencyKey,
                x.PolicyPackKey, x.PolicyPackVersion, x.Description))
            .ToArrayAsync(cancellationToken);
        var entryIds = entries.Select(x => x.Id).ToArray();
        var lines = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && entryIds.Contains(x.LedgerEntryId))
            .OrderBy(x => x.Id)
            .Select(x => new HistoricalLineRow(x.Id, x.LedgerEntryId, x.FinanceAccountId, x.DebitAmount, x.CreditAmount, x.Currency, x.TaxFactsJson))
            .ToArrayAsync(cancellationToken);
        var mappings = await _dbContext.LedgerEntrySourceMappings.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && entryIds.Contains(x.LedgerEntryId))
            .Select(x => new HistoricalMappingRow(x.LedgerEntryId, x.SourceType, x.SourceId))
            .ToArrayAsync(cancellationToken);
        var series = await _dbContext.VoucherSeries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId)
            .Select(x => new VoucherSeriesRow(x.Id, x.NumberPrefix))
            .ToArrayAsync(cancellationToken);
        var selections = await _dbContext.AccountingPolicyPackSelections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId)
            .Select(x => new { x.PackKey, x.PackVersion, x.EffectiveFrom, x.EffectiveTo })
            .ToArrayAsync(cancellationToken);
        var identities = await _dbContext.LedgerPostingIdentities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && entryIds.Contains(x.LedgerEntryId))
            .Select(x => x.LedgerEntryId).ToArrayAsync(cancellationToken);
        var entriesWithKnownTax = (await _dbContext.CustomerInvoiceAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == run.CompanyId && x.LedgerEntryId.HasValue &&
                    entryIds.Contains(x.LedgerEntryId.Value) && x.TaxBaseAmount != 0m)
                .Select(x => x.LedgerEntryId!.Value).ToArrayAsync(cancellationToken))
            .Concat(await _dbContext.SupplierBillAccountingProfiles.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == run.CompanyId && x.LedgerEntryId.HasValue &&
                    entryIds.Contains(x.LedgerEntryId.Value) &&
                    (x.RecoverableTaxBaseAmount != 0m || x.NonRecoverableTaxAmount != 0m))
                .Select(x => x.LedgerEntryId!.Value).ToArrayAsync(cancellationToken))
            .ToHashSet();
        var auditedTargets = (await _dbContext.AuditEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.Action == AuditEventActions.AccountingJournalMigrated)
            .Select(x => x.TargetId).ToArrayAsync(cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var updated = 0;
        var conflictsCreated = 0;

        foreach (var entry in entries)
        {
            var entryLines = lines.Where(x => x.LedgerEntryId == entry.Id).ToArray();
            var entryMappings = mappings.Where(x => x.LedgerEntryId == entry.Id).ToArray();
            var postingDate = entry.PostingDate ?? DateOnly.FromDateTime(entry.PostedAtUtc ?? entry.EntryUtc);
            var source = ResolveSource(entry.SourceType, entry.SourceId, entryMappings);
            var currencies = entryLines.Select(x => x.Currency.Trim().ToUpperInvariant()).Distinct(StringComparer.Ordinal).ToArray();
            var baseCurrency = entry.BaseCurrency ?? (currencies.Length == 1 ? currencies[0] : null);
            var sourceVersion = entry.SourceVersion ?? (source is null ? null : BuildLegacySourceVersion(entry, entryLines));
            var idempotencyKey = entry.IdempotencyKey ?? $"accounting-migration:{run.CompanyId:N}:{entry.Id:N}";
            var postingType = entry.PostingType ?? ResolvePostingType(source?.SourceType);
            var policy = selections.SingleOrDefault(x => x.EffectiveFrom <= postingDate &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= postingDate));
            var policyKey = entry.PolicyPackKey ?? policy?.PackKey;
            var policyVersion = entry.PolicyPackVersion ?? policy?.PackVersion;
            var voucher = ResolveVoucher(entry, series);
            var voucherSeriesId = voucher.HasValue ? voucher.Value.SeriesId : entry.VoucherSeriesId;
            var voucherFiscalYear = voucher.HasValue ? voucher.Value.FiscalYear : entry.VoucherFiscalYear;
            var voucherSequenceNumber = voucher.HasValue ? voucher.Value.SequenceNumber : entry.VoucherSequenceNumber;
            var debit = entryLines.Sum(x => x.DebitAmount);
            var credit = entryLines.Sum(x => x.CreditAmount);

            if (Math.Abs(debit - credit) > 0.0001m)
                conflictsCreated += await AddJournalConflictAsync(run, entry,
                    AccountingMigrationConflictReasonCodes.JournalUnbalanced,
                    "The historical journal is not balanced and cannot be accepted as authoritative accounting history.",
                    new { entry.Id, entry.EntryNumber, debit, credit },
                    "Create an evidence-backed correction in an open period and have an accountant review the historical difference.", cancellationToken);
            if (source is null)
                conflictsCreated += await AddJournalConflictAsync(run, entry,
                    entryMappings.Length > 1 ? AccountingMigrationConflictReasonCodes.JournalSourceMismatch : AccountingMigrationConflictReasonCodes.JournalSourceMissing,
                    "The historical journal does not have one unambiguous business source.",
                    new { entry.Id, entry.EntryNumber, entry.SourceType, entry.SourceId, mappings = entryMappings },
                    "Link the journal to its verified source record or document, then start a new migration run.", cancellationToken);
            if (baseCurrency is null)
                conflictsCreated += await AddJournalConflictAsync(run, entry,
                    AccountingMigrationConflictReasonCodes.JournalCurrencyAmbiguous,
                    "The historical journal contains no single unambiguous base currency.",
                    new { entry.Id, entry.EntryNumber, currencies },
                    "Confirm the historical base-currency amounts with an accountant before recording a correction or migration mapping.", cancellationToken);
            if (voucher is null)
                conflictsCreated += await AddJournalConflictAsync(run, entry,
                    AccountingMigrationConflictReasonCodes.JournalVoucherAmbiguous,
                    "The historical journal number cannot be mapped safely to a configured voucher series and sequence.",
                    new { entry.Id, entry.EntryNumber },
                    "Map the historical voucher using verified source evidence. Do not allocate a new number over the old record.", cancellationToken);
            if (policyKey is null || policyVersion is null)
                conflictsCreated += await AddJournalConflictAsync(run, entry,
                    AccountingMigrationConflictReasonCodes.JournalPolicyVersionUnknown,
                    "No policy-pack selection was effective on the historical posting date.",
                    new { entry.Id, entry.EntryNumber, postingDate },
                    "Record the historically effective pack after local review; do not apply the current pack retroactively.", cancellationToken);
            if (entriesWithKnownTax.Contains(entry.Id) && entryLines.All(x => string.IsNullOrWhiteSpace(x.TaxFactsJson)))
                conflictsCreated += await AddJournalConflictAsync(run, entry,
                    AccountingMigrationConflictReasonCodes.JournalTaxFactsUnknown,
                    "The historical source records a tax amount, but the posted journal has no immutable line-level tax facts.",
                    new { entry.Id, entry.EntryNumber, entry.SourceType, entry.SourceId },
                    "Reconstruct tax facts only from reviewed source evidence and the historically effective policy pack; otherwise retain this conflict.",
                    cancellationToken);

            var affected = await _dbContext.LedgerEntries.IgnoreQueryFilters()
                .Where(x => x.CompanyId == run.CompanyId && x.Id == entry.Id && x.Status == LedgerEntryStatuses.Posted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.PostingDate, postingDate)
                    .SetProperty(x => x.DocumentDate, x => x.DocumentDate ?? postingDate)
                    .SetProperty(x => x.BaseCurrency, baseCurrency ?? entry.BaseCurrency)
                    .SetProperty(x => x.PostingType, postingType ?? entry.PostingType)
                    .SetProperty(x => x.SourceType, source == null ? entry.SourceType : source.SourceType)
                    .SetProperty(x => x.SourceId, source == null ? entry.SourceId : source.SourceId)
                    .SetProperty(x => x.SourceVersion, sourceVersion ?? entry.SourceVersion)
                    .SetProperty(x => x.IdempotencyKey, idempotencyKey)
                    .SetProperty(x => x.PolicyPackKey, policyKey ?? entry.PolicyPackKey)
                    .SetProperty(x => x.PolicyPackVersion, policyVersion ?? entry.PolicyPackVersion)
                    .SetProperty(x => x.VoucherSeriesId, voucherSeriesId)
                    .SetProperty(x => x.VoucherFiscalYear, voucherFiscalYear)
                    .SetProperty(x => x.VoucherSequenceNumber, voucherSequenceNumber)
                    .SetProperty(x => x.UpdatedUtc, nowUtc), cancellationToken);
            updated += affected;

            if (source is not null && entryMappings.Length == 0)
                _dbContext.LedgerEntrySourceMappings.Add(new LedgerEntrySourceMapping(Guid.NewGuid(), run.CompanyId,
                    entry.Id, source.SourceType, source.SourceId, entry.PostedAtUtc ?? entry.EntryUtc, nowUtc));

            if (source is not null && sourceVersion is not null && !identities.Contains(entry.Id))
                _dbContext.LedgerPostingIdentities.Add(new LedgerPostingIdentity(Guid.NewGuid(), run.CompanyId,
                    entry.Id, "historical_backfill", source.SourceType, source.SourceId, sourceVersion,
                    idempotencyKey, BuildPayloadHash(entry, entryLines, source, postingDate, baseCurrency), nowUtc));

            if (voucher is not null)
                await EnsureVoucherSequenceAsync(run.CompanyId, voucher.Value, nowUtc, cancellationToken);

            if (affected > 0 && !auditedTargets.Contains(entry.Id.ToString("N")))
            {
                await _auditWriter.WriteAsync(new AuditEventWriteRequest(run.CompanyId, AuditActorTypes.System,
                    null, AuditEventActions.AccountingJournalMigrated, AuditTargetTypes.AccountingJournal,
                    entry.Id.ToString("N"), AuditEventOutcomes.Succeeded,
                    "Unambiguous historical journal metadata was backfilled without changing posted amounts or lines.",
                    ["ledger_entry", "ledger_entry_lines", "accounting_policy_pack", "voucher_series"],
                    new Dictionary<string, string?>
                    {
                        ["migrationRunId"] = run.Id.ToString("D"),
                        ["entryNumber"] = entry.EntryNumber,
                        ["sourceType"] = source?.SourceType,
                        ["sourceId"] = source?.SourceId,
                        ["sourceVersion"] = sourceVersion,
                        ["policyPack"] = policyKey is null || policyVersion is null ? null : $"{policyKey}@{policyVersion}"
                    }, run.CorrelationId, nowUtc), cancellationToken);
                auditedTargets.Add(entry.Id.ToString("N"));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var conflictEntryIds = (await _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CompanyId == run.CompanyId && c.MigrationRunId == run.Id && c.EntityType == "ledger_entry")
            .Select(c => c.EntityId).ToArrayAsync(cancellationToken))
            .Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty).Where(x => x != Guid.Empty).ToArray();
        var hasRemaining = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == run.CompanyId && x.Status == LedgerEntryStatuses.Posted &&
                (x.VoucherSeriesId == null || x.VoucherSequenceNumber == null || x.VoucherFiscalYear == null ||
                 x.PostingDate == null || x.BaseCurrency == null || x.PostingType == null ||
                 x.SourceVersion == null || x.IdempotencyKey == null || x.PolicyPackKey == null || x.PolicyPackVersion == null) &&
                !conflictEntryIds.Contains(x.Id),
                cancellationToken);
        if (!hasRemaining)
            await AddMissingEvidenceConflictsAsync(run, cancellationToken);
        var nextPhase = hasRemaining ? AccountingMigrationPhases.Journals : AccountingMigrationPhases.Reports;
        var openConflicts = await CountOpenConflictsAsync(run.CompanyId, run.Id, cancellationToken);
        run.RecordBatch(nextPhase, entries.Length, updated, openConflicts,
            await CountReportsAsync(run.CompanyId, run.Id, cancellationToken), nowUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _telemetry.MigrationBatch(run.CompanyId, run.Id, AccountingMigrationPhases.Journals,
            entries.Length, updated, conflictsCreated);
    }

    private async Task ProcessReportsAsync(AccountingMigrationRun run, int batchSize, CancellationToken cancellationToken)
    {
        var existingPeriodIds = _dbContext.AccountingCutoverReports.IgnoreQueryFilters()
            .Where(x => x.CompanyId == run.CompanyId && x.MigrationRunId == run.Id)
            .Select(x => x.FiscalPeriodId);
        var periods = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && !existingPeriodIds.Contains(x.Id))
            .OrderBy(x => x.StartUtc).Take(Math.Min(batchSize, 24))
            .Select(x => new { x.Id, x.Name, x.StartUtc, x.EndUtc })
            .ToArrayAsync(cancellationToken);
        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;

        foreach (var period in periods)
            _dbContext.AccountingCutoverReports.Add(await BuildReportAsync(run, period.Id, period.Name,
                period.StartUtc, period.EndUtc, nowUtc, cancellationToken));
        await _dbContext.SaveChangesAsync(cancellationToken);

        var remaining = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == run.CompanyId &&
                !_dbContext.AccountingCutoverReports.IgnoreQueryFilters().Any(r =>
                    r.CompanyId == run.CompanyId && r.MigrationRunId == run.Id && r.FiscalPeriodId == x.Id), cancellationToken);
        var conflictCount = await CountOpenConflictsAsync(run.CompanyId, run.Id, cancellationToken);
        var reportCount = await CountReportsAsync(run.CompanyId, run.Id, cancellationToken);
        if (remaining)
        {
            run.RecordBatch(AccountingMigrationPhases.Reports, periods.Length, periods.Length,
                conflictCount, reportCount, nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            run.Complete(conflictCount, reportCount, nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
                AuditEventActions.AccountingMigrationCompleted, run.Id,
                conflictCount == 0 ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
                conflictCount == 0
                    ? "Historical accounting migration completed without unresolved conflicts."
                    : "Historical accounting migration completed with operator-visible conflicts; no ambiguous facts were fabricated.",
                run.CorrelationId, new Dictionary<string, string?>
                {
                    ["targetVersion"] = run.TargetVersion,
                    ["status"] = run.Status,
                    ["scannedCount"] = run.ScannedCount.ToString(CultureInfo.InvariantCulture),
                    ["updatedCount"] = run.UpdatedCount.ToString(CultureInfo.InvariantCulture),
                    ["conflictCount"] = conflictCount.ToString(CultureInfo.InvariantCulture),
                    ["reportCount"] = reportCount.ToString(CultureInfo.InvariantCulture)
                }, cancellationToken);
            _telemetry.MigrationCompleted(run.CompanyId, run.Id, run.Status,
                nowUtc - (run.StartedUtc ?? run.RequestedUtc), conflictCount, run.CorrelationId);
        }
    }

    private async Task<AccountingCutoverReport> BuildReportAsync(
        AccountingMigrationRun run, Guid periodId, string periodName, DateTime startUtc, DateTime endUtc,
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        var lines = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.LedgerEntry.FiscalPeriodId == periodId &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted)
            .Select(x => new CutoverLineRow(x.DebitAmount, x.CreditAmount, x.TaxFactsJson,
                x.FinanceAccount.ControlAccountRole))
            .ToArrayAsync(cancellationToken);
        var openingBalance = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && (x.EffectiveFrom == null || x.EffectiveFrom <= DateOnly.FromDateTime(startUtc)))
            .SumAsync(x => x.OpeningBalance, cancellationToken);
        var entryIds = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.FiscalPeriodId == periodId && x.Status == LedgerEntryStatuses.Posted)
            .Select(x => x.Id).ToArrayAsync(cancellationToken);
        var evidenceCount = await _dbContext.LedgerEntryEvidenceLinks.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == run.CompanyId && entryIds.Contains(x.LedgerEntryId), cancellationToken);
        var trialBalanceSnapshots = await _dbContext.TrialBalanceSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.FiscalPeriodId == periodId)
            .Select(x => new { x.Id, x.FinanceAccountId, x.BalanceAmount })
            .ToArrayAsync(cancellationToken);
        var snapshotCount = trialBalanceSnapshots.Length +
            await _dbContext.FinancialStatementSnapshots.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == run.CompanyId && x.FiscalPeriodId == periodId, cancellationToken);
        var accountOpenings = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId)
            .ToDictionaryAsync(x => x.Id, x => x.OpeningBalance, cancellationToken);
        var cumulativeLedger = await _dbContext.LedgerEntryLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                x.LedgerEntry.EntryUtc < endUtc)
            .GroupBy(x => x.FinanceAccountId)
            .Select(x => new { FinanceAccountId = x.Key, Balance = x.Sum(line => line.DebitAmount - line.CreditAmount) })
            .ToDictionaryAsync(x => x.FinanceAccountId, x => x.Balance, cancellationToken);
        var trialBalanceMismatchCount = trialBalanceSnapshots.Count(snapshot =>
            Math.Abs(accountOpenings.GetValueOrDefault(snapshot.FinanceAccountId) +
                cumulativeLedger.GetValueOrDefault(snapshot.FinanceAccountId) - snapshot.BalanceAmount) > 0.0001m);
        var trialBalanceSnapshotTotal = trialBalanceSnapshots.Sum(x => x.BalanceAmount);
        var calculatedTrialBalanceTotal = trialBalanceSnapshots.Sum(x =>
            accountOpenings.GetValueOrDefault(x.FinanceAccountId) + cumulativeLedger.GetValueOrDefault(x.FinanceAccountId));
        var providerReferences = await _dbContext.FinanceExternalReferences.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == run.CompanyId, cancellationToken) +
            await _dbContext.FortnoxExternalReferences.IgnoreQueryFilters().AsNoTracking()
                .CountAsync(x => x.CompanyId == run.CompanyId, cancellationToken);
        var migrationIssueCount = await _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == run.CompanyId && x.MigrationRunId == run.Id &&
                x.Status == AccountingMigrationConflictStatuses.Open &&
                (x.FiscalPeriodId == null || x.FiscalPeriodId == periodId), cancellationToken);
        var issueCount = migrationIssueCount + trialBalanceMismatchCount;
        var debit = lines.Sum(x => x.DebitAmount);
        var credit = lines.Sum(x => x.CreditAmount);
        static decimal Balance(IEnumerable<CutoverLineRow> rows, string role) => rows
            .Where(x => string.Equals(x.ControlAccountRole, role, StringComparison.OrdinalIgnoreCase))
            .Sum(x => x.DebitAmount - x.CreditAmount);
        var receivables = Balance(lines, AccountingAccountRoleKeys.AccountsReceivable);
        var payables = Balance(lines, AccountingAccountRoleKeys.AccountsPayable);
        var bank = Balance(lines, AccountingAccountRoleKeys.Bank);
        var suspense = Balance(lines, AccountingAccountRoleKeys.Suspense);
        var details = JsonSerializer.Serialize(new
        {
            run.CompanyId,
            migrationRunId = run.Id,
            fiscalPeriodId = periodId,
            periodName,
            startUtc,
            endUtc,
            openingBalance,
            journalDebit = debit,
            journalCredit = credit,
            receivablesBalance = receivables,
            payablesBalance = payables,
            bankBalance = bank,
            suspenseBalance = suspense,
            taxFactLineCount = lines.Count(x => !string.IsNullOrWhiteSpace(x.TaxFactsJson)),
            providerReferenceCount = providerReferences,
            evidenceLinkCount = evidenceCount,
            snapshotCount,
            trialBalanceSnapshotTotal,
            calculatedTrialBalanceTotal,
            trialBalanceMismatchCount,
            issueCount
        });
        return new AccountingCutoverReport(Guid.NewGuid(), run.CompanyId, run.Id, periodId,
            openingBalance, debit, credit, receivables, payables, bank, suspense,
            lines.Count(x => !string.IsNullOrWhiteSpace(x.TaxFactsJson)), providerReferences,
            evidenceCount, snapshotCount, issueCount, details, Sha256(details), nowUtc);
    }

    private async Task AddMissingEvidenceConflictsAsync(AccountingMigrationRun run, CancellationToken cancellationToken)
    {
        var candidates = await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.Status == LedgerEntryStatuses.Posted &&
                x.PostingType == LedgerPostingTypeValues.SourceDocument &&
                !_dbContext.LedgerEntryEvidenceLinks.IgnoreQueryFilters().Any(link =>
                    link.CompanyId == run.CompanyId && link.LedgerEntryId == x.Id))
            .Select(x => new { x.Id, x.FiscalPeriodId, x.EntryNumber, x.SourceType, x.SourceId })
            .ToArrayAsync(cancellationToken);
        foreach (var entry in candidates)
            await AddConflictAsync(run, "ledger_entry", entry.Id.ToString("D"), entry.FiscalPeriodId,
                AccountingMigrationConflictReasonCodes.SourceDocumentEvidenceMissing,
                "A source-document journal has no immutable accounting evidence link.",
                JsonSerializer.Serialize(entry),
                "Restore or attach the verified source document and confirm its content hash before cutover.",
                cancellationToken);
        if (candidates.Length > 0) await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<int> AddJournalConflictAsync(
        AccountingMigrationRun run, HistoricalJournalRow entry, string reasonCode, string explanation,
        object evidence, string operatorAction, CancellationToken cancellationToken) =>
        await AddConflictAsync(run, "ledger_entry", entry.Id.ToString("D"), entry.FiscalPeriodId,
            reasonCode, explanation, JsonSerializer.Serialize(evidence), operatorAction, cancellationToken);

    private async Task<int> AddConflictAsync(
        AccountingMigrationRun run, string entityType, string entityId, Guid? fiscalPeriodId,
        string reasonCode, string explanation, string evidenceJson, string operatorAction,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == run.CompanyId && x.MigrationRunId == run.Id &&
                x.EntityType == entityType && x.EntityId == entityId && x.ReasonCode == reasonCode, cancellationToken);
        if (exists) return 0;
        _dbContext.AccountingMigrationConflicts.Add(new AccountingMigrationConflict(Guid.NewGuid(), run.CompanyId,
            run.Id, run.TargetVersion, entityType, entityId, fiscalPeriodId, reasonCode, explanation,
            evidenceJson, operatorAction, _timeProvider.GetUtcNow().UtcDateTime));
        _telemetry.MigrationConflict(run.CompanyId, run.Id, reasonCode, entityType, entityId, run.CorrelationId);
        return 1;
    }

    private async Task<Guid[]> ConflictEntityIdsAsync(
        Guid companyId,
        Guid runId,
        string entityType,
        CancellationToken cancellationToken) =>
        (await _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.MigrationRunId == runId && x.EntityType == entityType)
            .Select(x => x.EntityId).ToArrayAsync(cancellationToken))
        .Select(x => Guid.TryParse(x, out var parsed) ? parsed : Guid.Empty)
        .Where(x => x != Guid.Empty)
        .ToArray();

    private async Task EnsureVoucherSequenceAsync(Guid companyId, HistoricalVoucher voucher, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var updated = await _dbContext.VoucherSequences.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.VoucherSeriesId == voucher.SeriesId &&
                x.FiscalYear == voucher.FiscalYear && x.LastAllocatedNumber < voucher.SequenceNumber)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.LastAllocatedNumber, voucher.SequenceNumber)
                .SetProperty(x => x.UpdatedUtc, nowUtc), cancellationToken);
        if (updated > 0) return;
        var exists = await _dbContext.VoucherSequences.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.VoucherSeriesId == voucher.SeriesId &&
                x.FiscalYear == voucher.FiscalYear, cancellationToken);
        if (!exists)
            _dbContext.VoucherSequences.Add(new VoucherSequence(Guid.NewGuid(), companyId, voucher.SeriesId,
                voucher.FiscalYear, voucher.SequenceNumber, nowUtc));
    }

    private static HistoricalSource? ResolveSource(string? sourceType, string? sourceId,
        IReadOnlyList<HistoricalMappingRow> mappings)
    {
        var normalizedType = Optional(sourceType);
        var normalizedId = Optional(sourceId);
        if (normalizedType is not null && normalizedId is not null)
        {
            if (mappings.Count == 0 || mappings.All(x =>
                    string.Equals(x.SourceType, normalizedType, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.SourceId, normalizedId, StringComparison.OrdinalIgnoreCase)))
                return new HistoricalSource(normalizedType.ToLowerInvariant(), normalizedId);
            return null;
        }
        if (mappings.Count == 1)
            return new HistoricalSource(mappings[0].SourceType.Trim().ToLowerInvariant(),
                mappings[0].SourceId.Trim());
        return null;
    }

    private static HistoricalVoucher? ResolveVoucher(HistoricalJournalRow entry, IReadOnlyList<VoucherSeriesRow> series)
    {
        if (entry.VoucherSeriesId.HasValue && entry.VoucherFiscalYear.HasValue && entry.VoucherSequenceNumber.HasValue)
            return new(entry.VoucherSeriesId.Value, entry.VoucherFiscalYear.Value, entry.VoucherSequenceNumber.Value);
        var match = VoucherNumberPattern.Match(entry.EntryNumber.Trim());
        if (!match.Success) return null;
        var prefix = match.Groups["prefix"].Value;
        var candidates = series.Where(x => string.Equals(x.NumberPrefix, prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1 ||
            !int.TryParse(match.Groups["year"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !long.TryParse(match.Groups["number"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number <= 0)
            return null;
        return new HistoricalVoucher(candidates[0].Id, year, number);
    }

    private static string? ResolvePostingType(string? sourceType)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) return null;
        var normalized = sourceType.Trim().ToLowerInvariant();
        if (normalized.Contains("bank", StringComparison.Ordinal)) return LedgerPostingTypeValues.Bank;
        if (normalized.Contains("cash", StringComparison.Ordinal) || normalized.Contains("settlement", StringComparison.Ordinal))
            return LedgerPostingTypeValues.CashSettlement;
        if (normalized.Contains("manual", StringComparison.Ordinal)) return LedgerPostingTypeValues.Manual;
        return LedgerPostingTypeValues.SourceDocument;
    }

    private static bool TryMapAccountSemantics(string accountType, out string accountClass, out string normalBalance)
    {
        switch (accountType.Trim().ToLowerInvariant())
        {
            case "asset": accountClass = FinanceAccountClassValues.Asset; normalBalance = FinanceNormalBalanceValues.Debit; return true;
            case "liability": accountClass = FinanceAccountClassValues.Liability; normalBalance = FinanceNormalBalanceValues.Credit; return true;
            case "equity": accountClass = FinanceAccountClassValues.Equity; normalBalance = FinanceNormalBalanceValues.Credit; return true;
            case "income":
            case "revenue": accountClass = FinanceAccountClassValues.Income; normalBalance = FinanceNormalBalanceValues.Credit; return true;
            case "expense": accountClass = FinanceAccountClassValues.Expense; normalBalance = FinanceNormalBalanceValues.Debit; return true;
            default: accountClass = string.Empty; normalBalance = string.Empty; return false;
        }
    }

    private async Task<bool> RequiresMigrationAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceAccounts.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            (x.AccountClass == null || x.NormalBalance == null || !x.IsPostingEnabled), cancellationToken) ||
        await _dbContext.LedgerEntries.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            x.Status == LedgerEntryStatuses.Posted &&
            (x.VoucherSeriesId == null || x.VoucherSequenceNumber == null || x.VoucherFiscalYear == null ||
             x.PostingDate == null || x.BaseCurrency == null || x.PostingType == null || x.SourceVersion == null ||
             x.IdempotencyKey == null || x.PolicyPackKey == null || x.PolicyPackVersion == null), cancellationToken) ||
        await _dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.PostingState == BankTransactionPostingStates.Conflict, cancellationToken) ||
        await _dbContext.AccountingProviderExports.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.Status == AccountingProviderExportStatuses.ReconciliationRequired,
                cancellationToken);

    private async Task<AccountingMigrationRun?> LoadLatestAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Conflicts).Include(x => x.Reports).ThenInclude(x => x.FiscalPeriod)
            .Where(x => x.CompanyId == companyId).OrderByDescending(x => x.RequestedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<AccountingMigrationRun?> LoadAsync(Guid companyId, Guid runId, CancellationToken cancellationToken) =>
        await _dbContext.AccountingMigrationRuns.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.Conflicts).Include(x => x.Reports).ThenInclude(x => x.FiscalPeriod)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == runId, cancellationToken);

    private Task<int> CountOpenConflictsAsync(Guid companyId, Guid runId, CancellationToken cancellationToken) =>
        _dbContext.AccountingMigrationConflicts.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.MigrationRunId == runId &&
                x.Status == AccountingMigrationConflictStatuses.Open, cancellationToken);

    private Task<int> CountReportsAsync(Guid companyId, Guid runId, CancellationToken cancellationToken) =>
        _dbContext.AccountingCutoverReports.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == companyId && x.MigrationRunId == runId, cancellationToken);

    private Task WriteAuditAsync(Guid companyId, Guid actorUserId, string action, Guid targetId, string outcome,
        string rationale, string? correlationId, IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken) =>
        _auditWriter.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.User, actorUserId,
            action, AuditTargetTypes.AccountingMigration, targetId.ToString("D"), outcome, rationale,
            Metadata: metadata, CorrelationId: correlationId, OccurredUtc: _timeProvider.GetUtcNow().UtcDateTime), cancellationToken);

    private static AccountingMigrationRunDto Map(AccountingMigrationRun run) => new(
        run.Id, run.CompanyId, run.TargetVersion, run.Status, run.Phase, run.AttemptCount, run.ScannedCount,
        run.UpdatedCount, run.ConflictCount, run.ReportCount, run.FailureCode, run.FailureSummary,
        run.RequestedUtc, run.StartedUtc, run.CompletedUtc, run.Version,
        run.Conflicts.OrderByDescending(x => x.Status == AccountingMigrationConflictStatuses.Open).ThenBy(x => x.CreatedUtc)
            .Select(x => new AccountingMigrationConflictDto(x.Id, x.EntityType, x.EntityId, x.FiscalPeriodId,
                x.ReasonCode, x.Explanation, x.EvidenceJson, x.OperatorAction, x.Status,
                x.ResolutionSummary, x.Version, x.UpdatedUtc)).ToArray(),
        run.Reports.OrderBy(x => x.FiscalPeriod.StartUtc).Select(x => new AccountingCutoverReportDto(
            x.Id, x.FiscalPeriodId, x.FiscalPeriod.Name, x.OpeningBalance, x.JournalDebit, x.JournalCredit,
            x.ReceivablesBalance, x.PayablesBalance, x.BankBalance, x.SuspenseBalance, x.TaxFactLineCount,
            x.ProviderReferenceCount, x.EvidenceLinkCount, x.SnapshotCount, x.IssueCount, x.Checksum,
            x.GeneratedUtc)).ToArray());

    private static string BuildLegacySourceVersion(HistoricalJournalRow entry, IReadOnlyList<HistoricalLineRow> lines) =>
        $"legacy-{BuildPayloadHash(entry, lines, null, entry.PostingDate ?? DateOnly.FromDateTime(entry.EntryUtc), entry.BaseCurrency)[..16]}";

    private static string BuildPayloadHash(HistoricalJournalRow entry, IReadOnlyList<HistoricalLineRow> lines,
        HistoricalSource? source, DateOnly postingDate, string? currency)
    {
        var value = JsonSerializer.Serialize(new
        {
            entry.Id,
            entry.EntryNumber,
            postingDate,
            currency,
            source,
            lines = lines.OrderBy(x => x.Id).Select(x => new
            {
                x.Id, x.FinanceAccountId, x.DebitAmount, x.CreditAmount, x.Currency, x.TaxFactsJson
            })
        });
        return Sha256(value);
    }

    private static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string Required(string value, string name, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) :
        value.Trim().Length <= maxLength ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Safe(string? value, int maxLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "Accounting migration failed." : value.Trim().Replace('\r', ' ').Replace('\n', ' ');
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }
    private static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
    }

    private sealed record HistoricalJournalRow(Guid Id, Guid FiscalPeriodId, string EntryNumber, DateTime EntryUtc,
        DateTime? PostedAtUtc, string? SourceType, string? SourceId, Guid? VoucherSeriesId,
        long? VoucherSequenceNumber, int? VoucherFiscalYear, DateOnly? PostingDate, string? BaseCurrency,
        string? PostingType, string? SourceVersion, string? IdempotencyKey, string? PolicyPackKey,
        string? PolicyPackVersion, string? Description);
    private sealed record HistoricalLineRow(Guid Id, Guid LedgerEntryId, Guid FinanceAccountId,
        decimal DebitAmount, decimal CreditAmount, string Currency, string? TaxFactsJson);
    private sealed record HistoricalMappingRow(Guid LedgerEntryId, string SourceType, string SourceId);
    private sealed record VoucherSeriesRow(Guid Id, string NumberPrefix);
    private sealed record CutoverLineRow(decimal DebitAmount, decimal CreditAmount, string? TaxFactsJson,
        string? ControlAccountRole);
    private sealed record ReconciliationInventoryRow(Guid Id, Guid BankTransactionId, string? ConflictCode,
        string? ConflictDetails, long SourceVersion);
    private sealed record ProviderInventoryRow(Guid Id, Guid LedgerEntryId, string ProviderKey,
        string SourceType, string SourceId, string SourceVersion, string? FailureCategory,
        string? SafeSummary, int AttemptCount);
    private sealed record HistoricalSource(string SourceType, string SourceId);
    private readonly record struct HistoricalVoucher(Guid SeriesId, int FiscalYear, long SequenceNumber);
}
