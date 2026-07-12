using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportCaseService : ISupportCaseService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;
    private readonly ISupportSlaPolicyService? _slaPolicies;
    private readonly ICompanyOutboxEnqueuer? _outbox;

    public SupportCaseService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit, ISupportSlaPolicyService? slaPolicies = null, ICompanyOutboxEnqueuer? outbox = null)
    {
        _dbContext = dbContext;
        _audit = audit;
        _slaPolicies = slaPolicies;
        _outbox = outbox;
    }

    public async Task<SupportCaseListResponse> ListCasesAsync(Guid companyId, SupportCaseListQuery query, CancellationToken cancellationToken)
    {
        var cases = ApplyFilters(_dbContext.SupportCases.AsNoTracking().Where(x => x.CompanyId == companyId), query);
        var total = await cases.CountAsync(cancellationToken);
        var ordered = ApplySorting(cases, query);
        var items = await ordered
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Clamp(query.Take, 1, 200))
            .Select(x => new
            {
                Case = x,
                Contact = x.ContactId == null ? null : _dbContext.Contacts.IgnoreQueryFilters().AsNoTracking().FirstOrDefault(c => c.CompanyId == companyId && c.Id == x.ContactId),
                Customer = x.CustomerCompanyId == null ? null : _dbContext.CustomerCompanies.IgnoreQueryFilters().AsNoTracking().FirstOrDefault(c => c.CompanyId == companyId && c.Id == x.CustomerCompanyId)
            })
            .ToListAsync(cancellationToken);

        var summary = await BuildSummaryAsync(companyId, cancellationToken);
        return new SupportCaseListResponse(items.Select(x => MapListItem(x.Case, x.Contact, x.Customer)).ToList(), total, summary);
    }

    public async Task<SupportCaseDetailResponse?> GetCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        return supportCase is null ? null : await MapDetailAsync(supportCase, cancellationToken);
    }

    public async Task<SupportCaseDetailResponse> CreateCaseAsync(Guid companyId, Guid userId, CreateSupportCaseRequest request, CancellationToken cancellationToken)
    {
        SupportValidationException.ThrowIfBlank(request.Subject, nameof(request.Subject));
        var now = DateTime.UtcNow;
        var supportCase = new SupportCase(
            Guid.NewGuid(),
            companyId,
            await NextCaseNumberAsync(companyId, cancellationToken),
            request.Subject,
            request.Description,
            request.Source ?? "Manual",
            request.ContactId,
            request.CustomerCompanyId,
            createdUtc: now);

        if (!string.IsNullOrWhiteSpace(request.SenderEmail))
        {
            supportCase.MarkCustomerMessage(now);
            supportCase.Messages.Add(new SupportMessage(Guid.NewGuid(), companyId, supportCase.Id, SupportMessageDirections.Inbound, "manual", request.SenderEmail!, null, request.Description ?? request.Subject, now));
        }

        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Created, "Support case created.", userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human, userId == Guid.Empty ? null : userId, now));
        _dbContext.SupportCases.Add(supportCase);
        await ApplySlaAsync(companyId, supportCase, now, cancellationToken);
        await AddAuditAsync(companyId, userId, "support.case.created", supportCase.Id, AuditEventOutcomes.Succeeded, "Support case created.", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetCaseAsync(companyId, supportCase.Id, cancellationToken))!;
    }

    public async Task<SupportCaseDetailResponse?> AddInternalNoteAsync(Guid companyId, Guid userId, Guid supportCaseId, AddSupportInternalNoteRequest request, CancellationToken cancellationToken)
    {
        SupportValidationException.ThrowIfBlank(request.Body, nameof(request.Body));
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        var now = DateTime.UtcNow;
        supportCase.Messages.Add(new SupportMessage(Guid.NewGuid(), companyId, supportCase.Id, SupportMessageDirections.Internal, "internal_note", userId.ToString("D"), null, request.Body, now));
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.StatusChanged, "Internal note added.", AuditActorTypes.Human, userId, now));
        supportCase.MarkInternalActivity(now);
        await AddAuditAsync(companyId, userId, "support.case.note_added", supportCase.Id, AuditEventOutcomes.Succeeded, "Internal note added to support case.", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    public Task<SupportCaseDetailResponse?> ChangeStatusAsync(Guid companyId, Guid userId, Guid supportCaseId, ChangeSupportStatusRequest request, CancellationToken cancellationToken)
    {
        var normalized = SupportCaseStatuses.Normalize(request.Status);
        if (normalized is SupportCaseStatuses.Resolved or SupportCaseStatuses.Closed or SupportCaseStatuses.Reopened)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.Status)] = ["Use the resolve, close, or reopen action so the required reason is recorded."] });
        }

        return MutateCaseAsync(companyId, userId, supportCaseId, "support.case.status_changed", SupportCaseEventTypes.StatusChanged, request.Note ?? $"Status changed to {SupportLabels.Status(normalized)}.", c => c.SetStatus(normalized), cancellationToken);
    }

    public async Task<SupportCaseDetailResponse?> ChangePriorityAsync(Guid companyId, Guid userId, Guid supportCaseId, ChangeSupportPriorityRequest request, CancellationToken cancellationToken)
    {
        var result = await MutateCaseAsync(companyId, userId, supportCaseId, "support.case.priority_changed", SupportCaseEventTypes.PriorityChanged, $"Priority changed to {SupportLabels.Priority(request.Priority)}.", c => c.SetPriority(request.Priority), cancellationToken);
        return result is null ? null : await RecalculateSlaAsync(companyId, supportCaseId, cancellationToken);
    }

    public async Task<SupportCaseDetailResponse?> ChangeCategoryAsync(Guid companyId, Guid userId, Guid supportCaseId, ChangeSupportCategoryRequest request, CancellationToken cancellationToken)
    {
        var result = await MutateCaseAsync(companyId, userId, supportCaseId, "support.case.category_changed", SupportCaseEventTypes.Triaged, $"Category changed to {SupportLabels.Category(request.Category)}.", c => c.SetCategory(request.Category), cancellationToken);
        return result is null ? null : await RecalculateSlaAsync(companyId, supportCaseId, cancellationToken);
    }

    public async Task<SupportCaseDetailResponse?> AssignAsync(Guid companyId, Guid userId, Guid supportCaseId, AssignSupportCaseRequest request, CancellationToken cancellationToken)
    {
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        if (supportCase.Status is SupportCaseStatuses.Resolved or SupportCaseStatuses.Closed)
        {
            throw new InvalidOperationException("This case is already resolved or closed.");
        }
        if (request.AssignedAgentId.HasValue && request.AssignedUserId.HasValue)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { ["assigned"] = ["Assign either an agent or a person, not both."] });
        }

        if (request.AssignedAgentId is Guid agentId)
        {
            var validAgent = await _dbContext.Agents.AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId &&
                x.Id == agentId &&
                x.Department == "Support" &&
                x.Status != AgentStatus.Paused &&
                x.Status != AgentStatus.Archived,
                cancellationToken);
            if (!validAgent)
            {
                throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.AssignedAgentId)] = ["Select an available support agent."] });
            }
        }

        if (request.AssignedUserId is Guid assignedUserId)
        {
            var validUser = await _dbContext.CompanyMemberships.AsNoTracking().AnyAsync(x =>
                x.CompanyId == companyId && x.UserId == assignedUserId && x.Status == CompanyMembershipStatus.Active,
                cancellationToken);
            if (!validUser)
            {
                throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.AssignedUserId)] = ["Select an active company member."] });
            }
        }

        supportCase.Assign(request.AssignedAgentId, request.AssignedUserId);
        supportCase.Assignments.Add(new SupportCaseAssignment(Guid.NewGuid(), companyId, supportCase.Id, request.AssignedAgentId, request.AssignedUserId, userId, DateTime.UtcNow, request.Reason));
        var summary = request.AssignedAgentId is null && request.AssignedUserId is null ? "Support case unassigned." : "Support case assigned.";
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Assigned, summary, AuditActorTypes.Human, userId, DateTime.UtcNow));
        await AddAuditAsync(companyId, userId, "support.case.assigned", supportCase.Id, AuditEventOutcomes.Succeeded, summary, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<SupportAssigneeOptionDto>> ListAssigneesAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var agentRows = await _dbContext.Agents.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Department == "Support")
            .Select(x => new
            {
                x.Id,
                x.DisplayName,
                x.RoleName,
                x.Status,
                OpenCaseCount = _dbContext.SupportCases.Count(c => c.CompanyId == companyId && c.AssignedAgentId == x.Id && c.Status != SupportCaseStatuses.Resolved && c.Status != SupportCaseStatuses.Closed)
            })
            .ToListAsync(cancellationToken);
        var people = await _dbContext.CompanyMemberships.AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.CompanyId == companyId && x.Status == CompanyMembershipStatus.Active && x.UserId != null && x.User != null)
            .Select(x => new
            {
                Id = x.UserId!.Value,
                x.User!.DisplayName,
                Role = x.Role,
                OpenCaseCount = _dbContext.SupportCases.Count(c => c.CompanyId == companyId && c.AssignedUserId == x.UserId && c.Status != SupportCaseStatuses.Resolved && c.Status != SupportCaseStatuses.Closed)
            })
            .ToListAsync(cancellationToken);

        return agentRows.Select(x => new SupportAssigneeOptionDto(
                x.Id,
                "agent",
                x.DisplayName,
                x.RoleName,
                x.Status is not AgentStatus.Paused and not AgentStatus.Archived,
                x.OpenCaseCount))
            .Concat(people.Select(x => new SupportAssigneeOptionDto(x.Id, "user", x.DisplayName, CompanyMembershipRoles.ToDisplayName(x.Role), true, x.OpenCaseCount)))
            .OrderByDescending(x => x.Type == "agent")
            .ThenBy(x => x.DisplayName)
            .ToList();
    }

    public async Task<SupportCaseDetailResponse?> ResolveAsync(Guid companyId, Guid userId, Guid supportCaseId, ResolveSupportCaseRequest request, CancellationToken cancellationToken)
    {
        SupportValidationException.ThrowIfBlank(request.Summary, nameof(request.Summary));
        SupportValidationException.ThrowIfBlank(request.Outcome, nameof(request.Outcome));
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        if (supportCase.Status is SupportCaseStatuses.Resolved or SupportCaseStatuses.Closed)
        {
            throw new InvalidOperationException("This case is already resolved or closed.");
        }
        var links = request.RelevantEntityIds?.Where(x => x != Guid.Empty).Distinct().ToArray() ?? [];
        foreach (var linkedId in links)
        {
            var owned = await _dbContext.FinanceInvoices.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == linkedId, cancellationToken) ||
                        await _dbContext.Payments.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == linkedId, cancellationToken) ||
                        await _dbContext.CompanyKnowledgeDocuments.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == linkedId, cancellationToken);
            if (!owned) throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.RelevantEntityIds)] = ["Every linked record must exist in this company."] });
        }
        var resolution = new SupportCaseResolution(Guid.NewGuid(), companyId, supportCase.Id, request.Summary, request.Outcome, userId, DateTime.UtcNow, request.RootCauseCategory, request.ActionTaken, request.ReusableAnswer, request.CustomerPreferenceObservations, links.Length == 0 ? null : System.Text.Json.JsonSerializer.Serialize(links), request.ReuseEligible);
        _dbContext.SupportCaseResolutions.Add(resolution);
        supportCase.SetStatus(SupportCaseStatuses.Resolved);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Resolved, "Support case resolved.", AuditActorTypes.Human, userId, DateTime.UtcNow));
        await AddAuditAsync(companyId, userId, "support.case.resolved", supportCase.Id, AuditEventOutcomes.Succeeded, request.Summary, cancellationToken);
        var eventKey = $"support-case-resolved:v1:{supportCase.Id:N}";
        var job = await _dbContext.SupportMemoryUpdateJobs.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.EventKey == eventKey, cancellationToken);
        if (job is null)
        {
            job = new SupportMemoryUpdateJob(Guid.NewGuid(), companyId, supportCase.Id, eventKey);
            _dbContext.SupportMemoryUpdateJobs.Add(job);
            _outbox?.Enqueue(companyId, CompanyOutboxTopics.SupportMemoryUpdateRequested, new SupportMemoryUpdateRequestedMessage(companyId, supportCase.Id, job.Id, eventKey, null), idempotencyKey: eventKey, causationId: supportCase.Id.ToString("N"));
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    public async Task<SupportCaseDetailResponse?> ReopenAsync(Guid companyId, Guid userId, Guid supportCaseId, SupportActionRequest request, CancellationToken cancellationToken)
    {
        SupportValidationException.ThrowIfBlank(request.Note, nameof(request.Note));
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        if (supportCase.Status is not (SupportCaseStatuses.Resolved or SupportCaseStatuses.Closed))
        {
            throw new InvalidOperationException("Only resolved or closed cases can be reopened.");
        }

        supportCase.SetStatus(SupportCaseStatuses.Reopened);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Reopened, request.Note!, AuditActorTypes.Human, userId, DateTime.UtcNow));
        await AddAuditAsync(companyId, userId, "support.case.reopened", supportCase.Id, AuditEventOutcomes.Succeeded, request.Note!, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    public async Task<SupportCaseDetailResponse?> CloseAsync(Guid companyId, Guid userId, Guid supportCaseId, SupportActionRequest request, CancellationToken cancellationToken)
    {
        SupportValidationException.ThrowIfBlank(request.Note, nameof(request.Note));
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        if (supportCase.Status != SupportCaseStatuses.Resolved)
        {
            throw new InvalidOperationException("Resolve this case before closing it.");
        }

        supportCase.SetStatus(SupportCaseStatuses.Closed);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Closed, request.Note!, AuditActorTypes.Human, userId, DateTime.UtcNow));
        await AddAuditAsync(companyId, userId, "support.case.closed", supportCase.Id, AuditEventOutcomes.Succeeded, request.Note!, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    private async Task<SupportCaseDetailResponse?> MutateCaseAsync(Guid companyId, Guid userId, Guid supportCaseId, string auditAction, string eventType, string summary, Action<SupportCase> mutation, CancellationToken cancellationToken)
    {
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        mutation(supportCase);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, eventType, summary, AuditActorTypes.Human, userId, DateTime.UtcNow));
        await AddAuditAsync(companyId, userId, auditAction, supportCase.Id, AuditEventOutcomes.Succeeded, summary, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    internal static IQueryable<SupportCase> ApplyFilters(IQueryable<SupportCase> query, SupportCaseListQuery filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Status)) query = query.Where(x => x.Status == filter.Status);
        if (!string.IsNullOrWhiteSpace(filter.Priority)) query = query.Where(x => x.Priority == filter.Priority);
        if (!string.IsNullOrWhiteSpace(filter.Category)) query = query.Where(x => x.Category == filter.Category);
        if (filter.AssignedAgentId is Guid agentId) query = query.Where(x => x.AssignedAgentId == agentId);
        if (filter.AssignedUserId is Guid userId) query = query.Where(x => x.AssignedUserId == userId);
        if (filter.ContactId is Guid contactId) query = query.Where(x => x.ContactId == contactId);
        if (filter.CustomerCompanyId is Guid customerCompanyId) query = query.Where(x => x.CustomerCompanyId == customerCompanyId);
        if (filter.SlaRisk is bool slaRisk) query = query.Where(x => x.IsSlaRisk == slaRisk || x.IsSlaBreached == slaRisk);
        if (filter.CreatedFromUtc is DateTime from) query = query.Where(x => x.CreatedUtc >= from);
        if (filter.CreatedToUtc is DateTime to) query = query.Where(x => x.CreatedUtc <= to);
        if (filter.OpenOnly) query = query.Where(x => x.Status != SupportCaseStatuses.Resolved && x.Status != SupportCaseStatuses.Closed);
        if (filter.ResolvedToday)
        {
            var today = DateTime.UtcNow.Date;
            query = query.Where(x => x.ResolvedUtc >= today);
        }
        if (filter.Unassigned is true) query = query.Where(x => x.AssignedAgentId == null && x.AssignedUserId == null);
        if (filter.SlaBreached is bool breached) query = query.Where(x => x.IsSlaBreached == breached);
        if (filter.WaitingTooLong) query = query.Where(x => (x.Status == SupportCaseStatuses.WaitingForCustomer || x.Status == SupportCaseStatuses.WaitingInternal) && x.UpdatedUtc < DateTime.UtcNow.AddHours(-24));
        if (filter.FailedReply) query = query.Where(x => x.ReplyDrafts.Any(d => d.SendFailureSummary != null));
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x => x.Subject.Contains(search) || x.CaseNumber.Contains(search) || x.Summary.Contains(search));
        }
        return query;
    }

    private static IOrderedQueryable<SupportCase> ApplySorting(IQueryable<SupportCase> query, SupportCaseListQuery filter)
    {
        var descending = !string.Equals(filter.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);
        return filter.SortBy?.Trim().ToLowerInvariant() switch
        {
            "created" => descending ? query.OrderByDescending(x => x.CreatedUtc) : query.OrderBy(x => x.CreatedUtc),
            "updated" => descending ? query.OrderByDescending(x => x.UpdatedUtc) : query.OrderBy(x => x.UpdatedUtc),
            "due" => descending ? query.OrderByDescending(x => x.ResolutionDueUtc) : query.OrderBy(x => x.ResolutionDueUtc),
            "priority" => descending ? query.OrderByDescending(x => x.Priority) : query.OrderBy(x => x.Priority),
            _ => query.OrderByDescending(x => x.IsSlaBreached).ThenByDescending(x => x.IsSlaRisk).ThenByDescending(x => x.UpdatedUtc)
        };
    }

    private async Task<SupportCase?> LoadCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken) =>
        await _dbContext.SupportCases
            .Include(x => x.Messages)
            .Include(x => x.Events)
            .Include(x => x.ReplyDrafts)
            .Include(x => x.RefundRequests)
            .Include(x => x.KnowledgeGaps)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);

    private async Task ApplySlaAsync(Guid companyId, SupportCase supportCase, DateTime startUtc, CancellationToken cancellationToken)
    {
        if (_slaPolicies is null) return;
        var resolved = await _slaPolicies.ResolveAsync(companyId, supportCase.Category, supportCase.Priority, null, startUtc, cancellationToken);
        supportCase.SetSla(resolved.FirstResponseDueUtc, resolved.ResolutionDueUtc);
    }

    private async Task<SupportCaseDetailResponse?> RecalculateSlaAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        await ApplySlaAsync(companyId, supportCase, supportCase.CreatedUtc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCaseId, cancellationToken);
    }

    private async Task<string> NextCaseNumberAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var count = await _dbContext.SupportCases.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, cancellationToken);
        return $"SUP-{DateTime.UtcNow:yyyyMMdd}-{count + 1:0000}";
    }

    private async Task<SupportCaseSummaryCounts> BuildSummaryAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var cases = _dbContext.SupportCases.AsNoTracking().Where(x => x.CompanyId == companyId);
        return new SupportCaseSummaryCounts(
            await cases.CountAsync(x => x.Status != SupportCaseStatuses.Resolved && x.Status != SupportCaseStatuses.Closed, cancellationToken),
            await cases.CountAsync(x => x.Status == SupportCaseStatuses.AwaitingApproval, cancellationToken),
            await cases.CountAsync(x => x.Status == SupportCaseStatuses.Escalated, cancellationToken),
            await cases.CountAsync(x => x.IsSlaRisk, cancellationToken),
            await cases.CountAsync(x => x.IsSlaBreached, cancellationToken),
            await cases.CountAsync(x => x.ResolvedUtc >= today, cancellationToken));
    }

    private async Task<SupportCaseDetailResponse> MapDetailAsync(SupportCase supportCase, CancellationToken cancellationToken)
    {
        var contact = supportCase.ContactId is Guid contactId
            ? await _dbContext.Contacts.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == supportCase.CompanyId && x.Id == contactId, cancellationToken)
            : null;
        var customer = supportCase.CustomerCompanyId is Guid customerId
            ? await _dbContext.CustomerCompanies.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == supportCase.CompanyId && x.Id == customerId, cancellationToken)
            : null;
        var context = new SupportCaseContextSummary(
            supportCase.Id,
            customer?.Name,
            contact?.FullName,
            contact?.Email,
            BuildReferences(supportCase, contact, customer),
            contact is null && customer is null ? 0 : 0.9m,
            contact is null && customer is null ? "No matching customer context has been linked yet." : "Support case is linked to known customer records.");
        return new SupportCaseDetailResponse(
            supportCase.Id,
            supportCase.CaseNumber,
            supportCase.Subject,
            supportCase.Summary,
            supportCase.Description,
            supportCase.Status,
            SupportLabels.Status(supportCase.Status),
            supportCase.Priority,
            SupportLabels.Priority(supportCase.Priority),
            supportCase.Category,
            SupportLabels.Category(supportCase.Category),
            supportCase.Source,
            supportCase.Sentiment,
            supportCase.ConfidenceScore,
            supportCase.SuggestedNextAction,
            supportCase.RationaleSummary,
            supportCase.ContactId,
            supportCase.CustomerCompanyId,
            supportCase.RelatedInvoiceId,
            supportCase.RelatedPaymentId,
            customer?.Name,
            contact?.FullName,
            contact?.Email,
            supportCase.AssignedAgentId,
            supportCase.AssignedUserId,
            supportCase.FirstResponseDueUtc,
            supportCase.ResolutionDueUtc,
            supportCase.IsSlaRisk,
            supportCase.IsSlaBreached,
            supportCase.IsChurnRisk,
            supportCase.IsVipRisk,
            ResolveCaseAllowedActions(supportCase),
            supportCase.CreatedUtc,
            supportCase.UpdatedUtc,
            supportCase.Messages.OrderBy(x => x.OccurredUtc).Select(MapMessage).ToList(),
            supportCase.Events.OrderByDescending(x => x.OccurredUtc).Select(MapEvent).ToList(),
            supportCase.ReplyDrafts.OrderByDescending(x => x.CreatedUtc).Select(MapDraft).ToList(),
            supportCase.RefundRequests.OrderByDescending(x => x.CreatedUtc).Select(MapRefund).ToList(),
            supportCase.KnowledgeGaps.OrderByDescending(x => x.CreatedUtc).Select(MapGap).ToList(),
            context);
    }

    private static IReadOnlyList<string> ResolveCaseAllowedActions(SupportCase supportCase)
    {
        var actions = new List<string> { "assign", "change_priority", "change_category" };
        if (supportCase.Status is SupportCaseStatuses.Resolved or SupportCaseStatuses.Closed)
        {
            actions.Add("reopen");
            if (supportCase.Status == SupportCaseStatuses.Resolved)
            {
                actions.Add("close");
            }
            return actions;
        }

        actions.AddRange(["resolve", "wait_for_customer", "wait_internally"]);
        if (supportCase.Status != SupportCaseStatuses.Escalated)
        {
            actions.Add("escalate");
        }
        return actions;
    }

    internal static SupportCaseListItem MapListItem(SupportCase supportCase, Contact? contact, CustomerCompany? customer) =>
        new(
            supportCase.Id,
            supportCase.CaseNumber,
            supportCase.Subject,
            supportCase.Status,
            SupportLabels.Status(supportCase.Status),
            supportCase.Priority,
            SupportLabels.Priority(supportCase.Priority),
            supportCase.Category,
            SupportLabels.Category(supportCase.Category),
            supportCase.Source,
            customer?.Name,
            contact?.FullName,
            contact?.Email,
            supportCase.AssignedAgentId,
            supportCase.AssignedUserId,
            supportCase.CreatedUtc,
            supportCase.UpdatedUtc,
            supportCase.FirstResponseDueUtc,
            supportCase.ResolutionDueUtc,
            supportCase.IsSlaRisk,
            supportCase.IsSlaBreached,
            supportCase.IsChurnRisk,
            supportCase.IsVipRisk);

    internal static SupportMessageDto MapMessage(SupportMessage message) =>
        new(message.Id, message.Direction, message.Channel, message.Sender, message.Recipient, message.Body, message.OccurredUtc, message.EmailMessageSnapshotId, message.ProviderMessageId, message.ProviderThreadId);

    internal static SupportCaseEventDto MapEvent(SupportCaseEvent evt) =>
        new(evt.Id, evt.EventType, SupportLabels.Event(evt.EventType), evt.Summary, evt.ActorType, evt.ActorId, evt.OccurredUtc);

    internal static SupportReplyDraftDto MapDraft(SupportReplyDraft draft) =>
        new(draft.Id, draft.SupportCaseId, draft.DraftBody, draft.Tone, draft.Status, SupportLabels.DraftStatus(draft.Status), draft.Confidence, draft.Answerability, draft.RationaleSummary, draft.SourceReferencesJson, draft.CreatedByAgentId, draft.CreatedByUserId, draft.ApprovedByUserId, draft.ApprovedUtc, draft.SentUtc, draft.SendFailureSummary, draft.CreatedUtc, draft.UpdatedUtc, draft.SafetyDecision, draft.SafetyReasonCodesJson, draft.SafetyPolicyVersion, draft.SafetyEvaluatedUtc);

    internal static SupportRefundRequestDto MapRefund(SupportRefundRequest refund) =>
        new(
            refund.Id,
            refund.SupportCaseId,
            refund.Amount,
            refund.Currency,
            refund.ReasonCode,
            refund.Explanation,
            refund.InvoiceId,
            refund.PaymentId,
            refund.ApprovalRequestId,
            refund.FinanceActionReferenceId,
            refund.ProviderWriteRequestId,
            refund.ProviderApprovalRequestId,
            refund.Status,
            SupportLabels.Status(refund.Status),
            refund.LastFailureSummary,
            refund.ExecutionRequestedUtc,
            refund.CompletedUtc,
            ResolveRefundAllowedActions(refund),
            refund.CreatedUtc,
            refund.UpdatedUtc);

    private static IReadOnlyList<string> ResolveRefundAllowedActions(SupportRefundRequest refund) => refund.Status switch
    {
        SupportRefundRequestStatuses.Queued => ["request_execution", "cancel"],
        SupportRefundRequestStatuses.Failed => ["retry", "reconcile"],
        SupportRefundRequestStatuses.ReconciliationRequired => ["reconcile"],
        SupportRefundRequestStatuses.PendingApproval => ["cancel"],
        _ => []
    };

    internal static SupportKnowledgeGapDto MapGap(SupportKnowledgeGap gap) =>
        new(gap.Id, gap.SupportCaseId, gap.SupportReplyDraftId, gap.Category, SupportLabels.Category(gap.Category), gap.QuestionSummary, gap.MissingInformationSummary, gap.RetrievalSourceSummary, gap.FrequencyCount, gap.Status, SupportLabels.KnowledgeGapStatus(gap.Status), gap.CreatedUtc, gap.UpdatedUtc, gap.LinkedTaskId, gap.LinkedKnowledgeDocumentId);

    internal static IReadOnlyList<SupportContextReference> BuildReferences(SupportCase supportCase, Contact? contact, CustomerCompany? customer)
    {
        var references = new List<SupportContextReference>();
        if (contact is not null) references.Add(new SupportContextReference("contact", contact.FullName, contact.Id, contact.Email));
        if (customer is not null) references.Add(new SupportContextReference("customer", customer.Name, customer.Id, customer.Industry));
        if (supportCase.RelatedInvoiceId is Guid invoiceId) references.Add(new SupportContextReference("invoice", "Related invoice", invoiceId));
        if (supportCase.RelatedPaymentId is Guid paymentId) references.Add(new SupportContextReference("payment", "Related payment", paymentId));
        return references;
    }

    private async Task AddAuditAsync(Guid companyId, Guid userId, string action, Guid targetId, string outcome, string summary, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human, userId == Guid.Empty ? null : userId, action, "support_case", targetId.ToString("D"), outcome, summary, ["support"]), cancellationToken);
}

