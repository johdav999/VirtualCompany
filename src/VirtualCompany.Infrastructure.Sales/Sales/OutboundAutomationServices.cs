using System.Data;
using System.Net.Mail;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class OutboundAutomationPolicyService : IOutboundAutomationPolicyService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public OutboundAutomationPolicyService(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OutboundAutomationPolicyResponse> GetPolicyAsync(Guid companyId, CancellationToken cancellationToken) =>
        Map(await GetOrCreatePolicyAsync(companyId, cancellationToken));

    public async Task<OutboundAutomationPolicyResponse> UpdatePolicyAsync(Guid companyId, Guid userId, UpdateOutboundAutomationPolicyRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        Validate(request);

        if (request.WebsiteLeadFollowUpSequenceId is Guid sequenceId)
        {
            var exists = await _dbContext.SalesSequences.IgnoreQueryFilters()
                .AnyAsync(x => x.CompanyId == companyId && x.Id == sequenceId && x.Status == SalesStatuses.Active, cancellationToken);
            if (!exists)
            {
                throw new SalesValidationException(new Dictionary<string, string[]>
                {
                    [nameof(request.WebsiteLeadFollowUpSequenceId)] = ["Choose an active follow-up sequence for this company."]
                });
            }
        }

        var policy = await GetOrCreatePolicyAsync(companyId, cancellationToken);
        var before = Map(policy);
        policy.UpdateOutboundSettings(
            request.OutboundEnabled,
            request.MaxEmailsPerDay,
            request.RequireApprovalFirstContact,
            request.RequireApprovalPricingDiscussion,
            request.RequireApprovalFollowUps,
            request.RequireApprovalReEngagement,
            request.WebsiteLeadDeduplicationWindowMinutes,
            request.WebsiteLeadFollowUpSequenceId);

        _dbContext.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(),
            companyId,
            AuditActorTypes.Human,
            userId,
            "sales.outbound_policy.updated",
            "sales_automation_policy",
            policy.Id.ToString("D"),
            AuditEventOutcomes.Succeeded,
            "Outbound automation policy was updated.",
            occurredUtc: DateTime.UtcNow,
            payloadDiffJson: JsonSerializer.Serialize(new
            {
                before,
                after = Map(policy)
            })));

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    private async Task<SalesAutomationPolicy> GetOrCreatePolicyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var policy = await _dbContext.SalesAutomationPolicies.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (policy is not null)
        {
            return policy;
        }

        policy = new SalesAutomationPolicy(Guid.NewGuid(), companyId, SalesAutomationPolicyModes.ManualOnly);
        policy.EnsureWebsiteLeadFormKey();
        _dbContext.SalesAutomationPolicies.Add(policy);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return policy;
    }

    private static OutboundAutomationPolicyResponse Map(SalesAutomationPolicy policy) =>
        new(
            policy.Id,
            policy.OutboundEnabled,
            policy.MaxEmailsPerDay,
            policy.RequireApprovalFirstContact,
            policy.RequireApprovalPricingDiscussion,
            policy.RequireApprovalFollowUps,
            policy.RequireApprovalReEngagement,
            policy.WebsiteLeadDeduplicationWindowMinutes,
            policy.WebsiteLeadFormKey,
            policy.WebsiteLeadFollowUpSequenceId,
            policy.UpdatedUtc);

    private static void Validate(UpdateOutboundAutomationPolicyRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.MaxEmailsPerDay < 0)
        {
            errors[nameof(request.MaxEmailsPerDay)] = ["Daily email limit cannot be negative."];
        }

        if (request.WebsiteLeadDeduplicationWindowMinutes is < 1 or > 43200)
        {
            errors[nameof(request.WebsiteLeadDeduplicationWindowMinutes)] = ["Deduplication window must be between 1 minute and 30 days."];
        }

        if (errors.Count > 0)
        {
            throw new SalesValidationException(errors);
        }
    }

    private static void EnsureCompany(Guid companyId) => SalesValidationException.ThrowIfEmpty(companyId, "companyId");
    private static void EnsureUser(Guid userId) => SalesValidationException.ThrowIfEmpty(userId, "userId");
}

