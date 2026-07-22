using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class DealIntelligenceSignalTests
{
    [Fact]
    public void Signal_requires_supported_type_confidence_and_explanation()
    {
        var companyId = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => new DealIntelligenceSignal(Guid.NewGuid(), companyId, "unsupported", 0.8m, "Detected.", DateTime.UtcNow));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DealIntelligenceSignal(Guid.NewGuid(), companyId, DealIntelligenceSignalTypes.BuyingIntent, 1.1m, "Detected.", DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => new DealIntelligenceSignal(Guid.NewGuid(), companyId, DealIntelligenceSignalTypes.BuyingIntent, 0.9m, "", DateTime.UtcNow));
    }

    [Fact]
    public void Deterministic_detector_finds_buying_intent_and_price_resistance()
    {
        var companyId = Guid.NewGuid();
        var detector = new DeterministicReplySignalDetectionService();

        var result = detector.Detect(new ReplySignalDetectionRequest(
            companyId,
            [
                new ReplySignalContextMessage("inbound", "Re: proposal", "This looks good, can we schedule a demo next week? It is a bit too expensive for our budget right now.", "buyer@example.com", DateTime.UtcNow, "msg-1", "thread-1")
            ],
            SourceMessageId: "msg-1",
            SourceThreadId: "thread-1"));

        Assert.Contains(result.Signals, x => x.SignalType == DealIntelligenceSignalTypes.BuyingIntent && x.Confidence > 0);
        Assert.Contains(result.Signals, x => x.SignalType == DealIntelligenceSignalTypes.PriceResistance && x.Confidence > 0);
        Assert.All(result.Signals, x => Assert.False(string.IsNullOrWhiteSpace(x.Explanation)));
    }

    [Fact]
    public void Deterministic_detector_finds_ghosting_from_unanswered_outbound_history()
    {
        var companyId = Guid.NewGuid();
        var detector = new DeterministicReplySignalDetectionService();

        var result = detector.Detect(new ReplySignalDetectionRequest(
            companyId,
            [
                new ReplySignalContextMessage("outbound", "Checking in", "Following up on the proposal.", "alex@virtual.test", DateTime.UtcNow.AddDays(-8), "out-1", "thread-1"),
                new ReplySignalContextMessage("outbound", "Next steps", "Do you want to continue?", "alex@virtual.test", DateTime.UtcNow.AddDays(-3), "out-2", "thread-1")
            ],
            SourceType: DealIntelligenceSignalSourceTypes.ConversationReanalysis,
            SourceThreadId: "thread-1"));

        var signal = Assert.Single(result.Signals);
        Assert.Equal(DealIntelligenceSignalTypes.Ghosting, signal.SignalType);
        Assert.True(signal.Confidence >= 0.8m);
    }

    [Fact]
    public async Task Repository_upserts_by_company_message_and_signal_type()
    {
        var companyId = Guid.NewGuid();
        await using var dbContext = CreateDbContext(companyId);
        dbContext.Companies.Add(new Company(companyId, "Tenant A"));
        await dbContext.SaveChangesAsync();

        var repository = new DealIntelligenceSignalRepository(dbContext, new FixedCompanyContextAccessor(companyId));
        var signal = new DetectedReplySignal(
            companyId,
            DealIntelligenceSignalTypes.BuyingIntent,
            0.8m,
            "The buyer asked for a demo.",
            null,
            null,
            null,
            null,
            null,
            DealIntelligenceSignalSourceTypes.InboundReply,
            "msg-1",
            "thread-1",
            DateTime.UtcNow,
            DateTime.UtcNow.AddMinutes(-1),
            DateTime.UtcNow,
            new Dictionary<string, string?> { ["matchedTerms"] = "schedule a demo" });

        await repository.UpsertAsync(companyId, [signal], CancellationToken.None);
        await repository.UpsertAsync(companyId, [signal with { Confidence = 0.9m }], CancellationToken.None);

        var rows = await dbContext.DealIntelligenceSignals.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync();
        var row = Assert.Single(rows);
        Assert.Equal(0.9m, row.ConfidenceScore);
    }

    [Fact]
    public async Task Repository_blocks_cross_tenant_reads()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using var dbContext = CreateDbContext(tenantA);
        var repository = new DealIntelligenceSignalRepository(dbContext, new FixedCompanyContextAccessor(tenantA));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ListLatestByCompanyAsync(tenantB, null, 10, CancellationToken.None));
    }

    private static VirtualCompanyDbContext CreateDbContext(Guid companyId)
    {
        var options = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new VirtualCompanyDbContext(options, new FixedCompanyContextAccessor(companyId));
    }

    private sealed class FixedCompanyContextAccessor : ICompanyContextAccessor
    {
        public FixedCompanyContextAccessor(Guid companyId)
        {
            CompanyId = companyId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId { get; private set; }
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership { get; private set; }
        public void SetCompanyId(Guid? companyId) => CompanyId = companyId;
        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            Membership = companyContext;
            CompanyId = companyContext?.CompanyId;
            UserId = companyContext?.UserId;
        }
    }
}