public sealed class SupportMailboxIngestionService : ISupportMailboxIngestionService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISupportCaseService _cases;
    private readonly ISupportTriageService _triage;

    public SupportMailboxIngestionService(VirtualCompanyDbContext dbContext, ISupportCaseService cases, ISupportTriageService triage)
    {
        _dbContext = dbContext;
        _cases = cases;
        _triage = triage;
    }

    public async Task<SupportMailboxIngestionResult> IngestMessageAsync(Guid companyId, SupportMailboxMessageInput input, CancellationToken cancellationToken)
    {
        var existingMessage = await _dbContext.SupportMessages.AsNoTracking().FirstOrDefaultAsync(x =>
            x.CompanyId == companyId &&
            input.ProviderMessageId != null &&
            x.ProviderMessageId == input.ProviderMessageId, cancellationToken);
        if (existingMessage is not null)
        {
            return new SupportMailboxIngestionResult(existingMessage.SupportCaseId, existingMessage.Id, false, true);
        }

        var supportCase = await FindMatchingCaseAsync(companyId, input, cancellationToken);
        var created = false;
        if (supportCase is null)
        {
            var createdDetail = await _cases.CreateCaseAsync(companyId, Guid.Empty, new CreateSupportCaseRequest(input.Subject, input.Body, "Email", input.SenderEmail), cancellationToken);
            supportCase = await _dbContext.SupportCases.FirstAsync(x => x.Id == createdDetail.Id, cancellationToken);
            created = true;
        }

        var message = new SupportMessage(Guid.NewGuid(), companyId, supportCase.Id, SupportMessageDirections.Inbound, "email", input.SenderEmail, input.RecipientEmail, input.Body, input.OccurredUtc, input.EmailMessageSnapshotId, input.ProviderMessageId, input.ProviderThreadId);
        supportCase.Messages.Add(message);
        supportCase.MarkCustomerMessage(input.OccurredUtc);
        supportCase.LinkProviderMessage(input.ProviderThreadId, input.ProviderMessageId);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.MessageReceived, "Customer message received.", AuditActorTypes.System, null, input.OccurredUtc));
        await _dbContext.SaveChangesAsync(cancellationToken);
        await _triage.TriageAsync(companyId, Guid.Empty, supportCase.Id, cancellationToken);
        return new SupportMailboxIngestionResult(supportCase.Id, message.Id, created, false);
    }

    private async Task<SupportCase?> FindMatchingCaseAsync(Guid companyId, SupportMailboxMessageInput input, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(input.Subject))
        {
            var match = System.Text.RegularExpressions.Regex.Match(input.Subject, "SUP-[0-9]{8}-[0-9]{4}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var byNumber = await _dbContext.SupportCases.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.CaseNumber == match.Value, cancellationToken);
                if (byNumber is not null) return byNumber;
            }
        }

        if (!string.IsNullOrWhiteSpace(input.ProviderThreadId))
        {
            var byThread = await _dbContext.SupportCases.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ProviderThreadId == input.ProviderThreadId && x.Status != SupportCaseStatuses.Closed, cancellationToken);
            if (byThread is not null) return byThread;
        }

        return await _dbContext.SupportCases
            .Where(x => x.CompanyId == companyId && x.Status != SupportCaseStatuses.Closed && x.LastCustomerMessageUtc >= DateTime.UtcNow.AddDays(-14))
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }
}

public sealed class SupportMailboxRoutingService : ISupportMailboxRoutingService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISupportMailboxIngestionService _ingestion;
    private readonly ISupportAgentOrchestrationService? _agentOrchestration;
    private readonly ILogger<SupportMailboxRoutingService>? _logger;

    public SupportMailboxRoutingService(VirtualCompanyDbContext dbContext, ISupportMailboxIngestionService ingestion)
    {
        _dbContext = dbContext;
        _ingestion = ingestion;
    }

    public SupportMailboxRoutingService(
        VirtualCompanyDbContext dbContext,
        ISupportMailboxIngestionService ingestion,
        ISupportAgentOrchestrationService agentOrchestration,
        ILogger<SupportMailboxRoutingService> logger)
    {
        _dbContext = dbContext;
        _ingestion = ingestion;
        _agentOrchestration = agentOrchestration;
        _logger = logger;
    }

    public async Task<SupportMailboxRoutingResult> RouteUnlinkedInboundMessagesAsync(DateTime sinceUtc, int batchSize, CancellationToken cancellationToken)
    {
        var snapshots = await _dbContext.EmailMessageSnapshots.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.FromAddress != null && (x.ReceivedUtc ?? x.CreatedUtc) >= sinceUtc)
            .OrderBy(x => x.ReceivedUtc ?? x.CreatedUtc)
            .Take(Math.Clamp(batchSize, 1, 200))
            .ToListAsync(cancellationToken);
        var routed = 0;
        var created = 0;
        var duplicates = 0;
        foreach (var snapshot in snapshots)
        {
            var existingSupportMessage = await _dbContext.SupportMessages.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.CompanyId == snapshot.CompanyId && x.EmailMessageSnapshotId == snapshot.Id, cancellationToken);
            if (existingSupportMessage is not null)
            {
                duplicates++;
                await TryRunAgentAsync(snapshot.CompanyId, existingSupportMessage.SupportCaseId, existingSupportMessage.Id, cancellationToken);
                continue;
            }

            var result = await _ingestion.IngestMessageAsync(snapshot.CompanyId, new SupportMailboxMessageInput(
                snapshot.MailboxConnectionId,
                snapshot.Id,
                snapshot.FromAddress!,
                snapshot.FromDisplayName,
                null,
                snapshot.Subject ?? "Support request",
                snapshot.UntrustedBodyText ?? snapshot.Subject ?? "Support request",
                snapshot.ExternalMessageId,
                null,
                snapshot.ReceivedUtc ?? snapshot.CreatedUtc), cancellationToken);
            routed++;
            if (result.CreatedCase)
            {
                created++;
            }

            await TryRunAgentAsync(snapshot.CompanyId, result.SupportCaseId, result.SupportMessageId, cancellationToken);
        }

        return new SupportMailboxRoutingResult(snapshots.Count, routed, created, duplicates);
    }

    private async Task TryRunAgentAsync(Guid companyId, Guid supportCaseId, Guid supportMessageId, CancellationToken cancellationToken)
    {
        if (_agentOrchestration is null) return;
        try
        {
            await _agentOrchestration.RunAsync(
                companyId,
                Guid.Empty,
                supportCaseId,
                new RunSupportAgentRequest($"support-inbound:{supportMessageId:N}"),
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Message ingestion remains durable; the next polling pass retries the same idempotent execution.
            _logger?.LogError(
                ex,
                "Support agent drafting failed after mailbox routing. CompanyId: {CompanyId}, SupportCaseId: {SupportCaseId}, SupportMessageId: {SupportMessageId}.",
                companyId,
                supportCaseId,
                supportMessageId);
        }
    }
}