public sealed class OutboundAutomationEnforcementService : IOutboundAutomationEnforcementService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public OutboundAutomationEnforcementService(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<OutboundPolicyEvaluationResult> EvaluateSequenceStepAsync(Guid companyId, Guid stepId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        SalesValidationException.ThrowIfEmpty(stepId, nameof(stepId));

        var step = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .Include(x => x.SequenceExecution).ThenInclude(x => x.Contact)
            .Include(x => x.SequenceExecution).ThenInclude(x => x.SalesCampaign)
            .Include(x => x.SalesSequenceStep)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == stepId, cancellationToken);
        if (step is null)
        {
            return Blocked(OutboundPolicyReasonCodes.MissingPolicy, "The message could not be evaluated because the sequence step was not found.");
        }

        if (step.OutboundMessageReviewId is Guid existingReviewId)
        {
            return new OutboundPolicyEvaluationResult(OutboundPolicyOutcomes.RequiresApproval, step.PolicyDecisionReasonCode ?? OutboundPolicyReasonCodes.FollowUpApprovalRequired, step.PolicyDecisionReason ?? "This message is waiting for approval.", existingReviewId);
        }

        var policy = await _dbContext.SalesAutomationPolicies.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (policy is null)
        {
            return await PersistBlockedAsync(step, OutboundPolicyReasonCodes.MissingPolicy, "Outbound automation policy is missing, so this send was blocked.", cancellationToken);
        }

        if (!policy.OutboundEnabled)
        {
            return await PersistBlockedAsync(step, OutboundPolicyReasonCodes.OutboundDisabled, "Outbound automation is disabled for this company.", cancellationToken);
        }

        if (policy.MaxEmailsPerDay == 0 || await DailyLimitReachedAsync(companyId, policy.MaxEmailsPerDay, cancellationToken))
        {
            return await PersistBlockedAsync(step, OutboundPolicyReasonCodes.DailyLimitReached, "The daily outbound email limit has been reached.", cancellationToken);
        }

        var category = ResolveCategory(step);
        var approval = ApprovalRequired(policy, category);
        if (approval is not null)
        {
            return await PersistReviewAsync(step, category, approval.Value.reasonCode, approval.Value.reason, cancellationToken);
        }

        return new OutboundPolicyEvaluationResult(OutboundPolicyOutcomes.Allowed, OutboundPolicyReasonCodes.Allowed, "Outbound send is allowed under the current policy.", null);
    }

    private async Task<OutboundPolicyEvaluationResult> PersistBlockedAsync(SalesSequenceExecutionStep step, string reasonCode, string reason, CancellationToken cancellationToken)
    {
        step.MarkBlockedByPolicy(reasonCode, reason);
        _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), step.CompanyId, AuditActorTypes.System, "outbound-policy", "sales.outbound_send.blocked", "sales_sequence_execution_step", step.Id.ToString("D"), AuditEventOutcomes.Blocked, reason, DateTime.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Blocked(reasonCode, reason);
    }

    private async Task<OutboundPolicyEvaluationResult> PersistReviewAsync(SalesSequenceExecutionStep step, string category, string reasonCode, string reason, CancellationToken cancellationToken)
    {
        var subject = step.CurrentDraftSubject ?? step.OriginalGeneratedSubject ?? step.SalesSequenceStep.TemplateSubject ?? "Following up";
        var body = step.CurrentDraftBody ?? step.OriginalGeneratedBody ?? step.SalesSequenceStep.TemplateContent;
        var review = await _dbContext.OutboundMessageReviews.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == step.CompanyId && x.SequenceExecutionStepId == step.Id, cancellationToken);

        if (review is null)
        {
            review = new OutboundMessageReview(Guid.NewGuid(), step.CompanyId, step.Id, step.SalesCampaignId, step.ContactId, category, reasonCode, reason, subject, body);
            _dbContext.OutboundMessageReviews.Add(review);
        }

        step.MarkWaitingForOutboundReview(review.Id, reasonCode, reason);
        _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), step.CompanyId, AuditActorTypes.System, "outbound-policy", "sales.outbound_review.requested", "outbound_message_review", review.Id.ToString("D"), AuditEventOutcomes.Started, reason, DateTime.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new OutboundPolicyEvaluationResult(OutboundPolicyOutcomes.RequiresApproval, reasonCode, reason, review.Id);
    }

    private async Task<bool> DailyLimitReachedAsync(Guid companyId, int maxEmailsPerDay, CancellationToken cancellationToken)
    {
        var today = DateTime.UtcNow.Date;
        var sentToday = await _dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == companyId && x.SentUtc >= today && x.SentUtc < today.AddDays(1), cancellationToken);
        return sentToday >= maxEmailsPerDay;
    }

    private static (string reasonCode, string reason)? ApprovalRequired(SalesAutomationPolicy policy, string category) =>
        category switch
        {
            OutboundMessageCategories.FirstContact when policy.RequireApprovalFirstContact => (OutboundPolicyReasonCodes.FirstContactApprovalRequired, "First contact emails need approval before sending."),
            OutboundMessageCategories.PricingDiscussion when policy.RequireApprovalPricingDiscussion => (OutboundPolicyReasonCodes.PricingApprovalRequired, "Pricing discussions need approval before sending."),
            OutboundMessageCategories.FollowUp when policy.RequireApprovalFollowUps => (OutboundPolicyReasonCodes.FollowUpApprovalRequired, "Follow-up emails need approval before sending."),
            OutboundMessageCategories.ReEngagement when policy.RequireApprovalReEngagement => (OutboundPolicyReasonCodes.ReEngagementApprovalRequired, "Re-engagement emails need approval before sending."),
            _ => null
        };

    private static string ResolveCategory(SalesSequenceExecutionStep step)
    {
        var text = $"{step.SalesSequenceStep.TemplateSubject} {step.SalesSequenceStep.TemplateContent}".ToLowerInvariant();
        if (text.Contains("price", StringComparison.Ordinal) || text.Contains("pricing", StringComparison.Ordinal) || text.Contains("quote", StringComparison.Ordinal))
        {
            return OutboundMessageCategories.PricingDiscussion;
        }

        if (text.Contains("re-engage", StringComparison.Ordinal) || text.Contains("checking back", StringComparison.Ordinal))
        {
            return OutboundMessageCategories.ReEngagement;
        }

        return step.StepOrder == 1 ? OutboundMessageCategories.FirstContact : OutboundMessageCategories.FollowUp;
    }

    private static OutboundPolicyEvaluationResult Blocked(string reasonCode, string reason) =>
        new(OutboundPolicyOutcomes.Blocked, reasonCode, reason, null);

    private static void EnsureCompany(Guid companyId) => SalesValidationException.ThrowIfEmpty(companyId, "companyId");
}

