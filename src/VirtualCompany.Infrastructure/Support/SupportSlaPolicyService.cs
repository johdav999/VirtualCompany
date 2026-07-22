using System.Text.Json.Nodes;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Approvals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Mailbox;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

namespace VirtualCompany.Infrastructure.Support;

public sealed class SupportSlaPolicyService : ISupportSlaPolicyService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _audit;

    public SupportSlaPolicyService(VirtualCompanyDbContext dbContext, IAuditEventWriter audit)
    {
        _dbContext = dbContext;
        _audit = audit;
    }

    public async Task<IReadOnlyList<SupportSlaPolicyDto>> ListAsync(Guid companyId, CancellationToken cancellationToken) =>
        (await _dbContext.SupportSlaPolicies.AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Priority)
            .ThenBy(x => x.Category)
            .ToListAsync(cancellationToken))
        .Select(Map)
        .ToList();

    public async Task<SupportSlaPolicyDto> UpsertAsync(Guid companyId, Guid userId, UpsertSupportSlaPolicyRequest request, CancellationToken cancellationToken)
    {
        if (request.FirstResponseMinutes <= 0 || request.ResolutionMinutes <= 0 || request.ResolutionMinutes < request.FirstResponseMinutes)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.ResolutionMinutes)] = ["Resolution time must be at least the first-response time and both must be positive."] });
        }

        var category = SupportCaseCategories.Normalize(request.Category);
        var priority = SupportPriorities.Normalize(request.Priority);
        var tier = string.IsNullOrWhiteSpace(request.CustomerTier) ? null : request.CustomerTier.Trim();
        if (request.RiskThresholdMinutes <= 0 || request.RiskThresholdMinutes >= request.ResolutionMinutes)
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.RiskThresholdMinutes)] = ["Risk threshold must be positive and shorter than the resolution target."] });
        string recipientRole;
        try
        {
            recipientRole = CompanyMembershipRoles.ToStorageValue(CompanyMembershipRoles.Parse(request.EscalationRecipientRole));
        }
        catch (ArgumentOutOfRangeException)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.EscalationRecipientRole)] = ["Choose a supported company role for escalation."] });
        }
        var duplicate = await _dbContext.SupportSlaPolicies.AsNoTracking().AnyAsync(x =>
            x.CompanyId == companyId && x.Id != request.Id && x.IsActive && request.IsActive &&
            x.Category == category && x.Priority == priority && x.CustomerTier == tier,
            cancellationToken);
        if (duplicate)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.Category)] = ["An active SLA policy already covers this category, priority, and customer tier."] });
        }

        SupportSlaPolicy policy;
        if (request.Id is Guid policyId)
        {
            policy = await _dbContext.SupportSlaPolicies.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == policyId, cancellationToken)
                ?? throw new KeyNotFoundException("SLA policy was not found.");
            policy.Update(request.Name, category, priority, request.FirstResponseMinutes, request.ResolutionMinutes, tier, request.IsActive, request.TimeBasis, request.RiskThresholdMinutes, recipientRole);
        }
        else
        {
            policy = new SupportSlaPolicy(Guid.NewGuid(), companyId, request.Name, category, priority, request.FirstResponseMinutes, request.ResolutionMinutes, tier, request.IsActive, request.TimeBasis, request.RiskThresholdMinutes, recipientRole);
            _dbContext.SupportSlaPolicies.Add(policy);
        }

        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.sla_policy.saved", "support_sla_policy", policy.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Support SLA policy saved.", ["support", "settings"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    public async Task<SupportSlaPolicyDto?> DeactivateAsync(Guid companyId, Guid userId, Guid policyId, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.SupportSlaPolicies.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == policyId, cancellationToken);
        if (policy is null) return null;
        policy.Deactivate();
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.sla_policy.deactivated", "support_sla_policy", policy.Id.ToString("D"), AuditEventOutcomes.Succeeded, "Support SLA policy deactivated.", ["support", "settings"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(policy);
    }

    public async Task<SupportSlaResolutionDto> ResolveAsync(Guid companyId, string category, string priority, string? customerTier, DateTime startUtc, CancellationToken cancellationToken)
    {
        category = SupportCaseCategories.Normalize(category);
        priority = SupportPriorities.Normalize(priority);
        var tier = string.IsNullOrWhiteSpace(customerTier) ? null : customerTier.Trim();
        var policies = await _dbContext.SupportSlaPolicies.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive && x.Category == category && x.Priority == priority)
            .ToListAsync(cancellationToken);
        var selected = policies
            .OrderByDescending(x => tier is not null && x.CustomerTier == tier)
            .ThenByDescending(x => x.CustomerTier == null)
            .FirstOrDefault(x => x.CustomerTier == null || x.CustomerTier == tier);
        var defaults = DefaultDurations(priority);
        var first = selected?.FirstResponseMinutes ?? defaults.First;
        var resolution = selected?.ResolutionMinutes ?? defaults.Resolution;
        var start = startUtc.Kind == DateTimeKind.Utc ? startUtc : startUtc.ToUniversalTime();
        var useBusinessTime = string.Equals(selected?.TimeBasis, "business", StringComparison.OrdinalIgnoreCase);
        var calendar = useBusinessTime ? await GetCalendarAsync(companyId, cancellationToken) : null;
        var firstDue = calendar is null ? start.AddMinutes(first) : AddBusinessMinutes(start, first, calendar);
        var resolutionDue = calendar is null ? start.AddMinutes(resolution) : AddBusinessMinutes(start, resolution, calendar);
        return new SupportSlaResolutionDto(
            selected?.Id,
            selected?.Name ?? "Default support SLA",
            first,
            resolution,
            firstDue,
            resolutionDue,
            selected is null ? "No matching company policy was found, so the documented default target applies." : useBusinessTime ? "Matched the company policy and calculated deadlines in configured working time." : "Matched category, priority, and customer tier using the company SLA policy.",
            selected?.RiskThresholdMinutes ?? Math.Min(240, Math.Max(15, resolution / 4)),
            selected?.EscalationRecipientRole ?? "support_supervisor");
    }

    public async Task<SupportBusinessCalendarDto> GetCalendarAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var company = await _dbContext.Companies.AsNoTracking().SingleAsync(x => x.Id == companyId, cancellationToken);
        var fallback = new SupportBusinessCalendarDto(
            string.IsNullOrWhiteSpace(company.Timezone) ? TimeZoneInfo.Utc.Id : company.Timezone,
            new TimeOnly(8, 0),
            new TimeOnly(17, 0),
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
            []);
        if (!company.Settings.Extensions.TryGetValue("supportBusinessCalendar", out var value) || value is not JsonObject calendar)
        {
            return fallback;
        }

        var zone = calendar["timeZoneId"]?.GetValue<string>() ?? fallback.TimeZoneId;
        var start = TimeOnly.TryParse(calendar["workdayStart"]?.GetValue<string>(), out var parsedStart) ? parsedStart : fallback.WorkdayStart;
        var end = TimeOnly.TryParse(calendar["workdayEnd"]?.GetValue<string>(), out var parsedEnd) ? parsedEnd : fallback.WorkdayEnd;
        var days = calendar["workingDays"] is JsonArray dayArray
            ? dayArray.Select(x => x?.GetValue<int>()).Where(x => x.HasValue && Enum.IsDefined((DayOfWeek)x.Value)).Select(x => (DayOfWeek)x!.Value).Distinct().ToList()
            : fallback.WorkingDays.ToList();
        var holidays = calendar["holidays"] is JsonArray holidayArray
            ? holidayArray.Select(x => DateOnly.TryParse(x?.GetValue<string>(), out var date) ? date : (DateOnly?)null).Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList()
            : [];
        return new SupportBusinessCalendarDto(zone, start, end, days.Count == 0 ? fallback.WorkingDays : days, holidays);
    }

    public async Task<SupportBusinessCalendarDto> SaveCalendarAsync(Guid companyId, Guid userId, SaveSupportBusinessCalendarRequest request, CancellationToken cancellationToken)
    {
        _ = ResolveTimeZone(request.TimeZoneId);
        if (request.WorkdayEnd <= request.WorkdayStart || request.WorkingDays.Count == 0)
        {
            throw new SupportValidationException(new Dictionary<string, string[]> { [nameof(request.WorkdayEnd)] = ["Working hours need at least one day and an end time after the start time."] });
        }

        var company = await _dbContext.Companies.SingleAsync(x => x.Id == companyId, cancellationToken);
        company.Settings.Extensions["supportBusinessCalendar"] = new JsonObject
        {
            ["timeZoneId"] = request.TimeZoneId,
            ["workdayStart"] = request.WorkdayStart.ToString("HH:mm"),
            ["workdayEnd"] = request.WorkdayEnd.ToString("HH:mm"),
            ["workingDays"] = new JsonArray(request.WorkingDays.Distinct().Select(x => (JsonNode?)JsonValue.Create((int)x)).ToArray()),
            ["holidays"] = new JsonArray(request.Holidays.Distinct().OrderBy(x => x).Select(x => (JsonNode?)JsonValue.Create(x.ToString("yyyy-MM-dd"))).ToArray())
        };
        company.UpdateBrandingAndSettings(company.Branding, company.Settings);
        await _audit.WriteAsync(new AuditEventWriteRequest(companyId, AuditActorTypes.Human, userId, "support.business_calendar.saved", "company", companyId.ToString("D"), AuditEventOutcomes.Succeeded, "Support working calendar saved.", ["support", "settings"]), cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return await GetCalendarAsync(companyId, cancellationToken);
    }

    private static DateTime AddBusinessMinutes(DateTime startUtc, int minutes, SupportBusinessCalendarDto calendar)
    {
        var zone = ResolveTimeZone(calendar.TimeZoneId);
        var cursor = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(startUtc, DateTimeKind.Utc), zone);
        var remaining = minutes;
        var holidays = calendar.Holidays.ToHashSet();
        for (var guard = 0; guard < 3660 && remaining > 0; guard++)
        {
            var date = DateOnly.FromDateTime(cursor);
            if (calendar.WorkingDays.Contains(cursor.DayOfWeek) && !holidays.Contains(date))
            {
                var dayStart = date.ToDateTime(calendar.WorkdayStart, DateTimeKind.Unspecified);
                var dayEnd = date.ToDateTime(calendar.WorkdayEnd, DateTimeKind.Unspecified);
                var effective = cursor < dayStart ? dayStart : cursor;
                if (effective < dayEnd)
                {
                    var available = (int)Math.Floor((dayEnd - effective).TotalMinutes);
                    var consumed = Math.Min(remaining, available);
                    effective = effective.AddMinutes(consumed);
                    remaining -= consumed;
                    cursor = effective;
                    if (remaining == 0) return TimeZoneInfo.ConvertTimeToUtc(AdjustInvalidLocalTime(cursor, zone), zone);
                }
            }

            cursor = date.AddDays(1).ToDateTime(calendar.WorkdayStart, DateTimeKind.Unspecified);
        }

        throw new InvalidOperationException("The support working calendar could not produce a deadline within ten years.");
    }

    private static DateTime AdjustInvalidLocalTime(DateTime value, TimeZoneInfo zone)
    {
        while (zone.IsInvalidTime(value)) value = value.AddMinutes(30);
        return value;
    }

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { throw new SupportValidationException(new Dictionary<string, string[]> { ["timeZoneId"] = ["Choose a valid timezone."] }); }
        catch (InvalidTimeZoneException) { throw new SupportValidationException(new Dictionary<string, string[]> { ["timeZoneId"] = ["Choose a valid timezone."] }); }
    }

    private static (int First, int Resolution) DefaultDurations(string priority) => priority switch
    {
        SupportPriorities.Urgent => (60, 480),
        SupportPriorities.High => (120, 1440),
        SupportPriorities.Low => (1440, 10080),
        _ => (480, 4320)
    };

    private static SupportSlaPolicyDto Map(SupportSlaPolicy policy) =>
        new(policy.Id, policy.Name, policy.Category, SupportLabels.Category(policy.Category), policy.Priority, SupportLabels.Priority(policy.Priority), policy.CustomerTier, policy.FirstResponseMinutes, policy.ResolutionMinutes, policy.IsActive, policy.UpdatedUtc, policy.TimeBasis, policy.RiskThresholdMinutes, policy.EscalationRecipientRole);
}

