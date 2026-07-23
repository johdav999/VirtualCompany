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
            auditEvent.OccurredUtc,
            BuildDataSourcesUsed(auditEvent),
            auditEvent.PayloadDiffJson,
            auditEvent.AgentName,
            auditEvent.AgentRole,
            auditEvent.ResponsibilityDomain,
            auditEvent.PromptProfileVersion,
            auditEvent.BoundaryDecisionOutcome,
            auditEvent.IdentityReasonCode,
            auditEvent.BoundaryReasonCode));
        return Task.CompletedTask;
    }

    private static IReadOnlyCollection<AuditDataSourceUsed> BuildDataSourcesUsed(AuditEventWriteRequest auditEvent)
    {
        if (auditEvent.DataSourcesUsed is { Count: > 0 })
        {
            return auditEvent.DataSourcesUsed;
        }

        return auditEvent.DataSources?
            .Select(source => new AuditDataSourceUsed(source, DisplayName: source))
            .ToArray() ?? [];
    }
}