public sealed class OutboundReviewQueueService : IOutboundReviewQueueService
{
    private readonly VirtualCompanyDbContext _dbContext;

    public OutboundReviewQueueService(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<OutboundReviewQueueItemResponse>> ListPendingAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var reviews = await ReviewQuery(companyId)
            .Where(x => x.Status == SalesStatuses.WaitingForApproval)
            .OrderBy(x => x.RequestedUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
        return reviews.Select(x => new OutboundReviewQueueItemResponse(x.Id, x.SequenceExecutionStepId, x.SalesCampaignId, x.ContactId, x.Contact.FullName, x.Contact.Email, Label(x.Category), Label(x.Status), x.Reason, x.RequestedUtc)).ToList();
    }

    public async Task<OutboundReviewQueueDetailResponse?> GetAsync(Guid companyId, Guid reviewId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var review = await ReviewQuery(companyId).SingleOrDefaultAsync(x => x.Id == reviewId, cancellationToken);
        return review is null ? null : Map(review);
    }

    public Task<OutboundReviewQueueDetailResponse?> ApproveAsync(Guid companyId, Guid userId, Guid reviewId, OutboundReviewDecisionRequest request, CancellationToken cancellationToken) =>
        DecideAsync(companyId, userId, reviewId, r => r.Approve(userId, request.Comment), "sales.outbound_review.approved", "Outbound message was approved.", cancellationToken);

    public Task<OutboundReviewQueueDetailResponse?> RejectAsync(Guid companyId, Guid userId, Guid reviewId, OutboundReviewDecisionRequest request, CancellationToken cancellationToken) =>
        DecideAsync(companyId, userId, reviewId, r => r.Reject(userId, request.Comment), "sales.outbound_review.rejected", "Outbound message was rejected.", cancellationToken);

    public Task<OutboundReviewQueueDetailResponse?> EditAndApproveAsync(Guid companyId, Guid userId, Guid reviewId, OutboundEditAndApproveRequest request, CancellationToken cancellationToken) =>
        DecideAsync(companyId, userId, reviewId, r =>
        {
            r.EditAndApprove(userId, request.Subject, request.Body, request.Comment);
            r.SequenceExecutionStep.UpdateDraftContent(request.Subject, request.Body, DateTime.UtcNow);
        }, "sales.outbound_review.edited_and_approved", "Outbound message was edited and approved.", cancellationToken);

    private async Task<OutboundReviewQueueDetailResponse?> DecideAsync(Guid companyId, Guid userId, Guid reviewId, Action<OutboundMessageReview> apply, string auditAction, string auditSummary, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var review = await ReviewQuery(companyId).SingleOrDefaultAsync(x => x.Id == reviewId, cancellationToken);
        if (review is null)
        {
            return null;
        }

        apply(review);
        _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.Human, userId.ToString("D"), auditAction, "outbound_message_review", review.Id.ToString("D"), AuditEventOutcomes.Succeeded, auditSummary, DateTime.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(review);
    }

    private IQueryable<OutboundMessageReview> ReviewQuery(Guid companyId) =>
        _dbContext.OutboundMessageReviews.IgnoreQueryFilters()
            .Include(x => x.Contact)
            .Include(x => x.SequenceExecutionStep)
            .Where(x => x.CompanyId == companyId);

    private static OutboundReviewQueueDetailResponse Map(OutboundMessageReview review) =>
        new(
            review.Id,
            review.SequenceExecutionStepId,
            review.SalesCampaignId,
            review.ContactId,
            review.Contact.FullName,
            review.Contact.Email,
            Label(review.Category),
            Label(review.Status),
            review.ReasonCode,
            review.Reason,
            review.OriginalSubject,
            review.OriginalBody,
            review.EditedSubject,
            review.EditedBody,
            review.DecidedByUserId,
            review.DecidedUtc,
            review.DecisionComment,
            review.RequestedUtc);

    private static string Label(string value) =>
        value.Replace("_", " ", StringComparison.Ordinal).Trim() is { Length: > 0 } label ? char.ToUpperInvariant(label[0]) + label[1..] : value;

    private static void EnsureCompany(Guid companyId) => SalesValidationException.ThrowIfEmpty(companyId, "companyId");
    private static void EnsureUser(Guid userId) => SalesValidationException.ThrowIfEmpty(userId, "userId");
}

public sealed class WebsiteLeadCaptureService : IWebsiteLeadCaptureService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyOutboxEnqueuer _outbox;
    private readonly ISalesSourceService _sources;

