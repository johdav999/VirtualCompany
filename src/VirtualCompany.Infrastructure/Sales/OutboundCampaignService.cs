using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.CustomerMemory;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class OutboundCampaignService : IOutboundCampaignService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ISequenceExecutionService _sequenceExecution;

    public OutboundCampaignService(VirtualCompanyDbContext dbContext, ISequenceExecutionService sequenceExecution)
    {
        _dbContext = dbContext;
        _sequenceExecution = sequenceExecution;
    }

    public async Task<OutboundCampaignDetailResponse> CreateCampaignAsync(Guid companyId, Guid userId, CreateOutboundCampaignRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        ValidateCreate(request);

        var contactIds = request.ContactIds.Distinct().ToArray();
        var contacts = await _dbContext.Contacts.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && contactIds.Contains(x.Id) && !x.IsDeleted && x.Status == SalesStatuses.Active)
            .ToListAsync(cancellationToken);
        if (contacts.Count != contactIds.Length)
        {
            throw Validation(nameof(request.ContactIds), "Every campaign contact must belong to this company and be active.");
        }

        var now = DateTime.UtcNow;
        var sequence = new SalesSequence(Guid.NewGuid(), companyId, request.Name, description: request.Description, createdUtc: now, updatedUtc: now);
        foreach (var step in request.Steps.OrderBy(x => x.StepOrder))
        {
            sequence.Steps.Add(new SalesSequenceStep(Guid.NewGuid(), companyId, sequence.Id, step.StepOrder, step.DelayDays, step.Body, templateSubject: step.Subject, aiPersonalizationEnabled: step.AiPersonalizationEnabled, createdUtc: now, updatedUtc: now));
        }

        if (!sequence.HasEnoughSteps)
        {
            throw Validation(nameof(request.Steps), "A campaign sequence needs at least 4 steps.");
        }

        sequence.Activate();
        var campaign = new SalesCampaign(Guid.NewGuid(), companyId, sequence.Id, request.Name, request.AudienceType, createdUtc: now, updatedUtc: now);
        campaign.SetPolicy(request.Policy.OutboundEnabled, request.Policy.MaxEmailsPerDay, request.Policy.ApprovalRequired);
        foreach (var contact in contacts)
        {
            campaign.Contacts.Add(new SalesCampaignContact(Guid.NewGuid(), companyId, campaign.Id, contact.Id, enrolledUtc: now, createdUtc: now, updatedUtc: now));
        }

        _dbContext.SalesSequences.Add(sequence);
        _dbContext.SalesCampaigns.Add(campaign);
        AddAudit(companyId, userId, "sales.campaign.created", "sales_campaign", campaign.Id, AuditEventOutcomes.Succeeded, "Alex created an outbound campaign draft.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return (await GetCampaignAsync(companyId, campaign.Id, cancellationToken))!;
    }

    public async Task<OutboundCampaignDetailResponse?> GetCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureId(campaignId, nameof(campaignId));

        var campaign = await CampaignQuery(companyId)
            .SingleOrDefaultAsync(x => x.Id == campaignId, cancellationToken);
        return campaign is null ? null : MapDetail(campaign);
    }

    public async Task<OutboundAudienceOptionsResponse> GetAudienceOptionsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);

        var wonContactIds = await _dbContext.Deals.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.PrimaryContactId.HasValue && x.Status == SalesStatuses.Won)
            .Select(x => x.PrimaryContactId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var importedContactIds = await _dbContext.SalesEmailLinks.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.ContactId.HasValue && !x.IsDeleted)
            .Select(x => x.ContactId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var contacts = await _dbContext.Contacts.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.CustomerCompany)
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Status == SalesStatuses.Active)
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Email)
            .ToListAsync(cancellationToken);

        var audience = contacts.Select(contact =>
        {
            var sources = new List<string> { "existing_contacts" };
            if (wonContactIds.Contains(contact.Id))
            {
                sources.Add("past_customers");
            }

            if (importedContactIds.Contains(contact.Id))
            {
                sources.Add("imported_contacts");
            }

            return new OutboundAudienceContactResponse(
                contact.Id,
                contact.FullName,
                contact.Email,
                contact.CustomerCompany?.Name,
                sources);
        }).ToList();

        var sourceCounts = new[]
        {
            new OutboundAudienceSourceResponse("existing_contacts", "Existing contacts", audience.Count),
            new OutboundAudienceSourceResponse("past_customers", "Past customers", audience.Count(x => x.SourceTypes.Contains("past_customers"))),
            new OutboundAudienceSourceResponse("imported_contacts", "Imported contact lists", audience.Count(x => x.SourceTypes.Contains("imported_contacts")))
        };

        return new OutboundAudienceOptionsResponse(audience, sourceCounts);
    }

    public async Task<IReadOnlyList<OutboundCampaignSummaryResponse>> ListCampaignsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var campaigns = await CampaignQuery(companyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var campaignIds = campaigns.Select(x => x.Id).ToArray();
        var executionCounts = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && campaignIds.Contains(x.SalesCampaignId))
            .GroupBy(x => x.SalesCampaignId)
            .Select(x => new
            {
                CampaignId = x.Key,
                Pending = x.Count(step => step.Status == SalesStatuses.Pending),
                Sent = x.Count(step => step.SentUtc.HasValue),
                Bounced = x.Count(step => step.BounceStatus != null || step.DeliveryStatus == SalesStatuses.Bounced)
            })
            .ToDictionaryAsync(x => x.CampaignId, cancellationToken);

        return campaigns.Select(x => new OutboundCampaignSummaryResponse(
            x.Id,
            x.Name,
            StatusLabel(x.Status),
            x.Contacts.Count,
            executionCounts.TryGetValue(x.Id, out var counts) ? counts.Pending : 0,
            executionCounts.TryGetValue(x.Id, out counts) ? counts.Sent : 0,
            executionCounts.TryGetValue(x.Id, out counts) ? counts.Bounced : 0,
            x.UpdatedUtc)).ToList();
    }

    public async Task<OutboundCampaignDetailResponse?> LaunchCampaignAsync(Guid companyId, Guid userId, Guid campaignId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var campaign = await MutableCampaignAsync(companyId, campaignId, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        if (campaign.SalesSequence.Steps.Count < 4)
        {
            throw Validation(nameof(campaignId), "A campaign sequence needs at least 4 steps before launch.");
        }

        campaign.RequestLaunch();
        AddAudit(companyId, userId, "sales.campaign.launch_requested", "sales_campaign", campaign.Id, AuditEventOutcomes.Started, campaign.ApprovalRequired ? "Outbound approval is required before campaign emails are sent." : "Alex launched the outbound campaign.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (campaign.Status == SalesStatuses.Active)
        {
            await _sequenceExecution.ScheduleExecutionsForCampaignAsync(companyId, campaign.Id, cancellationToken);
        }

        return await GetCampaignAsync(companyId, campaign.Id, cancellationToken);
    }

    public async Task<OutboundCampaignDetailResponse?> PauseCampaignAsync(Guid companyId, Guid userId, Guid campaignId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var campaign = await MutableCampaignAsync(companyId, campaignId, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        campaign.Pause();
        AddAudit(companyId, userId, "sales.campaign.paused", "sales_campaign", campaign.Id, AuditEventOutcomes.Succeeded, "Alex paused new campaign sends.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCampaignAsync(companyId, campaign.Id, cancellationToken);
    }

    public async Task<OutboundCampaignDetailResponse?> StopCampaignAsync(Guid companyId, Guid userId, Guid campaignId, string? reason, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var campaign = await MutableCampaignAsync(companyId, campaignId, cancellationToken);
        if (campaign is null)
        {
            return null;
        }

        campaign.Stop();
        foreach (var contact in campaign.Contacts)
        {
            contact.MarkCancelled();
        }

        var pendingSteps = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId && x.Status == SalesStatuses.Pending)
            .ToListAsync(cancellationToken);
        foreach (var step in pendingSteps)
        {
            step.Cancel();
        }

        var executions = await _dbContext.SalesSequenceExecutions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId && x.Status != SalesStatuses.Completed)
            .ToListAsync(cancellationToken);
        foreach (var execution in executions)
        {
            execution.Stop(string.IsNullOrWhiteSpace(reason) ? SalesStopReasons.CampaignStopped : reason);
        }

        AddAudit(companyId, userId, "sales.campaign.stopped", "sales_campaign", campaign.Id, AuditEventOutcomes.Succeeded, "Alex stopped the campaign and cancelled pending future steps.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCampaignAsync(companyId, campaign.Id, cancellationToken);
    }

    private IQueryable<SalesCampaign> CampaignQuery(Guid companyId) =>
        _dbContext.SalesCampaigns.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.SalesSequence).ThenInclude(x => x.Steps)
            .Include(x => x.Contacts).ThenInclude(x => x.Contact)
            .Where(x => x.CompanyId == companyId);

    private Task<SalesCampaign?> MutableCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken) =>
        _dbContext.SalesCampaigns.IgnoreQueryFilters()
            .Include(x => x.SalesSequence).ThenInclude(x => x.Steps)
            .Include(x => x.Contacts).ThenInclude(x => x.Contact)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == campaignId, cancellationToken);

    private OutboundCampaignDetailResponse MapDetail(SalesCampaign campaign)
    {
        var executions = _dbContext.SalesSequenceExecutions.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Contact)
            .Include(x => x.Steps)
            .Where(x => x.CompanyId == campaign.CompanyId && x.SalesCampaignId == campaign.Id)
            .OrderBy(x => x.Contact.FullName)
            .ToList();

        return new OutboundCampaignDetailResponse(
            campaign.Id,
            campaign.Name,
            campaign.SalesSequence.Description,
            StatusLabel(campaign.Status),
            StatusLabel(campaign.AudienceType),
            new OutboundPolicyResponse(campaign.OutboundEnabled, campaign.MaxEmailsPerDay, campaign.ApprovalRequired),
            campaign.Contacts.OrderBy(x => x.Contact.FullName).Select(x => new OutboundCampaignContactResponse(x.ContactId, x.Contact.FullName, x.Contact.Email, StatusLabel(x.Status), x.CurrentStepOrder, x.EnrolledUtc)).ToList(),
            campaign.SalesSequence.Steps.OrderBy(x => x.StepOrder).Select(x => new SequenceStepResponse(x.Id, x.StepOrder, x.DelayDays, x.TemplateSubject ?? $"Step {x.StepOrder}", x.AiPersonalizationEnabled)).ToList(),
            executions.Select(x => new SequenceExecutionResponse(
                x.Id,
                x.ContactId,
                x.Contact.FullName,
                StatusLabel(x.Status),
                x.StopReason is null ? null : StatusLabel(x.StopReason),
                x.Steps.OrderBy(s => s.StepOrder).Select(MapStep).ToList())).ToList(),
            campaign.CreatedUtc,
            campaign.UpdatedUtc);
    }

    private static void ValidateCreate(CreateOutboundCampaignRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors[nameof(request.Name)] = ["Campaign name is required."];
        if (string.IsNullOrWhiteSpace(request.AudienceType)) errors[nameof(request.AudienceType)] = ["Audience type is required."];
        if (request.ContactIds.Count == 0) errors[nameof(request.ContactIds)] = ["Select at least one eligible contact."];
        if (request.Policy.MaxEmailsPerDay <= 0) errors[nameof(request.Policy.MaxEmailsPerDay)] = ["Daily email limit must be greater than zero."];
        if (!request.Policy.OutboundEnabled) errors[nameof(request.Policy.OutboundEnabled)] = ["Outbound email is disabled for this company."];
        if (request.Steps.Count < 4) errors[nameof(request.Steps)] = ["A campaign sequence needs at least 4 steps."];
        if (request.Steps.Select(x => x.StepOrder).Distinct().Count() != request.Steps.Count) errors[nameof(request.Steps)] = ["Sequence step order must be unique."];
        foreach (var step in request.Steps)
        {
            if (step.StepOrder <= 0) errors[$"{nameof(request.Steps)}.{step.StepOrder}.StepOrder"] = ["Step order must be positive."];
            if (step.DelayDays < 0) errors[$"{nameof(request.Steps)}.{step.StepOrder}.DelayDays"] = ["Delay days cannot be negative."];
            if (string.IsNullOrWhiteSpace(step.Subject)) errors[$"{nameof(request.Steps)}.{step.StepOrder}.Subject"] = ["Email subject is required."];
            if (string.IsNullOrWhiteSpace(step.Body)) errors[$"{nameof(request.Steps)}.{step.StepOrder}.Body"] = ["Email body is required."];
        }

        if (errors.Count > 0)
        {
            throw new SalesValidationException(errors);
        }
    }

    private void AddAudit(Guid companyId, Guid userId, string action, string targetType, Guid targetId, string outcome, string summary) =>
        _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.Human, userId.ToString("D"), action, targetType, targetId.ToString("D"), outcome, summary, DateTime.UtcNow));

    internal static SequenceExecutionStepResponse MapStep(SalesSequenceExecutionStep step) =>
        new(
            step.Id,
            step.StepOrder,
            StatusLabel(step.Status),
            step.ScheduledSendUtc,
            step.SentUtc,
            step.ProviderMessageId,
            StatusLabel(step.DeliveryStatus),
            step.BounceStatus is null ? null : StatusLabel(step.BounceStatus),
            step.CancellationReason is null ? null : StatusLabel(step.CancellationReason),
            step.CancellationSourceReference,
            step.OriginalGeneratedSubject,
            step.OriginalGeneratedBody,
            step.CurrentDraftSubject,
            step.CurrentDraftBody,
            step.FinalSentSubject,
            step.FinalSentBody,
            step.GeneratedDraftUtc,
            step.DraftUpdatedUtc);

    private static SalesValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static string StatusLabel(string value) =>
        value.Replace("_", " ", StringComparison.Ordinal).Trim() is { Length: > 0 } label ? char.ToUpperInvariant(label[0]) + label[1..] : value;

    private static void EnsureCompany(Guid companyId) => SalesValidationException.ThrowIfEmpty(companyId, "companyId");
    private static void EnsureUser(Guid userId) => SalesValidationException.ThrowIfEmpty(userId, "userId");
    private static void EnsureId(Guid id, string field) => SalesValidationException.ThrowIfEmpty(id, field);
}

