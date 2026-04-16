using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BriefingPreferenceIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BriefingPreferenceIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Authenticated_user_can_save_and_fetch_preferences()
    {
        var seed = await SeedCompanyAsync(CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient();

        var put = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me", new
        {
            deliveryFrequency = "daily",
            includedFocusAreas = new[] { "alerts", "anomalies" },
            priorityThreshold = "high"
        });

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var get = await client.GetAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var preference = await get.Content.ReadFromJsonAsync<BriefingPreferenceResponse>();

        Assert.NotNull(preference);
        Assert.Equal(seed.CompanyId, preference!.CompanyId);
        Assert.Equal(seed.UserId, preference.UserId);
        Assert.Equal("daily", preference.DeliveryFrequency);
        Assert.Equal(["alerts", "anomalies"], preference.IncludedFocusAreas);
        Assert.Equal("high", preference.PriorityThreshold);
    }

    [Fact]
    public async Task Fetch_preferences_returns_effective_tenant_default_when_user_has_no_saved_preference()
    {
        var seed = await SeedCompanyAsync(CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient();

        var defaults = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/tenant-defaults", new
        {
            deliveryFrequency = "weekly",
            includedFocusAreas = new[] { "pending_approvals", "alerts" },
            priorityThreshold = "medium"
        });
        Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);

        var get = await client.GetAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var preference = await get.Content.ReadFromJsonAsync<BriefingPreferenceResponse>();

        Assert.NotNull(preference);
        Assert.Equal(seed.CompanyId, preference!.CompanyId);
        Assert.Equal(seed.UserId, preference.UserId);
        Assert.Equal("weekly", preference.DeliveryFrequency);
        Assert.Equal(["pending_approvals", "alerts"], preference.IncludedFocusAreas);
        Assert.Equal("medium", preference.PriorityThreshold);
        Assert.Equal("tenant_default", preference.Source);
    }

    [Fact]
    public async Task Invalid_focus_area_returns_descriptive_4xx_error()
    {
        var seed = await SeedCompanyAsync(CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me", new
        {
            deliveryFrequency = "daily",
            includedFocusAreas = new[] { "sales_gossip" },
            priorityThreshold = "medium"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.Contains("briefing_preferences.unsupported_focus_area", problem!.Errors.SelectMany(x => x.Value));

        var saved = await client.GetFromJsonAsync<BriefingPreferenceResponse>($"/api/companies/{seed.CompanyId}/briefings/preferences/me");
        Assert.NotNull(saved);
        Assert.Equal("system_default", saved!.Source);
        Assert.DoesNotContain("sales_gossip", saved.IncludedFocusAreas);
    }

    [Fact]
    public async Task Invalid_delivery_frequency_returns_descriptive_4xx_error()
    {
        var seed = await SeedCompanyAsync(CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me", new
        {
            deliveryFrequency = "hourly",
            includedFocusAreas = new[] { "alerts" },
            priorityThreshold = "medium"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.Contains("briefing_preferences.invalid_delivery_frequency", problem!.Errors.SelectMany(x => x.Value));
    }

    [Fact]
    public async Task Enum_style_delivery_frequency_returns_descriptive_4xx_error()
    {
        var seed = await SeedCompanyAsync(CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me", new
        {
            deliveryFrequency = "DailyAndWeekly",
            includedFocusAreas = new[] { "alerts" },
            priorityThreshold = "medium"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.Contains("briefing_preferences.invalid_delivery_frequency", problem!.Errors.SelectMany(x => x.Value));
        Assert.Contains(problem.Errors.SelectMany(x => x.Value), value => value.Contains("daily_and_weekly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Mixed_valid_and_invalid_focus_areas_fail_cleanly()
    {
        var seed = await SeedCompanyAsync(CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me", new
        {
            deliveryFrequency = "daily",
            includedFocusAreas = new[] { "alerts", "ops_rumors", "anomalies" },
            priorityThreshold = "medium"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.Contains("briefing_preferences.unsupported_focus_area", problem!.Errors.SelectMany(x => x.Value));
        Assert.Contains(problem.Errors.SelectMany(x => x.Value), value => value.Contains("ops_rumors", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Invalid_priority_threshold_returns_descriptive_4xx_error()
    {
        var seed = await SeedCompanyAsync(CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me", new
        {
            deliveryFrequency = "daily",
            includedFocusAreas = new[] { "alerts" },
            priorityThreshold = "urgent"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.Contains("briefing_preferences.invalid_priority_threshold", problem!.Errors.SelectMany(x => x.Value));
        Assert.Contains(problem.Errors.SelectMany(x => x.Value), value => value.Contains("informational", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Tenant_defaults_require_admin_authorization()
    {
        var seed = await SeedCompanyAsync(CompanyMembershipRole.Employee);
        using var client = CreateAuthenticatedClient();

        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/tenant-defaults", new
        {
            deliveryFrequency = "weekly",
            includedFocusAreas = new[] { "alerts" },
            priorityThreshold = "critical"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_default_fallback_is_recorded_and_filters_next_generation()
    {
        var seed = await SeedBriefingCompanyAsync();
        using var client = CreateAuthenticatedClient();

        var defaults = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/tenant-defaults", new
        {
            deliveryFrequency = "daily_and_weekly",
            includedFocusAreas = new[] { "alerts" },
            priorityThreshold = "critical"
        });
        Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);

        var generated = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12),
            force = true
        });

        Assert.Equal(HttpStatusCode.OK, generated.StatusCode);
        var result = await generated.Content.ReadFromJsonAsync<BriefingGenerationResponse>();

        Assert.NotNull(result);
        Assert.All(result!.Briefing.StructuredSections, section =>
        {
            Assert.Equal("alerts", section.SectionType);
            Assert.Equal("critical", section.PriorityCategory);
        });
        Assert.Equal("tenant_default", result.Briefing.PreferenceSnapshot["source"].GetString());
        Assert.True(result.Briefing.PreferenceSnapshot["fallbackApplied"].GetBoolean());
    }

    [Fact]
    public async Task User_preference_update_affects_next_generated_briefing()
    {
        var seed = await SeedBriefingCompanyAsync();
        using var client = CreateAuthenticatedClient();

        var firstPreference = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me", new
        {
            deliveryFrequency = "daily_and_weekly",
            includedFocusAreas = new[] { "alerts" },
            priorityThreshold = "critical"
        });
        Assert.Equal(HttpStatusCode.OK, firstPreference.StatusCode);

        var first = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "daily_briefing",
            nowUtc = DateTime.UtcNow.Date.AddDays(1).AddHours(12),
            force = true
        });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var secondPreference = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/preferences/me", new
        {
            deliveryFrequency = "daily_and_weekly",
            includedFocusAreas = new[] { "anomalies" },
            priorityThreshold = "high"
        });
        Assert.Equal(HttpStatusCode.OK, secondPreference.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/briefings/generate", new
        {
            briefingType = "weekly_summary",
            nowUtc = DateTime.UtcNow.Date.AddDays(8).AddHours(12),
            force = true
        });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var result = await second.Content.ReadFromJsonAsync<BriefingGenerationResponse>();

        Assert.NotNull(result);
        Assert.All(result!.Briefing.StructuredSections, section => Assert.Equal("anomalies", section.SectionType));
        Assert.Equal("user", result.Briefing.PreferenceSnapshot["source"].GetString());
    }

    private HttpClient CreateAuthenticatedClient() =>
        _factory.CreateClient().WithDevUser("founder");

    private async Task<CompanySeed> SeedCompanyAsync(CompanyMembershipRole role)
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.Add(new Company(companyId, "Preference Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, role, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new CompanySeed(companyId, userId);
    }

    private async Task<CompanySeed> SeedBriefingCompanyAsync()
    {
        var seed = await SeedCompanyAsync(CompanyMembershipRole.Owner);
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Agents.Add(new Agent(agentId, seed.CompanyId, "ops", "Avery Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));
            var criticalAlert = new Alert(Guid.NewGuid(), seed.CompanyId, AlertType.Risk, AlertSeverity.Critical, "Critical margin risk", "Margin risk needs attention.", [], "pref-critical-alert", $"pref-critical-alert:{seed.CompanyId:N}", AlertStatus.Open);
            dbContext.Alerts.Add(criticalAlert);
            var blockedTask = new WorkTask(Guid.NewGuid(), seed.CompanyId, "briefing", "Blocked vendor renewal", null, WorkTaskPriority.High, agentId, null, "user", seed.UserId);
            blockedTask.UpdateStatus(WorkTaskStatus.Blocked);
            blockedTask.SetDueDate(now.AddDays(-2));
            dbContext.WorkTasks.Add(blockedTask);
            return Task.CompletedTask;
        });

        return seed;
    }

    private sealed record CompanySeed(Guid CompanyId, Guid UserId);

    private sealed class BriefingPreferenceResponse
    {
        public Guid CompanyId { get; set; }
        public Guid UserId { get; set; }
        public string DeliveryFrequency { get; set; } = string.Empty;
        public List<string> IncludedFocusAreas { get; set; } = [];
        public string PriorityThreshold { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    private sealed class BriefingGenerationResponse
    {
        public CompanyBriefingResponse Briefing { get; set; } = new();
    }

    private sealed class CompanyBriefingResponse
    {
        public List<AggregatedBriefingSectionResponse> StructuredSections { get; set; } = [];
        public Dictionary<string, JsonElement> PreferenceSnapshot { get; set; } = [];
    }

    private sealed class AggregatedBriefingSectionResponse
    {
        public string SectionType { get; set; } = string.Empty;
        public string PriorityCategory { get; set; } = string.Empty;
    }

    private sealed class ValidationProblemResponse
    {
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}