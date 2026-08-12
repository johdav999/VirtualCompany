using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class CompanyGoal : ICompanyOwnedEntity
{
    private CompanyGoal() { }

    public CompanyGoal(Guid id, Guid companyId, string name, string outcome, CompanyGoalPriority priority,
        DateTime startUtc, DateTime targetUtc, string? metricKey = null, string? metricUnit = null,
        decimal? baselineValue = null, decimal? targetValue = null, Guid? ownerUserId = null,
        Guid? ownerAgentId = null, IDictionary<string, JsonNode?>? constraints = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (targetUtc <= startUtc) throw new ArgumentException("Target date must be after the start date.", nameof(targetUtc));
        if (ownerUserId == Guid.Empty || ownerAgentId == Guid.Empty) throw new ArgumentException("Owner ids cannot be empty.");
        if (targetValue.HasValue && string.IsNullOrWhiteSpace(metricKey)) throw new ArgumentException("A metric key is required for a numeric target.", nameof(metricKey));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = Required(name, nameof(name), 200);
        Outcome = Required(outcome, nameof(outcome), 2000);
        Priority = priority;
        StartUtc = Utc(startUtc);
        TargetUtc = Utc(targetUtc);
        MetricKey = Optional(metricKey, nameof(metricKey), 128);
        MetricUnit = Optional(metricUnit, nameof(metricUnit), 64);
        BaselineValue = baselineValue;
        TargetValue = targetValue;
        OwnerUserId = ownerUserId;
        OwnerAgentId = ownerAgentId;
        Constraints = Clone(constraints);
        Status = CompanyGoalStatus.Draft;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Outcome { get; private set; } = null!;
    public CompanyGoalStatus Status { get; private set; }
    public CompanyGoalPriority Priority { get; private set; }
    public string? MetricKey { get; private set; }
    public string? MetricUnit { get; private set; }
    public decimal? BaselineValue { get; private set; }
    public decimal? TargetValue { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime TargetUtc { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? OwnerAgentId { get; private set; }
    public Dictionary<string, JsonNode?> Constraints { get; private set; } = new(StringComparer.OrdinalIgnoreCase);
    public int Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User? OwnerUser { get; private set; }
    public Agent? OwnerAgent { get; private set; }

    public void Update(string name, string outcome, CompanyGoalPriority priority, DateTime startUtc, DateTime targetUtc,
        string? metricKey, string? metricUnit, decimal? baselineValue, decimal? targetValue,
        Guid? ownerUserId, Guid? ownerAgentId, IDictionary<string, JsonNode?>? constraints)
    {
        if (Status is CompanyGoalStatus.Completed or CompanyGoalStatus.Cancelled) throw new InvalidOperationException("Completed or cancelled goals cannot be changed.");
        if (targetUtc <= startUtc) throw new ArgumentException("Target date must be after the start date.");
        if (targetValue.HasValue && string.IsNullOrWhiteSpace(metricKey)) throw new ArgumentException("A metric key is required for a numeric target.");
        Name = Required(name, nameof(name), 200); Outcome = Required(outcome, nameof(outcome), 2000); Priority = priority;
        StartUtc = Utc(startUtc); TargetUtc = Utc(targetUtc); MetricKey = Optional(metricKey, nameof(metricKey), 128);
        MetricUnit = Optional(metricUnit, nameof(metricUnit), 64); BaselineValue = baselineValue; TargetValue = targetValue;
        OwnerUserId = ownerUserId; OwnerAgentId = ownerAgentId; Constraints = Clone(constraints); Touch();
    }

    public void Activate() { RequireStatus(CompanyGoalStatus.Draft, CompanyGoalStatus.Paused); Status = CompanyGoalStatus.Active; Touch(); }
    public void Pause() { RequireStatus(CompanyGoalStatus.Active); Status = CompanyGoalStatus.Paused; Touch(); }
    public void Complete() { RequireStatus(CompanyGoalStatus.Active); Status = CompanyGoalStatus.Completed; CompletedUtc = DateTime.UtcNow; Touch(); }
    public void Cancel() { if (Status == CompanyGoalStatus.Completed) throw new InvalidOperationException("Completed goals cannot be cancelled."); Status = CompanyGoalStatus.Cancelled; Touch(); }

    private void RequireStatus(params CompanyGoalStatus[] allowed) { if (!allowed.Contains(Status)) throw new InvalidOperationException($"Goal cannot transition from {Status.ToStorageValue()}."); }
    private void Touch() { UpdatedUtc = DateTime.UtcNow; Version++; }
    private static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
    private static string Required(string value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name); var text = value.Trim(); if (text.Length > max) throw new ArgumentOutOfRangeException(name); return text; }
    private static string? Optional(string? value, string name, int max) { if (string.IsNullOrWhiteSpace(value)) return null; var text = value.Trim(); if (text.Length > max) throw new ArgumentOutOfRangeException(name); return text; }
    private static Dictionary<string, JsonNode?> Clone(IDictionary<string, JsonNode?>? source) => source?.ToDictionary(x => x.Key, x => x.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
}

public sealed class CompanyOperatingConfiguration : ICompanyOwnedEntity
{
    private CompanyOperatingConfiguration() { }
    public CompanyOperatingConfiguration(Guid id, Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id; CompanyId = companyId; AutonomyLevel = CompanyAutonomyLevel.Recommend;
        Timezone = "UTC"; DailyCycleHour = 6; MinimumCycleIntervalMinutes = 60; MaximumCyclesPerDay = 4;
        MaximumInitiativesPerCycle = 5; MaximumTasksPerCycle = 12; MaximumCollaborators = 3;
        MaximumRuntimeSeconds = 120; MaximumModelCallsPerCycle = 4; MaximumToolCallsPerCycle = 20;
        MaximumTasksPerDay = 48; MaximumModelCallsPerDay = 16; MaximumToolCallsPerDay = 80;
        CreatedUtc = UpdatedUtc = DateTime.UtcNow; Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? CoordinatorAgentId { get; private set; }
    public CompanyAutonomyLevel AutonomyLevel { get; private set; }
    public string Timezone { get; private set; } = null!;
    public int DailyCycleHour { get; private set; }
    public int MinimumCycleIntervalMinutes { get; private set; }
    public int MaximumCyclesPerDay { get; private set; }
    public int MaximumInitiativesPerCycle { get; private set; }
    public int MaximumTasksPerCycle { get; private set; }
    public int MaximumCollaborators { get; private set; }
    public int MaximumRuntimeSeconds { get; private set; }
    public int MaximumModelCallsPerCycle { get; private set; }
    public int MaximumToolCallsPerCycle { get; private set; }
    public decimal? MaximumMonetaryBudgetPerCycle { get; private set; }
    public int MaximumTasksPerDay { get; private set; }
    public int MaximumModelCallsPerDay { get; private set; }
    public int MaximumToolCallsPerDay { get; private set; }
    public decimal? MaximumMonetaryBudgetPerDay { get; private set; }
    public bool IsPaused { get; private set; }
    public string? PauseReason { get; private set; }
    public bool EmergencyStopped { get; private set; }
    public string? EmergencyStopReason { get; private set; }
    public DateTime? EmergencyStoppedUtc { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent? CoordinatorAgent { get; private set; }

    public void Update(Guid? coordinatorAgentId, CompanyAutonomyLevel autonomyLevel, string timezone, int dailyCycleHour,
        int minimumCycleIntervalMinutes, int maximumCyclesPerDay, int maximumInitiativesPerCycle, int maximumTasksPerCycle,
        int maximumCollaborators, int maximumRuntimeSeconds, int maximumModelCallsPerCycle, int maximumToolCallsPerCycle,
        decimal? maximumMonetaryBudgetPerCycle)
    {
        if (coordinatorAgentId == Guid.Empty) throw new ArgumentException("CoordinatorAgentId cannot be empty.");
        if (string.IsNullOrWhiteSpace(timezone) || timezone.Trim().Length > 100) throw new ArgumentException("A valid timezone is required.", nameof(timezone));
        if (dailyCycleHour is < 0 or > 23) throw new ArgumentOutOfRangeException(nameof(dailyCycleHour));
        if (minimumCycleIntervalMinutes is < 5 or > 10080 || maximumCyclesPerDay is < 1 or > 48 || maximumInitiativesPerCycle is < 1 or > 50 || maximumTasksPerCycle is < 1 or > 200 || maximumCollaborators is < 1 or > 10 || maximumRuntimeSeconds is < 15 or > 3600 || maximumModelCallsPerCycle is < 1 or > 50 || maximumToolCallsPerCycle is < 1 or > 500 || maximumMonetaryBudgetPerCycle is < 0) throw new ArgumentOutOfRangeException(nameof(maximumCyclesPerDay), "Operating limits are outside supported bounds.");
        CoordinatorAgentId = coordinatorAgentId; AutonomyLevel = autonomyLevel; Timezone = timezone.Trim(); DailyCycleHour = dailyCycleHour;
        MinimumCycleIntervalMinutes = minimumCycleIntervalMinutes; MaximumCyclesPerDay = maximumCyclesPerDay;
        MaximumInitiativesPerCycle = maximumInitiativesPerCycle; MaximumTasksPerCycle = maximumTasksPerCycle; MaximumCollaborators = maximumCollaborators;
        MaximumRuntimeSeconds = maximumRuntimeSeconds; MaximumModelCallsPerCycle = maximumModelCallsPerCycle; MaximumToolCallsPerCycle = maximumToolCallsPerCycle;
        MaximumMonetaryBudgetPerCycle = maximumMonetaryBudgetPerCycle; Touch();
    }
    public void UpdateRollingLimits(int maximumTasksPerDay, int maximumModelCallsPerDay,
        int maximumToolCallsPerDay, decimal? maximumMonetaryBudgetPerDay)
    {
        if (maximumTasksPerDay is < 1 or > 2000 || maximumModelCallsPerDay is < 1 or > 500 ||
            maximumToolCallsPerDay is < 1 or > 5000 || maximumMonetaryBudgetPerDay is < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumTasksPerDay), "Daily operating limits are outside supported bounds.");
        MaximumTasksPerDay = maximumTasksPerDay; MaximumModelCallsPerDay = maximumModelCallsPerDay;
        MaximumToolCallsPerDay = maximumToolCallsPerDay; MaximumMonetaryBudgetPerDay = maximumMonetaryBudgetPerDay; Touch();
    }
    public void Pause(string reason) { if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A pause reason is required.", nameof(reason)); IsPaused = true; PauseReason = reason.Trim().Length <= 500 ? reason.Trim() : reason.Trim()[..500]; Touch(); }
    public void Resume() { if (EmergencyStopped) throw new InvalidOperationException("Clear the emergency stop before resuming company operation."); IsPaused = false; PauseReason = null; Touch(); }
    public void EmergencyStop(string reason) { if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("An emergency-stop reason is required.", nameof(reason)); EmergencyStopped = true; EmergencyStopReason = reason.Trim()[..Math.Min(reason.Trim().Length, 500)]; EmergencyStoppedUtc = DateTime.UtcNow; IsPaused = true; PauseReason = "Emergency stop active."; Touch(); }
    public void ClearEmergencyStop() { EmergencyStopped = false; EmergencyStopReason = null; EmergencyStoppedUtc = null; Touch(); }
    private void Touch() { Version++; UpdatedUtc = DateTime.UtcNow; }
}
