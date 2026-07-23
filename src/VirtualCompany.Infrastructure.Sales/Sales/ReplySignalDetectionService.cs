using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class DeterministicReplySignalDetectionService : IReplySignalDetectionService
{
    private static readonly string[] PriceResistanceTerms =
    [
        "too expensive",
        "over budget",
        "budget right now",
        "cost is high",
        "price is high",
        "pricing is high",
        "discount",
        "cheaper",
        "can't afford",
        "cannot afford"
    ];

    private static readonly string[] BuyingIntentTerms =
    [
        "schedule a demo",
        "book a demo",
        "next step",
        "send the contract",
        "send a contract",
        "send a quote",
        "send proposal",
        "looks good",
        "move forward",
        "procurement",
        "approval process",
        "start next week",
        "ready to proceed"
    ];

    public ReplySignalDetectionResult Detect(ReplySignalDetectionRequest request)
    {
        if (request.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(request));
        }

        var detectedUtc = request.DetectedUtc ?? DateTime.UtcNow;
        var windowStart = request.Messages.Where(x => x.OccurredUtc.HasValue).Min(x => x.OccurredUtc);
        var windowEnd = request.Messages.Where(x => x.OccurredUtc.HasValue).Max(x => x.OccurredUtc);
        var latestInbound = request.Messages.LastOrDefault(x => x.Direction.Equals("inbound", StringComparison.OrdinalIgnoreCase));
        var text = string.Join(" ", request.Messages.Select(x => $"{x.Subject} {x.Body}")).Trim();
        var signals = new List<DetectedReplySignal>();

        AddKeywordSignal(request, signals, DealIntelligenceSignalTypes.PriceResistance, PriceResistanceTerms, text, detectedUtc, windowStart, windowEnd, "The reply raised a pricing or budget objection.");
        AddKeywordSignal(request, signals, DealIntelligenceSignalTypes.BuyingIntent, BuyingIntentTerms, text, detectedUtc, windowStart, windowEnd, "The reply expressed clear interest in moving to a next step.");

        var unansweredOutboundCount = CountRecentUnansweredOutbound(request.Messages);
        if (latestInbound is null && unansweredOutboundCount >= 2)
        {
            signals.Add(BuildSignal(
                request,
                DealIntelligenceSignalTypes.Ghosting,
                Math.Min(0.95m, 0.60m + (unansweredOutboundCount * 0.10m)),
                "The conversation has multiple unanswered outbound follow-ups and no recent customer reply.",
                detectedUtc,
                windowStart,
                windowEnd,
                new Dictionary<string, string?>
                {
                    ["unansweredOutboundCount"] = unansweredOutboundCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["heuristic"] = "two_or_more_unanswered_outbound_messages"
                }));
        }

        return new ReplySignalDetectionResult(request.CompanyId, signals);
    }

    private static void AddKeywordSignal(ReplySignalDetectionRequest request, List<DetectedReplySignal> signals, string signalType, IReadOnlyList<string> terms, string text, DateTime detectedUtc, DateTime? windowStart, DateTime? windowEnd, string explanation)
    {
        var matches = terms.Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)).Take(5).ToArray();
        if (matches.Length == 0)
        {
            return;
        }

        signals.Add(BuildSignal(
            request,
            signalType,
            Math.Min(0.95m, 0.70m + (matches.Length * 0.05m)),
            explanation,
            detectedUtc,
            windowStart,
            windowEnd,
            new Dictionary<string, string?>
            {
                ["matchedTerms"] = string.Join(", ", matches),
                ["detector"] = "deterministic_keyword_baseline"
            }));
    }

    private static int CountRecentUnansweredOutbound(IReadOnlyList<ReplySignalContextMessage> messages)
    {
        var ordered = messages.OrderBy(x => x.OccurredUtc ?? DateTime.MinValue).ToArray();
        var latestInboundUtc = ordered.LastOrDefault(x => x.Direction.Equals("inbound", StringComparison.OrdinalIgnoreCase))?.OccurredUtc;
        return ordered.Count(x =>
            x.Direction.Equals("outbound", StringComparison.OrdinalIgnoreCase) &&
            (!latestInboundUtc.HasValue || x.OccurredUtc > latestInboundUtc.Value));
    }

    private static DetectedReplySignal BuildSignal(ReplySignalDetectionRequest request, string signalType, decimal confidence, string explanation, DateTime detectedUtc, DateTime? windowStart, DateTime? windowEnd, IReadOnlyDictionary<string, string?> evidence) =>
        new(
            request.CompanyId,
            signalType,
            confidence,
            explanation,
            request.DealId,
            request.ConversationId,
            request.MessageId,
            request.SequenceId,
            request.SequenceStepId,
            request.SourceType,
            request.SourceMessageId,
            request.SourceThreadId,
            detectedUtc,
            windowStart,
            windowEnd,
            evidence);
}

