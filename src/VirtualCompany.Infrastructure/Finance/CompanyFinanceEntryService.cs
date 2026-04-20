using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.BackgroundExecution;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceEntryService : IFinanceEntryService
{
    private const string FinanceSeedRequestedAction = "finance.seed.job.requested";
    private const string FinanceSeedSkippedAction = "finance.seed.job.skipped";
    private const string FinanceSeedRejectedAction = "finance.seed.job.rejected";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceSeedingStateService _financeSeedingStateService;
    private readonly IBackgroundExecutionIdentityFactory _identityFactory;
    private readonly IFinanceSeedTelemetry _financeSeedTelemetry;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICurrentUserAccessor? _currentUserAccessor;
    private readonly ICorrelationContextAccessor? _correlationContextAccessor;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyFinanceEntryService> _logger;


    public CompanyFinanceEntryService(
        VirtualCompanyDbContext dbContext,
        IFinanceSeedingStateService financeSeedingStateService,
        IBackgroundExecutionIdentityFactory identityFactory,
        IAuditEventWriter auditEventWriter,
        TimeProvider timeProvider,
        ILogger<CompanyFinanceEntryService> logger,
        IFinanceSeedTelemetry financeSeedTelemetry,
        ICurrentUserAccessor? currentUserAccessor = null,
        ICorrelationContextAccessor? correlationContextAccessor = null)
    {
        _dbContext = dbContext;
        _financeSeedingStateService = financeSeedingStateService;
        _identityFactory = identityFactory;
        _financeSeedTelemetry = financeSeedTelemetry;
        _auditEventWriter = auditEventWriter;
        _currentUserAccessor = currentUserAccessor;
        _timeProvider = timeProvider;
        _logger = logger;
        _correlationContextAccessor = correlationContextAccessor;
    }

    public async Task<FinanceEntryStateDto> GetEntryStateAsync(GetFinanceEntryStateQuery query, CancellationToken cancellationToken)
    {
        var company = await GetCompanyAsync(query.CompanyId, cancellationToken);
        var checkedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var seedingState = await _financeSeedingStateService.GetCompanyFinanceSeedingStateAsync(query.CompanyId, cancellationToken);
        var execution = await FindExecutionAsync(query.CompanyId, cancellationToken);

        return CreateEntryState(
            company.Id,
            checkedAtUtc,
            seedingState.State,
            execution,
            seedJobEnqueued: false,
            seedMode: FinanceSeedRequestModes.Normalize(query.SeedMode),
            dataAlreadyExists: seedingState.State == FinanceSeedingState.Seeded,
            fallbackTriggered: string.Equals(query.Source, FinanceEntrySources.FallbackRead, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<FinanceEntryStateDto> RequestEntryStateAsync(GetFinanceEntryStateQuery query, CancellationToken cancellationToken)
    {
        _ = await GetCompanyAsync(query.CompanyId, cancellationToken);
        var checkedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var result = await EnsureSeedRequestedAsync(query, checkedAtUtc, cancellationToken);

        return CreateEntryState(
            query.CompanyId,
            checkedAtUtc,
            result.State.State,
            result.Execution,
            result.SeedJobEnqueued,
            result.SeedMode,
            result.SeedOperation,
            result.DataAlreadyExists,
            result.ConfirmationRequired,
            result.FallbackTriggered);
    }

    private async Task<(FinanceSeedingStateResultDto State, BackgroundExecution? Execution, bool SeedJobEnqueued, bool DataAlreadyExists, string SeedMode, string SeedOperation, bool ConfirmationRequired, bool FallbackTriggered)> EnsureSeedRequestedAsync(
        GetFinanceEntryStateQuery query,
        DateTime checkedAtUtc,
        CancellationToken cancellationToken)
    {
        var normalizedSeedMode = FinanceSeedRequestModes.Normalize(query.SeedMode);
        var fallbackTriggered = string.Equals(query.Source, FinanceEntrySources.FallbackRead, StringComparison.OrdinalIgnoreCase);

        if (_dbContext.Database.IsRelational())
        {
            var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
            try
            {
                return await executionStrategy.ExecuteAsync(async () =>
                {
                    await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                    var result = await EnsureSeedRequestedCoreAsync(query, checkedAtUtc, cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return result;
                });
            }
            catch (DbUpdateException)
            {
                _dbContext.ChangeTracker.Clear();
            }
        }

        try
        {
            return await EnsureSeedRequestedCoreAsync(query, checkedAtUtc, cancellationToken);
        }
        catch (DbUpdateException)
        {
            _dbContext.ChangeTracker.Clear();
        }

        var existingState = await _financeSeedingStateService.GetCompanyFinanceSeedingStateAsync(query.CompanyId, cancellationToken);
        var existingExecution = await FindExecutionAsync(query.CompanyId, cancellationToken);
        return (
            existingState,
            existingExecution,
            false,
            existingState.State == FinanceSeedingState.Seeded,
            normalizedSeedMode,
            existingExecution is null ? FinanceSeedOperationContractValues.Skipped : FinanceSeedOperationContractValues.Reused,
            false,
            fallbackTriggered);
    }

    private async Task<(FinanceSeedingStateResultDto State, BackgroundExecution? Execution, bool SeedJobEnqueued, bool DataAlreadyExists, string SeedMode, string SeedOperation, bool ConfirmationRequired, bool FallbackTriggered)> EnsureSeedRequestedCoreAsync(
        GetFinanceEntryStateQuery query,
        DateTime checkedAtUtc,
        CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == query.CompanyId, cancellationToken);

        var execution = await FindExecutionAsync(query.CompanyId, cancellationToken);
        var seedingState = await _financeSeedingStateService.GetCompanyFinanceSeedingStateAsync(query.CompanyId, cancellationToken);
        var seedJobEnqueued = false;
        var seedStateBefore = seedingState.State;
        var normalizedSeedMode = FinanceSeedRequestModes.Normalize(query.SeedMode);
        var dataAlreadyExists = seedingState.State == FinanceSeedingState.Seeded;
        var actor = ResolveActor(query.Source);
        var fallbackTriggered = string.Equals(query.Source, FinanceEntrySources.FallbackRead, StringComparison.OrdinalIgnoreCase);
        var seedOperation = FinanceSeedOperationContractValues.None;
        var confirmationRequired = false;

        if (query.ForceSeed &&
            string.Equals(normalizedSeedMode, FinanceSeedRequestModes.Replace, StringComparison.OrdinalIgnoreCase) &&
            dataAlreadyExists &&
            !query.ConfirmReplace)
        {
            confirmationRequired = true;
            seedOperation = FinanceSeedOperationContractValues.Rejected;
            _logger.LogInformation(
                "Finance replace seed request for company {CompanyId} from {TriggerSource} in {SeedMode} mode was rejected because explicit confirmation was not provided. ActorType={ActorType}, ActorId={ActorId}, CorrelationId={CorrelationId}.",
                query.CompanyId,
                query.Source,
                normalizedSeedMode,
                actor.ActorType,
                actor.ActorId,
                _correlationContextAccessor?.CorrelationId);
            await WriteDecisionAuditAsync(
                query.CompanyId,
                execution,
                FinanceSeedRejectedAction,
                "rejected",
                "Finance replace seed request requires explicit confirmation before overwriting existing seeded data.",
                query.Source,
                normalizedSeedMode,
                actor.ActorType,
                actor.ActorId,
                cancellationToken,
                new Dictionary<string, string?>
                {
                    ["confirmationRequired"] = "true", ["dataAlreadyExists"] = "true", ["seedStateBefore"] = seedStateBefore.ToStorageValue()
                });
            return (seedingState, execution, false, true, normalizedSeedMode, seedOperation, true, fallbackTriggered);
        }

        if (ShouldForceSeed(query, execution, normalizedSeedMode))
        {
            if (execution is null)
            {
                execution = CreateExecution(query.CompanyId);
                _dbContext.BackgroundExecutions.Add(execution);
            }
            else
            {
                execution.Queue(
                    checkedAtUtc,
                    _identityFactory.EnsureCorrelationId(_correlationContextAccessor?.CorrelationId),
                    resetAttempts: true);
            }

            FinanceSeedingMetadata.MarkSeeding(
                company,
                checkedAtUtc,
                requestedAtUtc: checkedAtUtc,
                triggerSource: query.Source,
                jobId: execution.Id,
                correlationId: execution.CorrelationId);
            seedJobEnqueued = true;
            await WriteRequestedAuditAsync(
                query.CompanyId,
                execution,
                query.Source,
                normalizedSeedMode,
                actor.ActorType,
                actor.ActorId,
                seedStateBefore,
                FinanceSeedingState.Seeding,
                isRetry: false,
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await EmitRequestedInstrumentationAsync(query, execution, seedStateBefore, FinanceSeedingState.Seeding, cancellationToken);
            seedOperation = FinanceSeedOperationContractValues.Started;
        }
        else if (ShouldRetry(query, seedingState.State, execution))
        {
            if (execution is null)
            {
                execution = CreateExecution(query.CompanyId);
                _dbContext.BackgroundExecutions.Add(execution);
            }
            else
            {
                execution.Queue(
                    checkedAtUtc,
                    _identityFactory.EnsureCorrelationId(_correlationContextAccessor?.CorrelationId),
                    resetAttempts: true);
            }

            FinanceSeedingMetadata.MarkSeeding(
                company,
                checkedAtUtc,
                requestedAtUtc: checkedAtUtc,
                triggerSource: query.Source,
                jobId: execution.Id,
                correlationId: execution.CorrelationId);
            seedJobEnqueued = true;
            await WriteRequestedAuditAsync(
                query.CompanyId,
                execution,
                query.Source,
                normalizedSeedMode,
                actor.ActorType,
                actor.ActorId,
                seedStateBefore,
                FinanceSeedingState.Seeding,
                isRetry: true,
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await EmitRequestedInstrumentationAsync(
                query,
                execution,
                seedStateBefore,
                FinanceSeedingState.Seeding,
                cancellationToken);
            seedOperation = FinanceSeedOperationContractValues.Started;
        }
        else if (ShouldEnqueueInitialRequest(seedingState.State, execution))
        {
            execution = CreateExecution(query.CompanyId);
            FinanceSeedingMetadata.MarkSeeding(
                company,
                checkedAtUtc,
                requestedAtUtc: checkedAtUtc,
                triggerSource: query.Source,
                jobId: execution.Id,
                correlationId: execution.CorrelationId);
            _dbContext.BackgroundExecutions.Add(execution);
            seedJobEnqueued = true;
            await WriteRequestedAuditAsync(
                query.CompanyId,
                execution,
                query.Source,
                normalizedSeedMode,
                actor.ActorType,
                actor.ActorId,
                seedStateBefore,
                FinanceSeedingState.Seeding,
                isRetry: false,
                cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Queued finance seed background execution {ExecutionId} for company {CompanyId}.",
                execution.Id,
                query.CompanyId);
            await EmitRequestedInstrumentationAsync(
                query,
                execution,
                seedStateBefore,
                FinanceSeedingState.Seeding,
                cancellationToken);
            seedOperation = FinanceSeedOperationContractValues.Started;
        }
        else if (IsExecutionActive(execution))
        {
            _logger.LogInformation(
                "Finance seed request from {TriggerSource} in {SeedMode} mode for company {CompanyId} reused existing execution {ExecutionId} because a job is already running with status {JobStatus}. ActorType={ActorType}, ActorId={ActorId}, CorrelationId={CorrelationId}, IdempotencyKey={IdempotencyKey}.",
                query.Source,
                normalizedSeedMode,
                query.CompanyId,
                execution!.Id,
                execution.Status.ToStorageValue(),
                actor.ActorType,
                actor.ActorId,
                execution.CorrelationId,
                execution.IdempotencyKey);
            seedOperation = FinanceSeedOperationContractValues.Reused;
            await WriteDecisionAuditAsync(
                query.CompanyId,
                execution,
                FinanceSeedSkippedAction,
                "skipped",
                "Finance seed request reused an already active background execution.",
                query.Source,
                normalizedSeedMode,
                actor.ActorType,
                actor.ActorId,
                cancellationToken,
                new Dictionary<string, string?> { ["reason"] = "job_already_active" });
        }
        else if (query.ForceSeed || query.RetryOnFailure || fallbackTriggered)
        {
            seedOperation = FinanceSeedOperationContractValues.Skipped;
            _logger.LogInformation(
                "Finance seed request from {TriggerSource} in {SeedMode} mode for company {CompanyId} was skipped because finance data was already ready. ActorType={ActorType}, ActorId={ActorId}, CorrelationId={CorrelationId}, IdempotencyKey={IdempotencyKey}.",
                query.Source,
                normalizedSeedMode,
                query.CompanyId,
                actor.ActorType,
                actor.ActorId,
                execution?.CorrelationId,
                execution?.IdempotencyKey);
            await WriteDecisionAuditAsync(
                query.CompanyId,
                execution,
                FinanceSeedSkippedAction,
                "skipped",
                "Finance seed request was skipped because the company finance dataset was already initialized.",
                query.Source,
                normalizedSeedMode,
                actor.ActorType,
                actor.ActorId,
                cancellationToken,
                new Dictionary<string, string?> { ["reason"] = "already_initialized" });
        }

        var refreshedState = await _financeSeedingStateService.GetCompanyFinanceSeedingStateAsync(query.CompanyId, cancellationToken);
        var refreshedExecution = await FindExecutionAsync(query.CompanyId, cancellationToken);
        return (
            refreshedState,
            refreshedExecution,
            seedJobEnqueued,
            dataAlreadyExists || refreshedState.State == FinanceSeedingState.Seeded,
            normalizedSeedMode,
            seedOperation,
            confirmationRequired,
            fallbackTriggered);
    }

    private BackgroundExecution CreateExecution(Guid companyId)
    {
        var identity = CreateIdentity(companyId);
        return new BackgroundExecution(
            Guid.NewGuid(),
            companyId,
            BackgroundExecutionType.FinanceSeed,
            BackgroundExecutionRelatedEntityTypes.FinanceSeed,
            companyId.ToString("D"),
            identity.CorrelationId,
            identity.IdempotencyKey,
            maxAttempts: 5);
    }

    private async Task WriteRequestedAuditAsync(
        Guid companyId,
        BackgroundExecution execution,
        string source,
        string seedMode,
        string actorType,
        Guid? actorId,
        FinanceSeedingState seedStateBefore,
        FinanceSeedingState seedStateAfter,
        bool isRetry,
        CancellationToken cancellationToken)
    {
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                actorType,
                actorId,
                FinanceSeedRequestedAction,
                BackgroundExecutionRelatedEntityTypes.FinanceSeed,
                execution.Id.ToString("D"),
                AuditEventOutcomes.Requested,
                isRetry
                    ? "Finance entry requested a retry for the finance seed background job."
                    : "Finance entry requested the finance seed background job.",
                Metadata: new Dictionary<string, string?>
                {
                    ["triggerSource"] = source,
                    ["source"] = source,
                    ["actorType"] = actorType,
                    ["actorId"] = actorId?.ToString("D"),
                    ["companyId"] = companyId.ToString("D"),
                    ["executionId"] = execution.Id.ToString("D"),
                    ["jobStatus"] = execution.Status.ToStorageValue(),
                    ["idempotencyKey"] = execution.IdempotencyKey,
                    ["correlationId"] = execution.CorrelationId,
                    ["mode"] = seedMode,
                    ["isRetry"] = isRetry ? "true" : "false",
                    ["seedStateBefore"] = seedStateBefore.ToStorageValue(),
                    ["seedStateAfter"] = seedStateAfter.ToStorageValue()
                },
                CorrelationId: execution.CorrelationId,
                OccurredUtc: _timeProvider.GetUtcNow().UtcDateTime),
            cancellationToken);
    }

    private async Task WriteDecisionAuditAsync(
        Guid companyId,
        BackgroundExecution? execution,
        string action,
        string outcome,
        string rationale,
        string source,
        string seedMode,
        string actorType,
        Guid? actorId,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? extraMetadata = null)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["triggerSource"] = source,
            ["source"] = source,
            ["actorType"] = actorType,
            ["actorId"] = actorId?.ToString("D"),
            ["companyId"] = companyId.ToString("D"),
            ["executionId"] = execution?.Id.ToString("D"),
            ["jobStatus"] = execution?.Status.ToStorageValue(),
            ["idempotencyKey"] = execution?.IdempotencyKey,
            ["correlationId"] = execution?.CorrelationId,
            ["mode"] = seedMode
        };

        if (extraMetadata is not null)
        {
            foreach (var pair in extraMetadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                actorType,
                actorId,
                action,
                BackgroundExecutionRelatedEntityTypes.FinanceSeed,
                execution?.Id.ToString("D") ?? companyId.ToString("D"),
                outcome,
                rationale,
                Metadata: metadata,
                CorrelationId: execution?.CorrelationId,
                OccurredUtc: _timeProvider.GetUtcNow().UtcDateTime), cancellationToken);
    }

    private async Task EmitRequestedInstrumentationAsync(
        GetFinanceEntryStateQuery query,
        BackgroundExecution execution,
        FinanceSeedingState seedStateBefore,
        FinanceSeedingState seedStateAfter,
        CancellationToken cancellationToken)
    {
        var actor = ResolveActor(query.Source);
        _logger.LogInformation(
            "Finance seed orchestration requested from {TriggerSource} in {SeedMode} mode for company {CompanyId} by {ActorType} {ActorId}. Execution {ExecutionId} with correlation {CorrelationId} and idempotency key {IdempotencyKey} moved from {SeedStateBefore} to {SeedStateAfter}.",
            query.Source,
            FinanceSeedRequestModes.Normalize(query.SeedMode),
            query.CompanyId,
            actor.ActorType,
            actor.ActorId,
            execution.Id,
            execution.CorrelationId,
            execution.IdempotencyKey,
            seedStateBefore.ToStorageValue(),
            seedStateAfter.ToStorageValue());

        await _financeSeedTelemetry.TrackAsync(
            FinanceSeedTelemetryEventNames.Requested,
            new FinanceSeedTelemetryContext(
                query.CompanyId,
                execution.Id,
                execution.CorrelationId,
                execution.IdempotencyKey,
                query.Source,
                seedStateBefore,
                seedStateAfter,
                _currentUserAccessor?.UserId,
                SeedMode: FinanceSeedRequestModes.Normalize(query.SeedMode),
                ActorType: actor.ActorType,
                ActorId: actor.ActorId),
            cancellationToken);
    }

    private (string ActorType, Guid? ActorId) ResolveActor(string source)
    {
        if (string.Equals(source, FinanceEntrySources.FallbackRead, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(source, FinanceEntrySources.Backfill, StringComparison.OrdinalIgnoreCase))
        {
            return (AuditActorTypes.System, null);
        }

        if (_currentUserAccessor?.UserId is Guid userId)
        {
            return (AuditActorTypes.User, userId);
        }

        return (AuditActorTypes.System, null);
    }

    private async Task<Company> GetCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        return await _dbContext.Companies.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken)
            ?? throw new KeyNotFoundException($"Company '{companyId}' was not found.");
    }

    private BackgroundExecutionIdentity CreateIdentity(Guid companyId) =>
        _identityFactory.Create(
            companyId,
            "finance-seed",
            _correlationContextAccessor?.CorrelationId,
            BackgroundExecutionType.FinanceSeed.ToStorageValue(),
            companyId.ToString("N"));

    private async Task<BackgroundExecution?> FindExecutionAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var identity = _identityFactory.CreateIdempotencyKey(
            "finance-seed",
            BackgroundExecutionType.FinanceSeed.ToStorageValue(),
            companyId.ToString("N"));

        return await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.ExecutionType == BackgroundExecutionType.FinanceSeed &&
                     x.IdempotencyKey == identity,
                cancellationToken);
    }

    private static bool ShouldEnqueueInitialRequest(FinanceSeedingState seedingState, BackgroundExecution? execution) =>
        seedingState == FinanceSeedingState.NotSeeded &&
        execution is null;

    private static bool ShouldForceSeed(GetFinanceEntryStateQuery query, BackgroundExecution? execution, string normalizedSeedMode) =>
        query.ForceSeed &&
        string.Equals(normalizedSeedMode, FinanceSeedRequestModes.Replace, StringComparison.OrdinalIgnoreCase) &&
        !IsExecutionActive(execution);

    private static bool ShouldRetry(GetFinanceEntryStateQuery query, FinanceSeedingState seedingState, BackgroundExecution? execution) =>
        (query.RetryOnFailure ||
         string.Equals(query.Source, FinanceEntrySources.FallbackRead, StringComparison.OrdinalIgnoreCase)) &&
        seedingState == FinanceSeedingState.Failed &&
        (execution is null || execution.Status is BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Blocked);

    private FinanceEntryStateDto CreateEntryState(
        Guid companyId,
        DateTime checkedAtUtc,
        FinanceSeedingState seedingState,
        BackgroundExecution? execution,
        bool seedJobEnqueued,
        string seedMode = "",
        string seedOperation = FinanceSeedOperationContractValues.None,
        bool dataAlreadyExists = false,
        bool confirmationRequired = false,
        bool fallbackTriggered = false)
    {
        var progressState = ResolveProgressState(seedingState, execution);
        var initializationStatus = ResolveInitializationStatus(progressState);
        var canRetry = progressState == FinanceEntryProgressStates.Failed;
        var canRefresh = progressState != FinanceEntryProgressStates.Seeded;
        var statusEndpoint = $"/internal/companies/{companyId:D}/finance/entry-state";
        var seedEndpoint = $"/internal/companies/{companyId:D}/finance/manual-seed";
        var confirmationMessage = confirmationRequired
            ? "Finance data already exists. Confirm replace to regenerate the current seeded dataset."
            : null;
        var recommendedAction = ResolveRecommendedAction(seedingState, dataAlreadyExists);

        return new FinanceEntryStateDto(
            companyId,
            initializationStatus,
            progressState,
            seedingState,
            seedJobEnqueued,
            IsExecutionActive(execution) || seedingState == FinanceSeedingState.Seeding,
            canRetry,
            canRefresh,
            BuildMessage(progressState, execution, seedOperation, confirmationRequired, dataAlreadyExists),
            checkedAtUtc,
            seedingState == FinanceSeedingState.Seeded ? execution?.CompletedUtc ?? checkedAtUtc : null,
            execution?.StartedUtc,
            execution?.CompletedUtc,
            execution?.FailureCode,
            execution?.FailureMessage,
            seedMode,
            seedOperation,
            dataAlreadyExists,
            confirmationRequired,
            fallbackTriggered,
            statusEndpoint,
            seedEndpoint,
            execution?.Status.ToStorageValue(),
            execution?.IdempotencyKey,
            confirmationMessage,
            true,
            recommendedAction,
            [FinanceSeedRequestModes.Replace],
            execution?.CorrelationId);
    }

    private static string ResolveProgressState(FinanceSeedingState seedingState, BackgroundExecution? execution)
    {
        if (seedingState == FinanceSeedingState.Seeded)
        {
            return FinanceEntryProgressStates.Seeded;
        }

        if (execution?.Status == BackgroundExecutionStatus.Pending)
        {
            return FinanceEntryProgressStates.SeedingRequested;
        }

        if (execution?.Status is BackgroundExecutionStatus.InProgress or BackgroundExecutionStatus.RetryScheduled)
        {
            return FinanceEntryProgressStates.InProgress;
        }

        if (execution?.Status is BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Blocked || seedingState == FinanceSeedingState.Failed)
        {
            return FinanceEntryProgressStates.Failed;
        }

        if (seedingState == FinanceSeedingState.Seeding)
        {
            return FinanceEntryProgressStates.InProgress;
        }

        return FinanceEntryProgressStates.NotSeeded;
    }

    private static bool IsExecutionActive(BackgroundExecution? execution) =>
        execution?.Status is BackgroundExecutionStatus.Pending
            or BackgroundExecutionStatus.InProgress
            or BackgroundExecutionStatus.RetryScheduled;

    private static string ResolveInitializationStatus(string progressState) =>
        progressState switch
        {
            FinanceEntryProgressStates.Seeded => FinanceEntryInitializationStates.Ready,
            FinanceEntryProgressStates.Failed => FinanceEntryInitializationStates.Failed,
            _ => FinanceEntryInitializationStates.Initializing
        };

    private static string BuildMessage(string progressState, BackgroundExecution? execution, string seedOperation, bool confirmationRequired, bool dataAlreadyExists)
    {
        if (confirmationRequired)
        {
            return "Finance data already exists for this company. Confirm replace to overwrite the current seeded dataset.";
        }

        if (string.Equals(seedOperation, FinanceSeedOperationContractValues.Reused, StringComparison.OrdinalIgnoreCase))
        {
            return "Finance setup is already running in the background. The existing seed execution was reused instead of starting a duplicate job.";
        }

        return BuildMessage(progressState, execution);
    }

    private static string BuildMessage(string progressState, BackgroundExecution? execution)
    {
        if (execution?.Status == BackgroundExecutionStatus.Pending)
        {
            return "Finance setup has been requested and is waiting to start.";
        }

        if (execution?.Status is BackgroundExecutionStatus.InProgress or BackgroundExecutionStatus.RetryScheduled ||
            string.Equals(progressState, FinanceEntryProgressStates.InProgress, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(progressState, FinanceEntryProgressStates.SeedingRequested, StringComparison.OrdinalIgnoreCase))
        {
            return "Finance setup is running in the background.";
        }

        if (execution?.Status is BackgroundExecutionStatus.Failed or BackgroundExecutionStatus.Blocked ||
            string.Equals(progressState, FinanceEntryProgressStates.Failed, StringComparison.OrdinalIgnoreCase))
        {
            return execution?.FailureMessage ?? "Finance setup failed. Retry to request a new seed run.";
        }

        if (string.Equals(progressState, FinanceEntryProgressStates.Seeded, StringComparison.OrdinalIgnoreCase))
        {
            return "Finance data is ready.";
        }

        return "Finance setup has not started yet.";
    }

    private static string ResolveRecommendedAction(FinanceSeedingState seedingState, bool dataAlreadyExists) =>
        dataAlreadyExists || seedingState != FinanceSeedingState.NotSeeded
            ? FinanceRecommendedActionContractValues.Regenerate
            : FinanceRecommendedActionContractValues.Generate;
}
