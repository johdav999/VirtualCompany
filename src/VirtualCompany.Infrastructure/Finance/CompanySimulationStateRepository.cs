using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class EfCompanySimulationStateRepository : ICompanySimulationStateRepository
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ILogger<EfCompanySimulationStateRepository> _logger;

    public EfCompanySimulationStateRepository(
        VirtualCompanyDbContext dbContext,
        ILogger<EfCompanySimulationStateRepository> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task<CompanySimulationState?> GetCurrentAsync(Guid companyId, CancellationToken cancellationToken) =>
        _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

    public Task<CompanySimulationState?> GetByActiveSessionAsync(Guid companyId, Guid activeSessionId, CancellationToken cancellationToken) =>
        _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.ActiveSessionId.HasValue &&
                     x.ActiveSessionId.Value == activeSessionId,
                cancellationToken);

    public async Task<IReadOnlyList<CompanySimulationRunHistory>> GetRecentHistoryAsync(Guid companyId, int limit, CancellationToken cancellationToken) =>
        await _dbContext.Set<CompanySimulationRunHistory>()
            .IgnoreQueryFilters()
            .Include(x => x.StatusTransitions.OrderBy(y => y.TransitionedUtc))
            .Include(x => x.DayLogs.OrderBy(y => y.SimulatedDateUtc))
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.StartedUtc)
            .Take(Math.Max(1, limit))
            .ToListAsync(cancellationToken);

    public async Task<CompanySimulationState> StartAsync(
        StartCompanySimulationStateCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompany(command.CompanyId);

        var transitionedUtc = NormalizeUtc(command.TransitionedUtc ?? DateTime.UtcNow);
        var sessionId = command.SessionId ?? Guid.NewGuid();
        var existing = await GetCurrentAsync(command.CompanyId, cancellationToken);
        _logger.LogInformation(
            "Simulation repository start requested. CompanyId: {CompanyId}. ExistingStateFound: {ExistingStateFound}. ExistingStatus: {ExistingStatus}. ExistingSessionId: {ExistingSessionId}. NewSessionId: {NewSessionId}. StartSimulatedUtc: {StartSimulatedUtc}. GenerationEnabled: {GenerationEnabled}. Seed: {Seed}.",
            command.CompanyId,
            existing is not null,
            existing?.Status,
            existing?.ActiveSessionId,
            sessionId,
            command.StartSimulatedUtc,
            command.GenerationEnabled,
            command.Seed);

        if (existing is null)
        {
            existing = new CompanySimulationState(
                Guid.NewGuid(),
                command.CompanyId,
                sessionId,
                CompanySimulationStatus.Running,
                command.StartSimulatedUtc,
                command.StartSimulatedUtc,
                transitionedUtc,
                command.GenerationEnabled,
                command.Seed,
                command.DeterministicConfigurationJson,
                transitionedUtc,
                transitionedUtc);
            await _dbContext.CompanySimulationStates.AddAsync(existing, cancellationToken);
        }
        else
        {
            existing.StartNewSession(
                sessionId,
                command.StartSimulatedUtc,
                command.GenerationEnabled,
                command.Seed,
                command.DeterministicConfigurationJson,
                transitionedUtc);
        }

        var runHistory = new CompanySimulationRunHistory(
            Guid.NewGuid(),
            command.CompanyId,
            sessionId,
            CompanySimulationStatus.Running,
            transitionedUtc,
            command.StartSimulatedUtc,
            command.StartSimulatedUtc,
            command.GenerationEnabled,
            command.Seed,
            command.DeterministicConfigurationJson,
            transitionedUtc,
            transitionedUtc);
        runHistory.ApplyLifecycleUpdate(CompanySimulationStatus.Running, command.StartSimulatedUtc, transitionedUtc, "Simulation started.");
        await _dbContext.AddAsync(runHistory, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Simulation repository start persisted. CompanyId: {CompanyId}. StateId: {StateId}. SessionId: {SessionId}. Status: {Status}. CurrentSimulatedUtc: {CurrentSimulatedUtc}. LastProgressedUtc: {LastProgressedUtc}. RunHistoryId: {RunHistoryId}.",
            existing.CompanyId,
            existing.Id,
            existing.ActiveSessionId,
            existing.Status,
            existing.CurrentSimulatedUtc,
            existing.LastProgressedUtc,
            runHistory.Id);

        return existing;
    }

    public async Task<CompanySimulationState> SaveStoppedDraftAsync(
        SaveCompanySimulationStoppedDraftCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompany(command.CompanyId);

        var updatedUtc = NormalizeUtc(command.UpdatedUtc ?? DateTime.UtcNow);
        var referenceSimulatedUtc = NormalizeUtc(command.ReferenceSimulatedUtc);
        var existing = await GetCurrentAsync(command.CompanyId, cancellationToken);

        if (existing is null)
        {
            existing = new CompanySimulationState(
                Guid.NewGuid(),
                command.CompanyId,
                Guid.Empty,
                CompanySimulationStatus.Stopped,
                referenceSimulatedUtc,
                referenceSimulatedUtc,
                lastProgressedUtc: null,
                command.GenerationEnabled,
                command.Seed,
                command.DeterministicConfigurationJson,
                createdUtc: updatedUtc,
                updatedUtc: updatedUtc,
                stoppedUtc: updatedUtc);

            await _dbContext.CompanySimulationStates.AddAsync(existing, cancellationToken);
        }
        else
        {
            existing.UpdateStoppedDraft(command.GenerationEnabled, command.DeterministicConfigurationJson, command.Seed, updatedUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<CompanySimulationState> UpdateAsync(
        UpdateCompanySimulationStateCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompany(command.CompanyId);
        var state = await RequireCurrentAsync(command.CompanyId, cancellationToken);
        state.Update(
            command.CurrentSimulatedUtc,
            command.LastProgressedUtc,
            command.GenerationEnabled,
            command.DeterministicConfigurationJson,
            command.UpdatedUtc ?? DateTime.UtcNow);

        if (state.ActiveSessionId is Guid activeSessionId)
        {
            var runHistory = await GetRunHistoryAsync(command.CompanyId, activeSessionId, cancellationToken);
            if (runHistory is not null)
            {
                runHistory.ApplyLifecycleUpdate(state.Status, state.CurrentSimulatedUtc, command.UpdatedUtc ?? DateTime.UtcNow);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<ProgressCompanySimulationStateResult> TryProgressAsync(
        UpdateCompanySimulationStateCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompany(command.CompanyId);

        var updatedUtc = NormalizeUtc(command.UpdatedUtc ?? DateTime.UtcNow);
        var currentSimulatedUtc = NormalizeUtc(command.CurrentSimulatedUtc);
        var lastProgressedUtc = command.LastProgressedUtc.HasValue
            ? NormalizeUtc(command.LastProgressedUtc.Value)
            : (DateTime?)null;
        var expectedCurrentSimulatedUtc = command.ExpectedCurrentSimulatedUtc.HasValue
            ? NormalizeUtc(command.ExpectedCurrentSimulatedUtc.Value)
            : (DateTime?)null;
        var expectedLastProgressedUtc = command.ExpectedLastProgressedUtc.HasValue
            ? NormalizeUtc(command.ExpectedLastProgressedUtc.Value)
            : (DateTime?)null;

        var rowsAffected = await _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == command.CompanyId &&
                x.Status != CompanySimulationStatus.Stopped &&
                x.ActiveSessionId.HasValue &&
                x.CurrentSimulatedUtc == expectedCurrentSimulatedUtc &&
                x.LastProgressedUtc == expectedLastProgressedUtc)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.CurrentSimulatedUtc, currentSimulatedUtc)
                    .SetProperty(x => x.LastProgressedUtc, lastProgressedUtc)
                    .SetProperty(x => x.UpdatedUtc, updatedUtc),
                cancellationToken);

        var state = await _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken);
        if (rowsAffected <= 0 || state?.ActiveSessionId is not Guid activeSessionId)
        {
            return new ProgressCompanySimulationStateResult(state, false);
        }

        await _dbContext.CompanySimulationRunHistories
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == command.CompanyId && x.SessionId == activeSessionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.CurrentSimulatedUtc, state.CurrentSimulatedUtc)
                    .SetProperty(x => x.UpdatedUtc, updatedUtc),
                cancellationToken);

        return new ProgressCompanySimulationStateResult(state, true);
    }

    public async Task<CompanySimulationState> PauseAsync(
        PauseCompanySimulationStateCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompany(command.CompanyId);
        var state = await RequireCurrentAsync(command.CompanyId, cancellationToken);
        var sessionId = state.ActiveSessionId;
        state.Pause(command.PausedUtc ?? DateTime.UtcNow);
        if (sessionId.HasValue)
        {
            var runHistory = await GetRunHistoryAsync(command.CompanyId, sessionId.Value, cancellationToken);
            if (runHistory is not null)
            {
                runHistory.ApplyLifecycleUpdate(CompanySimulationStatus.Paused, state.CurrentSimulatedUtc, command.PausedUtc ?? DateTime.UtcNow, "Simulation paused.");
            }
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<CompanySimulationState> ResumeAsync(
        ResumeCompanySimulationStateCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompany(command.CompanyId);
        var state = await RequireCurrentAsync(command.CompanyId, cancellationToken);
        var sessionId = state.ActiveSessionId;
        state.Resume(command.ResumedUtc ?? DateTime.UtcNow);
        if (sessionId.HasValue)
        {
            var runHistory = await GetRunHistoryAsync(command.CompanyId, sessionId.Value, cancellationToken);
            if (runHistory is not null)
            {
                runHistory.ApplyLifecycleUpdate(CompanySimulationStatus.Running, state.CurrentSimulatedUtc, command.ResumedUtc ?? DateTime.UtcNow, "Simulation resumed.");
            }
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return state;
    }

    public async Task<CompanySimulationState> StopAsync(
        StopCompanySimulationStateCommand command,
        CancellationToken cancellationToken)
    {
        ValidateCompany(command.CompanyId);
        var stoppedUtc = NormalizeUtc(command.StoppedUtc ?? DateTime.UtcNow);

        _dbContext.ChangeTracker.Clear();

        var current = await _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId, cancellationToken);

        if (current is null)
        {
            throw new KeyNotFoundException($"Simulation state for company '{command.CompanyId}' was not found.");
        }

        if (current.Status != CompanySimulationStatus.Running && current.Status != CompanySimulationStatus.Paused)
        {
            return current;
        }

        var sessionId = current.ActiveSessionId;

        var rowsAffected = await _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == command.CompanyId &&
                x.ActiveSessionId == current.ActiveSessionId &&
                (x.Status == CompanySimulationStatus.Running || x.Status == CompanySimulationStatus.Paused))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.Status, CompanySimulationStatus.Stopped)
                    .SetProperty(x => x.StoppedUtc, stoppedUtc)
                    .SetProperty(x => x.ActiveSessionId, (Guid?)null)
                    .SetProperty(x => x.PausedUtc, (DateTime?)null)
                    .SetProperty(x => x.UpdatedUtc, stoppedUtc),
                cancellationToken);

        var state = await _dbContext.CompanySimulationStates
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == command.CompanyId, cancellationToken);

        if (rowsAffected > 0 && sessionId.HasValue)
        {
            var runHistoryId = await _dbContext.CompanySimulationRunHistories
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId && x.SessionId == sessionId.Value)
                .Select(x => (Guid?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);

            await _dbContext.CompanySimulationRunHistories
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == command.CompanyId && x.SessionId == sessionId.Value)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.Status, CompanySimulationStatus.Stopped)
                        .SetProperty(x => x.CompletedUtc, stoppedUtc)
                        .SetProperty(x => x.CurrentSimulatedUtc, state.CurrentSimulatedUtc)
                        .SetProperty(x => x.UpdatedUtc, stoppedUtc),
                    cancellationToken);

            if (runHistoryId.HasValue)
            {
                await _dbContext.CompanySimulationRunTransitions.AddAsync(
                    new CompanySimulationRunTransition(
                        Guid.NewGuid(),
                        command.CompanyId,
                        runHistoryId.Value,
                        sessionId.Value,
                        CompanySimulationStatus.Stopped,
                        stoppedUtc,
                        "Simulation stopped.",
                        stoppedUtc),
                    cancellationToken);

                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        return state;
    }

    public async Task RecordFinanceGenerationAsync(
        Guid companyId,
        Guid sessionId,
        CompanySimulationFinanceGenerationResultDto result,
        DateTime currentSimulatedUtc,
        CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();

        var runHistory = await _dbContext.Set<CompanySimulationRunHistory>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId)
            .Select(x => new
            {
                x.Id,
                x.InjectedAnomalies,
                x.Warnings,
                x.Errors
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (runHistory is null)
        {
            return;
        }

        var observedUtc = NormalizeUtc(DateTime.UtcNow);
        var dailyLogs = result.DailyLogs ?? [];
        var aggregatedAnomalies = MergeDistinct(
            runHistory.InjectedAnomalies,
            dailyLogs.SelectMany(x => x.InjectedAnomalies ?? []));
        var aggregatedWarnings = MergeDistinct(
            runHistory.Warnings,
            dailyLogs.SelectMany(x => x.Warnings ?? []));
        var aggregatedErrors = MergeDistinct(
            runHistory.Errors,
            dailyLogs.SelectMany(x => x.Errors ?? []));

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE [company_simulation_run_histories]
            SET [current_simulated_at] = {currentSimulatedUtc},
                [injected_anomalies_json] = {JsonSerializer.Serialize(aggregatedAnomalies)},
                [warnings_json] = {JsonSerializer.Serialize(aggregatedWarnings)},
                [errors_json] = {JsonSerializer.Serialize(aggregatedErrors)},
                [updated_at] = {observedUtc}
            WHERE [id] = {runHistory.Id};
            """,
            cancellationToken);

        foreach (var log in dailyLogs)
        {
            var simulatedDateUtc = NormalizeUtc(log.SimulatedDateUtc);
            var updatedRows = await _dbContext.Set<CompanySimulationRunDayLog>()
                .IgnoreQueryFilters()
                .Where(x =>
                    x.CompanyId == companyId &&
                    x.SessionId == sessionId &&
                    x.SimulatedDateUtc == simulatedDateUtc)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.TransactionsGenerated, log.TransactionsCreated)
                        .SetProperty(x => x.InvoicesGenerated, log.InvoicesCreated)
                        .SetProperty(x => x.AssetPurchasesGenerated, log.AssetPurchasesCreated)
                        .SetProperty(x => x.BillsGenerated, log.BillsCreated)
                        .SetProperty(x => x.RecurringExpenseInstancesGenerated, log.RecurringExpenseInstancesCreated)
                        .SetProperty(x => x.AlertsGenerated, log.AlertsCreated)
                        .SetProperty(x => x.InjectedAnomalies, NormalizeDistinct(log.InjectedAnomalies))
                        .SetProperty(x => x.Warnings, NormalizeDistinct(log.Warnings))
                        .SetProperty(x => x.Errors, NormalizeDistinct(log.Errors))
                        .SetProperty(x => x.UpdatedUtc, observedUtc),
                    cancellationToken);

            if (updatedRows > 0)
            {
                continue;
            }

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"""
                IF NOT EXISTS (
                    SELECT 1
                    FROM [company_simulation_run_day_logs]
                    WHERE [company_id] = {companyId}
                      AND [session_id] = {sessionId}
                      AND [simulated_date_at] = {simulatedDateUtc})
                BEGIN
                    INSERT INTO [company_simulation_run_day_logs]
                        ([id], [company_id], [run_history_id], [session_id], [simulated_date_at], [transactions_generated],
                         [invoices_generated], [asset_purchases_generated], [bills_generated], [recurring_expense_instances_generated], [alerts_generated],
                         [injected_anomalies_json], [warnings_json], [errors_json], [created_at], [updated_at])
                    VALUES
                        ({CreateDeterministicGuid(companyId, $"run-day:{sessionId:N}:{simulatedDateUtc:yyyyMMdd}")}, {companyId}, {runHistory.Id}, {sessionId}, {simulatedDateUtc}, {Math.Max(0, log.TransactionsCreated)},
                         {Math.Max(0, log.InvoicesCreated)}, {Math.Max(0, log.AssetPurchasesCreated)}, {Math.Max(0, log.BillsCreated)}, {Math.Max(0, log.RecurringExpenseInstancesCreated)}, {Math.Max(0, log.AlertsCreated)},
                         {JsonSerializer.Serialize(NormalizeDistinct(log.InjectedAnomalies))}, {JsonSerializer.Serialize(NormalizeDistinct(log.Warnings))}, {JsonSerializer.Serialize(NormalizeDistinct(log.Errors))}, {observedUtc}, {observedUtc});
                END
                """,
                cancellationToken);
        }
    }

    private Task<CompanySimulationRunHistory?> GetRunHistoryAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken) =>
        _dbContext.Set<CompanySimulationRunHistory>()
            .IgnoreQueryFilters()
            .Include(x => x.StatusTransitions)
            .Include(x => x.DayLogs)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId, cancellationToken);

    private async Task<CompanySimulationState> RequireCurrentAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var state = await GetCurrentAsync(companyId, cancellationToken);
        return state ?? throw new KeyNotFoundException($"Simulation state for company '{companyId}' was not found.");
    }

    private static void ValidateCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }
    }

    private static List<string> MergeDistinct(IEnumerable<string>? existing, IEnumerable<string>? incoming) =>
        NormalizeDistinct((existing ?? []).Concat(incoming ?? []));

    private static List<string> NormalizeDistinct(IEnumerable<string>? values) =>
        values?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];

    private static Guid CreateDeterministicGuid(Guid companyId, string scope)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{companyId:N}:{scope}")));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}