public sealed class SupportOperationsBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<SupportOperationsWorkerOptions> _options;
    private readonly ILogger<SupportOperationsBackgroundService> _logger;

    public SupportOperationsBackgroundService(IServiceScopeFactory scopeFactory, IOptionsMonitor<SupportOperationsWorkerOptions> options, ILogger<SupportOperationsBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var options = _options.CurrentValue;
            if (options.Enabled)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var router = scope.ServiceProvider.GetRequiredService<ISupportMailboxRoutingService>();
                    var sla = scope.ServiceProvider.GetRequiredService<ISupportSlaMonitor>();
                    var routed = await router.RouteUnlinkedInboundMessagesAsync(DateTime.UtcNow.AddMinutes(-Math.Clamp(options.MailboxLookbackMinutes, 5, 1440)), options.MailboxBatchSize, stoppingToken);
                    var monitored = await sla.RunAsync(DateTime.UtcNow, stoppingToken);
                    _logger.LogInformation("Support operations worker routed {Routed} messages and scanned {Cases} SLA cases.", routed.MessagesRouted, monitored.CasesScanned);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Support operations worker failed while routing mailbox messages or monitoring SLA.");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 10, 600)), stoppingToken);
        }
    }
}
public sealed class SupportContextResolutionService : ISupportContextResolutionService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public SupportContextResolutionService(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

    public async Task<SupportCaseContextSummary> ResolveAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken)
            ?? throw new InvalidOperationException("Support case was not found.");
        var lastInboundEmail = await _dbContext.SupportMessages.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SupportCaseId == supportCaseId && x.Direction == SupportMessageDirections.Inbound)
            .OrderByDescending(x => x.OccurredUtc)
            .Select(x => x.Sender)
            .FirstOrDefaultAsync(cancellationToken);
        Contact? contact = null;
        CustomerCompany? customer = null;
        if (!string.IsNullOrWhiteSpace(lastInboundEmail))
        {
            contact = await _dbContext.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Email == lastInboundEmail.ToLower(), cancellationToken);
            if (contact?.CustomerCompanyId is Guid customerId)
            {
                customer = await _dbContext.CustomerCompanies.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == customerId, cancellationToken);
            }
        }

        if (contact is not null || customer is not null)
        {
            supportCase.LinkContext(contact?.Id, customer?.Id, supportCase.RelatedInvoiceId, supportCase.RelatedPaymentId);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new SupportCaseContextSummary(supportCase.Id, customer?.Name, contact?.FullName, contact?.Email, SupportCaseService.BuildReferences(supportCase, contact, customer), contact is null ? 0 : 0.9m, contact is null ? "No customer match found." : "Matched sender email to a known contact.");
    }
}

public sealed class SupportTriageService : ISupportTriageService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportTriageService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<SupportTriageResult?> TriageAsync(Guid companyId, Guid userId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.Include(x => x.Messages).Include(x => x.Events).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        var text = $"{supportCase.Subject} {supportCase.Description} {string.Join(' ', supportCase.Messages.Select(x => x.Body))}".ToLowerInvariant();
        var category = text.Contains("refund") || text.Contains("credit") ? SupportCaseCategories.Refund :
            text.Contains("invoice") || text.Contains("payment") || text.Contains("billing") ? SupportCaseCategories.Billing :
            text.Contains("bug") || text.Contains("crash") || text.Contains("error") ? SupportCaseCategories.BugReport :
            text.Contains("cancel") || text.Contains("churn") ? SupportCaseCategories.ChurnRisk :
            text.Contains("angry") || text.Contains("complaint") || text.Contains("unacceptable") ? SupportCaseCategories.Complaint :
            text.Contains("login") || text.Contains("password") ? SupportCaseCategories.AccountAccess :
            SupportCaseCategories.GeneralQuestion;
        var urgent = text.Contains("urgent") || text.Contains("asap") || text.Contains("immediately");
        var negative = text.Contains("angry") || text.Contains("unhappy") || text.Contains("complaint") || text.Contains("cancel");
        var priority = urgent || category == SupportCaseCategories.ChurnRisk || category == SupportCaseCategories.Complaint ? SupportPriorities.High : SupportPriorities.Normal;
        var confidence = category == SupportCaseCategories.GeneralQuestion ? 0.55m : 0.82m;
        var suggested = category switch
        {
            SupportCaseCategories.Billing => "Review invoice and payment context, then send a source-backed explanation.",
            SupportCaseCategories.Refund => "Validate refund policy and prepare an approval request if money movement is needed.",
            SupportCaseCategories.BugReport => "Collect reproduction details and create an internal task.",
            SupportCaseCategories.ChurnRisk => "Escalate to customer success or sales with retention context.",
            _ => "Draft a helpful response from approved knowledge."
        };
        supportCase.SetTriage(category, priority, negative ? "Negative" : "Neutral", confidence, suggested, "Support case triaged from message content and linked context.", false, category == SupportCaseCategories.ChurnRisk, urgent);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Triaged, "Support case triaged.", userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human, userId == Guid.Empty ? null : userId, DateTime.UtcNow));
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human, userId == Guid.Empty ? null : userId, "support.case.triaged", "support_case", supportCase.Id.ToString("D"), AuditEventOutcomes.Succeeded, supportCase.RationaleSummary, ["support"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new SupportTriageResult(supportCase.Id, category, priority, supportCase.Sentiment ?? "Neutral", confidence, suggested, supportCase.RationaleSummary ?? string.Empty, false, supportCase.IsChurnRisk, supportCase.IsSlaRisk);
    }
}

public sealed class SupportKnowledgeContextProvider : ISupportKnowledgeContextProvider
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyKnowledgeSearchService? _knowledgeSearch;

    public SupportKnowledgeContextProvider(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public SupportKnowledgeContextProvider(
        VirtualCompanyDbContext dbContext,
        ICompanyKnowledgeSearchService knowledgeSearch)
    {
        _dbContext = dbContext;
        _knowledgeSearch = knowledgeSearch;
    }

    public async Task<SupportKnowledgeContext> RetrieveAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.AsNoTracking()
            .Include(x => x.Messages)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (supportCase is null)
        {
            return new SupportKnowledgeContext(supportCaseId, [], [], [], 0m, "Support case was not found.");
        }

        var queryTerms = BuildQueryTerms(supportCase);
        var sources = new List<SupportKnowledgeSourceReference>
        {
            new("support_case", $"Support case {supportCase.CaseNumber}", supportCase.Id, TrimForExcerpt($"{supportCase.Subject}. {supportCase.Summary} {supportCase.Description}"), 1m)
        };

        var queryText = string.Join(' ', queryTerms);
        if (!string.IsNullOrWhiteSpace(queryText))
        {
            var searchResults = _knowledgeSearch is null
                ? await SearchIndexedKnowledgeForTestsAsync(companyId, queryTerms, cancellationToken)
                : await _knowledgeSearch.SearchAsync(
                    new CompanyKnowledgeSemanticSearchQuery(
                        companyId,
                        queryText,
                        6,
                        new CompanyKnowledgeAccessContext(companyId, DataScopes: ["support", "knowledge"])),
                    cancellationToken);
            sources.AddRange(searchResults
                .Where(x => x.Score >= 0.25d)
                .Take(4)
                .Select(x => new SupportKnowledgeSourceReference(
                    "knowledge_chunk",
                    x.DocumentTitle,
                    x.ChunkId,
                    TrimForExcerpt(x.Content),
                    Math.Min(0.98m, Math.Max(0.55m, Convert.ToDecimal(x.Score))),
                    true,
                    x.DocumentId,
                    x.SourceReference)));
        }

        var similarCases = await _dbContext.SupportCases.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Id != supportCase.Id && x.Category == supportCase.Category && (x.Status == SupportCaseStatuses.Resolved || x.Status == SupportCaseStatuses.Closed))
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(3)
            .Select(x => $"{x.CaseNumber}: {x.Subject} - {x.Summary}")
            .ToListAsync(cancellationToken);

        var memories = new List<string>();
        if (supportCase.ContactId is Guid contactId)
        {
            var profile = await _dbContext.CustomerMemoryProfiles.AsNoTracking()
                .Include(x => x.Preferences)
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ContactId == contactId, cancellationToken);
            if (profile is not null)
            {
                memories.AddRange(profile.Preferences
                    .Where(x => x.PreferenceKey.Contains("support", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.ObservedUtc)
                    .Take(3)
                    .Select(x => x.PreferenceValue));
            }
        }

        foreach (var similar in similarCases)
        {
            sources.Add(new SupportKnowledgeSourceReference("support_case_history", "Resolved similar support case", null, TrimForExcerpt(similar), 0.65m));
        }

        foreach (var memory in memories)
        {
            sources.Add(new SupportKnowledgeSourceReference("customer_memory", "Customer support memory", supportCase.ContactId, TrimForExcerpt(memory), 0.7m));
        }

        var trustedSources = sources.Where(x => x.IsTrusted).ToList();
        var confidence = trustedSources.Count == 0 ? 0.35m : Math.Min(0.92m, trustedSources.Average(x => x.Relevance));
        var rationale = trustedSources.Count == 0
            ? "No processed, indexed, and accessible company knowledge was found for this question."
            : "Retrieved support case context, customer memory, similar outcomes, and relevant knowledge snippets for grounded drafting.";
        return new SupportKnowledgeContext(supportCase.Id, sources, memories, similarCases, confidence, rationale);
    }

    private async Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchIndexedKnowledgeForTestsAsync(
        Guid companyId,
        IReadOnlyCollection<string> queryTerms,
        CancellationToken cancellationToken)
    {
        var chunks = await _dbContext.CompanyKnowledgeChunks.AsNoTracking()
            .Include(x => x.Document)
            .Where(x => x.CompanyId == companyId && x.IsActive &&
                x.Document.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed &&
                x.Document.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexed)
            .Take(200)
            .ToListAsync(cancellationToken);
        return chunks
            .Select(x => new { Chunk = x, Score = queryTerms.Count(term => (x.Content + " " + x.Document.Title).Contains(term, StringComparison.OrdinalIgnoreCase)) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(6)
            .Select(x => new CompanyKnowledgeSearchResultDto(
                x.Chunk.Id,
                x.Chunk.Content,
                Math.Min(0.95d, 0.45d + x.Score / 10d),
                x.Chunk.DocumentId,
                x.Chunk.Document.Title,
                x.Chunk.ChunkIndex,
                x.Chunk.SourceReference,
                new Dictionary<string, JsonNode?>(),
                new CompanyKnowledgeSourceReferenceDto(x.Chunk.DocumentId, x.Chunk.Document.Title, x.Chunk.Document.DocumentType.ToStorageValue(), x.Chunk.Document.SourceType.ToStorageValue(), null, x.Chunk.Id, x.Chunk.ChunkIndex, x.Chunk.SourceReference),
                new CompanyKnowledgeSourceDocumentDto(x.Chunk.DocumentId, x.Chunk.Document.Title, x.Chunk.Document.DocumentType.ToStorageValue(), x.Chunk.Document.SourceType.ToStorageValue(), null)))
            .ToList();
    }

    private static string[] BuildQueryTerms(SupportCase supportCase)
    {
        var text = $"{supportCase.Subject} {supportCase.Summary} {supportCase.Description} {supportCase.Category} {string.Join(' ', supportCase.Messages.Select(x => x.Body))}";
        return text.Split([' ', '\r', '\n', '\t', '.', ',', ':', ';', '/', '\\', '-', '_', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().ToLowerInvariant())
            .Where(x => x.Length > 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(24)
            .ToArray();
    }

    private static string TrimForExcerpt(string? value)
    {
        var normalized = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= 420 ? normalized : normalized[..420];
    }
}
public sealed class SupportReplyDraftService : ISupportReplyDraftService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;
    private readonly ISupportOutboundEmailSender _outboundEmailSender;
    private readonly ISupportKnowledgeContextProvider _knowledgeContextProvider;
    private readonly ISupportKnowledgeGapService _knowledgeGaps;
    private readonly ISupportReplySafetyPolicy _safetyPolicy;

    public SupportReplyDraftService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter audit,
        ISupportOutboundEmailSender outboundEmailSender,
        ISupportKnowledgeContextProvider knowledgeContextProvider,
        ISupportKnowledgeGapService knowledgeGaps,
        ISupportReplySafetyPolicy? safetyPolicy = null)
    {
        _dbContext = dbContext;
        _audit = audit;
        _outboundEmailSender = outboundEmailSender;
        _knowledgeContextProvider = knowledgeContextProvider;
        _knowledgeGaps = knowledgeGaps;
        _safetyPolicy = safetyPolicy ?? new DeterministicSupportReplySafetyPolicy(dbContext);
    }

    public async Task<SupportReplyDraftDto?> GenerateDraftAsync(Guid companyId, Guid userId, Guid supportCaseId, GenerateSupportReplyDraftRequest request, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.Include(x => x.Messages).Include(x => x.Events).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (supportCase is null) return null;

        var context = await _knowledgeContextProvider.RetrieveAsync(companyId, supportCase.Id, cancellationToken);
        var lastInbound = supportCase.Messages.Where(x => x.Direction == SupportMessageDirections.Inbound).OrderByDescending(x => x.OccurredUtc).FirstOrDefault();
        var answerability = ResolveAnswerability(supportCase, context, request.ForceReview);
        var confidence = Math.Min(0.92m, Math.Max(0.48m, answerability + 0.08m));
        var body = BuildGroundedDraftBody(supportCase, context, lastInbound, answerability);
        var sourceJson = BuildSourceReferencesJson(context);
        var rationale = answerability < 0.7m
            ? "Draft generated with low answerability because available support knowledge is incomplete. Human review is required."
            : context.RationaleSummary;
        var draft = new SupportReplyDraft(Guid.NewGuid(), companyId, supportCase.Id, body, request.Tone ?? "Helpful", confidence, answerability, rationale, sourceJson, null, userId == Guid.Empty ? null : userId);
        _dbContext.SupportReplyDrafts.Add(draft);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.ReplyDrafted, answerability < 0.7m ? "Reply draft needs review because source confidence is low." : "Reply draft created from retrieved knowledge.", userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human, userId == Guid.Empty ? null : userId, DateTime.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (answerability < 0.7m)
        {
            await _knowledgeGaps.CreateOrIncrementAsync(companyId, new CreateSupportKnowledgeGapRequest(
                supportCase.Id,
                draft.Id,
                supportCase.Category,
                supportCase.Subject,
                "Support reply drafting could not find enough approved knowledge or outcome history to answer confidently.",
                context.RationaleSummary), cancellationToken);
        }

        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human, userId == Guid.Empty ? null : userId, "support.reply.drafted", "support_case", supportCase.Id.ToString("D"), AuditEventOutcomes.Succeeded, draft.RationaleSummary, ["support", "knowledge"], DataSourcesUsed: context.Sources.Select(x => new AuditDataSourceUsed(x.Type, x.EntityId?.ToString("D") ?? supportCase.Id.ToString("D"), x.Label, x.Excerpt)).ToList()), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapDraft(draft);
    }

    private static decimal ResolveAnswerability(SupportCase supportCase, SupportKnowledgeContext context, bool forceReview)
    {
        if (forceReview) return 0.62m;
        if (!context.HasTrustedGrounding) return 0.45m;
        if (supportCase.Category is SupportCaseCategories.Refund or SupportCaseCategories.Billing) return Math.Min(0.82m, context.RetrievalConfidence);
        return Math.Min(0.88m, Math.Max(0.72m, context.RetrievalConfidence));
    }

    private static string BuildGroundedDraftBody(SupportCase supportCase, SupportKnowledgeContext context, SupportMessage? lastInbound, decimal answerability)
    {
        var greeting = "Hello";
        var sourceLines = context.Sources
            .Where(x => x.IsTrusted && !string.IsNullOrWhiteSpace(x.Excerpt))
            .Take(3)
            .Select((x, index) => $"[{index + 1}] {x.Excerpt}")
            .ToList();
        var grounding = sourceLines.Count == 0
            ? "I need to verify the right policy or account details before giving a final answer."
            : "Based on the company information available to me:\n" + string.Join("\n", sourceLines);
        var nextStep = supportCase.Category switch
        {
            SupportCaseCategories.Refund => "If a refund or credit is needed, I will route it for approval before any financial action is taken.",
            SupportCaseCategories.Billing => "I will verify the related invoice or payment record before confirming the final billing answer.",
            SupportCaseCategories.BugReport => "I will capture the reproduction details and create an internal follow-up if engineering needs to investigate.",
            SupportCaseCategories.AccountAccess => "I can help with the account-access next step while keeping any sensitive change under review.",
            _ => "I will help with the next step and ask for anything missing before making account changes."
        };
        var reviewLine = answerability < 0.7m ? "\n\nThis needs a support review because the available knowledge is incomplete." : string.Empty;
        return $"{greeting},\n\nThanks for your message. {grounding}\n\n{nextStep}{reviewLine}\n\nBest regards,\nSupport";
    }

    private static string BuildSourceReferencesJson(SupportKnowledgeContext context)
    {
        var array = new JsonArray();
        foreach (var source in context.Sources)
        {
            array.Add(new JsonObject
            {
                ["type"] = source.Type,
                ["label"] = source.Label,
                ["entityId"] = source.EntityId?.ToString("D"),
                ["excerpt"] = source.Excerpt,
                ["relevance"] = source.Relevance,
                ["trusted"] = source.IsTrusted,
                ["documentId"] = source.DocumentId?.ToString("D"),
                ["sourceReference"] = source.SourceReference
            });
        }

        return array.ToJsonString();
    }
    public async Task<IReadOnlyList<SupportReplyDraftDto>> ListDraftsAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken) =>
        await _dbContext.SupportReplyDrafts.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SupportCaseId == supportCaseId)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => SupportCaseService.MapDraft(x))
            .ToListAsync(cancellationToken);

    public async Task<SupportReplyDraftDto?> EditDraftAsync(Guid companyId, Guid userId, Guid draftId, EditSupportReplyDraftRequest request, CancellationToken cancellationToken)
    {
        var draft = await _dbContext.SupportReplyDrafts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draftId, cancellationToken);
        if (draft is null) return null;
        draft.Edit(request.DraftBody, request.Tone);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.reply.edited", "support_reply_draft", draft.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Support reply draft edited.", ["support"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapDraft(draft);
    }

    public async Task<SupportReplyDraftDto?> ApproveDraftAsync(Guid companyId, Guid userId, Guid draftId, SupportActionRequest request, CancellationToken cancellationToken)
    {
        var draft = await _dbContext.SupportReplyDrafts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draftId, cancellationToken);
        if (draft is null) return null;
        var supportCase = await _dbContext.SupportCases.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draft.SupportCaseId, cancellationToken);
        if (supportCase is null) return null;
        var safety = await EvaluateAndRecordSafetyAsync(companyId, draft, supportCase.Id, cancellationToken);
        if (safety.Decision != "allow")
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.reply.approval_blocked", "support_reply_draft", draft.Id.ToString("D"), AuditEventOutcomes.Blocked, string.Join(" ", safety.Explanations), ["support", "safety"], Metadata: new Dictionary<string, string?> { ["policyVersion"] = safety.PolicyVersion, ["decision"] = safety.Decision }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("This reply needs changes before it can be approved: " + string.Join(" ", safety.Explanations));
        }
        draft.Approve(userId);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.reply.approved", "support_reply_draft", draft.Id.ToString("D"), AuditEventOutcomes.Approved, request.Note ?? "Support reply approved.", ["support"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapDraft(draft);
    }

    public async Task<SupportReplyDraftDto?> RejectDraftAsync(Guid companyId, Guid userId, Guid draftId, SupportActionRequest request, CancellationToken cancellationToken)
    {
        var draft = await _dbContext.SupportReplyDrafts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draftId, cancellationToken);
        if (draft is null) return null;
        draft.Reject();
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.reply.rejected", "support_reply_draft", draft.Id.ToString("D"), AuditEventOutcomes.Rejected, request.Note ?? "Support reply rejected.", ["support"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapDraft(draft);
    }

    public async Task<SupportCaseDetailResponse?> SendDraftAsync(Guid companyId, Guid userId, Guid draftId, SendSupportReplyDraftRequest request, CancellationToken cancellationToken)
    {
        var draft = await _dbContext.SupportReplyDrafts.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draftId, cancellationToken);
        if (draft is null) return null;
        var supportCase = await _dbContext.SupportCases.Include(x => x.Messages).Include(x => x.Events).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == draft.SupportCaseId, cancellationToken);
        if (supportCase is null) return null;
        var safety = await EvaluateAndRecordSafetyAsync(companyId, draft, supportCase.Id, cancellationToken);
        if (safety.Decision != "allow")
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, request.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human, request.Autonomous ? null : userId, "support.reply.send_blocked", "support_reply_draft", draft.Id.ToString("D"), AuditEventOutcomes.Blocked, string.Join(" ", safety.Explanations), ["support", "safety"], Metadata: new Dictionary<string, string?> { ["policyVersion"] = safety.PolicyVersion, ["decision"] = safety.Decision }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new InvalidOperationException("This reply needs changes before it can be sent: " + string.Join(" ", safety.Explanations));
        }
        var lowRisk = supportCase.Category == SupportCaseCategories.GeneralQuestion ||
            supportCase.Category == SupportCaseCategories.AccountAccess ||
            supportCase.Category == SupportCaseCategories.BugReport;
        if (request.Autonomous && (!lowRisk || draft.Confidence < 0.8m || draft.Answerability < 0.75m))
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.reply.send_blocked", "support_reply_draft", draft.Id.ToString("D"), AuditEventOutcomes.Blocked, "Support reply requires review before sending.", ["support"]), cancellationToken);
            throw new InvalidOperationException("This reply requires review before it can be sent.");
        }
        if (!request.Autonomous && draft.Status != SupportReplyDraftStatuses.Approved)
        {
            throw new InvalidOperationException("Only approved support reply drafts can be sent.");
        }

        var latestInbound = supportCase.Messages
            .Where(x => x.Direction == SupportMessageDirections.Inbound)
            .OrderByDescending(x => x.OccurredUtc)
            .FirstOrDefault();
        var toEmail = FirstNonEmpty(request.ToEmail, latestInbound?.Sender);
        var subject = FirstNonEmpty(request.Subject, supportCase.Subject);
        var originalMessageId = FirstNonEmpty(request.OriginalMessageId, latestInbound?.ProviderMessageId, supportCase.ProviderMessageId, supportCase.CaseNumber);
        var providerThreadId = FirstNonEmptyOrNull(request.ProviderThreadId, latestInbound?.ProviderThreadId, supportCase.ProviderThreadId);
        SupportOutboundEmailSendResult sendResult;
        try
        {
            sendResult = await _outboundEmailSender.SendReplyAsync(new SupportOutboundEmailSendRequest(
                companyId,
                supportCase.Id,
                draft.Id,
                request.MailboxConnectionId,
                toEmail,
                request.ToDisplayName,
                subject,
                draft.DraftBody,
                originalMessageId,
                providerThreadId,
                request.InternetMessageId,
                $"support:{companyId:N}:{supportCase.Id:N}:{draft.Id:N}"), cancellationToken);
        }
        catch (MailboxProviderExecutionException ex)
        {
            draft.MarkSendFailed(ex.Message);
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, request.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human, request.Autonomous ? null : userId, "support.reply.send_failed", "support_reply_draft", draft.Id.ToString("D"), AuditEventOutcomes.Failed, ex.Message, ["support", "mailbox"], Metadata: new Dictionary<string, string?> { ["code"] = ex.Code, ["retryable"] = ex.IsRetryable.ToString() }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            draft.MarkSendFailed("Support reply could not be sent through the connected mailbox.");
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, request.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human, request.Autonomous ? null : userId, "support.reply.send_failed", "support_reply_draft", draft.Id.ToString("D"), AuditEventOutcomes.Failed, "Support reply could not be sent through the connected mailbox.", ["support", "mailbox"], Metadata: new Dictionary<string, string?> { ["exceptionType"] = ex.GetType().Name }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }

        var now = DateTime.UtcNow;
        draft.MarkSent(now);
        supportCase.Messages.Add(new SupportMessage(Guid.NewGuid(), companyId, supportCase.Id, SupportMessageDirections.Outbound, "email", "support", toEmail, draft.DraftBody, now, providerMessageId: sendResult.ProviderMessageId, providerThreadId: sendResult.ProviderThreadId, replyDraftId: draft.Id));
        supportCase.LinkProviderMessage(sendResult.ProviderThreadId, sendResult.ProviderMessageId);
        supportCase.MarkFirstResponseSent(now);
        supportCase.SetStatus(request.ResolveAfterSend ? SupportCaseStatuses.Resolved : SupportCaseStatuses.WaitingForCustomer);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.ReplySent, "Support reply sent.", request.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human, request.Autonomous ? null : userId, now));
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, request.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human, request.Autonomous ? null : userId, "support.reply.sent", "support_case", supportCase.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Support reply sent through the connected mailbox provider.", ["support", "mailbox"], Metadata: new Dictionary<string, string?> { ["provider"] = sendResult.Provider, ["mailboxConnectionId"] = sendResult.MailboxConnectionId.ToString("D"), ["providerMessageId"] = sendResult.ProviderMessageId, ["providerThreadId"] = sendResult.ProviderThreadId }, DataSourcesUsed: [new AuditDataSourceUsed("support_reply_draft", draft.Id.ToString("D"), "Support reply draft", null)]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await new SupportCaseService(_dbContext, _audit).GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    private static string FirstNonEmpty(params string?[] values) =>
        FirstNonEmptyOrNull(values) ?? throw new SupportValidationException(new Dictionary<string, string[]> { ["transport"] = ["Support reply transport metadata is incomplete."] });

    private static string? FirstNonEmptyOrNull(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();

    private async Task<SupportReplySafetyDecision> EvaluateAndRecordSafetyAsync(Guid companyId, SupportReplyDraft draft, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var decision = await _safetyPolicy.EvaluateAsync(companyId, supportCaseId, draft.DraftBody, draft.SourceReferencesJson, cancellationToken);
        var reasonJson = System.Text.Json.JsonSerializer.Serialize(decision.ReasonCodes);
        draft.RecordSafetyDecision(decision.Decision, reasonJson, decision.PolicyVersion, DateTime.UtcNow);
        return decision;
    }
}

