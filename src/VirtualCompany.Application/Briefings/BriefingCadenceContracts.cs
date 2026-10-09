namespace VirtualCompany.Application.Briefings;

public sealed record BriefingCadenceRow(string Kind, bool Enabled, TimeOnly LocalTime, int Weekday = 1, int Day = 1, int Month = 1);
public sealed record BriefingCadenceSettings(string Role, string Timezone, TimeOnly WorkStart, TimeOnly WorkEnd,
    int[] Workdays, TimeOnly QuietStart, TimeOnly QuietEnd, bool GroupUpdates, bool SuppressUnchanged,
    bool UrgentEnabled, bool UrgentOutsideHours, string[] FocusAreas, BriefingCadenceRow[] Schedules,
    DateTime? AbsenceStartUtc = null, DateTime? AbsenceEndUtc = null, Guid? DelegateUserId = null, Guid? FallbackUserId = null, bool DeliveryEnabled = true);
public sealed record BriefingCadencePerson(Guid Id, string Name, string[] Areas);
public sealed record BriefingCadenceContext(Guid CompanyId, Guid UserId, bool Configured, BriefingCadenceSettings Settings,
    BriefingCadencePerson[] EligiblePeople, string[] AllowedAreas, string[] Channels);
public sealed record BriefingCadenceItem(Guid Id, string Title, string Status, string Area, string Priority, DateTime? DueUtc,
    DateTime UpdatedUtc, string WorkPath, string? RetainedSourcePath, string? SourceKind, int? SourceVersion);
public sealed record BriefingCadenceAudit(Guid Id, string Cadence, DateTime ScheduledUtc, Guid? RecipientUserId,
    string Routing, string Status, int Attempts, string? Reason, DateTime UpdatedUtc, string Path);
public sealed record BriefingCadencePreview(Guid CompanyId, Guid UserId, Guid? RecipientUserId, string Routing,
    string Timezone, DateTime? NextDeliveryUtc, string? NextDeliveryLocal, DateTime FreshnessUtc,
    string[] Cadences, BriefingCadenceItem[] Items, BriefingCadenceAudit[] Audit);
public sealed record BriefingCadenceDeliveryRequest(Guid CompanyId, Guid DeliveryId);
public interface IBriefingCadenceService
{
    Task<BriefingCadenceContext> GetAsync(Guid companyId, CancellationToken ct);
    Task<BriefingCadenceContext> SaveAsync(Guid companyId, BriefingCadenceSettings settings, CancellationToken ct);
    Task<BriefingCadencePreview> PreviewAsync(Guid companyId, CancellationToken ct);
    Task<BriefingCadencePreview> OpenAsync(Guid companyId, Guid deliveryId, CancellationToken ct);
    Task<int> ScheduleDueAsync(Guid companyId, DateTime nowUtc, CancellationToken ct);
    Task<CompanyBriefingGenerationResult> GenerateAsync(BriefingGenerationJobContext job, CancellationToken ct);
    Task DeliverAsync(BriefingCadenceDeliveryRequest request, CancellationToken ct);
}
