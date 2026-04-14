using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Alerts;

public sealed record CreateAlertCommand(
    Guid CompanyId,
    string Type,
    string Severity,
    string Title,
    string Summary,
    Dictionary<string, JsonNode?>? Evidence,
    string CorrelationId,
    string Fingerprint,
    Guid? SourceAgentId,
    Dictionary<string, JsonNode?>? Metadata);

public sealed record CreateDetectionAlertCommand(
    Guid CompanyId,
    string Type,
    string Severity,
    string Title,
    string Summary,
    Dictionary<string, JsonNode?>? Evidence,
    string CorrelationId,
    string Fingerprint,
    Guid? SourceAgentId,
    Dictionary<string, JsonNode?>? Metadata);

public sealed record UpdateAlertCommand(
    string Severity,
    string Title,
    string Summary,
    Dictionary<string, JsonNode?>? Evidence,
    string Status,
    Dictionary<string, JsonNode?>? Metadata);

public sealed record ListAlertsQuery(
    string? Type,
    string? Severity,
    string? Status,
    DateTime? CreatedFrom,
    DateTime? CreatedTo,
    int? Page,
    int? PageSize,
    int? Skip,
    int? Take);

public sealed record AlertDto(
    Guid Id,
    Guid CompanyId,
    string Type,
    string Severity,
    string Title,
    string Summary,
    Dictionary<string, JsonNode?> Evidence,
    string Status,
    string CorrelationId,
    string Fingerprint,
    Guid? SourceAgentId,
    int OccurrenceCount,
    Dictionary<string, JsonNode?> Metadata,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? LastDetectedAt,
    DateTime? ResolvedAt,
    DateTime? ClosedAt);

public sealed record AlertMutationResultDto(AlertDto Alert, bool Created, bool Deduplicated);

public sealed record AlertListResultDto(
    IReadOnlyList<AlertDto> Items,
    int TotalCount,
    int Skip,
    int Take,
    int Page,
    int PageSize,
    int TotalPages);

public interface ICompanyAlertService
{
    Task<AlertMutationResultDto> CreateAsync(Guid companyId, CreateAlertCommand command, CancellationToken cancellationToken);
    Task<AlertMutationResultDto> CreateOrDeduplicateFromDetectionAsync(Guid companyId, CreateDetectionAlertCommand command, CancellationToken cancellationToken);
    Task<AlertDto> GetByIdAsync(Guid companyId, Guid alertId, CancellationToken cancellationToken);
    Task<AlertListResultDto> ListAsync(Guid companyId, ListAlertsQuery query, CancellationToken cancellationToken);
    Task<AlertDto> UpdateAsync(Guid companyId, Guid alertId, UpdateAlertCommand command, CancellationToken cancellationToken);
    Task DeleteAsync(Guid companyId, Guid alertId, CancellationToken cancellationToken);
}

public sealed class AlertValidationException : Exception
{
    public AlertValidationException(IDictionary<string, string[]> errors)
        : base("Alert validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