public sealed class DealIntelligenceSignalRepository : IDealIntelligenceSignalRepository
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;

    public DealIntelligenceSignalRepository(VirtualCompanyDbContext dbContext, ICompanyContextAccessor? companyContextAccessor = null)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
    }

    public async Task UpsertAsync(Guid companyId, IReadOnlyList<DetectedReplySignal> signals, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        foreach (var signal in signals.Where(x => x.CompanyId == companyId))
        {
            var metadataJson = JsonSerializer.Serialize(signal.Evidence);
            var existing = await FindExistingAsync(companyId, signal, cancellationToken);
            if (existing is null)
            {
                _dbContext.DealIntelligenceSignals.Add(new DealIntelligenceSignal(
                    Guid.NewGuid(),
                    companyId,
                    signal.SignalType,
                    signal.Confidence,
                    signal.Explanation,
                    signal.DetectedUtc,
                    signal.DealId,
                    signal.ConversationId,
                    signal.MessageId,
                    signal.SequenceId,
                    signal.SequenceStepId,
                    sourceType: signal.SourceType,
                    sourceMessageId: signal.SourceMessageId,
                    sourceThreadId: signal.SourceThreadId,
                    sourceMetadataJson: metadataJson,
                    sourceWindowStartedUtc: signal.SourceWindowStartedUtc,
                    sourceWindowEndedUtc: signal.SourceWindowEndedUtc));
                continue;
            }

            existing.UpdateDetection(signal.Confidence, signal.Explanation, metadataJson, signal.DetectedUtc, signal.SourceWindowStartedUtc, signal.SourceWindowEndedUtc);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DealIntelligenceSignalDto>> ListLatestByDealAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        return await Project(_dbContext.DealIntelligenceSignals.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.DealId == dealId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DealIntelligenceSignalDto>> ListLatestByConversationAsync(Guid companyId, Guid conversationId, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        return await Project(_dbContext.DealIntelligenceSignals.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.ConversationId == conversationId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DealIntelligenceSignalDto>> ListLatestByCompanyAsync(Guid companyId, string? signalType, int limit, CancellationToken cancellationToken)
    {
        EnsureTenant(companyId);
        var query = _dbContext.DealIntelligenceSignals.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(signalType))
        {
            var normalized = DealIntelligenceSignalTypes.Normalize(signalType);
            query = query.Where(x => x.SignalType == normalized);
        }

        return await Project(query)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(cancellationToken);
    }

    private async Task<DealIntelligenceSignal?> FindExistingAsync(Guid companyId, DetectedReplySignal signal, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(signal.SourceMessageId))
        {
            return await _dbContext.DealIntelligenceSignals.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SourceType == signal.SourceType && x.SourceMessageId == signal.SourceMessageId && x.SignalType == signal.SignalType, cancellationToken);
        }

        return await _dbContext.DealIntelligenceSignals.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.SignalType == signal.SignalType)
            .Where(x => signal.DealId.HasValue && x.DealId == signal.DealId || signal.ConversationId.HasValue && x.ConversationId == signal.ConversationId)
            .OrderByDescending(x => x.DetectedUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static IQueryable<DealIntelligenceSignalDto> Project(IQueryable<DealIntelligenceSignal> query) =>
        query.OrderByDescending(x => x.DetectedUtc)
            .Select(x => new DealIntelligenceSignalDto(
                x.Id,
                x.CompanyId,
                x.DealId,
                x.ConversationId,
                x.MessageId,
                x.SequenceId,
                x.SequenceStepId,
                x.SignalType,
                x.SignalState,
                x.ConfidenceScore,
                x.Explanation,
                x.SourceType,
                x.SourceMessageId,
                x.SourceThreadId,
                x.SourceMetadataJson,
                x.DetectedUtc,
                x.SourceWindowStartedUtc,
                x.SourceWindowEndedUtc,
                x.UpdatedUtc));

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new InvalidOperationException("Deal intelligence signals are outside the active company context.");
        }
    }
}

