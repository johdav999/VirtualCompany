using System.Net;
using System.Text.Json;
using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Activity;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ActivityFeedIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public ActivityFeedIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Feed_returns_empty_items_and_null_cursor_when_no_events_exist()
    {
        var seed = await SeedTenantAsync();
        using var client = CreateAuthenticatedClient();

        var result = await client.GetFromJsonAsync<ActivityFeedResponse>($"/api/companies/{seed.CompanyId}/activity-feed?pageSize=20");

        Assert.NotNull(result);
        Assert.Empty(result!.Items);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task Feed_returns_events_in_reverse_chronological_order_with_stable_tie_breaker()
    {
        var seed = await SeedTenantAsync();
        using var client = CreateAuthenticatedClient();
        var occurred = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var older = await PersistAsync(client, seed.CompanyId, seed.AgentId, "task_started", occurred.AddMinutes(-10), "running", "Agent started invoice review", "corr-older");
        var firstTie = await PersistAsync(client, seed.CompanyId, seed.AgentId, "task_progressed", occurred, "running", "Agent reviewed invoice header", "corr-tie-a");
        var secondTie = await PersistAsync(client, seed.CompanyId, seed.AgentId, "task_completed", occurred, "completed", "Agent completed invoice review", "corr-tie-b");

        var result = await client.GetFromJsonAsync<ActivityFeedResponse>($"/api/companies/{seed.CompanyId}/activity-feed?pageSize=10");

        Assert.NotNull(result);
        Assert.Equal(3, result!.Items.Count);
        Assert.Equal(secondTie.EventId, result.Items[0].EventId);
        Assert.Equal(firstTie.EventId, result.Items[1].EventId);
        Assert.Equal(older.EventId, result.Items[2].EventId);
        Assert.Null(result.NextCursor);
    }

    [Fact]
    public async Task Feed_uses_cursor_pagination_without_duplicates()
    {
        var seed = await SeedTenantAsync();
        using var client = CreateAuthenticatedClient();
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var newest = await PersistAsync(client, seed.CompanyId, seed.AgentId, "task_completed", start.AddMinutes(3), "completed", "Newest activity", "corr-newest");
        var middle = await PersistAsync(client, seed.CompanyId, seed.AgentId, "task_progressed", start.AddMinutes(2), "running", "Middle activity", "corr-middle");
        var older = await PersistAsync(client, seed.CompanyId, seed.AgentId, "task_started", start.AddMinutes(1), "running", "Older activity", "corr-older");

        var first = await client.GetFromJsonAsync<ActivityFeedResponse>($"/api/companies/{seed.CompanyId}/activity-feed?pageSize=2");
        Assert.NotNull(first);
        Assert.Equal([newest.EventId, middle.EventId], first!.Items.Select(x => x.EventId).ToArray());
        Assert.False(string.IsNullOrWhiteSpace(first.NextCursor));

        var second = await client.GetFromJsonAsync<ActivityFeedResponse>($"/api/companies/{seed.CompanyId}/activity-feed?pageSize=2&cursor={Uri.EscapeDataString(first.NextCursor!)}");
        Assert.NotNull(second);
        var only = Assert.Single(second!.Items);
        Assert.Equal(older.EventId, only.EventId);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Feed_is_tenant_scoped()
    {
        var seed = await SeedTenantAsync(includeOtherMembership: true);
        using var client = CreateAuthenticatedClient();

        var tenantEvent = await PersistAsync(client, seed.CompanyId, seed.AgentId, "task_completed", DateTime.UtcNow, "completed", "Tenant activity", "corr-tenant");
        await PersistAsync(client, seed.OtherCompanyId, null, "task_completed", DateTime.UtcNow.AddMinutes(1), "completed", "Other tenant activity", "corr-other");

        var result = await client.GetFromJsonAsync<ActivityFeedResponse>($"/api/companies/{seed.CompanyId}/activity-feed?pageSize=10");

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.Equal(tenantEvent.EventId, item.EventId);
        Assert.Equal(seed.CompanyId, item.TenantId);
    }

    [Fact]
    public async Task Unauthorized_tenant_access_returns_forbidden()
    {
        var seed = await SeedTenantAsync(includeOtherMembership: false);
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/activity-feed");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_alias_agent_activity_endpoint_is_tenant_scoped()
    {
        var seed = await SeedTenantAsync();
        using var client = CreateAuthenticatedClient();
        var persisted = await PersistAsync(
            client,
            seed.CompanyId,
            seed.AgentId,
            "task_completed",
            DateTime.UtcNow,
            "completed",
            "Alias activity",
            "corr-alias");

        var result = await client.GetFromJsonAsync<ActivityFeedResponse>($"/api/tenants/{seed.CompanyId}/agent-activity?pageSize=10");

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.Equal(persisted.EventId, item.EventId);
    }

    [Fact]
    public async Task Feed_returns_raw_payload_and_normalized_summary()
    {
        var seed = await SeedTenantAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/activity-events",
            new
            {
                tenantId = seed.CompanyId,
                agentId = seed.AgentId,
                eventType = "task_completed",
                occurredAt = DateTime.UtcNow,
                status = "completed",
                summary = "Legacy summary",
                correlationId = "corr-summary-contract",
                source = new Dictionary<string, object?>
                {
                    ["actor"] = "Ops Agent",
                    ["taskTitle"] = "Invoice Review"
                }
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var normalizedSummary = document.RootElement.GetProperty("normalizedSummary");
        Assert.Equal("Ops Agent completed task Invoice Review", normalizedSummary.GetProperty("summaryText").GetString());
        Assert.Equal("Ops Agent completed task Invoice Review", normalizedSummary.GetProperty("text").GetString());

        var payload = JsonSerializer.Deserialize<ActivityEventResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.Equal("Invoice Review", payload!.RawPayload["taskTitle"].GetString());
        Assert.Equal("Ops Agent completed task Invoice Review", payload.NormalizedSummary.SummaryText);
        Assert.Equal("Ops Agent completed task Invoice Review", payload.NormalizedSummary.Text);
    }

    [Fact]
    public async Task Feed_omits_missing_normalized_summary_fields_without_placeholder_text()
    {
        var seed = await SeedTenantAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/activity-events",
            new
            {
                tenantId = seed.CompanyId,
                agentId = seed.AgentId,
                eventType = "task_completed",
                occurredAt = DateTime.UtcNow,
                status = "completed",
                summary = "Legacy summary",
                correlationId = "corr-summary-missing-fields",
                source = new Dictionary<string, object?>
                {
                    ["taskTitle"] = "Invoice Review",
                    ["actor"] = null
                }
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var normalizedSummary = document.RootElement.GetProperty("normalizedSummary");
        var summaryText = normalizedSummary.GetProperty("summaryText").GetString();

        Assert.False(normalizedSummary.TryGetProperty("actor", out _));
        Assert.Equal("Invoice Review", normalizedSummary.GetProperty("target").GetString());
        Assert.Equal("completed", normalizedSummary.GetProperty("outcome").GetString());
        Assert.Equal("completed task Invoice Review", summaryText);
        Assert.DoesNotContain("null", summaryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("undefined", summaryText, StringComparison.OrdinalIgnoreCase);

        var payload = JsonSerializer.Deserialize<ActivityEventResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(payload);
        Assert.True(payload!.RawPayload.ContainsKey("taskTitle"));
        Assert.Null(payload.NormalizedSummary.Actor);
        Assert.Equal("Invoice Review", payload.NormalizedSummary.Target);
        Assert.Equal("completed task Invoice Review", payload.NormalizedSummary.SummaryText);
    }

    [Fact]
    public async Task Real_time_channel_delivers_new_events_only_to_authorized_tenant_group()
    {
        var seed = await SeedTenantAsync(includeOtherMembership: true);
        using var tenantAConnection = CreateHubConnection(seed.CompanyId);
        using var tenantBConnection = CreateHubConnection(seed.OtherCompanyId);
        using var client = CreateAuthenticatedClient();
        var tenantAReceived = new TaskCompletionSource<ActivityEventResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tenantBReceived = new TaskCompletionSource<ActivityEventResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        tenantAConnection.On<ActivityEventResponse>(ActivityFeedHub.EventName, activityEvent => tenantAReceived.TrySetResult(activityEvent));
        tenantBConnection.On<ActivityEventResponse>(ActivityFeedHub.EventName, activityEvent => tenantBReceived.TrySetResult(activityEvent));

        await tenantAConnection.StartAsync();
        await tenantBConnection.StartAsync();

        var persisted = await PersistAsync(
            client,
            seed.CompanyId,
            seed.AgentId,
            "task_completed",
            DateTime.UtcNow,
            "completed",
            "Agent completed invoice review",
            "corr-realtime");

        var delivered = await WaitAsync(tenantAReceived.Task, TimeSpan.FromSeconds(2));

        Assert.Equal(persisted.EventId, delivered.EventId);
        Assert.Equal(seed.CompanyId, delivered.TenantId);
        Assert.False(tenantBReceived.Task.Wait(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public async Task Unauthorized_hub_subscription_is_rejected()
    {
        var seed = await SeedTenantAsync(includeOtherMembership: false);
        await using var connection = CreateHubConnection(seed.OtherCompanyId);

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }

    private async Task<SeedContext> SeedTenantAsync(bool includeOtherMembership = false)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "activity-owner@example.com", "Activity Owner", "dev-header", "activity-owner"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Activity Tenant"),
                new Company(otherCompanyId, "Other Activity Tenant"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            if (includeOtherMembership)
            {
                dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            }

            dbContext.Agents.Add(new Agent(
                agentId,
                companyId,
                "ops-agent",
                "Ops Agent",
                "Operations Agent",
                "Operations",
                null,
                AgentSeniority.Mid));

            return Task.CompletedTask;
        });

        return new SeedContext(companyId, otherCompanyId, agentId);
    }

    private static async Task<ActivityEventResponse> PersistAsync(
        HttpClient client,
        Guid companyId,
        Guid? agentId,
        string eventType,
        DateTime occurredAt,
        string status,
        string summary,
        string correlationId)
    {
        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{companyId}/activity-events",
            new
            {
                tenantId = companyId,
                agentId,
                eventType,
                occurredAt,
                status,
                summary,
                correlationId,
                source = new Dictionary<string, object?>
                {
                    ["sourceType"] = "task",
                    ["sourceId"] = Guid.NewGuid()
                }
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ActivityEventResponse>();
        Assert.NotNull(payload);
        return payload!;
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "activity-owner");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "activity-owner@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Activity Owner");
        return client;
    }

    private HubConnection CreateHubConnection(Guid tenantId)
    {
        return new HubConnectionBuilder()
            .WithUrl(
                new Uri(_factory.Server.BaseAddress, $"{ActivityFeedHub.Route}?tenantId={tenantId}"),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                    options.Headers.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "activity-owner");
                    options.Headers.Add(DevHeaderAuthenticationDefaults.EmailHeader, "activity-owner@example.com");
                    options.Headers.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Activity Owner");
                })
            .Build();
    }

    private static async Task<T> WaitAsync<T>(Task<T> task, TimeSpan timeout)
    {
        var completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (completed != task)
        {
            throw new TimeoutException("Timed out waiting for activity feed event.");
        }

        return await task;
    }

    private sealed record SeedContext(Guid CompanyId, Guid OtherCompanyId, Guid AgentId);

    private sealed record ActivityFeedResponse(
        IReadOnlyList<ActivityEventResponse> Items,
        string? NextCursor);

    private sealed record ActivityEventResponse(
        Guid EventId,
        Guid TenantId,
        Guid? AgentId,
        string EventType,
        DateTime OccurredAt,
        string Status,
        string Summary,
        string? CorrelationId,
        Dictionary<string, object?> Source,
        Dictionary<string, JsonElement> RawPayload,
        ActivitySummaryResponse NormalizedSummary);

    private sealed record ActivitySummaryResponse(
        string EventType,
        string FormatterKey,
        string? Actor,
        string Action,
        string? Target,
        string? Outcome,
        string SummaryText,
        string Text);
}
