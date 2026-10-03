using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentStaffOverviewIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Overview_groups_existing_tasks_by_governed_stage_and_links_pending_approval()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient("owner", "owner@staff.example");

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/executive-cockpit/agent-staff?year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var overview = await response.Content.ReadFromJsonAsync<AgentStaffOverviewDto>();
        Assert.NotNull(overview);
        Assert.Equal(seed.CompanyId, overview!.CompanyId);
        Assert.Equal("Staff Overview Company", overview.CompanyName);
        Assert.Equal(1, overview.StageCounts.Planned);
        Assert.Equal(1, overview.StageCounts.InProgress);
        Assert.Equal(1, overview.StageCounts.AwaitingHumanApproval);
        Assert.Equal(1, overview.StageCounts.Completed);

        var finance = Assert.Single(overview.Agents, agent => agent.AgentId == seed.FinanceAgentId);
        Assert.Single(finance.Planned);
        Assert.Single(finance.InProgress);
        var approvalTask = Assert.Single(finance.AwaitingHumanApproval);
        Assert.Equal(seed.ApprovalId, approvalTask.ApprovalId);
        Assert.Contains($"approvalId={seed.ApprovalId:D}", approvalTask.ApprovalRoute, StringComparison.Ordinal);
        Assert.Single(finance.Completed);
        Assert.DoesNotContain(overview.Agents, agent => agent.AgentId == seed.OtherCompanyAgentId);
        Assert.Contains(overview.AttentionItems, item => item.Key == "approvals");
    }

    [Fact]
    public async Task Overview_can_return_all_lane_tasks_for_inline_expansion()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(dbContext =>
        {
            for (var index = 1; index <= 4; index++)
            {
                dbContext.WorkTasks.Add(new WorkTask(
                    Guid.NewGuid(),
                    seed.CompanyId,
                    "finance_review",
                    $"Additional planned task {index}",
                    null,
                    WorkTaskPriority.Normal,
                    seed.FinanceAgentId,
                    null,
                    "user",
                    null,
                    status: WorkTaskStatus.New));
            }

            return Task.CompletedTask;
        });
        using var client = CreateAuthenticatedClient("owner", "owner@staff.example");
        var period = $"year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}";

        var preview = await client.GetFromJsonAsync<AgentStaffOverviewDto>(
            $"/api/companies/{seed.CompanyId:D}/executive-cockpit/agent-staff?{period}");
        var expanded = await client.GetFromJsonAsync<AgentStaffOverviewDto>(
            $"/api/companies/{seed.CompanyId:D}/executive-cockpit/agent-staff?{period}&includeAllTasks=true");

        Assert.NotNull(preview);
        Assert.NotNull(expanded);
        var previewFinance = Assert.Single(preview!.Agents, agent => agent.AgentId == seed.FinanceAgentId);
        var expandedFinance = Assert.Single(expanded!.Agents, agent => agent.AgentId == seed.FinanceAgentId);
        Assert.Equal(2, previewFinance.Planned.Count);
        Assert.Equal(expandedFinance.StageCounts.Planned, expandedFinance.Planned.Count);
        Assert.True(expandedFinance.Planned.Count > previewFinance.Planned.Count);
    }

    [Fact]
    public async Task Overview_projects_unassigned_department_work_to_the_active_department_agent()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkTasks.Add(new WorkTask(
                Guid.NewGuid(), seed.CompanyId, "finance.supplier_invoice_payment_proposal", "Review unassigned payment", null,
                WorkTaskPriority.High, null, null, "system", null, status: WorkTaskStatus.InProgress));
            var supportCase = new SupportCase(
                Guid.NewGuid(), seed.CompanyId, "SUP-STAFF-001", "Answer customer question", "A customer needs a response.", "email");
            supportCase.SetStatus(SupportCaseStatuses.Triaged);
            dbContext.SupportCases.Add(supportCase);
            return Task.CompletedTask;
        });
        using var client = CreateAuthenticatedClient("owner", "owner@staff.example");

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/executive-cockpit/agent-staff?year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var overview = await response.Content.ReadFromJsonAsync<AgentStaffOverviewDto>();
        Assert.NotNull(overview);
        var finance = Assert.Single(overview!.Agents, agent => agent.AgentId == seed.FinanceAgentId);
        Assert.Contains(finance.InProgress, item => item.Title == "Review unassigned payment");
        var support = Assert.Single(overview.Agents, agent => agent.AgentId == seed.SupportAgentId);
        var supportItem = Assert.Single(support.InProgress, item => item.Title == "Answer customer question");
        Assert.Contains($"/support/cases/{supportItem.Id:D}", supportItem.Route, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview_places_an_in_progress_task_with_a_pending_approval_in_the_human_approval_stage()
    {
        var seed = await SeedAsync();
        var taskId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkTasks.Add(new WorkTask(
                taskId,
                seed.CompanyId,
                "finance.supplier_invoice_payment_proposal",
                "Approve payment proposal for OpenAI",
                "Review the payment proposal before export.",
                WorkTaskPriority.High,
                seed.FinanceAgentId,
                null,
                "system",
                null,
                status: WorkTaskStatus.InProgress));
            dbContext.ApprovalRequests.Add(ApprovalRequest.CreateForTarget(
                approvalId,
                seed.CompanyId,
                ApprovalTargetEntityType.Task,
                taskId,
                "system",
                Guid.NewGuid(),
                "supplier_invoice_payment_proposal",
                new Dictionary<string, JsonNode?> { ["reason"] = JsonValue.Create("Human approval is pending") },
                "finance_approver",
                null,
                []));
            return Task.CompletedTask;
        });
        using var client = CreateAuthenticatedClient("owner", "owner@staff.example");

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/executive-cockpit/agent-staff?year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var overview = await response.Content.ReadFromJsonAsync<AgentStaffOverviewDto>();
        Assert.NotNull(overview);
        var finance = Assert.Single(overview!.Agents, agent => agent.AgentId == seed.FinanceAgentId);
        Assert.DoesNotContain(finance.InProgress, task => task.Id == taskId);
        var approvalTask = Assert.Single(finance.AwaitingHumanApproval, task => task.Id == taskId);
        Assert.Equal(WorkTaskStatus.AwaitingApproval.ToStorageValue(), approvalTask.Status);
        Assert.Equal(approvalId, approvalTask.ApprovalId);
        Assert.Contains($"approvalId={approvalId:D}", approvalTask.ApprovalRoute, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview_does_not_infer_task_completion_from_payment_proposal_approval()
    {
        var seed = await SeedAsync();
        var taskId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.WorkTasks.Add(new WorkTask(
                taskId,
                seed.CompanyId,
                "finance.supplier_invoice_payment_proposal",
                "Approve payment proposal for OpenAI",
                "Review the payment proposal before export.",
                WorkTaskPriority.High,
                seed.FinanceAgentId,
                null,
                "system",
                null,
                status: WorkTaskStatus.InProgress));
            var approval = ApprovalRequest.CreateForTarget(
                approvalId,
                seed.CompanyId,
                ApprovalTargetEntityType.Task,
                taskId,
                "system",
                Guid.NewGuid(),
                "supplier_invoice_payment_proposal",
                new Dictionary<string, JsonNode?> { ["reason"] = JsonValue.Create("Human approval is required") },
                "finance_approver",
                null,
                []);
            approval.ApproveCurrentStep(approval.CurrentActionableStep!.Id, Guid.NewGuid(), "Approved.");
            dbContext.ApprovalRequests.Add(approval);
            return Task.CompletedTask;
        });
        using var client = CreateAuthenticatedClient("owner", "owner@staff.example");

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/executive-cockpit/agent-staff?year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var overview = await response.Content.ReadFromJsonAsync<AgentStaffOverviewDto>();
        Assert.NotNull(overview);
        var finance = Assert.Single(overview!.Agents, agent => agent.AgentId == seed.FinanceAgentId);
        var currentTask = Assert.Single(finance.InProgress, task => task.Id == taskId);
        Assert.Equal(WorkTaskStatus.InProgress.ToStorageValue(), currentTask.Status);
        Assert.DoesNotContain(finance.Completed, task => task.Id == taskId);
    }

    [Fact]
    public async Task Overview_rejects_a_company_without_an_active_membership()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient("owner", "owner@staff.example");

        var response = await client.GetAsync(
            $"/api/companies/{seed.OtherCompanyId:D}/executive-cockpit/agent-staff?year={DateTime.UtcNow.Year}&month={DateTime.UtcNow.Month}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Overview_rejects_an_invalid_reporting_period()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient("owner", "owner@staff.example");

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId:D}/executive-cockpit/agent-staff?year=2026&month=13");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Staff Owner");
        return client;
    }

    [Fact]
    public async Task Board_maps_all_seven_states_retains_shared_identity_and_never_infers_outcome_completion()
    {
        var seed = await SeedAsync(); Guid human = default;
        await _factory.SeedAsync(async db => human = await db.Users.Select(x => x.Id).SingleAsync());
        var fixture = await AgentWorkLifecycleFixture.SeedAsync(_factory, seed.CompanyId, human);
        using var client = CreateAuthenticatedClient("owner", "owner@staff.example");
        var board = (await client.GetFromJsonAsync<AgentWorkBoardDto>($"/api/companies/{seed.CompanyId}/agent-work"))!;
        Assert.All(AgentWorkStates.All, state => Assert.True(board.StateCounts[state] > 0, state));
        var shared = Assert.Single(board.Items, x => x.Id == fixture.SharedId);
        Assert.Equal(AgentWorkStates.Active, shared.State); Assert.Equal(2, shared.Agents.Count);
        Assert.DoesNotContain(board.Items, x => x.Id == fixture.SharedTaskId);
        Assert.Contains(shared.Outputs, x => x.Summary.Contains("Draft only"));
        var draft = Assert.Single(board.Items, x => x.Title == "P10 in_progress review");
        Assert.Equal(AgentWorkStates.Active,draft.State); Assert.Single(draft.Outputs); Assert.Null(draft.CompletedUtc);
        var detail = (await client.GetFromJsonAsync<AgentWorkItemDto>($"/api/companies/{seed.CompanyId}/agent-work/initiative/{fixture.SharedId}"))!;
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(shared), System.Text.Json.JsonSerializer.Serialize(detail));
        var worker = (await client.GetFromJsonAsync<AgentWorkItemDto>($"/api/companies/{seed.CompanyId}/agent-work/task/{fixture.SharedTaskId}"))!;
        Assert.Equal(AgentWorkStates.Completed,worker.State);
        Assert.Contains(worker.RelatedRecords,x=>x.Label=="Company outcome: P10 shared renewal review" && x.Route.Contains(fixture.SharedId.ToString()));
    }

    [Fact]
    public async Task Board_detail_dependencies_filtered_pages_and_Work_use_the_same_durable_task()
    {
        var seed = await SeedAsync(); Guid human = default;
        await _factory.SeedAsync(async db => human = await db.Users.Select(x => x.Id).SingleAsync());
        var fixture = await AgentWorkLifecycleFixture.SeedAsync(_factory, seed.CompanyId, human);
        using var client = CreateAuthenticatedClient("owner", "owner@staff.example");
        var blocked = (await client.GetFromJsonAsync<AgentWorkItemDto>($"/api/companies/{seed.CompanyId}/agent-work/initiative/{fixture.BlockedId}"))!;
        Assert.Contains("P10 shared renewal review", blocked.Dependency); Assert.Contains(blocked.RelatedRecords, x => x.Route.Contains(fixture.SharedId.ToString()));
        var first = (await client.GetFromJsonAsync<AgentWorkBoardDto>($"/api/companies/{seed.CompanyId}/agent-work?objective=P10&agentId={fixture.AgentId}&take=2"))!;
        var second = (await client.GetFromJsonAsync<AgentWorkBoardDto>($"/api/companies/{seed.CompanyId}/agent-work?objective=P10&agentId={fixture.AgentId}&take=2&skip=2"))!;
        Assert.True(first.HasNext); Assert.Equal(2, first.Items.Count); Assert.Empty(first.Items.Select(x => x.Id).Intersect(second.Items.Select(x => x.Id)));
        Assert.Equal(first.Total, second.Total); Assert.All(first.Items, x => Assert.Contains(x.Agents, a => a.Id == fixture.AgentId));
        var completed = (await client.GetFromJsonAsync<AgentWorkItemDto>($"/api/companies/{seed.CompanyId}/agent-work/task/{fixture.CompletedTaskId}"))!;
        var work = await client.GetFromJsonAsync<JsonObject>($"/api/companies/{seed.CompanyId}/tasks/{fixture.CompletedTaskId}");
        Assert.Equal(completed.Id.ToString(), work!["id"]!.GetValue<string>()); Assert.Equal(completed.SourceState, work["status"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/companies/{seed.CompanyId}/agent-work/task/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/companies/{seed.CompanyId}/agent-work?take=101")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/agent-work")).StatusCode);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Responsibility_scope_removes_hidden_tasks_agents_counts_detail_and_legacy_summaries(bool perState)
    {
        var seed = await SeedAsync(); var manager = Guid.NewGuid(); var membership = Guid.NewGuid(); var visible = Guid.NewGuid(); var hidden = Guid.NewGuid();
        await _factory.SeedAsync(db =>
        {
            db.Users.Add(new User(manager,"scoped@staff.example","Sales only","dev-header","scoped"));
            db.CompanyMemberships.Add(new CompanyMembership(membership,seed.CompanyId,manager,CompanyMembershipRole.Manager,CompanyMembershipStatus.Active));
            db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(),seed.CompanyId,ResponsibilityArea.Sales,ResponsibilityAssignmentKind.Primary,membership,null,AgentAutonomyLevel.Level1,null,null));
            db.WorkTasks.AddRange(new WorkTask(visible,seed.CompanyId,"sales_review","Visible sales",null,WorkTaskPriority.Normal,null,null,"user",manager),
                new WorkTask(hidden,seed.CompanyId,"finance_review","Secret finance",null,WorkTaskPriority.Normal,null,visible,"user",manager));
            return Task.CompletedTask;
        });
        using var client = CreateAuthenticatedClient("scoped","scoped@staff.example");
        var board = (await client.GetFromJsonAsync<AgentWorkBoardDto>($"/api/companies/{seed.CompanyId}/agent-work?perState={perState}"))!;
        Assert.Equal(1,board.Total); Assert.Equal(visible,Assert.Single(board.Items).Id); Assert.Empty(board.Agents);
        Assert.DoesNotContain("finance",board.Responsibilities); Assert.Equal(1,board.StateCounts.Values.Sum());
        foreach (var path in new[] { $"agent-work/task/{hidden}", $"tasks/{hidden}" })
            Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/companies/{seed.CompanyId}/{path}")).StatusCode);
        var work = (await client.GetFromJsonAsync<JsonObject>($"/api/companies/{seed.CompanyId}/tasks/{visible}"))!;
        Assert.DoesNotContain("Secret finance",work.ToJsonString()); Assert.DoesNotContain(hidden.ToString(),work.ToJsonString());
        var summary = (await client.GetFromJsonAsync<AgentStaffOverviewDto>($"/api/companies/{seed.CompanyId}/executive-cockpit/agent-staff"))!;
        Assert.Empty(summary.Agents); Assert.False(summary.Finance.HasData);
        Assert.Equal(HttpStatusCode.NotFound,(await client.PatchAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks/{hidden}/status",new {status="completed"})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/tasks",new {type="finance_review",title="Forbidden Finance write",priority="normal"})).StatusCode);
        await _factory.SeedAsync(async db=> { Assert.Equal(WorkTaskStatus.New,(await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x=>x.Id==hidden)).Status);Assert.False(await db.WorkTasks.IgnoreQueryFilters().AnyAsync(x=>x.Title=="Forbidden Finance write")); });
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Partial_source_window_is_explicit_and_known_older_work_can_still_be_opened(bool perState)
    {
        var seed = await SeedAsync(); var older = Guid.NewGuid();
        await _factory.SeedAsync(db =>
        {
            var row = new WorkTask(older,seed.CompanyId,"finance_review","Older retained work",null,WorkTaskPriority.Normal,seed.FinanceAgentId,null,"system",null);
            db.WorkTasks.Add(row); db.Entry(row).Property(x=>x.UpdatedUtc).CurrentValue=DateTime.UtcNow.AddDays(-30);
            for(var i=0;i<2001;i++) db.WorkTasks.Add(new WorkTask(Guid.NewGuid(),seed.CompanyId,"finance_review","Recent retained work",null,WorkTaskPriority.Normal,seed.FinanceAgentId,null,"system",null));
            return Task.CompletedTask;
        });
        using var client=CreateAuthenticatedClient("owner","owner@staff.example");
        var board=(await client.GetFromJsonAsync<AgentWorkBoardDto>($"/api/companies/{seed.CompanyId}/agent-work?take=100&perState={perState}"))!;
        Assert.True(board.IsPartial); Assert.Equal(2000,board.Total); Assert.Equal(100,board.Items.Count); Assert.Contains(board.Diagnostics,x=>x.Contains("window"));
        var detail=(await client.GetFromJsonAsync<AgentWorkItemDto>($"/api/companies/{seed.CompanyId}/agent-work/task/{older}"))!;
        Assert.Equal(older,detail.Id); Assert.Contains(detail.Diagnostics,x=>x.Contains("older than one day"));
    }

    [Fact]
    public async Task Business_case_states_follow_the_owning_record_instead_of_assuming_agent_execution()
    {
        var seed=await SeedAsync();var ids=new Dictionary<string,Guid>();
        await _factory.SeedAsync(db=> { foreach(var status in new[] {SupportCaseStatuses.New,SupportCaseStatuses.WaitingForCustomer,SupportCaseStatuses.WaitingInternal,SupportCaseStatuses.AwaitingApproval,SupportCaseStatuses.Resolved,SupportCaseStatuses.Reopened})
            { var row=new SupportCase(Guid.NewGuid(),seed.CompanyId,"P10-"+status,"Case "+status,null,"manual"); if(status==SupportCaseStatuses.Reopened)row.SetStatus(SupportCaseStatuses.Resolved); if(status!=SupportCaseStatuses.New)row.SetStatus(status); db.SupportCases.Add(row); ids[status]=row.Id; }return Task.CompletedTask; });
        using var client=CreateAuthenticatedClient("owner","owner@staff.example");
        var board=(await client.GetFromJsonAsync<AgentWorkBoardDto>($"/api/companies/{seed.CompanyId}/agent-work?responsibility=support"))!;
        Assert.Equal(AgentWorkStates.Planned,Assert.Single(board.Items,x=>x.Id==ids[SupportCaseStatuses.New]).State);
        Assert.Equal(AgentWorkStates.Completed,Assert.Single(board.Items,x=>x.Id==ids[SupportCaseStatuses.Resolved]).State);
        Assert.Equal(AgentWorkStates.AwaitingApproval,Assert.Single(board.Items,x=>x.Id==ids[SupportCaseStatuses.AwaitingApproval]).State);
        Assert.Equal(AgentWorkStates.Blocked,Assert.Single(board.Items,x=>x.Id==ids[SupportCaseStatuses.WaitingForCustomer]).State);
        Assert.Contains("internal specialist",Assert.Single(board.Items,x=>x.Id==ids[SupportCaseStatuses.WaitingInternal]).Dependency);
        var reopened=Assert.Single(board.Items,x=>x.Id==ids[SupportCaseStatuses.Reopened]);
        Assert.Equal(AgentWorkStates.Active,reopened.State); Assert.Null(reopened.CompletedUtc);
    }

    [Fact]
    public async Task Scoped_owner_can_read_unlinked_outcome_without_inference_of_company_dependency()
    {
        var seed=await SeedAsync();var manager=Guid.NewGuid();var membership=Guid.NewGuid();var agent=Guid.NewGuid();
        var visible=Guid.NewGuid();var hidden=Guid.NewGuid();var task=Guid.NewGuid();
        await _factory.SeedAsync(db=>
        {
            db.Users.Add(new User(manager,"planner@staff.example","Sales planner","dev-header","planner"));
            db.CompanyMemberships.Add(new CompanyMembership(membership,seed.CompanyId,manager,CompanyMembershipRole.Manager,CompanyMembershipStatus.Active));
            db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(),seed.CompanyId,ResponsibilityArea.Sales,ResponsibilityAssignmentKind.Primary,membership,null,AgentAutonomyLevel.Level1,null,null));
            db.Agents.Add(new Agent(agent,seed.CompanyId,"sales","Sales planner agent","Planner","Sales",null,AgentSeniority.Senior,AgentStatus.Active));
            var cycle=new OperatingCycle(Guid.NewGuid(),seed.CompanyId,"manual",null,agent,"test","scoped-planning",1);
            var plan=new OperatingPlan(Guid.NewGuid(),seed.CompanyId,cycle.Id,1,"Scoped plan","Retained evidence");
            var goal=new CompanyGoal(Guid.NewGuid(),seed.CompanyId,"Sales goal","Retained outcome",CompanyGoalPriority.Normal,DateTime.UtcNow,DateTime.UtcNow.AddDays(7),ownerUserId:manager);
            db.AddRange(cycle,plan,goal);
            db.WorkTasks.Add(new WorkTask(task,seed.CompanyId,"manual","Own manual task",null,WorkTaskPriority.Normal,null,null,"user",manager));
            var outcome=new OperatingInitiative(visible,seed.CompanyId,plan.Id,goal.Id,"Sales planned outcome","Reviewed terms",CompanyGoalPriority.Normal,"Signed terms",agent,null,null);
            var companyDependency=new OperatingInitiative(hidden,seed.CompanyId,plan.Id,goal.Id,"Private company dependency","Company evidence",CompanyGoalPriority.Normal,"Evidence",null,null,null);
            companyDependency.LinkWork(task,null); outcome.Approve(); outcome.Block(); db.AddRange(outcome,companyDependency);
            db.OperatingPlanDependencies.Add(new OperatingPlanDependency(Guid.NewGuid(),seed.CompanyId,plan.Id,visible,hidden));
            return Task.CompletedTask;
        });
        using var client=CreateAuthenticatedClient("planner","planner@staff.example");
        var board=(await client.GetFromJsonAsync<AgentWorkBoardDto>($"/api/companies/{seed.CompanyId}/agent-work"))!;
        Assert.Contains(board.Items,x=>x.Id==visible);Assert.DoesNotContain(board.Items,x=>x.Id==hidden);
        var detail=(await client.GetFromJsonAsync<AgentWorkItemDto>($"/api/companies/{seed.CompanyId}/agent-work/initiative/{visible}"))!;
        Assert.Equal(AgentWorkStates.Blocked,detail.State);Assert.Contains("unavailable in this access scope",detail.Dependency);
        var serialized=System.Text.Json.JsonSerializer.Serialize(detail);
        Assert.DoesNotContain("Private company dependency",serialized);Assert.DoesNotContain(hidden.ToString(),serialized);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/api/companies/{seed.CompanyId}/agent-work/initiative/{hidden}")).StatusCode);
    }

    private async Task<StaffSeed> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var financeAgentId = Guid.NewGuid();
        var supportAgentId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();
        var approvalTaskId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "owner@staff.example", "Staff Owner", "dev-header", "owner"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Staff Overview Company"),
                new Company(otherCompanyId, "Other Staff Company"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.AddRange(
                new Agent(financeAgentId, companyId, "finance", "Laura", "Finance Manager", "Finance", null, AgentSeniority.Senior, AgentStatus.Active),
                new Agent(supportAgentId, companyId, "support", "Ben", "Support Manager", "Support", null, AgentSeniority.Senior, AgentStatus.Active),
                new Agent(otherCompanyAgentId, otherCompanyId, "sales", "Other", "Sales Manager", "Sales", null, AgentSeniority.Senior, AgentStatus.Active));

            var planned = new WorkTask(
                Guid.NewGuid(), companyId, "finance_review", "Review cash plan", null, WorkTaskPriority.High,
                financeAgentId, null, "user", userId, status: WorkTaskStatus.New);
            var inProgress = new WorkTask(
                Guid.NewGuid(), companyId, "finance_work", "Reconcile bank transactions", null, WorkTaskPriority.High,
                financeAgentId, null, "user", userId, status: WorkTaskStatus.InProgress);
            var awaitingApproval = new WorkTask(
                approvalTaskId, companyId, "finance_approval", "Approve supplier payment", null, WorkTaskPriority.Critical,
                financeAgentId, null, "user", userId, status: WorkTaskStatus.AwaitingApproval);
            var completed = new WorkTask(
                Guid.NewGuid(), companyId, "finance_close", "Close monthly report", null, WorkTaskPriority.Normal,
                financeAgentId, null, "user", userId);
            completed.UpdateStatus(WorkTaskStatus.Completed);
            var otherCompanyTask = new WorkTask(
                Guid.NewGuid(), otherCompanyId, "sales", "Other tenant task", null, WorkTaskPriority.High,
                otherCompanyAgentId, null, "user", userId, status: WorkTaskStatus.InProgress);
            dbContext.WorkTasks.AddRange(planned, inProgress, awaitingApproval, completed, otherCompanyTask);
            dbContext.ApprovalRequests.Add(ApprovalRequest.CreateForTarget(
                approvalId,
                companyId,
                ApprovalTargetEntityType.Task,
                approvalTaskId,
                "user",
                userId,
                "task_review",
                new Dictionary<string, JsonNode?> { ["reason"] = JsonValue.Create("Human review required") },
                "owner",
                null,
                []));
            return Task.CompletedTask;
        });

        return new StaffSeed(companyId, otherCompanyId, financeAgentId, supportAgentId, otherCompanyAgentId, approvalId);
    }

    private sealed record StaffSeed(
        Guid CompanyId,
        Guid OtherCompanyId,
        Guid FinanceAgentId,
        Guid SupportAgentId,
        Guid OtherCompanyAgentId,
        Guid ApprovalId);
}