public sealed class ReplySignalDetectionPipeline : IReplySignalDetectionPipeline
{
    private const int ConversationWindowSize = 12;
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IReplySignalDetectionService _detector;
    private readonly IDealIntelligenceSignalRepository _repository;

    public ReplySignalDetectionPipeline(VirtualCompanyDbContext dbContext, IReplySignalDetectionService detector, IDealIntelligenceSignalRepository repository)
    {
        _dbContext = dbContext;
        _detector = detector;
        _repository = repository;
    }

    public async Task<ReplySignalDetectionResult> AnalyzeInboundReplyAsync(AnalyzeInboundReplySignalsCommand command, CancellationToken cancellationToken)
    {
        EnsureCompany(command.CompanyId);
        var history = await BuildEmailHistoryAsync(command.CompanyId, command.Provider, command.ProviderThreadId, command.SenderEmail, cancellationToken);
        var messages = history
            .Append(new ReplySignalContextMessage("inbound", command.Subject, command.Body, command.SenderEmail, command.ReceivedUtc, command.ProviderMessageId, command.ProviderThreadId))
            .OrderBy(x => x.OccurredUtc ?? DateTime.MinValue)
            .TakeLast(ConversationWindowSize)
            .ToArray();

        var result = _detector.Detect(new ReplySignalDetectionRequest(
            command.CompanyId,
            messages,
            command.DealId,
            command.ConversationId,
            command.MessageId,
            command.SequenceId,
            command.SequenceStepId,
            DealIntelligenceSignalSourceTypes.InboundReply,
            command.ProviderMessageId,
            command.ProviderThreadId,
            DateTime.UtcNow));

        await _repository.UpsertAsync(command.CompanyId, result.Signals, cancellationToken);
        return result;
    }

    public async Task<ReplySignalDetectionResult> AnalyzeConversationAsync(AnalyzeConversationSignalsCommand command, CancellationToken cancellationToken)
    {
        EnsureCompany(command.CompanyId);
        var history = await BuildEmailHistoryAsync(command.CompanyId, null, command.ProviderThreadId, command.SenderEmail, cancellationToken);
        var result = _detector.Detect(new ReplySignalDetectionRequest(
            command.CompanyId,
            history.TakeLast(ConversationWindowSize).ToArray(),
            command.DealId,
            command.ConversationId,
            SourceType: DealIntelligenceSignalSourceTypes.ConversationReanalysis,
            SourceThreadId: command.ProviderThreadId,
            DetectedUtc: DateTime.UtcNow));

        await _repository.UpsertAsync(command.CompanyId, result.Signals, cancellationToken);
        return result;
    }

    private async Task<IReadOnlyList<ReplySignalContextMessage>> BuildEmailHistoryAsync(Guid companyId, string? provider, string? providerThreadId, string? senderEmail, CancellationToken cancellationToken)
    {
        var query = _dbContext.SalesEmailLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && !x.IsDeleted && x.LinkKind == SalesEmailLinkKinds.Message);

        if (!string.IsNullOrWhiteSpace(providerThreadId))
        {
            query = query.Where(x => x.ExternalThreadId == providerThreadId);
        }
        else if (!string.IsNullOrWhiteSpace(senderEmail))
        {
            var normalizedSender = senderEmail.Trim().ToLowerInvariant();
            query = query.Where(x => x.ContactId != null && _dbContext.Contacts.IgnoreQueryFilters().Any(contact => contact.CompanyId == companyId && contact.Id == x.ContactId && contact.Email == normalizedSender));
        }

        if (!string.IsNullOrWhiteSpace(provider))
        {
            query = query.Where(x => x.Provider == provider);
        }

        return await query
            .OrderByDescending(x => x.CreatedUtc)
            .Take(ConversationWindowSize)
            .Select(x => new ReplySignalContextMessage(
                "inbound",
                null,
                x.Rationale,
                null,
                x.CreatedUtc,
                x.ExternalMessageId,
                x.ExternalThreadId))
            .ToListAsync(cancellationToken);
    }

    private static void EnsureCompany(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }
    }
}
