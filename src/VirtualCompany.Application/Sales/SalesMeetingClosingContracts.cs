namespace VirtualCompany.Application.Sales;

public static class SalesMeetingClosingToolNames
{
    public const string ReadEvidence = "sales.meeting.read_closing_evidence";
    public const string GenerateSummary = "sales.meeting.generate_closing_summary";
}

public static class SalesMeetingClosingProblemCodes
{
    public const string InvalidRequest = "sales.meeting_closing.invalid_request";
    public const string Conflict = "sales.meeting_closing.conflict";
    public const string CaptureNotFlushed = "sales.meeting_closing.capture_not_flushed";
    public const string InternalAccessDenied = "sales.meeting_closing.internal_access_denied";
}

public sealed record PrepareSalesMeetingClosingRequest(Guid GenerationRequestId, Guid AgentId,
    long ExpectedSessionVersion, long ExpectedCaptureVersion, Guid? CaptureCheckpointId,
    DateTime? ProposedNextMeetingUtc = null, IReadOnlyList<string>? ProposedDealChanges = null);
public sealed record EditSalesMeetingMinutesItemRequest(int Order, string Type, string Content,
    string? OwnerLabel, DateTime? DueUtc, string SourceId, Guid? SourceArtifactId, bool RequiresReview);
public sealed record EditSalesMeetingMinutesRequest(long ExpectedVersion, IReadOnlyList<EditSalesMeetingMinutesItemRequest> Items);
public sealed record EditSalesMeetingInternalItemRequest(int Order, string Type, string Content, decimal? Confidence,
    string SourceId, Guid? SourceArtifactId, bool RequiresReview);
public sealed record EditSalesMeetingInternalIntelligenceRequest(long ExpectedVersion, IReadOnlyList<EditSalesMeetingInternalItemRequest> Items);
public sealed record ReviewSalesMeetingClosingArtifactRequest(long ExpectedVersion);
public sealed record CompleteSalesMeetingClosingRequest(long ExpectedSessionVersion, long ExpectedCaptureVersion,
    Guid? CaptureCheckpointId, Guid MinutesId, int MinutesArtifactVersion,
    Guid InternalIntelligenceId, int InternalArtifactVersion);

public sealed record SalesMeetingMinutesItemDto(Guid Id, int Order, string Type, string Content,
    string? OwnerLabel, DateTime? DueUtc, string SourceId, Guid? SourceArtifactId, bool RequiresReview);
public sealed record SalesMeetingMinutesDto(Guid Id, Guid SessionId, Guid? PreviousVersionId, int ArtifactVersion,
    string Status, long EvidenceCaptureVersion, DateTime EvidenceCutoffUtc, Guid GeneratorAgentId, Guid? AiRunId,
    string GeneratorVersion, string PromptVersion, DateTime RetentionUntilUtc, DateTime? ReviewedUtc,
    DateTime? ApprovedUtc, DateTime UpdatedUtc, long Version, bool IsEvidenceStale,
    DateTime? EvidenceStaleUtc, string? EvidenceStaleReason, IReadOnlyList<SalesMeetingMinutesItemDto> Items);
public sealed record SalesMeetingCustomerMinutesPreviewItemDto(int Order, string Type, string Content,
    string? OwnerLabel, DateTime? DueUtc);
public sealed record SalesMeetingCustomerMinutesPreviewDto(Guid Id, Guid SessionId, int ArtifactVersion,
    string Status, DateTime? ApprovedUtc, IReadOnlyList<SalesMeetingCustomerMinutesPreviewItemDto> Items);

public sealed record SalesMeetingInternalIntelligenceItemDto(Guid Id, int Order, string Type, string Content,
    decimal? Confidence, string SourceId, Guid? SourceArtifactId, bool RequiresReview);
public sealed record SalesMeetingInternalIntelligenceDto(Guid Id, Guid SessionId, Guid MinutesId, int ArtifactVersion,
    string Status, long EvidenceCaptureVersion, DateTime EvidenceCutoffUtc, Guid GeneratorAgentId, Guid? AiRunId,
    string GeneratorVersion, string PromptVersion, DateTime RetentionUntilUtc, DateTime? ReviewedUtc,
    DateTime? ApprovedUtc, DateTime UpdatedUtc, long Version, bool IsEvidenceStale,
    DateTime? EvidenceStaleUtc, string? EvidenceStaleReason,
    IReadOnlyList<SalesMeetingInternalIntelligenceItemDto> Items);

public sealed record SalesMeetingClosingSnapshotDto(SalesMeetingMinutesDto CustomerMinutes,
    SalesMeetingInternalIntelligenceDto InternalIntelligence);

public interface ISalesMeetingClosingService
{
    Task<SalesMeetingClosingSnapshotDto?> PrepareAsync(Guid companyId, Guid userId, Guid sessionId,
        PrepareSalesMeetingClosingRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesMeetingMinutesDto>> ListMinutesAsync(Guid companyId, Guid userId, Guid sessionId, CancellationToken cancellationToken);
    Task<SalesMeetingMinutesDto?> GetMinutesAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId, CancellationToken cancellationToken);
    Task<SalesMeetingCustomerMinutesPreviewDto?> GetCustomerPreviewAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId, CancellationToken cancellationToken);
    Task<SalesMeetingInternalIntelligenceDto?> GetInternalAsync(Guid companyId, Guid userId, Guid sessionId, Guid intelligenceId, CancellationToken cancellationToken);
    Task<SalesMeetingMinutesDto?> EditMinutesAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId,
        EditSalesMeetingMinutesRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingInternalIntelligenceDto?> EditInternalAsync(Guid companyId, Guid userId, Guid sessionId, Guid intelligenceId,
        EditSalesMeetingInternalIntelligenceRequest request, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingMinutesDto?> SubmitMinutesAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId, long expectedVersion, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingMinutesDto?> ApproveMinutesAsync(Guid companyId, Guid userId, Guid sessionId, Guid minutesId, long expectedVersion, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingInternalIntelligenceDto?> SubmitInternalAsync(Guid companyId, Guid userId, Guid sessionId, Guid intelligenceId, long expectedVersion, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingInternalIntelligenceDto?> ApproveInternalAsync(Guid companyId, Guid userId, Guid sessionId, Guid intelligenceId, long expectedVersion, string? correlationId, CancellationToken cancellationToken);
    Task<SalesMeetingSessionResponse?> CompleteAsync(Guid companyId, Guid userId, Guid sessionId,
        CompleteSalesMeetingClosingRequest request, string? correlationId, CancellationToken cancellationToken);
}

public sealed class SalesMeetingClosingValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The sales meeting closing request is invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
public sealed class SalesMeetingClosingConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
