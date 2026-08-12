using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class CalendarConnection : ICompanyOwnedEntity
{
    private CalendarConnection() { }

    public CalendarConnection(
        Guid id, Guid companyId, Guid userId, Guid externalAccountConnectionId,
        ExternalAccountProvider provider, string accountEmail, string? displayName,
        string calendarId = "primary", string? timeZoneId = null, DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (userId == Guid.Empty) throw new ArgumentException("UserId is required.", nameof(userId));
        if (externalAccountConnectionId == Guid.Empty) throw new ArgumentException("ExternalAccountConnectionId is required.", nameof(externalAccountConnectionId));
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        ExternalAccountConnectionId = externalAccountConnectionId;
        Provider = provider;
        AccountEmail = NormalizeRequired(accountEmail, 256).ToLowerInvariant();
        DisplayName = NormalizeOptional(displayName, 200);
        CalendarId = NormalizeRequired(calendarId, 256);
        TimeZoneId = NormalizeOptional(timeZoneId, 100);
        Capabilities = CalendarCapability.ReadAvailability |
            CalendarCapability.CreateEvents | CalendarCapability.UpdateEvents |
            CalendarCapability.CancelEvents | CalendarCapability.CreateConferenceLinks;
        Status = ExternalConnectionStatus.Pending;
        CreatedUtc = NormalizeUtc(createdUtc ?? DateTime.UtcNow);
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ExternalAccountConnectionId { get; private set; }
    public ExternalAccountProvider Provider { get; private set; }
    public string AccountEmail { get; private set; } = null!;
    public string? DisplayName { get; private set; }
    public string CalendarId { get; private set; } = "primary";
    public string? TimeZoneId { get; private set; }
    public CalendarCapability Capabilities { get; private set; }
    public ExternalConnectionStatus Status { get; private set; }
    public DateTime? LastHealthCheckUtc { get; private set; }
    public string? LastErrorCode { get; private set; }
    public string? LastErrorSummary { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User User { get; private set; } = null!;
    public ExternalAccountConnection ExternalAccountConnection { get; private set; } = null!;

    public void UpdateProfile(string accountEmail, string? displayName, string calendarId, string? timeZoneId)
    {
        AccountEmail = NormalizeRequired(accountEmail, 256).ToLowerInvariant();
        DisplayName = NormalizeOptional(displayName, 200);
        CalendarId = NormalizeRequired(calendarId, 256);
        TimeZoneId = NormalizeOptional(timeZoneId, 100);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetStatus(ExternalConnectionStatus status, string? errorCode = null, string? errorSummary = null)
    {
        Status = status;
        LastErrorCode = NormalizeOptional(errorCode, 120);
        LastErrorSummary = NormalizeOptional(errorSummary, 1000);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void RecordHealth(bool succeeded, DateTime checkedUtc, string? errorCode = null, string? errorSummary = null)
    {
        LastHealthCheckUtc = NormalizeUtc(checkedUtc);
        SetStatus(succeeded ? ExternalConnectionStatus.Active : ExternalConnectionStatus.Failed, errorCode, errorSummary);
    }

    private static string NormalizeRequired(string value, int max) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A value is required.") : value.Trim().Length > max ? throw new ArgumentException($"Value cannot exceed {max} characters.") : value.Trim();
    private static string? NormalizeOptional(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : NormalizeRequired(value, max);
    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
