using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingQuestionAnsweringService(
    VirtualCompanyDbContext db,
    ICompanyKnowledgeSearchService knowledge,
    IAgentReasoningGateway reasoning,
    IAgentEffectiveAuthorityResolver authorityResolver,
    TimeProvider timeProvider,
    ILogger<SalesMeetingQuestionAnsweringService> logger) : ISalesMeetingQuestionAnsweringService
{
    public async Task<IReadOnlyList<SalesMeetingQuestionDto>> ListQuestionsAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        var query = db.SalesMeetingQuestions.AsNoTracking().Include(x => x.Evidence).Where(x => x.CompanyId == companyId && x.SessionId == sessionId);
        return (await query.OrderBy(x => x.Sequence).ToListAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<IReadOnlyList<SalesMeetingStageAnswerDto>> ListStageAnswersAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        return await db.SalesMeetingQuestions.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.SessionId == sessionId &&
                        x.Visibility == SalesMeetingAnswerVisibility.ApprovedForStage &&
                        x.Status == SalesMeetingQuestionStatus.Completed && x.AnswerText != null && x.StageApprovedUtc != null)
            .OrderBy(x => x.Sequence)
            .Select(x => new SalesMeetingStageAnswerDto(x.Id, x.Sequence, x.QuestionText, x.AnswerText!, x.StageApprovedUtc!.Value))
            .ToListAsync(cancellationToken);
    }

    public async Task<SalesMeetingQuestionDto?> GetQuestionAsync(Guid companyId, Guid userId, Guid sessionId, Guid questionId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        var question = await db.SalesMeetingQuestions.AsNoTracking().Include(x => x.Evidence)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == questionId, cancellationToken);
        return question is null ? null : ToDto(question);
    }

    public async Task<SalesMeetingQuestionDto?> AskAsync(Guid companyId, Guid userId, Guid sessionId,
        AskSalesMeetingQuestionRequest request, string? correlationId, CancellationToken cancellationToken)
    {
        EnsureIds(companyId, userId, sessionId, request.ClientQuestionId, request.AgentId);
        if (string.IsNullOrWhiteSpace(request.Question) || request.Question.Trim().Length > 2000)
            throw Validation(nameof(request.Question), "A question of 2,000 characters or fewer is required.");
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        var duplicate = await db.SalesMeetingQuestions.AsNoTracking().Include(x => x.Evidence)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.ClientQuestionId == request.ClientQuestionId, cancellationToken);
        if (duplicate is not null) return ToDto(duplicate);

        var session = await db.SalesMeetingSessions.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == sessionId, cancellationToken);
        if (session is null) return null;
        if (ParseInput(request.InputSource) == SalesMeetingInputSource.BrowserRoom &&
            (session.RetentionUntilUtc <= timeProvider.GetUtcNow().UtcDateTime ||
             !await db.SalesMeetingTranscriptSegments.AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
                 x.SessionId == sessionId && x.Id == request.ClientQuestionId &&
                 x.InputSource == SalesMeetingInputSource.BrowserRoom && x.Content == request.Question.Trim(), cancellationToken)))
            throw Validation(nameof(request.InputSource), "A browser question requires retained meeting evidence.");
        var membership = await db.CompanyMemberships.AsNoTracking().SingleAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, cancellationToken);
        var agent = await db.Agents.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == request.AgentId, cancellationToken)
            ?? throw new KeyNotFoundException("The sales agent is unavailable in this company.");
        if (!agent.Department.Equals("Sales", StringComparison.OrdinalIgnoreCase))
            throw new SalesMeetingCaptureConflictException(SalesMeetingCaptureProblemCodes.AgentNotAuthorized, "Meeting questions require an authorized Sales agent.");
        var authority = await authorityResolver.ResolveAsync(companyId, agent.Id, cancellationToken);
        RequireAuthority(authority, SalesMeetingCaptureToolNames.ReadContext, ToolActionType.Read);
        RequireAuthority(authority, SalesMeetingCaptureToolNames.SearchApprovedKnowledge, ToolActionType.Read);
        RequireAuthority(authority, SalesMeetingCaptureToolNames.AnswerQuestion, ToolActionType.Recommend);

        var deck = await db.SalesPresentationDecks.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.IsActive, cancellationToken);
        SalesPresentationSlide? slide = null;
        if (deck is not null && session.CurrentSlideIndex > 0)
            slide = await db.SalesPresentationSlides.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DeckId == deck.Id && x.SlideNumber == session.CurrentSlideIndex, cancellationToken);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var question = new SalesMeetingQuestion(Guid.NewGuid(), companyId, sessionId, request.ClientQuestionId,
            request.Sequence, agent.Id, request.Question, ParseSpeaker(request.AskerType), request.AskerLabel,
            ParseInput(request.InputSource), slide?.Id, session.ConcurrencyVersion, userId, now);
        question.MarkAnswering(now);
        db.SalesMeetingQuestions.Add(question);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException) { throw new SalesMeetingCaptureConflictException(SalesMeetingCaptureProblemCodes.Conflict, "The question order or idempotency key already exists. Refresh the meeting questions."); }

        try
        {
            var sources = await BuildSourcesAsync(session, membership, agent.Id, deck, slide, request.Question, cancellationToken);
            var result = await reasoning.ReasonAsync(new AgentReasoningRequest(
                companyId, agent.Id, AgentCapabilityIds.SalesMeetingQuestionAnswering, "1.0.0",
                "sales-meeting-grounded-answer-v1", "1.0.0",
                "Answer the meeting question using only the supplied sources. Cite source IDs for every factual claim. If the sources do not verify the requested detail, say clearly that it is not verified and identify what must be followed up. Never invent product, pricing, policy, customer, promise, discount, or contractual details. Do not request or perform any mutation.",
                sources.Values.Select(x => x.Source).ToArray(), ["recommend"],
                [SalesMeetingCaptureToolNames.ReadContext, SalesMeetingCaptureToolNames.SearchApprovedKnowledge, SalesMeetingCaptureToolNames.AnswerQuestion],
                userId, CorrelationId: Normalize(correlationId), IncludeClaims: true,
                EffectiveAuthorityVersion: authority.AuthorityVersion, EffectiveAuthorityHash: authority.AuthorityHash), cancellationToken);

            var acceptedClaims = result.Claims.Select((claim, index) => new { claim, index })
                .Where(x => x.claim.SourceIds.Count > 0 && x.claim.SourceIds.All(sources.ContainsKey)).ToArray();
            var verified = result.Status == AgentAiRunStatuses.Completed && acceptedClaims.Length > 0 && result.MissingEvidence.Count == 0;
            var answer = verified ? result.Summary : "I couldn't verify that detail from the approved meeting sources. I've marked it for follow-up.";
            question.Complete(answer, verified ? result.Confidence : 0m, !verified || result.Status == AgentAiRunStatuses.NeedsReview, result.RunId, verified, timeProvider.GetUtcNow().UtcDateTime);
            if (verified)
            {
                foreach (var item in acceptedClaims)
                foreach (var sourceId in item.claim.SourceIds.Distinct(StringComparer.Ordinal))
                {
                    var source = sources[sourceId].Source;
                    db.SalesMeetingQuestionEvidence.Add(new SalesMeetingQuestionEvidence(Guid.NewGuid(), companyId, question.Id,
                        item.index, item.claim.Text, item.claim.Type, item.claim.Confidence, source.Id, source.Type, source.Title, timeProvider.GetUtcNow().UtcDateTime));
                }
            }
            AddAudit(question, userId, verified ? AuditEventActions.SalesMeetingQuestionAnswered : AuditEventActions.SalesMeetingQuestionFailed,
                verified ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Pending,
                verified ? "A meeting question was answered from approved evidence." : "A meeting question needs follow-up because approved evidence was insufficient.", correlationId);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            question.Fail("cancelled", "Answer generation was cancelled. Retry the question when ready.", true, timeProvider.GetUtcNow().UtcDateTime);
            AddAudit(question, userId, AuditEventActions.SalesMeetingQuestionFailed, AuditEventOutcomes.Failed, "Meeting question answering was cancelled.", correlationId);
            await SaveFailureAsync();
            throw;
        }
        catch (Exception exception) when (exception is not SalesMeetingCaptureValidationException and not SalesMeetingCaptureConflictException)
        {
            logger.LogWarning(exception, "Grounded meeting answer failed for session {SessionId} and question {QuestionId}.", sessionId, question.Id);
            question.Fail("provider_unavailable", "Alex could not complete the grounded answer. Retry when the AI service is available.", false, timeProvider.GetUtcNow().UtcDateTime);
            AddAudit(question, userId, AuditEventActions.SalesMeetingQuestionFailed, AuditEventOutcomes.Failed, "Meeting question answering failed safely.", correlationId);
            await SaveFailureAsync();
        }
        await db.Entry(question).Collection(x => x.Evidence).LoadAsync(CancellationToken.None);
        return ToDto(question);

        async Task SaveFailureAsync()
        {
            try { await db.SaveChangesAsync(CancellationToken.None); }
            catch (Exception saveException) { logger.LogError(saveException, "Failed to persist meeting question failure state for {QuestionId}.", question.Id); }
        }
    }

    public async Task<SalesMeetingQuestionDto?> ApproveForStageAsync(Guid companyId, Guid userId, Guid sessionId,
        Guid questionId, long expectedVersion, string? correlationId, CancellationToken cancellationToken)
    {
        await EnsureMemberAsync(companyId, userId, cancellationToken);
        var question = await db.SalesMeetingQuestions.Include(x => x.Evidence).SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SessionId == sessionId && x.Id == questionId, cancellationToken);
        if (question is null) return null;
        try { question.ApproveForStage(userId, expectedVersion, timeProvider.GetUtcNow().UtcDateTime); }
        catch (InvalidOperationException exception) { throw new SalesMeetingCaptureConflictException(SalesMeetingCaptureProblemCodes.Conflict, exception.Message); }
        AddAudit(question, userId, AuditEventActions.SalesMeetingAnswerApprovedForStage, AuditEventOutcomes.Succeeded, "A verified meeting answer was explicitly approved for customer-visible stage use.", correlationId);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new SalesMeetingCaptureConflictException(SalesMeetingCaptureProblemCodes.Conflict, "The question changed after it was opened. Refresh before approving it."); }
        return ToDto(question);
    }

    private async Task<Dictionary<string, GroundingSource>> BuildSourcesAsync(SalesMeetingSession session, CompanyMembership membership,
        Guid agentId, SalesPresentationDeck? deck, SalesPresentationSlide? slide, string query, CancellationToken ct)
    {
        var values = new Dictionary<string, GroundingSource>(StringComparer.Ordinal);
        void Add(string id, string type, string title, string text)
        {
            if (!string.IsNullOrWhiteSpace(text)) values[id] = new(new AgentAiSource(id, type, title, Trim(text, 3000)));
        }
        if (slide is not null)
        {
            Add($"presentation-slide:{slide.Id:N}", "visible_slide", slide.Title ?? $"Slide {slide.SlideNumber}", slide.ExtractedText);
            if (!string.IsNullOrWhiteSpace(slide.SpeakerNotes)) Add($"presentation-notes:{slide.Id:N}", "approved_speaker_notes", $"Notes for slide {slide.SlideNumber}", slide.SpeakerNotes);
            var artifacts = await db.SalesMeetingArtifacts.AsNoTracking().Where(x => x.CompanyId == session.CompanyId && x.SessionId == session.Id && x.SlideId == slide.Id && x.Classification == SalesMeetingArtifactClassification.ConfirmedFact).Take(20).ToListAsync(ct);
            foreach (var item in artifacts) Add($"meeting-artifact:{item.Id:N}", "approved_slide_plan", item.Section, item.Content);
        }
        var customer = await db.CustomerCompanies.AsNoTracking().SingleAsync(x => x.CompanyId == session.CompanyId && x.Id == session.CustomerCompanyId, ct);
        Add($"customer:{customer.Id:N}", "authorized_customer_record", customer.Name, $"Customer: {customer.Name}. Industry: {customer.Industry ?? "not recorded"}. Status: {customer.Status}.");
        if (session.DealId is Guid dealId)
        {
            var deal = await db.Deals.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == session.CompanyId && x.Id == dealId, ct);
            if (deal is not null) Add($"deal:{deal.Id:N}", "authorized_deal_record", deal.Title, $"Deal: {deal.Title}. Status: {deal.Status}. Value: {deal.Amount} {deal.Currency}.");
        }
        if (session.ContactId is Guid contactId)
        {
            var contact = await db.Contacts.AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == session.CompanyId && x.Id == contactId, ct);
            if (contact is not null) Add($"contact:{contact.Id:N}", "authorized_contact_record", contact.FullName, $"Contact: {contact.FullName}. Role: {contact.Title ?? "not recorded"}.");
        }
        var knowledgeResults = await knowledge.SearchAsync(new CompanyKnowledgeSemanticSearchQuery(session.CompanyId, query, 10,
            new CompanyKnowledgeAccessContext(session.CompanyId, membership.Id, membership.UserId, membership.Role.ToStorageValue(), ["sales", "knowledge"], agentId)), ct);
        foreach (var item in knowledgeResults) Add($"knowledge-chunk:{item.ChunkId:N}", "approved_company_knowledge", item.DocumentTitle, item.Content);
        return values;
    }

    private static void RequireAuthority(AgentEffectiveAuthorityDto authority, string tool, ToolActionType action)
    {
        var decision = authority.Find(tool, action, "sales");
        if (decision is null || !decision.IsUsable)
            throw new SalesMeetingCaptureConflictException(SalesMeetingCaptureProblemCodes.AgentNotAuthorized, $"Alex is not authorized to use {tool} for this meeting.");
    }
    private async Task EnsureMemberAsync(Guid companyId, Guid userId, CancellationToken ct)
    {
        EnsureIds(companyId, userId);
        if (!await db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.UserId == userId && x.Status == CompanyMembershipStatus.Active, ct)) throw new UnauthorizedAccessException("An active company membership is required.");
    }
    private void AddAudit(SalesMeetingQuestion question, Guid actor, string action, string outcome, string rationale, string? correlationId) =>
        db.AuditEvents.Add(new AuditEvent(Guid.NewGuid(), question.CompanyId, AuditActorTypes.User, actor, action,
            "sales_meeting_question", question.Id.ToString("D"), outcome, rationale, ["meeting question", "approved sources"],
            new Dictionary<string, string?> { ["sessionId"] = question.SessionId.ToString("D"), ["status"] = question.Status.ToStorageValue(), ["aiRunId"] = question.AiRunId?.ToString("D"), ["sourceCount"] = question.Evidence.Select(x => x.SourceId).Distinct().Count().ToString() }, Normalize(correlationId), timeProvider.GetUtcNow().UtcDateTime));
    internal static SalesMeetingQuestionDto ToDto(SalesMeetingQuestion x) => new(x.Id, x.ClientQuestionId, x.Sequence, x.AgentId,
        x.QuestionText, x.AnswerText, x.AskerType.ToStorageValue(), x.AskerLabel, x.InputSource.ToStorageValue(), x.VisibleSlideId,
        x.PresentationVersion, x.Status.ToStorageValue(), x.Confidence, x.FollowUpRequired, x.ReviewState.ToStorageValue(),
        x.Visibility.ToStorageValue(), x.AiRunId, x.FailureCode, x.FailureSummary, x.AskedUtc, x.AnsweredUtc, x.StageApprovedUtc,
        x.UpdatedUtc, x.ConcurrencyVersion, x.Evidence.OrderBy(e => e.ClaimOrder).ThenBy(e => e.SourceId).Select(e => new SalesMeetingQuestionEvidenceDto(e.ClaimOrder, e.ClaimText, e.ClaimType, e.Confidence, e.SourceId, e.SourceType, e.SourceTitle)).ToArray());
    private static SalesMeetingSpeakerType ParseSpeaker(string value) { try { return SalesMeetingCaptureEnumValues.ParseSpeakerType(value); } catch (ArgumentException e) { throw Validation(nameof(AskSalesMeetingQuestionRequest.AskerType), e.Message); } }
    private static SalesMeetingInputSource ParseInput(string value) { try { return SalesMeetingCaptureEnumValues.ParseInputSource(value); } catch (ArgumentException e) { throw Validation(nameof(AskSalesMeetingQuestionRequest.InputSource), e.Message); } }
    private static SalesMeetingCaptureValidationException Validation(string field, string message) => new(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { [field] = [message] });
    private static string Trim(string value, int max) => value.Trim()[..Math.Min(value.Trim().Length, max)];
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : Trim(value, 128);
    private static void EnsureIds(params Guid[] ids) { if (ids.Any(x => x == Guid.Empty)) throw new ArgumentException("Required identifiers cannot be empty."); }
    private sealed record GroundingSource(AgentAiSource Source);
}
