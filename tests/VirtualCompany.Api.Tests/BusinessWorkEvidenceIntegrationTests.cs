using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class BusinessWorkEvidenceIntegrationTests
{
    [Fact]
    public async Task Intake_and_operational_bills_share_only_their_retained_same_company_evidence()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var owner = Guid.NewGuid();
        await SeedOwner(factory, company, owner); var fixture = await BusinessEvidenceFixture.SeedAsync(factory, company, owner);
        var intake = Guid.NewGuid(); var taskId = Guid.NewGuid();
        await factory.SeedAsync(db =>
        {
            db.Add(new DetectedBill(intake, company, "Intake supplier", null, "INTAKE-13", DateTime.UtcNow, DateTime.UtcNow.AddDays(7),
                "SEK", 400, 80, null, null, null, null, null, .8m, "high", "valid", "required", true, true, true, "[]", null, null,
                validationStatusPersistedAtUtc: DateTime.UtcNow));
            db.Add(new FinanceBillReviewState(Guid.NewGuid(), company, intake, FinanceBillInboxStatuses.NeedsReview, "Extraction retained for review."));
            db.Add(new WorkTask(taskId, company, "finance_review", "Intake evidence task", null, WorkTaskPriority.Normal, null, null, "user", owner,
                new Dictionary<string, JsonNode?> { ["billId"] = JsonValue.Create(intake) }));
            return Task.CompletedTask;
        });
        using var client = Client(factory);
        var evidence = (await client.GetFromJsonAsync<BusinessWorkEvidenceDto>(Route(company, "bill", intake)))!;
        Assert.Contains(evidence.Work, x => x.Id == taskId);
        Assert.Contains(evidence.Artifacts, x => x.Kind == "bill_intake_review" && x.RecordedState == FinanceBillInboxStatuses.NeedsReview);
        Assert.Contains(evidence.Work.Single(x => x.Id == taskId).RelatedRecords, x => x.Route.StartsWith($"/finance/bill-inbox/{intake}"));
        var operational = (await client.GetFromJsonAsync<BusinessWorkEvidenceDto>(Route(company, "bill", fixture.Records["bill"])))!;
        Assert.DoesNotContain(operational.Work, x => x.Id == taskId);
        Assert.Contains(operational.Work.Single().RelatedRecords, x => x.Route.StartsWith("/finance/supplier-bills/"));
        using var created = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, $"/api/companies/{company}/tasks",
            new { type = "finance_review", title = "Valid intake association", inputPayload = new { billId = intake } }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
    }
    [Theory]
    [InlineData("deal")][InlineData("case")][InlineData("invoice")][InlineData("bill")][InlineData("campaign")][InlineData("brief")]
    public async Task Correct_record_links_to_current_decision_and_refresh_preserves_native_workflow(string kind)
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var owner = Guid.NewGuid();
        await SeedOwner(factory, company, owner); var fixture = await BusinessEvidenceFixture.SeedAsync(factory, company, owner);
        await factory.SeedAsync(async db => { var retained = await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == fixture.Tasks[kind]); Assert.True(new Guid?[] {retained.BusinessDealId, retained.BusinessCaseId, retained.BusinessInvoiceId, retained.BusinessBillId, retained.BusinessCampaignId, retained.BusinessBriefId}.Contains(fixture.Records[kind]), JsonSerializer.Serialize(retained.InputPayload)); });
        using var client = Client(); var path = Route(company, kind, fixture.Records[kind]);
        var before = (await client.GetFromJsonAsync<BusinessWorkEvidenceDto>(path))!;
        Assert.True(before.Work.Any(x => x.Id == fixture.Tasks[kind]), JsonSerializer.Serialize(before));
        var item = Assert.Single(before.Work.Where(x => x.Id == fixture.Tasks[kind]));
        Assert.Contains(item.RelatedRecords, x => x.Route.Contains(fixture.Approvals[kind].ToString()));
        var approval = (await client.GetFromJsonAsync<ApprovalRequestDto>($"/api/companies/{company}/approvals/{fixture.Approvals[kind]}"))!;
        Assert.Equal(approval.RationaleSummary, before.Decisions!.Single(x => x.TaskId == item.Id).Reason);
        using var decision = await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client, $"/api/companies/{company}/approvals/{approval.Id}/decisions",
            new ApprovalDecisionCommand(approval.Id, "request_changes", approval.CurrentStep!.Id, "Clarify the retained proposal.", Guid.NewGuid(), approval.Review!.Token), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(decision.IsSuccessStatusCode, await decision.Content.ReadAsStringAsync());
        var after = (await client.GetFromJsonAsync<BusinessWorkEvidenceDto>(path))!;
        Assert.Equal(AgentWorkStates.Blocked, after.Work.Single(x => x.Id == item.Id).State);
        Assert.Equal(before.Artifacts.Select(x => (x.Id,x.RecordedState,x.ExecutionState)), after.Artifacts.Select(x => (x.Id,x.RecordedState,x.ExecutionState)));
        Assert.False(after.WorkflowMeaning.Contains("sent successfully", StringComparison.OrdinalIgnoreCase));
        Assert.All(after.Work, x => Assert.Contains(company.ToString(), x.DetailRoute));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Route(company,kind,Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Route(Guid.NewGuid(),kind,fixture.Records[kind]))).StatusCode);
        HttpClient Client() { var c = factory.CreateClient(); c.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "p13-owner"); c.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "p13-owner@example.com"); return c; }
    }
    [Fact]
    public async Task Stale_material_and_partial_grounding_are_visible_without_delivery_or_invented_completion()
    {
        using var factory = new TestWebApplicationFactory(); var company=Guid.NewGuid(); var owner=Guid.NewGuid();
        await SeedOwner(factory,company,owner); var f=await BusinessEvidenceFixture.SeedAsync(factory,company,owner);
        using var client=Client(factory); var approval=(await client.GetFromJsonAsync<ApprovalRequestDto>($"/api/companies/{company}/approvals/{f.Approvals["deal"]}"))!;
        await factory.SeedAsync(async db => (await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==f.Tasks["deal"])).InputPayload["proposalVersion"]=JsonValue.Create(2));
        using var decision=await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client,$"/api/companies/{company}/approvals/{approval.Id}/decisions",
            new ApprovalDecisionCommand(approval.Id,"approve",approval.CurrentStep!.Id,null,Guid.NewGuid(),approval.Review!.Token),new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(HttpStatusCode.Conflict,decision.StatusCode);
        var deal=(await client.GetFromJsonAsync<BusinessWorkEvidenceDto>(Route(company,"deal",f.Records["deal"])))!;
        Assert.Equal(AgentWorkStates.Blocked,Assert.Single(deal.Work).State);
        var current=(await client.GetFromJsonAsync<ApprovalRequestDto>($"/api/companies/{company}/approvals/{approval.Id}"))!;
        Assert.Equal("stale",current.Status);
        var support=(await client.GetFromJsonAsync<BusinessWorkEvidenceDto>(Route(company,"case",f.Records["case"])))!;
        Assert.Contains(support.Artifacts.Single().Diagnostics,x=>x.Contains("No source references"));
        Assert.Equal("pending",support.Artifacts.Single().ExecutionState);
    }
    [Fact]
    public async Task Sales_scope_cannot_read_other_areas_or_finance_derived_contributions()
    {
        using var factory = new TestWebApplicationFactory(); var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await SeedOwner(factory,company,owner);var f=await BusinessEvidenceFixture.SeedAsync(factory,company,owner);
        var collaboration=await CollaborationEvidenceFixture.SeedAsync(factory,company,owner);
        await factory.SeedAsync(db=>{var user=Guid.NewGuid();var membership=Guid.NewGuid();db.Users.Add(new User(user,"p13-sales@example.com","Sales reader","dev-header","p13-sales"));
            db.CompanyMemberships.Add(new CompanyMembership(membership,company,user,CompanyMembershipRole.Manager,CompanyMembershipStatus.Active));
            db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(),company,ResponsibilityArea.Sales,ResponsibilityAssignmentKind.Primary,membership,null,AgentAutonomyLevel.Level1,null,null));return Task.CompletedTask;});
        using var client=Client(factory,"p13-sales");
        foreach(var kind in new[]{"case","invoice","bill","campaign","brief"})Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(Route(company,kind,f.Records[kind]))).StatusCode);
        var sales=(await client.GetFromJsonAsync<BusinessWorkEvidenceDto>(Route(company,"deal",f.Records["deal"])))!;
        Assert.DoesNotContain(sales.Work,x=>x.Id==collaboration.RootId);
        Assert.True(sales.Diagnostics.Any(x=>x.Contains("restricted")),JsonSerializer.Serialize(sales));
        Assert.DoesNotContain("P11 margin",JsonSerializer.Serialize(sales));
    }
    [Fact]
    public async Task Failed_delivery_is_retained_and_foreign_association_creation_is_rejected()
    {
        using var factory=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await SeedOwner(factory,company,owner);var f=await BusinessEvidenceFixture.SeedAsync(factory,company,owner);
        await factory.SeedAsync(async db=>{var draft=await db.SupportReplyDrafts.IgnoreQueryFilters().SingleAsync(x=>x.SupportCaseId==f.Records["case"]);draft.MarkSendFailed("Controlled provider failure: no reply delivered.");});
        using var client=Client(factory);var support=(await client.GetFromJsonAsync<BusinessWorkEvidenceDto>(Route(company,"case",f.Records["case"])))!;
        Assert.Contains("Controlled provider failure",support.Artifacts.Single().Diagnostics.Single(x=>x.Contains("Controlled provider failure")));
        Assert.Equal("failed",support.Artifacts.Single().ExecutionState);
        var foreignCompany=Guid.NewGuid();var foreignOwner=Guid.NewGuid();await SeedOwnerWithSubject(factory,foreignCompany,foreignOwner);
        var stage=Guid.NewGuid();var deal=Guid.NewGuid();await factory.SeedAsync(db=>{db.AddRange(new SalesPipelineStage(stage,foreignCompany,"Foreign",1),new Deal(deal,foreignCompany,"Private foreign record",stage,100,"SEK"));return Task.CompletedTask;});
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(Route(company,"deal",deal))).StatusCode);
        using var created=await System.Net.Http.Json.HttpClientJsonExtensions.PostAsJsonAsync(client,$"/api/companies/{company}/tasks",new{type="sales_review",title="Invalid association",inputPayload=new{dealId=deal}},new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(HttpStatusCode.BadRequest,created.StatusCode);
        await factory.SeedAsync(async db=>Assert.False(await db.WorkTasks.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company&&x.Title=="Invalid association")));
    }
    private static Task SeedOwnerWithSubject(TestWebApplicationFactory factory,Guid company,Guid owner)=>factory.SeedAsync(db=>{
        db.AddRange(new User(owner,"p13-foreign@example.com","Foreign","dev-header","p13-foreign"),new Company(company,"Foreign"),new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));return Task.CompletedTask;});
    internal static Task SeedOwner(TestWebApplicationFactory factory,Guid company,Guid owner)=>factory.SeedAsync(db=>{
        db.Users.Add(new User(owner,"p13-owner@example.com","P13 Owner","dev-header","p13-owner"));db.Companies.Add(new Company(company,"P13 Company"));
        db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));return Task.CompletedTask;});
    internal static HttpClient Client(TestWebApplicationFactory factory,string subject="p13-owner") {var c=factory.CreateClient();c.DefaultRequestHeaders.Add("X-Dev-Auth-Subject",subject);c.DefaultRequestHeaders.Add("X-Dev-Auth-Email",subject+"@example.com");return c;}
    private static string Route(Guid company,string kind,Guid id)=>$"/api/companies/{company}/agent-work/business/{kind}/{id}";
}
