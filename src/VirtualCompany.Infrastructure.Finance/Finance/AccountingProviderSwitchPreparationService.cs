using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchPreparationService : IAccountingProviderSwitchPreparationService,
    IAccountingProviderSwitchPreparationJobRunner
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IAccountingProviderSwitchInternalReadinessPolicy _readinessPolicy;
    private readonly IAccountingPostingService _postingService;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _time;
    private readonly AccountingProviderSwitchPreparationWorkerOptions _options;

    public AccountingProviderSwitchPreparationService(VirtualCompanyDbContext db,
        IAccountingProviderSwitchInternalReadinessPolicy readinessPolicy,
        IAccountingPostingService postingService,
        IAuditEventWriter audit,
        TimeProvider time,
        IOptions<AccountingProviderSwitchPreparationWorkerOptions> options)
    {
        _db = db;
        _readinessPolicy = readinessPolicy;
        _postingService = postingService;
        _audit = audit;
        _time = time;
        _options = options.Value;
    }

    public Task<AccountingProviderSwitchInternalReadinessDto> GetReadinessAsync(
        EvaluateAccountingProviderSwitchInternalReadinessQuery query,
        CancellationToken cancellationToken) => _readinessPolicy.EvaluateAsync(query, cancellationToken);

    public async Task<AccountingProviderSwitchPreparationDto> StartAsync(
        StartAccountingProviderSwitchPreparationCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        Required(command.IdempotencyKey, nameof(command.IdempotencyKey), 128);
        if (command.PlanId == Guid.Empty) throw Error(AccountingProviderSwitchPreparationReasonCodes.PlanNotApproved,
            "Select the approved cutover plan before starting preparation.");

        var existing = await Preparations(command.CompanyId, command.SwitchId, tracking: false)
            .SingleOrDefaultAsync(x => x.IdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (existing is not null) return await ToDtoAsync(existing, cancellationToken);
        var providerSwitch = await SwitchAsync(command.CompanyId, command.SwitchId, tracking: true, cancellationToken);
        EnsureVersion(providerSwitch, command.ExpectedSwitchVersion);

        var readiness = await _readinessPolicy.EvaluateAsync(new(command.CompanyId, command.SwitchId, command.PlanId), cancellationToken);
        if (!readiness.IsReady || readiness.PlanId != command.PlanId || string.IsNullOrWhiteSpace(readiness.PlanHash))
            throw Conflict(AccountingProviderSwitchPreparationReasonCodes.NotReady,
                FirstBlocking(readiness) ?? "The approved cutover plan is not ready for native target preparation.");
        var planRun = await Preparations(command.CompanyId, command.SwitchId, tracking: false)
            .SingleOrDefaultAsync(x => x.PlanHash == readiness.PlanHash, cancellationToken);
        if (planRun is not null) return await ToDtoAsync(planRun, cancellationToken);

        EnsurePreparationState(providerSwitch);
        if (providerSwitch.Status == AccountingProviderSwitchStatuses.ReadyForPlanning)
            providerSwitch.TransitionTo(AccountingProviderSwitchStatuses.PlanAwaitingApproval,
                command.ActorUserId, command.CorrelationId, Now());
        if (providerSwitch.Status == AccountingProviderSwitchStatuses.PlanAwaitingApproval)
            providerSwitch.TransitionTo(AccountingProviderSwitchStatuses.PreparingTarget,
                command.ActorUserId, command.CorrelationId, Now());

        var total = await _db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == command.CompanyId && x.SwitchId == command.SwitchId && x.IsCurrent,
                cancellationToken);
        var preparation = new AccountingProviderSwitchPreparation(Guid.NewGuid(), command.CompanyId,
            command.SwitchId, command.PlanId, readiness.PlanHash, providerSwitch.MigrationStrategy,
            command.ActorUserId, command.IdempotencyKey, command.CorrelationId, total, Now());
        _db.AccountingProviderSwitchPreparations.Add(preparation);
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchPreparationRequested, preparation.Id,
            AuditEventOutcomes.Requested,
            "Approved external-provider data was queued for non-authoritative native preparation.",
            command.CorrelationId, new()
            {
                ["switchId"] = command.SwitchId.ToString("D"), ["planId"] = command.PlanId.ToString("D"),
                ["planHash"] = readiness.PlanHash, ["strategy"] = providerSwitch.MigrationStrategy,
                ["sourceProvider"] = providerSwitch.SourceProviderKey
            }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToDtoAsync(preparation, cancellationToken);
    }

    public async Task<AccountingProviderSwitchPreparationDto> ReplayAsync(
        ReplayAccountingProviderSwitchPreparationCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId);
        var preparation = await Preparations(command.CompanyId, command.SwitchId, tracking: true)
            .SingleOrDefaultAsync(x => x.Id == command.PreparationId, cancellationToken)
            ?? throw Error(AccountingProviderSwitchPreparationReasonCodes.NotFound,
                "The target preparation run was not found for this company.");
        var readiness = await _readinessPolicy.EvaluateAsync(
            new(command.CompanyId, command.SwitchId, preparation.PlanId), cancellationToken);
        if (!readiness.IsReady || readiness.PlanHash != preparation.PlanHash)
            throw Conflict(AccountingProviderSwitchPreparationReasonCodes.PlanStale,
                "The approved plan or its evidence changed. Create a preparation run from the current approved plan.");
        preparation.QueueReplay(command.CorrelationId, Now());
        await WriteAuditAsync(command.CompanyId, command.ActorUserId,
            AuditEventActions.AccountingProviderSwitchPreparationRequested, preparation.Id,
            AuditEventOutcomes.Requested, "A failed native target preparation was queued for a bounded replay.",
            command.CorrelationId, new() { ["switchId"] = command.SwitchId.ToString("D"),
                ["planHash"] = preparation.PlanHash }, cancellationToken);
        await SaveAsync(cancellationToken);
        return await ToDtoAsync(preparation, cancellationToken);
    }

    public async Task<AccountingProviderSwitchPreparationDto> GetAsync(
        GetAccountingProviderSwitchPreparationQuery query,
        CancellationToken cancellationToken)
    {
        _ = await SwitchAsync(query.CompanyId, query.SwitchId, tracking: false, cancellationToken);
        var preparations = Preparations(query.CompanyId, query.SwitchId, tracking: false);
        var run = query.PreparationId.HasValue
            ? await preparations.SingleOrDefaultAsync(x => x.Id == query.PreparationId.Value, cancellationToken)
            : await preparations.OrderByDescending(x => x.RequestedUtc).FirstOrDefaultAsync(cancellationToken);
        return run is null
            ? throw Error(AccountingProviderSwitchPreparationReasonCodes.NotFound,
                "No native target preparation exists for this accounting-system switch.")
            : await ToDtoAsync(run, cancellationToken);
    }

    public async Task<IReadOnlyList<AccountingProviderSwitchNativeCandidateDto>> ListCandidatesAsync(
        ListAccountingProviderSwitchNativeCandidatesQuery query,
        CancellationToken cancellationToken)
    {
        _ = await SwitchAsync(query.CompanyId, query.SwitchId, tracking: false, cancellationToken);
        var candidates = _db.AccountingProviderSwitchNativeCandidates.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId);
        if (query.PreparationId.HasValue) candidates = candidates.Where(x => x.PreparedByRunId == query.PreparationId);
        if (!string.IsNullOrWhiteSpace(query.CandidateKind))
        {
            var kind = AccountingProviderSwitchNativeCandidateKinds.Normalize(query.CandidateKind);
            candidates = candidates.Where(x => x.CandidateKind == kind);
        }
        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = AccountingProviderSwitchNativeCandidateStatuses.Normalize(query.Status);
            candidates = candidates.Where(x => x.Status == status);
        }
        var rows = await candidates.OrderBy(x => x.CandidateKind).ThenBy(x => x.SourceIdentity)
            .Take(Math.Clamp(query.Limit, 1, 1000)).ToListAsync(cancellationToken);
        return await CandidateDtosAsync(rows, cancellationToken);
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = Now();
        var due = await _db.AccountingProviderSwitchPreparations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == AccountingProviderSwitchPreparationStatuses.Queued && x.NextAttemptUtc <= now) ||
                        (x.Status == AccountingProviderSwitchPreparationStatuses.Running && x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.RequestedUtc).Select(x => x.Id).Take(_options.ClaimBatchSize)
            .ToListAsync(cancellationToken);
        var handled = 0;
        foreach (var runId in due)
        {
            var owner = $"preparation:{Environment.MachineName}:{Guid.NewGuid():N}";
            var claimed = await _db.AccountingProviderSwitchPreparations.IgnoreQueryFilters()
                .Where(x => x.Id == runId &&
                            ((x.Status == AccountingProviderSwitchPreparationStatuses.Queued && x.NextAttemptUtc <= now) ||
                             (x.Status == AccountingProviderSwitchPreparationStatuses.Running && x.LeaseExpiresUtc <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AccountingProviderSwitchPreparationStatuses.Running)
                    .SetProperty(x => x.LeaseOwner, owner)
                    .SetProperty(x => x.LeaseExpiresUtc, now.AddSeconds(_options.LeaseSeconds))
                    .SetProperty(x => x.StartedUtc, x => x.StartedUtc ?? now)
                    .SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                    .SetProperty(x => x.NextAttemptUtc, (DateTime?)null)
                    .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            if (claimed == 0) continue;
            _db.ChangeTracker.Clear();
            var run = await _db.AccountingProviderSwitchPreparations.IgnoreQueryFilters()
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
                    exception is DbUpdateException or TimeoutException or HttpRequestException)
                    run.Retry("preparation_retryable_failure", summary,
                        Now().AddSeconds(Math.Min(300, 10 * run.AttemptCount)));
                else
                    run.Fail(AccountingProviderSwitchPreparationReasonCodes.Failed, summary, Now());
                await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
                    AuditEventActions.AccountingProviderSwitchPreparationFailed, run.Id,
                    AuditEventOutcomes.Failed, summary, run.CorrelationId,
                    new() { ["switchId"] = run.SwitchId.ToString("D"),
                        ["attempt"] = run.AttemptCount.ToString() }, cancellationToken);
                await SaveAsync(cancellationToken);
            }
        }
        return handled;
    }

    private async Task ExecuteAsync(AccountingProviderSwitchPreparation run, CancellationToken cancellationToken)
    {
        var providerSwitch = await SwitchAsync(run.CompanyId, run.SwitchId, tracking: false, cancellationToken);
        EnsurePreparationState(providerSwitch);
        var readiness = await _readinessPolicy.EvaluateAsync(new(run.CompanyId, run.SwitchId, run.PlanId), cancellationToken);
        if (!readiness.IsReady || readiness.PlanHash != run.PlanHash)
            throw new InvalidOperationException(FirstBlocking(readiness) ??
                "The approved cutover plan is no longer current or target readiness changed.");

        await _db.AccountingProviderSwitchReadinessChecks.IgnoreQueryFilters()
            .Where(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.PreparationId == run.Id)
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var check in readiness.Checks)
            _db.AccountingProviderSwitchReadinessChecks.Add(new(run.CompanyId, run.SwitchId, run.Id,
                check.CheckKey, check.IsReady, check.IsBlocking, check.ReasonCode, check.Explanation,
                check.EvidenceJson, Now()));
        await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
            AuditEventActions.AccountingProviderSwitchReadinessEvaluated, run.Id,
            readiness.IsReady ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            readiness.IsReady ? "Internal target readiness passed deterministic preparation checks."
                : "Internal target readiness contains blocking checks.", run.CorrelationId,
            new() { ["switchId"] = run.SwitchId.ToString("D"), ["planHash"] = run.PlanHash,
                ["blockingChecks"] = string.Join(",", readiness.Checks.Where(x => x.IsBlocking && !x.IsReady).Select(x => x.CheckKey)) },
            cancellationToken);

        var connection = await _db.FinanceIntegrationConnections.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.ProviderKey == providerSwitch.SourceProviderKey &&
                        x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("The connected source provider is unavailable for immutable source references.");

        var records = await _db.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.IsCurrent)
            .OrderBy(x => x.Dataset).ThenBy(x => x.SourceIdentity).ToListAsync(cancellationToken);
        var candidateCount = 0;
        var validCount = 0;
        var rejectedCount = 0;
        var existingReferenceCount = 0;
        var archiveCount = 0;
        var processed = 0;

        foreach (var record in records)
        {
            if (record.Disposition is AccountingProviderSwitchDispositions.ExcludedWithApproval or
                AccountingProviderSwitchDispositions.Unsupported)
            {
                archiveCount += await RecordArchiveDependencyAsync(run, record, "approved_source_archive_dependency",
                    "The approved strategy keeps this unsupported or excluded source record in the accessible provider archive.",
                    cancellationToken);
                processed++;
                continue;
            }
            if (!TransfersToTarget(record))
            {
                processed++;
                continue;
            }
            if (!IncludedByStrategy(run.Strategy, record.Dataset))
            {
                archiveCount += await RecordArchiveDependencyAsync(run, record, "strategy_source_archive_dependency",
                    "The approved migration strategy keeps this earlier provider-authoritative record in the accessible source archive.",
                    cancellationToken);
                processed++;
                continue;
            }

            var kind = ResolveKind(record);
            if (kind is null)
            {
                archiveCount += await RecordArchiveDependencyAsync(run, record, "unsupported_internal_candidate_kind",
                    "The approved plan retains this source dataset in the provider archive because it has no native preparation candidate.",
                    cancellationToken);
                processed++;
                continue;
            }
            var existingCandidate = await _db.AccountingProviderSwitchNativeCandidates.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId &&
                                           x.StagedRecordId == record.Id && x.CandidateKind == kind,
                    cancellationToken);
            if (existingCandidate is not null)
            {
                if (existingCandidate.SourceHash != record.SourceHash)
                    throw new InvalidOperationException("A staged source record changed after its native candidate was prepared.");
                candidateCount++;
                if (existingCandidate.Status == AccountingProviderSwitchNativeCandidateStatuses.Valid) validCount++;
                else rejectedCount++;
                await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
                    AuditEventActions.AccountingProviderSwitchNativeCandidateReplayed, existingCandidate.Id,
                    AuditEventOutcomes.Succeeded, "An idempotent preparation replay reused the existing native candidate.",
                    run.CorrelationId, CandidateEvidence(run, record, kind), cancellationToken);
                processed++;
                continue;
            }

            var entityType = ExternalEntityType(kind);
            var represented = await _db.FinanceExternalReferences.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == run.CompanyId && x.ProviderKey == providerSwitch.SourceProviderKey &&
                                           x.EntityType == entityType && x.ExternalId == record.SourceIdentity,
                    cancellationToken);
            if (represented is not null)
            {
                existingReferenceCount++;
                await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
                    AuditEventActions.AccountingProviderSwitchExistingReferenceMatched, represented.Id,
                    AuditEventOutcomes.Succeeded,
                    "The provider source identity already belongs to a native record, so no duplicate candidate was created.",
                    run.CorrelationId, CandidateEvidence(run, record, kind), cancellationToken);
                processed++;
                continue;
            }

            var validation = await ValidateCandidateAsync(run, providerSwitch, record, kind, cancellationToken);
            var candidateId = Guid.NewGuid();
            FinanceExternalReference? externalReference = null;
            if (validation.Issues.All(x => !x.IsBlocking))
            {
                externalReference = new FinanceExternalReference(Guid.NewGuid(), run.CompanyId, connection.Id,
                    providerSwitch.SourceProviderKey!, entityType, candidateId, record.SourceIdentity,
                    ReadString(record.NormalizedDataJson, "documentNumber") ?? ReadString(record.NormalizedDataJson, "number"),
                    record.ProviderModifiedUtc, Now());
                externalReference.ReplaceMetadata(new JsonObject
                {
                    ["providerSwitchId"] = run.SwitchId,
                    ["preparationId"] = run.Id,
                    ["candidateId"] = candidateId,
                    ["sourceHash"] = record.SourceHash,
                    ["evidenceHash"] = Hash(record.EvidenceJson)
                }, Now());
                _db.FinanceExternalReferences.Add(externalReference);
            }
            var status = validation.Issues.Any(x => x.IsBlocking)
                ? AccountingProviderSwitchNativeCandidateStatuses.Rejected
                : AccountingProviderSwitchNativeCandidateStatuses.Valid;
            var candidate = new AccountingProviderSwitchNativeCandidate(candidateId, run.CompanyId, run.SwitchId,
                run.Id, record.Id, kind, record.Dataset, record.SourceIdentity, record.SourceVersion,
                record.SourceHash, StableIdempotency(run, record, kind), validation.FiscalPeriodId,
                validation.DocumentDate, validation.PostingDate, record.FinancialAmount, record.Currency,
                status, record.NormalizedDataJson, Hash(record.EvidenceJson), externalReference?.Id, Now());
            _db.AccountingProviderSwitchNativeCandidates.Add(candidate);
            foreach (var issue in validation.Issues)
                _db.AccountingProviderSwitchCandidateValidations.Add(new(run.CompanyId, run.SwitchId,
                    candidate.Id, issue.ReasonCode, issue.IsBlocking, issue.Explanation, issue.EvidenceJson, Now()));
            candidateCount++;
            if (status == AccountingProviderSwitchNativeCandidateStatuses.Valid) validCount++; else rejectedCount++;
            await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
                status == AccountingProviderSwitchNativeCandidateStatuses.Valid
                    ? AuditEventActions.AccountingProviderSwitchNativeCandidateCreated
                    : AuditEventActions.AccountingProviderSwitchNativeCandidateRejected,
                candidate.Id, status == AccountingProviderSwitchNativeCandidateStatuses.Valid
                    ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
                status == AccountingProviderSwitchNativeCandidateStatuses.Valid
                    ? "A non-authoritative native accounting candidate was created through governed validation."
                    : "A native accounting candidate was retained as rejected with actionable validation results.",
                run.CorrelationId, CandidateEvidence(run, record, kind), cancellationToken);
            processed++;
            if (processed % Math.Max(1, _options.SaveBatchSize) == 0) await SaveAsync(cancellationToken);
        }

        foreach (var gap in readiness.UnresolvedGaps.Where(x => !x.IsBlocking && x.ReasonCode.Contains("unsupported", StringComparison.OrdinalIgnoreCase)))
            archiveCount += await RecordArchiveGapAsync(run, gap, cancellationToken);

        run.Complete(processed, candidateCount, validCount, rejectedCount, existingReferenceCount, archiveCount, Now());
        await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
            AuditEventActions.AccountingProviderSwitchPreparationCompleted, run.Id,
            rejectedCount == 0 ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            rejectedCount == 0
                ? "Native target preparation completed without committing authoritative accounting records."
                : "Native target preparation completed with rejected candidates that block activation.",
            run.CorrelationId, new() { ["switchId"] = run.SwitchId.ToString("D"),
                ["candidateCount"] = candidateCount.ToString(), ["validCandidateCount"] = validCount.ToString(),
                ["rejectedCandidateCount"] = rejectedCount.ToString(),
                ["existingReferenceCount"] = existingReferenceCount.ToString(),
                ["archiveDependencyCount"] = archiveCount.ToString() }, cancellationToken);
        await SaveAsync(cancellationToken);
    }

    private async Task<CandidateValidation> ValidateCandidateAsync(AccountingProviderSwitchPreparation run,
        AccountingProviderSwitch providerSwitch, AccountingProviderSwitchStagedRecord record, string kind,
        CancellationToken cancellationToken)
    {
        var issues = new List<ValidationIssue>();
        if (string.IsNullOrWhiteSpace(record.EvidenceJson) || record.EvidenceJson == "{}" && kind == AccountingProviderSwitchNativeCandidateKinds.Document)
            issues.Add(Issue(AccountingProviderSwitchPreparationReasonCodes.CandidateEvidenceMissing,
                "The source document candidate requires hashed evidence before activation.", true,
                new { record.Id, record.Dataset }));

        Guid? fiscalPeriodId = null;
        DateOnly? documentDate = ReadDate(record.NormalizedDataJson, "documentDate") ?? ReadDate(record.NormalizedDataJson, "issueDate");
        DateOnly? postingDate = ReadDate(record.NormalizedDataJson, "postingDate");
        if (kind is AccountingProviderSwitchNativeCandidateKinds.OpeningJournal or AccountingProviderSwitchNativeCandidateKinds.HistoricalJournal)
        {
            if (kind == AccountingProviderSwitchNativeCandidateKinds.OpeningJournal)
            {
                fiscalPeriodId = providerSwitch.EffectiveFiscalPeriodId;
                var effectivePeriod = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.CompanyId == run.CompanyId && x.Id == fiscalPeriodId, cancellationToken);
                postingDate = DateOnly.FromDateTime(effectivePeriod.StartUtc);
                documentDate ??= postingDate;
            }
            else
            {
                if (!postingDate.HasValue)
                    issues.Add(Issue(AccountingProviderSwitchPreparationReasonCodes.CandidateInvalid,
                        "Historical journal candidates must preserve the source posting date.", true,
                        new { record.Id, record.SourceIdentity }));
                else
                    fiscalPeriodId = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
                        .Where(x => x.CompanyId == run.CompanyId && x.StartUtc <= postingDate.Value.ToDateTime(TimeOnly.MinValue) &&
                                    x.EndUtc > postingDate.Value.ToDateTime(TimeOnly.MinValue))
                        .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(cancellationToken);
                if (!fiscalPeriodId.HasValue)
                    issues.Add(Issue(AccountingProviderSwitchPreparationReasonCodes.FiscalPeriodMissing,
                        "No native fiscal period contains the preserved source posting date.", true,
                        new { record.Id, postingDate }));
            }

            var entry = TryBuildJournal(run, record, fiscalPeriodId, documentDate, postingDate, kind, issues);
            if (entry is not null)
            {
                var preview = await _postingService.PreviewNonAuthoritativeCandidateAsync(
                    new(entry), cancellationToken);
                foreach (var postingIssue in preview.Issues)
                    issues.Add(Issue(postingIssue.ReasonCode, postingIssue.Explanation, true,
                        new { postingIssue.SubjectId, preview.DebitTotal, preview.CreditTotal, preview.Difference }));
            }
        }
        else
        {
            ValidateDocumentShape(record, kind, issues);
        }
        return new(fiscalPeriodId, documentDate, postingDate, issues);
    }

    private static ProposedAccountingEntry? TryBuildJournal(AccountingProviderSwitchPreparation run,
        AccountingProviderSwitchStagedRecord record, Guid? fiscalPeriodId, DateOnly? documentDate,
        DateOnly? postingDate, string kind, ICollection<ValidationIssue> issues)
    {
        if (!fiscalPeriodId.HasValue || !postingDate.HasValue || !documentDate.HasValue) return null;
        JsonDocument document;
        try { document = JsonDocument.Parse(record.NormalizedDataJson); }
        catch (JsonException)
        {
            issues.Add(Issue(AccountingProviderSwitchPreparationReasonCodes.CandidateInvalid,
                "The normalized journal payload is not valid JSON.", true, new { record.Id }));
            return null;
        }
        using (document)
        {
            var root = document.RootElement;
            var series = JsonString(root, "voucherSeriesCode");
            if (string.IsNullOrWhiteSpace(series))
                issues.Add(Issue(AccountingProviderSwitchPreparationReasonCodes.VoucherSeriesMissing,
                    "The approved journal mapping must select a target voucher series.", true, new { record.Id }));
            if (!TryProperty(root, "lines", out var lineElement) || lineElement.ValueKind != JsonValueKind.Array)
            {
                issues.Add(Issue(AccountingProviderSwitchPreparationReasonCodes.CandidateInvalid,
                    "The normalized journal must contain mapped debit and credit lines.", true, new { record.Id }));
                return null;
            }
            var lines = new List<ProposedAccountingLine>();
            foreach (var line in lineElement.EnumerateArray())
            {
                var accountId = JsonGuid(line, "financeAccountId");
                var debit = JsonDecimal(line, "debitAmount") ?? JsonDecimal(line, "debit") ?? 0m;
                var credit = JsonDecimal(line, "creditAmount") ?? JsonDecimal(line, "credit") ?? 0m;
                lines.Add(new(accountId ?? Guid.Empty, debit, credit,
                    JsonString(line, "currency") ?? record.Currency ?? string.Empty,
                    JsonString(line, "description"), JsonGuid(line, "costCenterId"),
                    JsonDictionary(line, "taxFacts"), JsonDictionary(line, "dimensionFacts")));
            }
            if (string.IsNullOrWhiteSpace(series)) return null;
            var postingType = kind == AccountingProviderSwitchNativeCandidateKinds.OpeningJournal
                ? LedgerPostingTypeValues.SourceDocument
                : JsonString(root, "postingType") ?? string.Empty;
            return new(run.CompanyId, fiscalPeriodId.Value, series, documentDate.Value, postingDate.Value,
                postingType, JsonString(root, "description") ?? "Provider migration candidate",
                "provider_switch_candidate", record.Id.ToString("N"), record.SourceVersion,
                StableIdempotency(run, record, kind), lines, Guid.Empty, RequiresApproval: false,
                PolicyFacts: new Dictionary<string, string> { ["providerSwitchId"] = run.SwitchId.ToString("D"),
                    ["sourceHash"] = record.SourceHash, ["candidateKind"] = kind },
                ActorType: AuditActorTypes.System);
        }
    }

    private static void ValidateDocumentShape(AccountingProviderSwitchStagedRecord record, string kind,
        ICollection<ValidationIssue> issues)
    {
        if (kind == AccountingProviderSwitchNativeCandidateKinds.ExternalReference)
            issues.Add(Issue(AccountingProviderSwitchPreparationReasonCodes.CandidateInvalid,
                "The normalized source record does not identify the native customer, supplier, receivable, or payable boundary.",
                true, new { record.Id, record.Dataset, record.SourceIdentity }));
        var required = kind switch
        {
            AccountingProviderSwitchNativeCandidateKinds.Customer or AccountingProviderSwitchNativeCandidateKinds.Supplier => new[] { "name" },
            AccountingProviderSwitchNativeCandidateKinds.CustomerInvoice or AccountingProviderSwitchNativeCandidateKinds.SupplierBill or AccountingProviderSwitchNativeCandidateKinds.Credit => new[] { "documentNumber", "issueDate", "currency" },
            AccountingProviderSwitchNativeCandidateKinds.Payment => new[] { "paymentDate", "currency" },
            AccountingProviderSwitchNativeCandidateKinds.Allocation => new[] { "paymentSourceIdentity", "documentSourceIdentity" },
            AccountingProviderSwitchNativeCandidateKinds.Document => new[] { "contentHash" },
            AccountingProviderSwitchNativeCandidateKinds.BankState => new[] { "accountSourceIdentity", "currency" },
            _ => []
        };
        foreach (var property in required.Where(property => string.IsNullOrWhiteSpace(ReadString(record.NormalizedDataJson, property))))
            issues.Add(Issue(AccountingProviderSwitchPreparationReasonCodes.CandidateInvalid,
                $"The normalized {kind.Replace('_', ' ')} candidate must preserve '{property}'.", true,
                new { record.Id, property }));
        if (kind is AccountingProviderSwitchNativeCandidateKinds.CustomerInvoice or
            AccountingProviderSwitchNativeCandidateKinds.SupplierBill or
            AccountingProviderSwitchNativeCandidateKinds.Credit or
            AccountingProviderSwitchNativeCandidateKinds.Payment && record.FinancialAmount == 0m)
            issues.Add(Issue(AccountingProviderSwitchPreparationReasonCodes.CandidateInvalid,
                "The normalized financial document must preserve its non-zero source amount.", true,
                new { record.Id, record.FinancialAmount }));
    }

    private async Task<int> RecordArchiveDependencyAsync(AccountingProviderSwitchPreparation run,
        AccountingProviderSwitchStagedRecord record, string reasonCode, string explanation,
        CancellationToken cancellationToken)
    {
        var exists = await _db.AccountingProviderSwitchArchiveDependencies.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId &&
                           x.Dataset == record.Dataset && x.SourceIdentity == record.SourceIdentity &&
                           x.ReasonCode == reasonCode, cancellationToken);
        if (exists) return 1;
        var dependency = new AccountingProviderSwitchArchiveDependency(run.CompanyId, run.SwitchId, run.Id,
            record.Id, record.Dataset, record.SourceIdentity, reasonCode, explanation, Hash(record.EvidenceJson),
            run.PlanId, run.PlanHash, Now());
        _db.AccountingProviderSwitchArchiveDependencies.Add(dependency);
        await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
            AuditEventActions.AccountingProviderSwitchArchiveDependencyRecorded, dependency.Id,
            AuditEventOutcomes.Succeeded, explanation, run.CorrelationId,
            new() { ["switchId"] = run.SwitchId.ToString("D"), ["dataset"] = record.Dataset,
                ["sourceIdentity"] = record.SourceIdentity, ["planHash"] = run.PlanHash }, cancellationToken);
        return 1;
    }

    private async Task<int> RecordArchiveGapAsync(AccountingProviderSwitchPreparation run,
        AccountingProviderSwitchGapDto gap, CancellationToken cancellationToken)
    {
        var dataset = gap.DatasetKey ?? "provider_capability";
        var exists = await _db.AccountingProviderSwitchArchiveDependencies.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.Dataset == dataset &&
                           x.SourceIdentity == gap.ReasonCode && x.ReasonCode == gap.ReasonCode, cancellationToken);
        if (exists) return 1;
        var dependency = new AccountingProviderSwitchArchiveDependency(run.CompanyId, run.SwitchId, run.Id,
            null, dataset, gap.ReasonCode, gap.ReasonCode, gap.Explanation, Hash(gap.EvidenceJson), run.PlanId,
            run.PlanHash, Now());
        _db.AccountingProviderSwitchArchiveDependencies.Add(dependency);
        await WriteAuditAsync(run.CompanyId, run.RequestedByUserId,
            AuditEventActions.AccountingProviderSwitchArchiveDependencyRecorded, dependency.Id,
            AuditEventOutcomes.Succeeded, gap.Explanation, run.CorrelationId,
            new() { ["switchId"] = run.SwitchId.ToString("D"), ["dataset"] = dataset,
                ["reasonCode"] = gap.ReasonCode, ["planHash"] = run.PlanHash }, cancellationToken);
        return 1;
    }

    private async Task<AccountingProviderSwitchPreparationDto> ToDtoAsync(AccountingProviderSwitchPreparation run,
        CancellationToken cancellationToken)
    {
        var readiness = await _readinessPolicy.EvaluateAsync(new(run.CompanyId, run.SwitchId, run.PlanId), cancellationToken);
        var candidates = await _db.AccountingProviderSwitchNativeCandidates.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.PreparedByRunId == run.Id)
            .OrderBy(x => x.CandidateKind).ThenBy(x => x.SourceIdentity).ToListAsync(cancellationToken);
        var candidateDtos = await CandidateDtosAsync(candidates, cancellationToken);
        var archive = await _db.AccountingProviderSwitchArchiveDependencies.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == run.CompanyId && x.SwitchId == run.SwitchId && x.PreparedByRunId == run.Id)
            .OrderBy(x => x.Dataset).ThenBy(x => x.SourceIdentity).ToListAsync(cancellationToken);
        var activationReady = run.Status == AccountingProviderSwitchPreparationStatuses.Completed &&
                              run.RejectedCandidateCount == 0 && readiness.IsReady && readiness.PlanHash == run.PlanHash;
        return new(run.Id, run.CompanyId, run.SwitchId, run.PlanId, run.PlanHash, run.Strategy, run.Status,
            run.CompletedWorkItems, run.TotalWorkItems,
            run.TotalWorkItems == 0 ? 0 : (int)Math.Floor(run.CompletedWorkItems * 100m / run.TotalWorkItems),
            run.CandidateCount, run.ValidCandidateCount, run.RejectedCandidateCount, run.ExistingReferenceCount,
            run.ArchiveDependencyCount, run.AttemptCount, run.NextAttemptUtc, run.FailureCode, run.FailureSummary,
            run.RequestedUtc, run.StartedUtc, run.CompletedUtc, run.Version, activationReady,
            activationReady
                ? "All prepared native candidates are valid and bound to the current approved plan. Accounting authority is still external."
                : run.RejectedCandidateCount > 0
                    ? $"{run.RejectedCandidateCount} rejected native candidate(s) block activation."
                    : FirstBlocking(readiness) ?? "Preparation has not completed successfully.",
            readiness, candidateDtos, archive.Select(x => new AccountingProviderSwitchArchiveDependencyDto(x.Id,
                x.CompanyId, x.SwitchId, x.PreparedByRunId, x.StagedRecordId, x.Dataset, x.SourceIdentity,
                x.ReasonCode, x.Explanation, x.EvidenceHash, x.ApprovedPlanId, x.ApprovedPlanHash,
                x.CreatedUtc)).ToArray());
    }

    private async Task<IReadOnlyList<AccountingProviderSwitchNativeCandidateDto>> CandidateDtosAsync(
        IReadOnlyList<AccountingProviderSwitchNativeCandidate> candidates, CancellationToken cancellationToken)
    {
        var ids = candidates.Select(x => x.Id).ToArray();
        var validations = await _db.AccountingProviderSwitchCandidateValidations.IgnoreQueryFilters().AsNoTracking()
            .Where(x => ids.Contains(x.CandidateId)).OrderByDescending(x => x.IsBlocking).ThenBy(x => x.ReasonCode)
            .ToListAsync(cancellationToken);
        var byCandidate = validations.GroupBy(x => x.CandidateId).ToDictionary(x => x.Key, x => x.ToArray());
        return candidates.Select(x => new AccountingProviderSwitchNativeCandidateDto(x.Id, x.CompanyId,
            x.SwitchId, x.PreparedByRunId, x.StagedRecordId, x.CandidateKind, x.SourceDataset,
            x.SourceIdentity, x.SourceVersion, x.SourceHash, x.IdempotencyKey, x.FiscalPeriodId,
            x.DocumentDate, x.PostingDate, x.FinancialAmount, x.Currency, x.Status, x.PayloadJson,
            x.EvidenceHash, x.ExternalReferenceId, x.CreatedUtc, x.UpdatedUtc,
            byCandidate.GetValueOrDefault(x.Id, []).Select(v => new AccountingProviderSwitchCandidateValidationDto(
                v.Id, v.ReasonCode, v.IsBlocking, v.Explanation, v.EvidenceJson, v.ValidatedUtc)).ToArray())).ToArray();
    }

    private IQueryable<AccountingProviderSwitchPreparation> Preparations(Guid companyId, Guid switchId, bool tracking)
    {
        var query = _db.AccountingProviderSwitchPreparations.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.SwitchId == switchId);
        return tracking ? query : query.AsNoTracking();
    }

    private Task<AccountingProviderSwitch> SwitchAsync(Guid companyId, Guid switchId, bool tracking,
        CancellationToken cancellationToken)
    {
        var query = _db.AccountingProviderSwitches.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Id == switchId);
        return (tracking ? query : query.AsNoTracking()).SingleOrDefaultAsync(cancellationToken)
            .ContinueWith(task => task.Result ?? throw Error(AccountingProviderSwitchReasonCodes.NotFound,
                "The accounting-system switch was not found for this company."), cancellationToken,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private static bool TransfersToTarget(AccountingProviderSwitchStagedRecord record) => record.Disposition is not
        (AccountingProviderSwitchDispositions.Duplicate or AccountingProviderSwitchDispositions.ExcludedWithApproval or
         AccountingProviderSwitchDispositions.Missing or AccountingProviderSwitchDispositions.Unsupported or
         AccountingProviderSwitchDispositions.Conflicting or AccountingProviderSwitchDispositions.AwaitingEvidence or
         AccountingProviderSwitchDispositions.Blocked);

    private static bool IncludedByStrategy(string strategy, string dataset) => strategy switch
    {
        AccountingProviderSwitchStrategies.OpeningBalancesAndOpenItems => dataset is not
            (AccountingProviderSwitchStagingDatasets.Journals or AccountingProviderSwitchStagingDatasets.JournalLines),
        AccountingProviderSwitchStrategies.CurrentFiscalYear or AccountingProviderSwitchStrategies.FullHistory => true,
        _ => false
    };

    private static string? ResolveKind(AccountingProviderSwitchStagedRecord record) => record.Dataset switch
    {
        AccountingProviderSwitchStagingDatasets.OpeningBalanceCandidates => AccountingProviderSwitchNativeCandidateKinds.OpeningJournal,
        AccountingProviderSwitchStagingDatasets.Journals => AccountingProviderSwitchNativeCandidateKinds.HistoricalJournal,
        AccountingProviderSwitchStagingDatasets.Counterparties => ReadString(record.NormalizedDataJson, "counterpartyType")?.ToLowerInvariant() switch
        {
            "customer" => AccountingProviderSwitchNativeCandidateKinds.Customer,
            "supplier" => AccountingProviderSwitchNativeCandidateKinds.Supplier,
            _ => AccountingProviderSwitchNativeCandidateKinds.ExternalReference
        },
        AccountingProviderSwitchStagingDatasets.Invoices or AccountingProviderSwitchStagingDatasets.OpenItems =>
            ResolveDocumentKind(record.NormalizedDataJson),
        AccountingProviderSwitchStagingDatasets.Credits => AccountingProviderSwitchNativeCandidateKinds.Credit,
        AccountingProviderSwitchStagingDatasets.Payments => AccountingProviderSwitchNativeCandidateKinds.Payment,
        AccountingProviderSwitchStagingDatasets.Allocations => AccountingProviderSwitchNativeCandidateKinds.Allocation,
        AccountingProviderSwitchStagingDatasets.BankState => AccountingProviderSwitchNativeCandidateKinds.BankState,
        AccountingProviderSwitchStagingDatasets.Documents => AccountingProviderSwitchNativeCandidateKinds.Document,
        _ => null
    };

    private static string ExternalEntityType(string kind) => kind switch
    {
        AccountingProviderSwitchNativeCandidateKinds.Customer => "customer",
        AccountingProviderSwitchNativeCandidateKinds.Supplier => "supplier",
        AccountingProviderSwitchNativeCandidateKinds.CustomerInvoice => "invoice",
        AccountingProviderSwitchNativeCandidateKinds.SupplierBill => "supplier_invoice",
        AccountingProviderSwitchNativeCandidateKinds.Credit => "credit",
        AccountingProviderSwitchNativeCandidateKinds.Payment => "payment",
        AccountingProviderSwitchNativeCandidateKinds.Allocation => "payment_allocation",
        AccountingProviderSwitchNativeCandidateKinds.OpeningJournal or AccountingProviderSwitchNativeCandidateKinds.HistoricalJournal => "voucher",
        AccountingProviderSwitchNativeCandidateKinds.Document => "document",
        AccountingProviderSwitchNativeCandidateKinds.BankState => "bank_state",
        _ => "migration_candidate"
    };

    private static bool IsSupplier(string json) =>
        string.Equals(ReadString(json, "counterpartyType"), "supplier", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ReadString(json, "documentType"), "supplier_invoice", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ReadString(json, "openItemType"), "payable", StringComparison.OrdinalIgnoreCase);

    private static string ResolveDocumentKind(string json)
    {
        if (IsSupplier(json)) return AccountingProviderSwitchNativeCandidateKinds.SupplierBill;
        var type = ReadString(json, "counterpartyType") ?? ReadString(json, "documentType") ??
                   ReadString(json, "openItemType");
        return type?.ToLowerInvariant() switch
        {
            "customer" or "customer_invoice" or "invoice" or "receivable" =>
                AccountingProviderSwitchNativeCandidateKinds.CustomerInvoice,
            _ => AccountingProviderSwitchNativeCandidateKinds.ExternalReference
        };
    }

    private static Dictionary<string, string?> CandidateEvidence(AccountingProviderSwitchPreparation run,
        AccountingProviderSwitchStagedRecord record, string kind) => new()
    {
        ["switchId"] = run.SwitchId.ToString("D"), ["preparationId"] = run.Id.ToString("D"),
        ["planHash"] = run.PlanHash, ["candidateKind"] = kind, ["dataset"] = record.Dataset,
        ["sourceIdentity"] = record.SourceIdentity, ["sourceVersion"] = record.SourceVersion,
        ["sourceHash"] = record.SourceHash, ["evidenceHash"] = Hash(record.EvidenceJson)
    };

    private static string StableIdempotency(AccountingProviderSwitchPreparation run,
        AccountingProviderSwitchStagedRecord record, string kind) =>
        $"provider-switch:{run.SwitchId:N}:{kind}:{Hash($"{record.SourceIdentity}|{record.SourceVersion}|{record.SourceHash}")[..32]}";

    private static string? ReadString(string json, string property)
    {
        try { using var document = JsonDocument.Parse(json); return JsonString(document.RootElement, property); }
        catch (JsonException) { return null; }
    }
    private static DateOnly? ReadDate(string json, string property) =>
        DateOnly.TryParse(ReadString(json, property), out var value) ? value : null;
    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            { value = property.Value; return true; }
        value = default; return false;
    }
    private static string? JsonString(JsonElement element, string property) =>
        TryProperty(element, property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static Guid? JsonGuid(JsonElement element, string property) =>
        Guid.TryParse(JsonString(element, property), out var value) ? value : null;
    private static decimal? JsonDecimal(JsonElement element, string property) =>
        TryProperty(element, property, out var value) && value.TryGetDecimal(out var number) ? number : null;
    private static IReadOnlyDictionary<string, string>? JsonDictionary(JsonElement element, string property)
    {
        if (!TryProperty(element, property, out var value) || value.ValueKind != JsonValueKind.Object) return null;
        return value.EnumerateObject().Where(x => x.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(x => x.Name, x => x.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private static ValidationIssue Issue(string code, string explanation, bool blocking, object evidence) =>
        new(code, blocking, explanation, JsonSerializer.Serialize(evidence));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string? FirstBlocking(AccountingProviderSwitchInternalReadinessDto readiness) =>
        readiness.Checks.FirstOrDefault(x => x.IsBlocking && !x.IsReady)?.Explanation;
    private DateTime Now() => _time.GetUtcNow().UtcDateTime;
    private static string Safe(string text) => string.IsNullOrWhiteSpace(text) ? "Native target preparation failed safely."
        : text.Length <= 1000 ? text : text[..1000];
    private static string Required(string? value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static void Validate(Guid companyId, Guid switchId, Guid actor, string correlation)
    {
        if (companyId == Guid.Empty || switchId == Guid.Empty || actor == Guid.Empty)
            throw new ArgumentException("Company, accounting-system switch, and actor are required.");
        Required(correlation, nameof(correlation), 128);
    }
    private static void EnsureVersion(AccountingProviderSwitch providerSwitch, long version)
    {
        if (providerSwitch.Version != version)
            throw Conflict(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
                "The accounting-system switch changed while preparation was being reviewed.");
    }
    private static void EnsurePreparationState(AccountingProviderSwitch providerSwitch)
    {
        if (providerSwitch.Status is not (AccountingProviderSwitchStatuses.ReadyForPlanning or
            AccountingProviderSwitchStatuses.PlanAwaitingApproval or AccountingProviderSwitchStatuses.PreparingTarget))
            throw Conflict(AccountingProviderSwitchPreparationReasonCodes.NotReady,
                "Native target preparation is available only after planning and before rehearsal completion.");
    }
    private Task WriteAuditAsync(Guid companyId, Guid actor, string action, Guid target, string outcome,
        string summary, string correlation, Dictionary<string, string?> evidence, CancellationToken cancellationToken) =>
        _audit.WriteAsync(new(companyId, AuditActorTypes.User, actor, action,
            "accounting_provider_switch_preparation", target.ToString("D"), outcome, summary,
            ["accounting_provider_switch", "native_preparation", "non_authoritative"], evidence,
            correlation, Now()), cancellationToken);
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Conflict(AccountingProviderSwitchPreparationReasonCodes.ConcurrencyConflict,
            "Target preparation changed concurrently. Retry with current data."); }
    }
    private static AccountingAuthorityException Error(string code, string message) => new(code, message);
    private static AccountingAuthorityException Conflict(string code, string message) => new(code, message, true);
    private sealed record ValidationIssue(string ReasonCode, bool IsBlocking, string Explanation, string EvidenceJson);
    private sealed record CandidateValidation(Guid? FiscalPeriodId, DateOnly? DocumentDate,
        DateOnly? PostingDate, IReadOnlyList<ValidationIssue> Issues);
}
