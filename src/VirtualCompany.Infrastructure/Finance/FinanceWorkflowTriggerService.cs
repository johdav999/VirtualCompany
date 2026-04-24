using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceWorkflowTriggerService : IFinanceWorkflowTriggerService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceReadService _financeReadService;
    private readonly IFinanceApprovalTaskService _financeApprovalTaskService;
    private readonly IFinanceCashPositionWorkflowService _financeCashPositionWorkflowService;
    private readonly TimeProvider _timeProvider;
    private readonly IFinanceWorkflowTriggerRegistry _triggerRegistry;
    private readonly ILogger<FinanceWorkflowTriggerService> _logger;

    public FinanceWorkflowTriggerService(
        VirtualCompanyDbContext dbContext,
        IFinanceReadService financeReadService,
        IFinanceApprovalTaskService financeApprovalTaskService,
        IFinanceCashPositionWorkflowService financeCashPositionWorkflowService,
        IFinanceWorkflowTriggerRegistry triggerRegistry,
        TimeProvider timeProvider,
        ILogger<FinanceWorkflowTriggerService> logger)
    {
        _dbContext = dbContext;
        _financeReadService = financeReadService;
        _financeApprovalTaskService = financeApprovalTaskService;
        _financeCashPositionWorkflowService = financeCashPositionWorkflowService;
        _triggerRegistry = triggerRegistry;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<FinanceWorkflowTriggerExecutionDto> ProcessAsync(
        ProcessFinanceWorkflowTriggerCommand command,
        CancellationToken cancellationToken)
    {
        Validate(command);

        var normalizedTriggerType = FinanceWorkflowTriggerTypes.Normalize(command.TriggerType);
        var normalizedSourceEntityType = command.SourceEntityType.Trim();
        var normalizedSourceEntityId = command.SourceEntityId.Trim();
        var normalizedSourceEntityVersion = command.SourceEntityVersion.Trim();
        var normalizedCorrelationId = NormalizeCorrelationId(command.CorrelationId, command.EventId);
        var duplicateChecks = new List<string>();
        var registeredChecks = _triggerRegistry.GetChecks(normalizedTriggerType);

        var startedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var execution = new FinanceWorkflowTriggerExecution(
            Guid.NewGuid(),
            command.CompanyId,
            normalizedTriggerType,
            normalizedSourceEntityType,
            normalizedSourceEntityId,
            normalizedSourceEntityVersion,
            command.OccurredAtUtc,
            startedAtUtc,
            normalizedCorrelationId,
            command.EventId,
            command.CausationId,
            command.TriggerMessageId,
            BuildMetadataJson(
                command,
                normalizedTriggerType,
                normalizedSourceEntityType,
                normalizedSourceEntityId,
                normalizedSourceEntityVersion,
                normalizedCorrelationId,
                registeredChecks,
                [],
                []));
        _dbContext.FinanceWorkflowTriggerExecutions.Add(execution);
        var executedChecks = new List<string>();

        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            foreach (var checkCode in registeredChecks)
            {
                var checkExecution = await TryStartCheckExecutionAsync(
                    execution,
                    command,
                    normalizedTriggerType,
                    normalizedSourceEntityType,
                    normalizedSourceEntityId,
                    normalizedSourceEntityVersion,
                    normalizedCorrelationId,
                    checkCode,
                    cancellationToken);
                if (checkExecution is null)
                {
                    duplicateChecks.Add(checkCode);
                    continue;
                }

                try
                {
                    var executed = await ExecuteRegisteredCheckAsync(
                    checkCode,
                    command,
                    normalizedTriggerType,
                    normalizedSourceEntityId,
                    normalizedSourceEntityVersion,
                    normalizedCorrelationId,
                    executedChecks,
                    cancellationToken);

                    checkExecution.UpdateMetadataJson(
                        BuildCheckMetadataJson(
                            command,
                            execution,
                            normalizedTriggerType,
                            normalizedSourceEntityType,
                            normalizedSourceEntityId,
                            normalizedSourceEntityVersion,
                            normalizedCorrelationId,
                            checkCode));
                    checkExecution.Complete(
                        executed
                            ? FinanceWorkflowTriggerOutcomes.Succeeded
                            : FinanceWorkflowTriggerOutcomes.NoOp,
                        _timeProvider.GetUtcNow().UtcDateTime);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    checkExecution.UpdateMetadataJson(
                        BuildCheckMetadataJson(
                            command,
                            execution,
                            normalizedTriggerType,
                            normalizedSourceEntityType,
                            normalizedSourceEntityId,
                            normalizedSourceEntityVersion,
                            normalizedCorrelationId,
                            checkCode));
                    checkExecution.Complete(FinanceWorkflowTriggerOutcomes.Failed, _timeProvider.GetUtcNow().UtcDateTime, ex.Message);
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    throw;
                }
            }

            var finalOutcome = ResolveExecutionOutcome(executedChecks, duplicateChecks, registeredChecks.Count);
            execution.UpdateMetadataJson(
                BuildMetadataJson(
                    command, normalizedTriggerType, normalizedSourceEntityType, normalizedSourceEntityId, normalizedSourceEntityVersion, normalizedCorrelationId, registeredChecks, executedChecks, duplicateChecks));
            execution.Complete(
                executedChecks,
                finalOutcome,
                _timeProvider.GetUtcNow().UtcDateTime,
                finalOutcome == FinanceWorkflowTriggerOutcomes.DuplicateSkipped
                    ? $"Skipped duplicate finance workflow trigger execution because all registered checks were already processed for source version '{normalizedSourceEntityVersion}'."
                    : null);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Map(execution);
        }
        catch (Exception ex)
        {
            execution.UpdateMetadataJson(
                BuildMetadataJson(
                    command,
                    normalizedTriggerType,
                    normalizedSourceEntityType,
                    normalizedSourceEntityId,
                    normalizedSourceEntityVersion,
                    normalizedCorrelationId,
                    registeredChecks,
                    executedChecks,
                    duplicateChecks));
            execution.Complete(
                executedChecks,
                FinanceWorkflowTriggerOutcomes.Failed,
                _timeProvider.GetUtcNow().UtcDateTime,
                ex.Message);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private async Task<FinanceWorkflowTriggerCheckExecution?> TryStartCheckExecutionAsync(
        FinanceWorkflowTriggerExecution execution,
        ProcessFinanceWorkflowTriggerCommand command,
        string triggerType,
        string sourceEntityType,
        string sourceEntityId,
        string sourceEntityVersion,
        string correlationId,
        string checkCode,
        CancellationToken cancellationToken)
    {
        var existingCheckExecution = await _dbContext.FinanceWorkflowTriggerCheckExecutions
            .AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId &&
                        x.TriggerType == triggerType &&
                        x.SourceEntityType == sourceEntityType &&
                        x.SourceEntityId == sourceEntityId &&
                        x.SourceEntityVersion == sourceEntityVersion &&
                        x.CheckType == checkCode)
            .Select(x => x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existingCheckExecution != Guid.Empty)
        {
            return null;
        }

        var checkExecution = new FinanceWorkflowTriggerCheckExecution(
            Guid.NewGuid(),
            command.CompanyId,
            execution.Id,
            triggerType,
            sourceEntityType,
            sourceEntityId,
            sourceEntityVersion,
            checkCode,
            _timeProvider.GetUtcNow().UtcDateTime,
            correlationId,
            command.EventId,
            command.CausationId,
            command.TriggerMessageId,
            BuildCheckMetadataJson(command, execution, triggerType, sourceEntityType, sourceEntityId, sourceEntityVersion, correlationId, checkCode));
        _dbContext.FinanceWorkflowTriggerCheckExecutions.Add(checkExecution);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return checkExecution;
        }
        catch (DbUpdateException ex) when (IsDuplicateCheckExecution(ex))
        {
            _dbContext.Entry(checkExecution).State = EntityState.Detached;
            return null;
        }
    }

    private Task<bool> ExecuteRegisteredCheckAsync(
        string checkCode,
        ProcessFinanceWorkflowTriggerCommand command,
        string triggerType,
        string sourceEntityId,
        string sourceEntityVersion,
        string correlationId,
        ICollection<string> executedChecks,
        CancellationToken cancellationToken) =>
        checkCode switch
        {
            FinanceWorkflowExecutedChecks.RefreshInsightsSnapshot => TryRefreshInsightsSnapshotAsync(
                command.CompanyId,
                command.OccurredAtUtc,
                correlationId,
                executedChecks,
                cancellationToken),
            FinanceWorkflowExecutedChecks.EvaluateCashPosition => TryEvaluateCashPositionAsync(
                command.CompanyId,
                correlationId,
                command.EventId,
                sourceEntityId,
                sourceEntityVersion,
                executedChecks,
                cancellationToken),
            FinanceWorkflowExecutedChecks.EnsureApprovalTask => TryEnsureApprovalTaskAsync(
                command.CompanyId,
                triggerType,
                sourceEntityId,
                sourceEntityVersion,
                correlationId,
                command.EventId,
                executedChecks,
                cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported finance workflow trigger check '{checkCode}'.")
        };

    private async Task<bool> TryRefreshInsightsSnapshotAsync(
        Guid companyId,
        DateTime occurredAtUtc,
        string correlationId,
        ICollection<string> executedChecks,
        CancellationToken cancellationToken)
    {
        try
        {
            await _financeReadService.RefreshInsightsSnapshotAsync(
                new RefreshFinanceInsightsSnapshotCommand(
                    companyId,
                    occurredAtUtc,
                    SnapshotKey: FinanceInsightSnapshotKeys.Default,
                    CorrelationId: correlationId),
                cancellationToken);
            executedChecks.Add(FinanceWorkflowExecutedChecks.RefreshInsightsSnapshot);
            _logger.LogInformation(
                "Finance analytics refresh succeeded for company {CompanyId}. Check={CheckCode}. CorrelationId={CorrelationId}.",
                companyId,
                FinanceWorkflowExecutedChecks.RefreshInsightsSnapshot,
                correlationId);
            return true;
        }
        catch (FinanceNotInitializedException ex)
        {
            _logger.LogDebug(
                ex,
                "Skipped finance insights refresh for company {CompanyId} because finance data is not initialized.",
                companyId);
        }
        return false;
    }

    private async Task<bool> TryEvaluateCashPositionAsync(
        Guid companyId,
        string correlationId,
        string? eventId,
        string sourceEntityId,
        string sourceEntityVersion,
        ICollection<string> executedChecks,
        CancellationToken cancellationToken)
    {
        try
        {
            await _financeCashPositionWorkflowService.EvaluateAsync(
                new EvaluateFinanceCashPositionWorkflowCommand(
                    companyId,
                    CorrelationId: correlationId,
                    TriggerEventId: eventId,
                    SourceEntityId: sourceEntityId,
                    SourceEntityVersion: sourceEntityVersion),
                cancellationToken);
            executedChecks.Add(FinanceWorkflowExecutedChecks.EvaluateCashPosition);
            _logger.LogInformation(
                "Finance cash-position evaluation succeeded for company {CompanyId}. Check={CheckCode}. CorrelationId={CorrelationId}. EventId={EventId}.",
                companyId,
                FinanceWorkflowExecutedChecks.EvaluateCashPosition,
                correlationId,
                eventId);
            return true;
        }
        catch (FinanceNotInitializedException ex)
        {
            _logger.LogDebug(
                ex,
                "Skipped finance cash position evaluation for company {CompanyId} because finance data is not initialized.",
                companyId);
        }
        return false;
    }

    private async Task<bool> TryEnsureApprovalTaskAsync(
        Guid companyId,
        string triggerType,
        string sourceEntityId,
        string sourceEntityVersion,
        string correlationId,
        string? eventId,
        ICollection<string> executedChecks,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(sourceEntityId, out var targetId))
        {
            return false;
        }

        switch (triggerType)
        {
            case FinanceWorkflowTriggerTypes.Bill:
            {
                var bill = await _dbContext.FinanceBills
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == targetId, cancellationToken);
                if (bill is null)
                {
                    return false;
                }

                await _financeApprovalTaskService.EnsureTaskAsync(
                    new EnsureFinanceApprovalTaskCommand(
                        companyId,
                        ApprovalTargetType.Bill,
                        bill.Id,
                        bill.Amount,
                        bill.Currency,
                        bill.DueUtc,
                        correlationId,
                        eventId,
                        sourceEntityVersion),
                    cancellationToken);
                executedChecks.Add(FinanceWorkflowExecutedChecks.EnsureApprovalTask);
                _logger.LogInformation(
                    "Finance approval-task check succeeded for company {CompanyId}. Check={CheckCode}. TriggerType={TriggerType}. TargetId={TargetId}.",
                    companyId,
                    FinanceWorkflowExecutedChecks.EnsureApprovalTask,
                    triggerType,
                    bill.Id);
                return true;
                break;
            }
            case FinanceWorkflowTriggerTypes.Payment:
            {
                var payment = await _dbContext.Payments
                    .IgnoreQueryFilters()
                    .AsNoTracking()
                    .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == targetId, cancellationToken);
                if (payment is null)
                {
                    return false;
                }

                await _financeApprovalTaskService.EnsureTaskAsync(
                    new EnsureFinanceApprovalTaskCommand(
                        companyId,
                        ApprovalTargetType.Payment,
                        payment.Id,
                        payment.Amount,
                        payment.Currency,
                        payment.PaymentDate,
                        correlationId,
                        eventId,
                        sourceEntityVersion),
                    cancellationToken);
                executedChecks.Add(FinanceWorkflowExecutedChecks.EnsureApprovalTask);
                _logger.LogInformation(
                    "Finance approval-task check succeeded for company {CompanyId}. Check={CheckCode}. TriggerType={TriggerType}. TargetId={TargetId}.",
                    companyId,
                    FinanceWorkflowExecutedChecks.EnsureApprovalTask,
                    triggerType,
                    payment.Id);
                return true;
                break;
            }
        }

        return false;
    }

    private static string ResolveExecutionOutcome(
        IReadOnlyCollection<string> executedChecks,
        IReadOnlyCollection<string> duplicateChecks,
        int registeredCheckCount) =>
        executedChecks.Count > 0
            ? FinanceWorkflowTriggerOutcomes.Succeeded
            : registeredCheckCount > 0 && duplicateChecks.Count == registeredCheckCount
                ? FinanceWorkflowTriggerOutcomes.DuplicateSkipped
                : FinanceWorkflowTriggerOutcomes.NoOp;

    private static FinanceWorkflowTriggerExecutionDto Map(FinanceWorkflowTriggerExecution execution) =>
        new(
            execution.Id,
            execution.CompanyId,
            execution.TriggerType,
            execution.SourceEntityType,
            execution.SourceEntityId,
            execution.SourceEntityVersion,
            execution.GetExecutedChecks(),
            execution.StartedAtUtc,
            execution.CompletedAtUtc,
            execution.Outcome,
            execution.ErrorDetails);

    private static void Validate(ProcessFinanceWorkflowTriggerCommand command)
    {
        if (command.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(command));
        }

        if (string.IsNullOrWhiteSpace(command.SourceEntityType) ||
            string.IsNullOrWhiteSpace(command.SourceEntityId) ||
            string.IsNullOrWhiteSpace(command.SourceEntityVersion))
        {
            throw new ArgumentException("Source entity metadata is required.", nameof(command));
        }
    }

    private static string NormalizeCorrelationId(string? correlationId, string? eventId)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            return correlationId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(eventId))
        {
            return eventId.Trim();
        }

        return Guid.NewGuid().ToString("N");
    }

    private static bool IsDuplicateCheckExecution(DbUpdateException exception)
    {
        var message = exception.ToString();
        return message.Contains("duplicate key value violates unique constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("finance_workflow_trigger_check_executions", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("IX_finance_workflow_trigger_check_exec_dedupe", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMetadataJson(
        ProcessFinanceWorkflowTriggerCommand command,
        string triggerType,
        string sourceEntityType,
        string sourceEntityId,
        string sourceEntityVersion,
        string correlationId,
        IReadOnlyCollection<string> registeredChecks,
        IReadOnlyCollection<string> executedChecks,
        IReadOnlyCollection<string> duplicateChecks)
    {
        var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if (command.Metadata is not null)
        {
            foreach (var pair in command.Metadata)
            {
                metadata[pair.Key] = pair.Value?.DeepClone();
            }
        }

        metadata["triggerType"] = JsonValue.Create(triggerType);
        metadata["sourceEntityType"] = JsonValue.Create(sourceEntityType);
        metadata["sourceEntityId"] = JsonValue.Create(sourceEntityId);
        metadata["sourceEntityVersion"] = JsonValue.Create(sourceEntityVersion);
        metadata["correlationId"] = JsonValue.Create(correlationId);
        metadata["eventId"] = JsonValue.Create(command.EventId);
        metadata["causationId"] = JsonValue.Create(command.CausationId);
        metadata["triggerMessageId"] = JsonValue.Create(command.TriggerMessageId);
        metadata["registeredChecks"] = BuildJsonArray(registeredChecks);
        metadata["executedChecks"] = BuildJsonArray(executedChecks);
        metadata["duplicateChecks"] = BuildJsonArray(duplicateChecks);

        return JsonSerializer.Serialize(metadata);
    }

    private static string BuildCheckMetadataJson(
        ProcessFinanceWorkflowTriggerCommand command,
        FinanceWorkflowTriggerExecution execution,
        string triggerType,
        string sourceEntityType,
        string sourceEntityId,
        string sourceEntityVersion,
        string correlationId,
        string checkCode)
    {
        var metadata = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if (command.Metadata is not null)
        {
            foreach (var pair in command.Metadata)
            {
                metadata[pair.Key] = pair.Value?.DeepClone();
            }
        }

        metadata["triggerExecutionId"] = JsonValue.Create(execution.Id);
        metadata["triggerType"] = JsonValue.Create(triggerType);
        metadata["sourceEntityType"] = JsonValue.Create(sourceEntityType);
        metadata["sourceEntityId"] = JsonValue.Create(sourceEntityId);
        metadata["sourceEntityVersion"] = JsonValue.Create(sourceEntityVersion);
        metadata["checkType"] = JsonValue.Create(checkCode);
        metadata["correlationId"] = JsonValue.Create(correlationId);
        metadata["eventId"] = JsonValue.Create(command.EventId);
        metadata["causationId"] = JsonValue.Create(command.CausationId);
        metadata["triggerMessageId"] = JsonValue.Create(command.TriggerMessageId);
        return JsonSerializer.Serialize(metadata);
    }

    private static JsonArray BuildJsonArray(IEnumerable<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            array.Add(value);
        }

        return array;
    }
}