public sealed class DeterministicSupportReplySafetyPolicy : ISupportReplySafetyPolicy
{
    public const string Version = SupportReplySafetyRules.PolicyVersion;
    private readonly VirtualCompanyDbContext _dbContext;

    public DeterministicSupportReplySafetyPolicy(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

    public async Task<SupportReplySafetyDecision> EvaluateAsync(Guid companyId, Guid supportCaseId, string draftBody, string? sourceReferencesJson, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken)
            ?? throw new InvalidOperationException("Support case was not found for safety evaluation.");
        return SupportReplySafetyRules.Evaluate(supportCase.Category, draftBody, sourceReferencesJson);
    }
}

public sealed class SupportMailboxOutboundEmailSender : ISupportOutboundEmailSender
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IFieldEncryptionService _fieldEncryption;
    private readonly TimeProvider _timeProvider;

    public SupportMailboxOutboundEmailSender(
        VirtualCompanyDbContext dbContext,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption,
        TimeProvider timeProvider)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _fieldEncryption = fieldEncryption;
        _timeProvider = timeProvider;
    }

    public async Task<SupportOutboundEmailSendResult> SendReplyAsync(SupportOutboundEmailSendRequest request, CancellationToken cancellationToken)
    {
        var connectionQuery = _dbContext.MailboxConnections.IgnoreQueryFilters()
            .Where(x => x.CompanyId == request.CompanyId && x.Status == MailboxConnectionStatus.Active && x.EncryptedAccessToken != null);
        if (request.MailboxConnectionId is Guid mailboxConnectionId)
        {
            connectionQuery = connectionQuery.Where(x => x.Id == mailboxConnectionId);
        }

        var connection = await connectionQuery.OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("A connected support mailbox is required before support replies can be sent.");
        var provider = _providerRegistry.Resolve(connection.Provider);
        var accessToken = await GetMailboxAccessTokenAsync(provider, connection, cancellationToken);
        var result = await provider.SendReplyAsync(accessToken, new MailboxReplyExecutionRequest(
            request.CompanyId,
            connection.Id,
            connection.Provider.ToStorageValue(),
            request.OriginalMessageId,
            request.ProviderThreadId,
            request.InternetMessageId,
            request.ToEmail,
            request.ToDisplayName,
            request.Subject,
            request.BodyText,
            request.IdempotencyKey), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new SupportOutboundEmailSendResult(connection.Provider.ToStorageValue(), connection.Id, result.ProviderMessageId, result.ProviderThreadId, result.Status);
    }

    private async Task<string> GetMailboxAccessTokenAsync(IMailboxProviderClient provider, MailboxConnection connection, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (!string.IsNullOrWhiteSpace(connection.EncryptedAccessToken) &&
            (!connection.AccessTokenExpiresUtc.HasValue || connection.AccessTokenExpiresUtc.Value > now.AddMinutes(5)))
        {
            return _fieldEncryption.Decrypt(
                connection.CompanyId,
                CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
                connection.EncryptedAccessToken);
        }

        if (string.IsNullOrWhiteSpace(connection.EncryptedRefreshToken))
        {
            if (!string.IsNullOrWhiteSpace(connection.EncryptedAccessToken))
            {
                return _fieldEncryption.Decrypt(
                    connection.CompanyId,
                    CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
                    connection.EncryptedAccessToken);
            }

            throw new InvalidOperationException("Mailbox access token is missing.");
        }

        var refreshToken = _fieldEncryption.Decrypt(
            connection.CompanyId,
            CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "refresh_token"),
            connection.EncryptedRefreshToken);
        var tokenResult = await provider.RefreshTokenAsync(new MailboxRefreshTokenRequest(refreshToken), cancellationToken);
        connection.StoreEncryptedCredentials(
            _fieldEncryption.Encrypt(connection.CompanyId, CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"), tokenResult.AccessToken),
            string.IsNullOrWhiteSpace(tokenResult.RefreshToken)
                ? connection.EncryptedRefreshToken
                : _fieldEncryption.Encrypt(connection.CompanyId, CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "refresh_token"), tokenResult.RefreshToken),
            tokenResult.AccessTokenExpiresUtc,
            tokenResult.GrantedScopes.Count > 0 ? tokenResult.GrantedScopes : connection.GrantedScopes);
        connection.SetStatus(MailboxConnectionStatus.Active);
        return tokenResult.AccessToken;
    }
}

public sealed class SupportToolActionService : ISupportToolActionService
{
    private readonly ISupportCaseService _cases;
    private readonly ISupportTriageService _triage;
    private readonly ISupportReplyDraftService _drafts;
    private readonly ISupportRefundWorkflowService _refunds;
    private readonly ISupportKnowledgeGapService _knowledgeGaps;
    private readonly IAuditEventWriter _audit;

    public SupportToolActionService(
        ISupportCaseService cases,
        ISupportTriageService triage,
        ISupportReplyDraftService drafts,
        ISupportRefundWorkflowService refunds,
        ISupportKnowledgeGapService knowledgeGaps,
        IAuditEventWriter audit)
    {
        _cases = cases;
        _triage = triage;
        _drafts = drafts;
        _refunds = refunds;
        _knowledgeGaps = knowledgeGaps;
        _audit = audit;
    }

