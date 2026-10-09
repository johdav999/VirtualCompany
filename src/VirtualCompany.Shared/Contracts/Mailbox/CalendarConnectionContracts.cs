using VirtualCompany.Domain.Enums;


namespace VirtualCompany.Application.Mailbox;


public sealed record CalendarConnectionSummary(
    Guid Id, ExternalAccountProvider Provider, string AccountEmail,
    string? DisplayName, string CalendarId, string? TimeZoneId,
    CalendarCapability Capabilities, ExternalConnectionStatus Status,
    bool HasRequiredPermissions, bool RequiresReconnect,
    DateTime? LastHealthCheckUtc, string? LastErrorSummary);
