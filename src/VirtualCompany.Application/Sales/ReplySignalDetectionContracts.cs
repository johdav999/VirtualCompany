namespace VirtualCompany.Application.Sales;

public interface IReplySignalDetectionPipeline
{
    Task<ReplySignalDetectionResult> AnalyzeInboundReplyAsync(AnalyzeInboundReplySignalsCommand command, CancellationToken cancellationToken);
    Task<ReplySignalDetectionResult> AnalyzeConversationAsync(AnalyzeConversationSignalsCommand command, CancellationToken cancellationToken);
}

public interface IReplySignalDetectionService
{
    ReplySignalDetectionResult Detect(ReplySignalDetectionRequest request);
}

public interface IDealIntelligenceSignalRepository
{
    Task UpsertAsync(Guid companyId, IReadOnlyList<DetectedReplySignal> signals, CancellationToken cancellationToken);
    Task<IReadOnlyList<DealIntelligenceSignalDto>> ListLatestByDealAsync(Guid companyId, Guid dealId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DealIntelligenceSignalDto>> ListLatestByConversationAsync(Guid companyId, Guid conversationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<DealIntelligenceSignalDto>> ListLatestByCompanyAsync(Guid companyId, string? signalType, int limit, CancellationToken cancellationToken);
}

public sealed record AnalyzeInboundReplySignalsCommand(
    Guid CompanyId,
    string Provider,
    string ProviderMessageId,
    string? ProviderThreadId,
    string? InternetMessageId,
    string? Subject,
    string? Body,
    string SenderEmail,
    DateTime? ReceivedUtc,
    Guid? DealId = null,
    Guid? ConversationId = null,
    Guid? MessageId = null,
    Guid? SequenceId = null,
    Guid? SequenceStepId = null);

public sealed record AnalyzeConversationSignalsCommand(
    Guid CompanyId,
    Guid? DealId = null,
    Guid? ConversationId = null,
    string? ProviderThreadId = null,
    string? SenderEmail = null);

public sealed record ReplySignalDetectionRequest(
    Guid CompanyId,
    IReadOnlyList<ReplySignalContextMessage> Messages,
    Guid? DealId = null,
    Guid? ConversationId = null,
    Guid? MessageId = null,
    Guid? SequenceId = null,
    Guid? SequenceStepId = null,
    string SourceType = "inbound_reply",
    string? SourceMessageId = null,
    string? SourceThreadId = null,
    DateTime? DetectedUtc = null);

public sealed record ReplySignalContextMessage(
    string Direction,
    string? Subject,
    string? Body,
    string? SenderEmail,
    DateTime? OccurredUtc,
    string? ProviderMessageId = null,
    string? ProviderThreadId = null);

public sealed record ReplySignalDetectionResult(
    Guid CompanyId,
    IReadOnlyList<DetectedReplySignal> Signals);

public sealed record DetectedReplySignal(
    Guid CompanyId,
    string SignalType,
    decimal Confidence,
    string Explanation,
    Guid? DealId,
    Guid? ConversationId,
    Guid? MessageId,
    Guid? SequenceId,
    Guid? SequenceStepId,
    string SourceType,
    string? SourceMessageId,
    string? SourceThreadId,
    DateTime DetectedUtc,
    DateTime? SourceWindowStartedUtc,
    DateTime? SourceWindowEndedUtc,
    IReadOnlyDictionary<string, string?> Evidence);

public sealed record DealIntelligenceSignalDto(
    Guid Id,
    Guid CompanyId,
    Guid? DealId,
    Guid? ConversationId,
    Guid? MessageId,
    Guid? SequenceId,
    Guid? SequenceStepId,
    string SignalType,
    string SignalState,
    decimal ConfidenceScore,
    string Explanation,
    string SourceType,
    string? SourceMessageId,
    string? SourceThreadId,
    string? SourceMetadataJson,
    DateTime DetectedUtc,
    DateTime? SourceWindowStartedUtc,
    DateTime? SourceWindowEndedUtc,
    DateTime UpdatedUtc);