    public async Task<SupportToolActionResult> ExecuteAsync(Guid companyId, Guid agentId, SupportToolActionRequest request, CancellationToken cancellationToken)
    {
        var tool = request.ToolName.Trim();
        if (agentId == Guid.Empty)
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, null, "support.tool.denied", "support_tool", tool, AuditEventOutcomes.Denied, "Support tool execution requires an agent identity.", ["support"]), cancellationToken);
            return new SupportToolActionResult(false, "denied", "Support tool execution requires an agent identity.", request.SupportCaseId);
        }

        tool = NormalizeToolName(tool);
        var policy = EvaluateToolPolicy(tool, request);
        if (!policy.Allowed)
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, agentId, "support.tool.denied", "support_tool", tool, AuditEventOutcomes.Denied, policy.Summary, ["support", "policy"], Metadata: new Dictionary<string, string?> { ["policyDecision"] = policy.Status }), cancellationToken);
            return new SupportToolActionResult(false, policy.Status, policy.Summary, request.SupportCaseId);
        }

        try
        {
            if (tool.Equals("ClassifySupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid classifyId)
            {
                await _triage.TriageAsync(companyId, Guid.Empty, classifyId, cancellationToken);
                return new SupportToolActionResult(true, "succeeded", "Support case classified.", classifyId);
            }

            if (tool.Equals("DraftSupportReply", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid draftId)
            {
                var draft = await _drafts.GenerateDraftAsync(companyId, Guid.Empty, draftId, new GenerateSupportReplyDraftRequest(), cancellationToken);
                return new SupportToolActionResult(draft is not null, draft is null ? "not_found" : "succeeded", draft is null ? "Support case was not found." : "Support reply drafted.", draftId, draft?.Id);
            }

            if (tool.Equals("UpdateSupportCaseStatus", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid caseId && request.Payload.TryGetValue("status", out var status) && !string.IsNullOrWhiteSpace(status))
            {
                await _cases.ChangeStatusAsync(companyId, Guid.Empty, caseId, new ChangeSupportStatusRequest(status!), cancellationToken);
                return new SupportToolActionResult(true, "succeeded", "Support case status updated.", caseId);
            }

            if (tool.Equals("AddInternalSupportNote", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid noteCaseId)
            {
                var note = RequiredPayload(request, "note");
                var updated = await _cases.AddInternalNoteAsync(companyId, Guid.Empty, noteCaseId, new AddSupportInternalNoteRequest(note), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Internal note added.", noteCaseId);
            }

            if (tool.Equals("ChangeSupportPriority", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid priorityCaseId)
            {
                var priority = RequiredPayload(request, "priority");
                var updated = await _cases.ChangePriorityAsync(companyId, Guid.Empty, priorityCaseId, new ChangeSupportPriorityRequest(priority, OptionalPayload(request, "note")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support priority updated.", priorityCaseId);
            }

            if (tool.Equals("ChangeSupportCategory", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid categoryCaseId)
            {
                var category = RequiredPayload(request, "category");
                var updated = await _cases.ChangeCategoryAsync(companyId, Guid.Empty, categoryCaseId, new ChangeSupportCategoryRequest(category, OptionalPayload(request, "note")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support category updated.", categoryCaseId);
            }

            if (tool.Equals("AssignSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid assignCaseId)
            {
                var assignedAgentId = OptionalGuidPayload(request, "assignedAgentId");
                var assignedUserId = OptionalGuidPayload(request, "assignedUserId");
                var updated = await _cases.AssignAsync(companyId, Guid.Empty, assignCaseId, new AssignSupportCaseRequest(assignedAgentId, assignedUserId, OptionalPayload(request, "reason")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case assigned.", assignCaseId);
            }

            if (tool.Equals("EscalateSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid escalateCaseId)
            {
                var updated = await _cases.ChangeStatusAsync(companyId, Guid.Empty, escalateCaseId, new ChangeSupportStatusRequest(SupportCaseStatuses.Escalated, OptionalPayload(request, "reason") ?? "Escalated by support agent."), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case escalated.", escalateCaseId);
            }

            if (tool.Equals("RequestMissingInformation", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid missingInfoCaseId)
            {
                var question = OptionalPayload(request, "question") ?? "Please share the missing details so we can continue.";
                var draft = await _drafts.GenerateDraftAsync(companyId, Guid.Empty, missingInfoCaseId, new GenerateSupportReplyDraftRequest("Helpful"), cancellationToken);
                if (draft is not null)
                {
                    draft = await _drafts.EditDraftAsync(companyId, Guid.Empty, draft.Id, new EditSupportReplyDraftRequest($"Hello,\n\n{question}\n\nBest regards,\nSupport", draft.Tone), cancellationToken);
                }
                return new SupportToolActionResult(draft is not null, draft is null ? "not_found" : "succeeded", draft is null ? "Support case was not found." : "Missing-information reply drafted.", missingInfoCaseId, draft?.Id);
            }

            if (tool.Equals("ResolveSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid resolveCaseId)
            {
                var summary = RequiredPayload(request, "summary");
                var updated = await _cases.ResolveAsync(companyId, Guid.Empty, resolveCaseId, new ResolveSupportCaseRequest(summary, OptionalPayload(request, "outcome") ?? "Resolved"), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case resolved.", resolveCaseId);
            }

            if (tool.Equals("ReopenSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid reopenCaseId)
            {
                var updated = await _cases.ReopenAsync(companyId, Guid.Empty, reopenCaseId, new SupportActionRequest(OptionalPayload(request, "note")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case reopened.", reopenCaseId);
            }

            if (tool.Equals("CloseSupportCase", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid closeCaseId)
            {
                var updated = await _cases.CloseAsync(companyId, Guid.Empty, closeCaseId, new SupportActionRequest(OptionalPayload(request, "note")), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support case was not found." : "Support case closed.", closeCaseId);
            }

            if (tool.Equals("RequestSupportRefund", StringComparison.OrdinalIgnoreCase) && request.SupportCaseId is Guid refundCaseId)
            {
                var refund = await _refunds.RequestRefundAsync(companyId, Guid.Empty, refundCaseId, new CreateSupportRefundRequest(
                    RequiredDecimalPayload(request, "amount"),
                    OptionalPayload(request, "currency") ?? "SEK",
                    OptionalPayload(request, "reasonCode") ?? "customer_support",
                    RequiredPayload(request, "explanation"),
                    OptionalGuidPayload(request, "invoiceId"),
                    OptionalGuidPayload(request, "paymentId")), cancellationToken);
                return new SupportToolActionResult(refund is not null, refund is null ? "not_found" : "succeeded", refund is null ? "Support case was not found." : "Refund or credit approval requested.", refundCaseId, refund?.Id);
            }

            if (tool.Equals("CreateSupportKnowledgeGap", StringComparison.OrdinalIgnoreCase))
            {
                var gap = await _knowledgeGaps.CreateOrIncrementAsync(companyId, new CreateSupportKnowledgeGapRequest(
                    request.SupportCaseId,
                    OptionalGuidPayload(request, "supportReplyDraftId"),
                    OptionalPayload(request, "category") ?? SupportCaseCategories.GeneralQuestion,
                    RequiredPayload(request, "questionSummary"),
                    RequiredPayload(request, "missingInformationSummary"),
                    OptionalPayload(request, "retrievalSourceSummary")), cancellationToken);
                return new SupportToolActionResult(true, "succeeded", "Support knowledge gap recorded.", request.SupportCaseId, gap.Id);
            }

            if (tool.Equals("SendSupportReply", StringComparison.OrdinalIgnoreCase))
            {
                var sendDraftId = RequiredGuidPayload(request, "draftId");
                var updated = await _drafts.SendDraftAsync(companyId, Guid.Empty, sendDraftId, new SendSupportReplyDraftRequest(BoolPayload(request, "resolveAfterSend"), request.Autonomous), cancellationToken);
                return new SupportToolActionResult(updated is not null, updated is null ? "not_found" : "succeeded", updated is null ? "Support reply draft was not found." : "Support reply sent.", updated?.Id ?? request.SupportCaseId);
            }

            return new SupportToolActionResult(false, "unsupported", "Support tool is not supported yet or missing required payload.", request.SupportCaseId);
        }
        finally
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, agentId, "support.tool.executed", "support_tool", tool, AuditEventOutcomes.Succeeded, "Support tool execution attempted.", ["support"]), cancellationToken);
        }
    }

    private sealed record SupportToolPolicyDecision(bool Allowed, string Status, string Summary);

    private static string NormalizeToolName(string tool) => tool.Trim() switch
    {
        var value when value.Equals("AddSupportInternalNote", StringComparison.OrdinalIgnoreCase) => "AddInternalSupportNote",
        var value when value.Equals("MarkSupportCaseResolved", StringComparison.OrdinalIgnoreCase) => "ResolveSupportCase",
        var value when value.Equals("RequestRefund", StringComparison.OrdinalIgnoreCase) => "RequestSupportRefund",
        var value when value.Equals("CreateBugReportTask", StringComparison.OrdinalIgnoreCase) => "CreateSupportKnowledgeGap",
        var value when value.Equals("CreateOperationsFollowUpTask", StringComparison.OrdinalIgnoreCase) => "CreateSupportKnowledgeGap",
        var value => value
    };

    private static SupportToolPolicyDecision EvaluateToolPolicy(string tool, SupportToolActionRequest request)
    {
        var knownTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ClassifySupportCase",
            "DraftSupportReply",
            "UpdateSupportCaseStatus",
            "AddInternalSupportNote",
            "ChangeSupportPriority",
            "ChangeSupportCategory",
            "AssignSupportCase",
            "EscalateSupportCase",
            "RequestMissingInformation",
            "ResolveSupportCase",
            "ReopenSupportCase",
            "CloseSupportCase",
            "RequestSupportRefund",
            "CreateSupportKnowledgeGap",
            "SendSupportReply"
        };
        if (!knownTools.Contains(tool))
        {
            return new SupportToolPolicyDecision(false, "unsupported", "Support tool is not supported by the shared support tool policy.");
        }

        var requiresApproval = tool.Equals("RequestSupportRefund", StringComparison.OrdinalIgnoreCase) ||
            tool.Equals("SendSupportReply", StringComparison.OrdinalIgnoreCase) ||
            tool.Equals("ResolveSupportCase", StringComparison.OrdinalIgnoreCase) ||
            tool.Equals("CloseSupportCase", StringComparison.OrdinalIgnoreCase) ||
            tool.Equals("EscalateSupportCase", StringComparison.OrdinalIgnoreCase);
        if (request.Autonomous && requiresApproval)
        {
            return new SupportToolPolicyDecision(false, "approval_required", "This support action is risky and requires human approval before execution.");
        }

        return new SupportToolPolicyDecision(true, "allowed", "Support tool execution allowed by policy.");
    }
    private static string RequiredPayload(SupportToolActionRequest request, string key) =>
        OptionalPayload(request, key) ?? throw new SupportValidationException(new Dictionary<string, string[]> { [key] = ["This field is required."] });

    private static string? OptionalPayload(SupportToolActionRequest request, string key) =>
        request.Payload.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static Guid RequiredGuidPayload(SupportToolActionRequest request, string key) =>
        OptionalGuidPayload(request, key) ?? throw new SupportValidationException(new Dictionary<string, string[]> { [key] = ["This field must be a valid identifier."] });

    private static Guid? OptionalGuidPayload(SupportToolActionRequest request, string key) =>
        Guid.TryParse(OptionalPayload(request, key), out var value) && value != Guid.Empty ? value : null;

    private static decimal RequiredDecimalPayload(SupportToolActionRequest request, string key)
    {
        if (decimal.TryParse(OptionalPayload(request, key), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0)
        {
            return value;
        }

        throw new SupportValidationException(new Dictionary<string, string[]> { [key] = ["This field must be a positive amount."] });
    }

    private static bool BoolPayload(SupportToolActionRequest request, string key) =>
        bool.TryParse(OptionalPayload(request, key), out var value) && value;
}

public sealed class SupportAgentOrchestrationService : ISupportAgentOrchestrationService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISupportContextResolutionService _context;
    private readonly ISupportTriageService _triage;
    private readonly ISupportReplyDraftService _drafts;
    private readonly ISupportReplySafetyPolicy _safety;
    private readonly IAuditEventWriter _audit;

    public SupportAgentOrchestrationService(
        VirtualCompanyDbContext dbContext,
        ISupportContextResolutionService context,
        ISupportTriageService triage,
        ISupportReplyDraftService drafts,
        ISupportReplySafetyPolicy safety,
        IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _context = context;
        _triage = triage;
        _drafts = drafts;
        _safety = safety;
        _audit = audit;
    }

    public async Task<SupportAgentExecutionDto?> RunAsync(Guid companyId, Guid userId, Guid supportCaseId, RunSupportAgentRequest request, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.SupportCases.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (!exists) return null;
        var idempotencyKey = string.IsNullOrWhiteSpace(request.IdempotencyKey) ? $"support-agent:{supportCaseId:N}" : request.IdempotencyKey.Trim();
        var execution = await _dbContext.SupportAgentExecutions.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (execution is { Status: "completed" }) return Map(execution);

        var agentId = await _dbContext.Agents.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Department == "Support")
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (execution is null)
        {
            execution = new SupportAgentExecution(Guid.NewGuid(), companyId, supportCaseId, agentId, idempotencyKey);
            _dbContext.SupportAgentExecutions.Add(execution);
        }

        try
        {
            execution.MoveTo("context", "Resolved tenant-scoped customer and case context.");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _context.ResolveAsync(companyId, supportCaseId, cancellationToken);

            execution.MoveTo("triage", "Classified case priority, category, and risk.");
            await _dbContext.SaveChangesAsync(cancellationToken);
            await _triage.TriageAsync(companyId, userId, supportCaseId, cancellationToken);

            execution.MoveTo("draft", "Generated a grounded reply draft for human review.");
            await _dbContext.SaveChangesAsync(cancellationToken);
            var draft = await _drafts.GenerateDraftAsync(companyId, userId, supportCaseId, new GenerateSupportReplyDraftRequest("Helpful", request.ForceReview), cancellationToken);

            if (draft is not null)
            {
                execution.MoveTo("safety", "Evaluated draft safety policy.");
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _safety.EvaluateAsync(companyId, supportCaseId, draft.DraftBody, draft.SourceReferencesJson, cancellationToken);
            }

            execution.Complete(draft?.Id, draft is null ? "Support agent completed without creating a draft." : "Support agent created a policy-evaluated reply draft.");
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, agentId, "support.agent.run_completed", "support_case", supportCaseId.ToString("D"), AuditEventOutcomes.Succeeded, execution.Summary, ["support", "agent"], Metadata: new Dictionary<string, string?> { ["executionId"] = execution.Id.ToString("D") }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Map(execution);
        }
        catch (Exception ex)
        {
            execution.Fail(execution.CurrentStep, "Support agent run stopped before an unsafe follow-up could run.");
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Agent, agentId, "support.agent.run_failed", "support_case", supportCaseId.ToString("D"), AuditEventOutcomes.Failed, execution.FailureSummary, ["support", "agent"], Metadata: new Dictionary<string, string?> { ["executionId"] = execution.Id.ToString("D"), ["exceptionType"] = ex.GetType().Name }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }

    private static SupportAgentExecutionDto Map(SupportAgentExecution execution) =>
        new(execution.Id, execution.SupportCaseId, execution.AgentId, execution.Status, execution.CurrentStep, execution.CreatedDraftId, execution.Summary, execution.FailureSummary, execution.CreatedUtc, execution.UpdatedUtc, execution.CompletedUtc);
}

public sealed class SupportRefundWorkflowService : ISupportRefundWorkflowService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportRefundWorkflowService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<SupportRefundRequestDto?> RequestRefundAsync(Guid companyId, Guid userId, Guid supportCaseId, CreateSupportRefundRequest request, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.Include(x => x.Events).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (supportCase is null) return null;

        var actorType = userId == Guid.Empty ? AuditActorTypes.Agent : AuditActorTypes.Human;
        var actorId = userId == Guid.Empty ? supportCase.Id : userId;
        var refund = new SupportRefundRequest(Guid.NewGuid(), companyId, supportCase.Id, request.Amount, request.Currency, request.ReasonCode, request.Explanation, request.InvoiceId ?? supportCase.RelatedInvoiceId, request.PaymentId ?? supportCase.RelatedPaymentId, null, userId == Guid.Empty ? null : userId);
        var approvalTask = new WorkTask(
            Guid.NewGuid(),
            companyId,
            "support_refund_approval",
            $"Approve support refund or credit for {supportCase.CaseNumber}",
            request.Explanation,
            request.Amount >= 5000m ? WorkTaskPriority.High : WorkTaskPriority.Normal,
            null,
            null,
            actorType,
            actorId,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["supportCaseId"] = supportCase.Id.ToString("D"),
                ["refundRequestId"] = refund.Id.ToString("D"),
                ["amount"] = request.Amount,
                ["currency"] = request.Currency,
                ["reasonCode"] = request.ReasonCode,
                ["invoiceId"] = refund.InvoiceId?.ToString("D"),
                ["paymentId"] = refund.PaymentId?.ToString("D")
            },
            sourceType: WorkTaskSourceTypes.Agent,
            triggerSource: "support_refund_request",
            creationReason: "Support refund or credit requires approval before finance action.");
        approvalTask.UpdateStatus(WorkTaskStatus.AwaitingApproval, rationaleSummary: "Waiting for refund or credit approval.", confidenceScore: 0.9m);
        _dbContext.SupportRefundRequests.Add(refund);
        _dbContext.WorkTasks.Add(approvalTask);

        var approval = ApprovalRequest.CreateForTarget(
            Guid.NewGuid(),
            companyId,
            ApprovalTargetEntityType.Task,
            approvalTask.Id,
            actorType,
            actorId,
            "support_refund_credit",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["supportCaseId"] = supportCase.Id.ToString("D"),
                ["supportCaseNumber"] = supportCase.CaseNumber,
                ["refundRequestId"] = refund.Id.ToString("D"),
                ["amount"] = request.Amount,
                ["currency"] = request.Currency,
                ["reasonCode"] = request.ReasonCode,
                ["explanation"] = request.Explanation
            },
            "owner",
            null,
            [new ApprovalStepDefinition(1, ApprovalStepApproverType.Role, "owner")]);
        _dbContext.ApprovalRequests.Add(approval);
        refund.LinkApproval(approval.Id);
        supportCase.SetStatus(SupportCaseStatuses.AwaitingApproval);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.ApprovalRequested, "Refund or credit approval requested.", actorType, actorId, DateTime.UtcNow));
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, actorType, actorId, "support.refund.requested", "support_case", supportCase.Id.ToString("D"), AuditEventOutcomes.Pending, request.Explanation, ["support", "finance", "approvals"], Metadata: new Dictionary<string, string?> { ["approvalRequestId"] = approval.Id.ToString("D"), ["approvalTaskId"] = approvalTask.Id.ToString("D"), ["refundRequestId"] = refund.Id.ToString("D") }), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapRefund(refund);
    }
}

public sealed class SupportRefundApprovalOutcomeHandler : ISupportRefundApprovalOutcomeHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;
    private readonly ISupportRefundFinanceService _finance;

    public SupportRefundApprovalOutcomeHandler(VirtualCompanyDbContext dbContext, IAuditEventWriter audit, ISupportRefundFinanceService finance)
    {
        _dbContext = dbContext;
        _audit = audit;
        _finance = finance;
    }

    public async Task<bool> ProcessAsync(
        Guid companyId,
        Guid approvalRequestId,
        string approvalStatus,
        Guid? decidedByUserId,
        string? decisionSummary,
        CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty || approvalRequestId == Guid.Empty)
        {
            throw new ArgumentException("Company and approval identifiers are required.");
        }

        var refund = await _dbContext.SupportRefundRequests
            .Include(x => x.SupportCase)
            .ThenInclude(x => x.Events)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.ApprovalRequestId == approvalRequestId,
                cancellationToken);
        if (refund is null)
        {
            return false;
        }

        if (!refund.ApplyApprovalOutcome(approvalStatus))
        {
            return false;
        }

        var isApproved = string.Equals(refund.Status, SupportRefundRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase);
        var summary = string.IsNullOrWhiteSpace(decisionSummary)
            ? isApproved
                ? "Refund or credit approved and ready for finance validation."
                : $"Refund or credit approval ended as {SupportLabels.Status(refund.Status).ToLowerInvariant()}."
            : decisionSummary.Trim();
        refund.SupportCase.Events.Add(new SupportCaseEvent(
            Guid.NewGuid(),
            companyId,
            refund.SupportCaseId,
            SupportCaseEventTypes.ApprovalResolved,
            summary,
            decidedByUserId.HasValue ? AuditActorTypes.Human : AuditActorTypes.System,
            decidedByUserId,
            DateTime.UtcNow));

        if (!isApproved && string.Equals(refund.SupportCase.Status, SupportCaseStatuses.AwaitingApproval, StringComparison.OrdinalIgnoreCase))
        {
            refund.SupportCase.SetStatus(SupportCaseStatuses.WaitingInternal);
        }

        await _audit.WriteAsync(new AuditEventWriteRequest(
            companyId,
            decidedByUserId.HasValue ? AuditActorTypes.Human : AuditActorTypes.System,
            decidedByUserId,
            "support.refund.approval_resolved",
            "support_refund_request",
            refund.Id.ToString("D"),
            isApproved ? AuditEventOutcomes.Approved : AuditEventOutcomes.Rejected,
            summary,
            ["support", "finance", "approvals"],
            Metadata: new Dictionary<string, string?>
            {
                ["approvalRequestId"] = approvalRequestId.ToString("D"),
                ["refundRequestId"] = refund.Id.ToString("D"),
                ["status"] = refund.Status
            }), cancellationToken);

        if (isApproved)
        {
            await _finance.CreateApprovedActionAsync(companyId, refund.Id, cancellationToken);
        }

        return true;
    }
}

public sealed class SupportRefundFinanceService : ISupportRefundFinanceService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;
    private readonly TimeProvider _timeProvider;
    private readonly IFinanceCustomerInvoiceFortnoxActionService? _customerInvoiceActions;

    public SupportRefundFinanceService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter audit,
        TimeProvider timeProvider,
        IFinanceCustomerInvoiceFortnoxActionService? customerInvoiceActions = null)
    {
        _dbContext = dbContext;
        _audit = audit;
        _timeProvider = timeProvider;
        _customerInvoiceActions = customerInvoiceActions;
    }

    public async Task<SupportRefundFinanceActionResult> CreateApprovedActionAsync(Guid companyId, Guid refundRequestId, CancellationToken cancellationToken)
    {
        var refund = await _dbContext.SupportRefundRequests
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == refundRequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Support refund request was not found.");
        if (refund.FinanceActionReferenceId is Guid existingActionId)
        {
            return new SupportRefundFinanceActionResult(refund.Id, existingActionId, false, 0m, refund.Status, "The finance action already exists.");
        }

        if (!string.Equals(refund.Status, SupportRefundRequestStatuses.Approved, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only approved support refunds can create finance actions.");
        }

        if (refund.InvoiceId is not Guid invoiceId)
        {
            throw new InvalidOperationException("Link a customer invoice before creating the refund or credit action.");
        }

        var invoice = await _dbContext.FinanceInvoices
            .Include(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invoiceId, cancellationToken)
            ?? throw new InvalidOperationException("The linked customer invoice was not found.");
        if (!string.Equals(invoice.DocumentKind, FinanceDocumentKinds.Invoice, StringComparison.OrdinalIgnoreCase) || invoice.Amount <= 0m)
        {
            throw new InvalidOperationException("The linked record is not an eligible customer invoice.");
        }

        if (!string.Equals(invoice.Currency, refund.Currency, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The refund currency must match the customer invoice currency.");
        }

        if (refund.PaymentId is Guid paymentId)
        {
            var paymentMatches = await _dbContext.Payments.AnyAsync(x =>
                x.CompanyId == companyId && x.Id == paymentId && x.Currency == refund.Currency,
                cancellationToken);
            if (!paymentMatches)
            {
                throw new InvalidOperationException("The linked payment was not found or uses another currency.");
            }
        }

        var paidAmount = Math.Max(invoice.PaidAmount, invoice.Allocations.Sum(x => x.AllocatedAmount));
        if (paidAmount <= 0m)
        {
            throw new InvalidOperationException("The customer invoice has no recorded payment to refund.");
        }

        var committedAmount = await _dbContext.SupportRefundRequests
            .Where(x => x.CompanyId == companyId && x.InvoiceId == invoice.Id && x.Id != refund.Id && x.FinanceActionReferenceId != null)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;
        var refundableBalance = Math.Max(0m, decimal.Round(paidAmount - committedAmount, 2, MidpointRounding.AwayFromZero));
        if (refund.Amount > refundableBalance)
        {
            throw new InvalidOperationException($"The refund amount exceeds the refundable balance of {refundableBalance:0.00} {refund.Currency}.");
        }

        var actionId = CreateDeterministicId("support-refund-credit", companyId, refund.Id);
        var existing = await _dbContext.FinanceInvoices.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == actionId, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        if (existing is null)
        {
            existing = new FinanceInvoice(
                actionId,
                companyId,
                invoice.CounterpartyId,
                $"SUP-CR-{refund.Id:N}"[..Math.Min(64, $"SUP-CR-{refund.Id:N}".Length)],
                now,
                now,
                -refund.Amount,
                refund.Currency,
                "approved",
                settlementStatus: FinanceSettlementStatuses.Unpaid,
                postingStatus: FinanceDocumentPostingStatuses.Draft,
                dueStatus: FinanceDocumentDueStatuses.NotDue,
                documentKind: FinanceDocumentKinds.CreditNote,
                processingStatus: FinanceDocumentProcessingStatuses.None);
            _dbContext.FinanceInvoices.Add(existing);
        }

        var created = refund.LinkFinanceAction(existing.Id);
        await _audit.WriteAsync(new AuditEventWriteRequest(
            companyId,
            AuditActorTypes.System,
            null,
            "support.refund.finance_action_created",
            "support_refund_request",
            refund.Id.ToString("D"),
            AuditEventOutcomes.Succeeded,
            "Approved support refund was converted into an internal customer credit action.",
            ["support", "finance"],
            Metadata: new Dictionary<string, string?>
            {
                ["financeActionReferenceId"] = existing.Id.ToString("D"),
                ["sourceInvoiceId"] = invoice.Id.ToString("D"),
                ["refundableBalance"] = refundableBalance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            }), cancellationToken);
        return new SupportRefundFinanceActionResult(refund.Id, existing.Id, created, refundableBalance, refund.Status, "Customer credit action is ready for finance execution.");
    }

    public async Task<SupportRefundRequestDto> RequestExecutionAsync(
        Guid companyId,
        Guid refundRequestId,
        Guid? actorUserId,
        string actorDisplayName,
        CancellationToken cancellationToken)
    {
        var refund = await _dbContext.SupportRefundRequests
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == refundRequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Support refund request was not found.");
        if (refund.FinanceActionReferenceId is not Guid creditActionId)
        {
            throw new InvalidOperationException("Create the internal customer credit action before requesting provider execution.");
        }

        var customerInvoiceActions = _customerInvoiceActions
            ?? throw new InvalidOperationException("Customer credit provider execution is not configured.");
        var state = await customerInvoiceActions.RequestExportAsync(
            new RequestCustomerInvoiceFortnoxExportCommand(companyId, creditActionId, actorUserId, actorDisplayName),
            cancellationToken);
        refund.MarkPendingFinanceApproval(state.CreateWriteRequestId, state.CreateApprovalId);
        await _audit.WriteAsync(new AuditEventWriteRequest(
            companyId,
            actorUserId.HasValue ? AuditActorTypes.Human : AuditActorTypes.System,
            actorUserId,
            "support.refund.finance_execution_requested",
            "support_refund_request",
            refund.Id.ToString("D"),
            AuditEventOutcomes.Pending,
            "Customer credit is waiting for accounting-system approval.",
            ["support", "finance", "approvals", "fortnox"],
            Metadata: new Dictionary<string, string?>
            {
                ["financeActionReferenceId"] = creditActionId.ToString("D"),
                ["writeRequestId"] = state.CreateWriteRequestId?.ToString("D"),
                ["approvalRequestId"] = state.CreateApprovalId?.ToString("D")
            }), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapRefund(refund);
    }

    public async Task<SupportRefundRequestDto?> RefreshExecutionAsync(Guid companyId, Guid financeActionReferenceId, CancellationToken cancellationToken)
    {
        var refund = await _dbContext.SupportRefundRequests
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.FinanceActionReferenceId == financeActionReferenceId, cancellationToken);
        if (refund is null)
        {
            return null;
        }

        var writeRequestId = CustomerInvoiceFortnoxActionService.CreateWriteRequestId("create", financeActionReferenceId, null);
        var write = await _dbContext.FinanceIntegrationWriteCommands
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken);
        if (write is null)
        {
            return SupportCaseService.MapRefund(refund);
        }

        if (refund.ApplyFinanceExecutionStatus(write.Status, write.SafeFailureSummary))
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.System,
                null,
                "support.refund.finance_execution_updated",
                "support_refund_request",
                refund.Id.ToString("D"),
                write.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Pending,
                BuildSafeExecutionSummary(refund.Status),
                ["support", "finance", "fortnox"],
                Metadata: new Dictionary<string, string?>
                {
                    ["financeActionReferenceId"] = financeActionReferenceId.ToString("D"),
                    ["writeRequestId"] = write.Id.ToString("D"),
                    ["writeStatus"] = write.Status
                }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return SupportCaseService.MapRefund(refund);
    }

    public async Task<SupportRefundRequestDto?> RefreshByWriteRequestAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken)
    {
        var actionIds = await _dbContext.SupportRefundRequests
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FinanceActionReferenceId != null)
            .Select(x => x.FinanceActionReferenceId!.Value)
            .ToListAsync(cancellationToken);
        var actionId = actionIds.FirstOrDefault(id =>
            CustomerInvoiceFortnoxActionService.CreateWriteRequestId("create", id, null) == writeRequestId);
        return actionId == Guid.Empty
            ? null
            : await RefreshExecutionAsync(companyId, actionId, cancellationToken);
    }

    public async Task<SupportRefundRequestDto> CancelAsync(Guid companyId, Guid refundRequestId, Guid actorUserId, string reason, CancellationToken cancellationToken)
    {
        SupportValidationException.ThrowIfBlank(reason, nameof(reason));
        var refund = await _dbContext.SupportRefundRequests.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == refundRequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Support refund request was not found.");
        if (refund.CancelBeforeExecution())
        {
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId, "support.refund.cancelled", "support_refund_request", refund.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Refund or credit request cancelled before provider execution.", ["support", "finance"], Metadata: new Dictionary<string, string?> { ["reason"] = reason.Trim()[..Math.Min(reason.Trim().Length, 500)] }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        return SupportCaseService.MapRefund(refund);
    }

    public async Task<SupportRefundRequestDto> ReconcileAsync(Guid companyId, Guid refundRequestId, Guid actorUserId, CancellationToken cancellationToken)
    {
        var refund = await _dbContext.SupportRefundRequests.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == refundRequestId, cancellationToken)
            ?? throw new KeyNotFoundException("Support refund request was not found.");
        if (refund.FinanceActionReferenceId is not Guid actionId) throw new InvalidOperationException("No accounting-system action exists to reconcile.");
        var refreshed = await RefreshExecutionAsync(companyId, actionId, cancellationToken);
        if (refreshed is not null && refreshed.Status is not (SupportRefundRequestStatuses.Failed or SupportRefundRequestStatuses.ReconciliationRequired)) return refreshed;
        refund.MarkReconciliationRequired("The accounting-system result is missing or inconclusive. Verify the provider record before retrying.");
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, actorUserId, "support.refund.reconciliation_requested", "support_refund_request", refund.Id.ToString("D"), AuditEventOutcomes.Pending, "Refund or credit requires accounting-system reconciliation.", ["support", "finance", "reconciliation"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapRefund(refund);
    }

    private static string BuildSafeExecutionSummary(string status) => status switch
    {
        SupportRefundRequestStatuses.Completed => "Customer credit was completed in the accounting system.",
        SupportRefundRequestStatuses.Failed => "Customer credit execution failed and can be reviewed safely.",
        SupportRefundRequestStatuses.Cancelled => "Customer credit execution did not receive final approval.",
        SupportRefundRequestStatuses.ReconciliationRequired => "Customer credit outcome needs reconciliation before retrying.",
        _ => "Customer credit execution state was updated."
    };

    private static Guid CreateDeterministicId(string purpose, Guid companyId, Guid sourceId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{purpose}:{companyId:N}:{sourceId:N}"));
        Span<byte> bytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(bytes);
        return new Guid(bytes);
    }
}

public sealed class SupportSlaPolicyService : ISupportSlaPolicyService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportSlaPolicyService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SupportSlaPolicyDto>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await _dbContext.SupportSlaPolicies.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.Category)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToList();

    public async Task<SupportSlaPolicyDto> UpsertAsync(Guid companyId, Guid userId, UpsertSupportSlaPolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.FirstResponseMinutes <= 0 || request.ResolutionMinutes <= 0 || request.ResolutionMinutes < request.FirstResponseMinutes)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.ResolutionMinutes)] = ["Resolution time must be at least the first-response time and both must be positive."] });
        }

        var category = SupportCaseCategories.Normalize(request.Category);
        var priority = SupportPriorities.Normalize(request.Priority);
        var tier = string.IsNullOrWhiteSpace(request.CustomerTier) ? null : request.CustomerTier.Trim();
        if (request.RiskThresholdMinutes <= 0 || request.RiskThresholdMinutes >= request.ResolutionMinutes)
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.RiskThresholdMinutes)] = ["Risk threshold must be positive and shorter than the resolution target."] });
        string recipientRole;
        try
        {
            recipientRole = CompanyMembershipRoles.ToStorageValue(CompanyMembershipRoles.Parse(request.EscalationRecipientRole));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.EscalationRecipientRole)] = ["Choose a supported company role for escalation."] });
        }
        var duplicate = await _dbContext.SupportSlaPolicies.AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.Id != request.Id && x.IsActive && request.IsActive &&
            x.Category == category && x.Priority == priority && x.CustomerTier == tier,
            cancellationToken);
        if (duplicate)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.Category)] = ["An active SLA policy already covers this category, priority, and customer tier."] });
        }

        SupportSlaPolicy policy;
        if (request.Id is Guid policyId)
        {
            policy = await _dbContext.SupportSlaPolicies.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == policyId, cancellationToken)
                ?? throw new KeyNotFoundException("SLA policy was not found.");
            policy.Update(request.Name, category, priority, request.FirstResponseMinutes, request.ResolutionMinutes, tier, request.IsActive, request.TimeBasis, request.RiskThresholdMinutes, recipientRole);
        }
        else
        {
            policy = new SupportSlaPolicy(Guid.NewGuid(), companyId, request.Name, category, priority, request.FirstResponseMinutes, request.ResolutionMinutes, tier, request.IsActive, request.TimeBasis, request.RiskThresholdMinutes, recipientRole);
            _dbContext.SupportSlaPolicies.Add(policy);
        }

        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.sla_policy.saved", "support_sla_policy", policy.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Support SLA policy saved.", ["support", "settings"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    public async Task<SupportSlaPolicyDto?> DeactivateAsync(Guid companyId, Guid userId, Guid policyId, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.SupportSlaPolicies.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == policyId, cancellationToken);
        if (policy is null) return null;
        policy.Deactivate();
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.sla_policy.deactivated", "support_sla_policy", policy.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Support SLA policy deactivated.", ["support", "settings"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    public async Task<SupportSlaResolutionDto> ResolveAsync(Guid companyId, string category, string priority, string? customerTier, DateTime startUtc, CancellationToken cancellationToken)
    {
        category = SupportCaseCategories.Normalize(category);
        priority = SupportPriorities.Normalize(priority);
        var tier = string.IsNullOrWhiteSpace(customerTier) ? null : customerTier.Trim();
        var policies = await _dbContext.SupportSlaPolicies.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive && x.Category == category && x.Priority == priority)
            .ToListAsync(cancellationToken);
        var selected = policies
            .OrderByDescending(x => tier is not null && x.CustomerTier == tier)
            .ThenByDescending(x => x.CustomerTier == null)
            .FirstOrDefault(x => x.CustomerTier == null || x.CustomerTier == tier);
        var defaults = DefaultDurations(priority);
        var first = selected?.FirstResponseMinutes ?? defaults.First;
        var resolution = selected?.ResolutionMinutes ?? defaults.Resolution;
        var start = startUtc.Kind == DateTimeKind.Utc ? startUtc : startUtc.ToUniversalTime();
        var useBusinessTime = string.Equals(selected?.TimeBasis, "business", StringComparison.OrdinalIgnoreCase);
        var calendar = useBusinessTime ? await GetCalendarAsync(companyId, cancellationToken) : null;
        var firstDue = calendar is null ? start.AddMinutes(first) : AddBusinessMinutes(start, first, calendar);
        var resolutionDue = calendar is null ? start.AddMinutes(resolution) : AddBusinessMinutes(start, resolution, calendar);
        return new SupportSlaResolutionDto(
            selected?.Id,
            selected?.Name ?? "Default support SLA",
            first,
            resolution,
            firstDue,
            resolutionDue,
            selected is null ? "No matching company policy was found, so the documented default target applies." : useBusinessTime ? "Matched the company policy and calculated deadlines in configured working time." : "Matched category, priority, and customer tier using the company SLA policy.",
            selected?.RiskThresholdMinutes ?? Math.Min(240, Math.Max(15, resolution / 4)),
            selected?.EscalationRecipientRole ?? "support_supervisor");
    }

    public async Task<SupportBusinessCalendarDto> GetCalendarAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies.AsNoTracking().SingleAsync(x => x.Id == companyId, cancellationToken);
        var fallback = new SupportBusinessCalendarDto(
            string.IsNullOrWhiteSpace(company.Timezone) ? TimeZoneInfo.Utc.Id : company.Timezone,
            new TimeOnly(8, 0),
            new TimeOnly(17, 0),
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            []);
        if (!company.Settings.Extensions.TryGetValue("supportBusinessCalendar", out var value) || value is not JsonObject calendar)
        {
            return fallback;
        }

        var zone = calendar["timeZoneId"]?.GetValue<string>() ?? fallback.TimeZoneId;
        var start = TimeOnly.TryParse(calendar["workdayStart"]?.GetValue<string>(), out var parsedStart) ? parsedStart : fallback.WorkdayStart;
        var end = TimeOnly.TryParse(calendar["workdayEnd"]?.GetValue<string>(), out var parsedEnd) ? parsedEnd : fallback.WorkdayEnd;
        var days = calendar["workingDays"] is JsonArray dayArray
            ? dayArray.Select(x => x?.GetValue<int>()).Where(x => x.HasValue && Enum.IsDefined((DayOfWeek)x.Value)).Select(x => (DayOfWeek)x!.Value).Distinct().ToList()
            : fallback.WorkingDays.ToList();
        var holidays = calendar["holidays"] is JsonArray holidayArray
            ? holidayArray.Select(x => DateOnly.TryParse(x?.GetValue<string>(), out var date) ? date : (DateOnly?)null).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList()
            : [];
        return new SupportBusinessCalendarDto(zone, start, end, days.Count == 0 ? fallback.WorkingDays : days, holidays);
    }

    public async Task<SupportBusinessCalendarDto> SaveCalendarAsync(Guid companyId, Guid userId, SaveSupportBusinessCalendarRequest request, CancellationToken cancellationToken)
    {
        _ = ResolveTimeZone(request.TimeZoneId);
        if (request.WorkdayEnd <= request.WorkdayStart || request.WorkingDays.Count == 0)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.WorkdayEnd)] = ["Working hours need at least one day and an end time after the start time."] });
        }

        var company = await _dbContext.Companies.SingleAsync(x => x.Id == companyId, cancellationToken);
        company.Settings.Extensions["supportBusinessCalendar"] = new JsonObject
        {
            ["timeZoneId"] = request.TimeZoneId,
            ["workdayStart"] = request.WorkdayStart.ToString("HH:mm"),
            ["workdayEnd"] = request.WorkdayEnd.ToString("HH:mm"),
            ["workingDays"] = new JsonArray(request.WorkingDays.Distinct().Select(x => (JsonNode?)JsonValue.Create((int)x)).ToArray()),
            ["holidays"] = new JsonArray(request.Holidays.Distinct().OrderBy(x => x).Select(x => (JsonNode?)JsonValue.Create(x.ToString("yyyy-MM-dd"))).ToArray())
        };
        company.UpdateBrandingAndSettings(company.Branding, company.Settings);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.business_calendar.saved", "company", companyId.ToString("D"), AuditEventOutcomes.Succeeded, "Support working calendar saved.", ["support", "settings"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCalendarAsync(companyId, cancellationToken);
    }

    private static DateTime AddBusinessMinutes(DateTime startUtc, int minutes, SupportBusinessCalendarDto calendar)
    {
        var zone = ResolveTimeZone(calendar.TimeZoneId);
        var cursor = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), zone);
        var remaining = minutes;
        var holidays = calendar.Holidays.ToHashSet();
        for (var guard = 0; guard < 3660 && remaining > 0; guard++)
        {
            var date = DateOnly.FromDateTime(cursor);
            if (calendar.WorkingDays.Contains(cursor.DayOfWeek) && !holidays.Contains(date))
            {
                var dayStart = date.ToDateTime(calendar.WorkdayStart, DateTimeKind.Unspecified);
                var dayEnd = date.ToDateTime(calendar.WorkdayEnd, DateTimeKind.Unspecified);
                var effective = cursor < dayStart ? dayStart : cursor;
                if (effective < dayEnd)
                {
                    var available = (int)Math.Floor((dayEnd - effective).TotalMinutes);
                    var consumed = Math.Min(remaining, available);
                    effective = effective.AddMinutes(consumed);
                    remaining -= consumed;
                    cursor = effective;
                    if (remaining == 0) return TimeZoneInfo.ConvertTimeToUtc(AdjustInvalidLocalTime(cursor, zone), zone);
                }
            }

            cursor = date.AddDays(1).ToDateTime(calendar.WorkdayStart, DateTimeKind.Unspecified);
        }

        throw new InvalidOperationException("The support working calendar could not produce a deadline within ten years.");
    }

    private static DateTime AdjustInvalidLocalTime(DateTime value, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(value)) value = value.AddMinutes(30);
        return value;
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new SupportValidationException(new Dictionary<string, string[]> { ["timeZoneId"] = ["Choose a valid timezone."] }); }
        catch (InvalidTimeZoneException) { throw new SupportValidationException(new Dictionary<string, string[]> { ["timeZoneId"] = ["Choose a valid timezone."] }); }
    }

    private static (int First, int Resolution) DefaultDurations(string priority) => priority switch
    {
        SupportPriorities.Urgent => (60, 480),
        SupportPriorities.High => (120, 1440),
        SupportPriorities.Low => (1440, 10080),
        _ => (480, 4320)
    };

    private static SupportSlaPolicyDto Map(SupportSlaPolicy policy) =>
        new(policy.Id, policy.Name, policy.Category, SupportLabels.Category(policy.Category), policy.Priority, SupportLabels.Priority(policy.Priority), policy.CustomerTier, policy.FirstResponseMinutes, policy.ResolutionMinutes, policy.IsActive, policy.UpdatedUtc, policy.TimeBasis, policy.RiskThresholdMinutes, policy.EscalationRecipientRole);
}

