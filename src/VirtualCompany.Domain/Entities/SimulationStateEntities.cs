using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Domain.Entities;

public sealed class CompanySimulationState : ICompanyOwnedEntity
{
    private const int DeterministicConfigurationMaxLength = 16000;

    private CompanySimulationState()
    {
    }

    public CompanySimulationState(
        Guid id,
        Guid companyId,
        Guid activeSessionId,
        CompanySimulationStatus status,
        DateTime startSimulatedUtc,
        DateTime currentSimulatedUtc,
        DateTime? lastProgressedUtc,
        bool generationEnabled,
        int seed,
        string? deterministicConfigurationJson,
        DateTime createdUtc,
        DateTime updatedUtc,
        DateTime? pausedUtc = null,
        DateTime? stoppedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (status != CompanySimulationStatus.Stopped &&
            activeSessionId == Guid.Empty)
        {
            throw new ArgumentException("ActiveSessionId is required.", nameof(activeSessionId));
        }

        CompanySimulationStatusValues.EnsureSupported(status, nameof(status));

        var normalizedStartSimulatedUtc = EntityTimestampNormalizer.NormalizeUtc(startSimulatedUtc, nameof(startSimulatedUtc));
        var normalizedCurrentSimulatedUtc = EntityTimestampNormalizer.NormalizeUtc(currentSimulatedUtc, nameof(currentSimulatedUtc));
        if (normalizedCurrentSimulatedUtc < normalizedStartSimulatedUtc)
        {
            throw new ArgumentException("Current simulated time cannot be before the start simulated time.", nameof(currentSimulatedUtc));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Status = status;
        StartSimulatedUtc = normalizedStartSimulatedUtc;
        CurrentSimulatedUtc = normalizedCurrentSimulatedUtc;
        LastProgressedUtc = lastProgressedUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(lastProgressedUtc.Value, nameof(lastProgressedUtc))
            : null;
        GenerationEnabled = generationEnabled;
        Seed = seed;
        ActiveSessionId = status == CompanySimulationStatus.Stopped ? null : activeSessionId;
        DeterministicConfigurationJson = NormalizeDeterministicConfiguration(deterministicConfigurationJson);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        PausedUtc = pausedUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(pausedUtc.Value, nameof(pausedUtc)) : null;
        StoppedUtc = stoppedUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(stoppedUtc.Value, nameof(stoppedUtc)) : null;

        if (Status == CompanySimulationStatus.Stopped)
        {
            ActiveSessionId = null;
        }
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public CompanySimulationStatus Status { get; private set; }
    public DateTime StartSimulatedUtc { get; private set; }
    public DateTime CurrentSimulatedUtc { get; private set; }
    public DateTime? LastProgressedUtc { get; private set; }
    public bool GenerationEnabled { get; private set; }
    public int Seed { get; private set; }
    public Guid? ActiveSessionId { get; private set; }
    public string? DeterministicConfigurationJson { get; private set; }
    public DateTime? PausedUtc { get; private set; }
    public DateTime? StoppedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<CompanySimulationRunHistory> RunHistories { get; private set; } = [];

    public bool IsRunning => Status == CompanySimulationStatus.Running;
    public bool IsPaused => Status == CompanySimulationStatus.Paused;
    public bool IsStopped => Status == CompanySimulationStatus.Stopped;

    public void StartNewSession(
        Guid activeSessionId,
        DateTime startSimulatedUtc,
        bool generationEnabled,
        int seed,
        string? deterministicConfigurationJson,
        DateTime transitionedUtc)
    {
        if (activeSessionId == Guid.Empty)
        {
            throw new ArgumentException("ActiveSessionId is required.", nameof(activeSessionId));
        }

        var normalizedTransitionedUtc = EntityTimestampNormalizer.NormalizeUtc(transitionedUtc, nameof(transitionedUtc));
        var normalizedStartSimulatedUtc = EntityTimestampNormalizer.NormalizeUtc(startSimulatedUtc, nameof(startSimulatedUtc));

        Status = CompanySimulationStatus.Running;
        StartSimulatedUtc = normalizedStartSimulatedUtc;
        CurrentSimulatedUtc = normalizedStartSimulatedUtc;
        LastProgressedUtc = normalizedTransitionedUtc;
        GenerationEnabled = generationEnabled;
        Seed = seed;
        ActiveSessionId = activeSessionId;
        DeterministicConfigurationJson = NormalizeDeterministicConfiguration(deterministicConfigurationJson);
        PausedUtc = null;
        StoppedUtc = null;
        UpdatedUtc = normalizedTransitionedUtc;
    }

    public void Update(
        DateTime currentSimulatedUtc,
        DateTime? lastProgressedUtc,
        bool? generationEnabled,
        string? deterministicConfigurationJson,
        DateTime updatedUtc)
    {
        EnsureSessionExists();

        if (Status == CompanySimulationStatus.Stopped)
        {
            throw new InvalidOperationException("Stopped simulation sessions cannot be updated.");
        }

        var normalizedUpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        var normalizedCurrentSimulatedUtc = EntityTimestampNormalizer.NormalizeUtc(currentSimulatedUtc, nameof(currentSimulatedUtc));
        if (normalizedCurrentSimulatedUtc < StartSimulatedUtc)
        {
            throw new ArgumentException("Current simulated time cannot be before the start simulated time.", nameof(currentSimulatedUtc));
        }

        CurrentSimulatedUtc = normalizedCurrentSimulatedUtc;
        LastProgressedUtc = lastProgressedUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(lastProgressedUtc.Value, nameof(lastProgressedUtc))
            : LastProgressedUtc;

        if (generationEnabled.HasValue)
        {
            GenerationEnabled = generationEnabled.Value;
        }

        if (deterministicConfigurationJson is not null)
        {
            DeterministicConfigurationJson = NormalizeDeterministicConfiguration(deterministicConfigurationJson);
        }

        UpdatedUtc = normalizedUpdatedUtc;
    }

    public void UpdateStoppedDraft(
        bool? generationEnabled,
        string? deterministicConfigurationJson,
        int? seed,
        DateTime updatedUtc)
    {
        if (Status != CompanySimulationStatus.Stopped)
        {
            throw new InvalidOperationException("Only stopped simulation drafts can be updated without an active session.");
        }

        if (generationEnabled.HasValue)
        {
            GenerationEnabled = generationEnabled.Value;
        }

        if (seed.HasValue)
        {
            Seed = seed.Value;
        }

        DeterministicConfigurationJson = deterministicConfigurationJson is null ? DeterministicConfigurationJson : NormalizeDeterministicConfiguration(deterministicConfigurationJson);
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    public void Pause(DateTime pausedUtc)
    {
        EnsureSessionExists();
        if (Status != CompanySimulationStatus.Running)
        {
            throw new InvalidOperationException("Only running simulations can be paused.");
        }

        Status = CompanySimulationStatus.Paused;
        PausedUtc = EntityTimestampNormalizer.NormalizeUtc(pausedUtc, nameof(pausedUtc));
        UpdatedUtc = PausedUtc.Value;
    }

    public void Resume(DateTime resumedUtc)
    {
        EnsureSessionExists();
        if (Status != CompanySimulationStatus.Paused)
        {
            throw new InvalidOperationException("Only paused simulations can be resumed.");
        }

        var normalizedResumedUtc = EntityTimestampNormalizer.NormalizeUtc(resumedUtc, nameof(resumedUtc));
        Status = CompanySimulationStatus.Running;
        PausedUtc = null;
        LastProgressedUtc = normalizedResumedUtc;
        UpdatedUtc = normalizedResumedUtc;
    }

    public void Stop(DateTime stoppedUtc)
    {
        EnsureSessionExists();
        if (Status != CompanySimulationStatus.Running && Status != CompanySimulationStatus.Paused)
        {
            throw new InvalidOperationException("Only running or paused simulations can be stopped.");
        }

        Status = CompanySimulationStatus.Stopped;
        StoppedUtc = EntityTimestampNormalizer.NormalizeUtc(stoppedUtc, nameof(stoppedUtc));
        ActiveSessionId = null;
        PausedUtc = null;
        UpdatedUtc = StoppedUtc.Value;
    }

    private void EnsureSessionExists()
    {
        if (ActiveSessionId is null || Status == CompanySimulationStatus.Stopped)
        {
            throw new InvalidOperationException("Simulation state does not have an active session.");
        }
    }

    private static string? NormalizeDeterministicConfiguration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > DeterministicConfigurationMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Deterministic configuration must be {DeterministicConfigurationMaxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class CompanySimulationRunHistory : ICompanyOwnedEntity
{
    private const int DeterministicConfigurationMaxLength = 16000;

    private CompanySimulationRunHistory() { }

    public CompanySimulationRunHistory(
        Guid id,
        Guid companyId,
        Guid sessionId,
        CompanySimulationStatus status,
        DateTime startedUtc,
        DateTime startSimulatedUtc,
        DateTime? currentSimulatedUtc,
        bool generationEnabled,
        int seed,
        string? deterministicConfigurationJson,
        DateTime createdUtc,
        DateTime updatedUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (sessionId == Guid.Empty) throw new ArgumentException("SessionId is required.", nameof(sessionId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SessionId = sessionId;
        Status = status;
        StartedUtc = EntityTimestampNormalizer.NormalizeUtc(startedUtc, nameof(startedUtc));
        StartSimulatedUtc = EntityTimestampNormalizer.NormalizeUtc(startSimulatedUtc, nameof(startSimulatedUtc));
        CurrentSimulatedUtc = currentSimulatedUtc.HasValue ? EntityTimestampNormalizer.NormalizeUtc(currentSimulatedUtc.Value, nameof(currentSimulatedUtc)) : null;
        GenerationEnabled = generationEnabled;
        Seed = seed;
        DeterministicConfigurationJson = NormalizeDeterministicConfiguration(deterministicConfigurationJson);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SessionId { get; private set; }
    public CompanySimulationStatus Status { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime StartSimulatedUtc { get; private set; }
    public DateTime? CurrentSimulatedUtc { get; private set; }
    public bool GenerationEnabled { get; private set; }
    public int Seed { get; private set; }
    public string? DeterministicConfigurationJson { get; private set; }
    public List<string> InjectedAnomalies { get; private set; } = [];
    public List<string> Warnings { get; private set; } = [];
    public List<string> Errors { get; private set; } = [];
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<CompanySimulationRunTransition> StatusTransitions { get; private set; } = [];
    public ICollection<CompanySimulationRunDayLog> DayLogs { get; private set; } = [];

    public void ApplyLifecycleUpdate(
        CompanySimulationStatus status,
        DateTime? currentSimulatedUtc,
        DateTime updatedUtc,
        string? message = null)
    {
        Status = status;
        CurrentSimulatedUtc = currentSimulatedUtc.HasValue
            ? EntityTimestampNormalizer.NormalizeUtc(currentSimulatedUtc.Value, nameof(currentSimulatedUtc))
            : CurrentSimulatedUtc;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
        if (status == CompanySimulationStatus.Stopped)
        {
            CompletedUtc = UpdatedUtc;
        }

        StatusTransitions.Add(new CompanySimulationRunTransition(
            Guid.NewGuid(),
            CompanyId,
            Id,
            SessionId,
            status,
            UpdatedUtc,
            message,
            UpdatedUtc));
    }

    public void RecordDailyLog(
        DateTime simulatedDateUtc,
        int transactionsCreated,
        int invoicesCreated,
        int assetPurchasesCreated,
        int billsCreated,
        int recurringExpenseInstancesCreated,
        int alertsCreated,
        IEnumerable<string> injectedAnomalies,
        IEnumerable<string> warnings,
        IEnumerable<string> errors,
        DateTime observedUtc)
    {
        var normalizedObservedUtc = EntityTimestampNormalizer.NormalizeUtc(observedUtc, nameof(observedUtc));
        var normalizedSimulatedDateUtc = EntityTimestampNormalizer.NormalizeUtc(simulatedDateUtc, nameof(simulatedDateUtc));
        var existing = DayLogs.FirstOrDefault(x => x.SimulatedDateUtc.Date == normalizedSimulatedDateUtc.Date);
        if (existing is null)
        {
            DayLogs.Add(new CompanySimulationRunDayLog(
                Guid.NewGuid(),
                CompanyId,
                Id,
                SessionId,
                normalizedSimulatedDateUtc,
                transactionsCreated,
                invoicesCreated,
                assetPurchasesCreated,
                billsCreated,
                recurringExpenseInstancesCreated,
                alertsCreated,
                injectedAnomalies,
                warnings,
                errors,
                normalizedObservedUtc));
        }
        else
        {
            existing.Update(
                transactionsCreated,
                invoicesCreated,
                assetPurchasesCreated,
                billsCreated,
                recurringExpenseInstancesCreated,
                alertsCreated,
                injectedAnomalies,
                warnings,
                errors,
                normalizedObservedUtc);
        }

        Merge(InjectedAnomalies, injectedAnomalies);
        Merge(Warnings, warnings);
        Merge(Errors, errors);
        UpdatedUtc = normalizedObservedUtc;
    }

    private static void Merge(List<string> target, IEnumerable<string> values)
    {
        foreach (var value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (!target.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                target.Add(value);
            }
        }
    }

    private static string? NormalizeDeterministicConfiguration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > DeterministicConfigurationMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Deterministic configuration must be {DeterministicConfigurationMaxLength} characters or fewer.");
        }

        return trimmed;
    }
}

public sealed class CompanySimulationRunTransition : ICompanyOwnedEntity
{
    private CompanySimulationRunTransition() { }

    public CompanySimulationRunTransition(
        Guid id,
        Guid companyId,
        Guid runHistoryId,
        Guid sessionId,
        CompanySimulationStatus status,
        DateTime transitionedUtc,
        string? message,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        RunHistoryId = runHistoryId;
        SessionId = sessionId;
        Status = status;
        TransitionedUtc = EntityTimestampNormalizer.NormalizeUtc(transitionedUtc, nameof(transitionedUtc));
        Message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunHistoryId { get; private set; }
    public Guid SessionId { get; private set; }
    public CompanySimulationStatus Status { get; private set; }
    public DateTime TransitionedUtc { get; private set; }
    public string? Message { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public CompanySimulationRunHistory RunHistory { get; private set; } = null!;
}

public sealed class CompanySimulationRunDayLog : ICompanyOwnedEntity
{
    private CompanySimulationRunDayLog() { }

    public CompanySimulationRunDayLog(
        Guid id,
        Guid companyId,
        Guid runHistoryId,
        Guid sessionId,
        DateTime simulatedDateUtc,
        int transactionsGenerated,
        int invoicesGenerated,
        int assetPurchasesGenerated,
        int billsGenerated,
        int recurringExpenseInstancesGenerated,
        int alertsGenerated,
        IEnumerable<string> injectedAnomalies,
        IEnumerable<string> warnings,
        IEnumerable<string> errors,
        DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        RunHistoryId = runHistoryId;
        SessionId = sessionId;
        SimulatedDateUtc = EntityTimestampNormalizer.NormalizeUtc(simulatedDateUtc, nameof(simulatedDateUtc));
        Update(transactionsGenerated, invoicesGenerated, assetPurchasesGenerated, billsGenerated, recurringExpenseInstancesGenerated, alertsGenerated, injectedAnomalies, warnings, errors, createdUtc);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunHistoryId { get; private set; }
    public Guid SessionId { get; private set; }
    public DateTime SimulatedDateUtc { get; private set; }
    public int TransactionsGenerated { get; private set; }
    public int InvoicesGenerated { get; private set; }
    public int AssetPurchasesGenerated { get; private set; }
    public int BillsGenerated { get; private set; }
    public int RecurringExpenseInstancesGenerated { get; private set; }
    public int AlertsGenerated { get; private set; }
    public List<string> InjectedAnomalies { get; private set; } = [];
    public List<string> Warnings { get; private set; } = [];
    public List<string> Errors { get; private set; } = [];
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public CompanySimulationRunHistory RunHistory { get; private set; } = null!;

    public void Update(
        int transactionsGenerated,
        int invoicesGenerated,
        int assetPurchasesGenerated,
        int billsGenerated,
        int recurringExpenseInstancesGenerated,
        int alertsGenerated,
        IEnumerable<string> injectedAnomalies,
        IEnumerable<string> warnings,
        IEnumerable<string> errors,
        DateTime updatedUtc)
    {
        TransactionsGenerated = Math.Max(0, transactionsGenerated);
        InvoicesGenerated = Math.Max(0, invoicesGenerated);
        AssetPurchasesGenerated = Math.Max(0, assetPurchasesGenerated);
        BillsGenerated = Math.Max(0, billsGenerated);
        RecurringExpenseInstancesGenerated = Math.Max(0, recurringExpenseInstancesGenerated);
        AlertsGenerated = Math.Max(0, alertsGenerated);
        InjectedAnomalies = injectedAnomalies.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Warnings = warnings.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Errors = errors.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }
}
