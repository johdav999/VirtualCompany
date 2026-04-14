using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Escalations;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class EfEscalationRepository : IEscalationRepository
{
    private readonly VirtualCompanyDbContext _dbContext;

    public EfEscalationRepository(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasExecutedAsync(
        Guid companyId,
        Guid policyId,
        string sourceEntityType,
        Guid sourceEntityId,
        int escalationLevel,
        int lifecycleVersion,
        CancellationToken cancellationToken) =>
        await _dbContext.Escalations
            .IgnoreQueryFilters()
            .AnyAsync(
                escalation =>
                    escalation.CompanyId == companyId &&
                    escalation.PolicyId == policyId &&
                    escalation.SourceEntityType == sourceEntityType &&
                    escalation.SourceEntityId == sourceEntityId &&
                    escalation.EscalationLevel == escalationLevel &&
                    escalation.LifecycleVersion == lifecycleVersion,
                cancellationToken);

    public async Task<EscalationCreationResult> TryCreateAsync(Escalation escalation, CancellationToken cancellationToken)
    {
        _dbContext.Escalations.Add(escalation);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new EscalationCreationResult(true, escalation, false);
        }
        catch (DbUpdateException ex) when (IsIdempotencyViolation(ex))
        {
            _dbContext.ChangeTracker.Clear();
            var existing = await _dbContext.Escalations
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(
                    item =>
                        item.CompanyId == escalation.CompanyId &&
                        item.PolicyId == escalation.PolicyId &&
                        item.SourceEntityType == escalation.SourceEntityType &&
                        item.SourceEntityId == escalation.SourceEntityId &&
                        item.EscalationLevel == escalation.EscalationLevel &&
                        item.LifecycleVersion == escalation.LifecycleVersion,
                    cancellationToken);

            if (existing is null)
            {
                throw;
            }

            return new EscalationCreationResult(false, existing, true);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);

    private static bool IsIdempotencyViolation(DbUpdateException exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("escalations", StringComparison.OrdinalIgnoreCase) &&
                (current.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                 current.Message.Contains("IX_escalations_company_id_policy_id_source_entity_type_source_entity_id_escalation_level_lifecycle_version", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }
}

public sealed class EfEscalationQueryService : IEscalationQueryService
{
    private const int DefaultTake = 50;
    private const int MaxTake = 200;

    private static readonly string[] PolicyEvaluationActions =
    [
        AuditEventActions.EscalationPolicyEvaluationStarted,
        AuditEventActions.EscalationPolicyEvaluationCompleted,
        AuditEventActions.EscalationPolicyEvaluationResult,
        AuditEventActions.EscalationCreated,
        AuditEventActions.EscalationDuplicateSkipped
    ];

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor _companyContextAccessor;

    public EfEscalationQueryService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor companyContextAccessor)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task<EscalationRecordListResult> ListEscalationsAsync(
        Guid companyId,
        EscalationRecordFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureEscalationReviewAccess(companyId);
        ValidateDateRange(filter.FromUtc, filter.ToUtc, nameof(filter));

        var skip = Math.Max(filter.Skip ?? 0, 0);
        var take = Math.Clamp(filter.Take ?? DefaultTake, 1, MaxTake);

        var query = _dbContext.Escalations
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId);

        if (filter.SourceEntityId is Guid sourceEntityId)
        {
            query = query.Where(x => x.SourceEntityId == sourceEntityId);
        }

        if (!string.IsNullOrWhiteSpace(filter.SourceEntityType))
        {
            var sourceEntityType = filter.SourceEntityType.Trim();
            query = query.Where(x => x.SourceEntityType == sourceEntityType);
        }

        if (filter.PolicyId is Guid policyId)
        {
            query = query.Where(x => x.PolicyId == policyId);
        }

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            var correlationId = filter.CorrelationId.Trim();
            query = query.Where(x => x.CorrelationId == correlationId);
        }

        if (filter.FromUtc is DateTime fromUtc)
        {
            query = query.Where(x => x.TriggeredUtc >= fromUtc);
        }

        if (filter.ToUtc is DateTime toUtc)
        {
            query = query.Where(x => x.TriggeredUtc <= toUtc);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.TriggeredUtc)
            .ThenByDescending(x => x.CreatedUtc)
            .ThenBy(x => x.EscalationLevel)
            .Skip(skip)
            .Take(take)
            .Select(x => MapEscalationRecord(x))
            .ToListAsync(cancellationToken);

        return new EscalationRecordListResult(items, totalCount, skip, take);
    }

    public async Task<EscalationRecordDto> GetEscalationAsync(
        Guid companyId,
        Guid escalationId,
        CancellationToken cancellationToken)
    {
        EnsureEscalationReviewAccess(companyId);

        var escalation = await _dbContext.Escalations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == escalationId, cancellationToken)
            ?? throw new KeyNotFoundException("Escalation record was not found.");

        return MapEscalationRecord(escalation);
    }

    public async Task<PolicyEvaluationHistoryResult> ListPolicyEvaluationHistoryAsync(
        Guid companyId,
        PolicyEvaluationHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        EnsureEscalationReviewAccess(companyId);
        ValidateDateRange(filter.FromUtc, filter.ToUtc, nameof(filter));

        var skip = Math.Max(filter.Skip ?? 0, 0);
        var take = Math.Clamp(filter.Take ?? DefaultTake, 1, MaxTake);

        var query = _dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && PolicyEvaluationActions.Contains(x.Action));

        if (!string.IsNullOrWhiteSpace(filter.CorrelationId))
        {
            var correlationId = filter.CorrelationId.Trim();
            query = query.Where(x => x.CorrelationId == correlationId);
        }

        if (filter.FromUtc is DateTime fromUtc)
        {
            query = query.Where(x => x.OccurredUtc >= fromUtc);
        }

        if (filter.ToUtc is DateTime toUtc)
        {
            query = query.Where(x => x.OccurredUtc <= toUtc);
        }

        var candidateEvents = await query
            .OrderByDescending(x => x.OccurredUtc)
            .ThenBy(x => x.Action)
            .ToListAsync(cancellationToken);

        var filteredEvents = candidateEvents
            .Where(x => MatchesPolicyEvaluationFilter(x, filter))
            .ToList();

        var page = filteredEvents
            .Skip(skip)
            .Take(take)
            .ToList();

        return new PolicyEvaluationHistoryResult(
            page.Select(MapPolicyEvaluationHistoryItem).ToList(),
            filteredEvents.Count,
            skip,
            take);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("Company context is required.");
        }

        if (!_companyContextAccessor.IsResolved ||
            _companyContextAccessor.CompanyId is not Guid currentCompanyId ||
            currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Escalation history is scoped to the active company context.");
        }
    }

    private void EnsureEscalationReviewAccess(Guid companyId)
    {
        EnsureTenant(companyId);

        var membership = _companyContextAccessor.Membership;
        if (membership is null ||
            membership.CompanyId != companyId ||
            membership.Status != CompanyMembershipStatus.Active ||
            !CanReviewEscalations(membership.MembershipRole))
        {
            throw new UnauthorizedAccessException("Escalation history requires escalation review access.");
        }
    }

    private static bool CanReviewEscalations(CompanyMembershipRole role) =>
        role is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager;

    private static void ValidateDateRange(DateTime? fromUtc, DateTime? toUtc, string paramName)
    {
        if (fromUtc is DateTime requestedFromUtc &&
            toUtc is DateTime requestedToUtc &&
            requestedFromUtc > requestedToUtc)
        {
            throw new ArgumentException("Escalation history from date must be on or before the to date.", paramName);
        }
    }

    private static bool MatchesPolicyEvaluationFilter(AuditEvent auditEvent, PolicyEvaluationHistoryFilter filter) =>
        MatchesMetadataGuid(auditEvent, "policyId", filter.PolicyId) &&
        MatchesMetadataGuid(auditEvent, "sourceEntityId", filter.SourceEntityId) &&
        MatchesMetadataText(auditEvent, "sourceEntityType", filter.SourceEntityType);

    private static bool MatchesMetadataGuid(AuditEvent auditEvent, string key, Guid? expected)
    {
        if (!expected.HasValue)
        {
            return true;
        }

        return auditEvent.Metadata.TryGetValue(key, out var value) &&
            Guid.TryParse(value, out var parsed) &&
            parsed == expected.Value;
    }

    private static bool MatchesMetadataText(AuditEvent auditEvent, string key, string? expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
        {
            return true;
        }

        return auditEvent.Metadata.TryGetValue(key, out var value) &&
            string.Equals(value, expected.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static EscalationRecordDto MapEscalationRecord(Escalation escalation) =>
        new(
            escalation.Id,
            escalation.CompanyId,
            escalation.PolicyId,
            escalation.SourceEntityId,
            escalation.SourceEntityType,
            escalation.EscalationLevel,
            escalation.Reason,
            escalation.TriggeredUtc,
            escalation.CorrelationId,
            escalation.Status.ToStorageValue(),
            escalation.CreatedUtc,
            escalation.LifecycleVersion);

    private static PolicyEvaluationHistoryItemDto MapPolicyEvaluationHistoryItem(AuditEvent auditEvent)
    {
        var policyId = TryGetGuid(auditEvent.Metadata, "policyId");
        var sourceEntityId = TryGetGuid(auditEvent.Metadata, "sourceEntityId");
        var sourceEntityType = TryGetValue(auditEvent.Metadata, "sourceEntityType");
        var escalationLevel = TryGetInt(auditEvent.Metadata, "escalationLevel");
        var conditionsMet = TryGetBool(auditEvent.Metadata, "conditionsMet");
        var escalationRecordId = TryGetGuid(auditEvent.Metadata, "escalationId");
        var diagnostic = TryGetValue(auditEvent.Metadata, "diagnostic");
        var evaluationResult = auditEvent.Action switch
        {
            AuditEventActions.EscalationPolicyEvaluationStarted => "started",
            AuditEventActions.EscalationPolicyEvaluationCompleted => "completed",
            AuditEventActions.EscalationCreated => "created",
            AuditEventActions.EscalationDuplicateSkipped => "duplicate_skipped",
            AuditEventActions.EscalationPolicyEvaluationResult when conditionsMet == true => "matched",
            AuditEventActions.EscalationPolicyEvaluationResult when conditionsMet == false => "not_matched",
            _ => auditEvent.Outcome
        };

        return new PolicyEvaluationHistoryItemDto(
            auditEvent.Id,
            auditEvent.CompanyId,
            policyId,
            sourceEntityId,
            sourceEntityType,
            escalationLevel,
            auditEvent.Action,
            auditEvent.Outcome,
            conditionsMet,
            evaluationResult,
            auditEvent.RationaleSummary ?? diagnostic,
            auditEvent.OccurredUtc,
            auditEvent.CorrelationId,
            escalationRecordId,
            auditEvent.TargetType,
            auditEvent.TargetId,
            new Dictionary<string, string?>(auditEvent.Metadata, StringComparer.OrdinalIgnoreCase));
    }

    private static Guid? TryGetGuid(IReadOnlyDictionary<string, string?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && Guid.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static int? TryGetInt(IReadOnlyDictionary<string, string?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && int.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static bool? TryGetBool(IReadOnlyDictionary<string, string?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : null;

    private static string? TryGetValue(IReadOnlyDictionary<string, string?> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
}