public sealed class SupportSlaMonitor : ISupportSlaMonitor
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ILogger<SupportSlaMonitor> _logger;
    private readonly ISupportSlaPolicyService? _slaPolicies;
    private readonly ICompanyOutboxEnqueuer? _outbox;

    public SupportSlaMonitor(VirtualCompanyDbContext dbContext, ILogger<SupportSlaMonitor> logger, ISupportSlaPolicyService? slaPolicies = null, ICompanyOutboxEnqueuer? outbox = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _slaPolicies = slaPolicies;
        _outbox = outbox;
    }

    public async Task<SupportSlaMonitorResult> RunAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cases = await _dbContext.SupportCases.IgnoreQueryFilters().Include(x => x.Events)
            .Where(x => x.Status != SupportCaseStatuses.Closed && x.Status != SupportCaseStatuses.Resolved)
            .ToListAsync(cancellationToken);
        var risks = 0;
        var breaches = 0;
        var notifications = 0;
        foreach (var supportCase in cases)
        {
            var riskThresholdMinutes = 240;
            var recipientRole = "support_supervisor";
            SupportSlaResolutionDto? appliedPolicy = null;
            if (_slaPolicies is not null)
            {
                appliedPolicy = await _slaPolicies.ResolveAsync(supportCase.CompanyId, supportCase.Category, supportCase.Priority, null, supportCase.CreatedUtc, cancellationToken);
                riskThresholdMinutes = appliedPolicy.RiskThresholdMinutes;
                recipientRole = appliedPolicy.EscalationRecipientRole;
            }
            if (supportCase.FirstResponseDueUtc is null || supportCase.ResolutionDueUtc is null)
            {
                if (appliedPolicy is not null)
                {
                    supportCase.SetSla(appliedPolicy.FirstResponseDueUtc, appliedPolicy.ResolutionDueUtc);
                }
                else
                {
                    var first = supportCase.CreatedUtc.AddHours(supportCase.Priority == SupportPriorities.High ? 2 : 8);
                    var resolution = supportCase.CreatedUtc.AddHours(supportCase.Priority == SupportPriorities.High ? 24 : 72);
                    supportCase.SetSla(first, resolution);
                }
            }

            var previousRisk = supportCase.IsSlaRisk;
            var previousBreach = supportCase.IsSlaBreached;
            var breached = (supportCase.FirstResponseSentUtc is null && supportCase.FirstResponseDueUtc < nowUtc) || supportCase.ResolutionDueUtc < nowUtc;
            var risk = !breached && supportCase.ResolutionDueUtc <= nowUtc.AddMinutes(riskThresholdMinutes);
            if (breached && !supportCase.IsSlaBreached)
            {
                breaches++;
                supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), supportCase.CompanyId, supportCase.Id, SupportCaseEventTypes.SlaBreached, "Support SLA breached.", AuditActorTypes.System, null, nowUtc));
            }
            else if (risk && !supportCase.IsSlaRisk)
            {
                risks++;
                supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), supportCase.CompanyId, supportCase.Id, SupportCaseEventTypes.SlaRisk, "Support SLA is at risk.", AuditActorTypes.System, null, nowUtc));
            }
            if (_outbox is not null && (breached != previousBreach || risk != previousRisk))
            {
                var transition = breached ? "breached" : risk ? "risk" : "recovered";
                var priority = breached ? CompanyNotificationPriority.Critical : CompanyNotificationPriority.High;
                var title = breached ? $"Support case {supportCase.CaseNumber} breached its target" : risk ? $"Support case {supportCase.CaseNumber} is at risk" : $"Support case {supportCase.CaseNumber} SLA recovered";
                var dueVersion = supportCase.ResolutionDueUtc?.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
                var dedupe = $"support-sla:{supportCase.Id:N}:{transition}:{dueVersion}";
                _outbox.Enqueue(supportCase.CompanyId, CompanyOutboxTopics.NotificationDeliveryRequested,
                    new NotificationDeliveryRequestedMessage(supportCase.CompanyId, CompanyNotificationType.Escalation.ToStorageValue(), priority.ToStorageValue(), title, $"Open {supportCase.CaseNumber} to review its response and resolution target.", "support_case", supportCase.Id, $"/support/cases/{supportCase.Id:D}", supportCase.AssignedUserId, supportCase.AssignedUserId.HasValue ? null : recipientRole, null, null, dedupe, null),
                    idempotencyKey: $"notification:{dedupe}", causationId: supportCase.Id.ToString("N"));
                notifications++;
            }
            supportCase.MarkSlaState(risk, breached);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Support SLA monitor scanned {Count} cases, created {Risks} risks and {Breaches} breaches.", cases.Count, risks, breaches);
        return new SupportSlaMonitorResult(cases.Count, risks, breaches, notifications);
    }
}

