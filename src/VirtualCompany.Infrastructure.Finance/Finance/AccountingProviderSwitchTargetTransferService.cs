using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchTargetTransferWorkerOptions
{
    public const string SectionName = "AccountingProviderSwitchTargetTransfer";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 10;
    public int ClaimBatchSize { get; set; } = 4;
    public int LeaseSeconds { get; set; } = 120;
    public int MaximumAttempts { get; set; } = 4;
}

public sealed class AccountingProviderSwitchTargetTransferService :
    IAccountingProviderSwitchTargetTransferService,
    IAccountingProviderSwitchTargetTransferJobRunner,
    IAccountingProviderSwitchTargetTransferExecutionTracker
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IAccountingProviderSwitchStagingService _staging;
    private readonly IAccountingProviderSwitchRehearsalService _rehearsal;
    private readonly IFinanceIntegrationWriteCommandService _writes;
    private readonly IReadOnlyDictionary<string, IAccountingProviderSwitchTargetPreparationAdapter> _adapters;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _time;
    private readonly AccountingProviderSwitchTargetTransferWorkerOptions _options;

    public AccountingProviderSwitchTargetTransferService(VirtualCompanyDbContext db,
        IAccountingProviderSwitchStagingService staging, IAccountingProviderSwitchRehearsalService rehearsal,
        IFinanceIntegrationWriteCommandService writes,
        IEnumerable<IAccountingProviderSwitchTargetPreparationAdapter> adapters, IAuditEventWriter audit,
        TimeProvider time, IOptions<AccountingProviderSwitchTargetTransferWorkerOptions> options)
    {
        _db = db; _staging = staging; _rehearsal = rehearsal; _writes = writes;
        _adapters = adapters.ToDictionary(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase);
        _audit = audit; _time = time; _options = options.Value;
    }

    public async Task<AccountingProviderSwitchTargetTransferBatchDto> StartAsync(
        StartAccountingProviderSwitchTargetTransferCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var idempotencyKey = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 128);
        var sw = await SwitchAsync(command.CompanyId, command.SwitchId, false, cancellationToken);
        EnsureVersion(sw, command.ExpectedSwitchVersion);
        if (sw.TargetKind != AccountingProviderEndpointKinds.External || string.IsNullOrWhiteSpace(sw.TargetProviderKey))
            throw Error(AccountingProviderSwitchTargetTransferReasonCodes.TargetMustBeExternal,
                "Target transfer preparation is available only when the approved plan targets an external accounting provider.");
        if (!_adapters.ContainsKey(sw.TargetProviderKey))
            throw Error(AccountingProviderSwitchTargetTransferReasonCodes.AdapterUnavailable,
                $"No production target-preparation adapter is registered for {sw.TargetProviderKey}.");
        var existingByKey = await Batches(command.CompanyId, command.SwitchId, false)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingByKey is not null) return await ToDtoAsync(existingByKey, cancellationToken);

        var readiness = await _rehearsal.GetPlanReadinessAsync(new(command.CompanyId, command.SwitchId, command.PlanId), cancellationToken);
        if (!readiness.IsReady || readiness.Plan is null)
            throw Conflict(readiness.BlockingReasonCode ?? AccountingProviderSwitchTargetTransferReasonCodes.PlanNotApproved,
                readiness.Explanation);
        var completeness = await _staging.GetCompletenessAsync(new(command.CompanyId, command.SwitchId), cancellationToken);
        if (!completeness.IsComplete)
            throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.StagingIncomplete, completeness.Explanation);
        var records = await TransferRecords(command.CompanyId, command.SwitchId, sw.MigrationStrategy).ToListAsync(cancellationToken);
        var packageHash = PackageHash(sw, readiness.Plan, records);
        var existingPackage = await Batches(command.CompanyId, command.SwitchId, false)
            .SingleOrDefaultAsync(x => x.PlanId == command.PlanId && x.PackageHash == packageHash, cancellationToken);
        if (existingPackage is not null) return await ToDtoAsync(existingPackage, cancellationToken);

        var batch = new AccountingProviderSwitchTargetTransferBatch(Guid.NewGuid(), command.CompanyId,
            command.SwitchId, command.PlanId, readiness.Plan.PlanVersion, readiness.Plan.PlanHash,
            sw.TargetProviderKey, packageHash, command.ActorUserId, idempotencyKey, command.CorrelationId, Now());
        _db.AccountingProviderSwitchTargetTransferBatches.Add(batch);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchTargetTransferRequested, batch.Id, AuditEventOutcomes.Requested,
            "A durable external target transfer package was requested from the approved normalized plan.",
            command.CorrelationId, new() { ["switchId"] = command.SwitchId.ToString("D"),
                ["planId"] = command.PlanId.ToString("D"), ["planHash"] = readiness.Plan.PlanHash,
                ["packageHash"] = packageHash, ["targetProviderKey"] = sw.TargetProviderKey }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToDtoAsync(batch, cancellationToken);
    }

    public async Task<AccountingProviderSwitchTargetTransferBatchDto> ReplayAsync(
        ReplayAccountingProviderSwitchTargetTransferCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var batch = await Batches(command.CompanyId, command.SwitchId, true)
            .SingleOrDefaultAsync(x => x.Id == command.BatchId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchTargetTransferReasonCodes.BatchNotFound,
                "The target transfer batch was not found for this company.");
        if (batch.Version != command.ExpectedBatchVersion)
            throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.ConcurrencyConflict,
                "The target transfer batch changed while replay was requested.");
        try { batch.QueueReplay(command.CorrelationId, Now()); }
        catch (InvalidOperationException exception) { throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.BatchNotReplayable, exception.Message); }
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchTargetTransferReplayed, batch.Id, AuditEventOutcomes.Requested,
            "The failed target transfer package was queued for a safe rebuild.", command.CorrelationId,
            new() { ["switchId"] = command.SwitchId.ToString("D"), ["batchId"] = batch.Id.ToString("D") }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToDtoAsync(batch, cancellationToken);
    }

    public async Task<AccountingProviderSwitchTargetTransferBatchDto> GetAsync(
        GetAccountingProviderSwitchTargetTransferQuery query, CancellationToken cancellationToken)
    {
        var batches = Batches(query.CompanyId, query.SwitchId, false);
        var batch = query.BatchId.HasValue
            ? await batches.SingleOrDefaultAsync(x => x.Id == query.BatchId, cancellationToken)
            : await batches.OrderByDescending(x => x.RequestedUtc).FirstOrDefaultAsync(cancellationToken);
        return await ToDtoAsync(batch ?? throw Error(AccountingProviderSwitchTargetTransferReasonCodes.BatchNotFound,
            "The target transfer batch was not found for this company."), cancellationToken);
    }

    public async Task<AccountingProviderSwitchTargetTransferItemDto> ReconcileAsync(
        ReconcileAccountingProviderSwitchTargetTransferItemCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var item = await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                x.BatchId == command.BatchId && x.Id == command.ItemId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchTargetTransferReasonCodes.BatchNotFound,
                "The target transfer item was not found for this company.");
        if (item.Version != command.ExpectedItemVersion)
            throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.ConcurrencyConflict,
                "The target transfer item changed while it was being reconciled.");
        item.Reconcile(command.ProviderConfirmedSuccess, command.ProviderExternalId,
            Required(command.Summary, nameof(command.Summary), 1000), Now());
        await SaveAsync(cancellationToken);
        var batch = await Batches(command.CompanyId, command.SwitchId, true).SingleAsync(x => x.Id == command.BatchId, cancellationToken);
        await RefreshBatchStatusAsync(batch, cancellationToken);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchTargetTransferReconciled, item.Id, AuditEventOutcomes.Succeeded,
            command.ProviderConfirmedSuccess ? "The provider confirmed that the preparatory target write succeeded."
                : "The provider confirmed that the preparatory target write was not applied.", command.CorrelationId,
            new() { ["switchId"] = command.SwitchId.ToString("D"), ["batchId"] = command.BatchId.ToString("D"),
                ["providerConfirmedSuccess"] = command.ProviderConfirmedSuccess.ToString(),
                ["providerExternalId"] = command.ProviderExternalId }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await ItemDtoAsync(item, cancellationToken);
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = Now();
        var due = await _db.AccountingProviderSwitchTargetTransferBatches.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == AccountingProviderSwitchTargetTransferBatchStatuses.Queued && x.NextAttemptUtc <= now) ||
                        (x.Status == AccountingProviderSwitchTargetTransferBatchStatuses.Building && x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.RequestedUtc).Select(x => x.Id).Take(_options.ClaimBatchSize).ToListAsync(cancellationToken);
        var handled = 0;
        foreach (var id in due)
        {
            var owner = $"target-transfer:{Environment.MachineName}:{Guid.NewGuid():N}";
            var claimed = await _db.AccountingProviderSwitchTargetTransferBatches.IgnoreQueryFilters()
                .Where(x => x.Id == id && ((x.Status == AccountingProviderSwitchTargetTransferBatchStatuses.Queued && x.NextAttemptUtc <= now) ||
                    (x.Status == AccountingProviderSwitchTargetTransferBatchStatuses.Building && x.LeaseExpiresUtc <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AccountingProviderSwitchTargetTransferBatchStatuses.Building)
                    .SetProperty(x => x.LeaseOwner, owner).SetProperty(x => x.LeaseExpiresUtc, now.AddSeconds(_options.LeaseSeconds))
                    .SetProperty(x => x.StartedUtc, x => x.StartedUtc ?? now).SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.NextAttemptUtc, (DateTime?)null).SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            if (claimed == 0) continue;
            _db.ChangeTracker.Clear();
            var batch = await _db.AccountingProviderSwitchTargetTransferBatches.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == id && x.LeaseOwner == owner, cancellationToken);
            try { await BuildAsync(batch, cancellationToken); handled++; }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var summary = Safe(exception.Message);
                if (batch.AttemptCount < _options.MaximumAttempts && exception is DbUpdateException or HttpRequestException or TimeoutException)
                    batch.Retry("target_transfer_retryable_failure", summary, Now().AddSeconds(Math.Min(300, 10 * batch.AttemptCount)));
                else batch.Fail(AccountingProviderSwitchTargetTransferReasonCodes.Failed, summary, Now());
                await WriteAuditAsync(batch.CompanyId, batch.RequestedByUserId,
                    AuditEventActions.AccountingProviderSwitchTargetTransferFailed, batch.Id, AuditEventOutcomes.Failed,
                    summary, batch.CorrelationId, new() { ["switchId"] = batch.SwitchId.ToString("D"),
                        ["attempt"] = batch.AttemptCount.ToString() }, cancellationToken);
                await SaveAsync(cancellationToken);
            }
        }
        return handled;
    }

    private async Task BuildAsync(AccountingProviderSwitchTargetTransferBatch batch, CancellationToken cancellationToken)
    {
        var sw = await SwitchAsync(batch.CompanyId, batch.SwitchId, false, cancellationToken);
        var readiness = await _rehearsal.GetPlanReadinessAsync(new(batch.CompanyId, batch.SwitchId, batch.PlanId), cancellationToken);
        if (!readiness.IsReady || readiness.Plan is null || readiness.Plan.PlanHash != batch.PlanHash || readiness.Plan.PlanVersion != batch.PlanVersion)
            throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.PlanStale,
                "The approved cutover plan changed before target preparation. Generate and approve a current plan.");
        var completeness = await _staging.GetCompletenessAsync(new(batch.CompanyId, batch.SwitchId), cancellationToken);
        if (!completeness.IsComplete) throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.StagingIncomplete, completeness.Explanation);
        var records = await TransferRecords(batch.CompanyId, batch.SwitchId, sw.MigrationStrategy).ToListAsync(cancellationToken);
        if (PackageHash(sw, readiness.Plan, records) != batch.PackageHash)
            throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.PlanStale,
                "Normalized staging or mapping versions changed after this target package was requested.");
        if (!_adapters.TryGetValue(batch.TargetProviderKey, out var adapter))
            throw Error(AccountingProviderSwitchTargetTransferReasonCodes.AdapterUnavailable,
                "The production target-preparation adapter is not available.");
        var connection = await _db.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == batch.CompanyId && x.ProviderKey == batch.TargetProviderKey && x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw Error(AccountingProviderSwitchTargetTransferReasonCodes.ConnectionMissing,
                $"Connect {batch.TargetProviderKey} before preparing target records.");
        var scopes = connection.Scopes.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var mapped = records.Select(record =>
        {
            var identity = StableIdentity(batch, record, "prepare_target");
            var dto = new AccountingProviderSwitchTargetRecord(record.Id, record.Dataset, record.SourceIdentity,
                record.SourceVersion, record.SourceHash, record.NormalizedHash, record.NormalizedDataJson,
                record.EvidenceJson, record.FinancialAmount, record.Currency, record.Disposition, record.MappingVersion);
            var operation = adapter.Map(new(batch.CompanyId, batch.SwitchId, batch.PlanId, batch.PlanVersion,
                batch.PlanHash, batch.TargetProviderKey, dto, identity, batch.CorrelationId));
            if (!operation.IsSupported)
                throw Error(AccountingProviderSwitchTargetTransferReasonCodes.CapabilityUnsupported, operation.Explanation);
            var missing = operation.RequiredScopes.Where(scope => !scopes.Contains(scope)).ToArray();
            if (missing.Length > 0) throw Error(AccountingProviderSwitchTargetTransferReasonCodes.ScopeMissing,
                $"Grant the {string.Join(", ", missing)} scope(s) to prepare the '{record.Dataset}' dataset.");
            return (Record: record, Identity: identity, Operation: operation);
        }).ToArray();

        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var previous = await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters()
            .Where(x => x.CompanyId == batch.CompanyId && x.BatchId == batch.Id).ToListAsync(cancellationToken);
        if (previous.Count > 0) _db.AccountingProviderSwitchTargetTransferItems.RemoveRange(previous);
        await _db.SaveChangesAsync(cancellationToken);
        foreach (var entry in mapped)
        {
            await ValidateMappingBindingAsync(batch.CompanyId, entry.Record, cancellationToken);
            var command = entry.Operation.ProviderCommand;
            var payloadHash = command?.PayloadHash ?? Hash($"preview|{entry.Record.NormalizedHash}|{entry.Operation.Action}");
            var summary = command?.PayloadSummary ?? entry.Operation.Explanation;
            var item = new AccountingProviderSwitchTargetTransferItem(DeterministicGuid($"item|{entry.Identity}"),
                batch.CompanyId, batch.SwitchId, batch.Id, entry.Record.Id, entry.Record.Dataset,
                entry.Record.SourceIdentity, entry.Record.SourceVersion, entry.Record.SourceHash,
                entry.Record.NormalizedHash, entry.Record.MappingVersion, entry.Operation.OperationMode,
                entry.Operation.Action, entry.Identity, payloadHash, summary, command?.CommandType,
                command?.HttpMethod, command?.Path, command?.SanitizedPayloadJson,
                command?.ProviderPayloadType, Now());
            _db.AccountingProviderSwitchTargetTransferItems.Add(item);
            await _db.SaveChangesAsync(cancellationToken);
            if (entry.Operation.OperationMode == AccountingProviderSwitchTargetOperationModes.PreviewOnly)
                item.MarkPreviewValidated(entry.Operation.Explanation, Now());
            else if (entry.Operation.OperationMode == AccountingProviderSwitchTargetOperationModes.FinalAuthoritative)
                item.HoldForCutover("This authoritative target operation is validated and held for Prompt 7 cutover execution.", Now());
            else
            {
                if (command is null) throw new InvalidOperationException("A preparatory provider write requires a provider command.");
                var writeRequestId = DeterministicGuid($"write|{entry.Identity}");
                var approval = await _writes.RequestApprovalAsync(new(command.ProviderKey, batch.CompanyId,
                    connection.Id, batch.RequestedByUserId, command.CommandType, command.HttpMethod, command.Path,
                    command.TargetLabel, command.PayloadSummary, command.PayloadHash,
                    new(command.SanitizedPayloadJson, command.ProviderPayloadType), writeRequestId,
                    batch.CorrelationId), cancellationToken);
                item.AttachApproval(writeRequestId, approval.ApprovalId ?? throw new InvalidOperationException(
                    "The preparatory provider write did not create an approval request."), Now());
            }
        }
        batch.CompleteBuild(mapped.Length,
            mapped.Count(x => x.Operation.OperationMode == AccountingProviderSwitchTargetOperationModes.PreviewOnly),
            mapped.Count(x => x.Operation.OperationMode == AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting),
            mapped.Count(x => x.Operation.OperationMode == AccountingProviderSwitchTargetOperationModes.FinalAuthoritative), Now());
        await WriteAuditAsync(batch.CompanyId, batch.RequestedByUserId,
            AuditEventActions.AccountingProviderSwitchTargetTransferPrepared, batch.Id, AuditEventOutcomes.Succeeded,
            "The versioned target transfer package was built; preparatory writes await separate approval and authoritative writes remain held.",
            batch.CorrelationId, new() { ["switchId"] = batch.SwitchId.ToString("D"), ["planHash"] = batch.PlanHash,
                ["packageHash"] = batch.PackageHash, ["itemCount"] = mapped.Length.ToString(),
                ["preparatoryItemCount"] = batch.PreparatoryItemCount.ToString(), ["finalItemCount"] = batch.FinalItemCount.ToString() }, cancellationToken);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task EnsureExecutionAllowedAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken)
    {
        var item = await ItemByWriteRequestAsync(companyId, writeRequestId, false, cancellationToken);
        if (item is null) return;
        if (item.OperationMode == AccountingProviderSwitchTargetOperationModes.FinalAuthoritative)
        {
            var activeCutover = await _db.AccountingProviderSwitchCutoverExecutions.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SwitchId == item.SwitchId &&
                    x.TargetTransferBatchId == item.BatchId && x.Status == AccountingProviderSwitchCutoverStatuses.Transferring,
                    cancellationToken);
            if (activeCutover is null || !activeCutover.FinalSnapshotId.HasValue)
                throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.ApprovalStale,
                    "Final provider operations may execute only inside the frozen, durable cutover coordinator.");
        }
        else if (item.OperationMode != AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting)
            throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.ApprovalStale,
                "Only approved target preparation or final cutover operations may execute.");
        var batch = await _db.AccountingProviderSwitchTargetTransferBatches.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == item.BatchId, cancellationToken);
        if (item.OperationMode == AccountingProviderSwitchTargetOperationModes.PreparatoryNonPosting)
        {
            var readiness = await _rehearsal.GetPlanReadinessAsync(new(companyId, item.SwitchId, batch.PlanId), cancellationToken);
            if (!readiness.IsReady || readiness.Plan?.PlanHash != batch.PlanHash || readiness.Plan.PlanVersion != batch.PlanVersion)
                throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.ApprovalStale,
                    "The plan, mapping, or approval binding changed after this preparatory write was approved.");
        }
        var staged = await _db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SwitchId == item.SwitchId && x.Id == item.StagedRecordId && x.IsCurrent, cancellationToken);
        if (staged is null || staged.SourceVersion != item.SourceVersion || staged.SourceHash != item.SourceHash ||
            staged.NormalizedHash != item.NormalizedHash || staged.MappingVersion != item.MappingVersion)
            throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.MappingStale,
                "The normalized source record or its approved mapping changed after approval.");
        var write = await _db.FinanceIntegrationWriteCommands.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken);
        if (write.PayloadHash != item.PayloadHash || write.ApprovalId != item.ApprovalRequestId)
            throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.ApprovalStale,
                "The approval does not match the immutable target item payload.");
    }

    public async Task MarkExecutionStartedAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken)
    {
        var item = await ItemByWriteRequestAsync(companyId, writeRequestId, true, cancellationToken); if (item is null) return;
        item.StartAttempt(Now());
        var attemptNo = await _db.AccountingProviderSwitchTargetTransferAttempts.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId && x.ItemId == item.Id, cancellationToken) + 1;
        _db.AccountingProviderSwitchTargetTransferAttempts.Add(new(companyId, item.SwitchId, item.BatchId,
            item.Id, attemptNo, "started", null, null, false, Now(), null));
        await SaveAsync(cancellationToken);
    }

    public async Task MarkExecutionSucceededAsync(Guid companyId, Guid writeRequestId, string? providerExternalId,
        string summary, CancellationToken cancellationToken)
    {
        var item = await ItemByWriteRequestAsync(companyId, writeRequestId, true, cancellationToken); if (item is null) return;
        item.Succeed(providerExternalId, Safe(summary), Now());
        await CompleteAttemptAsync(item, "succeeded", null, summary, true, cancellationToken);
        if (!await _db.AccountingProviderSwitchTargetAcknowledgements.IgnoreQueryFilters().AnyAsync(
            x => x.CompanyId == companyId && x.ItemId == item.Id, cancellationToken))
            _db.AccountingProviderSwitchTargetAcknowledgements.Add(new(companyId, item.SwitchId, item.BatchId,
                item.Id, "fortnox", providerExternalId, Hash($"{writeRequestId:D}|{providerExternalId}|{summary}"),
                Safe(summary), Now()));
        await SaveAsync(cancellationToken);
        var batch = await Batches(companyId, item.SwitchId, true).SingleAsync(x => x.Id == item.BatchId, cancellationToken);
        await RefreshBatchStatusAsync(batch, cancellationToken);
        await SaveAsync(cancellationToken);
    }

    public async Task MarkExecutionFailedAsync(Guid companyId, Guid writeRequestId, Exception exception,
        bool providerAcceptedRequest, CancellationToken cancellationToken)
    {
        var item = await ItemByWriteRequestAsync(companyId, writeRequestId, true, cancellationToken); if (item is null) return;
        var failure = AccountingProviderExportService.Classify(exception, providerAcceptedRequest);
        item.Fail(failure.Category, failure.Summary, failure.Ambiguous, Now());
        await CompleteAttemptAsync(item, failure.Ambiguous ? "ambiguous" : "failed", failure.Category,
            failure.Summary, providerAcceptedRequest, cancellationToken);
        await SaveAsync(cancellationToken);
        var batch = await Batches(companyId, item.SwitchId, true).SingleAsync(x => x.Id == item.BatchId, cancellationToken);
        if (failure.Ambiguous) batch.RequireReconciliation(AccountingProviderSwitchTargetTransferReasonCodes.ReconciliationRequired, failure.Summary, Now());
        await SaveAsync(cancellationToken);
    }

    private async Task CompleteAttemptAsync(AccountingProviderSwitchTargetTransferItem item, string outcome,
        string? category, string summary, bool accepted, CancellationToken cancellationToken)
    {
        var attempt = await _db.AccountingProviderSwitchTargetTransferAttempts.IgnoreQueryFilters()
            .Where(x => x.CompanyId == item.CompanyId && x.ItemId == item.Id && !x.CompletedUtc.HasValue)
            .OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(cancellationToken);
        if (attempt is null)
        {
            var number = await _db.AccountingProviderSwitchTargetTransferAttempts.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == item.CompanyId && x.ItemId == item.Id, cancellationToken) + 1;
            attempt = new(item.CompanyId, item.SwitchId, item.BatchId, item.Id, number, outcome, category,
                Safe(summary), accepted, Now(), Now()); _db.AccountingProviderSwitchTargetTransferAttempts.Add(attempt);
        }
        else attempt.Complete(outcome, category, Safe(summary), accepted, Now());
    }

    private async Task RefreshBatchStatusAsync(AccountingProviderSwitchTargetTransferBatch batch, CancellationToken cancellationToken)
    {
        var statuses = await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == batch.CompanyId && x.BatchId == batch.Id).Select(x => x.Status).ToListAsync(cancellationToken);
        if (statuses.Contains(AccountingProviderSwitchTargetTransferItemStatuses.ReconciliationRequired))
            batch.RequireReconciliation(AccountingProviderSwitchTargetTransferReasonCodes.ReconciliationRequired,
                "One or more provider outcomes require operator reconciliation.", Now());
        else if (statuses.All(x => x is AccountingProviderSwitchTargetTransferItemStatuses.PreviewValidated or
            AccountingProviderSwitchTargetTransferItemStatuses.Succeeded or AccountingProviderSwitchTargetTransferItemStatuses.HeldForCutover))
        { batch.Status = AccountingProviderSwitchTargetTransferBatchStatuses.ReadyForCutover; batch.Version++; }
    }

    private async Task ValidateMappingBindingAsync(Guid companyId, AccountingProviderSwitchStagedRecord record,
        CancellationToken cancellationToken)
    {
        if (record.Disposition == AccountingProviderSwitchDispositions.Mapped)
        {
            if (!record.MappingDecisionId.HasValue || !record.MappingVersion.HasValue ||
                !await _db.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.CompanyId == companyId && x.Id == record.MappingDecisionId &&
                    x.MappingVersion == record.MappingVersion && x.Status == AccountingProviderSwitchMappingStatuses.Approved,
                    cancellationToken))
                throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.MappingStale,
                    "A mapped source record is not bound to its current approved mapping version.");
        }
        if (record.Disposition is AccountingProviderSwitchDispositions.Transformed or AccountingProviderSwitchDispositions.OpeningBalanceRepresentation)
        {
            if (!record.ApprovalRequestId.HasValue || !await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == record.ApprovalRequestId && x.Status == ApprovalRequestStatus.Approved, cancellationToken))
                throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.ApprovalStale,
                    "A material source disposition no longer has a current approval.");
        }
    }

    private IQueryable<AccountingProviderSwitchStagedRecord> TransferRecords(Guid companyId, Guid switchId, string strategy) =>
        _db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SwitchId == switchId && x.IsCurrent &&
                x.Disposition != AccountingProviderSwitchDispositions.Duplicate &&
                x.Disposition != AccountingProviderSwitchDispositions.ExcludedWithApproval &&
                x.Disposition != AccountingProviderSwitchDispositions.Missing &&
                x.Disposition != AccountingProviderSwitchDispositions.Unsupported &&
                x.Disposition != AccountingProviderSwitchDispositions.Conflicting &&
                x.Disposition != AccountingProviderSwitchDispositions.AwaitingEvidence &&
                x.Disposition != AccountingProviderSwitchDispositions.Blocked &&
                (strategy != AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems ||
                 (x.Dataset != AccountingProviderSwitchStagingDatasets.Journals && x.Dataset != AccountingProviderSwitchStagingDatasets.JournalLines)))
            .OrderBy(x => x.Dataset).ThenBy(x => x.SourceIdentity);

    private static string PackageHash(AccountingProviderSwitch sw, AccountingProviderSwitchCutoverPlanDto plan,
        IReadOnlyList<AccountingProviderSwitchStagedRecord> records) => Hash($"{sw.CompanyId:D}|{sw.Id:D}|{plan.PlanVersion}|{plan.PlanHash}|{sw.TargetProviderKey}|{sw.MigrationStrategy}|" +
            string.Join('|', records.Select(x => $"{x.Dataset}:{x.SourceIdentity}:{x.SourceVersion}:{x.SourceHash}:{x.NormalizedHash}:{x.Disposition}:{x.MappingVersion}")));
    private static string StableIdentity(AccountingProviderSwitchTargetTransferBatch batch,
        AccountingProviderSwitchStagedRecord record, string action) => Hash($"{batch.CompanyId:D}|{batch.SwitchId:D}|{batch.PlanVersion}|{batch.TargetProviderKey}|{record.Dataset}|{record.SourceIdentity}|{record.SourceVersion}|{action}");

    private async Task<AccountingProviderSwitchTargetTransferBatchDto> ToDtoAsync(
        AccountingProviderSwitchTargetTransferBatch batch, CancellationToken cancellationToken)
    {
        var items = await _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == batch.CompanyId && x.SwitchId == batch.SwitchId && x.BatchId == batch.Id)
            .OrderBy(x => x.Dataset).ThenBy(x => x.SourceIdentity).ToListAsync(cancellationToken);
        var dtos = new List<AccountingProviderSwitchTargetTransferItemDto>(items.Count);
        foreach (var item in items) dtos.Add(await ItemDtoAsync(item, cancellationToken));
        var completed = items.Count(x => x.Status is AccountingProviderSwitchTargetTransferItemStatuses.PreviewValidated or
            AccountingProviderSwitchTargetTransferItemStatuses.Succeeded or AccountingProviderSwitchTargetTransferItemStatuses.HeldForCutover);
        var failed = items.Count(x => x.Status == AccountingProviderSwitchTargetTransferItemStatuses.Failed);
        var reconciliation = items.Count(x => x.ReconciliationNeeded);
        var ready = batch.Status == AccountingProviderSwitchTargetTransferBatchStatuses.ReadyForCutover && failed == 0 && reconciliation == 0;
        return new(batch.Id, batch.CompanyId, batch.SwitchId, batch.PlanId, batch.PlanVersion, batch.PlanHash,
            batch.TargetProviderKey, batch.PackageHash, batch.Status, batch.TotalItemCount, batch.PreviewItemCount,
            batch.PreparatoryItemCount, batch.FinalItemCount, completed, failed, reconciliation, batch.FailureCode,
            batch.FailureSummary, batch.RequestedUtc, batch.CompletedUtc, batch.Version, ready,
            ready ? "All preview and preparatory operations completed or are safely held for cutover; accounting authority is unchanged."
                : reconciliation > 0 ? "A provider outcome must be reconciled before cutover."
                : failed > 0 ? "A target operation failed and requires an operator action."
                : batch.Status == AccountingProviderSwitchTargetTransferBatchStatuses.AwaitingApproval
                    ? "Preparatory non-posting provider writes are waiting for their separate approvals."
                    : "The target transfer package is still being prepared.", dtos);
    }

    private async Task<AccountingProviderSwitchTargetTransferItemDto> ItemDtoAsync(
        AccountingProviderSwitchTargetTransferItem item, CancellationToken cancellationToken)
    {
        var attempts = await _db.AccountingProviderSwitchTargetTransferAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == item.CompanyId && x.ItemId == item.Id).OrderBy(x => x.AttemptNumber)
            .Select(x => new AccountingProviderSwitchTargetTransferAttemptDto(x.Id, x.AttemptNumber, x.Outcome,
                x.FailureCategory, x.SafeSummary, x.ProviderAcceptedRequest, x.StartedUtc, x.CompletedUtc))
            .ToListAsync(cancellationToken);
        return new(item.Id, item.StagedRecordId, item.Dataset, item.SourceIdentity, item.SourceVersion,
            item.MappingVersion, item.OperationMode, item.Action, item.StableIdentity, item.Status,
            item.WriteRequestId, item.ApprovalRequestId, item.ProviderExternalId, item.FailureCategory,
            item.SafeSummary, item.ReconciliationNeeded, item.Version, attempts);
    }

    private Task<AccountingProviderSwitchTargetTransferItem?> ItemByWriteRequestAsync(Guid companyId,
        Guid writeRequestId, bool tracking, CancellationToken cancellationToken)
    {
        var query = _db.AccountingProviderSwitchTargetTransferItems.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.WriteRequestId == writeRequestId);
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken);
    }
    private IQueryable<AccountingProviderSwitchTargetTransferBatch> Batches(Guid companyId, Guid switchId, bool tracking)
    {
        var query = _db.AccountingProviderSwitchTargetTransferBatches.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.SwitchId == switchId);
        return tracking ? query : query.AsNoTracking();
    }
    private Task<AccountingProviderSwitch> SwitchAsync(Guid companyId, Guid switchId, bool tracking, CancellationToken cancellationToken)
    {
        var query = _db.AccountingProviderSwitches.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Id == switchId);
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken).ContinueWith(task =>
            task.Result ?? throw Error(AccountingProviderSwitchReasonCodes.NotFound,
                "The accounting-system switch was not found for this company."), cancellationToken,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }
    private Task WriteAuditAsync(Guid companyId, Guid actor, string action, Guid target, string outcome,
        string summary, string correlation, Dictionary<string, string?> evidence, CancellationToken cancellationToken) =>
        _audit.WriteAsync(new(companyId, AuditActorTypes.User, actor, action, "accounting_provider_switch_target_transfer",
            target.ToString("D"), outcome, summary, ["accounting_provider_switch", "target_transfer", "non_authoritative"],
            evidence, correlation, Now()), cancellationToken);
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Conflict(AccountingProviderSwitchTargetTransferReasonCodes.ConcurrencyConflict,
            "Target transfer data changed concurrently. Reload and retry."); }
    }
    private static void EnsureVersion(AccountingProviderSwitch sw, long expected)
    {
        if (sw.Version != expected) throw Conflict(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
            "The accounting-system switch changed while target preparation was requested.");
    }
    private static void Validate(Guid companyId, Guid switchId, Guid actor, string correlation)
    {
        if (companyId == Guid.Empty || switchId == Guid.Empty || actor == Guid.Empty) throw new ArgumentException("Company, switch, and actor are required.");
        Required(correlation, nameof(correlation), 128);
    }
    private static string Required(string? value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static string Safe(string? value) => string.IsNullOrWhiteSpace(value) ? "Target transfer failed safely."
        : value.Trim().Length <= 1000 ? value.Trim() : value.Trim()[..1000];
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static Guid DeterministicGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);
    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
    private static AccountingAuthorityException Error(string code, string message) => new(code, message);
    private static AccountingAuthorityException Conflict(string code, string message) => new(code, message, true);
}
