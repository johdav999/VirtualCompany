using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportCaseService : ISupportCaseService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportCaseService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<SupportCaseListResponse> ListCasesAsync(Guid companyId, SupportCaseListQuery query, CancellationToken cancellationToken)
    {
        var cases = ApplyFilters(_dbContext.SupportCases.AsNoTracking().Where(x => x.CompanyId == companyId), query);
        var total = await cases.CountAsync(cancellationToken);
        var items = await cases
            .OrderByDescending(x => x.IsSlaBreached)
            .ThenByDescending(x => x.IsSlaRisk)
            .ThenByDescending(x => x.UpdatedUtc)
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

    public Task<SupportCaseDetailResponse?> ChangeStatusAsync(Guid companyId, Guid userId, Guid supportCaseId, ChangeSupportStatusRequest request, CancellationToken cancellationToken) =>
        MutateCaseAsync(companyId, userId, supportCaseId, "support.case.status_changed", SupportCaseEventTypes.StatusChanged, $"Status changed to {SupportLabels.Status(request.Status)}.", c => c.SetStatus(request.Status), cancellationToken);

    public Task<SupportCaseDetailResponse?> ChangePriorityAsync(Guid companyId, Guid userId, Guid supportCaseId, ChangeSupportPriorityRequest request, CancellationToken cancellationToken) =>
        MutateCaseAsync(companyId, userId, supportCaseId, "support.case.priority_changed", SupportCaseEventTypes.PriorityChanged, $"Priority changed to {SupportLabels.Priority(request.Priority)}.", c => c.SetPriority(request.Priority), cancellationToken);

    public Task<SupportCaseDetailResponse?> ChangeCategoryAsync(Guid companyId, Guid userId, Guid supportCaseId, ChangeSupportCategoryRequest request, CancellationToken cancellationToken) =>
        MutateCaseAsync(companyId, userId, supportCaseId, "support.case.category_changed", SupportCaseEventTypes.Triaged, $"Category changed to {SupportLabels.Category(request.Category)}.", c => c.SetCategory(request.Category), cancellationToken);

    public async Task<SupportCaseDetailResponse?> AssignAsync(Guid companyId, Guid userId, Guid supportCaseId, AssignSupportCaseRequest request, CancellationToken cancellationToken)
    {
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        if (request.AssignedAgentId is null && request.AssignedUserId is null)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { ["assigned"] = ["Assign an agent or a user."] });
        }

        supportCase.Assign(request.AssignedAgentId, request.AssignedUserId);
        supportCase.Assignments.Add(new SupportCaseAssignment(Guid.NewGuid(), companyId, supportCase.Id, request.AssignedAgentId, request.AssignedUserId, userId, DateTime.UtcNow, request.Reason));
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Assigned, "Support case assigned.", AuditActorTypes.Human, userId, DateTime.UtcNow));
        await AddAuditAsync(companyId, userId, "support.case.assigned", supportCase.Id, AuditEventOutcomes.Succeeded, "Support case assigned.", cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    public async Task<SupportCaseDetailResponse?> ResolveAsync(Guid companyId, Guid userId, Guid supportCaseId, ResolveSupportCaseRequest request, CancellationToken cancellationToken)
    {
        SupportValidationException.ThrowIfBlank(request.Summary, nameof(request.Summary));
        var supportCase = await LoadCaseAsync(companyId, supportCaseId, cancellationToken);
        if (supportCase is null) return null;
        var resolution = new SupportCaseResolution(Guid.NewGuid(), companyId, supportCase.Id, request.Summary, string.IsNullOrWhiteSpace(request.Outcome) ? "Resolved" : request.Outcome, userId, DateTime.UtcNow);
        _dbContext.SupportCaseResolutions.Add(resolution);
        supportCase.SetStatus(SupportCaseStatuses.Resolved);
        supportCase.Events.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.Resolved, "Support case resolved.", AuditActorTypes.Human, userId, DateTime.UtcNow));
        await AddAuditAsync(companyId, userId, "support.case.resolved", supportCase.Id, AuditEventOutcomes.Succeeded, request.Summary, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCaseAsync(companyId, supportCase.Id, cancellationToken);
    }

    public Task<SupportCaseDetailResponse?> ReopenAsync(Guid companyId, Guid userId, Guid supportCaseId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateCaseAsync(companyId, userId, supportCaseId, "support.case.reopened", SupportCaseEventTypes.Reopened, request.Note ?? "Support case reopened.", c => c.SetStatus(SupportCaseStatuses.Reopened), cancellationToken);

    public Task<SupportCaseDetailResponse?> CloseAsync(Guid companyId, Guid userId, Guid supportCaseId, SupportActionRequest request, CancellationToken cancellationToken) =>
        MutateCaseAsync(companyId, userId, supportCaseId, "support.case.closed", SupportCaseEventTypes.Closed, request.Note ?? "Support case closed.", c => c.SetStatus(SupportCaseStatuses.Closed), cancellationToken);

    private async Task UpdateMemoryFromResolvedCaseAsync(Guid companyId, SupportCase supportCase, ResolveSupportCaseRequest request, CancellationToken cancellationToken)
    {
        if (supportCase.ContactId is not Guid contactId)
        {
            return;
        }

        var profile = await _dbContext.CustomerMemoryProfiles
            .Include(x => x.Preferences)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ContactId == contactId, cancellationToken);
        if (profile is null)
        {
            return;
        }

        var value = $"Resolved support case {supportCase.CaseNumber}: {request.Summary}. Outcome: {(string.IsNullOrWhiteSpace(request.Outcome) ? "Resolved" : request.Outcome)}";
        if (profile.Preferences.Any(x => x.PreferenceKey == "support_context" && x.PreferenceValue == value))
        {
            return;
        }

        _dbContext.CustomerMemoryProfilePreferences.Add(new CustomerMemoryProfilePreference(
            Guid.NewGuid(),
            companyId,
            profile.Id,
            "support_context",
            value,
            $"Support case {supportCase.CaseNumber}",
            0.85m,
            DateTime.UtcNow));
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.System, null, "support.memory.updated", "support_case", supportCase.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Customer support memory updated from resolved case outcome.", ["support", "memory"]), cancellationToken);
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
        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x => x.Subject.Contains(search) || x.CaseNumber.Contains(search) || x.Summary.Contains(search));
        }
        return query;
    }

    private async Task<SupportCase?> LoadCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken) =>
        await _dbContext.SupportCases
            .Include(x => x.Messages)
            .Include(x => x.Events)
            .Include(x => x.ReplyDrafts)
            .Include(x => x.RefundRequests)
            .Include(x => x.KnowledgeGaps)
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);

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
            supportCase.CreatedUtc,
            supportCase.UpdatedUtc,
            supportCase.Messages.OrderBy(x => x.OccurredUtc).Select(MapMessage).ToList(),
            supportCase.Events.OrderByDescending(x => x.OccurredUtc).Select(MapEvent).ToList(),
            supportCase.ReplyDrafts.OrderByDescending(x => x.CreatedUtc).Select(MapDraft).ToList(),
            supportCase.RefundRequests.OrderByDescending(x => x.CreatedUtc).Select(MapRefund).ToList(),
            supportCase.KnowledgeGaps.OrderByDescending(x => x.CreatedUtc).Select(MapGap).ToList(),
            context);
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
        new(draft.Id, draft.SupportCaseId, draft.DraftBody, draft.Tone, draft.Status, SupportLabels.DraftStatus(draft.Status), draft.Confidence, draft.Answerability, draft.RationaleSummary, draft.SourceReferencesJson, draft.CreatedByAgentId, draft.CreatedByUserId, draft.ApprovedByUserId, draft.ApprovedUtc, draft.SentUtc, draft.SendFailureSummary, draft.CreatedUtc, draft.UpdatedUtc);

    internal static SupportRefundRequestDto MapRefund(SupportRefundRequest refund) =>
        new(refund.Id, refund.SupportCaseId, refund.Amount, refund.Currency, refund.ReasonCode, refund.Explanation, refund.InvoiceId, refund.PaymentId, refund.ApprovalRequestId, refund.FinanceActionReferenceId, refund.Status, refund.CreatedUtc, refund.UpdatedUtc);

    internal static SupportKnowledgeGapDto MapGap(SupportKnowledgeGap gap) =>
        new(gap.Id, gap.SupportCaseId, gap.SupportReplyDraftId, gap.Category, SupportLabels.Category(gap.Category), gap.QuestionSummary, gap.MissingInformationSummary, gap.RetrievalSourceSummary, gap.FrequencyCount, gap.Status, SupportLabels.KnowledgeGapStatus(gap.Status), gap.CreatedUtc, gap.UpdatedUtc, gap.LinkedTaskId);

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

    public SupportMailboxRoutingService(VirtualCompanyDbContext dbContext, ISupportMailboxIngestionService ingestion)
    {
        _dbContext = dbContext;
        _ingestion = ingestion;
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
            var alreadyRouted = await _dbContext.SupportMessages.IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.CompanyId == snapshot.CompanyId && x.EmailMessageSnapshotId == snapshot.Id, cancellationToken);
            if (alreadyRouted)
            {
                duplicates++;
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
        }

        return new SupportMailboxRoutingResult(snapshots.Count, routed, created, duplicates);
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

    public SupportKnowledgeContextProvider(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

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

        var chunks = await _dbContext.CompanyKnowledgeChunks.AsNoTracking()
            .Include(x => x.Document)
            .Where(x => x.CompanyId == companyId && x.IsActive)
            .OrderByDescending(x => x.CreatedUtc)
            .Take(200)
            .ToListAsync(cancellationToken);
        sources.AddRange(chunks
            .Select(chunk => new { Chunk = chunk, Score = ScoreText(chunk.Content + " " + chunk.Document.Title, queryTerms) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(4)
            .Select(x => new SupportKnowledgeSourceReference("knowledge_chunk", x.Chunk.Document.Title, x.Chunk.Id, TrimForExcerpt(x.Chunk.Content), Math.Min(0.95m, 0.45m + x.Score / 10m))));

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

        var confidence = sources.Count <= 1 ? 0.45m : Math.Min(0.92m, sources.Average(x => x.Relevance));
        var rationale = sources.Count <= 1
            ? "No policy, memory, or similar-case knowledge was found beyond the support case itself."
            : "Retrieved support case context, customer memory, similar outcomes, and relevant knowledge snippets for grounded drafting.";
        return new SupportKnowledgeContext(supportCase.Id, sources, memories, similarCases, confidence, rationale);
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

    private static int ScoreText(string text, IReadOnlyCollection<string> terms)
    {
        if (terms.Count == 0) return 0;
        var lowered = text.ToLowerInvariant();
        return terms.Count(lowered.Contains);
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

    public SupportReplyDraftService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter audit,
        ISupportOutboundEmailSender outboundEmailSender,
        ISupportKnowledgeContextProvider knowledgeContextProvider,
        ISupportKnowledgeGapService knowledgeGaps)
    {
        _dbContext = dbContext;
        _audit = audit;
        _outboundEmailSender = outboundEmailSender;
        _knowledgeContextProvider = knowledgeContextProvider;
        _knowledgeGaps = knowledgeGaps;
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
        if (!context.HasGrounding) return 0.45m;
        if (supportCase.Category is SupportCaseCategories.Refund or SupportCaseCategories.Billing) return Math.Min(0.82m, context.RetrievalConfidence);
        return Math.Min(0.88m, Math.Max(0.72m, context.RetrievalConfidence));
    }

    private static string BuildGroundedDraftBody(SupportCase supportCase, SupportKnowledgeContext context, SupportMessage? lastInbound, decimal answerability)
    {
        var greeting = "Hello";
        var sourceLines = context.Sources
            .Where(x => !string.IsNullOrWhiteSpace(x.Excerpt) && x.Type != "support_case")
            .Take(3)
            .Select(x => $"- {x.Excerpt}")
            .ToList();
        var grounding = sourceLines.Count == 0
            ? "I need to verify the right policy or account details before giving a final answer."
            : "I checked the available support context:\n" + string.Join("\n", sourceLines);
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
                ["relevance"] = source.Relevance
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

public sealed class SupportSlaMonitor : ISupportSlaMonitor
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ILogger<SupportSlaMonitor> _logger;

    public SupportSlaMonitor(VirtualCompanyDbContext dbContext, ILogger<SupportSlaMonitor> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<SupportSlaMonitorResult> RunAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cases = await _dbContext.SupportCases.IgnoreQueryFilters().Include(x => x.Events)
            .Where(x => x.Status != SupportCaseStatuses.Closed && x.Status != SupportCaseStatuses.Resolved)
            .ToListAsync(cancellationToken);
        var risks = 0;
        var breaches = 0;
        foreach (var supportCase in cases)
        {
            if (supportCase.FirstResponseDueUtc is null || supportCase.ResolutionDueUtc is null)
            {
                var first = supportCase.CreatedUtc.AddHours(supportCase.Priority == SupportPriorities.High ? 2 : 8);
                var resolution = supportCase.CreatedUtc.AddHours(supportCase.Priority == SupportPriorities.High ? 24 : 72);
                supportCase.SetSla(first, resolution);
            }

            var breached = (supportCase.FirstResponseSentUtc is null && supportCase.FirstResponseDueUtc < nowUtc) || supportCase.ResolutionDueUtc < nowUtc;
            var risk = !breached && supportCase.ResolutionDueUtc <= nowUtc.AddHours(4);
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
            supportCase.MarkSlaState(risk, breached);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Support SLA monitor scanned {Count} cases, created {Risks} risks and {Breaches} breaches.", cases.Count, risks, breaches);
        return new SupportSlaMonitorResult(cases.Count, risks, breaches, risks + breaches);
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
        var insights = byCategory.Where(x => x.Count >= 3).Select(x => new SupportRootCauseInsight($"Recurring {x.Label.ToLowerInvariant()} cases", $"{x.Count} support cases share this category.", x.Key, x.Count, "Review related support knowledge and workflow steps.")).ToList();
        return new SupportAnalyticsDashboardResponse(summary.Summary, byStatus, byCategory, byPriority, insights);
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

    public SupportMemoryUpdateService(VirtualCompanyDbContext dbContext) => _dbContext = dbContext;

    public async Task UpdateFromResolvedCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId && x.Status == SupportCaseStatuses.Resolved, cancellationToken);
        if (supportCase?.ContactId is not Guid contactId) return;
        var profile = await _dbContext.CustomerMemoryProfiles.FirstOrDefaultAsync(x => x.CompanyId == companyId && x.ContactId == contactId, cancellationToken);
        if (profile is null) return;
        _dbContext.CustomerMemoryProfilePreferences.Add(new CustomerMemoryProfilePreference(
            Guid.NewGuid(),
            companyId,
            profile.Id,
            "support_context",
            $"Resolved support case {supportCase.CaseNumber}: {supportCase.Summary}",
            $"Support case {supportCase.CaseNumber}",
            0.85m,
            DateTime.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

file sealed class NoopAuditEventWriter : IAuditEventWriter
{
    public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}












