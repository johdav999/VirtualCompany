using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Application.Sales;

public static class SalesMeetingApprovalTypes
{
    public const string SendInvitation = "sales_meeting_invitation_send";
    public const string RescheduleInvitation = "sales_meeting_invitation_reschedule";
    public const string CancelInvitation = "sales_meeting_invitation_cancel";
}

public sealed record SalesCalendarConnectionResponse(
    Guid Id, string Provider, string EmailAddress, string? DisplayName,
    string Status, bool HasCalendarPermission, bool RequiresReconnect);

public sealed record CreateSalesMeetingInvitationRequest(
    Guid CalendarConnectionId, DateTime StartsUtc, DateTime EndsUtc,
    string TimeZoneId, string Title, string Description, string? Location,
    bool CreateOnlineMeeting = true);

public sealed record SalesMeetingInvitationResponse(
    Guid Id, Guid LeadId, Guid? DealId, Guid? ContactId, Guid CalendarConnectionId,
    string Provider, string OrganizerEmail, string AttendeeEmail, string? AttendeeName,
    string Title, string Description, DateTime StartsUtc, DateTime EndsUtc,
    string TimeZoneId, string? Location, bool CreateOnlineMeeting, string Status,
    Guid? ApprovalRequestId, string? ExternalEventId, string? ProviderWebUrl,
    string? OnlineMeetingUrl, int ExecutionAttemptCount, string? LastErrorCode,
    string? LastErrorSummary, DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ScheduledUtc,
    string ConfirmationStatus, Guid? ConfirmationMailboxConnectionId,
    string? ConfirmationProviderMessageId, string? ConfirmationProviderThreadId,
    string ConfirmationThreadingMode,
    int ConfirmationAttemptCount, string? ConfirmationErrorCode,
    string? ConfirmationErrorSummary, DateTime? ConfirmationSentUtc);

public sealed record SalesMeetingAvailabilityRequest(
    Guid CalendarConnectionId, DateTime FromUtc, DateTime ToUtc, string TimeZoneId,
    int DurationMinutes = 30);

public sealed record CalendarBusyWindow(DateTime StartsUtc, DateTime EndsUtc);
public sealed record CalendarAvailableSlot(DateTime StartsUtc, DateTime EndsUtc);

public sealed record SalesMeetingAvailabilityResponse(
    Guid CalendarConnectionId, string Provider,
    IReadOnlyList<CalendarBusyWindow> BusyWindows,
    IReadOnlyList<CalendarAvailableSlot> SuggestedSlots);

public sealed record CreateSalesMeetingRescheduleRequest(
    DateTime StartsUtc, DateTime EndsUtc, string TimeZoneId,
    string Title, string Description, string? Location,
    bool CreateOnlineMeeting = true);

public sealed record SalesMeetingChangeRequestResponse(
    Guid Id, Guid InvitationId, string Operation, string Status,
    DateTime? StartsUtc, DateTime? EndsUtc, string? TimeZoneId,
    string? Title, string? Description, string? Location, bool? CreateOnlineMeeting,
    Guid? ApprovalRequestId, int ExecutionAttemptCount,
    string? LastErrorCode, string? LastErrorSummary,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);
public interface ISalesMeetingSchedulingService
{
    Task<IReadOnlyList<SalesCalendarConnectionResponse>> ListCalendarConnectionsAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<SalesMeetingInvitationResponse>> ListForLeadAsync(Guid companyId, Guid leadId, CancellationToken cancellationToken);
    Task<SalesMeetingInvitationResponse?> GetAsync(Guid companyId, Guid invitationId, CancellationToken cancellationToken);
    Task<SalesMeetingInvitationResponse> CreateForLeadAsync(Guid companyId, Guid userId, Guid leadId, CreateSalesMeetingInvitationRequest request, CancellationToken cancellationToken);
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

public interface ICalendarProviderClient
{
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
    Guid CompanyId, Guid InvitationId, string IdempotencyKey, string? CorrelationId);

public interface ISalesMeetingInvitationDeliveryDispatcher
{
    Task DispatchAsync(SalesMeetingInvitationDeliveryRequestedMessage message, CancellationToken cancellationToken);
}
public sealed record SalesMeetingChangeDeliveryRequestedMessage(
    Guid CompanyId, Guid ChangeRequestId, string IdempotencyKey, string? CorrelationId);

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
