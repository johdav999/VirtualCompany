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
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class AgentManagementIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AgentManagementIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Template_catalog_returns_seeded_roles_with_prefill_defaults_for_active_company()
    {
        var ids = await SeedMembershipAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var templates = await client.GetFromJsonAsync<List<AgentTemplateResponse>>($"/api/companies/{ids.CompanyId}/agents/templates");

        Assert.NotNull(templates);
        Assert.NotEmpty(templates!);

        foreach (var templateId in RequiredTemplateIds)
        {
            var template = Assert.Single(templates, x => string.Equals(x.TemplateId, templateId, StringComparison.OrdinalIgnoreCase));
            Assert.True(template.TemplateVersion > 0);
            Assert.False(string.IsNullOrWhiteSpace(template.RoleName));
            Assert.False(string.IsNullOrWhiteSpace(template.Department));
            Assert.False(string.IsNullOrWhiteSpace(template.PersonaSummary));
            Assert.False(string.IsNullOrWhiteSpace(template.DefaultSeniority));
            Assert.NotEmpty(template.Personality);
            Assert.NotEmpty(template.Objectives);
            Assert.NotEmpty(template.Kpis);
            Assert.NotEmpty(template.Tools);
            Assert.NotEmpty(template.Scopes);
            Assert.NotEmpty(template.Thresholds);
            Assert.NotEmpty(template.EscalationRules);
        }
    }

    [Fact]
    public async Task Agent_template_seed_is_idempotent_and_keeps_single_row_per_template()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var seeder = scope.ServiceProvider.GetRequiredService<AgentTemplateSeeder>();

        await dbContext.Database.EnsureCreatedAsync();
        await seeder.SeedAsync();

        var baseline = await dbContext.AgentTemplates
            .AsNoTracking()
            .OrderBy(x => x.TemplateId)
            .Select(x => new SeededTemplateSnapshot(x.Id, x.TemplateId))
            .ToListAsync();

        await seeder.SeedAsync();

        var afterRerun = await dbContext.AgentTemplates
            .AsNoTracking()
            .OrderBy(x => x.TemplateId)
            .Select(x => new SeededTemplateSnapshot(x.Id, x.TemplateId))
            .ToListAsync();

        Assert.Equal(baseline, afterRerun);
        Assert.Equal(RequiredTemplateIds.Length, afterRerun.Count(x => RequiredTemplateIds.Contains(x.TemplateId, StringComparer.OrdinalIgnoreCase)));
        Assert.All(RequiredTemplateIds, templateId => Assert.Single(afterRerun, x => string.Equals(x.TemplateId, templateId, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Hiring_from_template_creates_company_owned_active_agent_with_copied_defaults_and_overrides()
    {
        var ids = await SeedMembershipAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var response = await client.PostAsJsonAsync($"/api/companies/{ids.CompanyId}/agents/from-template", new
        {
            templateId = "finance",
            displayName = "Nora Ledger",
            avatarUrl = "https://cdn.example.com/avatars/nora.png",
            department = "Strategic Finance",
            roleName = "Head of Finance",
            personality = "Sharp, calm, and relentless about runway discipline.",
            seniority = "lead"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HireAgentResponse>();
        Assert.NotNull(payload);
        Assert.Equal("active", payload!.Agent.Status);
        Assert.Equal("lead", payload.Agent.Seniority);
        Assert.Equal("Nora Ledger", payload.Agent.DisplayName);
        Assert.Equal("Strategic Finance", payload.Agent.Department);
        Assert.Equal("Head of Finance", payload.Agent.RoleName);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Auth.ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(ids.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var agent = await dbContext.Agents.AsNoTracking().SingleAsync(x => x.Id == payload.Agent.Id);
        var template = await dbContext.AgentTemplates.AsNoTracking().SingleAsync(x => x.TemplateId == "finance");

        Assert.Equal(ids.CompanyId, agent.CompanyId);
        Assert.Equal("finance", agent.TemplateId);
        Assert.Equal(AgentStatus.Active, agent.Status);
        Assert.Equal(AgentSeniority.Lead, agent.Seniority);
        Assert.Equal("Nora Ledger", agent.DisplayName);
        Assert.Equal("Strategic Finance", agent.Department);
        Assert.Equal("Head of Finance", agent.RoleName);
        Assert.Equal("Sharp, calm, and relentless about runway discipline.", agent.Personality["summary"]!.GetValue<string>());
        Assert.NotEmpty(agent.Objectives);
        Assert.NotEmpty(agent.Kpis);
        Assert.NotEmpty(agent.Tools);
        Assert.NotEmpty(agent.Scopes);
        Assert.NotEmpty(agent.Thresholds);
        Assert.NotEmpty(agent.EscalationRules);
        Assert.Equal(template.Objectives["primary"]!.ToJsonString(), agent.Objectives["primary"]!.ToJsonString());
        Assert.True(agent.CreatedUtc <= agent.UpdatedUtc);
    }

    [Fact]
    public async Task Hiring_from_template_uses_template_defaults_when_optional_customizations_are_omitted()
    {
        var ids = await SeedMembershipAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var response = await client.PostAsJsonAsync($"/api/companies/{ids.CompanyId}/agents/from-template", new
        {
            templateId = "support",
            displayName = "Casey Support"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HireAgentResponse>();
        Assert.NotNull(payload);
        Assert.Equal("active", payload!.Agent.Status);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Auth.ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(ids.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var agent = await dbContext.Agents.AsNoTracking().SingleAsync(x => x.Id == payload.Agent.Id);
        var template = await dbContext.AgentTemplates.AsNoTracking().SingleAsync(x => x.TemplateId == "support");

        Assert.Equal(ids.CompanyId, agent.CompanyId);
        Assert.Equal(template.TemplateId, agent.TemplateId);
        Assert.Equal(template.Department, agent.Department);
        Assert.Equal(template.RoleName, agent.RoleName);
        Assert.Equal(template.AvatarUrl, agent.AvatarUrl);
        Assert.Equal(template.DefaultSeniority, agent.Seniority);
        Assert.Equal(AgentStatus.Active, agent.Status);
        Assert.Equal(template.Personality["summary"]!.ToJsonString(), agent.Personality["summary"]!.ToJsonString());
        Assert.Equal(template.Objectives["primary"]!.ToJsonString(), agent.Objectives["primary"]!.ToJsonString());
        Assert.Equal("Casey Support", agent.DisplayName);
    }

    [Fact]
    public async Task Hiring_from_template_adds_agent_to_roster_with_active_status_immediately()
    {
        var ids = await SeedMembershipAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var hireResponse = await client.PostAsJsonAsync($"/api/companies/{ids.CompanyId}/agents/from-template", new
        {
            templateId = "support",
            displayName = "Casey Support"
        });

        Assert.Equal(HttpStatusCode.OK, hireResponse.StatusCode);

        var payload = await hireResponse.Content.ReadFromJsonAsync<HireAgentResponse>();
        Assert.NotNull(payload);

        var roster = await client.GetFromJsonAsync<List<CompanyAgentResponse>>($"/api/companies/{ids.CompanyId}/agents");

        Assert.NotNull(roster);
        var hiredAgent = Assert.Single(roster!, x => x.Id == payload!.Agent.Id);
        Assert.Equal(ids.CompanyId, hiredAgent.CompanyId);
        Assert.Equal("Casey Support", hiredAgent.DisplayName);
        Assert.Equal("active", hiredAgent.Status);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Equal(AgentStatus.Active, await dbContext.Agents.Where(x => x.Id == hiredAgent.Id).Select(x => x.Status).SingleAsync());
    }

    [Fact]
    public async Task Roster_is_company_scoped_and_cross_tenant_access_is_blocked()
    {
        var ids = await SeedMembershipsWithOtherCompanyAgentAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var roster = await client.GetFromJsonAsync<List<CompanyAgentResponse>>($"/api/companies/{ids.CompanyId}/agents");

        Assert.NotNull(roster);
        Assert.Single(roster!);
        Assert.Equal(ids.CompanyId, roster[0].CompanyId);
        Assert.Equal("Company A Finance", roster[0].DisplayName);

        var forbiddenResponse = await client.GetAsync($"/api/companies/{ids.OtherCompanyId}/agents");
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        var forbiddenCreateResponse = await client.PostAsJsonAsync($"/api/companies/{ids.OtherCompanyId}/agents/from-template", new
        {
            templateId = "support",
            displayName = "Blocked Agent"
        });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenCreateResponse.StatusCode);

        var invalidTemplateResponse = await client.PostAsJsonAsync($"/api/companies/{ids.CompanyId}/agents/from-template", new
        {
            templateId = "does-not-exist",
            displayName = "Ghost Agent"
        });
        Assert.Equal(HttpStatusCode.NotFound, invalidTemplateResponse.StatusCode);
    }

    [Fact]
    public async Task Creating_from_template_returns_field_level_validation_errors()
    {
        var ids = await SeedMembershipAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var response = await client.PostAsJsonAsync($"/api/companies/{ids.CompanyId}/agents/from-template", new
        {
            templateId = "",
            displayName = "",
            personality = new string('x', 1001),
            seniority = "principal"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(payload);
        Assert.Contains("TemplateId", payload!.Errors.Keys);
        Assert.Contains("DisplayName", payload.Errors.Keys);
        Assert.Contains("Personality", payload.Errors.Keys);
        Assert.Contains("Seniority", payload.Errors.Keys);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private async Task<SeedIds> SeedMembershipAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();

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
            return Task.CompletedTask;
        });

        return new SeedIds(companyId, Guid.Empty);
    }

    private async Task<SeedIds> SeedMembershipsWithOtherCompanyAgentAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Company A"),
                new Company(otherCompanyId, "Company B"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));
            dbContext.Agents.AddRange(
                new Agent(
                    Guid.NewGuid(),
                    companyId,
                    "finance",
                    "Company A Finance",
                    "Finance Manager",
                    "Finance",
                    null,
                    AgentSeniority.Senior,
                    AgentStatus.Active),
                new Agent(
                    Guid.NewGuid(),
                    otherCompanyId,
                    "support",
                    "Company B Support",
                    "Support Lead",
                    "Support",
                    null,
                    AgentSeniority.Lead,
                    AgentStatus.Active));
            return Task.CompletedTask;
        });

        return new SeedIds(companyId, otherCompanyId);
    }

    private static readonly string[] RequiredTemplateIds =
    [
        "finance",
        "sales",
        "marketing",
        "support",
        "operations",
        "executive-assistant"
    ];

    private sealed record SeedIds(Guid CompanyId, Guid OtherCompanyId);
    private sealed record SeededTemplateSnapshot(Guid Id, string TemplateId);

    private sealed class AgentTemplateResponse
    {
        public string TemplateId { get; set; } = string.Empty;
        public int TemplateVersion { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string PersonaSummary { get; set; } = string.Empty;
        public string DefaultSeniority { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
        public Dictionary<string, JsonElement> Personality { get; set; } = [];
        public Dictionary<string, JsonElement> Objectives { get; set; } = [];
        public Dictionary<string, JsonElement> Kpis { get; set; } = [];
        public Dictionary<string, JsonElement> Tools { get; set; } = [];
        public Dictionary<string, JsonElement> Scopes { get; set; } = [];
        public Dictionary<string, JsonElement> Thresholds { get; set; } = [];
        public Dictionary<string, JsonElement> EscalationRules { get; set; } = [];
    }

    private sealed class CompanyAgentResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private sealed class HireAgentResponse
    {
        public CompanyAgentHireSummary Agent { get; set; } = new();
    }

    private sealed class CompanyAgentHireSummary
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Seniority { get; set; } = string.Empty;
    }

    private sealed class ValidationProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
