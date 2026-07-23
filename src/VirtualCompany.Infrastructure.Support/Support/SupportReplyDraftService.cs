using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Agents;
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
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportReplyDraftService : ISupportReplyDraftService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;
    private readonly ISupportOutboundEmailSender _outboundEmailSender;
    private readonly ISupportKnowledgeContextProvider _knowledgeContextProvider;
    private readonly ISupportKnowledgeGapService _knowledgeGaps;
    private readonly ISupportReplySafetyPolicy _safetyPolicy;
    private readonly ICompanyOutboxEnqueuer? _outboxEnqueuer;
    private readonly IAgentReasoningGateway? _reasoningGateway;

    public SupportReplyDraftService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter audit,
        ISupportOutboundEmailSender outboundEmailSender,
        ISupportKnowledgeContextProvider knowledgeContextProvider,
        ISupportKnowledgeGapService knowledgeGaps,
        ISupportReplySafetyPolicy? safetyPolicy = null,
        ICompanyOutboxEnqueuer? outboxEnqueuer = null,
        IAgentReasoningGateway? reasoningGateway = null)
    {
        _dbContext = dbContext;
        _audit = audit;
        _outboundEmailSender = outboundEmailSender;
        _knowledgeContextProvider = knowledgeContextProvider;
        _knowledgeGaps = knowledgeGaps;
        _safetyPolicy = safetyPolicy ?? new DeterministicSupportReplySafetyPolicy(dbContext);
        _outboxEnqueuer = outboxEnqueuer;
        _reasoningGateway = reasoningGateway;
    }

    public async Task<SupportReplyDraftDto?> GenerateDraftAsync(Guid companyId, Guid userId, Guid supportCaseId, GenerateSupportReplyDraftRequest request, CancellationToken cancellationToken)
    {
        var supportCase = await _dbContext.SupportCases.Include(x => x.Messages).Include(x => x.Events).FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (supportCase is null) return null;

        var context = await _knowledgeContextProvider.RetrieveAsync(companyId, supportCase.Id, cancellationToken);
        var lastInbound = supportCase.Messages.Where(x => x.Direction == SupportMessageDirections.Inbound).OrderByDescending(x => x.OccurredUtc).FirstOrDefault();
        var answerability = ResolveAnswerability(supportCase, context, request.ForceReview);
        var confidence = Math.Min(0.92m, Math.Max(0.48m, answerability + 0.08m));
        var composition = await BuildGroundedDraftBodyAsync(companyId, userId, supportCase, context, lastInbound, cancellationToken);
        if (!composition.UsedAi)
        {
            confidence = Math.Min(confidence, 0.68m);
        }
        var body = composition.Body;
        var sourceJson = BuildSourceReferencesJson(context);
        var rationale = !composition.UsedAi && context.HasTrustedGrounding
            ? "Approved sources can answer this question, but automated reply generation did not complete. Human review is required."
            : answerability < 0.7m
                ? "Available approved support knowledge is incomplete. Human review is required."
                : context.RationaleSummary;
        var draft = new SupportReplyDraft(Guid.NewGuid(), companyId, supportCase.Id, body, request.Tone ?? "Helpful", confidence, answerability, rationale, sourceJson, null, userId == Guid.Empty ? null : userId);
        _dbContext.SupportReplyDrafts.Add(draft);
        var eventSummary = !composition.UsedAi && context.HasTrustedGrounding
            ? "Reply composition failed after approved knowledge was retrieved; human review is required."
            : answerability < 0.7m
                ? "Reply draft needs review because source confidence is low."
                : "Reply draft created from retrieved knowledge.";
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.ReplyDrafted, eventSummary, userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human, userId == Guid.Empty ? null : userId, DateTime.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!context.HasTrustedGrounding || answerability < 0.7m)
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

    private async Task<DraftComposition> BuildGroundedDraftBodyAsync(
        Guid companyId,
        Guid userId,
        SupportCase supportCase,
        SupportKnowledgeContext context,
        SupportMessage? lastInbound,
        CancellationToken cancellationToken)
    {
        var trustedSources = context.Sources
            .Where(x => x.IsTrusted && !string.IsNullOrWhiteSpace(x.Excerpt))
            .Take(6)
            .ToList();

        if (_reasoningGateway is not null && trustedSources.Count > 0)
        {
            var agentId = supportCase.AssignedAgentId ?? await _dbContext.Agents.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Department == "Support")
                .OrderBy(x => x.DisplayName)
                .Select(x => (Guid?)x.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (agentId.HasValue)
            {
                var question = TrimForPrompt(lastInbound?.Body ?? supportCase.Description ?? supportCase.Subject, 3000);
                var result = await _reasoningGateway.ReasonAsync(
                    new AgentReasoningRequest(
                        companyId,
                        agentId.Value,
                        "support.reply_drafting",
                        "1.0",
                        "support-reply-v2",
                        "1.0",
                        $"Write a concise, accurate customer-facing support email that directly answers this message: {question} Use only confirmed facts from the supplied sources. Explain relevant product functions and automation limits in plain language. Do not expose source excerpts, source IDs, implementation plans, prompts, schemas, internal statuses, or technical instructions. Do not promise unavailable functionality or full autonomy. If evidence is insufficient, say exactly what requires human confirmation. Include a suitable greeting and sign off as Ben, Virtual Company Support. Put the complete email in the summary field.",
                        trustedSources.Select((source, index) => new AgentAiSource(
                            $"knowledge:{source.DocumentId?.ToString("N") ?? "unknown"}:{source.EntityId?.ToString("N") ?? index.ToString()}",
                            source.Type,
                            source.Label,
                            TrimForPrompt(source.Excerpt!, 1600))).ToArray(),
                        [],
                        [],
                        userId == Guid.Empty ? null : userId,
                        CorrelationId: $"support-draft:{supportCase.Id:N}:{Guid.NewGuid():N}",
                        IncludeClaims: false),
                    cancellationToken);

                if (result.Status is AgentAiRunStatuses.Completed or AgentAiRunStatuses.NeedsReview &&
                    IsUsableCustomerDraft(result.Summary))
                {
                    return new DraftComposition(result.Summary.Trim(), true);
                }
            }
        }

        return new DraftComposition(
            "Hello,\n\nThank you for your message. I found potentially relevant company information, but I could not produce a reliable answer from the approved sources. A support colleague needs to review the question before we confirm the product capabilities or level of automation.\n\nBest regards,\nBen\nVirtual Company Support",
            false);
    }

    private static bool IsUsableCustomerDraft(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 40 and <= 6000 &&
        !CustomerDraftBlockedMarkers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static string TrimForPrompt(string value, int maxLength)
    {
        var normalized = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private sealed record DraftComposition(string Body, bool UsedAi);

    private static readonly string[] CustomerDraftBlockedMarkers =
    [
        "implementation requirements",
        "required statuses",
        "retrievalsourcesummary",
        "missinginformationsummary",
        "supportcasecontextsummary",
        "definition of done",
        "read and follow"
    ];

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
        var idempotencyKey = $"support:{companyId:N}:{supportCase.Id:N}:{draft.Id:N}";
        if (_outboxEnqueuer is not null)
        {
            _outboxEnqueuer.Enqueue(
                companyId,
                CompanyOutboxTopics.SupportReplyDeliveryRequested,
                new SupportReplyDeliveryRequestedMessage(
                    companyId,
                    supportCase.Id,
                    draft.Id,
                    userId,
                    request.Autonomous,
                    request.ResolveAfterSend,
                    request.MailboxConnectionId,
                    toEmail,
                    request.ToDisplayName,
                    subject,
                    originalMessageId,
                    providerThreadId,
                    request.InternetMessageId,
                    idempotencyKey,
                    idempotencyKey),
                correlationId: idempotencyKey,
                idempotencyKey: idempotencyKey,
                messageType: nameof(SupportReplyDeliveryRequestedMessage));
            await _audit.WriteAsync(new AuditEventWriteRequest(
                companyId,
                request.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human,
                request.Autonomous ? null : userId,
                "support.reply.send_requested",
                "support_reply_draft",
                draft.Id.ToString("D"),
                AuditEventOutcomes.Requested,
                "Support reply delivery was queued.",
                ["support", "mailbox", "outbox"],
                Metadata: new Dictionary<string, string?> { ["idempotencyKey"] = idempotencyKey }), cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return await new SupportCaseService(_dbContext, _audit).GetCaseAsync(companyId, supportCase.Id, cancellationToken);
        }

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
                idempotencyKey), cancellationToken);
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
        _dbContext.SupportMessages.Add(new SupportMessage(Guid.NewGuid(), companyId, supportCase.Id, SupportMessageDirections.Outbound, "email", "support", toEmail, draft.DraftBody, now, providerMessageId: sendResult.ProviderMessageId, providerThreadId: sendResult.ProviderThreadId, replyDraftId: draft.Id));
        supportCase.LinkProviderMessage(sendResult.ProviderThreadId, sendResult.ProviderMessageId);
        supportCase.MarkFirstResponseSent(now);
        supportCase.SetStatus(request.ResolveAfterSend ? SupportCaseStatuses.Resolved : SupportCaseStatuses.WaitingForCustomer);
        _dbContext.SupportCaseEvents.Add(new SupportCaseEvent(Guid.NewGuid(), companyId, supportCase.Id, SupportCaseEventTypes.ReplySent, "Support reply sent.", request.Autonomous ? AuditActorTypes.Agent : AuditActorTypes.Human, request.Autonomous ? null : userId, now));
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
