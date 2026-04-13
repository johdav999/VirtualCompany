using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.ExecutionExceptions;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class ExecutionExceptionService : IExecutionExceptionRecorder, IExecutionExceptionQueryService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICompanyOutboxEnqueuer _outboxEnqueuer;

    public ExecutionExceptionService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        IAuditEventWriter auditEventWriter,
        ICompanyOutboxEnqueuer outboxEnqueuer)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _auditEventWriter = auditEventWriter;
        _outboxEnqueuer = outboxEnqueuer;
    }

    public async Task<Guid> RecordAsync(RecordExecutionExceptionRequest request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(request));
        }

        var kind = ExecutionExceptionKindValues.Parse(request.Kind);
        var severity = ExecutionExceptionSeverityValues.Parse(request.Severity);
        var sourceType = ExecutionExceptionSourceTypeValues.Parse(request.SourceType);

        var existing = await _dbContext.ExecutionExceptionRecords
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x =>
                x.CompanyId == request.CompanyId &&
                x.IncidentKey == request.IncidentKey,
                cancellationToken);

        if (existing is not null)
        {
            existing.RefreshOpen(severity, request.Title, request.Summary, request.FailureCode, request.Details);
            return existing.Id;
        }

        var record = new ExecutionExceptionRecord(
            Guid.NewGuid(),
            request.CompanyId,
            kind,
            severity,
            request.Title,
            request.Summary,
            sourceType,
            request.SourceId,
            request.BackgroundExecutionId,
            request.RelatedEntityType,
            request.RelatedEntityId,
            request.IncidentKey,
            request.FailureCode,
            request.Details);

        _dbContext.ExecutionExceptionRecords.Add(record);
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                request.CompanyId,
                AuditActorTypes.System,
                null,
                AuditEventActions.ExecutionExceptionCreated,
                AuditTargetTypes.ExecutionException,
                record.Id.ToString("N"),
                AuditEventOutcomes.Pending,
                record.Title,
                Metadata: new Dictionary<string, string?>
                {
                    ["kind"] = record.Kind.ToStorageValue(),
                    ["severity"] = record.Severity.ToStorageValue(),
                    ["sourceType"] = record.SourceType.ToStorageValue(),
                    ["sourceId"] = record.SourceId,
                    ["backgroundExecutionId"] = record.BackgroundExecutionId?.ToString("N"),
                    ["incidentKey"] = record.IncidentKey,
                    ["failureCode"] = record.FailureCode
                }),
            cancellationToken);

        _outboxEnqueuer.Enqueue(
            request.CompanyId,
            CompanyOutboxTopics.NotificationDeliveryRequested,
            new NotificationDeliveryRequestedMessage(
                request.CompanyId,
                CompanyNotificationType.Escalation.ToStorageValue(),
                severity == ExecutionExceptionSeverity.Critical ? CompanyNotificationPriority.Critical.ToStorageValue() : CompanyNotificationPriority.High.ToStorageValue(),
                record.Title,
                record.Summary,
                AuditTargetTypes.ExecutionException,
                record.Id,
                $"/dashboard?companyId={request.CompanyId}",
                null,
                CompanyMembershipRole.Owner.ToStorageValue(),
                null,
                null,
                $"execution-exception:{record.Id:N}",
                null),
            idempotencyKey: $"notification:execution-exception:{record.Id:N}",
            causationId: record.Id.ToString("N"));

        return record.Id;
    }

    public async Task<IReadOnlyList<ExecutionExceptionDto>> ListAsync(
        Guid companyId,
        string? status,
        string? kind,
        CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var query = _dbContext.ExecutionExceptionRecords
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(x => x.Status == ExecutionExceptionStatusValues.Parse(status));
        }

        if (!string.IsNullOrWhiteSpace(kind))
        {
            query = query.Where(x => x.Kind == ExecutionExceptionKindValues.Parse(kind));
        }

        var records = await query
            .OrderByDescending(x => x.CreatedUtc)
            .Take(200)
            .ToListAsync(cancellationToken);

        return records.Select(ToDto).ToList();
    }

    public async Task<ExecutionExceptionDto> GetAsync(Guid companyId, Guid exceptionId, CancellationToken cancellationToken)
    {
        await RequireMembershipAsync(companyId, cancellationToken);

        var record = await _dbContext.ExecutionExceptionRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == exceptionId, cancellationToken);

        return record is null
            ? throw new KeyNotFoundException("Execution exception not found.")
            : ToDto(record);
    }

    private async Task RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken) is null)
        {
            throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");
        }
    }

    private static ExecutionExceptionDto ToDto(ExecutionExceptionRecord record) =>
        new(
            record.Id,
            record.CompanyId,
            record.Kind.ToStorageValue(),
            record.Severity.ToStorageValue(),
            record.Status.ToStorageValue(),
            record.Title,
            record.Summary,
            record.SourceType.ToStorageValue(),
            record.SourceId,
            record.BackgroundExecutionId,
            record.RelatedEntityType,
            record.RelatedEntityId,
            record.IncidentKey,
            record.FailureCode,
            new Dictionary<string, string?>(record.Details, StringComparer.OrdinalIgnoreCase),
            record.CreatedUtc,
            record.UpdatedUtc,
            record.ResolvedUtc);
}