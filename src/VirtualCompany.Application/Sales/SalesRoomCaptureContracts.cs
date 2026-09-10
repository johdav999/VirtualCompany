namespace VirtualCompany.Application.Sales;

// Worker-only input: never serialize speech into a durable retry or diagnostic payload.
public sealed record RetainSalesRoomTranscript(Guid CompanyId, Guid RoomId, Guid ParticipantId,
    long ConsentVersion, bool RetentionAllowedAtSubmission, long AgentGeneration, Guid LeaseOwnerId,
    string TrackId, long TrackGeneration, DateTime StartedUtc, DateTime EndedUtc, bool Overlapped, string Text)
{
    public override string ToString() => "RetainSalesRoomTranscript { Text = [redacted] }";
}
public sealed record SalesRoomCaptureReview(Guid RoomId, Guid SessionId, Guid? AgentId, string State,
    long SessionVersion, long CaptureVersion, Guid? CaptureCheckpointId, DateTime RetentionUntilUtc,
    string Coverage, IReadOnlyList<SalesMeetingTranscriptSegmentDto> Segments,
    SalesMeetingClosingSnapshotDto? Closing, Guid? EvidenceArtifactId = null, string MeetingStatus = "");
public interface ISalesRoomCaptureService
{
    Task<Guid?> RetainAsync(RetainSalesRoomTranscript input, CancellationToken ct);
    Task<SalesRoomCaptureReview> GetReviewAsync(Guid companyId, Guid userId, Guid roomId, CancellationToken ct);
    Task<int> PurgeExpiredAsync(CancellationToken ct);
}
