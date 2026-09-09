namespace VirtualCompany.Application.Sales;

public sealed record CreateSalesBrowserRoom(Guid CommandId, DateTime ExpiresUtc);
public sealed record SalesRoomCommand(Guid CommandId, long ExpectedVersion);
public sealed record CreateSalesRoomInvitation(Guid CommandId, long ExpectedVersion, DateTime ExpiresUtc);
public sealed record RedeemSalesRoomInvitation(string Secret, string DisplayName)
{ public override string ToString() => "RedeemSalesRoomInvitation { Secret = [redacted] }"; }
public sealed record SetSalesRoomConsent(Guid CommandId, long ExpectedVersion, string Purpose, bool Granted, string NoticeVersion);
public sealed record SalesRoomParticipantView(Guid Id, string DisplayName, string State, long Version, bool Connected);
public sealed record SalesRoomOperationView(Guid Id, string Action, string State, int Attempts, string? ProblemCode);
public sealed record SalesBrowserRoomView(Guid Id, Guid? MeetingSessionId, string State, string AgentHealth,
    long Version, DateTime ExpiresUtc, IReadOnlyList<SalesRoomParticipantView> Participants, IReadOnlyList<SalesRoomOperationView> Operations);
public sealed record SalesRoomInvitationResult(Guid Id, string Secret, DateTime ExpiresUtc)
{ public override string ToString() => $"Invitation {{ Id = {Id}, Secret = [redacted] }}"; }
public sealed record SalesRoomGuestSession(string Credential, SalesRoomGuestView Participant)
{ public override string ToString() => "SalesRoomGuestSession { Credential = [redacted] }"; }
public sealed record SalesRoomGuestView(Guid RoomId, Guid ParticipantId, string RoomState, string AdmissionState,
    long Version, DateTime ExpiresUtc, bool AiProcessingAllowed, bool TranscriptRetentionAllowed);
public sealed record SalesRoomWorkItem(Guid CompanyId, Guid RoomId, Guid OperationId);
public sealed record SalesRoomWebhook(string EventId, string EventType, string RoomReference, string? ParticipantIdentity, long OccurredUnixSeconds);
public interface ISalesBrowserRoomService
{
    Task<SalesBrowserRoomView> CreateAsync(Guid company, Guid actor, Guid meeting, CreateSalesBrowserRoom request, CancellationToken ct);
    Task<SalesBrowserRoomView> GetAsync(Guid company, Guid actor, Guid room, CancellationToken ct);
    Task<SalesRoomInvitationResult> InviteAsync(Guid company, Guid actor, Guid room, CreateSalesRoomInvitation request, CancellationToken ct);
    Task<SalesBrowserRoomView> DecideAsync(Guid company, Guid actor, Guid room, Guid participant, string decision, SalesRoomCommand request, CancellationToken ct);
    Task<SalesBrowserRoomView> RevokeInvitationAsync(Guid company, Guid actor, Guid room, Guid invitation, SalesRoomCommand request, CancellationToken ct);
    Task<SalesBrowserRoomView> EndAsync(Guid company, Guid actor, Guid room, SalesRoomCommand request, CancellationToken ct);
    Task<SalesBrowserRoomView> RetryAsync(Guid company, Guid actor, Guid room, Guid operation, SalesRoomCommand request, CancellationToken ct);
    Task<SalesRoomMediaToken> HostTokenAsync(Guid company, Guid actor, Guid room, CancellationToken ct);
    Task<SalesRoomGuestSession> RedeemAsync(RedeemSalesRoomInvitation request, CancellationToken ct);
    Task<SalesRoomGuestView> GuestStatusAsync(string credential, Guid room, CancellationToken ct);
    Task<SalesRoomMediaToken> GuestTokenAsync(string credential, Guid room, CancellationToken ct);
    Task<SalesRoomGuestView> HostConsentAsync(Guid company, Guid actor, Guid room, SetSalesRoomConsent request, CancellationToken ct);
    Task<SalesRoomGuestView> ConsentAsync(string credential, Guid room, SetSalesRoomConsent request, CancellationToken ct);
    Task LeaveAsync(string credential, Guid room, CancellationToken ct);
    Task AcceptWebhookAsync(string body, string authorization, CancellationToken ct);
}
public interface ISalesRoomWorkDispatcher { Task DispatchAsync(SalesRoomWorkItem work, CancellationToken ct); }
public interface ISalesRoomWebhookVerifier { SalesRoomWebhook Verify(string body, string authorization); }
public interface ISalesRoomProviderInspection
{
    Task<IReadOnlyList<string>> ParticipantsAsync(SalesRoomMediaScope scope, CancellationToken ct);
}
public sealed class SalesRoomAccessException(string code, int status = 409) : Exception("Browser meeting request could not be completed.")
{ public string Code { get; } = code; public int Status { get; } = status; }