public sealed class SequenceExecutionService : ISequenceExecutionService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IOutboundEmailSender _emailSender;
    private readonly ICompanyOutboxEnqueuer _outbox;
    private readonly IOutboundAutomationEnforcementService _outboundPolicy;
    private readonly ILogger<SequenceExecutionService> _logger;
    private readonly ICustomerMemoryService _customerMemory;
    private readonly CustomerMemoryOptions _customerMemoryOptions;
    private readonly IConversionAnalyticsService _conversionAnalytics;

    public SequenceExecutionService(VirtualCompanyDbContext dbContext, IOutboundEmailSender emailSender, ICompanyOutboxEnqueuer outbox, IOutboundAutomationEnforcementService outboundPolicy, ILogger<SequenceExecutionService> logger, ICustomerMemoryService customerMemory, IOptions<CustomerMemoryOptions> customerMemoryOptions, IConversionAnalyticsService conversionAnalytics)
    {
        _dbContext = dbContext;
        _emailSender = emailSender;
        _outbox = outbox;
        _outboundPolicy = outboundPolicy;
        _logger = logger;
        _customerMemory = customerMemory;
        _customerMemoryOptions = customerMemoryOptions.Value;
        _conversionAnalytics = conversionAnalytics;
    }

    public async Task<int> ScheduleExecutionsForCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken)
    {
        var campaign = await _dbContext.SalesCampaigns.IgnoreQueryFilters()
            .Include(x => x.SalesSequence).ThenInclude(x => x.Steps)
            .Include(x => x.Contacts).ThenInclude(x => x.Contact).ThenInclude(x => x.CustomerCompany)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == campaignId, cancellationToken);
        if (campaign is null || campaign.Status != SalesStatuses.Active)
        {
            return 0;
        }

        var steps = campaign.SalesSequence.Steps.OrderBy(x => x.StepOrder).ToArray();
        if (steps.Length < 4)
        {
            throw new InvalidOperationException("A campaign sequence needs at least 4 steps.");
        }

        var created = 0;
        var now = DateTime.UtcNow;
        foreach (var audience in campaign.Contacts.Where(x => x.Status != SalesStatuses.Cancelled && x.Contact.Status == SalesStatuses.Active))
        {
            var exists = await _dbContext.SalesSequenceExecutions.IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == companyId && x.SalesCampaignId == campaign.Id && x.ContactId == audience.ContactId, cancellationToken);
            if (exists)
            {
                continue;
            }

            var eligibility = await _customerMemory.EvaluateOfferEligibilityAsync(
                companyId,
                audience.ContactId,
                campaign.Name,
                TimeSpan.FromDays(Math.Clamp(_customerMemoryOptions.DuplicateOfferLookbackDays, 1, 3650)),
                cancellationToken);
            if (!eligibility.CanSend)
            {
                audience.MarkCancelled();
                _dbContext.AuditEvents.Add(new AuditEvent(
                    Guid.NewGuid(),
                    companyId,
                    AuditActorTypes.System,
                    "customer-memory",
                    "sales.sequence.duplicate_offer_blocked",
                    "contact",
                    audience.ContactId.ToString("D"),
                    AuditEventOutcomes.Succeeded,
                    eligibility.BlockReason ?? "Alex blocked a duplicate offer for this contact.",
                    DateTime.UtcNow));
                continue;
            }

            var memory = await _customerMemory.RefreshProfileAsync(companyId, audience.ContactId, cancellationToken);
            var execution = new SalesSequenceExecution(Guid.NewGuid(), companyId, campaign.Id, audience.Id, audience.ContactId);
            foreach (var sequenceStep in steps)
            {
                var scheduled = now.Date.AddDays(sequenceStep.DelayDays).Add(now.TimeOfDay);
                execution.Steps.Add(new SalesSequenceExecutionStep(
                    Guid.NewGuid(),
                    companyId,
                    execution.Id,
                    campaign.Id,
                    audience.ContactId,
                    sequenceStep.Id,
                    sequenceStep.StepOrder,
                    scheduled,
                    $"sales-sequence:{companyId:N}:{campaign.Id:N}:{audience.ContactId:N}:{sequenceStep.Id:N}"));
                var generatedStep = execution.Steps.Last();
                var draft = BuildPersonalizedDraft(sequenceStep.TemplateSubject ?? "Following up", sequenceStep.TemplateContent, audience.Contact, memory, sequenceStep.AiPersonalizationEnabled);
                generatedStep.RecordGeneratedDraft(draft.Subject, draft.Body, now);
            }

            audience.MarkScheduled(steps[0].StepOrder, execution.Steps.Min(x => x.ScheduledSendUtc));
            _dbContext.SalesSequenceExecutions.Add(execution);
            created++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task<SequenceProcessingResult> ProcessDueStepsAsync(DateTime dueBeforeUtc, int batchSize, CancellationToken cancellationToken)
    {
        var dueSteps = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .Include(x => x.SequenceExecution).ThenInclude(x => x.SalesCampaign)
            .Include(x => x.SequenceExecution).ThenInclude(x => x.Contact)
            .ThenInclude(x => x.CustomerCompany)
            .Include(x => x.SalesSequenceStep)
            .Where(x => x.Status == SalesStatuses.Pending && x.ScheduledSendUtc <= dueBeforeUtc)
            .OrderBy(x => x.ScheduledSendUtc)
            .Take(Math.Clamp(batchSize, 1, 200))
            .ToListAsync(cancellationToken);

        var sent = 0;
        var deferred = 0;
        var failed = 0;
        var cancelled = 0;
        foreach (var step in dueSteps)
        {
            var campaign = step.SequenceExecution.SalesCampaign;
            if (campaign.Status != SalesStatuses.Active || campaign.ApprovalRequired && campaign.ApprovedUtc is null)
            {
                step.Cancel();
                cancelled++;
                continue;
            }

            if (await DailyLimitReachedAsync(step.CompanyId, campaign.MaxEmailsPerDay, cancellationToken))
            {
                Defer(step);
                deferred++;
                continue;
            }

            var policyDecision = await _outboundPolicy.EvaluateSequenceStepAsync(step.CompanyId, step.Id, cancellationToken);
            if (policyDecision.Outcome == OutboundPolicyOutcomes.Blocked)
            {
                cancelled++;
                continue;
            }

            if (policyDecision.Outcome == OutboundPolicyOutcomes.RequiresApproval)
            {
                deferred++;
                continue;
            }

            try
            {
                step.MarkSending();
                step.SequenceExecution.MarkStarted();
                var contact = step.SequenceExecution.Contact;
                if (string.IsNullOrWhiteSpace(step.CurrentDraftSubject) || string.IsNullOrWhiteSpace(step.CurrentDraftBody))
                {
                    var memory = await _customerMemory.RefreshProfileAsync(step.CompanyId, step.ContactId, cancellationToken);
                    var generated = BuildPersonalizedDraft(step.SalesSequenceStep.TemplateSubject ?? "Following up", step.SalesSequenceStep.TemplateContent, contact, memory, step.SalesSequenceStep.AiPersonalizationEnabled);
                    step.RecordGeneratedDraft(generated.Subject, generated.Body, DateTime.UtcNow);
                }

                var draft = new PersonalizedDraft(step.CurrentDraftSubject!, step.CurrentDraftBody!);

                var result = await _emailSender.SendSequenceEmailAsync(new OutboundEmailSendRequest(
                    step.CompanyId,
                    step.SalesCampaignId,
                    step.SequenceExecutionId,
                    step.Id,
                    step.ContactId,
                    contact.Email,
                    contact.FullName,
                    draft.Subject,
                    draft.Body,
                    step.IdempotencyKey,
                    step.OriginalGeneratedSubject,
                    step.OriginalGeneratedBody), cancellationToken);

                var sentUtc = DateTime.UtcNow;
                step.MarkSent(result.Provider, result.MailboxConnectionId, result.ProviderMessageId, result.ProviderThreadId, result.InternetMessageId, result.DeliveryStatus, sentUtc, draft.Subject, draft.Body);
                var audience = await _dbContext.SalesCampaignContacts.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == step.CompanyId && x.Id == step.SequenceExecution.SalesCampaignContactId, cancellationToken);
                audience.MarkSent(step.StepOrder, sentUtc);
                await _conversionAnalytics.RecordMessagePerformanceEventAsync(
                    new RecordMessagePerformanceEventCommand(
                        step.CompanyId,
                        result.ProviderMessageId,
                        step.ContactId,
                        ConversionAnalyticsEventType.Sent,
                        sentUtc,
                        CampaignId: step.SalesCampaignId,
                        SequenceStepId: step.SalesSequenceStepId,
                        SequenceExecutionStepId: step.Id,
                        Provider: result.Provider,
                        ProviderMessageId: result.ProviderMessageId,
                        ProviderThreadId: result.ProviderThreadId,
                        InternetMessageId: result.InternetMessageId,
                        StepOrder: step.StepOrder),
                    cancellationToken);
                sent++;
            }
            catch (MailboxProviderExecutionException ex)
            {
                _logger.LogWarning(ex, "Sequence email provider failure for campaign {CampaignId} step {StepId}", step.SalesCampaignId, step.Id);
                Defer(step);
                failed++;
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return new SequenceProcessingResult(sent, deferred, failed, cancelled);
    }

    public async Task<int> CancelPendingStepsForContactAsync(Guid companyId, Guid contactId, string stopReason, CancellationToken cancellationToken)
    {
        var executions = await _dbContext.SalesSequenceExecutions.IgnoreQueryFilters()
            .Include(x => x.Steps)
            .Include(x => x.SalesCampaign)
            .Where(x => x.CompanyId == companyId &&
                x.ContactId == contactId &&
                x.SalesCampaign.Status == SalesStatuses.Active &&
                x.Status != SalesStatuses.Completed &&
                x.Status != SalesStatuses.Stopped)
            .ToListAsync(cancellationToken);
        var cancelled = 0;
        var cancelledAt = DateTime.UtcNow;
        foreach (var execution in executions)
        {
            execution.Stop(stopReason);
            foreach (var step in execution.Steps.Where(x => x.Status == SalesStatuses.Pending))
            {
                step.Cancel(stopReason, $"{stopReason}:{companyId:N}:{contactId:N}", cancelledAt);
                cancelled++;
            }

            var audience = await _dbContext.SalesCampaignContacts.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == execution.SalesCampaignContactId, cancellationToken);
            audience?.MarkCancelled();
        }

        if (cancelled > 0)
        {
            _dbContext.AuditEvents.Add(new AuditEvent(
                Guid.NewGuid(),
                companyId,
                AuditActorTypes.System,
                "sales-sequence-cancellation",
                "sales.sequence.pending_steps_cancelled",
                "contact",
                contactId.ToString("D"),
                AuditEventOutcomes.Succeeded,
                stopReason == SalesStopReasons.ReplyReceived
                    ? "Alex stopped future campaign emails because the contact replied."
                    : "Alex stopped future campaign emails because a deal was created for the contact.",
                DateTime.UtcNow));

            _logger.LogInformation(
                "Cancelled {CancelledStepCount} pending sales sequence step(s) for contact {ContactId} in company {CompanyId} because {StopReason}.",
                cancelled,
                contactId,
                companyId,
                stopReason);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return cancelled;
    }

    public async Task QueueReplyReceivedAsync(Guid companyId, OutboundReplyReceived request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var occurredUtc = request.OccurredUtc ?? DateTime.UtcNow;
        var sourceId = FirstNonEmpty(request.ProviderMessageId, request.InternetMessageId, request.ProviderThreadId, request.SenderEmail);
        var eventId = $"{CompanyOutboxTopics.SalesEmailReceived}:{companyId:N}:{sourceId}";
        _outbox.Enqueue(
            companyId,
            CompanyOutboxTopics.SalesEmailReceived,
            new PlatformEventEnvelope(
                eventId,
                CompanyOutboxTopics.SalesEmailReceived,
                occurredUtc,
                companyId,
                eventId,
                "sales_email",
                sourceId,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(companyId),
                    ["providerMessageId"] = JsonValue.Create(request.ProviderMessageId),
                    ["providerThreadId"] = JsonValue.Create(request.ProviderThreadId),
                    ["internetMessageId"] = JsonValue.Create(request.InternetMessageId),
                    ["senderEmail"] = JsonValue.Create(request.SenderEmail),
                    ["occurredUtc"] = JsonValue.Create(occurredUtc)
                }),
            eventId,
            idempotencyKey: $"platform-event:{companyId:N}:{eventId}",
            causationId: sourceId);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> HandleReplyReceivedAsync(Guid companyId, OutboundReplyReceived request, CancellationToken cancellationToken)
    {
        var step = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                (x.ProviderMessageId == request.ProviderMessageId ||
                 x.ProviderThreadId == request.ProviderThreadId ||
                 x.InternetMessageId == request.InternetMessageId))
            .OrderByDescending(x => x.SentUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (step is not null)
        {
            await _conversionAnalytics.RecordMessagePerformanceEventAsync(
                new RecordMessagePerformanceEventCommand(
                    companyId,
                    FirstNonEmpty(step.ProviderMessageId, step.InternetMessageId, step.ProviderThreadId),
                    step.ContactId,
                    ConversionAnalyticsEventType.Replied,
                    request.OccurredUtc ?? DateTime.UtcNow,
                    CampaignId: step.SalesCampaignId,
                    SequenceStepId: step.SalesSequenceStepId,
                    SequenceExecutionStepId: step.Id,
                    Provider: step.Provider,
                    ProviderMessageId: step.ProviderMessageId,
                    ProviderThreadId: step.ProviderThreadId,
                    InternetMessageId: step.InternetMessageId,
                    StepOrder: step.StepOrder),
                cancellationToken);
            return await CancelPendingStepsForContactAsync(companyId, step.ContactId, SalesStopReasons.ReplyReceived, cancellationToken);
        }

        var sender = request.SenderEmail.Trim().ToLowerInvariant();
        var contactId = await _dbContext.Contacts.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Email == sender && !x.IsDeleted)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return contactId is Guid resolvedContactId
            ? await CancelPendingStepsForContactAsync(companyId, resolvedContactId, SalesStopReasons.ReplyReceived, cancellationToken)
            : 0;
    }

    public async Task<int> HandleDealCreatedAsync(Guid companyId, Guid contactId, Guid dealId, CancellationToken cancellationToken)
    {
        await _conversionAnalytics.RecordDealCreatedForContactAsync(companyId, contactId, dealId, DateTime.UtcNow, cancellationToken);
        return await CancelPendingStepsForContactAsync(companyId, contactId, SalesStopReasons.DealCreated, cancellationToken);
    }

    public async Task QueueDealCreatedAsync(Guid companyId, Guid contactId, Guid dealId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        if (contactId == Guid.Empty || dealId == Guid.Empty)
        {
            throw new ArgumentException("Contact and deal identifiers are required.");
        }

        var eventId = $"{CompanyOutboxTopics.SalesDealCreated}:{companyId:N}:{dealId:N}";
        _outbox.Enqueue(
            companyId,
            CompanyOutboxTopics.SalesDealCreated,
            new PlatformEventEnvelope(
                eventId,
                CompanyOutboxTopics.SalesDealCreated,
                DateTime.UtcNow,
                companyId,
                eventId,
                "deal",
                dealId.ToString("D"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(companyId),
                    ["dealId"] = JsonValue.Create(dealId),
                    ["contactId"] = JsonValue.Create(contactId)
                }),
            eventId,
            idempotencyKey: $"platform-event:{companyId:N}:{eventId}",
            causationId: dealId.ToString("D"));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleDeliveryStatusAsync(Guid companyId, OutboundDeliveryStatusRequest request, CancellationToken cancellationToken)
    {
        var step = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ProviderMessageId == request.ProviderMessageId, cancellationToken);
        step?.MarkDeliveryStatus(request.Status, request.OccurredUtc);
        if (step is not null)
        {
            await _conversionAnalytics.RecordMessagePerformanceEventAsync(
                new RecordMessagePerformanceEventCommand(
                    companyId,
                    FirstNonEmpty(step.ProviderMessageId, step.InternetMessageId, step.ProviderThreadId),
                    step.ContactId,
                    request.Status.Equals(SalesStatuses.Bounced, StringComparison.OrdinalIgnoreCase)
                        ? ConversionAnalyticsEventType.Bounced
                        : ConversionAnalyticsEventType.Delivered,
                    request.OccurredUtc,
                    CampaignId: step.SalesCampaignId,
                    SequenceStepId: step.SalesSequenceStepId,
                    SequenceExecutionStepId: step.Id,
                    Provider: step.Provider,
                    ProviderMessageId: step.ProviderMessageId,
                    ProviderThreadId: step.ProviderThreadId,
                    InternetMessageId: step.InternetMessageId,
                    StepOrder: step.StepOrder),
                cancellationToken);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SequenceExecutionStepResponse?> SaveDraftAsync(Guid companyId, Guid userId, Guid campaignId, Guid stepId, SaveSequenceDraftRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        var step = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SalesCampaignId == campaignId && x.Id == stepId, cancellationToken);
        if (step is null)
        {
            return null;
        }

        if (step.Status is SalesStatuses.Completed or SalesStatuses.Cancelled)
        {
            throw new InvalidOperationException("Only unsent campaign drafts can be edited.");
        }

        if (string.IsNullOrWhiteSpace(step.OriginalGeneratedSubject) || string.IsNullOrWhiteSpace(step.OriginalGeneratedBody))
        {
            throw new InvalidOperationException("A generated draft must exist before edits can be saved.");
        }

        step.UpdateDraftContent(request.Subject, request.Body, DateTime.UtcNow);
        _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.Human, userId.ToString("D"), "sales.sequence.draft_edited", "sales_sequence_execution_step", step.Id.ToString("D"), AuditEventOutcomes.Succeeded, "A campaign draft was edited before sending.", DateTime.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return OutboundCampaignService.MapStep(step);
    }

    public async Task HandleBounceAsync(Guid companyId, OutboundBounceRequest request, CancellationToken cancellationToken)
    {
        var step = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ProviderMessageId == request.ProviderMessageId, cancellationToken);
        if (step is null)
        {
            return;
        }

        step.MarkBounce(request.BounceStatus, request.Reason, request.OccurredUtc);
        await _conversionAnalytics.RecordMessagePerformanceEventAsync(
            new RecordMessagePerformanceEventCommand(
                companyId,
                FirstNonEmpty(step.ProviderMessageId, step.InternetMessageId, step.ProviderThreadId),
                step.ContactId,
                ConversionAnalyticsEventType.Bounced,
                request.OccurredUtc,
                CampaignId: step.SalesCampaignId,
                SequenceStepId: step.SalesSequenceStepId,
                SequenceExecutionStepId: step.Id,
                Provider: step.Provider,
                ProviderMessageId: step.ProviderMessageId,
                ProviderThreadId: step.ProviderThreadId,
                InternetMessageId: step.InternetMessageId,
                StepOrder: step.StepOrder),
            cancellationToken);
        await CancelPendingStepsForContactAsync(companyId, step.ContactId, SalesStopReasons.Bounced, cancellationToken);
    }

    private async Task<bool> DailyLimitReachedAsync(Guid companyId, int maxEmailsPerDay, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var sentToday = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId && x.SentUtc >= today && x.SentUtc < today.AddDays(1), cancellationToken);
        return sentToday >= maxEmailsPerDay;
    }

    private static PersonalizedDraft BuildPersonalizedDraft(string subject, string body, Contact contact, CustomerMemoryContext? memory, bool aiPersonalizationEnabled)
    {
        var firstName = contact.FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? contact.FullName;
        var companyName = memory?.CustomerCompanyName ?? contact.CustomerCompany?.Name ?? "your team";
        var effectiveSubject = ApplyTemplateTokens(subject, firstName, companyName);
        var effectiveBody = ApplyTemplateTokens(body, firstName, companyName);

        if (aiPersonalizationEnabled && memory is not null)
        {
            var memoryLines = new List<string>();
            if (!string.IsNullOrWhiteSpace(memory.RelationshipMemory))
            {
                memoryLines.Add(memory.RelationshipMemory);
            }

            if (memory.PreviousDeals.Count > 0)
            {
                memoryLines.Add($"Previous deal context: {memory.PreviousDeals[0].Summary}");
            }

            if (memory.PriceSensitivityIndicators.Count > 0)
            {
                memoryLines.Add($"Price context: {memory.PriceSensitivityIndicators[0].Value}");
            }

            if (!string.IsNullOrWhiteSpace(memory.LastOutreachSummary))
            {
                memoryLines.Add($"Last outreach: {memory.LastOutreachSummary}");
            }

            var contextLine = string.Join(" ", memoryLines.Where(x => !string.IsNullOrWhiteSpace(x)).Take(3));
            if (!string.IsNullOrWhiteSpace(contextLine) &&
                !effectiveBody.Contains(contextLine, StringComparison.OrdinalIgnoreCase))
            {
                effectiveBody = $"{effectiveBody.Trim()}\n\nPersonal note: {contextLine}";
            }
        }

        return new PersonalizedDraft(effectiveSubject, effectiveBody);
    }

    private sealed record PersonalizedDraft(string Subject, string Body);

    private static string ApplyTemplateTokens(string template, string firstName, string companyName) =>
        template
            .Replace("{{first_name}}", firstName, StringComparison.OrdinalIgnoreCase)
            .Replace("{firstName}", firstName, StringComparison.OrdinalIgnoreCase)
            .Replace("{first_name}", firstName, StringComparison.OrdinalIgnoreCase)
            .Replace("{{company_name}}", companyName, StringComparison.OrdinalIgnoreCase)
            .Replace("{companyName}", companyName, StringComparison.OrdinalIgnoreCase)
            .Replace("{company_name}", companyName, StringComparison.OrdinalIgnoreCase);

    private static void Defer(SalesSequenceExecutionStep step) =>
        typeof(SalesSequenceExecutionStep).GetProperty(nameof(SalesSequenceExecutionStep.ScheduledSendUtc))!.SetValue(step, DateTime.UtcNow.Date.AddDays(1).AddHours(8));

    private static void EnsureCompany(Guid companyId) =>
        _ = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim()
        ?? throw new ArgumentException("A provider message id, thread id, internet message id, or sender email is required.");
}

