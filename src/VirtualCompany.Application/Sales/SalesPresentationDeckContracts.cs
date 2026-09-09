using System.Collections.ObjectModel;

namespace VirtualCompany.Application.Sales;

public static class SalesPresentationProblemCodes
{
    public const string InvalidFile = "sales.presentation.invalid_file";
    public const string UnsupportedFile = "sales.presentation.unsupported_file";
    public const string FileTooLarge = "sales.presentation.file_too_large";
    public const string SlideLimitExceeded = "sales.presentation.slide_limit_exceeded";
    public const string InvalidState = "sales.presentation.invalid_state";
    public const string ProcessingFailed = "sales.presentation.processing_failed";
}

public sealed class SalesPresentationValidationException : Exception
{
    public SalesPresentationValidationException(IDictionary<string, string[]> errors)
        : base("Sales presentation validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(
            new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class SalesPresentationConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class SalesPresentationProcessingException(
    string code, string safeMessage, bool canRetry, bool blocked = false, Exception? innerException = null)
    : Exception(safeMessage, innerException)
{
    public string Code { get; } = code;
    public string SafeMessage { get; } = safeMessage;
    public bool CanRetry { get; } = canRetry;
    public bool Blocked { get; } = blocked;
}

public sealed record ImportSalesPresentationDeckCommand(
    Guid AgentId,
    string? Title,
    string OriginalFileName,
    string? ContentType,
    long Length,
    Stream Content);

public sealed record SalesPresentationArtifactDto(
    Guid Id,
    Guid? SlideId,
    int ArtifactVersion,
    string ArtifactType,
    string Section,
    int Order,
    string Content,
    string Classification,
    string? SourceId,
    Guid? AiRunId,
    DateTime CreatedUtc);

public sealed record SalesPresentationSlideDto(
    Guid Id,
    int ProcessingVersion,
    int SlideNumber,
    string? Title,
    string ExtractedText,
    string? SpeakerNotes,
    string ImageStorageKey,
    string? ImageStorageUrl,
    int ImageWidthPixels,
    int ImageHeightPixels,
    long SourceWidthEmus,
    long SourceHeightEmus,
    string ContentHash,
    string Objective,
    int ExpectedDurationSeconds,
    string TransitionText,
    string Status,
    IReadOnlyList<SalesPresentationArtifactDto> PlanArtifacts);

public sealed record SalesPresentationDeckDto(
    Guid Id,
    Guid CompanyId,
    Guid SessionId,
    Guid AgentId,
    int Version,
    string Title,
    string OriginalFileName,
    string? ContentType,
    long FileSizeBytes,
    string ContentHash,
    string StorageKey,
    string? StorageUrl,
    string Status,
    int ProcessingVersion,
    int ProcessingAttemptCount,
    int SlideCount,
    string? RendererName,
    string? RendererVersion,
    string? AnimationHandling,
    string? FailureCode,
    string? FailureSummary,
    bool CanRetry,
    bool IsActive,
    int BriefVersion,
    bool BriefRegenerationPending,
    Guid UploadedByUserId,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? ProcessingStartedUtc,
    DateTime? ProcessedUtc,
    DateTime? FailedUtc,
    DateTime? ActivatedUtc,
    long ConcurrencyVersion,
    IReadOnlyList<SalesPresentationSlideDto> Slides);

public sealed record SalesMeetingBriefDto(
    Guid SessionId,
    Guid DeckId,
    int Version,
    bool RequiresReview,
    IReadOnlyList<SalesPresentationArtifactDto> Items);

public interface ISalesPresentationDeckService
{
    Task<SalesPresentationDeckDto> ImportAsync(
        Guid companyId, Guid actorUserId, Guid sessionId,
        ImportSalesPresentationDeckCommand command, string? correlationId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesPresentationDeckDto>> ListAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken);
    Task<SalesPresentationDeckDto?> GetAsync(Guid companyId, Guid sessionId, Guid deckId, CancellationToken cancellationToken);
    Task<SalesPresentationSlideDto?> GetSlideAsync(Guid companyId, Guid sessionId, Guid deckId, int slideNumber, CancellationToken cancellationToken);
    Task<SalesPresentationDeckDto?> ActivateAsync(Guid companyId, Guid actorUserId, Guid sessionId, Guid deckId, string? correlationId, CancellationToken cancellationToken);
    Task<SalesPresentationDeckDto?> RetryAsync(Guid companyId, Guid actorUserId, Guid sessionId, Guid deckId, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingBriefDto?> GetBriefAsync(Guid companyId, Guid sessionId, CancellationToken cancellationToken);
    Task<SalesPresentationDeckDto?> RequestBriefRegenerationAsync(Guid companyId, Guid actorUserId, Guid sessionId, string? correlationId, CancellationToken cancellationToken);
}

public sealed record ExtractedPresentationSlide(
    int SlideNumber,
    string? Title,
    string ExtractedText,
    string? SpeakerNotes,
    string ContentHash);

public sealed record ExtractedPresentationDeck(
    long SourceWidthEmus,
    long SourceHeightEmus,
    IReadOnlyList<ExtractedPresentationSlide> Slides);

public interface ISalesPresentationDeckExtractor
{
    Task<ExtractedPresentationDeck> ExtractAsync(Stream content, int maximumSlides, CancellationToken cancellationToken);
}

public sealed record SalesPresentationRenderRequest(
    Guid CompanyId,
    Guid DeckId,
    int ProcessingVersion,
    ExtractedPresentationSlide Slide,
    int WidthPixels,
    int HeightPixels);

public sealed record SalesPresentationRenderedSlide(
    byte[] Content,
    string ContentType,
    string FileExtension,
    int WidthPixels,
    int HeightPixels,
    string RendererName,
    string RendererVersion,
    string AnimationHandling);

public interface ISalesPresentationSlideRenderer
{
    Task<SalesPresentationRenderedSlide> RenderAsync(SalesPresentationRenderRequest request, CancellationToken cancellationToken);
}

public interface ISalesPresentationDeckProcessor
{
    Task<int> ProcessPendingAsync(CancellationToken cancellationToken);
    Task ProcessAsync(Guid companyId, Guid deckId, CancellationToken cancellationToken);
}
