using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Infrastructure.BackgroundJobs;
namespace VirtualCompany.Infrastructure.Companies;

public sealed class BriefingCadenceService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
    CompanyWorkVisibility visibility, IBriefingUpdateJobProducer jobs, ICompanyOutboxEnqueuer outbox,
    ICompanyNotificationDispatcher notifications, TimeProvider clock, IAuditEventWriter audit) : IBriefingCadenceService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Areas = ["company", "sales", "marketing", "finance", "support"];
    private IQueryable<BriefingCadenceDelivery> Deliveries(Guid company) => db.BriefingCadenceDeliveries.Where(x => x.CompanyId == company);
    private async Task<Guid> User(Guid company, CancellationToken ct) => (await memberships.ResolveAsync(company, ct) ?? throw new UnauthorizedAccessException()).UserId;
    private Task<CompanyBriefingDeliveryPreference?> Preference(Guid company, Guid user, CancellationToken ct) => db.CompanyBriefingDeliveryPreferences.SingleOrDefaultAsync(x => x.CompanyId == company && x.UserId == user, ct);
    private static BriefingCadenceSettings Read(CompanyBriefingDeliveryPreference p) => JsonSerializer.Deserialize<BriefingCadenceSettings>(p.CadenceSettingsJson!, Json) ?? throw new InvalidDataException("Briefing settings cannot be read.");
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Json)))).ToLowerInvariant();
    private static string Path(Guid company, Guid id) => $"/briefings?companyId={company:D}&deliveryId={id:D}";

    public async Task<BriefingCadenceContext> GetAsync(Guid companyId, CancellationToken ct)
    {
        var user = await User(companyId, ct); var scope = await visibility.ResolveRecipientAsync(companyId, user, ct);
        var p = await Preference(companyId, user, ct); var zone = scope.Resolution.CompanyId == companyId
            ? await db.Companies.Where(x => x.Id == companyId).Select(x => x.Timezone).SingleAsync(ct) : "UTC";
        var s = p?.CadenceSettingsJson == null ? Defaults(scope, p?.PreferredTimezone ?? zone ?? "UTC") : Read(p);
        s = s with { DeliveryEnabled = p?.InAppEnabled ?? true };
        var people = new List<BriefingCadencePerson>();
        var ids = await db.CompanyMemberships.Where(x => x.CompanyId == companyId && x.Status == CompanyMembershipStatus.Active && x.UserId != null).Select(x => x.UserId!.Value).Distinct().ToArrayAsync(ct);
        foreach (var id in ids)
        {
            try { var r = await visibility.ResolveRecipientAsync(companyId, id, ct); var name = await db.Users.Where(x => x.Id == id).Select(x => x.DisplayName).SingleAsync(ct); people.Add(new(id, name, r.Areas.Append("company").Distinct().Order().ToArray())); }
            catch (UnauthorizedAccessException) { }
        }
        return new(companyId, user, p?.CadenceSettingsJson != null, s, people.ToArray(), scope.Areas.Append("company").Distinct().Order().ToArray(), ["in_app"]);
    }
    private static BriefingCadenceSettings Defaults(CompanyWorkScope scope, string zone)
    {
        var role = scope.Executive ? "ceo" : scope.Areas.FirstOrDefault(x => x != "company") ?? "ceo";
        return new(role, zone, new(8, 0), new(18, 0), [1, 2, 3, 4, 5], new(20, 0), new(7, 0), true, true, true, false,
            role == "ceo" ? scope.Areas.Append("company").Distinct().Order().ToArray() : [role],
            [new("morning", true, new(8, 30)), new("end_of_day", false, new(17, 30)), new("shift_handover", role == "support", new(15, 0)),
             new("weekly", true, new(9, 0)), new("monthly", true, new(9, 0)), new("quarterly", false, new(9, 0)), new("annual", false, new(9, 0))]);
    }
    public async Task<BriefingCadenceContext> SaveAsync(Guid companyId, BriefingCadenceSettings s, CancellationToken ct)
    {
        var c = await GetAsync(companyId, ct); Validate(s, c);
        var p = await Preference(companyId, c.UserId, ct);
        if (p == null) { p = new(Guid.NewGuid(), companyId, c.UserId); db.Add(p); }
        p.Update(s.DeliveryEnabled, p.MobileEnabled, p.DailyEnabled, p.WeeklyEnabled, p.PreferredDeliveryTime, p.PreferredTimezone);
        p.SetCadenceSettings(JsonSerializer.Serialize(s, Json));
        await audit.WriteAsync(new(companyId, "user", c.UserId, "briefing.cadence.preferences_changed", "briefing_preferences", p.Id.ToString("N"), "succeeded",
            "Changed briefing schedules and routing. No record or approval authority is granted.", Metadata: new Dictionary<string, string?> { ["settingsHash"] = Hash(s) }), ct);
        await db.SaveChangesAsync(ct); return await GetAsync(companyId, ct);
    }
    private static void Validate(BriefingCadenceSettings s, BriefingCadenceContext c)
    {
        if (s == null || s.Role is not ("ceo" or "sales" or "marketing" or "finance" or "support")) throw new ArgumentException("Choose a supported role default.");
        try { BriefingCadenceCalendar.Zone(s.Timezone); } catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) { throw new ArgumentException("Choose a valid timezone."); }
        if (s.WorkStart == s.WorkEnd || s.Workdays == null || s.Workdays.Length == 0 || s.Workdays.Length > 7 || s.Workdays.Any(x => x < 0 || x > 6) || s.Workdays.Distinct().Count() != s.Workdays.Length) throw new ArgumentException("Choose distinct working days and a non-empty working interval.");
        if (s.FocusAreas == null || s.FocusAreas.Length == 0 || s.FocusAreas.Distinct().Count() != s.FocusAreas.Length || s.FocusAreas.Any(x => !Areas.Contains(x) || !c.AllowedAreas.Contains(x))) throw new ArgumentException("Choose focus areas available to your current responsibilities.");
        if (s.Schedules == null || s.Schedules.Length != 7 || s.Schedules.Any(x => x == null || !BriefingCadenceCalendar.Kinds.Contains(x.Kind) || x.Weekday < 0 || x.Weekday > 6 || x.Day < 1 || x.Day > 31 || x.Month < 1 || x.Month > 12 || x.LocalTime.Second != 0) || s.Schedules.Select(x => x.Kind).Distinct().Count() != 7) throw new ArgumentException("Provide each of the seven schedules with valid local times and calendar checkpoints.");
        if (s.AbsenceStartUtc.HasValue != s.AbsenceEndUtc.HasValue || s.AbsenceStartUtc.HasValue && (s.AbsenceStartUtc >= s.AbsenceEndUtc || s.AbsenceStartUtc.Value.Kind != DateTimeKind.Utc || s.AbsenceEndUtc!.Value.Kind != DateTimeKind.Utc)) throw new ArgumentException("Provide an increasing UTC absence interval.");
        foreach (var id in new[] { s.DelegateUserId, s.FallbackUserId }.Where(x => x.HasValue))
            if (id == c.UserId || !c.EligiblePeople.Any(x => x.Id == id && s.FocusAreas.All(a => x.Areas.Contains(a)))) throw new ArgumentException("Routing requires another active member with the selected responsibilities. It grants no access.");
        if (s.AbsenceStartUtc.HasValue && !s.FallbackUserId.HasValue) throw new ArgumentException("Choose an accountable eligible fallback for absence routing.");
        if ((s.Schedules.Any(x => x.Enabled) || s.UrgentEnabled && !s.UrgentOutsideHours) &&
            !Enumerable.Range(0, 7).Any(day => Enumerable.Range(0, 1440).Any(minute => BriefingCadenceCalendar.Allowed(new DateTime(2026, 1, 4).AddDays(day).AddMinutes(minute), s))))
            throw new ArgumentException("Working and quiet hours leave no permitted delivery time.");
    }
    private async Task<(Guid? User, string Routing)> Route(Guid company, Guid owner, BriefingCadenceSettings s, DateTime now, CancellationToken ct)
    {
        if (s.AbsenceStartUtc == null || now < s.AbsenceStartUtc || now >= s.AbsenceEndUtc) return (await Eligible(company, owner, s, ct) ? owner : null, "self");
        if (s.DelegateUserId.HasValue && await Eligible(company, s.DelegateUserId.Value, s, ct) && !await Absent(company, s.DelegateUserId.Value, now, ct)) return (s.DelegateUserId, "delegate");
        if (s.FallbackUserId.HasValue && await Eligible(company, s.FallbackUserId.Value, s, ct) && !await Absent(company, s.FallbackUserId.Value, now, ct)) return (s.FallbackUserId, "fallback");
        return (null, "no_eligible_recipient");
    }
    private async Task<bool> Absent(Guid c, Guid u, DateTime now, CancellationToken ct)
    { var p = await Preference(c, u, ct); if (p?.CadenceSettingsJson == null) return false; var s = Read(p); return s.AbsenceStartUtc <= now && s.AbsenceEndUtc > now; }
    private async Task<bool> Eligible(Guid c, Guid u, BriefingCadenceSettings s, CancellationToken ct)
    { try { var scope = await visibility.ResolveRecipientAsync(c, u, ct); return s.FocusAreas.All(a => a == "company" || scope.Allows(a)); } catch (UnauthorizedAccessException) { return false; } }
    private async Task<BriefingCadenceItem[]> Items(Guid company, Guid recipient, BriefingCadenceSettings s, bool urgent, DateTime now, CancellationToken ct)
    {
        var scope = await visibility.ResolveRecipientAsync(company, recipient, ct);
        var tasks = await scope.Tasks(db.WorkTasks).Where(x => x.CompanyId == company && (x.CompletedUtc == null || x.CompletedUtc >= now.AddDays(-31)))
            .Include(x => x.AssignedAgent).Include(x => x.DecisionOrigin).OrderByDescending(x => x.UpdatedUtc).ThenBy(x => x.Id).ToListAsync(ct);
        return tasks.Where(x => s.FocusAreas.Contains(CompanyWorkScope.Area(x.AssignedAgent?.Department, x.Type)))
            .Where(x => !urgent || x.Status == WorkTaskStatus.Blocked || x.Status == WorkTaskStatus.Failed || (x.DueUtc < now && x.Status != WorkTaskStatus.Completed))
            .Take(100).Select(x => new BriefingCadenceItem(x.Id, x.Title, x.Status.ToStorageValue(), CompanyWorkScope.Area(x.AssignedAgent?.Department, x.Type), x.Priority.ToStorageValue(), x.DueUtc, x.UpdatedUtc,
                $"/work?companyId={company:D}&taskId={x.Id:D}", x.DecisionOrigin != null && x.DecisionOrigin.CreatedByUserId == recipient && scope.Resolution.CanRequestReview ? $"/work/source?companyId={company:D}&taskId={x.Id:D}" : null,
                x.DecisionOrigin?.SourceKind, x.DecisionOrigin?.SourceVersion)).ToArray();
    }
    public async Task<BriefingCadencePreview> PreviewAsync(Guid companyId, CancellationToken ct)
    {
        var c = await GetAsync(companyId, ct); var now = clock.GetUtcNow().UtcDateTime;
        var next = c.Settings.DeliveryEnabled ? BriefingCadenceCalendar.Next(c.Settings, now) : null; var route = await Route(companyId, c.UserId, c.Settings, next?.Utc ?? now, ct);
        var previewUser = route.User == c.UserId ? c.UserId : route.User;
        // The requester sees the intersection of their scope and the routed recipient's scope.
        var mine = await Items(companyId, c.UserId, c.Settings, false, now, ct);
        var routed = previewUser.HasValue ? await Items(companyId, previewUser.Value, c.Settings, false, now, ct) : [];
        var ids = routed.Select(x => x.Id).ToHashSet(); var items = mine.Where(x => ids.Contains(x.Id)).ToArray();
        return new(companyId, c.UserId, route.User, route.Routing, c.Settings.Timezone, next?.Utc,
            next == null ? null : new DateTimeOffset(next.Utc).ToOffset(BriefingCadenceCalendar.Zone(c.Settings.Timezone).GetUtcOffset(next.Utc)).ToString("yyyy-MM-dd HH:mm zzz"),
            items.Select(x => (DateTime?)x.UpdatedUtc).Max() ?? now, next?.Kinds ?? [], items, await Audit(companyId, c.UserId, ct));
    }
    public async Task<BriefingCadencePreview> OpenAsync(Guid companyId, Guid deliveryId, CancellationToken ct)
    {
        var user = await User(companyId, ct); var d = await Deliveries(companyId).AsNoTracking().SingleOrDefaultAsync(x => x.Id == deliveryId && (x.OwnerUserId == user || x.RecipientUserId == user), ct) ?? throw new KeyNotFoundException();
        var p = await Preference(companyId, d.OwnerUserId, ct); if (p?.CadenceSettingsJson == null) throw new KeyNotFoundException();
        var s = Read(p); var now = clock.GetUtcNow().UtcDateTime;
        var route = await Route(companyId, d.OwnerUserId, s, now, ct);
        if (user != d.OwnerUserId && (route.User != user || !await Eligible(companyId, user, s, ct))) throw new UnauthorizedAccessException();
        var items = await Items(companyId, user, s, d.Cadence == "urgent", now, ct);
        return new(companyId, user, d.RecipientUserId, d.Routing, s.Timezone, d.ScheduledUtc,
            new DateTimeOffset(d.ScheduledUtc).ToOffset(BriefingCadenceCalendar.Zone(s.Timezone).GetUtcOffset(d.ScheduledUtc)).ToString("yyyy-MM-dd HH:mm zzz"),
            items.Select(x => (DateTime?)x.UpdatedUtc).Max() ?? now, d.Cadence.Split(','), items, [ToAudit(d)]);
    }
    private async Task<BriefingCadenceAudit[]> Audit(Guid c, Guid user, CancellationToken ct) => (await Deliveries(c).AsNoTracking().Where(x => x.OwnerUserId == user || x.RecipientUserId == user).OrderByDescending(x => x.ScheduledUtc).Take(30).ToArrayAsync(ct)).Select(ToAudit).ToArray();
    private static BriefingCadenceAudit ToAudit(BriefingCadenceDelivery d) => new(d.Id, d.Cadence, d.ScheduledUtc, d.RecipientUserId, d.Routing, d.Status, d.Attempts, d.Reason, d.UpdatedUtc, Path(d.CompanyId, d.Id));

    public async Task<int> ScheduleDueAsync(Guid companyId, DateTime nowUtc, CancellationToken ct)
    {
        var preferences = await db.CompanyBriefingDeliveryPreferences.Where(x => x.CompanyId == companyId && x.CadenceSettingsJson != null && x.InAppEnabled).ToArrayAsync(ct);
        var count = 0;
        foreach (var pending in await Deliveries(companyId).Where(x => x.Status == BriefingCadenceDeliveryStates.Queued).ToArrayAsync(ct))
            await QueueJob(pending, ct);
        foreach (var p in preferences)
        {
            var s = Read(p); if (!await Eligible(companyId, p.UserId, s, ct)) continue;
            // Bounded catch-up: at most the current local day; stable slot keys survive retries, edits and DST folds.
            var zone = BriefingCadenceCalendar.Zone(s.Timezone); var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
            var start = BriefingCadenceCalendar.ToUtc(local.Date, zone);
            foreach (var slot in BriefingCadenceCalendar.Allowed(local, s) ? BriefingCadenceCalendar.Slots(s, start, nowUtc) : [])
                count += await Enqueue(p, s, slot.Key, string.Join(',', slot.Kinds), slot.Utc, ct);
            if (s.UrgentEnabled && (s.UrgentOutsideHours || BriefingCadenceCalendar.Allowed(local, s)))
            {
                var urgent = await Items(companyId, p.UserId, s, true, nowUtc, ct);
                if (urgent.Length > 0) count += await Enqueue(p, s, "urgent:" + Hash(urgent), "urgent", nowUtc, ct);
            }
        }
        return count;
    }
    private async Task<int> Enqueue(CompanyBriefingDeliveryPreference p, BriefingCadenceSettings s, string key, string cadence, DateTime utc, CancellationToken ct)
    {
        if (await Deliveries(p.CompanyId).AnyAsync(x => x.OwnerUserId == p.UserId && x.SlotKey == key, ct)) return 0;
        var d = new BriefingCadenceDelivery { Id = Guid.NewGuid(), CompanyId = p.CompanyId, OwnerUserId = p.UserId,
            Cadence = cadence, SlotKey = key, ScheduledUtc = utc, CreatedUtc = clock.GetUtcNow().UtcDateTime, UpdatedUtc = clock.GetUtcNow().UtcDateTime, SettingsHash = Hash(s) };
        db.Add(d);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { db.Entry(d).State = EntityState.Detached; if (await Deliveries(p.CompanyId).AnyAsync(x => x.OwnerUserId == p.UserId && x.SlotKey == key, ct)) return 0; throw; }
        // A durable pending ledger with no job is repaired on the next scheduler pass below.
        await QueueJob(d, ct); return 1;
    }
    private Task<BriefingUpdateJobEnqueueResult> QueueJob(BriefingCadenceDelivery d, CancellationToken ct) => jobs.EnqueueScheduledAsync(d.CompanyId, "daily", "daily", "briefing-cadence:" + d.Id.ToString("N"),
        $"briefing-cadence:{d.CompanyId:N}:{d.OwnerUserId:N}:{d.SlotKey}", new() { ["cadenceDeliveryId"] = JsonValue.Create(d.Id), ["scheduledAtUtc"] = JsonValue.Create(d.ScheduledUtc) }, ct);

    private static bool Enabled(BriefingCadenceDelivery d, BriefingCadenceSettings s) => d.Cadence == "urgent" ? s.UrgentEnabled : d.Cadence.Split(',').Any(kind => s.Schedules.Any(x => x.Kind == kind && x.Enabled));
    private void RoutingFailure(BriefingCadenceDelivery d)
    {
        Set(d, BriefingCadenceDeliveryStates.Failed, "No eligible delegate or fallback. Accountable owner must revise absence routing.");
        outbox.Enqueue(d.CompanyId, CompanyOutboxTopics.NotificationDeliveryRequested, new NotificationDeliveryRequestedMessage(d.CompanyId, "escalation", "high",
            "Briefing absence routing needs attention", "No currently eligible recipient is available. Review your briefing absence routing.", "briefing_cadence_delivery", d.Id,
            $"/briefing-preferences?companyId={d.CompanyId:D}", d.OwnerUserId, null, null, null, "cadence-routing:" + d.Id.ToString("N"), null), idempotencyKey: "cadence-routing:" + d.Id.ToString("N"));
    }

    public async Task<CompanyBriefingGenerationResult> GenerateAsync(BriefingGenerationJobContext job, CancellationToken ct)
    {
        var id = Guid.Parse(job.SourceMetadata["cadenceDeliveryId"]!.ToString()); var d = await Deliveries(job.CompanyId).SingleAsync(x => x.Id == id, ct);
        if (d.Status == BriefingCadenceDeliveryStates.Queued)
        {
            var p = await Preference(d.CompanyId, d.OwnerUserId, ct); var now = clock.GetUtcNow().UtcDateTime;
            if (p?.CadenceSettingsJson == null || !p.InAppEnabled || !Enabled(d, Read(p)) || !await Eligible(d.CompanyId, d.OwnerUserId, Read(p), ct)) Set(d, BriefingCadenceDeliveryStates.Suppressed, "Owner is no longer eligible or delivery is disabled.");
            else
            {
                var s = Read(p); var r = await Route(d.CompanyId, d.OwnerUserId, s, now, ct); d.RecipientUserId = r.User; d.Routing = r.Routing;
                if (!r.User.HasValue) RoutingFailure(d);
                else
                {
                    var items = await Items(d.CompanyId, r.User.Value, s, d.Cadence == "urgent", now, ct); d.ContentHash = Hash(items);
                    var unchanged = s.SuppressUnchanged && !d.Cadence.Contains("quarterly") && !d.Cadence.Contains("annual") && await Deliveries(d.CompanyId).AnyAsync(x => x.Id != d.Id && x.OwnerUserId == d.OwnerUserId && x.RecipientUserId == r.User && x.ContentHash == d.ContentHash && (x.Cadence == "urgent") == (d.Cadence == "urgent") && x.Status == BriefingCadenceDeliveryStates.Sent, ct);
                    if (unchanged || d.Cadence == "urgent" && items.Length == 0) Set(d, BriefingCadenceDeliveryStates.Suppressed, "No content change since the preceding briefing.");
                    else { Set(d, BriefingCadenceDeliveryStates.Ready, null); outbox.Enqueue(d.CompanyId, CompanyOutboxTopics.BriefingCadenceDeliveryRequested, new BriefingCadenceDeliveryRequest(d.CompanyId, d.Id), idempotencyKey: "briefing-delivery:" + d.Id.ToString("N")); }
                }
            }
            await db.SaveChangesAsync(ct);
        }
        return new(new CompanyBriefingDto(d.Id, d.CompanyId, d.Cadence, d.ScheduledUtc, d.ScheduledUtc, "Scheduled briefing", d.Reason ?? "Open the current authorized briefing.", new() { ["deliveryStatus"] = JsonValue.Create(d.Status), ["deliveryPath"] = JsonValue.Create(Path(d.CompanyId, d.Id)) }, [], null, d.CreatedUtc, new() { ["settingsHash"] = JsonValue.Create(d.SettingsHash) }), false, d.Status == BriefingCadenceDeliveryStates.Ready ? 1 : 0);
    }
    private void Set(BriefingCadenceDelivery d, string status, string? reason) { d.Status = status; d.Reason = reason; d.UpdatedUtc = clock.GetUtcNow().UtcDateTime; }
    public async Task DeliverAsync(BriefingCadenceDeliveryRequest request, CancellationToken ct)
    {
        var d = await Deliveries(request.CompanyId).SingleOrDefaultAsync(x => x.Id == request.DeliveryId, ct) ?? throw new KeyNotFoundException();
        if (d.Status is BriefingCadenceDeliveryStates.Sent or BriefingCadenceDeliveryStates.Suppressed or BriefingCadenceDeliveryStates.Failed) return;
        var p = await Preference(d.CompanyId, d.OwnerUserId, ct); var now = clock.GetUtcNow().UtcDateTime;
        var key = "cadence-delivery:" + d.Id.ToString("N");
        var previous = await db.CompanyNotifications.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == d.CompanyId && x.DedupeKey.StartsWith(key + ":"), ct);
        if (previous != null) { d.RecipientUserId = previous.UserId; Set(d, BriefingCadenceDeliveryStates.Sent, "Reconciled existing idempotent notification."); await db.SaveChangesAsync(ct); return; }
        if (p?.CadenceSettingsJson == null || !p.InAppEnabled || !Enabled(d, Read(p)) || !await Eligible(d.CompanyId, d.OwnerUserId, Read(p), ct)) { Set(d, BriefingCadenceDeliveryStates.Suppressed, "Owner access changed or delivery disabled before dispatch."); await db.SaveChangesAsync(ct); return; }
        var s = Read(p); var r = await Route(d.CompanyId, d.OwnerUserId, s, now, ct);
        if (!(d.Cadence == "urgent" && s.UrgentOutsideHours) && !BriefingCadenceCalendar.Allowed(TimeZoneInfo.ConvertTimeFromUtc(now, BriefingCadenceCalendar.Zone(s.Timezone)), s))
        { Set(d, BriefingCadenceDeliveryStates.Suppressed, "Delivery window closed before dispatch. The next permitted schedule remains active."); await db.SaveChangesAsync(ct); return; }
        if (!r.User.HasValue) { RoutingFailure(d); await db.SaveChangesAsync(ct); return; }
        d.RecipientUserId = r.User; d.Routing = r.Routing;
        var items = await Items(d.CompanyId, r.User.Value, s, d.Cadence == "urgent", now, ct); d.ContentHash = Hash(items);
        if (d.Cadence == "urgent" && items.Length == 0 || s.SuppressUnchanged && !d.Cadence.Contains("quarterly") && !d.Cadence.Contains("annual") && await Deliveries(d.CompanyId).AnyAsync(x => x.Id != d.Id && x.OwnerUserId == d.OwnerUserId && x.RecipientUserId == r.User && x.ContentHash == d.ContentHash && (x.Cadence == "urgent") == (d.Cadence == "urgent") && x.Status == BriefingCadenceDeliveryStates.Sent, ct))
        { Set(d, BriefingCadenceDeliveryStates.Suppressed, "No content change at delivery."); await db.SaveChangesAsync(ct); return; }
        d.Attempts++; Set(d, BriefingCadenceDeliveryStates.Ready, null); await db.SaveChangesAsync(ct);
        try
        {
            // Keep notification previews safe after later revocation: source titles and snapshots are read only on open.
            await notifications.DispatchAsync(new(d.CompanyId, d.Cadence == "urgent" ? "escalation" : "briefing_available", d.Cadence == "urgent" ? "high" : "normal",
                d.Cadence == "urgent" ? "Urgent work escalation" : "Your scheduled briefing", "Open your briefing to review current authorized work and source freshness.",
                "briefing_cadence_delivery", d.Id, Path(d.CompanyId, d.Id), r.User, null, null, JsonSerializer.Serialize(new { d.Cadence, d.ScheduledUtc, d.Routing }, Json), key, d.Id.ToString("N")), ct);
            Set(d, BriefingCadenceDeliveryStates.Sent, null); await db.SaveChangesAsync(ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var permanent = e is PermanentBackgroundJobException or UnauthorizedAccessException or ArgumentException or KeyNotFoundException or InvalidDataException;
            Set(d, e is TimeoutException ? BriefingCadenceDeliveryStates.Uncertain : permanent || d.Attempts >= 5 ? BriefingCadenceDeliveryStates.Failed : BriefingCadenceDeliveryStates.Retry,
                e is TimeoutException ? "Delivery outcome uncertain. Reconcile the idempotent notification before retry." : "Delivery failed. The existing outbox applies bounded retry.");
            await db.SaveChangesAsync(ct); throw;
        }
    }
}
