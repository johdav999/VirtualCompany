using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Alerts;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyAlertService : ICompanyAlertService
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;

    public CompanyAlertService(VirtualCompanyDbContext dbContext, ICompanyMembershipContextResolver membershipContextResolver)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
    }

    public async Task<AlertMutationResultDto> CreateAsync(Guid companyId, CreateAlertCommand command, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        ValidateTenant(companyId, command.CompanyId);
        Validate(command.Type, command.Severity, AlertStatusValues.DefaultStatus.ToStorageValue(), command.Title, command.Summary, command.Evidence, command.CorrelationId, command.Fingerprint, command.SourceAgentId, requireFingerprint: true);

        var type = AlertTypeValues.Parse(command.Type);
        var severity = AlertSeverityValues.Parse(command.Severity);
        var existing = await FindOpenByFingerprintAsync(companyId, command.Fingerprint, cancellationToken);
        if (existing is not null)
        {
            existing.RefreshFromDuplicateDetection(severity, command.Title, command.Summary, command.Evidence!, command.CorrelationId, command.SourceAgentId, command.Metadata);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new AlertMutationResultDto(ToDto(existing), Created: false, Deduplicated: true);
        }

        var alert = new Alert(Guid.NewGuid(), companyId, type, severity, command.Title, command.Summary, command.Evidence!, command.CorrelationId, command.Fingerprint, command.SourceAgentId, command.Metadata);
        _dbContext.Alerts.Add(alert);
        var deduplicated = await SaveCreateAsync(alert, cancellationToken);
        if (deduplicated is not null)
        {
            return deduplicated;
        }

        return new AlertMutationResultDto(ToDto(alert), Created: true, Deduplicated: false);
    }

    public async Task<AlertMutationResultDto> CreateOrDeduplicateFromDetectionAsync(Guid companyId, CreateDetectionAlertCommand command, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        ValidateTenant(companyId, command.CompanyId);
        Validate(command.Type, command.Severity, AlertStatusValues.DefaultStatus.ToStorageValue(), command.Title, command.Summary, command.Evidence, command.CorrelationId, command.Fingerprint, command.SourceAgentId, requireFingerprint: true);

        var type = AlertTypeValues.Parse(command.Type);
        var severity = AlertSeverityValues.Parse(command.Severity);
        var existing = await FindOpenByFingerprintAsync(companyId, command.Fingerprint, cancellationToken);
        if (existing is not null)
        {
            existing.RefreshFromDuplicateDetection(severity, command.Title, command.Summary, command.Evidence!, command.CorrelationId, command.SourceAgentId, command.Metadata);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new AlertMutationResultDto(ToDto(existing), Created: false, Deduplicated: true);
        }

        var alert = new Alert(Guid.NewGuid(), companyId, type, severity, command.Title, command.Summary, command.Evidence!, command.CorrelationId, command.Fingerprint, command.SourceAgentId, command.Metadata);
        _dbContext.Alerts.Add(alert);
        var deduplicated = await SaveCreateAsync(alert, cancellationToken);
        if (deduplicated is not null)
        {
            return deduplicated;
        }

        return new AlertMutationResultDto(ToDto(alert), Created: true, Deduplicated: false);
    }

    public async Task<AlertDto> GetByIdAsync(Guid companyId, Guid alertId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var alert = await _dbContext.Alerts.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == alertId, cancellationToken);
        return alert is null ? throw new KeyNotFoundException("Alert not found.") : ToDto(alert);
    }

    public async Task<AlertListResultDto> ListAsync(Guid companyId, ListAlertsQuery query, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(query);

        var alerts = _dbContext.Alerts.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            var type = AlertTypeValues.Parse(query.Type);
            alerts = alerts.Where(x => x.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.Severity))
        {
            var severity = AlertSeverityValues.Parse(query.Severity);
            alerts = alerts.Where(x => x.Severity == severity);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            var status = AlertStatusValues.Parse(query.Status);
            alerts = alerts.Where(x => x.Status == status);
        }

        if (query.CreatedFrom.HasValue)
        {
            alerts = alerts.Where(x => x.CreatedUtc >= query.CreatedFrom.Value);
        }

        if (query.CreatedTo.HasValue)
        {
            alerts = alerts.Where(x => x.CreatedUtc <= query.CreatedTo.Value);
        }

        var totalCount = await alerts.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize ?? query.Take ?? DefaultPageSize, 1, MaxPageSize);
        var skip = query.Page.HasValue
            ? (query.Page.Value - 1) * pageSize
            : Math.Max(0, query.Skip ?? 0);
        var page = query.Page ?? (skip / pageSize) + 1;
        var items = await alerts
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .Skip(skip)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        return new AlertListResultDto(items.Select(ToDto).ToList(), totalCount, skip, pageSize, page, pageSize, totalPages);
    }

    public async Task<AlertDto> UpdateAsync(Guid companyId, Guid alertId, UpdateAlertCommand command, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        Validate(null, command.Severity, command.Status, command.Title, command.Summary, command.Evidence, null, null, null, requireFingerprint: false);

        var alert = await _dbContext.Alerts.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == alertId, cancellationToken);
        if (alert is null)
        {
            throw new KeyNotFoundException("Alert not found.");
        }

        alert.UpdateDetails(AlertSeverityValues.Parse(command.Severity), command.Title, command.Summary, command.Evidence!, command.Metadata);
        alert.UpdateStatus(AlertStatusValues.Parse(command.Status));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(alert);
    }

    public async Task DeleteAsync(Guid companyId, Guid alertId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);
        var alert = await _dbContext.Alerts.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == alertId, cancellationToken);
        if (alert is null)
        {
            throw new KeyNotFoundException("Alert not found.");
        }

        _dbContext.Alerts.Remove(alert);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Alert?> FindOpenByFingerprintAsync(Guid companyId, string fingerprint, CancellationToken cancellationToken)
    {
        var normalizedFingerprint = fingerprint.Trim();
        return await _dbContext.Alerts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Fingerprint == normalizedFingerprint)
            .Where(x => x.Status == AlertStatus.Open || x.Status == AlertStatus.Acknowledged)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<AlertMutationResultDto?> SaveCreateAsync(Alert alert, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (DbUpdateException ex) when (IsDedupUniqueViolation(ex))
        {
            _dbContext.ChangeTracker.Clear();
            var existing = await FindOpenByFingerprintAsync(alert.CompanyId, alert.Fingerprint, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            existing.RefreshFromDuplicateDetection(alert.Severity, alert.Title, alert.Summary, alert.Evidence, alert.CorrelationId, alert.SourceAgentId, alert.Metadata);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new AlertMutationResultDto(ToDto(existing), Created: false, Deduplicated: true);
        }
    }

    private async Task RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (await _membershipContextResolver.ResolveAsync(companyId, cancellationToken) is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }
    }

    private static AlertDto ToDto(Alert alert) =>
        new(
            alert.Id,
            alert.CompanyId,
            alert.Type.ToStorageValue(),
            alert.Severity.ToStorageValue(),
            alert.Title,
            alert.Summary,
            CloneNodes(alert.Evidence),
            alert.Status.ToStorageValue(),
            alert.CorrelationId,
            alert.Fingerprint,
            alert.SourceAgentId,
            alert.OccurrenceCount,
            CloneNodes(alert.Metadata),
            alert.CreatedUtc,
            alert.UpdatedUtc,
            alert.LastDetectedUtc,
            alert.ResolvedUtc,
            alert.ClosedUtc);

    private static Dictionary<string, JsonNode?> CloneNodes(IReadOnlyDictionary<string, JsonNode?> nodes) =>
        nodes.Count == 0
            ? new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            : nodes.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.OrdinalIgnoreCase);

    private static void ValidateTenant(Guid routeCompanyId, Guid requestCompanyId)
    {
        if (requestCompanyId == Guid.Empty || requestCompanyId != routeCompanyId)
        {
            throw new AlertValidationException(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(CreateAlertCommand.CompanyId)] = ["CompanyId is required and must match the route companyId."]
            });
        }
    }

    private static void Validate(ListAlertsQuery query)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(query.Type) && !AlertTypeValues.TryParse(query.Type, out _))
        {
            AddError(errors, nameof(query.Type), AlertTypeValues.BuildValidationMessage(query.Type));
        }

        if (!string.IsNullOrWhiteSpace(query.Severity) && !AlertSeverityValues.TryParse(query.Severity, out _))
        {
            AddError(errors, nameof(query.Severity), AlertSeverityValues.BuildValidationMessage(query.Severity));
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && !AlertStatusValues.TryParse(query.Status, out _))
        {
            AddError(errors, nameof(query.Status), AlertStatusValues.BuildValidationMessage(query.Status));
        }

        if (query.CreatedFrom.HasValue && query.CreatedTo.HasValue && query.CreatedFrom.Value > query.CreatedTo.Value)
        {
            AddError(errors, nameof(query.CreatedFrom), "CreatedFrom must be earlier than or equal to CreatedTo.");
        }

        if (query.Page is <= 0)
        {
            AddError(errors, nameof(query.Page), "Page must be 1 or greater.");
        }

        if (query.PageSize is <= 0 or > MaxPageSize)
        {
            AddError(errors, nameof(query.PageSize), $"PageSize must be between 1 and {MaxPageSize}.");
        }

        if (query.Skip is < 0)
        {
            AddError(errors, nameof(query.Skip), "Skip must be zero or greater.");
        }

        if (!query.PageSize.HasValue && query.Take is <= 0 or > MaxPageSize)
        {
            AddError(errors, nameof(query.Take), $"Take must be between 1 and {MaxPageSize}.");
        }

        ThrowIfInvalid(errors);
    }

    private static void Validate(string? type, string severity, string status, string title, string summary, IReadOnlyDictionary<string, JsonNode?>? evidence, string? correlationId, string? fingerprint, Guid? sourceAgentId, bool requireFingerprint)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (type is not null && !AlertTypeValues.TryParse(type, out _))
        {
            AddError(errors, nameof(CreateAlertCommand.Type), AlertTypeValues.BuildValidationMessage(type));
        }

        if (!AlertSeverityValues.TryParse(severity, out _))
        {
            AddError(errors, nameof(CreateAlertCommand.Severity), AlertSeverityValues.BuildValidationMessage(severity));
        }

        if (!AlertStatusValues.TryParse(status, out _))
        {
            AddError(errors, nameof(UpdateAlertCommand.Status), AlertStatusValues.BuildValidationMessage(status));
        }

        AddRequired(errors, nameof(CreateAlertCommand.Title), title, 200);
        AddRequired(errors, nameof(CreateAlertCommand.Summary), summary, 2000);
        if (evidence is null || evidence.Count == 0)
        {
            AddError(errors, nameof(CreateAlertCommand.Evidence), "Evidence is required.");
        }

        if (correlationId is not null)
        {
            AddRequired(errors, nameof(CreateAlertCommand.CorrelationId), correlationId, 128);
        }

        if (requireFingerprint)
        {
            AddRequired(errors, nameof(CreateAlertCommand.Fingerprint), fingerprint, 256);
        }

        if (sourceAgentId == Guid.Empty)
        {
            AddError(errors, nameof(CreateAlertCommand.SourceAgentId), "SourceAgentId cannot be empty.");
        }

        ThrowIfInvalid(errors);
    }

    private static void AddRequired(IDictionary<string, List<string>> errors, string key, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, key, $"{key} is required.");
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            AddError(errors, key, $"{key} must be {maxLength} characters or fewer.");
        }
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = [];
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private static void ThrowIfInvalid(IDictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
        {
            throw new AlertValidationException(errors.ToDictionary(x => x.Key, x => x.Value.ToArray(), StringComparer.OrdinalIgnoreCase));
        }
    }

    private static bool IsDedupUniqueViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("alerts", StringComparison.OrdinalIgnoreCase) &&
                (current.Message.Contains("fingerprint", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}