public sealed class MailboxOutboundEmailSender : IOutboundEmailSender
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxProviderRegistry _providerRegistry;
    private readonly IFieldEncryptionService _fieldEncryption;

    public MailboxOutboundEmailSender(VirtualCompanyDbContext dbContext, IMailboxProviderRegistry providerRegistry, IFieldEncryptionService fieldEncryption)
    {
        _dbContext = dbContext;
        _providerRegistry = providerRegistry;
        _fieldEncryption = fieldEncryption;
    }

    public async Task<OutboundEmailSendResult> SendSequenceEmailAsync(OutboundEmailSendRequest request, CancellationToken cancellationToken)
    {
        var connection = await _dbContext.MailboxConnections.IgnoreQueryFilters()
            .Where(x => x.CompanyId == request.CompanyId && x.Status == Domain.Enums.MailboxConnectionStatus.Active && x.EncryptedAccessToken != null)
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("A connected mailbox is required before campaign emails can be sent.");

        var accessToken = _fieldEncryption.Decrypt(
            request.CompanyId,
            CompanyMailboxConnectionService.BuildTokenPurpose(connection.Provider, "access_token"),
            connection.EncryptedAccessToken!);
        var provider = _providerRegistry.Resolve(connection.Provider);
        var result = await provider.SendReplyAsync(accessToken, new MailboxReplyExecutionRequest(
            request.CompanyId,
            connection.Id,
            connection.Provider.ToStorageValue(),
            request.IdempotencyKey,
            null,
            null,
            request.ToEmail,
            request.ToDisplayName,
            request.Subject,
            request.BodyText,
            request.IdempotencyKey), cancellationToken);

        return new OutboundEmailSendResult(connection.Provider.ToStorageValue(), connection.Id, result.ProviderMessageId, result.ProviderThreadId, null, result.Status);
    }
}

public static class SalesStopReasons
{
    public const string ReplyReceived = "reply_received";
    public const string DealCreated = "deal_created";
    public const string CampaignStopped = "campaign_stopped";
    public const string Bounced = "bounced";
    public const string EligibilityBlocked = "eligibility_blocked";
}
