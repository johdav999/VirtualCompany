using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
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
    public async Task Overview_does_not_leave_an_approved_payment_proposal_task_in_progress()
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
        Assert.DoesNotContain(finance.InProgress, task => task.Id == taskId);
        var completedTask = Assert.Single(finance.Completed, task => task.Id == taskId);
        Assert.Equal(WorkTaskStatus.Completed.ToStorageValue(), completedTask.Status);
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
