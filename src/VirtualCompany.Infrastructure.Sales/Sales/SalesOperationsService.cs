using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.CustomerMemory;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesOperationsService : ISalesOperationsService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IMailboxProviderRegistry _mailboxProviderRegistry;
    private readonly IMailboxOAuthAccessTokenLeaseService _tokenLeaseService;
    private readonly ICompanyOutboxEnqueuer _outbox;
    private readonly IFinanceAccountingActionService _financeAccountingActions;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly ISalesAutomationPolicyEvaluator _policyEvaluator;
    private readonly TimeProvider _timeProvider;
    private readonly ICustomerMemoryService _customerMemory;

    public SalesOperationsService(VirtualCompanyDbContext dbContext, IMailboxProviderRegistry mailboxProviderRegistry, IMailboxOAuthAccessTokenLeaseService tokenLeaseService, ICompanyOutboxEnqueuer outbox, IFinanceAccountingActionService financeAccountingActions, IApprovalRequestService approvalRequestService, ISalesAutomationPolicyEvaluator policyEvaluator, TimeProvider timeProvider, ICustomerMemoryService customerMemory)
    {
        _dbContext = dbContext;
        _mailboxProviderRegistry = mailboxProviderRegistry;
        _tokenLeaseService = tokenLeaseService;
        _outbox = outbox;
        _financeAccountingActions = financeAccountingActions;
        _approvalRequestService = approvalRequestService;
        _policyEvaluator = policyEvaluator;
        _timeProvider = timeProvider;
        _customerMemory = customerMemory;
    }

    public async Task<SalesDashboardResponse> GetDashboardAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);

        var leads = await LeadQuery(companyId).ToListAsync(cancellationToken);
        var deals = await DealQuery(companyId).ToListAsync(cancellationToken);
        var recommendations = await RecommendationQuery(companyId).Take(5).ToListAsync(cancellationToken);
        var recentActivity = await ActivityQuery(companyId).Take(10).ToListAsync(cancellationToken);

        var pipelineValue = deals.Where(x => x.Status == SalesStatuses.Open).Sum(x => x.Amount);
        var forecastRevenue = deals.Where(x => x.Status == SalesStatuses.Open).Sum(x => x.Amount * ForecastWeight(x.PipelineStageId));
        var attentionThreshold = _timeProvider.GetUtcNow().UtcDateTime.AddDays(-7);
        var dealsRequiringAction = deals
            .Where(x => x.Status == SalesStatuses.Open && x.UpdatedUtc <= attentionThreshold)
            .OrderBy(x => x.ExpectedCloseUtc ?? DateTime.MaxValue)
            .ThenBy(x => x.UpdatedUtc)
            .Take(5)
            .Select(MapDealSummary)
            .ToList();

        return new SalesDashboardResponse(
            pipelineValue,
            deals.FirstOrDefault()?.Currency ?? leads.FirstOrDefault()?.Currency ?? "USD",
            leads.Count(x => x.Status == SalesStatuses.Open),
            leads.Count(x => ResolveTemperature(x) == "Hot"),
            deals.Count(x => x.Status == SalesStatuses.Open && x.UpdatedUtc <= attentionThreshold),
            Math.Round(forecastRevenue, 2),
            dealsRequiringAction,
            recommendations.Select(MapRecommendation).ToList(),
            recentActivity.Select(MapActivity).ToList());
    }

    public async Task<IReadOnlyList<SalesLeadSummaryResponse>> ListLeadsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        return await LeadQuery(companyId)
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => MapLeadSummary(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesLeadDetailResponse?> GetLeadAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureId(leadId, nameof(leadId));

        var lead = await LeadQuery(companyId).SingleOrDefaultAsync(x => x.Id == leadId, cancellationToken);
        return lead is null ? null : MapLeadDetail(lead);
    }

    public async Task<SalesLeadDetailResponse?> QualifyLeadAsync(Guid companyId, Guid userId, Guid leadId, SalesActionRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var lead = await MutableLeadAsync(companyId, leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        lead.Qualify(qualifiedByUserId: userId);
        _dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "qualification", BuildSummary(request.Note, "Lead qualified by Alex."), DateTime.UtcNow, leadId: lead.Id, contactId: lead.PrimaryContactId, customerCompanyId: lead.CustomerCompanyId));
        AddAudit(companyId, userId, AuditEventActions.SalesLeadQualified, "lead", lead.Id, AuditEventOutcomes.Succeeded, request.Note ?? "Alex qualified the lead for follow-up.");
        EnqueueSalesEvent(companyId, CompanyOutboxTopics.SalesLeadQualified, "lead", lead.Id, new { leadId = lead.Id, lead.Title, lead.Status });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetLeadAsync(companyId, leadId, cancellationToken);
    }

    public async Task<SalesLeadDetailResponse?> UpdateLeadQualificationAsync(Guid companyId, Guid userId, Guid leadId, UpdateLeadQualificationRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var lead = await MutableLeadAsync(companyId, leadId, cancellationToken);
        if (lead is null) return null;

        lead.Qualify(request.Fit, request.Temperature, request.Priority, request.SuggestedNextAction, userId);
        _dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "qualification", BuildSummary(request.Note, "Lead qualification updated."), DateTime.UtcNow, leadId: lead.Id, contactId: lead.PrimaryContactId, customerCompanyId: lead.CustomerCompanyId));
        AddAudit(companyId, userId, AuditEventActions.SalesLeadQualified, "lead", lead.Id, AuditEventOutcomes.Succeeded, request.Note ?? "Alex updated the lead qualification.", new Dictionary<string, string?> { ["fit"] = lead.Fit, ["temperature"] = lead.Temperature, ["priority"] = lead.Priority, ["suggestedNextAction"] = lead.SuggestedNextAction });
        EnqueueSalesEvent(companyId, CompanyOutboxTopics.SalesLeadQualified, "lead", lead.Id, new { leadId = lead.Id, lead.Title, lead.Fit, lead.Temperature, lead.Priority, lead.SuggestedNextAction });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetLeadAsync(companyId, leadId, cancellationToken);
    }

    public async Task<SalesLeadDetailResponse?> RejectLeadAsync(Guid companyId, Guid userId, Guid leadId, SalesActionRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var lead = await MutableLeadAsync(companyId, leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        lead.Reject();
        _dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "review", BuildSummary(request.Note, "Lead rejected after review."), DateTime.UtcNow, leadId: lead.Id, contactId: lead.PrimaryContactId, customerCompanyId: lead.CustomerCompanyId));
        AddAudit(companyId, userId, AuditEventActions.SalesLeadRejected, "lead", lead.Id, AuditEventOutcomes.Rejected, request.Note ?? "The lead was not a fit right now.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetLeadAsync(companyId, leadId, cancellationToken);
    }

    public async Task<SalesDealDetailResponse?> ConvertLeadAsync(Guid companyId, Guid userId, Guid leadId, ConvertLeadRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        ValidateConvert(request);

        var lead = await MutableLeadAsync(companyId, leadId, cancellationToken);
        if (lead is null)
        {
            return null;
        }

        if (lead.Status == SalesStatuses.Converted)
        {
            throw Validation(nameof(leadId), "This lead has already been converted.");
        }

        var deal = new Deal(
            Guid.NewGuid(),
            companyId,
            lead.Title,
            SalesPipelineStage.QualifiedStageId,
            request.Amount,
            request.Currency,
            sourceLeadId: lead.Id,
            primaryContactId: lead.PrimaryContactId,
            customerCompanyId: lead.CustomerCompanyId,
            expectedCloseUtc: request.ExpectedCloseUtc);
        lead.ConvertToDeal(deal.Id);
        _dbContext.Deals.Add(deal);
        _dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "conversion", BuildSummary(request.Note, "Lead converted to a sales deal."), DateTime.UtcNow, leadId: lead.Id, dealId: deal.Id, contactId: lead.PrimaryContactId, customerCompanyId: lead.CustomerCompanyId));
        AddAudit(companyId, userId, AuditEventActions.SalesLeadConverted, "deal", deal.Id, AuditEventOutcomes.Succeeded, request.Note ?? "The qualified lead was converted to a deal.");
        if (deal.PrimaryContactId is Guid contactId)
        {
            EnqueueSalesEvent(companyId, CompanyOutboxTopics.SalesDealCreated, "deal", deal.Id, new { dealId = deal.Id, contactId, leadId = lead.Id, deal.Title });
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetDealAsync(companyId, deal.Id, cancellationToken);
    }

    public async Task<SalesPipelineResponse> GetPipelineAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var stages = await _dbContext.SalesPipelineStages
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive && (x.CompanyId == SalesPipelineStage.SystemCompanyId || x.CompanyId == companyId))
            .OrderBy(x => x.DisplayOrder)
            .ToListAsync(cancellationToken);

        var deals = await DealQuery(companyId).Where(x => x.Status == SalesStatuses.Open).ToListAsync(cancellationToken);
        var response = stages.Select(stage =>
        {
            var stageDeals = deals.Where(x => x.PipelineStageId == stage.Id).OrderByDescending(x => x.UpdatedUtc).ToList();
            return new SalesPipelineStageResponse(
                stage.Id,
                stage.Name,
                stage.DisplayOrder,
                stageDeals.Sum(x => x.Amount),
                stageDeals.Count,
                stageDeals.Select(MapDealSummary).ToList());
        }).ToList();
        return new SalesPipelineResponse(response);
    }

    public async Task<SalesDealDetailResponse?> GetDealAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureId(dealId, nameof(dealId));

        var deal = await DealQuery(companyId).SingleOrDefaultAsync(x => x.Id == dealId, cancellationToken);
        if (deal is null)
        {
            return null;
        }

        var memory = deal.PrimaryContactId.HasValue ? await _customerMemory.GetContextAsync(companyId, deal.PrimaryContactId.Value, cancellationToken) : null;
        return MapDealDetail(deal, memory);
    }

    public async Task<IReadOnlyList<SalesActivityResponse>> ListDealActivitiesAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        var exists = await _dbContext.Deals.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == dealId && !x.IsDeleted, cancellationToken);
        if (!exists)
        {
            return [];
        }

        return await ActivityQuery(companyId)
            .Where(x => x.DealId == dealId)
            .OrderByDescending(x => x.OccurredUtc)
            .Select(x => MapActivity(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SalesEmailTimelineResponse>> ListDealEmailsAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureId(dealId, nameof(dealId));

        var exists = await _dbContext.Deals.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == dealId && !x.IsDeleted, cancellationToken);
        if (!exists)
        {
            return [];
        }

        return await _dbContext.SalesEmailLinks.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DealId == dealId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedUtc)
            .Select(x => new SalesEmailTimelineResponse(x.Id, x.ExternalMessageId, StatusLabel(x.Status), x.DetectedIntent, x.ProductOrServiceInterest, x.Confidence, x.Rationale, x.CreatedUtc, x.LeadId, x.DealId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SalesRecommendationResponse>> ListRecommendationsAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        return await RecommendationQuery(companyId)
            .Take(50)
            .Select(x => MapRecommendation(x))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesDealDetailResponse?> ChangeDealStageAsync(Guid companyId, Guid userId, Guid dealId, ChangeDealStageRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        if (request.StageId == Guid.Empty)
        {
            throw Validation(nameof(request.StageId), "Stage is required.");
        }

        if (!await StageExistsAsync(companyId, request.StageId, cancellationToken))
        {
            throw Validation(nameof(request.StageId), "Stage is not available for this company.");
        }

        var deal = await MutableDealAsync(companyId, dealId, cancellationToken);
        if (deal is null)
        {
            return null;
        }

        var previousStage = deal.PipelineStageId;
        deal.ChangeStage(request.StageId);
        _dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "stage change", BuildSummary(request.Note, "Deal moved to a new stage."), DateTime.UtcNow, dealId: deal.Id, contactId: deal.PrimaryContactId, customerCompanyId: deal.CustomerCompanyId));
        AddAudit(companyId, userId, AuditEventActions.SalesDealStageChanged, "deal", deal.Id, AuditEventOutcomes.Succeeded, request.Note ?? "Deal stage changed.", new Dictionary<string, string?> { ["previousStageId"] = previousStage.ToString("D"), ["newStageId"] = request.StageId.ToString("D") });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetDealAsync(companyId, dealId, cancellationToken);
    }

    public async Task<SalesDealDetailResponse?> MarkDealWonAsync(Guid companyId, Guid userId, Guid dealId, SalesActionRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var deal = await MutableDealAsync(companyId, dealId, cancellationToken);
        if (deal is null)
        {
            return null;
        }

        deal.MarkWon();
        _dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "won", BuildSummary(request.Note, "Deal marked won."), DateTime.UtcNow, dealId: deal.Id, contactId: deal.PrimaryContactId, customerCompanyId: deal.CustomerCompanyId));
        await EnsureFinanceHandoffAsync(companyId, userId, deal, cancellationToken);
        AddAudit(companyId, userId, AuditEventActions.SalesDealWon, "deal", deal.Id, AuditEventOutcomes.Succeeded, request.Note ?? "The deal was won.");
        EnqueueSalesEvent(companyId, CompanyOutboxTopics.SalesDealWon, "deal", deal.Id, new { dealId = deal.Id, deal.Title, deal.Amount, deal.Currency });
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetDealAsync(companyId, dealId, cancellationToken);
    }

    public async Task<IReadOnlyList<SalesRecommendationResponse>> DetectFollowUpRecommendationsAsync(Guid companyId, Guid userId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var policy = await GetOrCreatePolicyAsync(companyId, cancellationToken);
        var now = DateTime.UtcNow;
        var created = new List<SalesAgentRecommendation>();

        var hotIdleLeads = await _dbContext.Leads.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Status == SalesStatuses.Qualified && x.UpdatedUtc <= now.AddDays(-3))
            .ToListAsync(cancellationToken);
        foreach (var lead in hotIdleLeads)
        {
            var decision = _policyEvaluator.Evaluate(policy.Mode, SalesRecommendationActions.CreateDraftReply, SalesRecommendationRiskLevels.Medium);
            var recommendation = await AddRecommendationIfMissingAsync(companyId, userId, $"hot-lead-idle:{lead.Id:N}", lead.Id, null, "follow_up", "hot_lead_idle", SalesRecommendationActions.CreateDraftReply, SalesRecommendationRiskLevels.Medium, "Create a draft reply", "Alex noticed this qualified lead has been idle for three days.", decision, cancellationToken);
            if (recommendation is not null) created.Add(recommendation);
        }

        var stuckDeals = await _dbContext.Deals.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Status == SalesStatuses.Open && x.UpdatedUtc <= now.AddDays(-7))
            .ToListAsync(cancellationToken);
        foreach (var deal in stuckDeals)
        {
            var decision = _policyEvaluator.Evaluate(policy.Mode, SalesRecommendationActions.SendEmail, SalesRecommendationRiskLevels.Low);
            var recommendation = await AddRecommendationIfMissingAsync(companyId, userId, $"stuck-deal:{deal.Id:N}", null, deal.Id, "follow_up", "stuck_deal", SalesRecommendationActions.SendEmail, SalesRecommendationRiskLevels.Low, "Send a follow-up email", "Alex noticed this deal has been quiet for a week.", decision, cancellationToken);
            if (recommendation is not null) created.Add(recommendation);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return created.Select(MapRecommendation).ToList();
    }

    public async Task<SalesRecommendationResponse?> ApproveRecommendationAsync(Guid companyId, Guid userId, Guid recommendationId, SalesActionRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var recommendation = await MutableRecommendationAsync(companyId, recommendationId, cancellationToken);
        if (recommendation is null) return null;
        recommendation.MarkApproved();
        AddAudit(companyId, userId, "sales.recommendation.approved", "sales_recommendation", recommendation.Id, AuditEventOutcomes.Succeeded, request.Note ?? recommendation.Rationale);
        await ExecuteRecommendationAsync(companyId, userId, recommendation, request.Note, false, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapRecommendation(recommendation);
    }

    public async Task<SalesRecommendationResponse?> RetryRecommendationAsync(Guid companyId, Guid userId, Guid recommendationId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var recommendation = await MutableRecommendationAsync(companyId, recommendationId, cancellationToken);
        if (recommendation is null) return null;
        recommendation.MarkRetrying();
        AddAudit(companyId, userId, "sales.recommendation.retry_attempted", "sales_recommendation", recommendation.Id, AuditEventOutcomes.Succeeded, "Alex retried the sales recommendation with the original idempotency key.");
        await ExecuteRecommendationAsync(companyId, userId, recommendation, null, true, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapRecommendation(recommendation);
    }

    private async Task ExecuteRecommendationAsync(Guid companyId, Guid userId, SalesAgentRecommendation recommendation, string? note, bool isRetry, CancellationToken cancellationToken)
    {
        recommendation.EnsureExecutionKey();
        if (recommendation.HasSucceeded)
        {
            return;
        }

        if (recommendation.ActionType is not SalesRecommendationActions.CreateDraftReply and not SalesRecommendationActions.SendEmail)
        {
            throw Validation(nameof(recommendation.ActionType), "Only email draft and email send recommendations can be executed.");
        }

        var context = await ResolveReplyContextAsync(companyId, recommendation, cancellationToken);
        recommendation.MarkExecuting(context.MailboxConnection.Id, context.MailboxConnection.Provider.ToStorageValue(), context.ThreadId);
        AddAudit(companyId, userId, isRetry ? "sales.recommendation.retry_started" : "sales.recommendation.execution_started", "sales_recommendation", recommendation.Id, AuditEventOutcomes.Started, "Alex started the approved follow-up email action.");

        try
        {
            var provider = _mailboxProviderRegistry.Resolve(context.MailboxConnection.Provider);
            var accessToken = (await _tokenLeaseService.AcquireAsync(
                companyId, context.MailboxConnection.Id, provider.ReplyRequiredScopes, cancellationToken)).AccessToken;
            var request = new MailboxReplyExecutionRequest(
                companyId,
                context.MailboxConnection.Id,
                context.MailboxConnection.Provider.ToStorageValue(),
                context.OriginalMessageId,
                context.ThreadId,
                context.InternetMessageId,
                context.RecipientEmail,
                context.RecipientName,
                context.Subject,
                BuildFollowUpBody(recommendation, note),
                recommendation.ExecutionIdempotencyKey);

            var result = recommendation.ActionType == SalesRecommendationActions.SendEmail
                ? await provider.SendReplyAsync(accessToken, request, cancellationToken)
                : await provider.CreateDraftReplyAsync(accessToken, request, cancellationToken);

            var existingActivityId = recommendation.ActivityId ??
                await FindExecutionActivityIdAsync(companyId, recommendation.Id, cancellationToken);
            var activityId = existingActivityId ?? Guid.NewGuid();
            if (existingActivityId is null)
            {
                var activityType = recommendation.ActionType == SalesRecommendationActions.SendEmail ? "email_sent" : "email_draft_created";
                var summary = recommendation.ActionType == SalesRecommendationActions.SendEmail
                    ? "Alex sent the approved follow-up reply."
                    : "Alex created the approved draft reply.";
                _dbContext.SalesActivities.Add(new SalesActivity(activityId, companyId, activityType, summary, _timeProvider.GetUtcNow().UtcDateTime, recommendation.LeadId, recommendation.DealId, context.ContactId, context.CustomerCompanyId));
            }

            if (recommendation.ActionType == SalesRecommendationActions.SendEmail)
            {
                recommendation.MarkSent(result.ProviderMessageId, result.ProviderThreadId, activityId);
            }
            else
            {
                recommendation.MarkDraftCreated(result.ProviderDraftId ?? result.ProviderMessageId, result.ProviderThreadId, activityId);
            }

            AddAudit(companyId, userId, isRetry ? "sales.recommendation.retry_succeeded" : "sales.recommendation.execution_succeeded", "sales_recommendation", recommendation.Id, AuditEventOutcomes.Succeeded, "The approved follow-up email action completed.", new Dictionary<string, string?> { ["provider"] = context.MailboxConnection.Provider.ToStorageValue(), ["providerThreadId"] = recommendation.ProviderThreadId, ["providerMessageId"] = recommendation.ProviderMessageId, ["providerDraftId"] = recommendation.ProviderDraftId });
        }
        catch (MailboxProviderExecutionException ex)
        {
            recommendation.MarkFailed(ex.Code, ex.Message, ex.IsRetryable);
            AddAudit(companyId, userId, isRetry ? "sales.recommendation.retry_failed" : "sales.recommendation.execution_failed", "sales_recommendation", recommendation.Id, AuditEventOutcomes.Failed, ex.Message, new Dictionary<string, string?> { ["errorCode"] = ex.Code, ["retryable"] = ex.IsRetryable.ToString() });
        }
        catch (HttpRequestException ex)
        {
            recommendation.MarkFailed("email_provider_transient_failure", "Email provider could not be reached. You can retry this action.", true);
            AddAudit(companyId, userId, "sales.recommendation.execution_failed", "sales_recommendation", recommendation.Id, AuditEventOutcomes.Failed, ex.Message);
        }
    }

    public async Task<SalesAutomationPolicyResponse> GetAutomationPolicyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        return MapPolicy(await GetOrCreatePolicyAsync(companyId, cancellationToken));
    }

    public async Task<SalesAutomationPolicyResponse> UpdateAutomationPolicyAsync(Guid companyId, Guid userId, UpdateSalesAutomationPolicyRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var policy = await GetOrCreatePolicyAsync(companyId, cancellationToken);
        policy.UpdateMode(request.Mode);
        AddAudit(companyId, userId, "sales.automation_policy.updated", "sales_automation_policy", policy.Id, AuditEventOutcomes.Succeeded, "Sales automation policy updated.", new Dictionary<string, string?> { ["mode"] = policy.Mode, ["financeDocumentsAlwaysRequireApproval"] = policy.FinanceDocumentsAlwaysRequireApproval.ToString() });
        policy.UpdateOutboundSettings(
            request.OutboundEnabled ?? policy.OutboundEnabled,
            request.MaxEmailsPerDay ?? policy.MaxEmailsPerDay,
            request.RequireApprovalFirstContact ?? policy.RequireApprovalFirstContact,
            request.RequireApprovalPricingDiscussion ?? policy.RequireApprovalPricingDiscussion,
            request.RequireApprovalFollowUps ?? policy.RequireApprovalFollowUps,
            request.RequireApprovalReEngagement ?? policy.RequireApprovalReEngagement,
            request.WebsiteLeadDeduplicationWindowMinutes ?? policy.WebsiteLeadDeduplicationWindowMinutes,
            request.WebsiteLeadFollowUpSequenceId ?? policy.WebsiteLeadFollowUpSequenceId);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapPolicy(policy);
    }

    public async Task<SalesFinanceHandoffResponse?> GetFinanceHandoffAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        return await _dbContext.SalesFinanceHandoffs.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.DealId == dealId)
            .Select(x => MapFinanceHandoff(x))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<SalesFinanceHandoffResponse?> ApproveFinanceHandoffAsync(Guid companyId, Guid userId, Guid dealId, SalesActionRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var handoff = await _dbContext.SalesFinanceHandoffs.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DealId == dealId, cancellationToken);
        if (handoff is null)
        {
            return null;
        }

        if (handoff.HasExternalDocument)
        {
            return MapFinanceHandoff(handoff);
        }

        if (handoff.ApprovalId is not Guid approvalId)
        {
            throw Validation(nameof(dealId), "No finance approval request was found for this won deal.");
        }

        handoff.MarkApproved();
        AddAudit(companyId, userId, "sales.finance_handoff.approved", "sales_finance_handoff", handoff.Id, AuditEventOutcomes.Succeeded, request.Note ?? "Finance draft creation approved for the won deal.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        handoff.MarkExecutionStarted();
        AddAudit(companyId, userId, "sales.finance_handoff.execution_started", "sales_finance_handoff", handoff.Id, AuditEventOutcomes.Started, "Laura started the approved Finance document action.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        var decision = await _approvalRequestService.DecideAsync(
            companyId,
            new ApprovalDecisionCommand(approvalId, "approve", Comment: request.Note),
            cancellationToken);

        if (!decision.IsFinalized)
        {
            return MapFinanceHandoff(handoff);
        }

        await RefreshFinanceHandoffFromWriteCommandAsync(companyId, userId, handoff, isRetry: false, cancellationToken);
        return MapFinanceHandoff(handoff);
    }

    public async Task<SalesFinanceHandoffResponse?> RetryFinanceHandoffAsync(Guid companyId, Guid userId, Guid dealId, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var handoff = await _dbContext.SalesFinanceHandoffs.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DealId == dealId, cancellationToken);
        if (handoff is null)
        {
            return null;
        }

        if (handoff.HasExternalDocument)
        {
            return MapFinanceHandoff(handoff);
        }

        if (!handoff.CanRetry || handoff.WriteRequestId is not Guid writeRequestId)
        {
            throw Validation(nameof(dealId), "Only failed finance handoffs can be retried.");
        }

        handoff.MarkRetrying();
        AddAudit(companyId, userId, "sales.finance_handoff.retried", "sales_finance_handoff", handoff.Id, AuditEventOutcomes.Started, "Laura retried the approved Finance document action with the original idempotency key.");
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _financeAccountingActions.RetryApprovedAsync(companyId, writeRequestId, cancellationToken);
        await RefreshFinanceHandoffFromWriteCommandAsync(companyId, userId, handoff, isRetry: true, cancellationToken);
        return MapFinanceHandoff(handoff);
    }

    public async Task<SalesDealDetailResponse?> MarkDealLostAsync(Guid companyId, Guid userId, Guid dealId, SalesActionRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        var deal = await MutableDealAsync(companyId, dealId, cancellationToken);
        if (deal is null)
        {
            return null;
        }

        deal.MarkLost();
        _dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "lost", BuildSummary(request.Note, "Deal marked lost."), DateTime.UtcNow, dealId: deal.Id, contactId: deal.PrimaryContactId, customerCompanyId: deal.CustomerCompanyId));
        AddAudit(companyId, userId, AuditEventActions.SalesDealLost, "deal", deal.Id, AuditEventOutcomes.Rejected, request.Note ?? "The deal was lost.");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetDealAsync(companyId, dealId, cancellationToken);
    }

    public async Task<ProcessSalesEmailResponse> ProcessEmailAsync(Guid companyId, Guid userId, ProcessSalesEmailRequest request, CancellationToken cancellationToken)
    {
        EnsureCompany(companyId);
        EnsureUser(userId);
        ValidateEmail(request);

        var existing = await _dbContext.SalesEmailLinks.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ExternalMessageId == request.ProviderMessageId && !x.IsDeleted, cancellationToken);
        if (existing is not null)
        {
            return new ProcessSalesEmailResponse("already_processed", existing.LeadId, null, existing.Id);
        }

        var customer = !string.IsNullOrWhiteSpace(request.CompanyName)
            ? await _dbContext.CustomerCompanies.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.CompanyId == companyId && !x.IsDeleted && x.Name == request.CompanyName.Trim(), cancellationToken)
            : null;
        if (customer is null && !string.IsNullOrWhiteSpace(request.CompanyName))
        {
            customer = new CustomerCompany(Guid.NewGuid(), companyId, request.CompanyName.Trim());
            _dbContext.CustomerCompanies.Add(customer);
        }

        var contact = await _dbContext.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.CompanyId == companyId && !x.IsDeleted && x.Email == request.SenderEmail.Trim().ToLowerInvariant(), cancellationToken);
        if (contact is null)
        {
            contact = new Contact(Guid.NewGuid(), companyId, string.IsNullOrWhiteSpace(request.SenderName) ? request.SenderEmail : request.SenderName, request.SenderEmail, customer?.Id);
            _dbContext.Contacts.Add(contact);
        }

        Lead? lead = null;
        SalesActivity? activity = null;
        if (request.CreateLead)
        {
            lead = new Lead(Guid.NewGuid(), companyId, string.IsNullOrWhiteSpace(request.Subject) ? "New sales email" : request.Subject, SalesPipelineStage.NewStageId, primaryContactId: contact.Id, customerCompanyId: customer?.Id, source: "sales email");
            lead.ApplyEmailSignal(
                request.Subject,
                contact.Id,
                customer?.Id,
                request.Confidence,
                string.IsNullOrWhiteSpace(request.ProductOrServiceInterest)
                    ? "sales email"
                    : request.ProductOrServiceInterest.Trim());
            _dbContext.Leads.Add(lead);
            activity = new SalesActivity(Guid.NewGuid(), companyId, "email", $"Inbound sales email from {request.SenderEmail}: {request.Subject}", DateTime.UtcNow, leadId: lead.Id, contactId: contact.Id, customerCompanyId: customer?.Id);
            _dbContext.SalesActivities.Add(activity);
        }

        var link = new SalesEmailLink(Guid.NewGuid(), companyId, request.ProviderMessageId, lead?.Id, null, contact.Id, customer?.Id, request.CreateLead ? SalesStatuses.Linked : SalesStatuses.Ignored, detectedIntent: request.Intent, productOrServiceInterest: request.ProductOrServiceInterest, confidence: request.Confidence, rationale: request.Body);
        _dbContext.SalesEmailLinks.Add(link);
        AddAudit(companyId, userId, AuditEventActions.SalesEmailProcessed, "sales_email", link.Id, AuditEventOutcomes.Succeeded, "Sales email processed.", dataSourcesUsed: [new AuditDataSourceUsed("email", request.ProviderMessageId, request.Subject, request.SenderEmail)]);
        EnqueueSalesEvent(companyId, CompanyOutboxTopics.SalesEmailReceived, "sales_email", link.Id, new { providerMessageId = request.ProviderMessageId, senderEmail = contact.Email, contactId = contact.Id, leadId = lead?.Id });
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ProcessSalesEmailResponse("processed", lead?.Id, activity?.Id, link.Id);
    }

    private IQueryable<Lead> LeadQuery(Guid companyId) =>
        _dbContext.Leads.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.PrimaryContact)
            .Include(x => x.CustomerCompany)
            .Include(x => x.Activities)
            .Include(x => x.Recommendations)
            .Where(x => x.CompanyId == companyId && !x.IsDeleted);

    private IQueryable<Deal> DealQuery(Guid companyId) =>
        _dbContext.Deals.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.PipelineStage)
            .Include(x => x.PrimaryContact)
            .Include(x => x.CustomerCompany)
            .Include(x => x.Activities)
            .Include(x => x.Recommendations)
            .Where(x => x.CompanyId == companyId && !x.IsDeleted);

    private IQueryable<SalesActivity> ActivityQuery(Guid companyId) =>
        _dbContext.SalesActivities.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && !x.IsDeleted).OrderByDescending(x => x.OccurredUtc);

    private IQueryable<SalesAgentRecommendation> RecommendationQuery(Guid companyId) =>
        _dbContext.SalesAgentRecommendations.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && !x.IsDeleted).OrderByDescending(x => x.CreatedUtc);

    private Task<Lead?> MutableLeadAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken) =>
        _dbContext.Leads.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == leadId && !x.IsDeleted, cancellationToken);

    private Task<Deal?> MutableDealAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken) =>
        _dbContext.Deals.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == dealId && !x.IsDeleted, cancellationToken);

    private Task<SalesAgentRecommendation?> MutableRecommendationAsync(Guid companyId, Guid recommendationId, CancellationToken cancellationToken) =>
        _dbContext.SalesAgentRecommendations.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == recommendationId && !x.IsDeleted, cancellationToken);

    private Task<bool> StageExistsAsync(Guid companyId, Guid stageId, CancellationToken cancellationToken) =>
        _dbContext.SalesPipelineStages.IgnoreQueryFilters().AnyAsync(x => x.Id == stageId && !x.IsDeleted && x.IsActive && (x.CompanyId == SalesPipelineStage.SystemCompanyId || x.CompanyId == companyId), cancellationToken);

    private void AddAudit(Guid companyId, Guid userId, string action, string targetType, Guid targetId, string outcome, string rationale, IReadOnlyDictionary<string, string?>? metadata = null, IEnumerable<AuditDataSourceUsed>? dataSourcesUsed = null) =>
        _dbContext.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), companyId, "human", userId, action, targetType, targetId.ToString("D"), outcome, rationale, ["sales"], metadata, dataSourcesUsed: dataSourcesUsed));

    private void EnqueueSalesEvent(Guid companyId, string topic, string sourceType, Guid sourceId, object payload)
    {
        var eventId = $"{topic}:{companyId:N}:{sourceId:N}";
        var metadata = JsonSerializer.SerializeToNode(payload)?.AsObject() ?? [];
        metadata["companyId"] = JsonValue.Create(companyId);
        _outbox.Enqueue(
            companyId,
            topic,
            new PlatformEventEnvelope(
                eventId,
                topic,
                DateTime.UtcNow,
                companyId,
                eventId,
                sourceType,
                sourceId.ToString("D"),
                metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase)),
            eventId,
            idempotencyKey: $"platform-event:{companyId:N}:{eventId}",
            messageType: "platform_event",
            causationId: sourceId.ToString("D"));
    }

    private static SalesLeadSummaryResponse MapLeadSummary(Lead lead) =>
        new(lead.Id, lead.Title, StatusLabel(lead.Status), ResolveTemperature(lead), lead.PrimaryContact?.Email, StatusLabel(lead.Status), ResolveConfidence(lead), SuggestedLeadAction(lead), lead.EstimatedValue, lead.Currency, lead.Fit, StatusLabel(lead.Priority ?? "not set"), lead.QualifiedUtc, lead.QualifiedByUserId, lead.CreatedUtc, lead.UpdatedUtc);

    private static SalesLeadDetailResponse MapLeadDetail(Lead lead) =>
        new(lead.Id, lead.Title, StatusLabel(lead.Status), StatusLabel(lead.Status), ResolveTemperature(lead), lead.PrimaryContact?.Email, lead.PrimaryContact?.FullName, lead.CustomerCompany?.Name, lead.EstimatedValue, lead.Currency, SuggestedLeadAction(lead), lead.Fit, StatusLabel(lead.Priority ?? "not set"), lead.QualifiedUtc, lead.QualifiedByUserId, lead.Activities.OrderByDescending(x => x.OccurredUtc).Select(MapActivity).ToList(), lead.Recommendations.OrderByDescending(x => x.CreatedUtc).Select(MapRecommendation).ToList());

    private static SalesDealSummaryResponse MapDealSummary(Deal deal) =>
        new(deal.Id, deal.Title, deal.PipelineStageId, deal.PipelineStage?.Name ?? "Pipeline", StatusLabel(deal.Status), deal.Amount, deal.Currency, deal.CustomerCompany?.Name, deal.PrimaryContact?.FullName, deal.ExpectedCloseUtc, deal.UpdatedUtc);

    private static SalesDealDetailResponse MapDealDetail(Deal deal, CustomerMemoryContext? customerMemory = null) =>
        new(deal.Id, deal.Title, deal.PipelineStageId, deal.PipelineStage?.Name ?? "Pipeline", StatusLabel(deal.Status), deal.Amount, deal.Currency, $"{deal.Title} is worth {deal.Amount:0.##} {deal.Currency}.", deal.PrimaryContact?.FullName, deal.PrimaryContact?.Email, deal.CustomerCompany?.Name, DealAnalysis(deal), SuggestedReply(deal), deal.Activities.OrderByDescending(x => x.OccurredUtc).Select(MapActivity).ToList(), deal.Recommendations.OrderByDescending(x => x.CreatedUtc).Select(MapRecommendation).ToList(), DealActions(deal), null, customerMemory, deal.SourceLeadId);

    private static SalesActivityResponse MapActivity(SalesActivity activity) =>
        new(activity.Id, StatusLabel(activity.ActivityType), activity.Summary, StatusLabel(activity.Status), activity.OccurredUtc, activity.LeadId, activity.DealId);

    private static SalesRecommendationResponse MapRecommendation(SalesAgentRecommendation recommendation) =>
        new(recommendation.Id, recommendation.Recommendation, recommendation.Rationale, StatusLabel(recommendation.Status), recommendation.LeadId, recommendation.DealId, StatusLabel(recommendation.Category), StatusLabel(recommendation.TriggerCondition), StatusLabel(recommendation.ActionType), StatusLabel(recommendation.RiskLevel), recommendation.RequiresApproval, StatusLabel(recommendation.ApprovalStatus), StatusLabel(recommendation.ExecutionStatus), recommendation.FailureSummary, recommendation.CanRetryExecution, recommendation.ExecutionAttemptCount, recommendation.LastExecutionErrorCode, recommendation.Provider, recommendation.MailboxConnectionId, recommendation.ProviderThreadId, recommendation.ProviderMessageId, recommendation.ProviderDraftId, recommendation.ActivityId, recommendation.CreatedUtc);

    private static SalesAutomationPolicyResponse MapPolicy(SalesAutomationPolicy policy) =>
        new(
            policy.Id,
            policy.Mode,
            policy.FinanceDocumentsAlwaysRequireApproval,
            policy.OutboundEnabled,
            policy.MaxEmailsPerDay,
            policy.RequireApprovalFirstContact,
            policy.RequireApprovalPricingDiscussion,
            policy.RequireApprovalFollowUps,
            policy.RequireApprovalReEngagement,
            policy.WebsiteLeadDeduplicationWindowMinutes,
            policy.WebsiteLeadFollowUpSequenceId,
            policy.UpdatedUtc);

    private static SalesFinanceHandoffResponse MapFinanceHandoff(SalesFinanceHandoff handoff) =>
        new(handoff.Id, handoff.DealId, StatusLabel(handoff.Status), StatusLabel(handoff.ApprovalStatus), StatusLabel(handoff.ExecutionStatus), handoff.Summary, StatusLabel(handoff.DocumentType), handoff.ExternalSystem, handoff.ExternalDocumentId, handoff.ExternalDocumentNumber, handoff.ApprovalId, handoff.WriteRequestId, handoff.IdempotencyKey, handoff.FailureSummary, handoff.CanRetry, handoff.CreatedUtc, handoff.UpdatedUtc, handoff.ApprovedUtc, handoff.ExecutedUtc, handoff.FailedUtc, handoff.RetriedUtc);

    private async Task<SalesAutomationPolicy> GetOrCreatePolicyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.SalesAutomationPolicies.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        if (policy is not null) return policy;
        policy = new SalesAutomationPolicy(Guid.NewGuid(), companyId, SalesAutomationPolicyModes.ManualOnly);
        _dbContext.SalesAutomationPolicies.Add(policy);
        return policy;
    }

    private async Task<SalesAgentRecommendation?> AddRecommendationIfMissingAsync(Guid companyId, Guid userId, string dedupeKey, Guid? leadId, Guid? dealId, string category, string triggerCondition, string actionType, string riskLevel, string recommendationText, string rationale, SalesAutomationPolicyDecision decision, CancellationToken cancellationToken)
    {
        var exists = await _dbContext.SalesAgentRecommendations.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.DedupeKey == dedupeKey && !x.IsDeleted && x.Status != SalesStatuses.Completed, cancellationToken);
        if (exists) return null;
        var recommendation = new SalesAgentRecommendation(Guid.NewGuid(), companyId, recommendationText, rationale, leadId, dealId, decision.RequiresApproval ? SalesStatuses.WaitingForApproval : SalesStatuses.Open, category, triggerCondition, actionType, riskLevel, decision.RequiresApproval, decision.RequiresApproval ? SalesStatuses.WaitingForApproval : SalesStatuses.Approved, decision.CanAutoExecute ? SalesStatuses.Completed : SalesStatuses.Pending, dedupeKey);
        _dbContext.SalesAgentRecommendations.Add(recommendation);
        _dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "recommendation", rationale, DateTime.UtcNow, leadId, dealId));
        AddAudit(companyId, userId, "sales.recommendation.created", "sales_recommendation", recommendation.Id, AuditEventOutcomes.Succeeded, rationale, new Dictionary<string, string?> { ["policyMode"] = decision.PolicyMode, ["requiresApproval"] = decision.RequiresApproval.ToString(), ["executionMode"] = decision.ExecutionMode });
        return recommendation;
    }

    private async Task EnsureFinanceHandoffAsync(Guid companyId, Guid userId, Deal deal, CancellationToken cancellationToken)
    {
        var dedupeKey = $"sales-finance-handoff:{companyId:N}:{deal.Id:N}";
        var existing = await _dbContext.SalesFinanceHandoffs.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DedupeKey == dedupeKey, cancellationToken);
        if (existing is not null)
        {
            return;
        }

        var documentType = "invoice";
        var writeRequestId = DeterministicGuid($"sales-finance-write:{companyId:N}:{deal.Id:N}:{documentType}");
        var idempotencyKey = $"sales-finance-handoff:{companyId:N}:{deal.Id:N}:{documentType}";
        var handoff = new SalesFinanceHandoff(
            Guid.NewGuid(),
            companyId,
            deal.Id,
            $"Ask Finance to prepare a {documentType} for {deal.Title} using the authoritative accounting workflow.",
            documentType,
            dedupeKey,
            idempotencyKey);

        _dbContext.SalesFinanceHandoffs.Add(handoff);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var action = await _financeAccountingActions.RequestDocumentAsync(new RequestFinanceDocumentActionCommand(
            companyId,
            "sales_deal",
            deal.Id.ToString("D"),
            deal.UpdatedUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            documentType,
            DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime),
            deal.CustomerCompany?.Name ?? deal.Title,
            deal.Title,
            deal.Amount,
            deal.Currency,
            deal.PrimaryContact?.FullName,
            writeRequestId,
            userId,
            handoff.IdempotencyKey), cancellationToken);

        if (action.ApprovalId is Guid approvalId)
        {
            handoff.SetDestination(action.DestinationKey);
            handoff.AttachApproval(approvalId, writeRequestId);
        }
        else
        {
            handoff.MarkFinanceReviewRequired(action.DestinationKey, action.Message);
        }

        _dbContext.SalesActivities.Add(new SalesActivity(Guid.NewGuid(), companyId, "finance_handoff", action.Message, _timeProvider.GetUtcNow().UtcDateTime, dealId: deal.Id, contactId: deal.PrimaryContactId, customerCompanyId: deal.CustomerCompanyId));
        AddAudit(companyId, userId, "sales.finance_handoff.requested", "sales_finance_handoff", handoff.Id, AuditEventOutcomes.Succeeded, action.Message, new Dictionary<string, string?> { ["documentType"] = handoff.DocumentType, ["destination"] = action.DestinationKey, ["authority"] = action.Authority, ["approvalId"] = handoff.ApprovalId?.ToString("D"), ["writeRequestId"] = handoff.WriteRequestId?.ToString("D") });
        if (handoff.ApprovalId.HasValue)
        {
            AddAudit(companyId, userId, "sales.finance_handoff.approval_requested", "sales_finance_handoff", handoff.Id, AuditEventOutcomes.Succeeded, "Finance approval is required before the external document action runs.", new Dictionary<string, string?> { ["approvalId"] = handoff.ApprovalId?.ToString("D"), ["destination"] = action.DestinationKey });
        }
    }

    private async Task RefreshFinanceHandoffFromWriteCommandAsync(Guid companyId, Guid userId, SalesFinanceHandoff handoff, bool isRetry, CancellationToken cancellationToken)
    {
        if (handoff.WriteRequestId is not Guid writeRequestId)
        {
            return;
        }

        var command = await _dbContext.FinanceIntegrationWriteCommands.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken);
        if (command is null)
        {
            handoff.MarkFailed("finance_write_missing", "The approved finance request could not be found. You can retry this action.", true);
            AddAudit(companyId, userId, "sales.finance_handoff.execution_failed", "sales_finance_handoff", handoff.Id, AuditEventOutcomes.Failed, handoff.FailureSummary ?? "Finance draft creation failed.");
            await _dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        if (command.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed)
        {
            var externalId = command.ExternalId ?? command.Id.ToString("D");
            handoff.MarkCompleted(externalId, command.ExternalId);
            await EnsureFinanceExternalReferenceAsync(companyId, handoff, command, externalId, cancellationToken);
            AddAudit(companyId, userId, isRetry ? "sales.finance_handoff.retry_succeeded" : "sales.finance_handoff.execution_succeeded", "sales_finance_handoff", handoff.Id, AuditEventOutcomes.Succeeded, "The accounting provider accepted the approved Finance document action.", new Dictionary<string, string?> { ["externalDocumentId"] = handoff.ExternalDocumentId, ["externalDocumentNumber"] = handoff.ExternalDocumentNumber });
        }
        else if (command.Status == FinanceIntegrationWriteCommandRecordStatuses.Failed)
        {
            handoff.MarkFailed(command.FailureCategory ?? "provider_write_failed", command.SafeFailureSummary ?? "The accounting provider could not create the document. You can retry this action.", command.RetrySupported);
            AddAudit(companyId, userId, isRetry ? "sales.finance_handoff.retry_failed" : "sales.finance_handoff.execution_failed", "sales_finance_handoff", handoff.Id, AuditEventOutcomes.Failed, handoff.FailureSummary ?? "Finance draft creation failed.", new Dictionary<string, string?> { ["errorCode"] = command.FailureCategory, ["retryable"] = command.RetrySupported.ToString() });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task EnsureFinanceExternalReferenceAsync(Guid companyId, SalesFinanceHandoff handoff, FinanceIntegrationWriteCommandRecord command, string externalId, CancellationToken cancellationToken)
    {
        var providerKey = handoff.ExternalSystem;
        if (string.Equals(providerKey, "virtual_company", StringComparison.OrdinalIgnoreCase)) return;
        var connectionId = command.ConnectionId ?? await _dbContext.FinanceIntegrationConnections.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ProviderKey == providerKey && x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (connectionId is not Guid resolvedConnectionId)
        {
            return;
        }

        var exists = await _dbContext.FinanceExternalReferences.AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.ProviderKey == providerKey && x.EntityType == "sales_finance_handoff" && x.InternalRecordId == handoff.Id, cancellationToken);
        if (exists)
        {
            return;
        }

        _dbContext.FinanceExternalReferences.Add(new FinanceExternalReference(Guid.NewGuid(), companyId, resolvedConnectionId, providerKey, "sales_finance_handoff", handoff.Id, externalId, handoff.ExternalDocumentNumber, DateTime.UtcNow, DateTime.UtcNow));
    }

    private static string ResolveTemperature(Lead lead)
    {
        if (!string.IsNullOrWhiteSpace(lead.Temperature))
        {
            return StatusLabel(lead.Temperature);
        }

        var confidence = ResolveConfidence(lead);
        return confidence switch
        {
            >= 0.8m => "Hot",
            >= 0.6m => "Warm",
            _ => "New"
        };
    }

    private static decimal? ResolveConfidence(Lead lead) =>
        lead.Recommendations.Count > 0
            ? Math.Clamp(lead.Recommendations.Max(x => x.Confidence ?? 0m), 0m, 1m)
            : null;

    private static string SuggestedLeadAction(Lead lead) =>
        lead.Status switch
        {
            SalesStatuses.Open => "Review and qualify",
            SalesStatuses.Qualified => "Convert to deal",
            SalesStatuses.Converted => "Open deal",
            SalesStatuses.Rejected => "No action needed",
            _ => "Review details"
        };

    private static string DealAnalysis(Deal deal) =>
        deal.Status switch
        {
            SalesStatuses.Won => "Alex marked this deal as won.",
            SalesStatuses.Lost => "Alex marked this deal as lost.",
            _ when deal.UpdatedUtc <= DateTime.UtcNow.AddDays(-7) => "Alex recommends follow-up because this deal has been quiet for a week.",
            _ => "Alex is monitoring this deal and recommends keeping momentum."
        };

    private static string SuggestedReply(Deal deal) =>
        deal.Status == SalesStatuses.Open ? "Thanks for the conversation. I will follow up with the next step and timing." : string.Empty;

    private static IReadOnlyList<string> DealActions(Deal deal) =>
        deal.Status == SalesStatuses.Open ? ["Change stage", "Mark won", "Mark lost", "Create finance document"] : ["Review activity"];

    private static decimal ForecastWeight(Guid stageId) =>
        stageId == SalesPipelineStage.ProposalStageId ? 0.7m :
        stageId == SalesPipelineStage.QualifiedStageId ? 0.45m :
        stageId == SalesPipelineStage.WonStageId ? 1m : 0.2m;

    private static string BuildSummary(string? note, string fallback) =>
        string.IsNullOrWhiteSpace(note) ? fallback : note.Trim();

    private static string StatusLabel(string value) =>
        value.Replace("_", " ", StringComparison.Ordinal).Trim() is { Length: > 0 } label
            ? char.ToUpperInvariant(label[0]) + label[1..]
            : value;

    private static void ValidateConvert(ConvertLeadRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Amount <= 0) errors[nameof(request.Amount)] = ["Amount must be greater than zero."];
        if (string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Trim().Length != 3) errors[nameof(request.Currency)] = ["Currency must be a three-letter code."];
        if (errors.Count > 0) throw new SalesValidationException(errors);
    }

    private static void ValidateEmail(ProcessSalesEmailRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.ProviderMessageId)) errors[nameof(request.ProviderMessageId)] = ["Provider message id is required."];
        if (string.IsNullOrWhiteSpace(request.SenderEmail) || !request.SenderEmail.Contains('@', StringComparison.Ordinal)) errors[nameof(request.SenderEmail)] = ["Sender email must be valid."];
        if (string.IsNullOrWhiteSpace(request.Subject)) errors[nameof(request.Subject)] = ["Subject is required."];
        if (request.Confidence is < 0 or > 1) errors[nameof(request.Confidence)] = ["Confidence must be between 0 and 1."];
        if (errors.Count > 0) throw new SalesValidationException(errors);
    }

    private static SalesValidationException Validation(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });

    private static void EnsureCompany(Guid companyId) => SalesValidationException.ThrowIfEmpty(companyId, "companyId");
    private static void EnsureUser(Guid userId) => SalesValidationException.ThrowIfEmpty(userId, "userId");
    private static void EnsureId(Guid id, string field) => SalesValidationException.ThrowIfEmpty(id, field);

    private async Task<SalesReplyContext> ResolveReplyContextAsync(Guid companyId, SalesAgentRecommendation recommendation, CancellationToken cancellationToken)
    {
        var linkQuery = _dbContext.SalesEmailLinks.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.MailboxConnectionId != null && x.Provider != null);

        if (recommendation.LeadId.HasValue)
        {
            linkQuery = linkQuery.Where(x => x.LeadId == recommendation.LeadId.Value);
        }
        else if (recommendation.DealId.HasValue)
        {
            linkQuery = linkQuery.Where(x => x.DealId == recommendation.DealId.Value);
        }
        else
        {
            throw Validation(nameof(recommendation.Id), "Recommendation is not linked to a sales conversation.");
        }

        var link = await linkQuery.OrderByDescending(x => x.CreatedUtc).FirstOrDefaultAsync(cancellationToken)
            ?? throw Validation(nameof(recommendation.Id), "No originating email thread was found for this recommendation.");
        var connection = await _dbContext.MailboxConnections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x =>
                x.CompanyId == companyId &&
                x.Id == link.MailboxConnectionId &&
                x.Purpose == MailboxPurpose.Sales &&
                x.Status == MailboxConnectionStatus.Active,
                cancellationToken)
            ?? throw Validation(nameof(recommendation.Id), "The mailbox used for this recommendation is not connected.");
        var contact = link.ContactId.HasValue
            ? await _dbContext.Contacts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == link.ContactId && !x.IsDeleted, cancellationToken)
            : null;
        if (contact is null && recommendation.LeadId.HasValue)
        {
            contact = await _dbContext.Leads.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Id == recommendation.LeadId && !x.IsDeleted)
                .Select(x => x.PrimaryContact)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (contact is null && recommendation.DealId.HasValue)
        {
            contact = await _dbContext.Deals.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Id == recommendation.DealId && !x.IsDeleted)
                .Select(x => x.PrimaryContact)
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (contact is null || string.IsNullOrWhiteSpace(contact.Email))
        {
            throw Validation(nameof(recommendation.Id), "No recipient email was found for this recommendation.");
        }

        var originalMessageId = link.LinkKind == SalesEmailLinkKinds.Message
            ? link.ExternalMessageId
            : await _dbContext.SalesEmailLinks.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Provider == link.Provider && x.MailboxConnectionId == link.MailboxConnectionId && x.ExternalThreadId == link.ExternalThreadId && x.LinkKind == SalesEmailLinkKinds.Message && !x.IsDeleted)
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => x.ExternalMessageId)
                .FirstOrDefaultAsync(cancellationToken) ?? link.ExternalMessageId;

        return new SalesReplyContext(
            connection,
            originalMessageId,
            link.ExternalThreadId,
            link.InternetMessageId,
            contact.Email,
            contact.FullName,
            ResolveReplySubject(recommendation),
            contact.Id,
            link.CustomerCompanyId);
    }

    private async Task<Guid?> FindExecutionActivityIdAsync(Guid companyId, Guid recommendationId, CancellationToken cancellationToken) =>
        await _dbContext.SalesActivities.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.Summary.Contains(recommendationId.ToString("D")))
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

    private static string ResolveReplySubject(SalesAgentRecommendation recommendation) =>
        recommendation.DealId.HasValue ? "Following up on our conversation" : "Following up";

    private static string BuildFollowUpBody(SalesAgentRecommendation recommendation, string? note)
    {
        var baseBody = recommendation.ActionType == SalesRecommendationActions.SendEmail
            ? "Thanks for the conversation. I wanted to follow up on the next step."
            : "Thanks for the conversation. Here is a draft follow-up for review.";
        return string.IsNullOrWhiteSpace(note) ? baseBody : $"{baseBody}\n\n{note.Trim()}";
    }

    private static Guid DeterministicGuid(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }

    private sealed record SalesReplyContext(MailboxConnection MailboxConnection, string OriginalMessageId, string? ThreadId, string? InternetMessageId, string RecipientEmail, string? RecipientName, string Subject, Guid? ContactId, Guid? CustomerCompanyId);
}
