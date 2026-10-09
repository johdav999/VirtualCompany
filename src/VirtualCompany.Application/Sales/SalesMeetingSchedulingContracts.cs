using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Sales;

public static class SalesMeetingApprovalTypes
{
    public const string SendInvitation = "sales_meeting_invitation_send";
    public const string RescheduleInvitation = "sales_meeting_invitation_reschedule";
    public const string CancelInvitation = "sales_meeting_invitation_cancel";
}
public interface ISalesMeetingSchedulingService
{
    Task<IReadOnlyList<SalesCalendarConnectionResponse>> ListCalendarConnectionsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesMeetingInvitationResponse>> ListForLeadAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken);
    Task<SalesMeetingInvitationResponse?> GetAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken);
    Task<SalesMeetingInvitationResponse> CreateForLeadAsync(Guid companyId, Guid userId, Guid leadId, CreateSalesMeetingInvitationRequest request, CancellationToken cancellationToken);
    Task<SalesMeetingInvitationResponse> RetryDeliveryAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken);
    Task<SalesMeetingAvailabilityResponse> GetAvailabilityAsync(Guid companyId, SalesMeetingAvailabilityRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesMeetingChangeRequestResponse>> ListChangesAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken);
    Task<SalesMeetingChangeRequestResponse> RequestRescheduleAsync(Guid companyId, Guid userId, Guid invitationId, CreateSalesMeetingRescheduleRequest request, CancellationToken cancellationToken);
    Task<SalesMeetingChangeRequestResponse> RequestCancellationAsync(Guid companyId, Guid userId, Guid invitationId, CancellationToken cancellationToken);
}

public sealed record CalendarProviderContext(
    Guid CompanyId, Guid ConnectionId, ExternalAccountProvider Provider,
    string OrganizerEmail, string AccessToken, string CalendarId);

public sealed record CalendarMeetingCreateRequest(
    Guid InvitationId, string IdempotencyKey, string Title, string Description,
    DateTime StartsUtc, DateTime EndsUtc, string TimeZoneId, string? Location,
    string AttendeeEmail, string? AttendeeName, bool CreateOnlineMeeting);

public sealed record CalendarMeetingCreateResult(
    string ExternalEventId, string? ExternalICalUid,
    string? ProviderWebUrl, string? OnlineMeetingUrl);

public sealed record CalendarMeetingUpdateRequest(
    Guid ChangeRequestId, string IdempotencyKey, string ExternalEventId,
    string Title, string Description, DateTime StartsUtc, DateTime EndsUtc,
    string TimeZoneId, string? Location, string AttendeeEmail,
    string? AttendeeName, bool CreateOnlineMeeting);
public enum CalendarProviderFailureKind
{
    Retryable = 1,
    Permanent = 2,
    AuthenticationRequired = 3,
    Ambiguous = 4
}

public sealed class CalendarProviderException : Exception
{
    public CalendarProviderException(string code, string safeMessage, CalendarProviderFailureKind kind, Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        Code = code;
        Kind = kind;
    }

    public string Code { get; }
    public CalendarProviderFailureKind Kind { get; }
}

public sealed record CalendarMeetingObservation(CalendarMeetingCreateResult Event, DateTime StartsUtc, DateTime EndsUtc, string Title, bool Cancelled);
public interface ICalendarProviderClient
{
    Task<CalendarMeetingObservation?> InspectMeetingAsync(CalendarProviderContext context, Guid invitationId, string? externalEventId, DateTime startsUtc, DateTime endsUtc, CancellationToken ct)
        => throw new NotSupportedException("Calendar inspection is not implemented by this provider.");
    ExternalAccountProvider Provider { get; }
    IReadOnlyCollection<string> RequiredScopes { get; }
    Task<IReadOnlyList<CalendarBusyWindow>> GetBusyWindowsAsync(
        CalendarProviderContext context, DateTime fromUtc, DateTime toUtc,
        string timeZoneId, CancellationToken cancellationToken);
    Task<CalendarMeetingCreateResult> CreateMeetingAsync(
        CalendarProviderContext context, CalendarMeetingCreateRequest request,
        CancellationToken cancellationToken);
    Task<CalendarMeetingCreateResult> UpdateMeetingAsync(
        CalendarProviderContext context, CalendarMeetingUpdateRequest request,
        CancellationToken cancellationToken);
    Task CancelMeetingAsync(
        CalendarProviderContext context, string externalEventId,
        string idempotencyKey, CancellationToken cancellationToken);
}

public interface ICalendarProviderRegistry
{
    ICalendarProviderClient Resolve(ExternalAccountProvider provider);
}

public sealed record SalesMeetingInvitationDeliveryRequestedMessage(
    Guid CompanyId, Guid InvitationId, string IdempotencyKey, string? CorrelationId, bool ReconcileOnly = false);

public interface ISalesMeetingInvitationDeliveryDispatcher
{
    Task DispatchAsync(SalesMeetingInvitationDeliveryRequestedMessage message, CancellationToken cancellationToken);
}
public sealed record SalesMeetingChangeDeliveryRequestedMessage(
    Guid CompanyId, Guid ChangeRequestId, string IdempotencyKey, string? CorrelationId, bool ReconcileOnly = false);

public sealed record SalesMeetingConfirmationDeliveryRequestedMessage(
    Guid CompanyId, Guid InvitationId, string IdempotencyKey, string? CorrelationId);

public interface ISalesMeetingChangeDeliveryDispatcher
{
    Task DispatchAsync(SalesMeetingChangeDeliveryRequestedMessage message, CancellationToken cancellationToken);
}

public interface ISalesMeetingConfirmationDeliveryDispatcher
{
    Task DispatchAsync(SalesMeetingConfirmationDeliveryRequestedMessage message, CancellationToken cancellationToken);
}
