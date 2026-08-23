using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchRehearsalService : IAccountingProviderSwitchRehearsalService,
    IAccountingProviderSwitchRehearsalJobRunner
{
    private const string CalculationVersion = "1.0";
    private const decimal DefaultTolerance = 0.01m;
    private readonly VirtualCompanyDbContext _db;
    private readonly IAccountingProviderSwitchStagingService _staging;
    private readonly IApprovalRequestService _approvals;
    private readonly IReadOnlyList<IAccountingProviderSwitchRehearsalAdapter> _adapters;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _time;
    private readonly AccountingProviderSwitchRehearsalWorkerOptions _options;

    public AccountingProviderSwitchRehearsalService(VirtualCompanyDbContext db,
        IAccountingProviderSwitchStagingService staging, IApprovalRequestService approvals,
        IEnumerable<IAccountingProviderSwitchRehearsalAdapter> adapters, IAuditEventWriter audit,
        TimeProvider time, IOptions<AccountingProviderSwitchRehearsalWorkerOptions> options)
    {
        _db = db; _staging = staging; _approvals = approvals; _adapters = adapters.ToArray();
        _audit = audit; _time = time; _options = options.Value;
    }

    public async Task<AccountingProviderSwitchRehearsalDto> StartAsync(
        StartAccountingProviderSwitchRehearsalCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var sw = await SwitchAsync(command.CompanyId, command.SwitchId, cancellationToken);
        EnsureVersion(sw, command.ExpectedSwitchVersion);
        EnsureRehearsalState(sw);
        var completeness = await _staging.GetCompletenessAsync(
            new GetAccountingProviderSwitchCompletenessQuery(command.CompanyId, command.SwitchId), cancellationToken);
        if (!completeness.IsComplete)
            throw Conflict(AccountingProviderSwitchRehearsalReasonCodes.NotReady, completeness.Explanation);
        if (await HasBlockingGapsAsync(command.CompanyId, command.SwitchId, cancellationToken))
            throw Conflict(AccountingProviderSwitchRehearsalReasonCodes.BlockingGap,
                "The latest assessment still contains blocking migration gaps.");

        var key = Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 128);
        var existing = await _db.AccountingProviderSwitchRehearsals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                                       x.IdempotencyKey == key, cancellationToken);
        if (existing is not null) return await ToDtoAsync(existing, cancellationToken);

        var run = new AccountingProviderSwitchRehearsal(Guid.NewGuid(), command.CompanyId, command.SwitchId,
            command.ActorUserId, key, command.CorrelationId, Now());
        _db.AccountingProviderSwitchRehearsals.Add(run);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchRehearsalRequested, run.Id, AuditEventOutcomes.Requested,
            "A non-authoritative accounting migration rehearsal was queued.", command.CorrelationId,
            new() { ["switchId"] = command.SwitchId.ToString("D"), ["idempotencyKey"] = key }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToDtoAsync(run, cancellationToken);
    }

    public async Task<AccountingProviderSwitchRehearsalDto> ReplayAsync(
        ReplayAccountingProviderSwitchRehearsalCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var original = await Runs(command.CompanyId, command.SwitchId, false)
            .SingleOrDefaultAsync(x => x.Id == command.RehearsalId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchRehearsalReasonCodes.NotFound, "The rehearsal was not found for this company.");
        _ = original;
        return await StartAsync(new StartAccountingProviderSwitchRehearsalCommand(command.CompanyId,
            command.SwitchId, command.ExpectedSwitchVersion, command.ActorUserId, command.CorrelationId,
            command.IdempotencyKey), cancellationToken);
    }

    public async Task<AccountingProviderSwitchRehearsalDto> GetAsync(
        GetAccountingProviderSwitchRehearsalQuery query, CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty || query.SwitchId == Guid.Empty) throw new ArgumentException("Company and switch are required.");
        await SwitchAsync(query.CompanyId, query.SwitchId, cancellationToken);
        var runs = Runs(query.CompanyId, query.SwitchId, false);
        var run = query.RehearsalId.HasValue
            ? await runs.SingleOrDefaultAsync(x => x.Id == query.RehearsalId.Value, cancellationToken)
            : await runs.OrderByDescending(x => x.RequestedUtc).FirstOrDefaultAsync(cancellationToken);
        return run is null
            ? throw Error(AccountingProviderSwitchRehearsalReasonCodes.NotFound, "The rehearsal was not found for this company.")
            : await ToDtoAsync(run, cancellationToken);
    }

    public async Task<AccountingProviderSwitchManualEvidenceDto> RecordManualEvidenceAsync(
        RecordAccountingProviderSwitchManualEvidenceCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var run = await Runs(command.CompanyId, command.SwitchId, false)
            .SingleOrDefaultAsync(x => x.Id == command.RehearsalId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchRehearsalReasonCodes.NotFound, "The rehearsal was not found for this company.");
        if (run.Status != AccountingProviderSwitchRehearsalStatuses.Completed)
            throw Conflict(AccountingProviderSwitchRehearsalReasonCodes.InvalidEvidence, "Evidence can be recorded only for a completed rehearsal.");
        var check = await _db.AccountingProviderSwitchReconciliationChecks.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId &&
                                       x.RehearsalId == command.RehearsalId && x.Id == command.CheckId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchRehearsalReasonCodes.InvalidEvidence, "The reconciliation check was not found.");
        if (!check.ManualEvidenceAllowed || check.Result != AccountingProviderSwitchReconciliationResults.ManualEvidenceRequired)
            throw Conflict(AccountingProviderSwitchRehearsalReasonCodes.InvalidEvidence,
                "Calculated reconciliation failures cannot be overridden with manual evidence.");
        var input = await InputAsync(command.CompanyId, command.SwitchId, command.RehearsalId, cancellationToken);
        var evidence = new AccountingProviderSwitchManualEvidence(command.CompanyId, command.SwitchId,
            command.RehearsalId, command.CheckId, input.SourceSnapshotHash, command.Explanation,
            command.EvidenceReference, command.ActorUserId, Now(), command.ExpiresUtc);
        _db.AccountingProviderSwitchManualEvidence.Add(evidence);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchManualEvidenceRecorded, evidence.Id, AuditEventOutcomes.Succeeded,
            "Authorized manual evidence was attached to a non-calculable reconciliation requirement.",
            command.CorrelationId, new() { ["switchId"] = command.SwitchId.ToString("D"),
                ["rehearsalId"] = command.RehearsalId.ToString("D"), ["checkKey"] = check.CheckKey,
                ["evidenceReference"] = command.EvidenceReference, ["expiresUtc"] = command.ExpiresUtc?.ToString("O") }, cancellationToken);
        await SaveAsync(cancellationToken);
        return ToDto(evidence);
    }

    public async Task<AccountingProviderSwitchCutoverPlanDto> GeneratePlanAsync(
        GenerateAccountingProviderSwitchCutoverPlanCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var sw = await SwitchAsync(command.CompanyId, command.SwitchId, cancellationToken);
        EnsureVersion(sw, command.ExpectedSwitchVersion);
        var runDto = await GetAsync(new(command.CompanyId, command.SwitchId, command.RehearsalId), cancellationToken);
        if (!runDto.IsReadyForPlan)
            throw Conflict(AccountingProviderSwitchRehearsalReasonCodes.PlanNotReady, runDto.ReadinessExplanation);
        var input = await InputAsync(command.CompanyId, command.SwitchId, command.RehearsalId, cancellationToken);
        if (!await IsInputCurrentAsync(sw, input, cancellationToken))
        {
            await AuditStaleAsync(command.CompanyId, command.ActorUserId, command.SwitchId, command.RehearsalId,
                command.CorrelationId, cancellationToken);
            throw Conflict(AccountingProviderSwitchRehearsalReasonCodes.Stale,
                "Staging, mappings, gaps, strategy, or source evidence changed after rehearsal. Run a new rehearsal.");
        }
        var participants = (command.ParticipantUserIds ?? []).Where(x => x != Guid.Empty).Distinct().Order().ToArray();
        if (participants.Length == 0) throw Error(AccountingProviderSwitchRehearsalReasonCodes.PlanNotReady,
            "At least one responsible participant is required.");
        var planVersion = (await _db.AccountingProviderSwitchCutoverPlans.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId)
            .MaxAsync(x => (int?)x.PlanVersion, cancellationToken) ?? 0) + 1;
        var snapshot = new
        {
            schemaVersion = 1, sw.Id, source = new { sw.SourceKind, sw.SourceProviderKey },
            target = new { sw.TargetKind, sw.TargetProviderKey }, sw.EffectiveFiscalPeriodId,
            strategy = sw.MigrationStrategy, input.SourceSnapshotHash, input.StagingHash, input.MappingHash,
            input.GapHash, input.StagedRecordCount, input.FinancialTotal, input.DatasetSummaryJson,
            checks = runDto.Checks.Select(x => new { x.CheckKey, x.ExpectedValue, x.ObservedValue, x.Tolerance,
                x.Currency, x.Result, x.ReasonCode, x.HasCurrentManualEvidence }),
            acceptedExceptions = runDto.ManualEvidence.Select(x => new { x.CheckId, x.EvidenceReference, x.ExpiresUtc }),
            rehearsal = new { runDto.Id, runDto.SimulationKind, runDto.ProviderAcceptanceProven, runDto.Disclosure },
            freezeStartsUtc = command.FreezeStartsUtc, freezeEndsUtc = command.FreezeEndsUtc,
            command.RecoveryBoundary, participants
        };
        var snapshotJson = JsonSerializer.Serialize(snapshot);
        var planHash = Hash(snapshotJson);
        var plan = new AccountingProviderSwitchCutoverPlan(command.CompanyId, command.SwitchId,
            command.RehearsalId, planVersion, planHash, input.SourceSnapshotHash, sw.MigrationStrategy,
            command.FreezeStartsUtc, command.FreezeEndsUtc, command.RecoveryBoundary,
            JsonSerializer.Serialize(participants), snapshotJson, command.ActorUserId, Now());
        _db.AccountingProviderSwitchCutoverPlans.Add(plan);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchCutoverPlanGenerated, plan.Id, AuditEventOutcomes.Succeeded,
            "An immutable accounting migration cutover plan was generated from current rehearsal evidence.",
            command.CorrelationId, new() { ["switchId"] = command.SwitchId.ToString("D"),
                ["rehearsalId"] = command.RehearsalId.ToString("D"), ["planVersion"] = planVersion.ToString(),
                ["planHash"] = planHash }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await PlanDtoAsync(plan, sw, cancellationToken);
    }

    public async Task<AccountingProviderSwitchCutoverPlanDto> RequestPlanApprovalAsync(
        RequestAccountingProviderSwitchPlanApprovalCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var sw = await SwitchAsync(command.CompanyId, command.SwitchId, cancellationToken);
        EnsureVersion(sw, command.ExpectedSwitchVersion);
        var plan = await Plans(command.CompanyId, command.SwitchId).SingleOrDefaultAsync(x => x.Id == command.PlanId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchRehearsalReasonCodes.PlanNotReady, "The cutover plan was not found for this company.");
        var existing = await _db.AccountingProviderSwitchPlanApprovals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.PlanId == plan.Id, cancellationToken);
        if (existing is not null) return await PlanDtoAsync(plan, sw, cancellationToken);
        if (!await IsPlanCurrentAsync(sw, plan, cancellationToken))
        {
            await AuditStaleAsync(command.CompanyId, command.ActorUserId, command.SwitchId, plan.RehearsalId,
                command.CorrelationId, cancellationToken);
            throw Conflict(AccountingProviderSwitchRehearsalReasonCodes.PlanStale,
                "The cutover plan is stale. Generate a new plan from a current rehearsal.");
        }
        var approval = await _approvals.CreateAsync(command.CompanyId, new CreateApprovalRequestCommand(
            ApprovalTargetEntityType.AccountingProviderSwitchCutoverPlan.ToStorageValue(), plan.Id, "human",
            command.ActorUserId, "accounting_provider_switch_cutover_plan",
            new Dictionary<string, JsonNode?> { ["switchId"] = command.SwitchId, ["planId"] = plan.Id,
                ["planVersion"] = plan.PlanVersion, ["planHash"] = plan.PlanHash,
                ["sourceSnapshotHash"] = plan.SourceSnapshotHash, ["strategy"] = plan.Strategy,
                ["freezeStartsUtc"] = plan.FreezeStartsUtc, ["freezeEndsUtc"] = plan.FreezeEndsUtc },
            RequiredRole: "finance_approver"), cancellationToken);
        _db.AccountingProviderSwitchPlanApprovals.Add(new AccountingProviderSwitchPlanApproval(command.CompanyId,
            command.SwitchId, plan.Id, plan.PlanHash, approval.Id, command.ActorUserId, Now()));
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchPlanApprovalRequested, plan.Id, AuditEventOutcomes.Requested,
            "The immutable cutover plan was submitted for separate human approval.", command.CorrelationId,
            new() { ["switchId"] = command.SwitchId.ToString("D"), ["planHash"] = plan.PlanHash,
                ["approvalRequestId"] = approval.Id.ToString("D") }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await PlanDtoAsync(plan, sw, cancellationToken);
    }

    public async Task<AccountingProviderSwitchPlanReadinessDto> GetPlanReadinessAsync(
        GetAccountingProviderSwitchPlanReadinessQuery query, CancellationToken cancellationToken)
    {
        var sw = await SwitchAsync(query.CompanyId, query.SwitchId, cancellationToken);
        var plans = Plans(query.CompanyId, query.SwitchId);
        var plan = query.PlanId.HasValue ? await plans.SingleOrDefaultAsync(x => x.Id == query.PlanId, cancellationToken)
            : await plans.OrderByDescending(x => x.PlanVersion).FirstOrDefaultAsync(cancellationToken);
        if (plan is null) return new(query.SwitchId, null, false,
            AccountingProviderSwitchRehearsalReasonCodes.PlanNotReady, "Generate a cutover plan from a successful rehearsal.");
        var dto = await PlanDtoAsync(plan, sw, cancellationToken);
        return dto.IsApprovedAndCurrent
            ? new(query.SwitchId, dto, true, null, "The current immutable plan is approved and the switch is eligible for target preparation. Accounting authority is unchanged.")
            : new(query.SwitchId, dto, false, dto.IsCurrent ? AccountingProviderSwitchRehearsalReasonCodes.PlanApprovalPending : AccountingProviderSwitchRehearsalReasonCodes.PlanStale,
                dto.IsCurrent ? "The current cutover plan is waiting for separate human approval." : "The approved or requested plan is stale because its bound evidence changed.");
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = Now();
        var due = await _db.AccountingProviderSwitchRehearsals.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == AccountingProviderSwitchRehearsalStatuses.Queued && x.NextAttemptUtc <= now) ||
                        (x.Status == AccountingProviderSwitchRehearsalStatuses.Running && x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.RequestedUtc).Select(x => x.Id).Take(_options.ClaimBatchSize).ToListAsync(cancellationToken);
        var handled = 0;
        foreach (var runId in due)
        {
            var owner = $"rehearsal:{Environment.MachineName}:{Guid.NewGuid():N}";
            var claimed = await _db.AccountingProviderSwitchRehearsals.IgnoreQueryFilters()
                .Where(x => x.Id == runId && ((x.Status == AccountingProviderSwitchRehearsalStatuses.Queued && x.NextAttemptUtc <= now) ||
                    (x.Status == AccountingProviderSwitchRehearsalStatuses.Running && x.LeaseExpiresUtc <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AccountingProviderSwitchRehearsalStatuses.Running)
                    .SetProperty(x => x.LeaseOwner, owner)
                    .SetProperty(x => x.LeaseExpiresUtc, now.AddSeconds(_options.LeaseSeconds))
                    .SetProperty(x => x.StartedUtc, x => x.StartedUtc ?? now)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.NextAttemptUtc, (DateTime?)null)
                    .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            if (claimed == 0) continue;
            _db.ChangeTracker.Clear();
            var run = await _db.AccountingProviderSwitchRehearsals.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == runId && x.LeaseOwner == owner, cancellationToken);
            try
            {
                await ExecuteAsync(run, cancellationToken);
                handled++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var summary = Safe(exception.Message);
                if (run.AttemptCount < _options.MaximumAttempts &&
                    exception is DbUpdateException or HttpRequestException or TimeoutException)
                    run.Retry("rehearsal_retryable_failure", summary, Now().AddSeconds(Math.Min(300, 10 * run.AttemptCount)));
                else
                    run.Fail("rehearsal_failed", summary, Now());
                await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
                    AuditEventActions.AccountingProviderSwitchRehearsalFailed, run.Id, AuditEventOutcomes.Failed,
                    summary, run.CorrelationId, new() { ["switchId"] = run.SwitchId.ToString("D"),
                        ["attempt"] = run.AttemptCount.ToString() }, cancellationToken);
                await SaveAsync(cancellationToken);
            }
        }
        return handled;
    }

    private async Task ExecuteAsync(AccountingProviderSwitchRehearsal run, CancellationToken ct)
    {
        var sw = await SwitchAsync(run.CompanyId, run.SwitchId, ct);
        EnsureRehearsalState(sw);
        var completeness = await _staging.GetCompletenessAsync(new(run.CompanyId, run.SwitchId), ct);
        if (!completeness.IsComplete || await HasBlockingGapsAsync(run.CompanyId, run.SwitchId, ct))
            throw new InvalidOperationException("Rehearsal inputs are no longer complete or contain blocking gaps.");
        var records = await _db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.IsCurrent)
            .OrderBy(x => x.Dataset).ThenBy(x => x.SourceIdentity).ToListAsync(ct);
        var hashes = await ComputeHashesAsync(sw, records, ct);
        var expected = await LatestExpectedDatasetsAsync(run.CompanyId, run.SwitchId, ct);
        var summaryJson = JsonSerializer.Serialize(expected.OrderBy(x => x.Key).ToDictionary(x => x.Key,
            x => new { x.Value.RecordCount, x.Value.FinancialTotal, x.Value.Currency, x.Value.IntegrityHash }));
        var input = new AccountingProviderSwitchRehearsalInput(Guid.NewGuid(), run.CompanyId, run.SwitchId,
            run.Id, sw.Version, sw.MigrationStrategy, hashes.Source, hashes.Staging, hashes.Mapping, hashes.Gap,
            records.Count, records.Sum(x => x.FinancialAmount), summaryJson, Now());
        _db.AccountingProviderSwitchRehearsalInputs.Add(input);
        run.SetProgress(1, 12 + expected.Count + 2);

        var requestRecords = records.Select(x => new RehearsalStagedRecord(x.Id, x.Dataset, x.SourceIdentity,
            x.SourceVersion, x.SourceHash, x.NormalizedHash, x.NormalizedDataJson, x.EvidenceJson,
            x.FinancialAmount, x.Currency, x.Disposition)).ToArray();
        var adapter = _adapters.First(x => x.CanHandle(sw.TargetKind, sw.TargetProviderKey));
        var preview = await adapter.PreviewAsync(new(run.CompanyId, run.SwitchId, sw.TargetKind,
            sw.TargetProviderKey, input.SourceSnapshotHash, requestRecords, run.CorrelationId), ct);
        if (!preview.IsSupported)
            preview = LocalSimulation(requestRecords, preview.Disclosure);

        foreach (var group in records.GroupBy(x => new { x.Dataset, Currency = x.Currency ?? "" }))
        {
            var exp = expected.GetValueOrDefault(group.Key.Dataset);
            var observed = group.Where(TransfersToTarget).ToArray();
            var expectedCount = exp?.RecordCount ?? group.LongCount();
            var expectedTotal = exp?.FinancialTotal ?? group.Sum(x => x.FinancialAmount);
            var observedTotal = observed.Sum(x => x.FinancialAmount);
            var passed = expectedCount == group.LongCount() && Math.Abs(expectedTotal - group.Sum(x => x.FinancialAmount)) <= DefaultTolerance;
            _db.AccountingProviderSwitchRehearsalDatasetResults.Add(new(run.CompanyId, run.SwitchId, run.Id,
                group.Key.Dataset, expectedCount, observed.LongLength, expectedTotal, observedTotal,
                string.IsNullOrEmpty(group.Key.Currency) ? exp?.Currency : group.Key.Currency,
                passed ? AccountingProviderSwitchReconciliationResults.Passed : AccountingProviderSwitchReconciliationResults.Failed,
                passed ? "dataset_reconciled" : "dataset_count_or_total_mismatch",
                JsonSerializer.Serialize(new { assessmentIntegrityHash = exp?.IntegrityHash,
                    stagedHashes = group.Select(x => x.NormalizedHash), targetOutcomes = observed.Select(x => preview.RecordOutcomes.GetValueOrDefault(x.Id, "accepted")) }), Now()));
        }
        var checks = CalculateChecks(run, sw, input, records, expected, preview);
        _db.AccountingProviderSwitchReconciliationChecks.AddRange(checks);
        run.SetProgress(run.TotalWorkItems - 1, run.TotalWorkItems);
        run.Complete(preview.SimulationKind, preview.ProviderAcceptanceProven, preview.Disclosure, Now());
        foreach (var check in checks)
            await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
                AuditEventActions.AccountingProviderSwitchReconciliationCalculated, check.Id,
                check.Result == AccountingProviderSwitchReconciliationResults.Failed ? AuditEventOutcomes.Blocked : AuditEventOutcomes.Succeeded,
                $"Reconciliation check '{check.CheckKey}' calculated as {check.Result}.", run.CorrelationId,
                new() { ["switchId"] = run.SwitchId.ToString("D"), ["rehearsalId"] = run.Id.ToString("D"),
                    ["checkKey"] = check.CheckKey, ["result"] = check.Result, ["reasonCode"] = check.ReasonCode }, ct);
        await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
            AuditEventActions.AccountingProviderSwitchRehearsalCompleted, run.Id, AuditEventOutcomes.Succeeded,
            checks.Any(x => x.Result == AccountingProviderSwitchReconciliationResults.Failed)
                ? "The non-authoritative rehearsal completed with blocking reconciliation differences."
                : "The non-authoritative rehearsal completed and its controls were persisted.",
            run.CorrelationId, new() { ["switchId"] = run.SwitchId.ToString("D"),
                ["sourceSnapshotHash"] = input.SourceSnapshotHash, ["simulationKind"] = preview.SimulationKind,
                ["providerAcceptanceProven"] = preview.ProviderAcceptanceProven.ToString() }, ct);
        await SaveAsync(ct);
    }

    private List<AccountingProviderSwitchReconciliationCheck> CalculateChecks(AccountingProviderSwitchRehearsal run,
        AccountingProviderSwitch sw, AccountingProviderSwitchRehearsalInput input,
        IReadOnlyList<AccountingProviderSwitchStagedRecord> records,
        IReadOnlyDictionary<string, ExpectedDataset> expected,
        AccountingProviderSwitchRehearsalTargetResult preview)
    {
        var checks = new List<AccountingProviderSwitchReconciliationCheck>();
        var journalBalances = records.SelectMany(ExtractJournalBalances).ToArray();
        var trialBreakdown = journalBalances.GroupBy(x => new { x.Account, x.Currency })
            .Select(x => new { x.Key.Account, x.Key.Currency, Debit = x.Sum(y => y.Debit),
                Credit = x.Sum(y => y.Credit), Difference = x.Sum(y => y.Debit - y.Credit) }).ToArray();
        void Add(string key, decimal expectedValue, decimal observed, string reason, bool manual = false,
            string? currency = null, decimal? tolerance = null)
        {
            var appliedTolerance = tolerance ?? CurrencyTolerance(currency);
            var result = manual ? AccountingProviderSwitchReconciliationResults.ManualEvidenceRequired :
                Math.Abs(expectedValue - observed) <= appliedTolerance ? AccountingProviderSwitchReconciliationResults.Passed : AccountingProviderSwitchReconciliationResults.Failed;
            checks.Add(new(run.CompanyId, run.SwitchId, run.Id, key,
                expectedValue.ToString(CultureInfo.InvariantCulture), observed.ToString(CultureInfo.InvariantCulture),
                appliedTolerance, currency, result, result == AccountingProviderSwitchReconciliationResults.Passed ? "reconciliation_passed" : reason,
                JsonSerializer.Serialize(new { input.SourceSnapshotHash, input.StagingHash, input.MappingHash,
                    recordIds = records.Select(x => x.Id), preview.SimulationKind, trialBalance = trialBreakdown }), CalculationVersion, manual, Now()));
        }
        if (journalBalances.Length == 0)
        {
            Add(AccountingProviderSwitchReconciliationCheckKeys.DebitCreditEquality, 0, 0, "debit_credit_mismatch");
            Add(AccountingProviderSwitchReconciliationCheckKeys.TrialBalanceByAccountAndCurrency, 0, 0, "trial_balance_mismatch");
        }
        else foreach (var currencyGroup in journalBalances.GroupBy(x => x.Currency))
        {
            Add(AccountingProviderSwitchReconciliationCheckKeys.DebitCreditEquality, 0,
                currencyGroup.Sum(x => x.Debit - x.Credit), "debit_credit_mismatch", currency: currencyGroup.Key);
            Add(AccountingProviderSwitchReconciliationCheckKeys.TrialBalanceByAccountAndCurrency, 0,
                trialBreakdown.Where(x => x.Currency == currencyGroup.Key).Select(x => Math.Abs(x.Difference)).DefaultIfEmpty().Max(),
                "trial_balance_mismatch", currency: currencyGroup.Key);
        }
        var receivableDocs = records.Where(x => IsCustomer(x.NormalizedDataJson) && x.Dataset is "invoices" or "credits").Sum(x => Math.Abs(x.FinancialAmount));
        var receivableOpen = records.Where(x => x.Dataset == "open_items" && IsCustomer(x.NormalizedDataJson)).Sum(x => Math.Abs(x.FinancialAmount));
        Add(AccountingProviderSwitchReconciliationCheckKeys.ReceivableOpenItems, receivableDocs, receivableOpen, "receivable_open_items_mismatch");
        var payableDocs = records.Where(x => IsSupplier(x.NormalizedDataJson) && x.Dataset is "invoices" or "credits").Sum(x => Math.Abs(x.FinancialAmount));
        var payableOpen = records.Where(x => x.Dataset == "open_items" && IsSupplier(x.NormalizedDataJson)).Sum(x => Math.Abs(x.FinancialAmount));
        Add(AccountingProviderSwitchReconciliationCheckKeys.PayableOpenItems, payableDocs, payableOpen, "payable_open_items_mismatch");
        var taxControl = records.Where(x => x.Dataset == AccountingProviderSwitchStagingDatasets.TaxTreatments).Sum(x => x.FinancialAmount);
        var taxDetail = records.Sum(x => JsonDecimal(x.NormalizedDataJson, "taxAmount"));
        Add(AccountingProviderSwitchReconciliationCheckKeys.TaxControlDetail, taxControl, taxDetail, "tax_control_detail_mismatch");
        var bankDifference = records.Where(x => x.Dataset == AccountingProviderSwitchStagingDatasets.BankState).Sum(x =>
            JsonDecimal(x.NormalizedDataJson, "ledgerBalance") - JsonDecimal(x.NormalizedDataJson, "reconciledBalance"));
        Add(AccountingProviderSwitchReconciliationCheckKeys.BankReconciliation, 0, bankDifference, "bank_reconciliation_mismatch");
        var equityDifference = records.Where(x => x.Dataset == AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates).Sum(x =>
            JsonDecimal(x.NormalizedDataJson, "debit") - JsonDecimal(x.NormalizedDataJson, "credit"));
        Add(AccountingProviderSwitchReconciliationCheckKeys.OpeningEquity, 0, equityDifference, "opening_equity_mismatch");
        Add(AccountingProviderSwitchReconciliationCheckKeys.SourceDispositionCompleteness, 0,
            records.LongCount(x => AccountingProviderSwitchDispositions.BlocksProgress(x.Disposition)), "source_disposition_incomplete", tolerance: 0);
        var duplicates = records.Where(TransfersToTarget).GroupBy(x => x.NormalizedHash).Sum(x => Math.Max(0, x.Count() - 1));
        Add(AccountingProviderSwitchReconciliationCheckKeys.DuplicateIdentities, 0, duplicates, "duplicate_identity_detected", tolerance: 0);
        var unresolved = records.LongCount(x => preview.RecordOutcomes.GetValueOrDefault(x.Id, "accepted") is not "accepted");
        Add(AccountingProviderSwitchReconciliationCheckKeys.UnresolvedProviderOutcomes, 0, unresolved, "provider_outcome_unresolved", tolerance: 0);
        Add(AccountingProviderSwitchReconciliationCheckKeys.EvidenceCoverage, 0,
            records.LongCount(x => string.IsNullOrWhiteSpace(x.EvidenceJson) || x.EvidenceJson.Trim() is "{}" or "null"), "evidence_coverage_incomplete", tolerance: 0);
        var newestExtraction = expected.Count == 0 ? DateTime.MinValue : expected.Values.Max(x => x.ExtractedUtc);
        var staleHours = newestExtraction == DateTime.MinValue ? decimal.MaxValue : (decimal)(Now() - newestExtraction).TotalHours;
        Add(AccountingProviderSwitchReconciliationCheckKeys.SourceSnapshotFreshness, 0,
            staleHours <= _options.SourceFreshnessHours ? 0 : staleHours, "source_snapshot_stale", tolerance: 0);
        if (sw.MigrationStrategy == AccountingProviderSwitchStrategies.FullHistory &&
            !records.Any(x => x.Dataset == AccountingProviderSwitchStagingDatasets.Documents))
            Add("archive_evidence", 0, 1, "archive_evidence_required", manual: true, tolerance: 0);
        return checks;
    }

    private static AccountingProviderSwitchRehearsalTargetResult LocalSimulation(
        IReadOnlyList<RehearsalStagedRecord> records, string disclosure) => new(true, false,
        "local_target_simulation", disclosure,
        records.Where(x => x.Disposition is not (AccountingProviderSwitchDispositions.Duplicate or AccountingProviderSwitchDispositions.ExcludedWithApproval))
            .ToDictionary(x => x.Id, _ => "accepted"));

    private async Task<AccountingProviderSwitchRehearsalDto> ToDtoAsync(AccountingProviderSwitchRehearsal run, CancellationToken ct)
    {
        var input = await _db.AccountingProviderSwitchRehearsalInputs.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.RehearsalId == run.Id, ct);
        var datasets = await _db.AccountingProviderSwitchRehearsalDatasetResults.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.RehearsalId == run.Id).OrderBy(x => x.Dataset).ToListAsync(ct);
        var checks = await _db.AccountingProviderSwitchReconciliationChecks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.RehearsalId == run.Id).OrderBy(x => x.CheckKey).ToListAsync(ct);
        var evidence = await _db.AccountingProviderSwitchManualEvidence.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.RehearsalId == run.Id).OrderBy(x => x.RecordedUtc).ToListAsync(ct);
        var now = Now();
        var currentEvidence = input is null ? new HashSet<Guid>() : evidence.Where(x => x.InputHash == input.SourceSnapshotHash && (!x.ExpiresUtc.HasValue || x.ExpiresUtc > now)).Select(x => x.CheckId).ToHashSet();
        var blockingChecks = checks.Where(x => x.Result == AccountingProviderSwitchReconciliationResults.Failed ||
            (x.Result == AccountingProviderSwitchReconciliationResults.ManualEvidenceRequired && !currentEvidence.Contains(x.Id))).ToArray();
        var ready = run.Status == AccountingProviderSwitchRehearsalStatuses.Completed && blockingChecks.Length == 0 &&
                    datasets.All(x => x.Result == AccountingProviderSwitchReconciliationResults.Passed);
        return new(run.Id, run.CompanyId, run.SwitchId, run.Status, run.SimulationKind, run.ProviderAcceptanceProven,
            run.Disclosure, run.CompletedWorkItems, run.TotalWorkItems,
            run.TotalWorkItems == 0 ? 0 : (int)Math.Floor(run.CompletedWorkItems * 100m / run.TotalWorkItems),
            run.AttemptCount, run.NextAttemptUtc, run.FailureCode, run.FailureSummary, run.RequestedUtc,
            run.StartedUtc, run.CompletedUtc, run.Version,
            input is null ? null : new(input.Id, input.SwitchVersion, input.Strategy, input.SourceSnapshotHash,
                input.StagingHash, input.MappingHash, input.GapHash, input.StagedRecordCount,
                input.FinancialTotal, input.DatasetSummaryJson, input.CreatedUtc),
            datasets.Select(x => new AccountingProviderSwitchRehearsalDatasetResultDto(x.Id, x.Dataset,
                x.ExpectedCount, x.ObservedCount, x.ExpectedTotal, x.ObservedTotal, x.Currency, x.Result,
                x.ReasonCode, x.EvidenceJson, x.CalculatedUtc)).ToArray(),
            checks.Select(x => new AccountingProviderSwitchReconciliationCheckDto(x.Id, x.CheckKey,
                x.ExpectedValue, x.ObservedValue, x.Tolerance, x.Currency, x.Result, x.ReasonCode,
                x.DataSourcesJson, x.CalculationVersion, x.ManualEvidenceAllowed, currentEvidence.Contains(x.Id), x.CalculatedUtc)).ToArray(),
            evidence.Select(ToDto).ToArray(), ready,
            ready ? "All calculated controls passed and required manual evidence is current."
                : blockingChecks.Length > 0 ? $"{blockingChecks.Length} reconciliation control(s) still block plan generation."
                : "The rehearsal has not completed successfully or a dataset result differs.");
    }

    private async Task<AccountingProviderSwitchCutoverPlanDto> PlanDtoAsync(AccountingProviderSwitchCutoverPlan plan,
        AccountingProviderSwitch sw, CancellationToken ct)
    {
        var binding = await _db.AccountingProviderSwitchPlanApprovals.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == plan.CompanyId && x.PlanId == plan.Id, ct);
        ApprovalRequestStatus? status = null;
        if (binding is not null)
            status = await _db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == plan.CompanyId && x.Id == binding.ApprovalRequestId).Select(x => (ApprovalRequestStatus?)x.Status).SingleOrDefaultAsync(ct);
        var current = await IsPlanCurrentAsync(sw, plan, ct);
        return new(plan.Id, plan.CompanyId, plan.SwitchId, plan.RehearsalId, plan.PlanVersion, plan.PlanHash,
            plan.SourceSnapshotHash, plan.Strategy, plan.FreezeStartsUtc, plan.FreezeEndsUtc, plan.RecoveryBoundary,
            plan.ParticipantsJson, plan.SnapshotJson, plan.GeneratedByUserId, plan.GeneratedUtc,
            binding?.ApprovalRequestId, status?.ToStorageValue(), current,
            current && binding?.PlanHash == plan.PlanHash && status == ApprovalRequestStatus.Approved);
    }

    private async Task<bool> IsPlanCurrentAsync(AccountingProviderSwitch sw, AccountingProviderSwitchCutoverPlan plan, CancellationToken ct)
    {
        var input = await InputAsync(plan.CompanyId, plan.SwitchId, plan.RehearsalId, ct);
        return plan.PlanHash == Hash(plan.SnapshotJson) && plan.SourceSnapshotHash == input.SourceSnapshotHash &&
               plan.Strategy == sw.MigrationStrategy && await IsInputCurrentAsync(sw, input, ct);
    }

    private async Task<bool> IsInputCurrentAsync(AccountingProviderSwitch sw, AccountingProviderSwitchRehearsalInput input, CancellationToken ct)
    {
        var records = await _db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.IsCurrent)
            .OrderBy(x => x.Dataset).ThenBy(x => x.SourceIdentity).ToListAsync(ct);
        var hashes = await ComputeHashesAsync(sw, records, ct);
        return input.Strategy == sw.MigrationStrategy && input.StagingHash == hashes.Staging &&
               input.MappingHash == hashes.Mapping && input.GapHash == hashes.Gap && input.SourceSnapshotHash == hashes.Source;
    }

    private async Task<InputHashes> ComputeHashesAsync(AccountingProviderSwitch sw,
        IReadOnlyList<AccountingProviderSwitchStagedRecord> records, CancellationToken ct)
    {
        var mappings = await _db.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.Status == AccountingProviderSwitchMappingStatuses.Approved)
            .OrderBy(x => x.MappingType).ThenBy(x => x.SourceKey).Select(x => x.BindingHash).ToListAsync(ct);
        var latestAssessment = await _db.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.Status == AccountingProviderSwitchAssessmentStatuses.Completed)
            .OrderByDescending(x => x.CompletedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        var gaps = latestAssessment.HasValue ? await _db.AccountingProviderSwitchGaps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == sw.CompanyId && x.SwitchId == sw.Id && x.AssessmentId == latestAssessment.Value)
            .OrderBy(x => x.ReasonCode).Select(x => x.ReasonCode + ":" + x.EvidenceJson).ToListAsync(ct) : [];
        var staging = Hash(string.Join("|", records.Select(x => $"{x.Id:D}:{x.SourceVersion}:{x.SourceHash}:{x.NormalizedHash}:{x.Disposition}:{x.MappingVersion}")));
        var mapping = Hash(string.Join("|", mappings)); var gap = Hash(string.Join("|", gaps));
        return new(Hash($"{sw.Id:D}|{sw.MigrationStrategy}|{staging}|{mapping}|{gap}"), staging, mapping, gap);
    }

    private async Task<Dictionary<string, ExpectedDataset>> LatestExpectedDatasetsAsync(Guid companyId, Guid switchId, CancellationToken ct)
    {
        var assessmentId = await _db.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SwitchId == switchId && x.Status == AccountingProviderSwitchAssessmentStatuses.Completed)
            .OrderByDescending(x => x.CompletedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        if (!assessmentId.HasValue) return new();
        var datasets = await _db.AccountingProviderSwitchDatasets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SwitchId == switchId && x.AssessmentId == assessmentId.Value && x.EndpointRole == AccountingProviderSwitchEndpointRoles.Source)
            .ToListAsync(ct);
        return datasets.Select(x => new { Key = ToStagingDataset(x.DatasetKey), Value = x })
            .Where(x => x.Key is not null)
            .GroupBy(x => x.Key!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => new ExpectedDataset(x.Sum(item => item.Value.RecordCount),
                x.Sum(item => item.Value.FinancialTotal), x.Select(item => item.Value.Currency).Distinct().Count() == 1
                    ? x.Select(item => item.Value.Currency).Single() : null,
                Hash(string.Join("|", x.OrderBy(item => item.Value.DatasetKey).Select(item => item.Value.IntegrityHash))),
                x.Max(item => item.Value.ExtractedUtc)), StringComparer.Ordinal);
    }

    private async Task<bool> HasBlockingGapsAsync(Guid companyId, Guid switchId, CancellationToken ct)
    {
        var assessmentId = await _db.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SwitchId == switchId && x.Status == AccountingProviderSwitchAssessmentStatuses.Completed)
            .OrderByDescending(x => x.CompletedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(ct);
        return assessmentId.HasValue && await _db.AccountingProviderSwitchGaps.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.SwitchId == switchId && x.AssessmentId == assessmentId.Value && x.IsBlocking, ct);
    }

    private Task<AccountingProviderSwitch> SwitchAsync(Guid companyId, Guid switchId, CancellationToken ct) =>
        _db.AccountingProviderSwitches.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == switchId, ct)
            .ContinueWith(x => x.Result ?? throw Error(AccountingProviderSwitchReasonCodes.NotFound,
                "The accounting-system switch was not found for this company."), ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    private IQueryable<AccountingProviderSwitchRehearsal> Runs(Guid companyId, Guid switchId, bool tracking) { var q = _db.AccountingProviderSwitchRehearsals.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.SwitchId == switchId); return tracking ? q : q.AsNoTracking(); }
    private IQueryable<AccountingProviderSwitchCutoverPlan> Plans(Guid companyId, Guid switchId) => _db.AccountingProviderSwitchCutoverPlans.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.SwitchId == switchId);
    private Task<AccountingProviderSwitchRehearsalInput> InputAsync(Guid companyId, Guid switchId, Guid rehearsalId, CancellationToken ct) => _db.AccountingProviderSwitchRehearsalInputs.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.SwitchId == switchId && x.RehearsalId == rehearsalId, ct);
    private static bool TransfersToTarget(AccountingProviderSwitchStagedRecord x) => x.Disposition is not (AccountingProviderSwitchDispositions.Duplicate or AccountingProviderSwitchDispositions.ExcludedWithApproval or AccountingProviderSwitchDispositions.Missing or AccountingProviderSwitchDispositions.Unsupported);
    private static bool IsCustomer(string json) => json.Contains("customer", StringComparison.OrdinalIgnoreCase) || json.Contains("receivable", StringComparison.OrdinalIgnoreCase);
    private static bool IsSupplier(string json) => json.Contains("supplier", StringComparison.OrdinalIgnoreCase) || json.Contains("payable", StringComparison.OrdinalIgnoreCase);
    private static decimal JsonDecimal(string json, string property) { try { using var doc = JsonDocument.Parse(json); return Sum(doc.RootElement, property); } catch (JsonException) { return 0; } }
    private static decimal Sum(JsonElement e, string property) { decimal total = 0; if (e.ValueKind == JsonValueKind.Object) foreach (var p in e.EnumerateObject()) total += p.NameEquals(property) && p.Value.TryGetDecimal(out var value) ? value : Sum(p.Value, property); else if (e.ValueKind == JsonValueKind.Array) foreach (var item in e.EnumerateArray()) total += Sum(item, property); return total; }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static decimal CurrencyTolerance(string? currency) => currency?.ToUpperInvariant() switch
    {
        "JPY" or "KRW" => 1m,
        "BHD" or "KWD" => 0.001m,
        _ => DefaultTolerance
    };
    private static IEnumerable<JournalBalance> ExtractJournalBalances(AccountingProviderSwitchStagedRecord record)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(record.NormalizedDataJson); }
        catch (JsonException) { yield break; }
        using (document)
            foreach (var balance in ExtractJournalBalances(document.RootElement, record.Currency ?? "BASE")) yield return balance;
    }
    private static IEnumerable<JournalBalance> ExtractJournalBalances(JsonElement element, string inheritedCurrency)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var currency = ReadString(element, "currency") ?? inheritedCurrency;
            decimal debit = 0, credit = 0;
            var hasDebit = element.TryGetProperty("debit", out var debitElement) && debitElement.TryGetDecimal(out debit);
            var hasCredit = element.TryGetProperty("credit", out var creditElement) && creditElement.TryGetDecimal(out credit);
            if (hasDebit || hasCredit)
                yield return new(ReadString(element, "accountKey") ?? ReadString(element, "accountCode") ??
                    ReadString(element, "account") ?? "unassigned", currency, hasDebit ? debit : 0, hasCredit ? credit : 0);
            foreach (var property in element.EnumerateObject())
                foreach (var child in ExtractJournalBalances(property.Value, currency)) yield return child;
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var child in ExtractJournalBalances(item, inheritedCurrency)) yield return child;
    }
    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
    private static string Safe(string text) => string.IsNullOrWhiteSpace(text) ? "The rehearsal failed safely." : text.Length <= 1000 ? text : text[..1000];
    private static string Required(string? value, string name, int max) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static void Validate(Guid companyId, Guid switchId, Guid actor, string correlation) { if (companyId == Guid.Empty || switchId == Guid.Empty || actor == Guid.Empty) throw new ArgumentException("Company, switch, and actor are required."); Required(correlation, nameof(correlation), 128); }
    private static void EnsureVersion(AccountingProviderSwitch sw, long version) { if (sw.Version != version) throw Conflict(AccountingProviderSwitchReasonCodes.ConcurrencyConflict, "The accounting-system switch changed while this request was being reviewed."); }
    private static void EnsureRehearsalState(AccountingProviderSwitch sw) { if (sw.Status is not (AccountingProviderSwitchStatuses.ReadyForPlanning or AccountingProviderSwitchStatuses.PlanAwaitingApproval or AccountingProviderSwitchStatuses.PreparingTarget)) throw Conflict(AccountingProviderSwitchRehearsalReasonCodes.NotReady, "Rehearsal is available after assessment and mapping preparation, before source freeze."); }
    private Task WriteAuditAsync(Guid companyId, Guid actor, string action, Guid target, string outcome, string summary, string correlation, Dictionary<string,string?> evidence, CancellationToken ct) => _audit.WriteAsync(new(companyId, AuditActorTypes.User, actor, action, "accounting_provider_switch_rehearsal", target.ToString("D"), outcome, summary, ["accounting_provider_switch","rehearsal","reconciliation"], evidence, correlation, Now()), ct);
    private Task AuditStaleAsync(Guid companyId, Guid actor, Guid switchId, Guid runId, string correlation, CancellationToken ct) => WriteAuditAsync(companyId, actor, AuditEventActions.AccountingProviderSwitchPlanStaleRejected, runId, AuditEventOutcomes.Blocked, "A stale rehearsal or cutover plan was rejected before approval recognition.", correlation, new() { ["switchId"] = switchId.ToString("D"), ["rehearsalId"] = runId.ToString("D") }, ct);
    private async Task SaveAsync(CancellationToken ct) { try { await _db.SaveChangesAsync(ct); } catch (DbUpdateConcurrencyException) { throw Conflict(AccountingProviderSwitchReasonCodes.ConcurrencyConflict, "Rehearsal evidence changed concurrently. Retry with current data."); } }
    private static AccountingAuthorityException Error(string code, string message) => new(code, message);
    private static AccountingAuthorityException Conflict(string code, string message) => new(code, message, true);
    private static AccountingProviderSwitchManualEvidenceDto ToDto(AccountingProviderSwitchManualEvidence x) => new(x.Id, x.CheckId, x.Explanation, x.EvidenceReference, x.RecordedByUserId, x.RecordedUtc, x.ExpiresUtc);
    private sealed record InputHashes(string Source, string Staging, string Mapping, string Gap);
    private sealed record ExpectedDataset(long RecordCount, decimal FinancialTotal, string? Currency,
        string IntegrityHash, DateTime ExtractedUtc);
    private sealed record JournalBalance(string Account, string Currency, decimal Debit, decimal Credit);
    private static string? ToStagingDataset(string dataset) => dataset switch
    {
        AccountingProviderSwitchDatasetKeys.Accounts => AccountingProviderSwitchStagingDatasets.Accounts,
        AccountingProviderSwitchDatasetKeys.Tax => AccountingProviderSwitchStagingDatasets.TaxTreatments,
        AccountingProviderSwitchDatasetKeys.Customers or AccountingProviderSwitchDatasetKeys.Suppliers => AccountingProviderSwitchStagingDatasets.Counterparties,
        AccountingProviderSwitchDatasetKeys.Invoices => AccountingProviderSwitchStagingDatasets.Invoices,
        AccountingProviderSwitchDatasetKeys.Credits => AccountingProviderSwitchStagingDatasets.Credits,
        AccountingProviderSwitchDatasetKeys.Payments => AccountingProviderSwitchStagingDatasets.Payments,
        AccountingProviderSwitchDatasetKeys.Allocations => AccountingProviderSwitchStagingDatasets.Allocations,
        AccountingProviderSwitchDatasetKeys.BankReconciliation => AccountingProviderSwitchStagingDatasets.BankState,
        AccountingProviderSwitchDatasetKeys.Currencies => AccountingProviderSwitchStagingDatasets.Currencies,
        AccountingProviderSwitchDatasetKeys.ExchangeRates => AccountingProviderSwitchStagingDatasets.ExchangeRates,
        AccountingProviderSwitchDatasetKeys.Dimensions => AccountingProviderSwitchStagingDatasets.Dimensions,
        AccountingProviderSwitchDatasetKeys.Journals => AccountingProviderSwitchStagingDatasets.Journals,
        AccountingProviderSwitchDatasetKeys.Attachments => AccountingProviderSwitchStagingDatasets.Documents,
        _ => null
    };
}