public sealed class SupportKnowledgeGapService : ISupportKnowledgeGapService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportKnowledgeGapService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<SupportKnowledgeGapDto> CreateOrIncrementAsync(Guid companyId, CreateSupportKnowledgeGapRequest request, CancellationToken cancellationToken)
    {
        var existing = await _dbContext.SupportKnowledgeGaps.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Status == SupportKnowledgeGapStatuses.Open && x.Category == request.Category && x.QuestionSummary == request.QuestionSummary, cancellationToken);
        if (existing is not null)
        {
            existing.Increment();
            await EnsureDocumentationTaskAsync(companyId, Guid.Empty, existing, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return SupportCaseService.MapGap(existing);
        }

        var gap = new SupportKnowledgeGap(Guid.NewGuid(), companyId, request.SupportCaseId, request.SupportReplyDraftId, request.Category, request.QuestionSummary, request.MissingInformationSummary, request.RetrievalSourceSummary);
        _dbContext.SupportKnowledgeGaps.Add(gap);
        await EnsureDocumentationTaskAsync(companyId, Guid.Empty, gap, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapGap(gap);
    }

    public async Task<IReadOnlyList<SupportKnowledgeGapDto>> ListAsync(Guid companyId, string? status, CancellationToken cancellationToken) =>
        await _dbContext.SupportKnowledgeGaps.AsNoTracking()
            .Where(x => x.CompanyId == companyId && (string.IsNullOrWhiteSpace(status) || x.Status == status))
            .OrderByDescending(x => x.FrequencyCount)
            .ThenByDescending(x => x.UpdatedUtc)
            .Select(x => SupportCaseService.MapGap(x))
            .ToListAsync(cancellationToken);

    public async Task<SupportKnowledgeGapDto?> CreateDocumentationTaskAsync(Guid companyId, Guid userId, Guid knowledgeGapId, CancellationToken cancellationToken)
    {
        var gap = await _dbContext.SupportKnowledgeGaps.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == knowledgeGapId, cancellationToken);
        if (gap is null) return null;
        await EnsureDocumentationTaskAsync(companyId, userId, gap, cancellationToken, force: true);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapGap(gap);
    }

    public async Task<SupportKnowledgeGapDto?> ResolveAsync(Guid companyId, Guid userId, Guid knowledgeGapId, ResolveSupportKnowledgeGapRequest request, CancellationToken cancellationToken)
    {
        var gap = await _dbContext.SupportKnowledgeGaps.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == knowledgeGapId, cancellationToken);
        if (gap is null) return null;
        var approvedKnowledge = await _dbContext.CompanyKnowledgeDocuments.AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.Id == request.KnowledgeDocumentId &&
            x.IngestionStatus == CompanyKnowledgeDocumentIngestionStatus.Processed &&
            x.IndexingStatus == CompanyKnowledgeDocumentIndexingStatus.Indexed, cancellationToken);
        if (!approvedKnowledge) throw new InvalidOperationException("Select a processed and indexed knowledge document from this company before resolving the gap.");
        gap.Resolve(request.KnowledgeDocumentId);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.knowledge_gap.resolved", "support_knowledge_gap", gap.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Knowledge gap resolved with approved knowledge.", ["support", "knowledge"], Metadata: new Dictionary<string, string?> { ["knowledgeDocumentId"] = request.KnowledgeDocumentId.ToString("D") }), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapGap(gap);
    }

    public async Task<SupportKnowledgeGapDto?> ReopenAsync(Guid companyId, Guid userId, Guid knowledgeGapId, CancellationToken cancellationToken)
    {
        var gap = await _dbContext.SupportKnowledgeGaps.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == knowledgeGapId, cancellationToken);
        if (gap is null) return null;
        gap.Reopen();
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.knowledge_gap.reopened", "support_knowledge_gap", gap.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Knowledge gap reopened for further documentation.", ["support", "knowledge"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return SupportCaseService.MapGap(gap);
    }

    private async Task EnsureDocumentationTaskAsync(Guid companyId, Guid userId, SupportKnowledgeGap gap, CancellationToken cancellationToken, bool force = false)
    {
        if (gap.LinkedTaskId is not null || (!force && gap.FrequencyCount < 3))
        {
            return;
        }

        var actorType = userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human;
        Guid? actorId = userId == Guid.Empty ? null : userId;
        var task = new WorkTask(
            Guid.NewGuid(),
            companyId,
            "support_knowledge_gap",
            $"Document support answer: {gap.QuestionSummary}",
            gap.MissingInformationSummary,
            gap.FrequencyCount >= 5 ? WorkTaskPriority.High : WorkTaskPriority.Normal,
            null,
            null,
            actorType,
            actorId,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["knowledgeGapId"] = gap.Id.ToString("D"),
                ["supportCaseId"] = gap.SupportCaseId?.ToString("D"),
                ["supportReplyDraftId"] = gap.SupportReplyDraftId?.ToString("D"),
                ["category"] = gap.Category,
                ["frequencyCount"] = gap.FrequencyCount
            },
            sourceType: WorkTaskSourceTypes.Agent,
            triggerSource: "support_knowledge_gap",
            creationReason: "Repeated support outcomes exposed missing answer knowledge.");
        _dbContext.WorkTasks.Add(task);
        gap.LinkTask(task.Id);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, actorType, actorId, "support.knowledge_gap.task_created", "support_knowledge_gap", gap.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Documentation task created from support knowledge gap.", ["support", "knowledge"], Metadata: new Dictionary<string, string?> { ["taskId"] = task.Id.ToString("D"), ["frequencyCount"] = gap.FrequencyCount.ToString(System.Globalization.CultureInfo.InvariantCulture) }), cancellationToken);
    }
}

