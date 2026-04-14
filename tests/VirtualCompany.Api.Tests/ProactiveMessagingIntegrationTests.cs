using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ProactiveMessagingIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ProactiveMessagingIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Level_3_agent_delivers_notification_channel_and_persists_proactive_message()
    {
        var seed = await SeedAsync(AgentAutonomyLevel.Level3);

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/proactive-messages/deliveries", new
        {
            agentId = seed.AgentId,
            sourceEntityType = "alert",
            sourceEntityId = seed.AlertId,
            channel = "notification",
            recipientUserId = seed.UserId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProactiveMessageDeliveryResponse>();

        Assert.NotNull(result);
        Assert.Equal("delivered", result!.Status);
        Assert.NotNull(result.Message);
        Assert.Null(result.PolicyBlock);
        Assert.Equal("notification", result.Message!.Channel);
        Assert.Equal(seed.UserId, result.Message.RecipientUserId);
        Assert.Equal("Founder", result.Message.Recipient);
        Assert.Equal("alert", result.Message.SourceEntityType);
        Assert.Equal(seed.AlertId, result.Message.SourceEntityId);
        Assert.Contains("Proactive update:", result.Message.Subject);
        Assert.Contains("Cash threshold alert", result.Message.Body);
        Assert.NotEqual(default, result.Message.SentAt);
        Assert.NotNull(result.Message.NotificationId);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var stored = await dbContext.ProactiveMessages.AsNoTracking().SingleAsync(x => x.Id == result.Message.Id);
        Assert.Equal(ProactiveMessageChannel.Notification, stored.Channel);
        Assert.Equal(ProactiveMessageDeliveryStatus.Delivered, stored.Status);
        Assert.Equal(seed.AlertId, stored.SourceEntityId);
        Assert.Equal("allow", stored.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal(seed.AgentId, stored.OriginatingAgentId);

        var policyDecision = await dbContext.ProactiveMessagePolicyDecisions.AsNoTracking().SingleAsync(x => x.ProactiveMessageId == stored.Id);
        Assert.Equal(ProactiveMessagePolicyDecisionOutcome.Allowed, policyDecision.Outcome);
        Assert.Equal(seed.AlertId, policyDecision.SourceEntityId);
        Assert.Equal("alert", policyDecision.SourceEntityType.ToStorageValue());
        Assert.Equal(ProactiveMessageChannel.Notification, policyDecision.Channel);
        Assert.Equal(seed.UserId, policyDecision.RecipientUserId);
        Assert.Equal("level_3", policyDecision.EvaluatedAutonomyLevel);
        Assert.Equal("policy_checks_passed", policyDecision.ReasonCode);
        Assert.Equal("allow", policyDecision.PolicyDecision["outcome"]!.GetValue<string>());

        var notification = await dbContext.CompanyNotifications.AsNoTracking().SingleAsync(x => x.Id == stored.NotificationId);
        Assert.Equal(CompanyNotificationType.ProactiveMessage, notification.Type);
        Assert.Equal(seed.AlertId, notification.RelatedEntityId);
        Assert.Equal(seed.UserId, notification.UserId);

        var listed = await client.GetFromJsonAsync<List<ProactiveMessageListItemResponse>>(
            $"/api/companies/{seed.CompanyId}/proactive-messages?sourceEntityType=alert&sourceEntityId={seed.AlertId}");
        var listItem = Assert.Single(listed!);
        Assert.Equal(result.Message.Id, listItem.Id);
        Assert.Equal("notification", listItem.Channel);
    }

    [Fact]
    public async Task Level_0_agent_blocks_inbox_delivery_before_message_persistence_with_policy_reason()
    {
        var seed = await SeedAsync(AgentAutonomyLevel.Level0);

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/proactive-messages/deliveries", new
        {
            agentId = seed.AgentId,
            sourceEntityType = "proactive_task",
            sourceEntityId = seed.TaskId,
            channel = "inbox",
            recipientUserId = seed.UserId
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProactiveMessageDeliveryResponse>();

        Assert.NotNull(result);
        Assert.Equal("blocked", result!.Status);
        Assert.Null(result.Message);
        Assert.NotNull(result.PolicyBlock);
        Assert.Equal("policy_denied", result.PolicyBlock!.Code);
        Assert.Contains(PolicyDecisionReasonCodes.AutonomyLevelBlocksAction, result.PolicyBlock.ReasonCodes);
        Assert.Equal("deny", result.PolicyBlock.PolicyDecision.Outcome);
        Assert.Equal("level_0", result.PolicyBlock.PolicyDecision.EvaluatedAutonomyLevel);
        Assert.Equal("This proactive message was blocked by policy and was not delivered.", result.PolicyBlock.UserFacingMessage);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        Assert.False(await dbContext.ProactiveMessages.AnyAsync(x => x.CompanyId == seed.CompanyId && x.SourceEntityId == seed.TaskId));
        Assert.False(await dbContext.CompanyNotifications.AnyAsync(x => x.CompanyId == seed.CompanyId && x.RelatedEntityId == seed.TaskId));

        var policyDecision = await dbContext.ProactiveMessagePolicyDecisions.AsNoTracking().SingleAsync(x => x.CompanyId == seed.CompanyId && x.SourceEntityId == seed.TaskId);
        Assert.Null(policyDecision.ProactiveMessageId);
        Assert.Equal(ProactiveMessagePolicyDecisionOutcome.Blocked, policyDecision.Outcome);
        Assert.Equal(seed.TaskId, policyDecision.SourceEntityId);
        Assert.Equal("proactive_task", policyDecision.SourceEntityType.ToStorageValue());
        Assert.Equal(ProactiveMessageChannel.Inbox, policyDecision.Channel);
        Assert.Equal(seed.UserId, policyDecision.RecipientUserId);
        Assert.Equal("level_0", policyDecision.EvaluatedAutonomyLevel);
        Assert.Equal(PolicyDecisionReasonCodes.AutonomyLevelBlocksAction, policyDecision.ReasonCode);
        Assert.Equal("deny", policyDecision.PolicyDecision["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task Level_3_agent_delivers_inbox_channel_for_escalation_without_notification_record()
    {
        var seed = await SeedAsync(AgentAutonomyLevel.Level3);

        using var client = CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/proactive-messages/deliveries", new
        {
            agentId = seed.AgentId,
            sourceEntityType = "escalation",
            sourceEntityId = seed.EscalationId,
            channel = "inbox",
            recipientUserId = seed.UserId
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProactiveMessageDeliveryResponse>();

        Assert.NotNull(result);
        Assert.Equal("delivered", result!.Status);
        Assert.NotNull(result.Message);
        Assert.Equal("inbox", result.Message!.Channel);
        Assert.Equal(seed.EscalationId, result.Message.SourceEntityId);
        Assert.Null(result.Message.NotificationId);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var stored = await dbContext.ProactiveMessages.AsNoTracking().SingleAsync(x => x.Id == result.Message.Id);
        Assert.Equal(ProactiveMessageChannel.Inbox, stored.Channel);
        Assert.Null(stored.NotificationId);
        Assert.Equal("escalation", stored.SourceEntityType.ToStorageValue());
    }

    private async Task<SeededProactiveMessaging> SeedAsync(AgentAutonomyLevel autonomyLevel)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var alertId = Guid.NewGuid();
        var escalationId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.Add(new Company(companyId, "Company A"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(
                agentId,
                companyId,
                "ops",
                "Nora Ops",
                "Operations Lead",
                "Operations",
                null,
                AgentSeniority.Senior,
                AgentStatus.Active,
                autonomyLevel: autonomyLevel,
                tools: Payload(("allowed", new JsonArray(JsonValue.Create("proactive_messaging")))),
                scopes: Payload(("execute", new JsonArray(JsonValue.Create("proactive_delivery")))),
                thresholds: Payload(("delivery", new JsonObject { ["proactiveMessagesPerHour"] = 100 })),
                escalationRules: Payload(("escalateTo", JsonValue.Create("owner")))));
            dbContext.WorkTasks.Add(new WorkTask(
                taskId,
                companyId,
                "proactive",
                "Review proactive cash task",
                "Cash forecast needs a proactive review.",
                WorkTaskPriority.High,
                agentId,
                null,
                "agent",
                agentId,
                sourceType: "proactive_task",
                originatingAgentId: agentId,
                creationReason: "Detected cash variance."));
            dbContext.Alerts.Add(new Alert(
                alertId,
                companyId,
                AlertType.Risk,
                AlertSeverity.High,
                "Cash threshold alert",
                "Cash threshold alert requires attention.",
                Payload(("signal", JsonValue.Create("cash"))),
                $"corr-{alertId:N}",
                $"fp-{alertId:N}",
                agentId));
            dbContext.Escalations.Add(new Escalation(
                escalationId,
                companyId,
                Guid.NewGuid(),
                alertId,
                "alert",
                1,
                "Alert breached escalation threshold.",
                DateTime.UtcNow,
                $"corr-{escalationId:N}",
                0));
            return Task.CompletedTask;
        });

        return new SeededProactiveMessaging(companyId, userId, agentId, taskId, alertId, escalationId);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Founder");
        return client;
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in properties)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }

    private sealed record SeededProactiveMessaging(Guid CompanyId, Guid UserId, Guid AgentId, Guid TaskId, Guid AlertId, Guid EscalationId);

    private sealed class ProactiveMessageDeliveryResponse
    {
        public string Status { get; set; } = string.Empty;
        public ProactiveMessageListItemResponse? Message { get; set; }
        public ProactivePolicyBlockResponse? PolicyBlock { get; set; }
    }

    private sealed class ProactiveMessageListItemResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string Channel { get; set; } = string.Empty;
        public Guid RecipientUserId { get; set; }
        public string Recipient { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public string SourceEntityType { get; set; } = string.Empty;
        public Guid SourceEntityId { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
        public Guid? NotificationId { get; set; }
    }

    private sealed class ProactivePolicyBlockResponse
    {
        public string Code { get; set; } = string.Empty;
        public string UserFacingMessage { get; set; } = string.Empty;
        public List<string> ReasonCodes { get; set; } = [];
        public string RationaleSummary { get; set; } = string.Empty;
        public PolicyDecisionResponse PolicyDecision { get; set; } = new();
    }

    private sealed class PolicyDecisionResponse
    {
        public string Outcome { get; set; } = string.Empty;
        public string EvaluatedAutonomyLevel { get; set; } = string.Empty;
        public List<string> ReasonCodes { get; set; } = [];
        public Dictionary<string, JsonElement> Metadata { get; set; } = [];
    }
}
