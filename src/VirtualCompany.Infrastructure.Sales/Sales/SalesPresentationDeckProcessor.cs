using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPresentationDeckProcessor(
    VirtualCompanyDbContext db,
    ICompanyDocumentStorage storage,
    ICompanyDocumentVirusScanner virusScanner,
    ISalesPresentationDeckExtractor extractor,
    ISalesPresentationSlideRenderer renderer,
    ICompanyKnowledgeSearchService knowledge,
    IAgentReasoningGateway reasoning,
    ISalesAgentDecisionService decisions,
    IOptions<SalesPresentationOptions> options,
    TimeProvider timeProvider,
    ILogger<SalesPresentationDeckProcessor> logger) : ISalesPresentationDeckProcessor
{
    public async Task<int> ProcessPendingAsync(CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var staleBefore = now.AddSeconds(-options.Value.ClaimTimeoutSeconds);
        var candidates = await db.SalesPresentationDecks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == SalesPresentationDeckStatus.PendingScan ||
                        x.Status == SalesPresentationDeckStatus.Processing && x.ProcessingStartedUtc <= staleBefore ||
                        x.Status == SalesPresentationDeckStatus.Processed && x.BriefRegenerationRequestedUtc != null)
            .OrderBy(x => x.UpdatedUtc).ThenBy(x => x.Id)
            .Take(options.Value.BatchSize)
            .Select(x => new { x.CompanyId, x.Id })
            .ToListAsync(cancellationToken);
        foreach (var item in candidates)
            await ProcessAsync(item.CompanyId, item.Id, cancellationToken);
        return candidates.Count;
    }

    public async Task ProcessAsync(Guid companyId, Guid deckId, CancellationToken cancellationToken)
    {
        var deck = await db.SalesPresentationDecks.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == deckId, cancellationToken);
        if (deck is null) return;
        if (deck.Status == SalesPresentationDeckStatus.Processed && deck.BriefRegenerationRequestedUtc.HasValue)
        {
            await RegenerateBriefAsync(deck, cancellationToken);
            return;
        }

        try
        {
            deck.BeginProcessing(UtcNow(), TimeSpan.FromSeconds(options.Value.ClaimTimeoutSeconds));
        }
        catch (InvalidOperationException)
        {
            return;
        }
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await using var source = await storage.OpenReadAsync(deck.StorageKey, cancellationToken);
            var scan = await virusScanner.ScanAsync(new CompanyDocumentVirusScanRequest(
                deck.CompanyId, deck.Id, deck.StorageKey, deck.StorageUrl,
                deck.OriginalFileName, deck.ContentType, deck.FileSizeBytes,
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                {
                    ["purpose"] = System.Text.Json.Nodes.JsonValue.Create("sales_presentation_deck"),
                    ["sessionId"] = System.Text.Json.Nodes.JsonValue.Create(deck.SessionId)
                }), cancellationToken);
            if (scan.Outcome == CompanyDocumentVirusScanOutcome.Blocked)
                throw new SalesPresentationProcessingException(
                    scan.FailureCode ?? "malware_blocked",
                    scan.Message ?? "The presentation was blocked by the malware scan.", false, true);
            if (scan.Outcome == CompanyDocumentVirusScanOutcome.Error)
                throw new SalesPresentationProcessingException(
                    scan.FailureCode ?? "virus_scan_unavailable",
                    "The presentation could not be cleared by malware scanning. Try again later.", true);

            var extracted = await extractor.ExtractAsync(source, options.Value.MaximumSlides, cancellationToken);
            var session = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == companyId && x.Id == deck.SessionId, cancellationToken);
            var membership = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == companyId && x.UserId == deck.UploadedByUserId &&
                                  x.Status == CompanyMembershipStatus.Active, cancellationToken);
            var evidence = await SearchKnowledgeAsync(deck, session, membership, extracted, cancellationToken);
            var generatedPlans = await GenerateSlidePlansAsync(deck, extracted, evidence, cancellationToken);

            var oldSlides = await db.SalesPresentationSlides.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.DeckId == deck.Id &&
                            x.ProcessingVersion == deck.ProcessingVersion)
                .ToListAsync(cancellationToken);
            if (oldSlides.Count > 0)
            {
                var oldIds = oldSlides.Select(x => x.Id).ToArray();
                var oldArtifacts = await db.SalesMeetingArtifacts.IgnoreQueryFilters()
                    .Where(x => x.CompanyId == companyId && x.DeckId == deck.Id &&
                                x.SlideId != null && oldIds.Contains(x.SlideId.Value))
                    .ToListAsync(cancellationToken);
                db.SalesMeetingArtifacts.RemoveRange(oldArtifacts);
                db.SalesPresentationSlides.RemoveRange(oldSlides);
            }

            var now = UtcNow();
            var slides = new List<SalesPresentationSlide>(extracted.Slides.Count);
            var slideArtifacts = new List<SalesMeetingArtifact>();
            SalesPresentationRenderedSlide? lastRendering = null;
            foreach (var item in extracted.Slides)
            {
                lastRendering = await renderer.RenderAsync(new SalesPresentationRenderRequest(
                    deck.CompanyId, deck.Id, deck.ProcessingVersion, item,
                    options.Value.RenderWidthPixels, options.Value.RenderHeightPixels), cancellationToken);
                var imageKey = $"companies/{deck.CompanyId:N}/sales/meeting-sessions/{deck.SessionId:N}/decks/{deck.Id:N}/v{deck.ProcessingVersion}/slides/{item.SlideNumber:D4}{lastRendering.FileExtension}";
                await using var imageContent = new MemoryStream(lastRendering.Content, writable: false);
                var storedImage = await storage.WriteAsync(new DocumentStorageWriteRequest(
                    deck.CompanyId, deck.Id, imageKey, Path.GetFileName(imageKey),
                    lastRendering.ContentType, imageContent), cancellationToken);
                var nextTitle = extracted.Slides.FirstOrDefault(x => x.SlideNumber == item.SlideNumber + 1)?.Title;
                generatedPlans.TryGetValue(SlideSourceId(deck.Id, item.SlideNumber), out var generatedPlan);
                var slide = new SalesPresentationSlide(
                    Guid.NewGuid(), deck.CompanyId, deck.Id, deck.ProcessingVersion, item.SlideNumber,
                    item.Title, item.ExtractedText, item.SpeakerNotes, storedImage.StorageKey, storedImage.StorageUrl,
                    lastRendering.WidthPixels, lastRendering.HeightPixels,
                    extracted.SourceWidthEmus, extracted.SourceHeightEmus, item.ContentHash,
                    generatedPlan?.Objective ?? (string.IsNullOrWhiteSpace(item.Title) ? $"Explain slide {item.SlideNumber}." : $"Explain {item.Title}."),
                    generatedPlan?.ExpectedTimingSeconds ?? Math.Max(15, session.PlannedDurationMinutes * 60 / extracted.Slides.Count),
                    generatedPlan?.Transition ?? (string.IsNullOrWhiteSpace(nextTitle) ? "Invite questions and continue." : $"Transition to {nextTitle}."),
                    now);
                slides.Add(slide);
                slideArtifacts.AddRange(BuildSlideArtifacts(deck, slide, item, generatedPlan, evidence, now));
            }

            var briefVersion = Math.Max(1, deck.BriefVersion + 1);
            var briefArtifacts = await BuildBriefArtifactsAsync(deck, session, briefVersion, cancellationToken);
            db.SalesPresentationSlides.AddRange(slides);
            db.SalesMeetingArtifacts.AddRange(slideArtifacts);
            db.SalesMeetingArtifacts.AddRange(briefArtifacts);
            if (lastRendering is null)
                throw new SalesPresentationProcessingException("pptx_has_no_slides", "The PowerPoint file contains no slides.", false, true);
            deck.MarkProcessed(slides.Count, lastRendering.RendererName, lastRendering.RendererVersion,
                lastRendering.AnimationHandling, briefVersion, UtcNow());
            AddAudit(deck, AuditEventActions.SalesPresentationDeckProcessed, AuditEventOutcomes.Succeeded,
                "The presentation deck was scanned, extracted, rendered, planned, and briefed.");
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SalesPresentationProcessingException exception)
        {
            await RecordFailureAsync(deck, exception, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Sales presentation processing failed for deck {DeckId} in company {CompanyId}.", deck.Id, deck.CompanyId);
            await RecordFailureAsync(deck, new SalesPresentationProcessingException(
                SalesPresentationProblemCodes.ProcessingFailed,
                "The presentation could not be processed. Retry when the dependency is available.", true, false, exception),
                cancellationToken);
        }
    }

    private async Task RegenerateBriefAsync(SalesPresentationDeck deck, CancellationToken cancellationToken)
    {
        try
        {
            var session = await db.SalesMeetingSessions.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == deck.CompanyId && x.Id == deck.SessionId, cancellationToken);
            var version = deck.BriefVersion + 1;
            db.SalesMeetingArtifacts.AddRange(await BuildBriefArtifactsAsync(deck, session, version, cancellationToken));
            deck.CompleteBriefRegeneration(version, UtcNow());
            AddAudit(deck, AuditEventActions.SalesPresentationDeckProcessed, AuditEventOutcomes.Succeeded,
                "A new version of Alex's pre-meeting brief was generated.");
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Pre-meeting brief regeneration failed for deck {DeckId}.", deck.Id);
            deck.MarkFailed("brief_regeneration_failed", "The pre-meeting brief could not be regenerated. Retry when AI and knowledge services are available.", true, false, UtcNow());
            AddAudit(deck, AuditEventActions.SalesPresentationDeckProcessingFailed, AuditEventOutcomes.Failed,
                "Pre-meeting brief regeneration failed safely.");
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private async Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchKnowledgeAsync(
        SalesPresentationDeck deck,
        SalesMeetingSession session,
        CompanyMembership membership,
        ExtractedPresentationDeck extracted,
        CancellationToken cancellationToken)
    {
        var query = string.Join(' ', new[]
        {
            session.MeetingGoal,
            session.DemoScenario,
            string.Join(' ', extracted.Slides.Select(x => x.Title).Where(x => !string.IsNullOrWhiteSpace(x)).Take(20))
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return await knowledge.SearchAsync(new CompanyKnowledgeSemanticSearchQuery(
            deck.CompanyId, query, 10,
            new CompanyKnowledgeAccessContext(
                deck.CompanyId, membership.Id, membership.UserId, membership.Role.ToStorageValue(),
                ["sales", "knowledge"], deck.AgentId)), cancellationToken);
    }

    private async Task<IReadOnlyDictionary<string, GeneratedSlidePlan>> GenerateSlidePlansAsync(
        SalesPresentationDeck deck,
        ExtractedPresentationDeck extracted,
        IReadOnlyList<CompanyKnowledgeSearchResultDto> evidence,
        CancellationToken cancellationToken)
    {
        var plans = new Dictionary<string, GeneratedSlidePlan>(StringComparer.Ordinal);
        foreach (var batch in extracted.Slides.Chunk(options.Value.ReasoningBatchSize))
        {
            var sources = batch.Select(slide => new AgentAiSource(
                    SlideSourceId(deck.Id, slide.SlideNumber), "presentation_slide",
                    slide.Title ?? $"Slide {slide.SlideNumber}", Trim(slide.ExtractedText, 3000)))
                .Concat(evidence.Take(10).Select(item => new AgentAiSource(
                    $"knowledge-chunk:{item.ChunkId:N}", "company_knowledge", item.DocumentTitle,
                    Trim(item.Content, 1200))))
                .Take(50).ToArray();
            try
            {
                var result = await reasoning.ReasonAsync(new AgentReasoningRequest(
                    deck.CompanyId, deck.AgentId, AgentCapabilityIds.SalesProposalAdvice,
                    "1.0.0", "sales-meeting-slide-plan-v1", "1.0.0",
                    "Create a structured presenter plan for every supplied presentation_slide source. Include an objective, ordered talking points, timing, transition, and claims. Cite only supplied source IDs. A claim may be confirmed_fact only when a company_knowledge source supports it; otherwise use inference or needs_confirmation. Provide internal guidance only and do not execute actions.",
                    sources, [], [], deck.UploadedByUserId, IncludeClaims: false,
                    StructuredResultSchema: BuildSlidePlanSchema(batch.Length)), cancellationToken);
                foreach (var plan in ParseSlidePlans(result, sources.Select(x => x.Id).ToHashSet(StringComparer.Ordinal)))
                    plans[plan.SlideSourceId] = plan;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Slide claim review was unavailable for deck {DeckId}; claims will require human confirmation.", deck.Id);
            }
        }
        return plans;
    }

    private static IEnumerable<SalesMeetingArtifact> BuildSlideArtifacts(
        SalesPresentationDeck deck,
        SalesPresentationSlide slide,
        ExtractedPresentationSlide extracted,
        GeneratedSlidePlan? generatedPlan,
        IReadOnlyList<CompanyKnowledgeSearchResultDto> evidence,
        DateTime now)
    {
        var artifacts = new List<SalesMeetingArtifact>();
        var points = generatedPlan?.TalkingPoints is { Count: > 0 }
            ? generatedPlan.TalkingPoints
            : TalkingPoints(extracted);
        for (var index = 0; index < points.Count; index++)
        {
            artifacts.Add(Artifact(deck, slide.Id, deck.ProcessingVersion,
                SalesMeetingArtifactType.SlideTalkingPoint, "talking_points", index,
                points[index], SalesMeetingArtifactClassification.Recommendation, SlideSourceId(deck.Id, slide.SlideNumber), null, now));
            if (!string.IsNullOrWhiteSpace(extracted.ExtractedText))
                artifacts.Add(Artifact(deck, slide.Id, deck.ProcessingVersion,
                    SalesMeetingArtifactType.SlideClaim, "claims", index,
                    points[index], SalesMeetingArtifactClassification.NeedsConfirmation, null, null, now));
        }

        var slideSource = SlideSourceId(deck.Id, slide.SlideNumber);
        var knowledgeIds = evidence.Select(x => $"knowledge-chunk:{x.ChunkId:N}").ToHashSet(StringComparer.Ordinal);
        var reviewedClaims = generatedPlan?.Claims ?? [];
        var claimOrder = points.Count;
        foreach (var reviewed in reviewedClaims)
        {
            var citedKnowledge = reviewed.SourceIds.FirstOrDefault(knowledgeIds.Contains);
            var classification = ClassifyClaim(reviewed.Type, citedKnowledge is not null);
            artifacts.Add(Artifact(deck, slide.Id, deck.ProcessingVersion,
                SalesMeetingArtifactType.SlideClaim, "claims", claimOrder++, reviewed.Text,
                classification, citedKnowledge, generatedPlan!.RunId, now));
        }
        foreach (var sourceId in artifacts.Where(x => x.SourceId is not null)
                     .Select(x => x.SourceId!).Distinct().ToArray())
            artifacts.Add(Artifact(deck, slide.Id, deck.ProcessingVersion,
                SalesMeetingArtifactType.SlideSource, "sources", artifacts.Count, sourceId,
                SalesMeetingArtifactClassification.ConfirmedFact, sourceId, null, now));
        return artifacts;
    }

    internal static JsonObject BuildSlidePlanSchema(int maximumSlides) => new()
    {
        ["type"] = "object",
        ["additionalProperties"] = false,
        ["required"] = new JsonArray("resultVersion", "state", "safeExplanation", "slides"),
        ["properties"] = new JsonObject
        {
            ["resultVersion"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("1.0.0") },
            ["state"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("ready", "needs_review") },
            ["safeExplanation"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 500 },
            ["slides"] = new JsonObject
            {
                ["type"] = "array", ["minItems"] = 1, ["maxItems"] = maximumSlides,
                ["items"] = new JsonObject
                {
                    ["type"] = "object", ["additionalProperties"] = false,
                    ["required"] = new JsonArray("slideSourceId", "objective", "talkingPoints", "expectedTimingSeconds", "transition", "claims"),
                    ["properties"] = new JsonObject
                    {
                        ["slideSourceId"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 200 },
                        ["objective"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1000 },
                        ["talkingPoints"] = new JsonObject
                        {
                            ["type"] = "array", ["minItems"] = 1, ["maxItems"] = 8,
                            ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1000 }
                        },
                        ["expectedTimingSeconds"] = new JsonObject { ["type"] = "integer", ["minimum"] = 15, ["maximum"] = 900 },
                        ["transition"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 1000 },
                        ["claims"] = new JsonObject
                        {
                            ["type"] = "array", ["maxItems"] = 20,
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object", ["additionalProperties"] = false,
                                ["required"] = new JsonArray("text", "type", "sourceIds"),
                                ["properties"] = new JsonObject
                                {
                                    ["text"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 2000 },
                                    ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("confirmed_fact", "inference", "needs_confirmation") },
                                    ["sourceIds"] = new JsonObject
                                    {
                                        ["type"] = "array", ["maxItems"] = 12,
                                        ["items"] = new JsonObject { ["type"] = "string", ["minLength"] = 1, ["maxLength"] = 500 }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    };

    internal static IReadOnlyList<GeneratedSlidePlan> ParseSlidePlans(
        AgentReasoningResult result,
        IReadOnlySet<string> allowedSourceIds)
    {
        if (result.FailureCode is not null || result.StructuredResult?["slides"] is not JsonArray values)
            return [];
        var plans = new List<GeneratedSlidePlan>();
        foreach (var value in values.OfType<JsonObject>())
        {
            try
            {
                var slideSourceId = value["slideSourceId"]?.GetValue<string>()?.Trim();
                if (string.IsNullOrWhiteSpace(slideSourceId) ||
                    !slideSourceId.StartsWith("presentation-slide:", StringComparison.Ordinal) ||
                    !allowedSourceIds.Contains(slideSourceId)) continue;
                var points = (value["talkingPoints"] as JsonArray)?.Select(x => x?.GetValue<string>()?.Trim())
                    .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Trim(x, 1000)).Take(8).ToArray() ?? [];
                if (points.Length == 0) continue;
                var claims = new List<GeneratedSlideClaim>();
                foreach (var claim in (value["claims"] as JsonArray)?.OfType<JsonObject>() ?? [])
                {
                    var text = claim["text"]?.GetValue<string>();
                    var type = claim["type"]?.GetValue<string>();
                    var sourceIds = (claim["sourceIds"] as JsonArray)?.Select(x => x?.GetValue<string>()?.Trim())
                        .Where(x => !string.IsNullOrWhiteSpace(x) && allowedSourceIds.Contains(x!))
                        .Select(x => x!).Distinct(StringComparer.Ordinal).Take(12).ToArray() ?? [];
                    if (string.IsNullOrWhiteSpace(text) || !sourceIds.Contains(slideSourceId, StringComparer.Ordinal)) continue;
                    claims.Add(new GeneratedSlideClaim(Trim(text, 2000),
                        type is "confirmed_fact" or "inference" ? type : "needs_confirmation", sourceIds));
                }
                plans.Add(new GeneratedSlidePlan(
                    slideSourceId,
                    Trim(value["objective"]?.GetValue<string>(), 1000),
                    points,
                    Math.Clamp(value["expectedTimingSeconds"]?.GetValue<int>() ?? 60, 15, 900),
                    Trim(value["transition"]?.GetValue<string>(), 1000),
                    claims,
                    result.RunId));
            }
            catch (InvalidOperationException)
            {
                // The shared gateway validates schema first; defensive parsing still rejects malformed test/provider data.
            }
        }
        return plans;
    }

    internal sealed record GeneratedSlideClaim(string Text, string Type, IReadOnlyList<string> SourceIds);
    internal sealed record GeneratedSlidePlan(
        string SlideSourceId,
        string Objective,
        IReadOnlyList<string> TalkingPoints,
        int ExpectedTimingSeconds,
        string Transition,
        IReadOnlyList<GeneratedSlideClaim> Claims,
        Guid RunId);

    internal static SalesMeetingArtifactClassification ClassifyClaim(string type, bool hasApprovedKnowledgeSource) =>
        type == "confirmed_fact" && hasApprovedKnowledgeSource
            ? SalesMeetingArtifactClassification.ConfirmedFact
            : type == "inference"
                ? SalesMeetingArtifactClassification.Inference
                : SalesMeetingArtifactClassification.NeedsConfirmation;

    private async Task<IReadOnlyList<SalesMeetingArtifact>> BuildBriefArtifactsAsync(
        SalesPresentationDeck deck,
        SalesMeetingSession session,
        int version,
        CancellationToken cancellationToken)
    {
        var now = UtcNow();
        var artifacts = new List<SalesMeetingArtifact>();
        try
        {
            var intelligence = await decisions.BuildIntelligenceBriefAsync(
                deck.CompanyId, deck.AgentId, deck.UploadedByUserId,
                session.DealId.HasValue
                    ? new SalesIntelligenceBriefRequest(DealId: session.DealId, Objective: session.MeetingGoal)
                    : new SalesIntelligenceBriefRequest(LeadId: session.LeadId, Objective: session.MeetingGoal),
                cancellationToken);
            var order = 0;
            foreach (var fact in intelligence.ConfirmedFacts)
                artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefFact,
                    "confirmed_facts", order++, $"{fact.Label}: {fact.Value}",
                    SalesMeetingArtifactClassification.ConfirmedFact, fact.SourceId, null, now));
            order = 0;
            foreach (var need in intelligence.QualificationGaps.Concat(intelligence.BuyingSignals).Distinct())
                artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefNeed,
                    "likely_needs", order++, need, SalesMeetingArtifactClassification.Inference, null,
                    intelligence.Advice.RunId, now));
            order = 0;
            foreach (var risk in intelligence.RiskSignals)
                artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefRisk,
                    "deal_risks", order++, risk, SalesMeetingArtifactClassification.Inference, null,
                    intelligence.Advice.RunId, now));
            order = 0;
            foreach (var gap in intelligence.QualificationGaps.DefaultIfEmpty("Confirm the customer's desired outcome and decision process."))
                artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefQuestion,
                    "recommended_questions", order++, $"Clarify: {gap}",
                    SalesMeetingArtifactClassification.Recommendation, null, intelligence.Advice.RunId, now));
            artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefRecommendation,
                "recommendations", 0, intelligence.Advice.Summary,
                SalesMeetingArtifactClassification.Recommendation, null, intelligence.Advice.RunId, now));
            foreach (var missing in intelligence.Advice.MissingEvidence.Concat(intelligence.QualificationGaps).Distinct())
                artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefMissingEvidence,
                    "missing_evidence", artifacts.Count, missing,
                    SalesMeetingArtifactClassification.NeedsConfirmation, null, intelligence.Advice.RunId, now));

            if (session.DealId.HasValue)
            {
                var strategy = await decisions.AnalyzeDealStrategyAsync(deck.CompanyId, deck.AgentId, deck.UploadedByUserId,
                    new SalesDealStrategyRequest(session.DealId.Value, Objective: session.MeetingGoal), cancellationToken);
                foreach (var risk in strategy.RiskFactors)
                    artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefRisk,
                        "deal_risks", artifacts.Count, risk, SalesMeetingArtifactClassification.Inference,
                        $"sales-deal:{session.DealId.Value:N}", strategy.Advice.RunId, now));
                foreach (var step in strategy.RecoveryPlan)
                    artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefNextStep,
                        "desired_next_step", step.Order, step.Milestone,
                        SalesMeetingArtifactClassification.Recommendation, step.SourceIds.FirstOrDefault(),
                        strategy.Advice.RunId, now));
                foreach (var unknown in strategy.Unknowns)
                    artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefMissingEvidence,
                        "missing_evidence", artifacts.Count, unknown,
                        SalesMeetingArtifactClassification.NeedsConfirmation, null, strategy.Advice.RunId, now));

                var proposal = await decisions.AdviseProposalAsync(deck.CompanyId, deck.AgentId, deck.UploadedByUserId,
                    new SalesProposalAdviceRequest(session.DealId.Value, session.DemoScenario,
                        Objective: session.MeetingGoal), cancellationToken);
                foreach (var positioning in proposal.ApprovedClaims)
                    artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefPositioning,
                        "product_positioning", artifacts.Count, positioning,
                        SalesMeetingArtifactClassification.ConfirmedFact,
                        proposal.Validations.SelectMany(x => x.SourceIds).FirstOrDefault(), proposal.Advice.RunId, now));
                foreach (var unknown in proposal.Unknowns)
                    artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefMissingEvidence,
                        "missing_evidence", artifacts.Count, unknown,
                        SalesMeetingArtifactClassification.NeedsConfirmation, null, proposal.Advice.RunId, now));
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Alex pre-meeting brief generation degraded safely for deck {DeckId}.", deck.Id);
            artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefMissingEvidence,
                "missing_evidence", 0,
                "Alex's generated recommendations are unavailable. Review the confirmed sales records and approved company sources before the meeting.",
                SalesMeetingArtifactClassification.NeedsConfirmation, null, null, now));
        }

        if (!artifacts.Any(x => x.ArtifactType == SalesMeetingArtifactType.BriefNextStep))
            artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefNextStep,
                "desired_next_step", 0, "Agree and record a customer-reviewed next step.",
                SalesMeetingArtifactClassification.Recommendation, null, null, now));
        if (!artifacts.Any(x => x.ArtifactType == SalesMeetingArtifactType.BriefPositioning))
            artifacts.Add(Artifact(deck, null, version, SalesMeetingArtifactType.BriefPositioning,
                "product_positioning", 0, "Use only product claims supported by the approved company sources listed in the slide plan.",
                SalesMeetingArtifactClassification.NeedsConfirmation, null, null, now));
        return artifacts;
    }

    private async Task RecordFailureAsync(
        SalesPresentationDeck deck,
        SalesPresentationProcessingException exception,
        CancellationToken cancellationToken)
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(x =>
                     x.State == EntityState.Added &&
                     (x.Entity is SalesPresentationSlide || x.Entity is SalesMeetingArtifact)))
            entry.State = EntityState.Detached;
        var canRetry = exception.CanRetry && deck.ProcessingAttemptCount < options.Value.MaximumAttempts;
        deck.MarkFailed(exception.Code, exception.SafeMessage, canRetry, exception.Blocked, UtcNow());
        AddAudit(deck, AuditEventActions.SalesPresentationDeckProcessingFailed,
            exception.Blocked ? AuditEventOutcomes.Blocked : AuditEventOutcomes.Failed,
            exception.SafeMessage);
        await db.SaveChangesAsync(cancellationToken);
    }

    private void AddAudit(SalesPresentationDeck deck, string action, string outcome, string rationale)
    {
        db.AuditEvents.Add(new AuditEvent(
            Guid.NewGuid(), deck.CompanyId, AuditActorTypes.System, null, action,
            "sales_presentation_deck", deck.Id.ToString("D"), outcome, rationale,
            ["presentation deck", "approved company knowledge", "sales records"],
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["sessionId"] = deck.SessionId.ToString("D"),
                ["processingVersion"] = deck.ProcessingVersion.ToString(),
                ["attempt"] = deck.ProcessingAttemptCount.ToString(),
                ["failureCode"] = deck.FailureCode
            }, $"sales-presentation:{deck.Id:N}:v{deck.ProcessingVersion}", UtcNow()));
    }

    private static SalesMeetingArtifact Artifact(
        SalesPresentationDeck deck, Guid? slideId, int version, SalesMeetingArtifactType type,
        string section, int order, string content, SalesMeetingArtifactClassification classification,
        string? sourceId, Guid? aiRunId, DateTime now) =>
        new(Guid.NewGuid(), deck.CompanyId, deck.SessionId, deck.Id, slideId, version, type,
            section, Math.Max(0, order), Trim(content, 4000), classification, sourceId, aiRunId, now);

    private static IReadOnlyList<string> TalkingPoints(ExtractedPresentationSlide slide)
    {
        var values = slide.ExtractedText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.Equals(x, slide.Title, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToArray();
        return values.Length > 0 ? values : ["Review the visual content and invite the customer to react."];
    }

    private static string SlideSourceId(Guid deckId, int slideNumber) =>
        $"presentation-slide:{deckId:N}:{slideNumber}";

    private static string Trim(string? value, int max)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "No additional content was produced." : value.Trim();
        return normalized[..Math.Min(normalized.Length, max)];
    }

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;
}
