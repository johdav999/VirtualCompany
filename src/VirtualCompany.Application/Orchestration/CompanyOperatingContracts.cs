using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Orchestration;

public sealed record CompanyGoalDto(
    Guid Id, Guid CompanyId, string Name, string Outcome, string Status, string Priority,
    string? MetricKey, string? MetricUnit, decimal? BaselineValue, decimal? TargetValue,
    DateTime StartUtc, DateTime TargetUtc, Guid? OwnerUserId, Guid? OwnerAgentId,
    IReadOnlyDictionary<string, JsonNode?> Constraints, int Version, DateTime CreatedUtc,
    DateTime UpdatedUtc, DateTime? CompletedUtc);

public sealed record CreateCompanyGoalCommand(
    string Name, string Outcome, string Priority, DateTime StartUtc, DateTime TargetUtc,
    string? MetricKey = null, string? MetricUnit = null, decimal? BaselineValue = null,
    decimal? TargetValue = null, Guid? OwnerUserId = null, Guid? OwnerAgentId = null,
    Dictionary<string, JsonNode?>? Constraints = null, string? CorrelationId = null);

public sealed record UpdateCompanyGoalCommand(
    string Name, string Outcome, string Priority, DateTime StartUtc, DateTime TargetUtc,
    string? MetricKey = null, string? MetricUnit = null, decimal? BaselineValue = null,
    decimal? TargetValue = null, Guid? OwnerUserId = null, Guid? OwnerAgentId = null,
    Dictionary<string, JsonNode?>? Constraints = null, int ExpectedVersion = 0, string? CorrelationId = null);

public sealed record CompanyOperatingConfigurationDto(
    Guid Id, Guid CompanyId, Guid? CoordinatorAgentId, string AutonomyLevel, string Timezone,
    int DailyCycleHour, int MinimumCycleIntervalMinutes, int MaximumCyclesPerDay,
    int MaximumInitiativesPerCycle, int MaximumTasksPerCycle, int MaximumCollaborators,
    int MaximumRuntimeSeconds, int MaximumModelCallsPerCycle, int MaximumToolCallsPerCycle,
    decimal? MaximumMonetaryBudgetPerCycle, int MaximumTasksPerDay, int MaximumModelCallsPerDay,
    int MaximumToolCallsPerDay, decimal? MaximumMonetaryBudgetPerDay,
    bool IsPaused, string? PauseReason, bool EmergencyStopped, string? EmergencyStopReason, DateTime? EmergencyStoppedUtc,
    int Version, DateTime CreatedUtc, DateTime UpdatedUtc);

public sealed record UpdateCompanyOperatingConfigurationCommand(
    Guid? CoordinatorAgentId, string AutonomyLevel, string Timezone, int DailyCycleHour,
    int MinimumCycleIntervalMinutes, int MaximumCyclesPerDay, int MaximumInitiativesPerCycle,
    int MaximumTasksPerCycle, int MaximumCollaborators, int MaximumRuntimeSeconds,
    int MaximumModelCallsPerCycle, int MaximumToolCallsPerCycle,
    decimal? MaximumMonetaryBudgetPerCycle, int ExpectedVersion = 0, string? CorrelationId = null,
    int? MaximumTasksPerDay = null, int? MaximumModelCallsPerDay = null,
    int? MaximumToolCallsPerDay = null, decimal? MaximumMonetaryBudgetPerDay = null);

public sealed record PauseCompanyOperationCommand(string Reason, int ExpectedVersion = 0, string? CorrelationId = null);
public sealed record ResumeCompanyOperationCommand(int ExpectedVersion = 0, string? CorrelationId = null);
public sealed record EmergencyStopCompanyOperationCommand(string Reason, int ExpectedVersion = 0, string? CorrelationId = null);
public sealed record ClearEmergencyStopCommand(int ExpectedVersion = 0, string? CorrelationId = null);

public interface ICompanyGoalCommandService
{
    Task<CompanyGoalDto> CreateAsync(Guid companyId, CreateCompanyGoalCommand command, CancellationToken cancellationToken);
    Task<CompanyGoalDto> UpdateAsync(Guid companyId, Guid goalId, UpdateCompanyGoalCommand command, CancellationToken cancellationToken);
    Task<CompanyGoalDto> ActivateAsync(Guid companyId, Guid goalId, int expectedVersion, string? correlationId, CancellationToken cancellationToken);
    Task<CompanyGoalDto> PauseAsync(Guid companyId, Guid goalId, int expectedVersion, string? correlationId, CancellationToken cancellationToken);
    Task<CompanyGoalDto> CompleteAsync(Guid companyId, Guid goalId, int expectedVersion, string? correlationId, CancellationToken cancellationToken);
    Task<CompanyGoalDto> CancelAsync(Guid companyId, Guid goalId, int expectedVersion, string? correlationId, CancellationToken cancellationToken);
}

public interface ICompanyGoalQueryService
{
    Task<CompanyGoalDto> GetAsync(Guid companyId, Guid goalId, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyGoalDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken);
}

public interface ICompanyOperatingConfigurationService
{
    Task<CompanyOperatingConfigurationDto> GetAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanyOperatingConfigurationDto> UpdateAsync(Guid companyId, UpdateCompanyOperatingConfigurationCommand command, CancellationToken cancellationToken);
    Task<CompanyOperatingConfigurationDto> PauseAsync(Guid companyId, PauseCompanyOperationCommand command, CancellationToken cancellationToken);
    Task<CompanyOperatingConfigurationDto> ResumeAsync(Guid companyId, ResumeCompanyOperationCommand command, CancellationToken cancellationToken);
    Task<CompanyOperatingConfigurationDto> EmergencyStopAsync(Guid companyId, EmergencyStopCompanyOperationCommand command, CancellationToken cancellationToken);
    Task<CompanyOperatingConfigurationDto> ClearEmergencyStopAsync(Guid companyId, ClearEmergencyStopCommand command, CancellationToken cancellationToken);
}

public sealed class CompanyOperatingValidationException : Exception
{
    public CompanyOperatingValidationException(IDictionary<string, string[]> errors)
        : base("Company operating validation failed.") =>
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class CompanyOperatingConcurrencyException(string message) : Exception(message);
