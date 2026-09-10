using VirtualCompany.Application.Agents;

namespace VirtualCompany.Application.Sales;

public sealed record SalesNarrationScript(int SlideNumber, int TalkingPoint, string Text);
public sealed record PrepareSalesNarration(string Language, IReadOnlyList<SalesNarrationScript>? Scripts = null);
public sealed record SalesNarrationDecision(long ExpectedVersion, bool AcknowledgeAdditionalCost = false);
public sealed record SalesNarrationSegmentDto(Guid Id, int SlideNumber, int TalkingPoint, string SourceText,
    string Script, string Status, bool Reused, int DurationMilliseconds, long Bytes, int Attempts, string? FailureCode);
public sealed record SalesNarrationRevisionDto(Guid Id, Guid DeckId, int DeckVersion, string Language, string Voice,
    string Model, Guid AudienceId, string Status, long Version, DateTime CreatedUtc, DateTime? ApprovedUtc,
    IReadOnlyList<SalesNarrationSegmentDto> Segments, int InputTokens, int OutputTokens, int UnresolvedAttempts,
    double GeneratedMinutes, double ReusedMinutes, decimal? EstimatedCostUsd);
public sealed record SalesNarrationWorkspace(ApprovedSpeechProfile Speech, IReadOnlyList<SalesNarrationRevisionDto> Revisions);
public sealed record SalesNarrationPreview(byte[] Audio, string ContentType);
public sealed record SalesNarrationPlaybackRequest(Guid RevisionId, Guid SegmentId, Guid SessionId,
    Guid AudienceId, int OffsetMilliseconds, long TurnGeneration);
public sealed record SalesNarrationPlayback(Guid AssetId, int SlideNumber, int TalkingPoint,
    int OffsetMilliseconds, long TurnGeneration, byte[] Pcm, int SampleRateHertz);

public interface ISalesNarrationService
{
    Task<SalesNarrationWorkspace> GetAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken ct);
    Task<SalesNarrationRevisionDto> PrepareAsync(Guid companyId, Guid userId, Guid sessionId, PrepareSalesNarration command, CancellationToken ct);
    Task DecideAsync(Guid companyId, Guid userId, Guid revisionId, string action, SalesNarrationDecision command, CancellationToken ct);
    Task<SalesNarrationPreview> PreviewAsync(Guid companyId, Guid userId, SalesNarrationPlaybackRequest request, CancellationToken ct);
    Task<SalesNarrationPlayback> OpenPlaybackAsync(Guid companyId, Guid userId, SalesNarrationPlaybackRequest request, CancellationToken ct);
}

public sealed class SalesNarrationException(string message, int statusCode = 409) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

