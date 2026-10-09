

namespace VirtualCompany.Application.Sales;


public sealed record SalesMeetingAvailabilityRequest(
    Guid CalendarConnectionId, DateTime FromUtc, DateTime ToUtc, string TimeZoneId,
    int DurationMinutes = 30);


public sealed record CalendarBusyWindow(DateTime StartsUtc, DateTime EndsUtc);


public sealed record SalesCalendarConnectionResponse(
    Guid Id, string Provider, string EmailAddress, string? DisplayName,
    string Status, bool HasCalendarPermission, bool RequiresReconnect);


public sealed record SalesMeetingChangeRequestResponse(
    Guid Id, Guid InvitationId, string Operation, string Status,
    DateTime? StartsUtc, DateTime? EndsUtc, string? TimeZoneId,
    string? Title, string? Description, string? Location, bool? CreateOnlineMeeting,
    Guid? ApprovalRequestId, int ExecutionAttemptCount,
    string? LastErrorCode, string? LastErrorSummary,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? CompletedUtc);


public sealed record SalesMeetingAvailabilityResponse(
    Guid CalendarConnectionId, string Provider,
    IReadOnlyList<CalendarBusyWindow> BusyWindows,
    IReadOnlyList<CalendarAvailableSlot> SuggestedSlots);

public sealed record CalendarAvailableSlot(DateTime StartsUtc, DateTime EndsUtc);


public sealed record CreateSalesMeetingRescheduleRequest(
    DateTime StartsUtc, DateTime EndsUtc, string TimeZoneId,
    string Title, string Description, string? Location,
    bool CreateOnlineMeeting = true, string? Conferencing = null);


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
    string? ConfirmationErrorSummary, DateTime? ConfirmationSentUtc, string Conferencing = "none", Guid? BrowserRoomId = null);


public sealed record CreateSalesMeetingInvitationRequest(
    Guid CalendarConnectionId, DateTime StartsUtc, DateTime EndsUtc,
    string TimeZoneId, string Title, string Description, string? Location,
    bool CreateOnlineMeeting = true, string? Conferencing = null, Guid? CommandId = null);
