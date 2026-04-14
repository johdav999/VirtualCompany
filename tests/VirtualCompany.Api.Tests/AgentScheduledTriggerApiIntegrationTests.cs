using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class AgentScheduledTriggerApiIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AgentScheduledTriggerApiIntegrationTests(TestWebApplicationFactory factory) =>
        _factory = factory;

    [Fact]
    public async Task Create_valid_trigger_persists_and_returns_next_run()
    {
        var seed = await SeedManagerAndAgentAsync();
        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        var code = $"daily-{Guid.NewGuid():N}";

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/schedule-triggers", new
        {
            name = "Daily standup",
            code,
            cronExpression = "0 9 * * *",
            timeZoneId = "Europe/Stockholm",
            enabled = true,
            metadata = new { source = "test" }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ScheduleTriggerResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.AgentId, payload!.AgentId);
        Assert.Equal(code.ToUpperInvariant(), payload.Code);
        Assert.True(payload.IsEnabled);
        Assert.NotNull(payload.NextRunAt);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var stored = await dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == payload.Id);

        Assert.Equal(seed.CompanyId, stored.CompanyId);
        Assert.Equal(seed.AgentId, stored.AgentId);
        Assert.Equal("0 9 * * *", stored.CronExpression);
        Assert.Equal("Europe/Stockholm", stored.TimeZoneId);
        Assert.True(stored.IsEnabled);
        Assert.NotNull(stored.NextRunUtc);
        Assert.NotNull(stored.EnabledUtc);
    }

    [Theory]
    [InlineData("not a cron", "UTC", "CronExpression")]
    [InlineData("0 9 * * *", "Mars/Olympus", "TimeZoneId")]
    public async Task Create_invalid_schedule_returns_validation_error_and_does_not_persist(
        string cronExpression,
        string timeZoneId,
        string expectedField)
    {
        var seed = await SeedManagerAndAgentAsync();
        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/schedule-triggers", new
        {
            name = "Invalid schedule",
            code = $"invalid-{Guid.NewGuid():N}",
            cronExpression,
            timeZoneId,
            enabled = true
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(problem);
        Assert.Contains(expectedField, problem!.Errors.Keys);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.False(await dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == seed.CompanyId && x.AgentId == seed.AgentId && x.Name == "Invalid schedule"));
    }

    [Fact]
    public async Task Update_enable_disable_and_delete_change_persisted_state()
    {
        var seed = await SeedManagerAgentAndDisabledTriggerAsync();
        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");

        var updateResponse = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/schedule-triggers/{seed.TriggerId}", new
        {
            name = "Updated weekday standup",
            code = $"updated-{Guid.NewGuid():N}",
            cronExpression = "30 10 * * 1-5",
            timeZoneId = "UTC"
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<ScheduleTriggerResponse>();
        Assert.NotNull(updated);
        Assert.False(updated!.IsEnabled);
        Assert.Null(updated.NextRunAt);

        var enableResponse = await client.PostAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/schedule-triggers/{seed.TriggerId}/enable", null);
        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);
        var enabled = await enableResponse.Content.ReadFromJsonAsync<ScheduleTriggerResponse>();
        Assert.NotNull(enabled);
        Assert.True(enabled!.IsEnabled);
        Assert.NotNull(enabled.NextRunAt);
        Assert.NotNull(enabled.EnabledAt);
        Assert.Null(enabled.DisabledAt);

        var disableResponse = await client.PostAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/schedule-triggers/{seed.TriggerId}/disable", null);
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);
        var disabled = await disableResponse.Content.ReadFromJsonAsync<ScheduleTriggerResponse>();
        Assert.NotNull(disabled);
        Assert.False(disabled!.IsEnabled);
        Assert.Null(disabled.NextRunAt);
        Assert.NotNull(disabled.DisabledAt);

        var deleteResponse = await client.DeleteAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/schedule-triggers/{seed.TriggerId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.False(await dbContext.AgentScheduledTriggers
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == seed.TriggerId));
    }

    [Fact]
    public async Task Schedule_trigger_endpoints_are_tenant_scoped()
    {
        var seed = await SeedCrossCompanyTriggerAsync();
        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.OtherCompanyAgentId}/schedule-triggers/{seed.OtherCompanyTriggerId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private async Task<ScheduleSeed> SeedManagerAndAgentAsync()
    {
        var managerUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(managerUserId, "manager@example.com", "Manager", "dev-header", "manager"));
            dbContext.Companies.Add(new Company(companyId, "Company A"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                managerUserId,
                CompanyMembershipRole.Manager,
                CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(
                agentId,
                companyId,
                "operations",
                "Avery Ops",
                "Operations Coordinator",
                "Operations",
                null,
                AgentSeniority.Mid,
                AgentStatus.Active));
            return Task.CompletedTask;
        });

        return new ScheduleSeed(companyId, agentId, Guid.Empty);
    }

    private async Task<ScheduleSeed> SeedManagerAgentAndDisabledTriggerAsync()
    {
        var seed = await SeedManagerAndAgentAsync();
        var triggerId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.AgentScheduledTriggers.Add(new AgentScheduledTrigger(
                triggerId,
                seed.CompanyId,
                seed.AgentId,
                "Weekday standup",
                $"weekday-{Guid.NewGuid():N}",
                "0 9 * * 1-5",
                "UTC",
                null,
                false));
            return Task.CompletedTask;
        });

        return seed with { TriggerId = triggerId };
    }

    private async Task<CrossCompanyScheduleSeed> SeedCrossCompanyTriggerAsync()
    {
        var managerUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();
        var otherCompanyTriggerId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(managerUserId, "manager@example.com", "Manager", "dev-header", "manager"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Company A"),
                new Company(otherCompanyId, "Company B"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                managerUserId,
                CompanyMembershipRole.Manager,
                CompanyMembershipStatus.Active));
            dbContext.Agents.AddRange(
                new Agent(agentId, companyId, "operations", "Company A Ops", "Operations", "Operations", null, AgentSeniority.Mid, AgentStatus.Active),
                new Agent(otherCompanyAgentId, otherCompanyId, "support", "Company B Support", "Support", "Support", null, AgentSeniority.Mid, AgentStatus.Active));
            dbContext.AgentScheduledTriggers.Add(new AgentScheduledTrigger(
                otherCompanyTriggerId,
                otherCompanyId,
                otherCompanyAgentId,
                "Other tenant trigger",
                $"other-{Guid.NewGuid():N}",
                "0 9 * * *",
                "UTC",
                DateTime.UtcNow.AddHours(1),
                true));
            return Task.CompletedTask;
        });

        return new CrossCompanyScheduleSeed(companyId, otherCompanyAgentId, otherCompanyTriggerId);
    }

    private sealed record ScheduleSeed(Guid CompanyId, Guid AgentId, Guid TriggerId);
    private sealed record CrossCompanyScheduleSeed(Guid CompanyId, Guid OtherCompanyAgentId, Guid OtherCompanyTriggerId);

    private sealed class ScheduleTriggerResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public Guid AgentId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string CronExpression { get; set; } = string.Empty;
        public string TimeZoneId { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
        public DateTime? NextRunAt { get; set; }
        public DateTime? EnabledAt { get; set; }
        public DateTime? DisabledAt { get; set; }
        public Dictionary<string, JsonElement> Metadata { get; set; } = [];
    }

    private sealed class ValidationProblemResponse
    {
        public string? Title { get; set; }
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
