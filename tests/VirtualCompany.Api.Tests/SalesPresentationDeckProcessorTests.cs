using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class SalesPresentationDeckProcessorTests
{
    [Fact]
    public async Task Background_processor_persists_rendered_plan_and_safe_brief_without_crossing_company_scope()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var membershipId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var deckId = Guid.NewGuid();
        var context = new TestCompanyContextAccessor();
        await using var db = new VirtualCompanyDbContext(
            new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase($"deck-processor-{Guid.NewGuid():N}").Options,
            context);
        var now = DateTime.UtcNow;
        db.Companies.Add(new Company(companyId, "Processor Company"));
        db.Users.Add(new User(userId, "owner@example.com", "Owner", "test", userId.ToString("N")));
        db.CompanyMemberships.Add(new CompanyMembership(
            membershipId, companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
        db.Agents.Add(new Agent(agentId, companyId, "alex", "Alex", "Sales", "Sales", null,
            AgentSeniority.Senior, AgentStatus.Active));
        db.SalesMeetingSessions.Add(new SalesMeetingSession(
            sessionId, companyId, Guid.NewGuid(), Guid.NewGuid(), null, null, Guid.NewGuid(),
            "Confirm fit", "Finance leaders", 30, null, "provider-id",
            SalesMeetingConsentStatus.Pending, SalesMeetingRetentionPolicy.Standard, 365,
            now.AddHours(1), userId, now));
        db.SalesPresentationDecks.Add(new SalesPresentationDeck(
            deckId, companyId, sessionId, agentId, 1, "Deck", "deck.pptx",
            "application/vnd.openxmlformats-officedocument.presentationml.presentation", 100,
            new string('a', 64), "safe/deck.pptx", null, userId, now));
        await db.SaveChangesAsync();

        var knowledge = new RecordingKnowledgeSearch();
        var reasoning = new StructuredReasoningGateway();
        var storage = new RecordingStorage();
        var logger = new RecordingLogger();
        var processor = new SalesPresentationDeckProcessor(
            db, storage, new CleanScanner(), new OneSlideExtractor(),
            new DeterministicSvgSalesPresentationSlideRenderer(), knowledge, reasoning,
            new UnavailableDecisionService(), Options.Create(new SalesPresentationOptions()),
            TimeProvider.System, logger);

        await processor.ProcessAsync(companyId, deckId, CancellationToken.None);

        var deck = await db.SalesPresentationDecks.IgnoreQueryFilters().SingleAsync();
        Assert.True(deck.Status == SalesPresentationDeckStatus.Processed,
            $"Processing ended as {deck.Status}: {deck.FailureCode} - {deck.FailureSummary}. {logger.Exception}");
        var slide = await db.SalesPresentationSlides.IgnoreQueryFilters().SingleAsync();
        var artifacts = await db.SalesMeetingArtifacts.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(1600, slide.ImageWidthPixels);
        Assert.Equal(900, slide.ImageHeightPixels);
        Assert.Equal("Show the customer outcome", slide.Objective);
        Assert.Equal(75, slide.ExpectedDurationSeconds);
        Assert.Contains(artifacts, x => x.ArtifactType == SalesMeetingArtifactType.SlideTalkingPoint && x.Content == "Lead with the result");
        Assert.Contains(artifacts, x => x.ArtifactType == SalesMeetingArtifactType.SlideClaim &&
                                        x.Classification == SalesMeetingArtifactClassification.NeedsConfirmation);
        Assert.Contains(artifacts, x => x.ArtifactType == SalesMeetingArtifactType.BriefMissingEvidence);
        Assert.Equal(companyId, knowledge.Query!.CompanyId);
        Assert.Equal(membershipId, knowledge.Query.AccessContext!.MembershipId);
        Assert.Equal(userId, knowledge.Query.AccessContext.UserId);
        Assert.Equal(companyId, reasoning.Request!.CompanyId);
        Assert.NotNull(reasoning.Request.StructuredResultSchema);
        Assert.Equal(1, storage.ImageWriteCount);
    }

    private sealed class OneSlideExtractor : ISalesPresentationDeckExtractor
    {
        public Task<ExtractedPresentationDeck> ExtractAsync(Stream content, int maximumSlides, CancellationToken cancellationToken) =>
            Task.FromResult(new ExtractedPresentationDeck(12_192_000, 6_858_000, [
                new ExtractedPresentationSlide(1, "Outcome", "Revenue improves", "Ask about timing", new string('b', 64))
            ]));
    }

    private sealed class RecordingKnowledgeSearch : ICompanyKnowledgeSearchService
    {
        public CompanyKnowledgeSemanticSearchQuery? Query { get; private set; }
        public Task<IReadOnlyList<CompanyKnowledgeSearchResultDto>> SearchAsync(
            CompanyKnowledgeSemanticSearchQuery query, CancellationToken cancellationToken)
        {
            Query = query;
            return Task.FromResult<IReadOnlyList<CompanyKnowledgeSearchResultDto>>([]);
        }
    }

    private sealed class StructuredReasoningGateway : IAgentReasoningGateway
    {
        public AgentReasoningRequest? Request { get; private set; }

        public Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken cancellationToken)
        {
            Request = request;
            var slideSource = request.Sources.Single(x => x.Type == "presentation_slide").Id;
            var structured = JsonNode.Parse($$"""
                {"resultVersion":"1.0.0","state":"needs_review","safeExplanation":"Review the unsupported claim.","slides":[
                  {"slideSourceId":"{{slideSource}}","objective":"Show the customer outcome","talkingPoints":["Lead with the result"],
                   "expectedTimingSeconds":75,"transition":"Ask for their reaction","claims":[
                     {"text":"Revenue improves","type":"needs_confirmation","sourceIds":["{{slideSource}}"]}
                   ]}
                ]}
                """)!.AsObject();
            return Task.FromResult(new AgentReasoningResult(
                Guid.NewGuid(), AgentAiRunStatuses.NeedsReview, "1.0.0", "Review", [], .5m,
                [], [], [], [slideSource], StructuredResult: structured));
        }

        public Task<AgentReasoningResult?> GetRunAsync(
            Guid companyId, Guid agentId, Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<AgentReasoningResult?>(null);
    }

    private sealed class RecordingStorage : ICompanyDocumentStorage
    {
        public int ImageWriteCount { get; private set; }
        public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new MemoryStream([1]));
        public Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken)
        {
            ImageWriteCount++;
            return Task.FromResult(new DocumentStorageWriteResult(request.StorageKey, null));
        }
        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class CleanScanner : ICompanyDocumentVirusScanner
    {
        public Task<CompanyDocumentVirusScanResult> ScanAsync(
            CompanyDocumentVirusScanRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(CompanyDocumentVirusScanResult.CleanPlaceholder("test-scanner"));
    }

    private sealed class UnavailableDecisionService : ISalesAgentDecisionService
    {
        private static Task<T> Fail<T>() => Task.FromException<T>(new InvalidOperationException("Not configured in this test."));
        public Task<SalesIntelligenceBriefResult> BuildIntelligenceBriefAsync(Guid companyId, Guid agentId, Guid? actorUserId, SalesIntelligenceBriefRequest request, CancellationToken cancellationToken) => Fail<SalesIntelligenceBriefResult>();
        public Task<SalesNextBestActionResult> RecommendNextActionsAsync(Guid companyId, Guid agentId, Guid? actorUserId, SalesNextBestActionRequest request, CancellationToken cancellationToken) => Fail<SalesNextBestActionResult>();
        public Task<SalesDealStrategyResult> AnalyzeDealStrategyAsync(Guid companyId, Guid agentId, Guid? actorUserId, SalesDealStrategyRequest request, CancellationToken cancellationToken) => Fail<SalesDealStrategyResult>();
        public Task<SalesForecastScenarioResult> AnalyzeForecastAsync(Guid companyId, Guid agentId, Guid? actorUserId, SalesForecastScenarioRequest request, CancellationToken cancellationToken) => Fail<SalesForecastScenarioResult>();
        public Task<SalesCampaignOptimizationResult> OptimizeCampaignsAsync(Guid companyId, Guid agentId, Guid? actorUserId, SalesCampaignOptimizationRequest request, CancellationToken cancellationToken) => Fail<SalesCampaignOptimizationResult>();
        public Task<SalesProposalAdviceResult> AdviseProposalAsync(Guid companyId, Guid agentId, Guid? actorUserId, SalesProposalAdviceRequest request, CancellationToken cancellationToken) => Fail<SalesProposalAdviceResult>();
    }

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public Guid? CompanyId => null;
        public Guid? UserId => null;
        public bool IsResolved => false;
        public ResolvedCompanyMembershipContext? Membership => null;
        public void SetCompanyId(Guid? companyId) { }
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext) { }
    }

    private sealed class RecordingLogger : ILogger<SalesPresentationDeckProcessor>
    {
        public Exception? Exception { get; private set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Exception ??= exception;
    }
}
