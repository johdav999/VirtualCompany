using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Auditing;

public sealed class AuditEventWriter : IAuditEventWriter
{
    private readonly VirtualCompanyDbContext _dbContext;

    public AuditEventWriter(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);

        _dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            auditEvent.CompanyId,
            auditEvent.ActorType,
            auditEvent.ActorId,
            auditEvent.Action,
            auditEvent.TargetType,
            auditEvent.TargetId,
            auditEvent.Outcome,
            auditEvent.RationaleSummary,
            auditEvent.DataSources,
            auditEvent.Metadata,
            auditEvent.CorrelationId,
            auditEvent.OccurredUtc));
        return Task.CompletedTask;
    }
}
