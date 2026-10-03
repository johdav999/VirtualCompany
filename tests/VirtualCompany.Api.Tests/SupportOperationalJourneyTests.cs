using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Application.Companies;
using VirtualCompany.Infrastructure.Documents;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Support;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Support;
using Microsoft.Extensions.Logging.Abstractions;

namespace VirtualCompany.Api.Tests;

public sealed class SupportOperationalJourneyTests : IDisposable
{
    private readonly TestWebApplicationFactory factory = new();
    private readonly Guid company = Guid.NewGuid(), other = Guid.NewGuid(), person = Guid.NewGuid(), caseId = Guid.NewGuid(), foreignCase = Guid.NewGuid();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private readonly DateTime now = DateTime.UtcNow;
    private HttpClient Client(string subject = "p08-owner")
    {
        var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Company-Id", company.ToString());
        client.DefaultRequestHeaders.Add("X-Dev-Auth-DisplayName", subject == "p08-owner" ? "Support specialist" : "Restricted member");
        client.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", subject); client.DefaultRequestHeaders.Add("X-Dev-Auth-Email", subject + "@example.com");
        return client;
    }
    private Task Seed() => factory.SeedAsync(db => {
        var user = new User(person, "p08-owner@example.com", "Support specialist", "dev-header", "p08-owner");
        var member = new User(Guid.NewGuid(), "p08-member@example.com", "Restricted member", "dev-header", "p08-member");
        db.Users.AddRange(user, member); db.Companies.AddRange(new Company(company, "P08"), new Company(other, "Private"));
        var membership = new CompanyMembership(Guid.NewGuid(), company, person, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active);
        db.CompanyMemberships.AddRange(membership, new CompanyMembership(Guid.NewGuid(), company, member.Id, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
        db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(), company, ResponsibilityArea.CustomerSupport,
            ResponsibilityAssignmentKind.Primary, membership.Id, null, AgentAutonomyLevel.Level1, null, null));
        var waiting = new SupportCase(caseId, company, "P08-WAIT", "Account access", "Please explain account access", "manual", createdUtc: now.AddDays(-10));
        waiting.SetStatus(SupportCaseStatuses.WaitingForCustomer); waiting.SetSla(now.AddMinutes(30), now.AddDays(1));
        var reopened = new SupportCase(Guid.NewGuid(), company, "P08-REOPEN", "Reopened question", null, "manual", createdUtc: now.AddDays(-31));
        reopened.SetStatus(SupportCaseStatuses.Resolved); reopened.SetStatus(SupportCaseStatuses.Reopened); reopened.SetSla(now.AddDays(-3), now.AddDays(-1));
        var missing = new SupportCase(Guid.NewGuid(), company, "P08-MISSING", "Missing target", null, "manual", createdUtc: now.AddDays(-2));
        var resolved = new SupportCase(Guid.NewGuid(), company, "P08-RESOLVED", "Resolved history", null, "manual", createdUtc: now.AddDays(-5));
        resolved.SetStatus(SupportCaseStatuses.Resolved); resolved.SetSla(now.AddDays(-5), now.AddDays(-3)); resolved.MarkSlaState(true, true);
        var privateCase = new SupportCase(foreignCase, other, "PRIVATE", "Private customer", null, "manual", createdUtc: now.AddDays(-4));
        privateCase.SetSla(now.AddDays(-2), now.AddDays(-1));
        db.SupportCases.AddRange(waiting, reopened, missing, resolved, privateCase);
        return Task.CompletedTask;
    });
    [Fact]
    public async Task Reports_include_waiting_reopened_missing_targets_and_reconcile_all_filtered_rows_without_writes()
    {
        await Seed(); using var client = Client();
        var backlog = (await client.GetFromJsonAsync<SupportOperationalReport>("api/support/reports?view=backlog", JsonOptions))!;
        Assert.Equal(3, backlog.Cases.Count); Assert.Equal(1, backlog.AtRisk); Assert.Equal(1, backlog.Breached);
        Assert.Equal(1, backlog.Waiting); Assert.Equal(1, backlog.MissingTargets); Assert.Equal(3, backlog.AgeBuckets.Sum(x => x.Count));
        Assert.Equal(Assert.Single(backlog.Cases, x => x.Case.Id == caseId).Case.FirstResponseDueUtc, Assert.Single(backlog.Cases, x => x.Case.Id == caseId).NextDeadlineUtc);
        var unresolved = (await client.GetFromJsonAsync<SupportOperationalReport>("api/support/reports?view=unresolved", JsonOptions))!;
        Assert.Equal(backlog.Cases.Select(x => x.Case.Id), unresolved.Cases.Select(x => x.Case.Id));
        var sla = (await client.GetFromJsonAsync<SupportOperationalReport>("api/support/reports?view=sla", JsonOptions))!;
        Assert.Equal(2, sla.Cases.Count); Assert.DoesNotContain(sla.Cases, x => x.Case.Status == "resolved");
        var aging = (await client.GetFromJsonAsync<SupportOperationalReport>("api/support/reports?view=aging&ageBucket=30d-plus", JsonOptions))!;
        Assert.Equal("reopened", Assert.Single(aging.Cases).Case.Status);
        Assert.Contains("do not pause", aging.ClockRules);
        Assert.Equal(0, await factory.ExecuteDbContextAsync(db => db.AuditEvents.IgnoreQueryFilters().CountAsync(x => x.CompanyId == company)));
    }
    [Fact]
    public async Task Direct_case_report_knowledge_and_commands_enforce_responsibility_and_company()
    {
        await Seed(); using var member = Client("p08-member"); using var owner = Client();
        foreach (var route in new[] { "api/support/reports", $"api/support/cases/{caseId}", $"api/support/cases/{caseId}/knowledge", "api/support/cases" })
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync(route)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await member.PostAsJsonAsync($"api/support/cases/{caseId}/assign", new AssignSupportCaseRequest(null, person, "Review"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"api/support/cases/{foreignCase}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"api/support/cases/{foreignCase}/knowledge")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsJsonAsync($"api/support/cases/{foreignCase}/assign", new AssignSupportCaseRequest(null, person, "Review"))).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-Company-Id"); owner.DefaultRequestHeaders.Add("X-Company-Id", other.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("api/support/reports")).StatusCode);
    }
    [Fact]
    public async Task Assignment_reason_and_owner_survive_reload_and_report_scope_changes()
    {
        await Seed(); using var client = Client();
        var assigned = await client.PostAsJsonAsync($"api/support/cases/{caseId}/assign", new AssignSupportCaseRequest(null, person, "Specialist to verify account ownership"));
        assigned.EnsureSuccessStatusCode();
        var detail = (await client.GetFromJsonAsync<SupportCaseDetailResponse>($"api/support/cases/{caseId}"))!;
        Assert.Equal(person, detail.AssignedUserId); Assert.Equal("waiting_for_customer", detail.Status);
        Assert.Contains(detail.Events, x => x.Summary.Contains("Specialist to verify account ownership", StringComparison.Ordinal));
        var report = (await client.GetFromJsonAsync<SupportOperationalReport>("api/support/reports?assignedToMe=true", JsonOptions))!;
        Assert.Equal("Support specialist", Assert.Single(report.Cases).Owner);
        Assert.Equal(1, await factory.ExecuteDbContextAsync(db => db.SupportCaseAssignments.IgnoreQueryFilters().CountAsync(x => x.SupportCaseId == caseId && x.Reason == "Specialist to verify account ownership")));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"api/support/cases/{caseId}/assign", new AssignSupportCaseRequest(null, Guid.NewGuid(), "Foreign person"))).StatusCode);
    }
    [Fact]
    public async Task Report_does_not_truncate_to_two_hundred_rows_and_rejects_invalid_views()
    {
        await Seed(); await factory.SeedAsync(db => { for (var i = 0; i < 205; i++) db.SupportCases.Add(new SupportCase(Guid.NewGuid(), company, $"MANY-{i}", "More cases", null, "manual", createdUtc: now.AddDays(-1))); return Task.CompletedTask; });
        using var client = Client(); var report = (await client.GetFromJsonAsync<SupportOperationalReport>("api/support/reports", JsonOptions))!;
        Assert.Equal(208, report.Cases.Count); Assert.Equal(208, report.AgeBuckets.Sum(x => x.Count));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("api/support/reports?view=unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("api/support/reports?ageBucket=unknown")).StatusCode);
    }
    [Fact]
    public async Task Business_calendar_skips_weekend_and_holiday_and_manual_monitor_is_company_scoped()
    {
        await Seed();
        await factory.ExecuteScopeAsync(async scope => {
            scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(company);
            var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var policies = new SupportSlaPolicyService(db, new Audit());
            await policies.SaveCalendarAsync(company, person, new("Europe/Stockholm", new(8,0), new(17,0), [DayOfWeek.Monday,DayOfWeek.Tuesday,DayOfWeek.Wednesday,DayOfWeek.Thursday,DayOfWeek.Friday], [new(2026,10,5)]), default);
            await policies.UpsertAsync(company, person, new(null,"Business target","general_question","normal",120,480,TimeBasis:"business",RiskThresholdMinutes:60), default);
            var result = await policies.ResolveAsync(company,"general_question","normal",null,new(2026,10,2,14,0,0,DateTimeKind.Utc),default);
            Assert.Equal(new DateTime(2026,10,6,7,0,0,DateTimeKind.Utc), result.FirstResponseDueUtc);
            var monitor = new SupportSlaMonitor(db, NullLogger<SupportSlaMonitor>.Instance, policies);
            await monitor.RunForCompanyAsync(company,now,default);
            Assert.False((await db.SupportCases.IgnoreQueryFilters().SingleAsync(x=>x.Id==foreignCase)).IsSlaBreached);
        });
    }
    [Fact]
    public async Task Delivery_rechecks_grounding_does_not_repeat_sent_reply_and_refuses_uncertain_retry()
    {
        await Seed();
        await factory.ExecuteScopeAsync(async scope => {
            scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(company);
            var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var doc = new CompanyKnowledgeDocument(Guid.NewGuid(), company, "Account access guide", CompanyKnowledgeDocumentType.Reference,
                "fixture/access.md",null,"access.md","text/markdown",".md",100,accessScope:new CompanyKnowledgeDocumentAccessScope(company,CompanyKnowledgeDocumentAccessScope.CompanyVisibility));
            doc.MarkScanClean();doc.MarkProcessing();doc.MarkProcessed();doc.MarkIndexed("Account access help",1,1,"fixture","fixture","v1",3,"v1");
            var chunk = new CompanyKnowledgeChunk(Guid.NewGuid(),company,doc.Id,1,0,"Account access help","[1,0,0]","fixture","fixture","v1",3);
            db.CompanyKnowledgeDocuments.Add(doc);db.CompanyKnowledgeChunks.Add(chunk);
            var json = JsonSerializer.Serialize(new[] { new { type = "knowledge_chunk", trusted = true, label = "Account access guide", documentId = doc.Id, entityId = chunk.Id } });
            var draft = new SupportReplyDraft(Guid.NewGuid(),company,caseId,"Please confirm your account reference with the support specialist.","Helpful",.9m,.9m,null,json,null,person);
            draft.Approve(person);db.SupportReplyDrafts.Add(draft);await db.SaveChangesAsync();
            var sourceAccess = new SupportReplySourceAccess(db,new KnowledgeAccessPolicyEvaluator());
            var safety = new DeterministicSupportReplySafetyPolicy(db,sourceAccess);
            var sender = new Sender();var dispatcher = new SupportReplyDeliveryDispatcher(db,sender,new Audit(),TimeProvider.System,safety);
            var message = new SupportReplyDeliveryRequestedMessage(company,caseId,draft.Id,person,false,false,null,"controlled@example.test",null,"Account question","original",null,null,"p08-controlled","p08-controlled");
            await dispatcher.DispatchAsync(message,default);await dispatcher.DispatchAsync(message,default);
            Assert.Equal(1,sender.Calls);Assert.NotNull(draft.SentUtc);
            var second = new SupportReplyDraft(Guid.NewGuid(),company,caseId,"Please confirm your account reference.","Helpful",.9m,.9m,null,json,null,person);
            second.Approve(person);db.SupportReplyDrafts.Add(second);await db.SaveChangesAsync();
            chunk.Deactivate();await db.SaveChangesAsync();
            var filtered = await sourceAccess.FilterAsync(company,json,default);Assert.Equal("[]",filtered);
            await Assert.ThrowsAsync<InvalidOperationException>(()=>dispatcher.DispatchAsync(message with {DraftId=second.Id},default));Assert.Equal(1,sender.Calls);
            second.MarkDeliveryReconciliationRequired("Outcome unknown",DateTime.UtcNow);await db.SaveChangesAsync();
            var error=await Assert.ThrowsAsync<InvalidOperationException>(()=>dispatcher.DispatchAsync(message with {DraftId=second.Id},default));Assert.Contains("Reconcile",error.Message);Assert.Equal(1,sender.Calls);
        });
    }
    private sealed class Sender : ISupportOutboundEmailSender
    {
        public int Calls { get; private set; }
        public Task<SupportOutboundEmailSendResult> SendReplyAsync(SupportOutboundEmailSendRequest request,CancellationToken ct)
        { Calls++;return Task.FromResult(new SupportOutboundEmailSendResult("controlled-test",Guid.NewGuid(),"confirmed-test-message","thread","sent")); }
    }
    private sealed class Audit : IAuditEventWriter { public Task WriteAsync(AuditEventWriteRequest request, CancellationToken ct) => Task.CompletedTask; }
    public void Dispose() => factory.Dispose();
}