    public WebsiteLeadCaptureService(VirtualCompanyDbContext dbContext, ICompanyOutboxEnqueuer outbox, ISalesSourceService sources)
    {
        _dbContext = dbContext;
        _outbox = outbox;
        _sources = sources;
    }

    public async Task<WebsiteLeadSubmissionResponse> SubmitAsync(WebsiteLeadSubmissionRequest request, CancellationToken cancellationToken)
    {
        Validate(request);
        var email = NormalizeEmail(request.Email);
        var tenantKey = NormalizeTenantKey(request.TenantKey);
        var externalSubmissionId = NormalizeOptional(request.ExternalSubmissionId, 256);
        var now = DateTime.UtcNow;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

        var policy = await _dbContext.SalesAutomationPolicies.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.WebsiteLeadFormKey == tenantKey, cancellationToken);
        if (policy is null)
        {
            throw new SalesValidationException(new Dictionary<string, string[]> { [nameof(WebsiteLeadSubmissionRequest.TenantKey)] = ["A valid website form key is required."] });
        }

        var companyId = policy.CompanyId;
        if (!string.IsNullOrWhiteSpace(externalSubmissionId))
        {
            var existingSubmission = await _dbContext.WebsiteLeadSubmissions.IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.ExternalSubmissionId == externalSubmissionId)
                .OrderByDescending(x => x.ReceivedUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (existingSubmission is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return new WebsiteLeadSubmissionResponse(
                    "accepted",
                    existingSubmission.ReceivedUtc,
                    existingSubmission.LeadId,
                    Deduplicated: true,
                    EnrollmentAccepted: existingSubmission.SequenceExecutionId.HasValue,
                    existingSubmission.FollowUpSequenceId,
                    existingSubmission.SequenceExecutionId);
            }
        }