public sealed class SupportAnalyticsService : ISupportAnalyticsService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public SupportAnalyticsService(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

    public async Task<SupportAnalyticsDashboardResponse> GetDashboardAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var summary = await new SupportCaseService(_dbContext, new NoopAuditEventWriter()).ListCasesAsync(companyId, new SupportCaseListQuery(Take: 1), cancellationToken);
        var byStatus = await BucketAsync(companyId, x => x.Status, cancellationToken);
        var byCategory = await BucketAsync(companyId, x => x.Category, cancellationToken);
        var byPriority = await BucketAsync(companyId, x => x.Priority, cancellationToken);
        var sla = await BuildSlaPerformanceAsync(companyId, cancellationToken);
        var learning = await BuildLearningEffectivenessAsync(companyId, cancellationToken);
        var insights = byCategory.Where(x => x.Count >= 3).Select(x => new SupportRootCauseInsight($"Recurring {x.Label.ToLowerInvariant()} cases", $"{x.Count} support cases share this category.", x.Key, x.Count, "Review related support knowledge and workflow steps.")).ToList();
        return new SupportAnalyticsDashboardResponse(summary.Summary, byStatus, byCategory, byPriority, sla, learning, insights);
    }

    private async Task<SupportSlaPerformanceSummary> BuildSlaPerformanceAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SupportCases.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Status,
                x.IsSlaRisk,
                x.IsSlaBreached,
                x.FirstResponseDueUtc,
                x.FirstResponseSentUtc,
                x.ResolutionDueUtc,
                x.ResolvedUtc
            })
            .ToListAsync(cancellationToken);
        var open = rows.Where(x => x.Status != SupportCaseStatuses.Resolved && x.Status != SupportCaseStatuses.Closed).ToList();
        var responded = rows.Where(x => x.FirstResponseDueUtc.HasValue && x.FirstResponseSentUtc.HasValue).ToList();
        var resolved = rows.Where(x => x.ResolutionDueUtc.HasValue && x.ResolvedUtc.HasValue).ToList();
        var missingTargets = rows.Count(x => !x.FirstResponseDueUtc.HasValue || !x.ResolutionDueUtc.HasValue);
        return new SupportSlaPerformanceSummary(
            open.Count(x => x.IsSlaRisk),
            open.Count(x => x.IsSlaBreached),
            responded.Count(x => x.FirstResponseSentUtc <= x.FirstResponseDueUtc),
            responded.Count(x => x.FirstResponseSentUtc > x.FirstResponseDueUtc),
            resolved.Count(x => x.ResolvedUtc <= x.ResolutionDueUtc),
            resolved.Count(x => x.ResolvedUtc > x.ResolutionDueUtc),
            missingTargets,
            missingTargets == 0
                ? "SLA reporting uses the targets stored on each support case."
                : "Some historical cases do not have stored SLA targets, so they are labeled as missing instead of counted as met or missed.");
    }

    private async Task<SupportLearningEffectivenessSummary> BuildLearningEffectivenessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var observations = await _dbContext.SupportMemoryObservations.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .GroupBy(x => x.Status)
            .Select(x => new { Status = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);
        var drafts = await _dbContext.SupportReplyDrafts.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .Select(x => new
            {
                x.Status,
                x.Answerability,
                x.SourceReferencesJson,
                x.SentUtc
            })
            .ToListAsync(cancellationToken);
        var withMemory = drafts.Where(x => !string.IsNullOrWhiteSpace(x.SourceReferencesJson) && x.SourceReferencesJson.Contains("customer_memory", StringComparison.OrdinalIgnoreCase)).ToList();
        var withoutMemory = drafts.Except(withMemory).ToList();
        var reopened = await _dbContext.SupportCases.AsNoTracking().CountAsync(x => x.CompanyId == companyId && x.Status == SupportCaseStatuses.Reopened, cancellationToken);
        return new SupportLearningEffectivenessSummary(
            observations.FirstOrDefault(x => x.Status == SupportMemoryObservationStatuses.Approved)?.Count ?? 0,
            observations.FirstOrDefault(x => x.Status == SupportMemoryObservationStatuses.Review)?.Count ?? 0,
            observations.FirstOrDefault(x => x.Status == SupportMemoryObservationStatuses.Rejected)?.Count ?? 0,
            withMemory.Count,
            withMemory.Count == 0 ? null : decimal.Round(withMemory.Average(x => x.Answerability), 3, MidpointRounding.AwayFromZero),
            withoutMemory.Count == 0 ? null : decimal.Round(withoutMemory.Average(x => x.Answerability), 3, MidpointRounding.AwayFromZero),
            drafts.Count(x => x.Status == SupportReplyDraftStatuses.Approved),
            drafts.Count(x => x.Status == SupportReplyDraftStatuses.Rejected),
            drafts.Count(x => x.SentUtc.HasValue),
            reopened,
            "Learning metrics use draft metadata and governed memory observations only; they show association, not guaranteed causation.");
    }

    private async Task<IReadOnlyList<SupportMetricBucket>> BucketAsync(Guid companyId, System.Linq.Expressions.Expression<Func<SupportCase, string>> selector, CancellationToken cancellationToken)
    {
        var rows = await _dbContext.SupportCases.AsNoTracking().Where(x => x.CompanyId == companyId)
            .GroupBy(selector)
            .Select(x => new { Key = x.Key, Count = x.Count() })
            .ToListAsync(cancellationToken);
        return rows.Select(x => new SupportMetricBucket(x.Key, SupportLabels.Status(x.Key) == x.Key ? SupportLabels.Category(x.Key) : SupportLabels.Status(x.Key), x.Count)).ToList();
    }
}

public sealed class SupportMemoryUpdateService : ISupportMemoryUpdateService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportMemoryUpdateService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit) { _dbContext = dbContext; _audit = audit; }

    public async Task UpdateFromResolvedCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var job = await _dbContext.SupportMemoryUpdateJobs.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.SupportCaseId == supportCaseId, cancellationToken);
        if (job is not null) await ProcessJobAsync(companyId, job.Id, cancellationToken);
    }

    public async Task ProcessJobAsync(Guid companyId, Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _dbContext.SupportMemoryUpdateJobs.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == jobId, cancellationToken)
            ?? throw new KeyNotFoundException("Support memory update job was not found.");
        if (job.Status is "completed" or "skipped") return;
        job.Start();
        try
        {
            var supportCase = await _dbContext.SupportCases.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == job.SupportCaseId && x.Status == SupportCaseStatuses.Resolved, cancellationToken);
            var resolution = await _dbContext.SupportCaseResolutions.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.SupportCaseId == job.SupportCaseId, cancellationToken);
            if (supportCase?.ContactId is not Guid contactId || resolution is null || string.IsNullOrWhiteSpace(resolution.CustomerPreferenceObservations))
            {
                job.Complete(skipped: true);
                await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null, "support.memory.skipped", "support_case", job.SupportCaseId.ToString("D"), AuditEventOutcomes.Succeeded, "No eligible explicit customer preference was available for memory.", ["support", "memory"]), cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            var candidate = resolution.CustomerPreferenceObservations.Trim();
            var existingObservation = await _dbContext.SupportMemoryObservations.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.SourceEventKey == job.EventKey && x.ContactId == contactId, cancellationToken);
            if (existingObservation is { Status: SupportMemoryObservationStatuses.Approved or SupportMemoryObservationStatuses.Rejected or SupportMemoryObservationStatuses.Deleted })
            {
                job.Complete(skipped: true);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var decision = SupportMemorySafetyPolicy.Evaluate(candidate);
            if (decision.Status == SupportMemoryObservationStatuses.Rejected)
            {
                if (existingObservation is null)
                {
                    _dbContext.SupportMemoryObservations.Add(new SupportMemoryObservation(Guid.NewGuid(), companyId, supportCase.Id, resolution.Id, contactId, SupportMemoryObservationStatuses.Rejected, null, decision.EvidenceSummary, 0m, resolution.ResolvedUtc, null, SupportMemorySafetyPolicy.PolicyVersion, job.EventKey));
                }
                else
                {
                    existingObservation.Reject();
                }
                job.Complete(skipped: true);
                await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null, "support.memory.rejected", "support_case", job.SupportCaseId.ToString("D"), AuditEventOutcomes.Succeeded, "A support memory candidate was rejected by policy.", ["support", "memory", "privacy"]), cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return;
            }
            var profile = await _dbContext.CustomerMemoryProfiles.Include(x => x.Preferences).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ContactId == contactId, cancellationToken);
            if (profile is null) { job.Complete(skipped: true); await _dbContext.SaveChangesAsync(cancellationToken); return; }
            var source = $"Support case {supportCase.CaseNumber}; {job.EventKey}";
            var duplicate = profile.Preferences.FirstOrDefault(x => x.PreferenceKey == "support_preference" && (x.PreferenceValue == candidate || x.SourceSummary == source));
            var contradictory = profile.Preferences.Any(x => x.PreferenceKey == "support_preference" && x.PreferenceValue != candidate);
            if (existingObservation is null)
            {
                var status = decision.Status == SupportMemoryObservationStatuses.Approved && !contradictory
                    ? SupportMemoryObservationStatuses.Approved
                    : SupportMemoryObservationStatuses.Review;
                existingObservation = new SupportMemoryObservation(Guid.NewGuid(), companyId, supportCase.Id, resolution.Id, contactId, status, candidate, contradictory ? "A different support preference already exists and needs review." : decision.EvidenceSummary, decision.Confidence, resolution.ResolvedUtc, decision.ValidUntilUtc, SupportMemorySafetyPolicy.PolicyVersion, job.EventKey);
                _dbContext.SupportMemoryObservations.Add(existingObservation);
            }
            if (duplicate is not null)
            {
                existingObservation.Approve(duplicate.Id);
            }
            else if (existingObservation.Status == SupportMemoryObservationStatuses.Approved)
            {
                var preference = new CustomerMemoryProfilePreference(Guid.NewGuid(), companyId, profile.Id, "support_preference", candidate, source, decision.Confidence, resolution.ResolvedUtc);
                _dbContext.CustomerMemoryProfilePreferences.Add(preference);
                existingObservation.Approve(preference.Id);
            }
            else
            {
                existingObservation.MarkReviewRequired();
            }
            job.Complete();
            var summary = existingObservation.Status == SupportMemoryObservationStatuses.Approved
                ? "An explicit support preference was added to customer memory."
                : "A support memory candidate was queued for review.";
            await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null, "support.memory.processed", "support_case", job.SupportCaseId.ToString("D"), AuditEventOutcomes.Succeeded, summary, ["support", "memory"]), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            job.Fail(ex is DbUpdateException ? "Memory persistence failed and will be retried." : "Memory processing failed and will be retried.");
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}

public sealed class SupportMemoryReviewService : ISupportMemoryReviewService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportMemoryReviewService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SupportMemoryObservationDto>> ListAsync(Guid companyId, Guid? contactId, string? status, CancellationToken cancellationToken)
    {
        var query = _dbContext.SupportMemoryObservations.AsNoTracking().Where(x => x.CompanyId == companyId);
        if (contactId is Guid id) query = query.Where(x => x.ContactId == id);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == SupportMemoryObservationStatuses.Normalize(status));
        return await query.OrderByDescending(x => x.UpdatedUtc).Take(200).Select(x => MapMemoryObservation(x)).ToListAsync(cancellationToken);
    }

    public Task<SupportMemoryObservationDto?> ApproveAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(companyId, userId, observationId, "support.memory.approved", request.Note ?? "Support memory approved.", async observation =>
        {
            if (observation.Status is SupportMemoryObservationStatuses.Deleted or SupportMemoryObservationStatuses.Rejected)
            {
                throw new SupportValidationException(new Dictionary<string, string[]> { ["status"] = ["This memory observation cannot be approved."] });
            }

            if (string.IsNullOrWhiteSpace(observation.Value))
            {
                throw new SupportValidationException(new Dictionary<string, string[]> { ["value"] = ["There is no safe value to approve."] });
            }

            var profile = await _dbContext.CustomerMemoryProfiles.Include(x => x.Preferences).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ContactId == observation.ContactId, cancellationToken);
            if (profile is null)
            {
                profile = new CustomerMemoryProfile(Guid.NewGuid(), companyId, observation.ContactId);
                _dbContext.CustomerMemoryProfiles.Add(profile);
            }

            var source = $"Support memory observation {observation.Id:D}";
            var preference = profile.Preferences.FirstOrDefault(x => x.PreferenceKey == "support_preference" && x.PreferenceValue == observation.Value)
                ?? new CustomerMemoryProfilePreference(Guid.NewGuid(), companyId, profile.Id, "support_preference", observation.Value, source, observation.Confidence, observation.ObservedUtc);
            if (preference.Id != Guid.Empty && !profile.Preferences.Any(x => x.Id == preference.Id))
            {
                _dbContext.CustomerMemoryProfilePreferences.Add(preference);
            }

            observation.Approve(preference.Id);
        }, cancellationToken);

    public Task<SupportMemoryObservationDto?> RejectAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(companyId, userId, observationId, "support.memory.rejected", request.Note ?? "Support memory rejected.", observation => { observation.Reject(); return Task.CompletedTask; }, cancellationToken);

    public Task<SupportMemoryObservationDto?> ExpireAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(companyId, userId, observationId, "support.memory.expired", request.Note ?? "Support memory expired.", async observation => { await RemoveLinkedPreferenceAsync(companyId, observation, cancellationToken); observation.Expire(); }, cancellationToken);

    public Task<SupportMemoryObservationDto?> DeleteAsync(Guid companyId, Guid userId, Guid observationId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateAsync(companyId, userId, observationId, "support.memory.deleted", request.Note ?? "Support memory deleted.", async observation => { await RemoveLinkedPreferenceAsync(companyId, observation, cancellationToken); observation.Delete(); }, cancellationToken);

    private async Task<SupportMemoryObservationDto?> MutateAsync(Guid companyId, Guid userId, Guid observationId, string action, string summary, Func<SupportMemoryObservation, Task> mutation, CancellationToken cancellationToken)
    {
        var observation = await _dbContext.SupportMemoryObservations.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == observationId, cancellationToken);
        if (observation is null) return null;
        await mutation(observation);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, action, "support_memory_observation", observation.Id.ToString("D"), AuditEventOutcomes.Succeeded, summary, ["support", "memory"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapMemoryObservation(observation);
    }

    private async Task RemoveLinkedPreferenceAsync(Guid companyId, SupportMemoryObservation observation, CancellationToken cancellationToken)
    {
        if (observation.CustomerMemoryProfilePreferenceId is not Guid preferenceId)
        {
            return;
        }

        var preference = await _dbContext.CustomerMemoryProfilePreferences.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == preferenceId, cancellationToken);
        if (preference is not null)
        {
            _dbContext.CustomerMemoryProfilePreferences.Remove(preference);
        }
    }

    private static SupportMemoryObservationDto MapMemoryObservation(SupportMemoryObservation observation) =>
        new(
            observation.Id,
            observation.SupportCaseId,
            observation.SupportCaseResolutionId,
            observation.ContactId,
            observation.CustomerMemoryProfilePreferenceId,
            observation.Status,
            SupportLabels.Event(observation.Status),
            observation.Status is SupportMemoryObservationStatuses.Rejected or SupportMemoryObservationStatuses.Deleted ? null : observation.Value,
            observation.EvidenceSummary,
            observation.Confidence,
            observation.ObservedUtc,
            observation.ValidUntilUtc,
            observation.PolicyVersion,
            observation.SourceEventKey,
            observation.UpdatedUtc,
            observation.Status switch
            {
                SupportMemoryObservationStatuses.Review => ["approve", "reject"],
                SupportMemoryObservationStatuses.Approved => ["expire", "delete"],
                SupportMemoryObservationStatuses.Expired => ["delete"],
                _ => []
            });
}

internal static class SupportMemorySafetyPolicy
{
    private static readonly string[] BlockedTerms = ["password", "passcode", "secret", "api key", "token", "credit card", "card number", "cvv", "iban", "bank account", "social security", "personnummer"];
    private static readonly string[] ReviewTerms = ["maybe", "probably", "seems", "appears", "angry", "upset", "temporary", "for now", "until"];
    public const string PolicyVersion = "support-memory-v1";

    public static SupportMemoryPolicyDecision Evaluate(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000)
        {
            return new SupportMemoryPolicyDecision(SupportMemoryObservationStatuses.Rejected, "Candidate was blank or too long.", 0m, null);
        }

        var normalized = value.ToLowerInvariant();
        if (BlockedTerms.Any(normalized.Contains) || System.Text.RegularExpressions.Regex.IsMatch(value, @"\b(?:\d[ -]*?){13,19}\b"))
        {
            return new SupportMemoryPolicyDecision(SupportMemoryObservationStatuses.Rejected, "Candidate contained sensitive information blocked by policy.", 0m, null);
        }

        if (ReviewTerms.Any(normalized.Contains))
        {
            return new SupportMemoryPolicyDecision(SupportMemoryObservationStatuses.Review, "Candidate may be temporary or inferred and needs review.", 0.65m, DateTime.UtcNow.AddDays(90));
        }

        return new SupportMemoryPolicyDecision(SupportMemoryObservationStatuses.Approved, "Explicit support preference passed deterministic safety checks.", 0.85m, null);
    }

    public sealed record SupportMemoryPolicyDecision(string Status, string EvidenceSummary, decimal Confidence, DateTime? ValidUntilUtc);
}

file sealed class NoopAuditEventWriter : IAuditEventWriter
{
    public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}












