using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchAssessmentService :
    IAccountingProviderSwitchAssessmentService,
    IAccountingProviderSwitchAssessmentJobRunner
{
    private static readonly Meter Meter = new("VirtualCompany.Finance.ProviderSwitchAssessment", "1.0.0");
    private static readonly Counter<long> CompletedCounter = Meter.CreateCounter<long>("accounting_provider_switch_assessments_completed");
    private static readonly Counter<long> FailureCounter = Meter.CreateCounter<long>("accounting_provider_switch_assessments_failed");
    private static readonly Histogram<double> DurationHistogram = Meter.CreateHistogram<double>("accounting_provider_switch_assessment_duration_ms");
    private static readonly Histogram<long> DatasetCountHistogram = Meter.CreateHistogram<long>("accounting_provider_switch_dataset_records");
    private static readonly Histogram<int> BlockingGapHistogram = Meter.CreateHistogram<int>("accounting_provider_switch_blocking_gaps");
    private const int WorkItemCount = 2 * (1 + 17) + 1;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAccountingProviderSwitchAdapterResolver _adapterResolver;
    private readonly IAccountingProviderSwitchGapPolicy _gapPolicy;
    private readonly IAccountingProviderSwitchService _switchService;
    private readonly IAuditEventWriter _auditWriter;
    private readonly IOptions<AccountingProviderSwitchAssessmentWorkerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountingProviderSwitchAssessmentService> _logger;

    public AccountingProviderSwitchAssessmentService(
        VirtualCompanyDbContext dbContext,
        IAccountingProviderSwitchAdapterResolver adapterResolver,
        IAccountingProviderSwitchGapPolicy gapPolicy,
        IAccountingProviderSwitchService switchService,
        IAuditEventWriter auditWriter,
        IOptions<AccountingProviderSwitchAssessmentWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<AccountingProviderSwitchAssessmentService> logger)
    {
        _dbContext = dbContext;
        _adapterResolver = adapterResolver;
        _gapPolicy = gapPolicy;
        _switchService = switchService;
        _auditWriter = auditWriter;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AccountingProviderSwitchAssessmentDto> StartAsync(
        StartAccountingProviderSwitchAssessmentCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId, command.IdempotencyKey);
        var duplicate = await Assessments(command.CompanyId).AsNoTracking()
            .FirstOrDefaultAsync(x => x.SwitchId == command.SwitchId && x.IdempotencyKey == command.IdempotencyKey.Trim(), cancellationToken);
        if (duplicate is not null) return await GetAsync(new(command.CompanyId, command.SwitchId, duplicate.Id), cancellationToken);

        var providerSwitch = await _switchService.GetAsync(new(command.CompanyId, command.SwitchId), cancellationToken);
        if (providerSwitch.Version != command.ExpectedSwitchVersion)
            throw Conflict(AccountingProviderSwitchReasonCodes.AssessmentStaleVersion, "The accounting-system switch changed. Refresh before starting assessment.");
        if (providerSwitch.Status == AccountingProviderSwitchStatuses.Draft)
        {
            providerSwitch = await _switchService.TransitionAsync(new(command.CompanyId, command.SwitchId,
                AccountingProviderSwitchStatuses.Assessing, command.ExpectedSwitchVersion, command.ActorUserId,
                command.CorrelationId), cancellationToken);
        }
        else if (providerSwitch.Status != AccountingProviderSwitchStatuses.Assessing)
            throw Conflict(AccountingProviderSwitchReasonCodes.AssessmentUnavailable, "Assessment can only start while the switch is in preparation.");

        var now = UtcNow();
        var assessment = new AccountingProviderSwitchAssessment(Guid.NewGuid(), command.CompanyId, command.SwitchId,
            command.ActorUserId, command.IdempotencyKey, command.CorrelationId, WorkItemCount, now);
        _dbContext.AccountingProviderSwitchAssessments.Add(assessment);
        await WriteAuditAsync(assessment, command.ActorUserId, AuditEventActions.AccountingProviderSwitchAssessmentRequested,
            AuditEventOutcomes.Started, "A read-only accounting-system assessment was queued.", null, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, command.SwitchId, assessment.Id), cancellationToken);
    }

    public async Task<AccountingProviderSwitchAssessmentDto> ReplayAsync(
        ReplayAccountingProviderSwitchAssessmentCommand command, CancellationToken cancellationToken)
    {
        Validate(command.CompanyId, command.SwitchId, command.ActorUserId, command.CorrelationId, command.IdempotencyKey);
        var prior = await Assessments(command.CompanyId).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == command.AssessmentId && x.SwitchId == command.SwitchId, cancellationToken)
            ?? throw NotFound();
        if (prior.Status is not (AccountingProviderSwitchAssessmentStatuses.Completed or AccountingProviderSwitchAssessmentStatuses.Failed))
            throw Conflict(AccountingProviderSwitchReasonCodes.AssessmentReplayUnavailable, "Only a completed or failed assessment can be replayed.");

        var duplicate = await Assessments(command.CompanyId).AsNoTracking()
            .FirstOrDefaultAsync(x => x.SwitchId == command.SwitchId && x.IdempotencyKey == command.IdempotencyKey.Trim(), cancellationToken);
        if (duplicate is not null) return await GetAsync(new(command.CompanyId, command.SwitchId, duplicate.Id), cancellationToken);

        var providerSwitch = await _switchService.GetAsync(new(command.CompanyId, command.SwitchId), cancellationToken);
        if (providerSwitch.Version != command.ExpectedSwitchVersion)
            throw Conflict(AccountingProviderSwitchReasonCodes.AssessmentStaleVersion, "The accounting-system switch changed. Refresh before replaying assessment.");
        if (!AccountingProviderSwitchStatuses.IsPreActivation(providerSwitch.Status, providerSwitch.BlockedFromStatus))
            throw Conflict(AccountingProviderSwitchReasonCodes.AssessmentReplayUnavailable, "Assessment cannot be replayed after target activation has begun.");

        var assessment = new AccountingProviderSwitchAssessment(Guid.NewGuid(), command.CompanyId, command.SwitchId,
            command.ActorUserId, command.IdempotencyKey, command.CorrelationId, WorkItemCount, UtcNow());
        _dbContext.AccountingProviderSwitchAssessments.Add(assessment);
        await WriteAuditAsync(assessment, command.ActorUserId, AuditEventActions.AccountingProviderSwitchAssessmentRequested,
            AuditEventOutcomes.Started, "A read-only accounting-system assessment replay was queued.",
            new Dictionary<string, string?> { ["replayedAssessmentId"] = prior.Id.ToString("D") }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetAsync(new(command.CompanyId, command.SwitchId, assessment.Id), cancellationToken);
    }

    public async Task<AccountingProviderSwitchAssessmentDto> GetAsync(
        GetAccountingProviderSwitchAssessmentQuery query, CancellationToken cancellationToken)
    {
        if (query.CompanyId == Guid.Empty || query.SwitchId == Guid.Empty) throw new ArgumentException("Company and switch are required.");
        var baseQuery = Assessments(query.CompanyId).AsNoTracking().Where(x => x.SwitchId == query.SwitchId);
        var assessment = query.AssessmentId.HasValue
            ? await baseQuery.SingleOrDefaultAsync(x => x.Id == query.AssessmentId.Value, cancellationToken)
            : await baseQuery.OrderByDescending(x => x.RequestedUtc).FirstOrDefaultAsync(cancellationToken);
        if (assessment is null) throw NotFound();

        var capabilities = await _dbContext.AccountingProviderSwitchCapabilities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId && x.AssessmentId == assessment.Id)
            .OrderBy(x => x.EndpointRole).ThenBy(x => x.CapabilityKey)
            .Select(x => new AccountingProviderSwitchCapabilityDto(x.EndpointRole, x.CapabilityKey, x.Level, x.Explanation, x.RequiredScope, x.ObservedUtc))
            .ToArrayAsync(cancellationToken);
        var datasets = await _dbContext.AccountingProviderSwitchDatasets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId && x.AssessmentId == assessment.Id)
            .OrderBy(x => x.EndpointRole).ThenBy(x => x.DatasetKey)
            .Select(x => new AccountingProviderSwitchDatasetDto(x.EndpointRole, x.DatasetKey, x.Availability, x.CapabilityLevel,
                x.RecordCount, x.FinancialTotal, x.Currency, x.SourceCursor, x.SourceVersion, x.IntegrityHash,
                x.EvidenceJson, x.FailureCode, x.FailureSummary, x.ExtractedUtc))
            .ToArrayAsync(cancellationToken);
        var gaps = await _dbContext.AccountingProviderSwitchGaps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.SwitchId == query.SwitchId && x.AssessmentId == assessment.Id)
            .OrderByDescending(x => x.IsBlocking).ThenBy(x => x.Category)
            .Select(x => new AccountingProviderSwitchGapDto(x.Id, x.Category, x.DatasetKey, x.Severity, x.IsBlocking,
                x.ReasonCode, x.Explanation, x.EvidenceJson, x.OperatorAction, x.CreatedUtc))
            .ToArrayAsync(cancellationToken);
        var blocking = gaps.Any(x => x.IsBlocking);
        var allowed = assessment.Status switch
        {
            AccountingProviderSwitchAssessmentStatuses.Completed when blocking => ("resolve_gaps", "Resolve blocking assessment gaps and replay before planning the migration."),
            AccountingProviderSwitchAssessmentStatuses.Completed => ("continue_planning", "The assessment is complete with no blocking gap."),
            AccountingProviderSwitchAssessmentStatuses.Failed => ("replay_assessment", "Correct the safe failure and replay the assessment."),
            _ => ("wait_for_assessment", "The durable read-only assessment is still in progress.")
        };
        return new AccountingProviderSwitchAssessmentDto(assessment.Id, assessment.CompanyId, assessment.SwitchId,
            assessment.Status, assessment.WorkIndex, assessment.TotalWorkItems,
            (int)Math.Floor(assessment.WorkIndex * 100d / assessment.TotalWorkItems), assessment.AttemptCount,
            assessment.NextAttemptUtc, assessment.FailureCode, assessment.FailureSummary, assessment.RequestedUtc,
            assessment.StartedUtc, assessment.CompletedUtc, capabilities, datasets, gaps, blocking, allowed.Item1, allowed.Item2);
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var candidates = await _dbContext.AccountingProviderSwitchAssessments.IgnoreQueryFilters().AsNoTracking()
            .Where(x => (x.Status == AccountingProviderSwitchAssessmentStatuses.Queued &&
                         (x.NextAttemptUtc == null || x.NextAttemptUtc <= now)) ||
                        (x.Status == AccountingProviderSwitchAssessmentStatuses.Running && x.LeaseExpiresUtc <= now))
            .OrderBy(x => x.RequestedUtc).Select(x => new
            {
                x.Id,
                IsExpiredLease = x.Status == AccountingProviderSwitchAssessmentStatuses.Running
            })
            .Take(Math.Clamp(_options.Value.ClaimBatchSize, 1, 20)).ToArrayAsync(cancellationToken);
        var handled = 0;
        foreach (var candidate in candidates)
        {
            var leaseOwner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
            var leaseExpiry = now.AddSeconds(Math.Max(15, _options.Value.LeaseSeconds));
            var claimed = await _dbContext.AccountingProviderSwitchAssessments.IgnoreQueryFilters()
                .Where(x => x.Id == candidate.Id && ((x.Status == AccountingProviderSwitchAssessmentStatuses.Queued &&
                    (x.NextAttemptUtc == null || x.NextAttemptUtc <= now)) ||
                    (x.Status == AccountingProviderSwitchAssessmentStatuses.Running && x.LeaseExpiresUtc <= now)))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, AccountingProviderSwitchAssessmentStatuses.Running)
                    .SetProperty(x => x.LeaseOwner, leaseOwner)
                    .SetProperty(x => x.LeaseExpiresUtc, leaseExpiry)
                    .SetProperty(x => x.StartedUtc, x => x.StartedUtc ?? now)
                    .SetProperty(x => x.AttemptCount, x => candidate.IsExpiredLease ? x.AttemptCount + 1 : x.AttemptCount)
                    .SetProperty(x => x.UpdatedUtc, now)
                    .SetProperty(x => x.Version, x => x.Version + 1), cancellationToken);
            if (claimed == 0) continue;
            handled++;
            _dbContext.ChangeTracker.Clear();
            var assessment = await _dbContext.AccountingProviderSwitchAssessments.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == candidate.Id && x.LeaseOwner == leaseOwner, cancellationToken);
            if (candidate.IsExpiredLease && assessment.AttemptCount >= Math.Max(1, _options.Value.MaximumAttempts))
            {
                assessment.Fail("assessment_lease_recovery_exhausted",
                    "The assessment stopped after repeated worker lease expiry. Review worker health and replay it.",
                    assessment.AttemptCount, now);
                await WriteAuditAsync(assessment, null, AuditEventActions.AccountingProviderSwitchAssessmentFailed,
                    AuditEventOutcomes.Blocked, assessment.FailureSummary!,
                    new Dictionary<string, string?> { ["failureCode"] = assessment.FailureCode }, cancellationToken);
                FailureCounter.Add(1, new KeyValuePair<string, object?>("failure_code", assessment.FailureCode));
                await _dbContext.SaveChangesAsync(cancellationToken);
                continue;
            }
            try
            {
                await ProcessWorkItemAsync(assessment, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                await HandleFailureAsync(assessment, exception, cancellationToken);
            }
        }
        return handled;
    }

    private async Task ProcessWorkItemAsync(AccountingProviderSwitchAssessment assessment, CancellationToken cancellationToken)
    {
        var providerSwitch = await _dbContext.AccountingProviderSwitches.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == assessment.CompanyId && x.Id == assessment.SwitchId, cancellationToken)
            ?? throw new InvalidOperationException("The accounting-system switch no longer exists for this company.");
        var source = Endpoint(providerSwitch.Source);
        var target = Endpoint(providerSwitch.Target);
        bool completed;
        if (assessment.WorkIndex == 0)
            completed = await ExtractCapabilitiesAsync(assessment, source, AccountingProviderSwitchEndpointRoles.Source, cancellationToken);
        else if (assessment.WorkIndex <= AccountingProviderSwitchDatasetKeys.All.Length)
            completed = await ExtractDatasetAsync(assessment, source, AccountingProviderSwitchEndpointRoles.Source,
                AccountingProviderSwitchDatasetKeys.All[assessment.WorkIndex - 1], cancellationToken);
        else if (assessment.WorkIndex == AccountingProviderSwitchDatasetKeys.All.Length + 1)
            completed = await ExtractCapabilitiesAsync(assessment, target, AccountingProviderSwitchEndpointRoles.Target, cancellationToken);
        else if (assessment.WorkIndex <= 2 * AccountingProviderSwitchDatasetKeys.All.Length + 1)
            completed = await ExtractDatasetAsync(assessment, target, AccountingProviderSwitchEndpointRoles.Target,
                AccountingProviderSwitchDatasetKeys.All[assessment.WorkIndex - AccountingProviderSwitchDatasetKeys.All.Length - 2], cancellationToken);
        else
            completed = await PersistGapsAsync(assessment, providerSwitch.MigrationStrategy, cancellationToken);

        var now = UtcNow();
        if (completed && assessment.WorkIndex + 1 >= assessment.TotalWorkItems)
        {
            assessment.Complete(now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await CompleteAsync(assessment, providerSwitch, cancellationToken);
        }
        else
        {
            assessment.Continue(completed, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<bool> ExtractCapabilitiesAsync(AccountingProviderSwitchAssessment assessment,
        AccountingProviderSwitchEndpointDto endpoint, string role, CancellationToken cancellationToken)
    {
        var profile = await _adapterResolver.GetRequired(endpoint.Kind, endpoint.ProviderKey)
            .GetCapabilityProfileAsync(assessment.CompanyId, endpoint, assessment.CorrelationId, cancellationToken);
        var existing = await _dbContext.AccountingProviderSwitchCapabilities.IgnoreQueryFilters()
            .Where(x => x.CompanyId == assessment.CompanyId && x.AssessmentId == assessment.Id && x.EndpointRole == role)
            .ToDictionaryAsync(x => x.CapabilityKey, cancellationToken);
        foreach (var item in profile.Capabilities)
        {
            if (existing.TryGetValue(item.Key, out var capability))
                capability.Replace(item.Level, item.Explanation, item.RequiredScope, profile.ObservedUtc);
            else
                _dbContext.AccountingProviderSwitchCapabilities.Add(new(assessment.CompanyId, assessment.SwitchId,
                    assessment.Id, role, item.Key, item.Level, item.Explanation, item.RequiredScope, profile.ObservedUtc));
        }
        return true;
    }

    private async Task<bool> ExtractDatasetAsync(AccountingProviderSwitchAssessment assessment,
        AccountingProviderSwitchEndpointDto endpoint, string role, string datasetKey, CancellationToken cancellationToken)
    {
        var dataset = await _dbContext.AccountingProviderSwitchDatasets.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == assessment.CompanyId && x.AssessmentId == assessment.Id &&
                x.EndpointRole == role && x.DatasetKey == datasetKey, cancellationToken);
        dataset ??= new AccountingProviderSwitchDataset(assessment.CompanyId, assessment.SwitchId, assessment.Id, role, datasetKey, UtcNow());
        if (_dbContext.Entry(dataset).State == EntityState.Detached) _dbContext.AccountingProviderSwitchDatasets.Add(dataset);

        var result = await _adapterResolver.GetRequired(endpoint.Kind, endpoint.ProviderKey).ExtractInventoryAsync(
            new(assessment.CompanyId, assessment.SwitchId, role, endpoint, datasetKey, dataset.SourceCursor,
                Math.Clamp(_options.Value.PageSize, 1, 500), assessment.CorrelationId), cancellationToken);
        var priorCount = dataset.RecordCount;
        var priorTotal = dataset.FinancialTotal;
        var aggregate = priorCount == 0 && dataset.IntegrityHash.Length == 0
            ? result.IntegrityHash
            : Hash($"{dataset.IntegrityHash}|{result.IntegrityHash}");
        var availability = priorCount > 0 || result.RecordCount > 0
            ? AccountingProviderSwitchDatasetAvailability.Available
            : result.Availability;
        dataset.Record(availability, result.CapabilityLevel, checked(priorCount + result.RecordCount),
            priorTotal + result.FinancialTotal, result.Currency ?? dataset.Currency, result.NextCursor,
            result.SourceVersion ?? dataset.SourceVersion, aggregate, result.EvidenceJson,
            result.FailureCode, result.FailureSummary, UtcNow());
        DatasetCountHistogram.Record(result.RecordCount,
            new KeyValuePair<string, object?>("provider", endpoint.ProviderKey ?? "internal"),
            new KeyValuePair<string, object?>("dataset", datasetKey));
        return result.IsComplete;
    }

    private async Task<bool> PersistGapsAsync(AccountingProviderSwitchAssessment assessment, string strategy,
        CancellationToken cancellationToken)
    {
        var capabilities = await _dbContext.AccountingProviderSwitchCapabilities.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == assessment.CompanyId && x.AssessmentId == assessment.Id)
            .Select(x => new AccountingProviderSwitchCapabilityDto(x.EndpointRole, x.CapabilityKey, x.Level, x.Explanation, x.RequiredScope, x.ObservedUtc))
            .ToArrayAsync(cancellationToken);
        var datasets = await _dbContext.AccountingProviderSwitchDatasets.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == assessment.CompanyId && x.AssessmentId == assessment.Id)
            .Select(x => new AccountingProviderSwitchDatasetDto(x.EndpointRole, x.DatasetKey, x.Availability, x.CapabilityLevel,
                x.RecordCount, x.FinancialTotal, x.Currency, x.SourceCursor, x.SourceVersion, x.IntegrityHash,
                x.EvidenceJson, x.FailureCode, x.FailureSummary, x.ExtractedUtc)).ToArrayAsync(cancellationToken);
        var decisions = _gapPolicy.Evaluate(new(strategy, capabilities, datasets));
        var existing = await _dbContext.AccountingProviderSwitchGaps.IgnoreQueryFilters()
            .Where(x => x.CompanyId == assessment.CompanyId && x.AssessmentId == assessment.Id).ToArrayAsync(cancellationToken);
        _dbContext.AccountingProviderSwitchGaps.RemoveRange(existing);
        var now = UtcNow();
        foreach (var decision in decisions)
            _dbContext.AccountingProviderSwitchGaps.Add(new(assessment.CompanyId, assessment.SwitchId, assessment.Id,
                decision.Category, decision.DatasetKey, decision.Severity, decision.IsBlocking, decision.ReasonCode,
                decision.Explanation, decision.EvidenceJson, decision.OperatorAction, now));
        if (decisions.Any(x => x.IsBlocking))
            await WriteAuditAsync(assessment, null, AuditEventActions.AccountingProviderSwitchMaterialGapsChanged,
                AuditEventOutcomes.Blocked, "The assessment found material gaps that require operator action.",
                new Dictionary<string, string?> { ["blockingGapCount"] = decisions.Count(x => x.IsBlocking).ToString() }, cancellationToken);
        return true;
    }

    private async Task CompleteAsync(AccountingProviderSwitchAssessment assessment, AccountingProviderSwitch providerSwitch,
        CancellationToken cancellationToken)
    {
        var blocking = await _dbContext.AccountingProviderSwitchGaps.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == assessment.CompanyId && x.AssessmentId == assessment.Id && x.IsBlocking, cancellationToken);
        await WriteAuditAsync(assessment, null, AuditEventActions.AccountingProviderSwitchAssessmentCompleted,
            blocking == 0 ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Blocked,
            blocking == 0 ? "The read-only accounting-system assessment completed." : "The read-only assessment completed with blocking gaps.",
            new Dictionary<string, string?> { ["blockingGapCount"] = blocking.ToString() }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (blocking == 0 && providerSwitch.Status == AccountingProviderSwitchStatuses.Assessing)
        {
            try
            {
                var current = await _switchService.GetAsync(new(assessment.CompanyId, assessment.SwitchId), cancellationToken);
                await _switchService.TransitionAsync(new(assessment.CompanyId, assessment.SwitchId,
                    AccountingProviderSwitchStatuses.ReadyForPlanning, current.Version, assessment.RequestedByUserId,
                    assessment.CorrelationId), cancellationToken);
            }
            catch (AccountingAuthorityException exception)
            {
                _logger.LogWarning("Assessment {AssessmentId} completed, but switch readiness transition was rejected: {ReasonCode}.",
                    assessment.Id, exception.ReasonCode);
            }
        }
        CompletedCounter.Add(1);
        BlockingGapHistogram.Record(blocking);
        if (assessment.StartedUtc.HasValue)
            DurationHistogram.Record((assessment.CompletedUtc!.Value - assessment.StartedUtc.Value).TotalMilliseconds);
    }

    private async Task HandleFailureAsync(AccountingProviderSwitchAssessment assessment, Exception exception,
        CancellationToken cancellationToken)
    {
        var attempt = assessment.AttemptCount + 1;
        var (retryable, code, summary, retryAfter) = Classify(exception);
        var now = UtcNow();
        if (retryable && attempt < Math.Max(1, _options.Value.MaximumAttempts))
            assessment.Retry(code, summary, attempt, now.Add(retryAfter ?? TimeSpan.FromSeconds(Math.Min(60, 5 * attempt))), now);
        else
        {
            assessment.Fail(code, summary, attempt, now);
            FailureCounter.Add(1, new KeyValuePair<string, object?>("failure_code", code));
            await WriteAuditAsync(assessment, null, AuditEventActions.AccountingProviderSwitchAssessmentFailed,
                AuditEventOutcomes.Blocked, summary, new Dictionary<string, string?> { ["failureCode"] = code }, cancellationToken);
        }
        _logger.LogWarning("Provider-switch assessment {AssessmentId} stopped work item {WorkIndex}: {FailureCode} (retryable={Retryable}).",
            assessment.Id, assessment.WorkIndex, code, retryable);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static (bool Retryable, string Code, string Summary, TimeSpan? RetryAfter) Classify(Exception exception) => exception switch
    {
        FortnoxApiException provider when provider.IsTransient => (true, "provider_transient_failure", provider.SafeMessage, provider.RetryAfter),
        FortnoxApiException provider when provider.RequiresReconnect || provider.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden
            => (false, "provider_authorization_failed", provider.SafeMessage, null),
        FortnoxApiException provider => (false, "provider_validation_failed", provider.SafeMessage, null),
        TimeoutException => (true, "provider_timeout", "The provider did not respond before the read timeout.", null),
        HttpRequestException => (true, "provider_transport_failure", "The provider could not be reached for this read.", null),
        DbUpdateConcurrencyException => (true, "assessment_concurrency_conflict", "The assessment changed while a batch was being recorded.", null),
        _ => (false, "assessment_processing_failed", "The assessment stopped safely. Review configuration and replay it.", null)
    };

    private Task WriteAuditAsync(AccountingProviderSwitchAssessment assessment, Guid? actorId, string action,
        string outcome, string summary, IReadOnlyDictionary<string, string?>? extra, CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string?>
        {
            ["switchId"] = assessment.SwitchId.ToString("D"),
            ["assessmentId"] = assessment.Id.ToString("D"),
            ["status"] = assessment.Status,
            ["workIndex"] = assessment.WorkIndex.ToString(),
            ["totalWorkItems"] = assessment.TotalWorkItems.ToString()
        };
        if (extra is not null) foreach (var pair in extra) metadata[pair.Key] = pair.Value;
        return _auditWriter.WriteAsync(new AuditEventWriteRequest(assessment.CompanyId,
            actorId.HasValue ? AuditActorTypes.User : AuditActorTypes.System, actorId, action,
            AuditTargetTypes.AccountingProviderSwitchAssessment, assessment.Id.ToString("D"), outcome, summary,
            ["accounting_provider_switch", "provider_capabilities", "financial_inventory"], metadata,
            assessment.CorrelationId, UtcNow()), cancellationToken);
    }

    private IQueryable<AccountingProviderSwitchAssessment> Assessments(Guid companyId) =>
        _dbContext.AccountingProviderSwitchAssessments.IgnoreQueryFilters().Where(x => x.CompanyId == companyId);
    private static AccountingProviderSwitchEndpointDto Endpoint(AccountingProviderEndpoint endpoint) =>
        new(endpoint.Kind, endpoint.ProviderKey, endpoint.ProviderKey ?? "Virtual Company");
    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static AccountingAuthorityException NotFound() =>
        new(AccountingProviderSwitchReasonCodes.AssessmentNotFound, "The assessment was not found for this company.");
    private static AccountingAuthorityException Conflict(string code, string message) => new(code, message, true);
    private static void Validate(Guid companyId, Guid switchId, Guid actorId, string correlationId, string idempotencyKey)
    {
        if (companyId == Guid.Empty || switchId == Guid.Empty || actorId == Guid.Empty) throw new ArgumentException("Company, switch, and actor are required.");
        if (string.IsNullOrWhiteSpace(correlationId) || string.IsNullOrWhiteSpace(idempotencyKey)) throw new ArgumentException("Correlation and idempotency keys are required.");
    }
}
