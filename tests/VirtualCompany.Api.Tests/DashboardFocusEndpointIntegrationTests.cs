using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class DashboardFocusEndpointIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public DashboardFocusEndpointIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Get_focus_returns_normalized_cross_domain_items()
    {
        var seed = await SeedCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/dashboard/focus?companyId={seed.CompanyId:D}&userId={seed.UserId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<FocusItemResponse>>();

        Assert.NotNull(items);
        Assert.InRange(items!.Count, 3, 5);
        Assert.Equal(items.OrderByDescending(item => item.PriorityScore).Select(item => item.Id), items.Select(item => item.Id));
        Assert.All(items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id));
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.DoesNotContain(seed.HiddenPeerTaskTitle, item.Title, StringComparison.Ordinal);
            Assert.DoesNotContain(seed.HiddenOtherTenantAlertTitle, item.Title, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.False(string.IsNullOrWhiteSpace(item.ActionType));
            Assert.False(string.IsNullOrWhiteSpace(item.NavigationTarget));
            Assert.InRange(item.PriorityScore, 0, 100);
        });

        var sources = items.Select(item => item.SourceType).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("approval", sources);
        Assert.Contains("task", sources);
        Assert.Contains("anomaly", sources);
        Assert.Contains(items, item => item.Title == seed.ApprovalTitle && item.NavigationTarget == $"/approvals?companyId={seed.CompanyId:D}&approvalId={seed.ApprovalId:D}");
        Assert.Contains(items, item => item.Title == seed.TaskTitle && item.NavigationTarget == $"/tasks?companyId={seed.CompanyId:D}&taskId={seed.TaskId:D}");
        Assert.Contains(items, item => item.Title == seed.AnomalyTitle && item.NavigationTarget == $"/finance/anomalies/{seed.AnomalyAlertId:D}?companyId={seed.CompanyId:D}");
        Assert.Contains(items, item => item.Title == seed.FinanceAlertTitle && item.NavigationTarget == $"/finance/alerts/{seed.FinanceAlertId:D}?companyId={seed.CompanyId:D}");
        Assert.All(items, item =>
        {
            Assert.Matches("^/", item.NavigationTarget);
        });
        Assert.Contains("finance_alert", sources);
    }

    [Fact]
    public async Task Get_focus_rejects_mismatched_user_context()
    {
        var seed = await SeedCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/dashboard/focus?companyId={seed.CompanyId:D}&userId={Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_focus_uses_authenticated_user_context_when_user_query_is_omitted()
    {
        var seed = await SeedCompanyAsync();

        using var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/dashboard/focus?companyId={seed.CompanyId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<FocusItemResponse>>();
        Assert.NotNull(items);
        Assert.NotEmpty(items!);
    }

    [Fact]
    public async Task Get_focus_serializes_priority_scores_as_in_range_integers_and_preserves_domain_titles()
    {
        var seed = await SeedCompanyAsync();

        using var client = CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/dashboard/focus?companyId={seed.CompanyId:D}&userId={seed.UserId:D}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.InRange(document.RootElement.GetArrayLength(), 3, 5);

        var titles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document.RootElement.EnumerateArray())
        {
            Assert.True(item.TryGetProperty("id", out var id) && !string.IsNullOrWhiteSpace(id.GetString()));
            Assert.True(item.TryGetProperty("title", out var title) && !string.IsNullOrWhiteSpace(title.GetString()));
            Assert.True(item.TryGetProperty("description", out var description) && !string.IsNullOrWhiteSpace(description.GetString()));
            Assert.True(item.TryGetProperty("actionType", out var actionType) && !string.IsNullOrWhiteSpace(actionType.GetString()));
            Assert.True(item.TryGetProperty("navigationTarget", out var navigationTarget) && !string.IsNullOrWhiteSpace(navigationTarget.GetString()));
            Assert.True(item.TryGetProperty("priorityScore", out var priorityScore));
            Assert.Equal(JsonValueKind.Number, priorityScore.ValueKind);
            Assert.True(priorityScore.TryGetInt32(out var normalizedScore));
            Assert.InRange(normalizedScore, 0, 100);

            titles.Add(title.GetString()!);
        }

        Assert.Contains(seed.ApprovalTitle, titles);
        Assert.Contains(seed.TaskTitle, titles);
        Assert.Contains(seed.AnomalyTitle, titles);
        Assert.Contains(seed.FinanceAlertTitle, titles);
        Assert.DoesNotContain(seed.HiddenOtherTenantAlertTitle, titles);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Founder");
        return client;
    }

    private async Task<SeedResult> SeedCompanyAsync()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var peerUserId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherCompanyAlertId = Guid.NewGuid();
        const string hiddenPeerTaskTitle = "Peer-only blocked task";
        const string hiddenOtherTenantAlertTitle = "Other tenant cash alert";
        var taskId = Guid.NewGuid();

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Users.Add(new User(peerUserId, "peer@example.com", "Peer", "dev-header", "peer"));
            dbContext.Companies.Add(new Company(companyId, "Focus Company"));
            dbContext.Companies.Add(new Company(otherCompanyId, "Other Focus Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, peerUserId, CompanyMembershipRole.Member, CompanyMembershipStatus.Active));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "operations", "Ops Lead", "Operations", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));

            var task = new WorkTask(
                taskId,
                companyId,
                "orchestration",
                "Resolve blocked vendor payout",
                "Payment batch is blocked pending a decision.",
                WorkTaskPriority.Critical,
                agentId,
                null,
                "user",
                userId);
            task.SetDueDate(DateTime.UtcNow.AddHours(-2));
            task.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: "Vendor payout is waiting on a release decision.");
            dbContext.WorkTasks.Add(task);

            var peerTask = new WorkTask(
                Guid.NewGuid(),
                companyId,
                "orchestration",
                hiddenPeerTaskTitle,
                "This task belongs to another user in the same tenant.",
                WorkTaskPriority.Critical,
                agentId,
                null,
                "user",
                peerUserId);
            peerTask.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: "This task should stay hidden from the founder focus feed.");
            dbContext.WorkTasks.Add(peerTask);

            var approval = ApprovalRequest.CreateForTarget(
                approvalId,
                companyId,
                ApprovalTargetEntityType.Task,
                taskId,
                "user",
                userId,
                "threshold",
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>(),
                null,
                userId,
                []);
            dbContext.ApprovalRequests.Add(approval);

            dbContext.Alerts.Add(new Alert(
                anomalyAlertId,
                companyId,
                AlertType.Anomaly,
                AlertSeverity.High,
                "Investigate unusual spend spike",
                "A recent transaction deviated from the expected spend profile.",
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                {
                    ["transactionId"] = System.Text.Json.Nodes.JsonValue.Create(Guid.NewGuid())
                },
                "corr-anomaly",
                "fingerprint-anomaly"));

            dbContext.Alerts.Add(new Alert(
                financeAlertId,
                companyId,
                AlertType.Risk,
                AlertSeverity.Critical,
                "Cash runway dropped below threshold",
                "Finance policy raised a low-cash alert that needs follow-up.",
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                {
                    ["policy"] = System.Text.Json.Nodes.JsonValue.Create("cash_runway")
                },
                "corr-risk",
                "fingerprint-risk"));

            dbContext.Alerts.Add(new Alert(
                otherCompanyAlertId,
                otherCompanyId,
                AlertType.Risk,
                AlertSeverity.Critical,
                hiddenOtherTenantAlertTitle,
                "This alert belongs to a different tenant and must not leak into the requested feed.",
                new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                {
                    ["policy"] = System.Text.Json.Nodes.JsonValue.Create("cash_runway")
                },
                "corr-risk-other",
                "fingerprint-risk-other"));

            await dbContext.SaveChangesAsync();

            var approvalEntry = await dbContext.ApprovalRequests.Include(request => request.Steps).SingleAsync(request => request.CompanyId == companyId);
            var step = Assert.Single(approvalEntry.Steps);
            Assert.Equal(ApprovalStepApproverType.User, step.ApproverType);
        });

        return new SeedResult(
            companyId,
            userId,
            taskId,
            approvalId,
            anomalyAlertId,
            financeAlertId,
            "Approval required for task",
            "Resolve blocked vendor payout",
            "Investigate unusual spend spike",
            "Cash runway dropped below threshold",
            hiddenPeerTaskTitle,
            hiddenOtherTenantAlertTitle);
    }

    private sealed record SeedResult(
        Guid CompanyId,
        Guid UserId,
        Guid TaskId,
        Guid ApprovalId,
        Guid AnomalyAlertId,
        Guid FinanceAlertId,
        string ApprovalTitle,
        string TaskTitle,
        string AnomalyTitle,
        string FinanceAlertTitle,
        string HiddenPeerTaskTitle,
        string HiddenOtherTenantAlertTitle);

    private sealed class FocusItemResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public int PriorityScore { get; set; }
        public string NavigationTarget { get; set; } = string.Empty;
        public string? SourceType { get; set; }
    }
}
