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
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
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
        if (!string.IsNullOrWhiteSpace(request.ConversationLanguage) && CommunicationLanguageResolver.Normalize(request.ConversationLanguage) is null)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.ConversationLanguage)] = ["Use a valid BCP 47 language tag, such as en-GB or sv-SE."] });
        }
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
            createdUtc: now,
            conversationLanguage: request.ConversationLanguage);

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
        _dbContext.SupportMessages.Add(new SupportMessage(Guid.NewGuid(), companyId, supportCase.Id, SupportMessageDirections.Internal, "internal_note", userId.ToString("D"), null, request.Body, now));
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.StatusChanged, "Internal note added.", AuditActorTypes.Human, userId, now));
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
        _dbContext.SupportCaseAssignments.Add(new SupportCaseAssignment(Guid.NewGuid(), companyId, supportCase.Id, request.AssignedAgentId, request.AssignedUserId, userId, DateTime.UtcNow, request.Reason));
        var summary = request.AssignedAgentId is null && request.AssignedUserId is null ? "Support case unassigned." : "Support case assigned.";
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Assigned, summary, AuditActorTypes.Human, userId, DateTime.UtcNow));
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
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Resolved, "Support case resolved.", AuditActorTypes.Human, userId, DateTime.UtcNow));
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
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Reopened, request.Note!, AuditActorTypes.Human, userId, DateTime.UtcNow));
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
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Closed, request.Note!, AuditActorTypes.Human, userId, DateTime.UtcNow));
        await AddAuditAsync(companyId, userId, "support.case.closed", supportCase.Id, AuditEventOutcomes.Succeeded, request.Note!, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    private async Task<SupportCaseDetailResponse?> MutateCaseAsync(Guid companyId, Guid userId, Guid supportCaseId, string auditAction, string eventType, string summary, Action<SupportCase> mutation, CancellationToken cancellationToken)
    {
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        mutation(supportCase);
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, eventType, summary, AuditActorTypes.Human, userId, DateTime.UtcNow));
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
        var companyLanguage = await _dbContext.Companies.IgnoreQueryFilters()
            .Where(x => x.Id == supportCase.CompanyId)
            .Select(x => x.Language)
            .SingleOrDefaultAsync(cancellationToken);
        var communicationLanguage = CommunicationLanguageResolver.Resolve(contact?.PreferredLanguage, supportCase.ConversationLanguage, null, companyLanguage);
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
            context,
            communicationLanguage.LanguageTag,
            communicationLanguage.Source,
            communicationLanguage.Confidence,
            communicationLanguage.RequiresHumanReview);
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
            supportCase.IsVipRisk,
            supportCase.ConversationLanguage);

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