        var cutoff = now.AddMinutes(-policy.WebsiteLeadDeduplicationWindowMinutes);
        var duplicate = await _dbContext.WebsiteLeadSubmissions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.NormalizedEmail == email && x.ReceivedUtc >= cutoff && x.Status != "merged")
            .OrderByDescending(x => x.ReceivedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var submission = new WebsiteLeadSubmission(
            Guid.NewGuid(),
            companyId,
            email,
            NormalizeOptional(request.Name, 160),
            NormalizeOptional(request.CompanyName, 200),
            NormalizeOptional(request.Message, 2000),
            NormalizeOptional(request.SourceUrl, 512),
            NormalizeOptional(request.FormId, 120),
            NormalizeOptional(request.Phone, 64),
            externalSubmissionId,
            BuildSourceMetadataJson(request));
        _dbContext.WebsiteLeadSubmissions.Add(submission);

        var contact = await _dbContext.Contacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Email == email && !x.IsDeleted, cancellationToken);
        if (contact is null)
        {
            contact = new Contact(
                Guid.NewGuid(),
                companyId,
                string.IsNullOrWhiteSpace(request.Name) ? email : request.Name!,
                email,
                phone: request.Phone);
            _dbContext.Contacts.Add(contact);
        }

        Lead lead;
        if (duplicate is not null)
        {
            submission.MarkMerged(duplicate.Id);
            lead = duplicate.LeadId.HasValue
                ? await _dbContext.Leads.IgnoreQueryFilters()
                    .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == duplicate.LeadId.Value && !x.IsDeleted, cancellationToken)
                    ?? await FindLeadByWebsiteEmailAsync(companyId, email, cancellationToken)
                : await FindLeadByWebsiteEmailAsync(companyId, email, cancellationToken);

            if (lead is null)
            {
                lead = CreateLead(companyId, contact.Id, request);
                _dbContext.Leads.Add(lead);
            }

            lead.ApplyWebsiteSubmission(submission.Id, email, request.Message);
            submission.LinkLead(lead.Id, contact.Id);
            submission.MarkExistingLeadUpdated();
            _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.System, "website-form", "sales.website_lead.deduplicated", "website_lead_submission", submission.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Duplicate website lead submission updated the existing lead.", now));
        }
        else
        {
            lead = CreateLead(companyId, contact.Id, request);
            lead.ApplyWebsiteSubmission(submission.Id, email, request.Message);
            submission.LinkLead(lead.Id, contact.Id);
            _dbContext.Leads.Add(lead);
            _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.System, "website-form", "sales.website_lead.submitted", "website_lead_submission", submission.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Website lead was accepted.", now));
        }

        Guid? sequenceExecutionId = null;

        string? Utm(string key) => request.Utm is not null && request.Utm.TryGetValue(key, out var value) ? value : null;
        await _sources.StageAsync(companyId, new RecordSalesSourceTouchRequest(
            "lead", lead.Id, SalesSourceCategories.Website, "virtual_company_website", "web_form", "inquiry",
            externalSubmissionId ?? $"website-submission:{submission.Id:D}", submission.ReceivedUtc, "visitor", email,
            Evidence: string.IsNullOrWhiteSpace(request.Message) ? "Public website inquiry submitted." : request.Message,
            LandingPage: request.SourceUrl, Referrer: request.Referrer, UtmSource: Utm("source"), UtmMedium: Utm("medium"),
            UtmCampaign: Utm("campaign"), UtmContent: Utm("content"), UtmTerm: Utm("term"),
            MetadataJson: BuildSourceMetadataJson(request), IsConversion: true), cancellationToken);

        var permission = await _dbContext.SalesContactPermissions.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.ContactId == contact.Id && x.Channel == "email" && x.Address == email, cancellationToken);
        var permissionStatus = request.ContactConsent ? "granted" : "not_granted";
        var legalBasis = request.ContactConsent ? (request.ConsentLegalBasis ?? "consent") : "none";
        if (permission is null) _dbContext.SalesContactPermissions.Add(new SalesContactPermission(Guid.NewGuid(), companyId, contact.Id, "email", email, permissionStatus, legalBasis, $"website-submission:{submission.Id:D}", now));
        else if (request.ContactConsent) permission.Update(permissionStatus, legalBasis, $"website-submission:{submission.Id:D}", now);

        if (request.ContactConsent && policy.WebsiteLeadFollowUpSequenceId is Guid sequenceId)
        {
            sequenceExecutionId = await EnrollFollowUpSequenceAsync(companyId, sequenceId, contact, submission, lead, now, cancellationToken);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new WebsiteLeadSubmissionResponse(
            "accepted",
            submission.ReceivedUtc,
            lead.Id,
            duplicate is not null,
            sequenceExecutionId.HasValue,
            policy.WebsiteLeadFollowUpSequenceId,
            sequenceExecutionId);
    }

    private async Task<Guid?> EnrollFollowUpSequenceAsync(Guid companyId, Guid sequenceId, Contact contact, WebsiteLeadSubmission submission, Lead lead, DateTime now, CancellationToken cancellationToken)
    {
        var sequence = await _dbContext.SalesSequences.IgnoreQueryFilters()
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sequenceId && x.Status == SalesStatuses.Active, cancellationToken);
        if (sequence is null || sequence.Steps.Count == 0)
        {
            _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.System, "website-form", "sales.website_lead.enrollment_skipped", "lead", lead.Id.ToString("D"), AuditEventOutcomes.Failed, "No active follow-up sequence is configured for website leads.", now));
            return null;
        }

        var campaign = await _dbContext.SalesCampaigns.IgnoreQueryFilters()
            .Include(x => x.Contacts)
            .Where(x => x.CompanyId == companyId && x.SalesSequenceId == sequenceId && x.AudienceType == "website_leads" && x.Status == SalesStatuses.Active)
            .OrderByDescending(x => x.CreatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (campaign is null)
        {
            campaign = new SalesCampaign(Guid.NewGuid(), companyId, sequenceId, "Website lead follow-up", "website_leads");
            campaign.SetPolicy(outboundEnabled: true, maxEmailsPerDay: 50, approvalRequired: false);
            campaign.RequestLaunch();
            _dbContext.SalesCampaigns.Add(campaign);
        }

        var campaignContact = await _dbContext.SalesCampaignContacts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SalesCampaignId == campaign.Id && x.ContactId == contact.Id, cancellationToken);
        if (campaignContact is null)
        {
            campaignContact = new SalesCampaignContact(Guid.NewGuid(), companyId, campaign.Id, contact.Id);
            _dbContext.SalesCampaignContacts.Add(campaignContact);
        }

        var existingExecution = await _dbContext.SalesSequenceExecutions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SalesCampaignId == campaign.Id && x.ContactId == contact.Id && x.Status != SalesStatuses.Stopped && x.Status != SalesStatuses.Completed, cancellationToken);
        if (existingExecution is not null)
        {
            submission.RecordSequenceEnrollment(sequenceId, existingExecution.Id);
            return existingExecution.Id;
        }

        var execution = new SalesSequenceExecution(Guid.NewGuid(), companyId, campaign.Id, campaignContact.Id, contact.Id);
        foreach (var sequenceStep in sequence.Steps.OrderBy(x => x.StepOrder))
        {
            var scheduled = now.AddDays(sequenceStep.DelayDays);
            var executionStep = new SalesSequenceExecutionStep(
                Guid.NewGuid(),
                companyId,
                execution.Id,
                campaign.Id,
                contact.Id,
                sequenceStep.Id,
                sequenceStep.StepOrder,
                scheduled,
                $"website-lead-sequence:{companyId:N}:{lead.Id:N}:{sequenceStep.Id:N}");
            executionStep.RecordGeneratedDraft(
                sequenceStep.TemplateSubject ?? "Following up on your enquiry",
                sequenceStep.TemplateContent,
                now);
            execution.Steps.Add(executionStep);
        }

        campaignContact.MarkScheduled(sequence.Steps.Min(x => x.StepOrder), execution.Steps.Min(x => x.ScheduledSendUtc));
        _dbContext.SalesSequenceExecutions.Add(execution);
        submission.RecordSequenceEnrollment(sequenceId, execution.Id);

        var eventId = $"website-lead:{companyId:N}:{submission.Id:N}:sequence-enrolled";
        _outbox.Enqueue(
            companyId,
            "sales.website_lead.sequence_enrolled",
            new PlatformEventEnvelope(
                eventId,
                "sales.website_lead.sequence_enrolled",
                now,
                companyId,
                eventId,
                "website_lead_submission",
                submission.Id.ToString("D"),
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["companyId"] = JsonValue.Create(companyId),
                    ["submissionId"] = JsonValue.Create(submission.Id),
                    ["leadId"] = JsonValue.Create(lead.Id),
                    ["contactId"] = JsonValue.Create(contact.Id),
                    ["sequenceId"] = JsonValue.Create(sequenceId),
                    ["sequenceExecutionId"] = JsonValue.Create(execution.Id),
                    ["policyContext"] = JsonSerializer.SerializeToNode(new
                    {
                        outboundPolicy = "sales_automation_policy",
                        approvalChecksApplyAtSendTime = true
                    })
                }),
            eventId,
            now,
            $"website-lead-sequence-enrolled:{companyId:N}:{submission.Id:N}",
            causationId: submission.Id.ToString("D"));

        _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, AuditActorTypes.System, "website-form", "sales.website_lead.enrolled", "sales_sequence_execution", execution.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Website lead was enrolled in the follow-up sequence.", now));
        return execution.Id;
    }

    private static string NormalizeTenantKey(string tenantKey)
    {
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw new SalesValidationException(new Dictionary<string, string[]> { [nameof(WebsiteLeadSubmissionRequest.TenantKey)] = ["A valid website form key is required."] });
        }

        return tenantKey.Trim();
    }

    private static Lead CreateLead(Guid companyId, Guid contactId, WebsiteLeadSubmissionRequest request) =>
        new(
            Guid.NewGuid(),
            companyId,
            string.IsNullOrWhiteSpace(request.CompanyName)
                ? $"Website enquiry from {NormalizeEmail(request.Email)}"
                : $"Website enquiry from {NormalizeOptional(request.CompanyName, 200)}",
            SalesPipelineStage.NewStageId,
            source: "website_form",
            primaryContactId: contactId);

    private async Task<Lead?> FindLeadByWebsiteEmailAsync(Guid companyId, string normalizedEmail, CancellationToken cancellationToken) =>
        await _dbContext.Leads.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.WebsiteSubmissionEmail == normalizedEmail && !x.IsDeleted && x.Status != SalesStatuses.Converted && x.Status != SalesStatuses.Rejected)
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new SalesValidationException(new Dictionary<string, string[]> { [nameof(WebsiteLeadSubmissionRequest.Email)] = ["A valid email address is required."] });
        }

        var normalized = email.Trim().ToLowerInvariant();
        try
        {
            var parsed = new MailAddress(normalized);
            if (!string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException();
            }
        }
        catch (FormatException)
        {
            throw new SalesValidationException(new Dictionary<string, string[]> { [nameof(WebsiteLeadSubmissionRequest.Email)] = ["A valid email address is required."] });
        }

        return normalized;
    }

    private static void Validate(WebsiteLeadSubmissionRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        AddLengthError(errors, nameof(request.Name), request.Name, 160);
        AddLengthError(errors, nameof(request.CompanyName), request.CompanyName, 200);
        AddLengthError(errors, nameof(request.Phone), request.Phone, 64);
        AddLengthError(errors, nameof(request.Message), request.Message, 2000);
        AddLengthError(errors, nameof(request.SourceUrl), request.SourceUrl, 512);
        AddLengthError(errors, nameof(request.FormId), request.FormId, 120);
        AddLengthError(errors, nameof(request.ExternalSubmissionId), request.ExternalSubmissionId, 256);
        if (errors.Count > 0)
        {
            throw new SalesValidationException(errors);
        }
    }

    private static void AddLengthError(Dictionary<string, string[]> errors, string field, string? value, int maxLength)
    {
        if (!string.IsNullOrWhiteSpace(value) && value.Trim().Length > maxLength)
        {
            errors[field] = [$"This field must be {maxLength} characters or fewer."];
        }
    }

    private static string? NormalizeOptional(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > maxLength ? value.Trim()[..maxLength] : value.Trim();

    private static string? BuildSourceMetadataJson(WebsiteLeadSubmissionRequest request) =>
        request.Utm is null && request.Metadata is null ? null : JsonSerializer.Serialize(new { utm = request.Utm, metadata = request.Metadata });
}
