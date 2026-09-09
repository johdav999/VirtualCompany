namespace VirtualCompany.Application.Sales;

public static class SalesMeetingTranscriptProblemCodes
{
    public const string InvalidRequest = "sales.meeting_transcript.invalid_request";
    public const string ConsentRequired = "sales.meeting_transcript.consent_required";
    public const string RetentionExpired = "sales.meeting_transcript.retention_expired";
    public const string ProviderUnsupported = "sales.meeting_transcript.provider_unsupported";
    public const string ProviderPermissionRequired = "sales.meeting_transcript.provider_permission_required";
    public const string Conflict = "sales.meeting_transcript.conflict";
}

public enum MeetingTranscriptProviderFailureKind
{
    Retryable = 1,
    Permanent = 2,
    AuthenticationRequired = 3
}

public sealed record MeetingTranscriptProviderContext(string AccessToken, string OrganizerUserId, DateTime MeetingStartsUtc);
public sealed record MeetingTranscriptResolvedMeeting(string OnlineMeetingId);
public sealed record MeetingTranscriptProviderSubscription(string SubscriptionId, string Resource, DateTime ExpiresUtc);
public sealed record MeetingTranscriptDescriptor(string TranscriptId, string Version, DateTime CreatedUtc,
    DateTime? EndedUtc, string MetadataJson);
public sealed record MeetingTranscriptDescriptorPage(IReadOnlyList<MeetingTranscriptDescriptor> Items, string? ContinuationToken);
public sealed record MeetingTranscriptNormalizedSegment(string SegmentId, string Content, string? SpeakerLabel,
    DateTime StartedUtc, DateTime? EndedUtc);
public sealed record MeetingTranscriptDocument(MeetingTranscriptDescriptor Descriptor, string ContentHash,
    IReadOnlyList<MeetingTranscriptNormalizedSegment> Segments);

public interface IMeetingTranscriptProviderAdapter
{
    string Provider { get; }
    IReadOnlyCollection<string> RequiredScopes { get; }
    Task<MeetingTranscriptResolvedMeeting> ResolveMeetingAsync(MeetingTranscriptProviderContext context,
        string onlineMeetingUrl, CancellationToken cancellationToken);
    Task<MeetingTranscriptProviderSubscription> CreateSubscriptionAsync(MeetingTranscriptProviderContext context,
        string onlineMeetingId, Uri notificationUrl, Uri lifecycleNotificationUrl, string clientState,
        DateTime expiresUtc, CancellationToken cancellationToken);
    Task<MeetingTranscriptProviderSubscription> RenewSubscriptionAsync(MeetingTranscriptProviderContext context,
        string subscriptionId, string resource, DateTime expiresUtc, CancellationToken cancellationToken);
    Task DeleteSubscriptionAsync(MeetingTranscriptProviderContext context, string subscriptionId,
        CancellationToken cancellationToken);
    Task<MeetingTranscriptDescriptorPage> ListTranscriptsAsync(MeetingTranscriptProviderContext context,
        string onlineMeetingId, string? continuationToken, CancellationToken cancellationToken);
    Task<MeetingTranscriptDocument> FetchTranscriptAsync(MeetingTranscriptProviderContext context,
        string onlineMeetingId, string transcriptId, CancellationToken cancellationToken);
}

public sealed class MeetingTranscriptProviderException(
    string code, string safeMessage, MeetingTranscriptProviderFailureKind kind, TimeSpan? retryAfter = null,
    Exception? innerException = null) : Exception(safeMessage, innerException)
{
    public string Code { get; } = code;
    public MeetingTranscriptProviderFailureKind Kind { get; } = kind;
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

public sealed record CreateSalesMeetingTranscriptSubscriptionRequest(Guid? CalendarConnectionId = null);
public sealed record SalesMeetingTranscriptSubscriptionDto(Guid Id, Guid SessionId, Guid CalendarConnectionId,
    string Provider, string Status, DateTime ExpiresUtc, DateTime RetentionUntilUtc, DateTime? LastNotificationUtc,
    DateTime? LastRenewedUtc, int RenewalAttemptCount, int AuthenticityFailureCount,
    string? LastErrorCode, string? LastErrorSummary, long Version);
public sealed record SalesMeetingTranscriptIngestionDto(Guid Id, string Status, string ProviderTranscriptId,
    string ProviderVersion, DateTime ReceivedUtc, int AttemptCount, int EquivalentCount, int AddedCount,
    int SpeakerCorrectionCount, int ConflictCount, bool MateriallyChanged, string? FailureCode,
    string? FailureSummary, DateTime? CompletedUtc);
public sealed record SalesMeetingTranscriptConflictDto(Guid Id, Guid TranscriptSegmentId, string ProviderSegmentId,
    string? ExistingContent, string ProviderContent, string? ExistingSpeakerLabel, string? ProviderSpeakerLabel,
    string Summary, DateTime CreatedUtc);
public sealed record SalesMeetingTranscriptReconciliationStatusDto(Guid SessionId, long ReconciliationVersion,
    bool HasStaleClosingArtifacts, int PendingCount, int ConflictCount,
    IReadOnlyList<SalesMeetingTranscriptSubscriptionDto> Subscriptions,
    IReadOnlyList<SalesMeetingTranscriptIngestionDto> Ingestions,
    IReadOnlyList<SalesMeetingTranscriptConflictDto> Conflicts);

public sealed record SalesMeetingTranscriptIngestionRequestedMessage(Guid CompanyId, Guid IngestionId,
    string IdempotencyKey, string? CorrelationId);

public interface ISalesMeetingTranscriptSubscriptionService
{
    Task<SalesMeetingTranscriptSubscriptionDto?> EnsureAsync(Guid companyId, Guid userId, Guid sessionId,
        CreateSalesMeetingTranscriptSubscriptionRequest request, string? correlationId,
        CancellationToken cancellationToken);
    Task<SalesMeetingTranscriptSubscriptionDto?> RenewAsync(Guid companyId, Guid userId, Guid sessionId,
        Guid subscriptionId, string? correlationId, CancellationToken cancellationToken);
    Task<int> RenewDueAsync(CancellationToken cancellationToken);
}

public interface ISalesMeetingTranscriptReconciliationQuery
{
    Task<SalesMeetingTranscriptReconciliationStatusDto?> GetStatusAsync(Guid companyId, Guid userId,
        Guid sessionId, CancellationToken cancellationToken);
}

public interface ISalesMeetingTranscriptIngestionDispatcher
{
    Task DispatchAsync(SalesMeetingTranscriptIngestionRequestedMessage message,
        CancellationToken cancellationToken);
}

public sealed class SalesMeetingTranscriptPolicyException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
