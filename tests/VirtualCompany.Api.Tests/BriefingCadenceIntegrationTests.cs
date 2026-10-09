using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
namespace VirtualCompany.Api.Tests;
public sealed class BriefingCadenceIntegrationTests
{
    [Fact] public async Task Delegate_with_only_owned_work_gets_the_task_and_no_private_retained_source_link()
    {
        var clock = new SupportQualityFixture.Clock(); using var f = new TestWebApplicationFactory(clock); var origin = await DecisionWorkFixture.Seed(f); using var owner = origin.Planning.Annual.Client(f);
        var person = Guid.NewGuid(); var fallback = Guid.NewGuid(); await f.SeedAsync(db => { foreach (var (id, subject) in new[] { (person, "p29-own-work"), (fallback, "p29-own-fallback") })
            db.AddRange(new User(id, subject + "@example.test", subject, "dev-header", subject), new CompanyMembership(Guid.NewGuid(), origin.Company, id, CompanyMembershipRole.Admin, CompanyMembershipStatus.Active)); return Task.CompletedTask; });
        var work = await DecisionWorkFixture.Create(owner, origin, origin.Input() with { OwnerUserId = person });
        var s = new BriefingCadenceFixture(origin.Company, origin.Planning.Annual.Quarter.Company.Owner, person, fallback, work.TaskId, Guid.Empty);
        await s.Save(owner, BriefingCadenceFixture.Settings with { Role = "ceo", FocusAreas = ["company"], AbsenceStartUtc = SupportQualityFixture.Now.AddDays(-1), AbsenceEndUtc = SupportQualityFixture.Now.AddDays(2), DelegateUserId = person, FallbackUserId = fallback });
        var preview = await owner.GetFromJsonAsync<BriefingCadencePreview>(s.Root + "/preview"); Assert.Contains(preview!.Items, x => x.Id == work.TaskId && x.RetainedSourcePath != null);
        await s.Schedule(f, SupportQualityFixture.Now); await s.RunJobs(f); await s.Dispatch(f);
        var audit = (await owner.GetFromJsonAsync<BriefingCadencePreview>(s.Root + "/preview"))!.Audit.Single(); using var delegateClient = WeeklyWorkspaceFixture.Client(f, "p29-own-work"); delegateClient.DefaultRequestHeaders.Add("X-Company-Id", origin.Company.ToString());
        var opened = await delegateClient.GetFromJsonAsync<BriefingCadencePreview>(s.Root + $"/deliveries/{audit.Id}"); var item = Assert.Single(opened!.Items); Assert.Equal(work.TaskId, item.Id); Assert.Null(item.RetainedSourcePath);
        Assert.Equal(HttpStatusCode.Forbidden, (await delegateClient.PostAsync(s.Root.Replace("briefings/cadence", "decision-work") + $"/tasks/{work.TaskId}/open", null)).StatusCode);
    }
    [Fact] public async Task Existing_scheduler_enqueues_recipient_jobs_and_delivery_switch_controls_them()
    {
        var clock = new BriefingCadenceFixture.Clock(); using var f = new TestWebApplicationFactory(clock); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f);
        await s.Save(h, BriefingCadenceFixture.Settings with { DeliveryEnabled = false }); Assert.Equal(0, await s.Schedule(f, clock.Now)); Assert.Null((await h.GetFromJsonAsync<BriefingCadencePreview>(s.Root + "/preview"))!.NextDeliveryUtc);
        await s.Save(h); await f.ExecuteScopeAsync(async scope => await scope.ServiceProvider.GetRequiredService<ICompanyBriefingService>().GenerateDueAsync(new(clock.Now), default));
        await f.SeedAsync(async db => { Assert.Single(await db.BriefingCadenceDeliveries.IgnoreQueryFilters().ToArrayAsync()); Assert.Contains(await db.CompanyBriefingUpdateJobs.IgnoreQueryFilters().ToArrayAsync(), x => x.SourceMetadata.ContainsKey("cadenceDeliveryId")); });
    }
    [Fact] public async Task Owner_permission_reduction_and_closed_dispatch_window_suppress_queued_content()
    {
        var clock = new BriefingCadenceFixture.Clock(); using var f = new TestWebApplicationFactory(clock); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f); await s.Save(h); await s.Schedule(f, clock.Now); await s.RunJobs(f);
        clock.Now = clock.Now.AddHours(16); await s.Dispatch(f); await f.SeedAsync(async db => { Assert.Equal("suppressed", (await db.BriefingCadenceDeliveries.IgnoreQueryFilters().SingleAsync()).Status); Assert.Empty(await db.CompanyNotifications.IgnoreQueryFilters().Where(x => x.RelatedEntityType == "briefing_cadence_delivery").ToArrayAsync()); });
        clock.Now = clock.Now.AddHours(-16).AddDays(1); await s.Schedule(f, clock.Now); await s.RunJobs(f);
        await f.SeedAsync(async db => (await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x => x.UserId == s.Owner)).UpdateRole(CompanyMembershipRole.SupportSupervisor));
        await s.Dispatch(f); var p = await h.GetFromJsonAsync<BriefingCadencePreview>(s.Root + "/preview"); Assert.Empty(p!.Items);
        await f.SeedAsync(async db => Assert.Equal(2, await db.BriefingCadenceDeliveries.IgnoreQueryFilters().CountAsync(x => x.Status == "suppressed")));
    }
    [Fact] public async Task Saved_recipient_schedule_runs_native_job_outbox_and_deduplicates_unchanged_content()
    {
        var clock = new BriefingCadenceFixture.Clock(); using var f = new TestWebApplicationFactory(clock); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f);
        var saved = await s.Save(h); Assert.True(saved.Configured); Assert.Equal(7, saved.Settings.Schedules.Length);
        var p = await h.GetFromJsonAsync<BriefingCadencePreview>(s.Root + "/preview"); Assert.Equal(s.SalesTask, Assert.Single(p!.Items).Id); Assert.DoesNotContain(p.Items, x => x.Id == s.FinanceTask); Assert.Contains("+02:00", p.NextDeliveryLocal);
        Assert.Equal(0, await s.Schedule(f, clock.Now.AddHours(-1))); Assert.Equal(1, await s.Schedule(f, clock.Now)); Assert.Equal(0, await s.Schedule(f, clock.Now));
        await s.RunJobs(f); await s.Dispatch(f); await s.Dispatch(f);
        Guid delivery = Guid.Empty; await f.SeedAsync(async db => { var d = await db.BriefingCadenceDeliveries.IgnoreQueryFilters().SingleAsync(); delivery = d.Id; Assert.Equal("sent", d.Status); Assert.Equal(1, d.Attempts);
            var n = Assert.Single(await db.CompanyNotifications.IgnoreQueryFilters().Where(x => x.RelatedEntityType == "briefing_cadence_delivery").ToArrayAsync()); Assert.DoesNotContain("Confirm recorded", n.Body); Assert.Contains("/briefings?", n.ActionUrl); });
        var opened = await h.GetFromJsonAsync<BriefingCadencePreview>(s.Root + $"/deliveries/{delivery}"); Assert.Equal(s.SalesTask, Assert.Single(opened!.Items).Id);
        clock.Now = clock.Now.AddDays(1); Assert.Equal(1, await s.Schedule(f, clock.Now)); await s.RunJobs(f); await s.Dispatch(f);
        await f.SeedAsync(async db => { Assert.Equal(1, await db.BriefingCadenceDeliveries.IgnoreQueryFilters().CountAsync(x => x.Status == "suppressed")); });
    }
    [Fact] public async Task Absence_routes_to_delegate_then_rechecks_revocation_and_uses_fallback_without_authority_grants()
    {
        var clock = new BriefingCadenceFixture.Clock(); using var f = new TestWebApplicationFactory(clock); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f);
        await s.Save(h, BriefingCadenceFixture.Settings with { AbsenceStartUtc = clock.Now.AddDays(-1), AbsenceEndUtc = clock.Now.AddDays(2), DelegateUserId = s.Delegate, FallbackUserId = s.Fallback });
        await s.Schedule(f, clock.Now); await s.RunJobs(f);
        await f.SeedAsync(async db => (await db.CompanyMemberships.IgnoreQueryFilters().SingleAsync(x => x.UserId == s.Delegate)).UpdateStatus(CompanyMembershipStatus.Revoked));
        await s.Dispatch(f); await f.SeedAsync(async db => { var d = await db.BriefingCadenceDeliveries.IgnoreQueryFilters().SingleAsync(); Assert.Equal(s.Fallback, d.RecipientUserId); Assert.Equal("fallback", d.Routing); Assert.Equal("sent", d.Status); Assert.Empty(await db.ApprovalRequests.IgnoreQueryFilters().ToArrayAsync()); });
        using var revoked = s.Client(f, "p29-delegate"); Assert.Equal(HttpStatusCode.Forbidden, (await revoked.GetAsync(s.Root)).StatusCode);
    }
    [Fact] public async Task Permission_change_blocks_payload_and_no_eligible_routing_creates_safe_owner_escalation()
    {
        var clock = new BriefingCadenceFixture.Clock(); using var f = new TestWebApplicationFactory(clock); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f);
        await s.Save(h, BriefingCadenceFixture.Settings with { AbsenceStartUtc = clock.Now.AddDays(-1), AbsenceEndUtc = clock.Now.AddDays(2), DelegateUserId = s.Delegate, FallbackUserId = s.Fallback });
        await s.Schedule(f, clock.Now); await s.RunJobs(f);
        await f.SeedAsync(async db => { foreach (var m in await db.CompanyMemberships.IgnoreQueryFilters().Where(x => x.UserId == s.Delegate || x.UserId == s.Fallback).ToArrayAsync()) m.UpdateRole(CompanyMembershipRole.SupportSupervisor); });
        await s.Dispatch(f); await s.Dispatch(f);
        await f.SeedAsync(async db => { Assert.Equal("failed", (await db.BriefingCadenceDeliveries.IgnoreQueryFilters().SingleAsync()).Status); var n = Assert.Single(await db.CompanyNotifications.IgnoreQueryFilters().Where(x => x.Title == "Briefing absence routing needs attention").ToArrayAsync()); Assert.Equal(s.Owner, n.UserId); Assert.DoesNotContain("Confirm recorded", n.Body); });
    }
    [Fact] public async Task Invalid_settings_foreign_recipient_and_cross_company_requests_fail_closed()
    {
        using var f = new TestWebApplicationFactory(new BriefingCadenceFixture.Clock()); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f);
        foreach (var bad in new[] { BriefingCadenceFixture.Settings with { Timezone = "Not/AZone" }, BriefingCadenceFixture.Settings with { DelegateUserId = Guid.NewGuid() }, BriefingCadenceFixture.Settings with { Workdays = [] }, BriefingCadenceFixture.Settings with { FocusAreas = ["secret"] }, BriefingCadenceFixture.Settings with { Schedules = [] }, BriefingCadenceFixture.Settings with { AbsenceStartUtc = DateTime.UtcNow } }) Assert.Equal(HttpStatusCode.BadRequest, (await h.PutAsJsonAsync(s.Root, bad)).StatusCode);
        var foreign = Guid.NewGuid(); Assert.Equal(HttpStatusCode.BadRequest, (await h.GetAsync($"/api/companies/{foreign}/briefings/cadence")).StatusCode);
        h.DefaultRequestHeaders.Remove("X-Company-Id"); h.DefaultRequestHeaders.Add("X-Company-Id", foreign.ToString()); Assert.Equal(HttpStatusCode.Forbidden, (await h.GetAsync($"/api/companies/{foreign}/briefings/cadence")).StatusCode);
        h.DefaultRequestHeaders.Remove("X-Company-Id"); h.DefaultRequestHeaders.Add("X-Company-Id", s.Company.ToString());
        Assert.False((await h.GetFromJsonAsync<BriefingCadenceContext>(s.Root))!.Configured);
    }
    [Fact] public async Task Urgent_is_separate_and_honors_hours_unless_explicitly_enabled_outside()
    {
        var clock = new BriefingCadenceFixture.Clock { Now = new(2026,10,6,22,0,0,DateTimeKind.Utc) }; using var f = new TestWebApplicationFactory(clock); var s = await BriefingCadenceFixture.Seed(f); using var h = s.Client(f);
        await f.SeedAsync(async db => (await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == s.SalesTask)).UpdateStatus(WorkTaskStatus.Blocked));
        var settings = BriefingCadenceFixture.Settings with { Schedules = BriefingCadenceFixture.Settings.Schedules.Select(x => x with { Enabled = false }).ToArray(), UrgentEnabled = true };
        await s.Save(h, settings); Assert.Equal(0, await s.Schedule(f, clock.Now)); await s.Save(h, settings with { UrgentOutsideHours = true }); Assert.Equal(1, await s.Schedule(f, clock.Now)); Assert.Equal(0, await s.Schedule(f, clock.Now));
        await s.RunJobs(f); await s.Dispatch(f); await f.SeedAsync(async db => Assert.Equal(CompanyNotificationType.Escalation, (await db.CompanyNotifications.IgnoreQueryFilters().SingleAsync()).Type));
    }
}
