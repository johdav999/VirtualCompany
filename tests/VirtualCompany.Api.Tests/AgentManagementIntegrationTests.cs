using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
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
    public async Task Agent_template_catalog_seed_is_idempotent_and_keeps_single_row_per_template()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        await dbContext.Database.EnsureCreatedAsync();

        var baseline = await dbContext.AgentTemplates
            .AsNoTracking()
            .OrderBy(x => x.TemplateId)
            .Select(x => new SeededTemplateSnapshot(x.Id, x.TemplateId))
            .ToListAsync();

        await dbContext.Database.EnsureCreatedAsync();

        var afterSecondInitialization = await dbContext.AgentTemplates
            .AsNoTracking()
            .OrderBy(x => x.TemplateId)
            .Select(x => new SeededTemplateSnapshot(x.Id, x.TemplateId))
            .ToListAsync();

        Assert.Equal(baseline, afterSecondInitialization);
        Assert.Equal(RequiredTemplateIds.Length, afterSecondInitialization.Count(x => RequiredTemplateIds.Contains(x.TemplateId, StringComparer.OrdinalIgnoreCase)));
        Assert.All(RequiredTemplateIds, templateId => Assert.Single(afterSecondInitialization, x => string.Equals(x.TemplateId, templateId, StringComparison.OrdinalIgnoreCase)));
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
        Assert.Equal("https://cdn.example.com/avatars/nora.png", agent.AvatarUrl);
        Assert.Equal("Sharp, calm, and relentless about runway discipline.", agent.Personality["summary"]!.GetValue<string>());
        Assert.NotEmpty(agent.Objectives);
        Assert.NotEmpty(agent.Kpis);
        Assert.NotEmpty(agent.Tools);
        Assert.NotEmpty(agent.Scopes);
        Assert.NotEmpty(agent.Thresholds);
        Assert.NotEmpty(agent.EscalationRules);
        Assert.Equal(template.Objectives["primary"]!.ToJsonString(), agent.Objectives["primary"]!.ToJsonString());
        Assert.True(agent.CreatedUtc <= agent.UpdatedUtc);

        Assert.Equal(AgentAutonomyLevel.Level0, agent.AutonomyLevel);
        var auditEvent = await dbContext.AuditEvents.SingleAsync(x =>
            x.CompanyId == ids.CompanyId &&
            x.Action == "agent.hired" &&
            x.TargetId == payload.Agent.Id.ToString("N"));

        Assert.Equal("user", auditEvent.ActorType);
        Assert.Equal("agent", auditEvent.TargetType);
        Assert.Equal("succeeded", auditEvent.Outcome);
        Assert.Equal("finance", auditEvent.Metadata["templateId"]);
        Assert.Equal("Nora Ledger", auditEvent.Metadata["displayName"]);
        Assert.Equal("active", auditEvent.Metadata["status"]);
        Assert.Equal(agent.AutonomyLevel.ToStorageValue(), auditEvent.Metadata["autonomyLevel"]);
        Assert.Contains("initial operating profile snapshot", auditEvent.RationaleSummary);

        var configuredFields = JsonSerializer.Deserialize<string[]>(auditEvent.Metadata["configuredFields"]!);
        Assert.NotNull(configuredFields);
        Assert.Contains("objectives", configuredFields!);
        Assert.Contains("status", configuredFields);
    }

    [Fact]
    public void Creating_company_agent_from_template_materializes_a_company_owned_configuration_snapshot()
    {
        var personality = Payload(
            ("summary", JsonValue.Create("Systematic, calm, and operationally disciplined.")),
            ("traits", new JsonArray(JsonValue.Create("structured"), JsonValue.Create("reliable"))));
        var objectives = Payload(("primary", new JsonArray(JsonValue.Create("Reduce delivery risk"))));
        var kpis = Payload(("targets", new JsonArray(JsonValue.Create("sla_adherence"), JsonValue.Create("cycle_time"))));
        var tools = Payload(("allowed", new JsonArray(JsonValue.Create("project_management"), JsonValue.Create("docs"))));
        var scopes = Payload(
            ("read", new JsonArray(JsonValue.Create("workflows"), JsonValue.Create("projects"))),
            ("write", new JsonArray(JsonValue.Create("runbooks"), JsonValue.Create("handoff_notes"))));
        var thresholds = Payload(("delivery", new JsonObject { ["blockedDays"] = 2 }));
        var escalationRules = Payload(
            ("critical", new JsonArray(JsonValue.Create("missed_customer_deadline"))),
            ("escalateTo", JsonValue.Create("founder")));

        var template = new AgentTemplate(
            Guid.NewGuid(),
            "operations",
            "Operations Manager",
            "Operations",
            "Execution owner who keeps workflows stable.",
            AgentSeniority.Lead,
            "/avatars/agents/operations-manager.png",
            10,
            true,
            personality,
            objectives,
            kpis,
            tools,
            scopes,
            thresholds,
            escalationRules);

        var agent = template.CreateCompanyAgent(Guid.NewGuid(), "Alex Ops", null, null, null, null);

        template.UpdateDefinition(
            "operations",
            "Operations Director",
            "Strategy",
            "Escalates every blocker immediately.",
            AgentSeniority.Executive,
            "/avatars/agents/updated-operations-manager.png",
            10,
            true,
            Payload(("summary", JsonValue.Create("Escalates every blocker immediately.")), ("traits", new JsonArray(JsonValue.Create("volatile")))),
            Payload(("primary", new JsonArray(JsonValue.Create("Restructure operating model")))),
            Payload(("targets", new JsonArray(JsonValue.Create("board_reporting_cadence")))),
            Payload(("allowed", new JsonArray(JsonValue.Create("spreadsheet")))),
            Payload(("read", new JsonArray(JsonValue.Create("board_packets"))), ("write", new JsonArray(JsonValue.Create("strategy_memos")))),
            Payload(("delivery", new JsonObject { ["blockedDays"] = 1 })),
            Payload(("critical", new JsonArray(JsonValue.Create("board_meeting_change"))), ("escalateTo", JsonValue.Create("ceo"))));

        Assert.Equal("operations", agent.TemplateId);
        Assert.Equal("Alex Ops", agent.DisplayName);
        Assert.Equal("Operations Manager", agent.RoleName);
        Assert.Equal("Operations", agent.Department);
        Assert.Equal("/avatars/agents/operations-manager.png", agent.AvatarUrl);
        Assert.Equal(AgentSeniority.Lead, agent.Seniority);
        Assert.Equal(AgentStatus.Active, agent.Status);
        Assert.Equal(AgentAutonomyLevel.Level0, agent.AutonomyLevel);
        AssertJsonDictionaryEqual(personality, agent.Personality);
        AssertJsonDictionaryEqual(objectives, agent.Objectives);
        AssertJsonDictionaryEqual(kpis, agent.Kpis);
        AssertJsonDictionaryEqual(tools, agent.Tools);
        AssertJsonDictionaryEqual(scopes, agent.Scopes);
        AssertJsonDictionaryEqual(thresholds, agent.Thresholds);
        AssertJsonDictionaryEqual(escalationRules, agent.EscalationRules);
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
        Assert.Equal("level_0", payload.Agent.AutonomyLevel);

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
        Assert.Equal(AgentAutonomyLevel.Level0, agent.AutonomyLevel);
        Assert.Equal(AgentStatus.Active, agent.Status);
        Assert.Equal(template.Personality["summary"]!.ToJsonString(), agent.Personality["summary"]!.ToJsonString());
        Assert.Equal("Casey Support", agent.DisplayName);
        AssertJsonDictionaryEqual(template.Personality, agent.Personality);
        AssertJsonDictionaryEqual(template.Objectives, agent.Objectives);
        AssertJsonDictionaryEqual(template.Kpis, agent.Kpis);
        AssertJsonDictionaryEqual(template.Tools, agent.Tools);
        AssertJsonDictionaryEqual(template.Scopes, agent.Scopes);
        AssertJsonDictionaryEqual(template.Thresholds, agent.Thresholds);
        AssertJsonDictionaryEqual(template.EscalationRules, agent.EscalationRules);
        Assert.NotSame(template.Personality, agent.Personality);
        Assert.NotSame(template.Objectives, agent.Objectives);
        Assert.NotSame(template.Kpis, agent.Kpis);
        Assert.NotSame(template.Tools, agent.Tools);
        Assert.NotSame(template.Scopes, agent.Scopes);
        Assert.NotSame(template.Thresholds, agent.Thresholds);
        Assert.NotSame(template.EscalationRules, agent.EscalationRules);
    }

    [Fact]
    public void Agent_defaults_to_level_0_autonomy_when_unspecified_and_rejects_invalid_values()
    {
        var agent = new Agent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "finance",
            "Nora Ledger",
            "Finance Manager",
            "Finance",
            null,
            AgentSeniority.Senior);

        Assert.Equal(AgentAutonomyLevel.Level0, agent.AutonomyLevel);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Agent(
            Guid.NewGuid(), Guid.NewGuid(), "finance", "Nora Ledger", "Finance Manager", "Finance", null,
            AgentSeniority.Senior, autonomyLevel: (AgentAutonomyLevel)99));
    }

    [Fact]
    public async Task Hiring_from_valid_new_template_record_works_without_role_specific_logic()
    {
        var ids = await SeedMembershipAsync();
        var templateId = $"revops-{Guid.NewGuid():N}";
        var templatePersonality = Payload(
            ("summary", JsonValue.Create("Commercial systems operator who keeps attribution and pipeline hygiene aligned.")),
            ("traits", new JsonArray(JsonValue.Create("systems-minded"), JsonValue.Create("precise"))));
        var templateObjectives = Payload(("primary", new JsonArray(JsonValue.Create("Improve funnel visibility"), JsonValue.Create("Reduce handoff leakage"))));
        var templateKpis = Payload(("targets", new JsonArray(JsonValue.Create("lead_to_sql_latency"), JsonValue.Create("attribution_coverage"))));
        var templateTools = Payload(("allowed", new JsonArray(JsonValue.Create("crm"), JsonValue.Create("bi"), JsonValue.Create("automation"))));
        var templateScopes = Payload(
            ("read", new JsonArray(JsonValue.Create("accounts"), JsonValue.Create("campaigns"), JsonValue.Create("opportunities"))),
            ("write", new JsonArray(JsonValue.Create("routing_rules"), JsonValue.Create("data_quality_backlog"))));
        var templateThresholds = Payload(("ops", new JsonObject { ["syncFailureHours"] = 4 }));
        var templateEscalationRules = Payload(
            ("critical", new JsonArray(JsonValue.Create("attribution_gap_over_15_percent"))),
            ("escalateTo", JsonValue.Create("founder")));

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.AgentTemplates.Add(new AgentTemplate(
                Guid.NewGuid(),
                templateId,
                "Revenue Operations Strategist",
                "Revenue Operations",
                "Commercial systems operator who keeps attribution and pipeline hygiene aligned.",
                AgentSeniority.Lead,
                "/avatars/agents/revenue-operations-strategist.png",
                70,
                true,
                templatePersonality,
                templateObjectives,
                templateKpis,
                templateTools,
                templateScopes,
                templateThresholds,
                templateEscalationRules));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var response = await client.PostAsJsonAsync($"/api/companies/{ids.CompanyId}/agents/from-template", new
        {
            templateId,
            displayName = "Morgan RevOps"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HireAgentResponse>();
        Assert.NotNull(payload);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Auth.ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(ids.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var agent = await dbContext.Agents.AsNoTracking().SingleAsync(x => x.Id == payload!.Agent.Id);
        Assert.Equal(templateId, agent.TemplateId);
        Assert.Equal("Revenue Operations Strategist", agent.RoleName);
        Assert.Equal("Revenue Operations", agent.Department);
        Assert.Equal("/avatars/agents/revenue-operations-strategist.png", agent.AvatarUrl);
        Assert.Equal(AgentSeniority.Lead, agent.Seniority);
        AssertJsonDictionaryEqual(templatePersonality, agent.Personality);
        AssertJsonDictionaryEqual(templateObjectives, agent.Objectives);
        AssertJsonDictionaryEqual(templateKpis, agent.Kpis);
        AssertJsonDictionaryEqual(templateTools, agent.Tools);
        AssertJsonDictionaryEqual(templateScopes, agent.Scopes);
        AssertJsonDictionaryEqual(templateThresholds, agent.Thresholds);
        AssertJsonDictionaryEqual(templateEscalationRules, agent.EscalationRules);
    }

    [Fact]
    public async Task Hiring_from_template_accepts_storage_avatar_references_and_returns_them_in_results()
    {
        var ids = await SeedMembershipAsync();
        const string avatarReference = "avatars/company-123/agent-abc.png";

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var response = await client.PostAsJsonAsync($"/api/companies/{ids.CompanyId}/agents/from-template", new
        {
            templateId = "sales",
            displayName = "Avery Pipeline",
            avatarUrl = avatarReference
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<HireAgentResponse>();
        Assert.NotNull(payload);
        Assert.Equal(avatarReference, payload!.Agent.AvatarUrl);

        var roster = await client.GetFromJsonAsync<List<CompanyAgentResponse>>($"/api/companies/{ids.CompanyId}/agents");
        var rosterAgent = Assert.Single(roster!, x => x.Id == payload.Agent.Id);
        Assert.Equal(avatarReference, rosterAgent.AvatarUrl);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Auth.ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(ids.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var agent = await dbContext.Agents.AsNoTracking().SingleAsync(x => x.Id == payload.Agent.Id);
        Assert.Equal(avatarReference, agent.AvatarUrl);
    }

    [Fact]
    public async Task Creating_from_template_rejects_inline_avatar_payloads_and_malformed_absolute_urls()
    {
        var ids = await SeedMembershipAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var inlineResponse = await client.PostAsJsonAsync($"/api/companies/{ids.CompanyId}/agents/from-template", new
        {
            templateId = "finance",
            displayName = "Inline Avatar",
            avatarUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAUA"
        });

        Assert.Equal(HttpStatusCode.BadRequest, inlineResponse.StatusCode);
        var inlinePayload = await inlineResponse.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(inlinePayload);
        Assert.Contains("AvatarUrl", inlinePayload!.Errors.Keys);

        var malformedUrlResponse = await client.PostAsJsonAsync($"/api/companies/{ids.CompanyId}/agents/from-template", new
        {
            templateId = "finance",
            displayName = "Broken Url",
            avatarUrl = "https//cdn.example.com/avatar.png"
        });

        Assert.Equal(HttpStatusCode.BadRequest, malformedUrlResponse.StatusCode);
        var malformedUrlPayload = await malformedUrlResponse.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(malformedUrlPayload);
        Assert.Contains("AvatarUrl", malformedUrlPayload!.Errors.Keys);
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

    [Fact]
    public void Agent_operating_profile_json_columns_are_mapped_to_jsonb()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var agentEntity = dbContext.Model.FindEntityType(typeof(Agent));
        Assert.NotNull(agentEntity);

        Assert.Equal("jsonb", agentEntity!.FindProperty(nameof(Agent.Objectives))!.GetColumnType());
        Assert.Equal("jsonb", agentEntity.FindProperty(nameof(Agent.Kpis))!.GetColumnType());
        Assert.Equal("jsonb", agentEntity.FindProperty(nameof(Agent.Tools))!.GetColumnType());
        Assert.Equal("jsonb", agentEntity.FindProperty(nameof(Agent.Scopes))!.GetColumnType());
        Assert.Equal("jsonb", agentEntity.FindProperty(nameof(Agent.Thresholds))!.GetColumnType());
        Assert.Equal("jsonb", agentEntity.FindProperty(nameof(Agent.EscalationRules))!.GetColumnType());
        Assert.Equal("jsonb", agentEntity.FindProperty(nameof(Agent.TriggerLogic))!.GetColumnType());
        Assert.Equal("jsonb", agentEntity.FindProperty(nameof(Agent.WorkingHours))!.GetColumnType());

        var templateEntity = dbContext.Model.FindEntityType(typeof(AgentTemplate));
        Assert.NotNull(templateEntity);

        Assert.Equal("jsonb", templateEntity!.FindProperty(nameof(AgentTemplate.Objectives))!.GetColumnType());
        Assert.Equal("jsonb", templateEntity.FindProperty(nameof(AgentTemplate.Kpis))!.GetColumnType());
        Assert.Equal("jsonb", templateEntity.FindProperty(nameof(AgentTemplate.Tools))!.GetColumnType());
        Assert.Equal("jsonb", templateEntity.FindProperty(nameof(AgentTemplate.Scopes))!.GetColumnType());
        Assert.Equal("jsonb", templateEntity.FindProperty(nameof(AgentTemplate.Thresholds))!.GetColumnType());
        Assert.Equal("jsonb", templateEntity.FindProperty(nameof(AgentTemplate.EscalationRules))!.GetColumnType());
    }

    [Fact]
    public async Task Owner_can_get_and_update_agent_operating_profile_with_round_trip_persistence()
    {
        var seed = await SeedMembershipWithExistingAgentAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "agent-profile-update-audit");
        var before = await client.GetFromJsonAsync<AgentOperatingProfileResponse>($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile");

        Assert.NotNull(before);
        Assert.Equal("active", before!.Status);
        Assert.Equal("Finance Manager", before.RoleName);

        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile", new
        {
            status = "archived",
            roleBrief = "Owns exception approvals, escalations, and finance tool access policy.",
            objectives = new { primary = new[] { "Protect margin", "Reduce spend leakage" } },
            kpis = new { targets = new[] { "forecast_accuracy", "approval_latency" } },
            toolPermissions = new { allowed = new[] { "erp", "spreadsheets" }, denied = new[] { "wire_transfer" } },
            dataScopes = new { read = new[] { "finance", "vendors" }, write = new[] { "approval_notes" } },
            approvalThresholds = new { approval = new { expenseUsd = 1200, wireTransferUsd = 0 } },
            escalationRules = new { critical = new[] { "failed_payment" }, escalateTo = "owner" },
            triggerLogic = new { enabled = true, conditions = new[] { new { @event = "invoice_created", source = "erp" } } },
            workingHours = new { timezone = "Europe/Stockholm", windows = new[] { new { day = "monday", start = "09:00", end = "17:00" } } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentOperatingProfileResponse>();
        Assert.NotNull(payload);
        Assert.Equal("archived", payload!.Status);
        Assert.Equal("Owns exception approvals, escalations, and finance tool access policy.", payload.RoleBrief);
        Assert.True(payload.UpdatedUtc > before.UpdatedUtc);
        Assert.False(payload.CanReceiveAssignments);
        Assert.Equal(JsonValueKind.Array, payload.Objectives["primary"].ValueKind);
        Assert.Equal("Europe/Stockholm", payload.WorkingHours["timezone"].GetString());

        var after = await client.GetFromJsonAsync<AgentOperatingProfileResponse>($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile");
        Assert.NotNull(after);
        Assert.Equal("archived", after!.Status);
        Assert.Equal(payload.RoleBrief, after.RoleBrief);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Auth.ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var agent = await dbContext.Agents.AsNoTracking().SingleAsync(x => x.Id == seed.AgentId);
        Assert.Equal(AgentStatus.Archived, agent.Status);
        Assert.Equal("Owns exception approvals, escalations, and finance tool access policy.", agent.RoleBrief);
        Assert.True(agent.UpdatedUtc > seed.OriginalUpdatedUtc);
        Assert.Equal("\"Europe/Stockholm\"", agent.WorkingHours["timezone"]!.ToJsonString());
        Assert.Equal("[\"forecast_accuracy\",\"approval_latency\"]", agent.Kpis["targets"]!.ToJsonString());

        var auditEvent = await dbContext.AuditEvents.SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == "agent.operating_profile.updated" &&
            x.TargetId == seed.AgentId.ToString("N"));

        Assert.Equal("user", auditEvent.ActorType);
        Assert.Equal("agent", auditEvent.TargetType);
        Assert.Equal("succeeded", auditEvent.Outcome);
        Assert.Equal("agent-profile-update-audit", auditEvent.CorrelationId);
        Assert.Equal("Nora Ledger", auditEvent.Metadata["displayName"]);
        Assert.Equal("archived", auditEvent.Metadata["status"]);
        Assert.Equal("10", auditEvent.Metadata["changedFieldCount"]);

        var changedFields = JsonSerializer.Deserialize<string[]>(auditEvent.Metadata["changedFields"]!);
        Assert.NotNull(changedFields);
        Assert.Equal(
            ["objectives", "kpis", "roleBrief", "toolPermissions", "dataScopes", "approvalThresholds", "escalationRules", "triggerLogic", "workingHours", "status"],
            changedFields);

        Assert.Equal("active", auditEvent.Metadata["statusBefore"]);
        Assert.Equal("archived", auditEvent.Metadata["statusAfter"]);
        Assert.Equal("Original finance operating profile.", auditEvent.Metadata["roleBriefBefore"]);
        Assert.Equal("Owns exception approvals, escalations, and finance tool access policy.", auditEvent.Metadata["roleBriefAfter"]);
    }

    [Fact]
    public async Task Runtime_profile_resolution_reads_latest_persisted_profile_after_operating_profile_update()
    {
        var seed = await SeedMembershipWithExistingAgentAsync();

        using var runtimeScope = _factory.Services.CreateScope();
        var runtimeResolver = runtimeScope.ServiceProvider.GetRequiredService<IAgentRuntimeProfileResolver>();

        var before = await runtimeResolver.GetCurrentProfileAsync(seed.CompanyId, seed.AgentId, CancellationToken.None);
        Assert.Equal("active", before.Status);
        Assert.Equal("Original finance operating profile.", before.RoleBrief);
        Assert.Equal("[\"Protect cash flow\"]", before.Objectives["primary"]!.ToJsonString());
        Assert.Equal("[\"erp\"]", before.ToolPermissions["allowed"]!.ToJsonString());
        Assert.Equal("[\"finance\"]", before.DataScopes["read"]!.ToJsonString());

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile", new
        {
            status = "restricted",
            roleBrief = "Escalation-heavy finance profile for exception handling.",
            objectives = new { primary = new[] { "Reduce spend leakage", "Tighten exception approvals" } },
            kpis = new { targets = new[] { "approval_latency", "forecast_accuracy" } },
            toolPermissions = new { allowed = new[] { "erp", "spreadsheets", "approvals_console" }, denied = new[] { "wire_transfer" } },
            dataScopes = new { read = new[] { "finance", "vendors", "contracts" }, write = new[] { "approval_notes", "exception_decisions" } },
            approvalThresholds = new { approval = new { expenseUsd = 2500 } },
            escalationRules = new { critical = new[] { "failed_payment", "cash_runway_under_90_days" }, escalateTo = "owner" },
            triggerLogic = new { enabled = true, conditions = new[] { new { @event = "invoice_created", source = "erp" } } },
            workingHours = new { timezone = "Europe/Stockholm", windows = new[] { new { day = "monday", start = "09:00", end = "17:00" } } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await runtimeResolver.GetCurrentProfileAsync(seed.CompanyId, seed.AgentId, CancellationToken.None);
        Assert.Equal("restricted", after.Status);
        Assert.Equal("Escalation-heavy finance profile for exception handling.", after.RoleBrief);
        Assert.True(after.UpdatedUtc > before.UpdatedUtc);
        Assert.True(after.CanReceiveAssignments);
        Assert.Equal("[\"Reduce spend leakage\",\"Tighten exception approvals\"]", after.Objectives["primary"]!.ToJsonString());
        Assert.Equal("[\"erp\",\"spreadsheets\",\"approvals_console\"]", after.ToolPermissions["allowed"]!.ToJsonString());
        Assert.Equal("[\"wire_transfer\"]", after.ToolPermissions["denied"]!.ToJsonString());
        Assert.Equal("[\"finance\",\"vendors\",\"contracts\"]", after.DataScopes["read"]!.ToJsonString());
        Assert.Equal("[\"approval_notes\",\"exception_decisions\"]", after.DataScopes["write"]!.ToJsonString());
        Assert.Equal("\"Europe/Stockholm\"", after.WorkingHours["timezone"]!.ToJsonString());
    }

    [Fact]
    public async Task Updating_operating_profile_returns_field_level_validation_errors()
    {
        var seed = await SeedMembershipWithExistingAgentAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile", new
        {
            status = "sleeping",
            roleBrief = new string('x', 4001),
            objectives = new { primary = new object[] { "" } },
            kpis = new { targets = new object[] { new { name = "" }, "cycle_time", "cycle_time" } },
            toolPermissions = new { allowed = new object[] { "erp", "", "erp" }, denied = new[] { "erp" } },
            dataScopes = new { read = new[] { "finance", "" } },
            approvalThresholds = new { approval = new { expenseUsd = -1, minAmount = 25, maxAmount = 10, requiresApproval = "yes" } },
            escalationRules = new { critical = new[] { "failed_payment" }, escalateTo = "" },
            triggerLogic = new { enabled = true, conditions = new[] { new { source = "erp" } } },
            workingHours = new { timezone = "", windows = new[] { new { day = "monday", start = "18:00", end = "09:00" } } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemResponse>();

        Assert.NotNull(payload);
        Assert.Contains("Status", payload!.Errors.Keys);
        Assert.Contains("RoleBrief", payload.Errors.Keys);
        Assert.Contains("Objectives.primary[0]", payload.Errors.Keys);
        Assert.Contains("Kpis.targets[0].name", payload.Errors.Keys);
        Assert.Contains("Kpis.targets[2]", payload.Errors.Keys);
        Assert.Contains("ToolPermissions.allowed[1]", payload.Errors.Keys);
        Assert.Contains("ToolPermissions.allowed[2]", payload.Errors.Keys);
        Assert.Contains("ToolPermissions.denied", payload.Errors.Keys);
        Assert.Contains("DataScopes.read[1]", payload.Errors.Keys);
        Assert.Contains("ApprovalThresholds.approval.expenseUsd", payload.Errors.Keys);
        Assert.Contains("ApprovalThresholds.approval.maxAmount", payload.Errors.Keys);
        Assert.Contains("ApprovalThresholds.approval.requiresApproval", payload.Errors.Keys);
        Assert.Contains("EscalationRules.escalateTo", payload.Errors.Keys);
        Assert.Contains("TriggerLogic.conditions[0].event", payload.Errors.Keys);
        Assert.Contains("WorkingHours.timezone", payload.Errors.Keys);
        Assert.Contains("WorkingHours.windows[0].end", payload.Errors.Keys);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Auth.ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var agent = await dbContext.Agents.AsNoTracking().SingleAsync(x => x.Id == seed.AgentId);

        Assert.Equal(seed.OriginalUpdatedUtc, agent.UpdatedUtc);
        Assert.Equal(AgentStatus.Active, agent.Status);
        Assert.False(await dbContext.AuditEvents.AnyAsync(x => x.CompanyId == seed.CompanyId && x.TargetId == seed.AgentId.ToString("N")));
        Assert.Equal("Original finance operating profile.", agent.RoleBrief);
    }

    [Fact]
    public async Task Updating_operating_profile_accepts_paused_status_and_persists_timestamp()
    {
        var seed = await SeedMembershipWithExistingAgentAsync();

        using var client = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile", new
        {
            status = "paused",
            roleBrief = "Paused while finance approvals are being reviewed.",
            objectives = new { primary = new[] { "Protect cash flow" } },
            kpis = new { targets = new[] { "forecast_accuracy" } },
            toolPermissions = new { allowed = new[] { "erp" } },
            dataScopes = new { read = new[] { "finance" } },
            approvalThresholds = new { approval = new { expenseUsd = 5000 } },
            escalationRules = new { critical = new[] { "cash_runway_under_90_days" }, escalateTo = "founder" },
            triggerLogic = new { enabled = true, conditions = new[] { new { @event = "invoice_created", source = "erp" } } },
            workingHours = new { timezone = "UTC", windows = new[] { new { day = "monday", start = "08:00", end = "16:00" } } }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentOperatingProfileResponse>();
        Assert.NotNull(payload);
        Assert.Equal("paused", payload!.Status);
        Assert.Equal("Paused while finance approvals are being reviewed.", payload.RoleBrief);
        Assert.False(payload.CanReceiveAssignments);
        Assert.True(payload.UpdatedUtc > seed.OriginalUpdatedUtc);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<VirtualCompany.Application.Auth.ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var agent = await dbContext.Agents.AsNoTracking().SingleAsync(x => x.Id == seed.AgentId);

        Assert.True(agent.UpdatedUtc > seed.OriginalUpdatedUtc);
        Assert.Equal(AgentStatus.Paused, agent.Status);

        var auditEvent = await dbContext.AuditEvents.SingleAsync(x =>
            x.CompanyId == seed.CompanyId &&
            x.Action == "agent.status.updated" &&
            x.TargetId == seed.AgentId.ToString("N"));

        Assert.Equal("paused", auditEvent.Metadata["status"]);
        Assert.Equal("active", auditEvent.Metadata["statusBefore"]);
        Assert.Equal("paused", auditEvent.Metadata["statusAfter"]);
        Assert.Equal("[\"status\"]", auditEvent.Metadata["changedFields"]);
        Assert.Equal("Updated agent status to paused.", auditEvent.RationaleSummary);
    }

    [Fact]
    public async Task Manager_can_get_operating_profile_but_receives_redacted_governance_sections()
    {
        var seed = await SeedMembershipWithExistingAgentAsync();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<AgentOperatingProfileResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.Visibility.CanEditAgent);
        Assert.True(payload.Visibility.CanEditObjectives);
        Assert.True(payload.Visibility.CanEditKpis);
        Assert.True(payload.Visibility.CanEditWorkingHours);
        Assert.True(payload.Visibility.CanEditStatus);
        Assert.False(payload.Visibility.CanEditSensitiveGovernance);
        Assert.False(payload.Visibility.CanPauseOrRestrictAgent);
        Assert.NotEmpty(payload.Objectives);
        Assert.NotEmpty(payload.Kpis);
        Assert.NotEmpty(payload.WorkingHours);
        Assert.Empty(payload.ToolPermissions);
        Assert.Empty(payload.ApprovalThresholds);
        Assert.Empty(payload.EscalationRules);
        Assert.Empty(payload.TriggerLogic);
    }

    [Fact]
    public async Task Manager_can_update_operational_profile_fields_but_cannot_change_governance_fields_or_restricted_status()
    {
        var seed = await SeedMembershipWithExistingAgentAsync();

        using var client = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");

        var allowedResponse = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile", new
        {
            status = "paused",
            roleBrief = "Manager-adjusted operating profile.",
            objectives = new { primary = new[] { "Protect cash flow", "Reduce approval queue" } },
            kpis = new { targets = new[] { "forecast_accuracy", "approval_latency" } },
            workingHours = new { timezone = "UTC", windows = new[] { new { day = "tuesday", start = "09:00", end = "17:00" } } }
        });

        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);

        var allowedPayload = await allowedResponse.Content.ReadFromJsonAsync<AgentOperatingProfileResponse>();
        Assert.NotNull(allowedPayload);
        Assert.Equal("paused", allowedPayload!.Status);
        Assert.Equal("Manager-adjusted operating profile.", allowedPayload.RoleBrief);
        Assert.Equal("UTC", allowedPayload.WorkingHours["timezone"].GetString());
        Assert.Empty(allowedPayload.ToolPermissions);

        var rejectedResponse = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile", new
        {
            status = "restricted",
            roleBrief = "Manager-adjusted operating profile.",
            autonomyLevel = "level_3",
            objectives = new { primary = new[] { "Protect cash flow", "Reduce approval queue" } },
            kpis = new { targets = new[] { "forecast_accuracy", "approval_latency" } },
            toolPermissions = new { allowed = new[] { "erp", "wire_transfer" } },
            dataScopes = new { read = new[] { "finance" }, write = new[] { "approval_notes" } },
            approvalThresholds = new { approval = new { expenseUsd = 1 } },
            escalationRules = new { critical = new[] { "cash_runway_under_90_days" }, escalateTo = "founder" },
            triggerLogic = new { enabled = true, conditions = new[] { new { @event = "invoice_created", source = "erp" } } },
            workingHours = new { timezone = "UTC", windows = new[] { new { day = "wednesday", start = "10:00", end = "18:00" } } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, rejectedResponse.StatusCode);

        var problem = await rejectedResponse.Content.ReadFromJsonAsync<ValidationProblemResponse>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(UpdateAgentOperatingProfileCommand.Status), problem!.Errors.Keys);
        Assert.Contains(nameof(UpdateAgentOperatingProfileCommand.AutonomyLevel), problem.Errors.Keys);
        Assert.Contains(nameof(UpdateAgentOperatingProfileCommand.ToolPermissions), problem.Errors.Keys);
        Assert.Contains(nameof(UpdateAgentOperatingProfileCommand.DataScopes), problem.Errors.Keys);
        Assert.Contains(nameof(UpdateAgentOperatingProfileCommand.ApprovalThresholds), problem.Errors.Keys);
        Assert.Contains(nameof(UpdateAgentOperatingProfileCommand.EscalationRules), problem.Errors.Keys);
        Assert.Contains(nameof(UpdateAgentOperatingProfileCommand.TriggerLogic), problem.Errors.Keys);
    }

    [Fact]
    public async Task Operating_profile_endpoints_require_manager_access_and_stay_company_scoped()
    {
        var seed = await SeedEmployeeAndCrossCompanyAgentAsync();

        using var employeeClient = CreateAuthenticatedClient("employee", "employee@example.com", "Employee");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await employeeClient.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile")).StatusCode);

        using var managerClient = CreateAuthenticatedClient("manager", "manager@example.com", "Manager");
        Assert.Equal(HttpStatusCode.OK, (await managerClient.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/profile")).StatusCode);

        using var founderClient = CreateAuthenticatedClient("founder", "founder@example.com", "Founder");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await founderClient.GetAsync($"/api/companies/{seed.OtherCompanyId}/agents/{seed.OtherCompanyAgentId}/profile")).StatusCode);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await founderClient.GetAsync($"/api/companies/{seed.CompanyId}/agents/{seed.OtherCompanyAgentId}/profile")).StatusCode);

        var crossTenantUpdate = await founderClient.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.OtherCompanyAgentId}/profile", new
        {
            status = "active",
            roleBrief = "Scoped update should not cross tenant boundaries.",
            objectives = new { primary = new[] { "Protect cash flow" } },
            kpis = new { targets = new[] { "forecast_accuracy" } },
            toolPermissions = new { allowed = new[] { "erp" } },
            dataScopes = new { read = new[] { "finance" } },
            approvalThresholds = new { approval = new { expenseUsd = 5000 } },
            escalationRules = new { critical = new[] { "cash_runway_under_90_days" }, escalateTo = "founder" },
            triggerLogic = new { enabled = false },
            workingHours = new
            {
                timezone = "UTC",
                windows = new[]
                {
                    new { day = "monday", start = "08:00", end = "16:00" }
                }
            }
        });

        Assert.Equal(HttpStatusCode.NotFound, crossTenantUpdate.StatusCode);
    }

    [Theory]
    [InlineData(AgentStatus.Paused)]
    [InlineData(AgentStatus.Archived)]
    public void Paused_and_archived_agents_are_blocked_from_future_assignment_paths(AgentStatus status)
    {
        var agent = new Agent(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "operations",
            status == AgentStatus.Paused ? "Paused Ops" : "Archived Ops",
            "Operations Manager",
            "Operations",
            null,
            AgentSeniority.Lead,
            status,
            objectives: Payload(("primary", new JsonArray(JsonValue.Create("Keep workflow stable")))),
            kpis: Payload(("targets", new JsonArray(JsonValue.Create("cycle_time")))),
            tools: Payload(("allowed", new JsonArray(JsonValue.Create("project_management")))),
            scopes: Payload(("read", new JsonArray(JsonValue.Create("projects")))),
            thresholds: Payload(("delivery", new JsonObject { ["blockedDays"] = 2 })),
            escalationRules: Payload(("critical", new JsonArray(JsonValue.Create("missed_deadline")))),
            roleBrief: "Do not assign new work.",
            triggerLogic: Payload(("enabled", JsonValue.Create(false))),
            workingHours: Payload(
                ("timezone", JsonValue.Create("Europe/Stockholm")),
                ("windows", new JsonArray(new JsonObject
                {
                    ["day"] = "monday",
                    ["start"] = "09:00",
                    ["end"] = "17:00"
                }))));

        var exception = Assert.Throws<InvalidOperationException>(() => agent.EnsureCanReceiveAssignments());
        var expectedMessage = status == AgentStatus.Paused
            ? Agent.PausedAssignmentErrorMessage
            : Agent.ArchivedAssignmentErrorMessage;
        Assert.Contains(expectedMessage, exception.Message);
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

    private async Task<EditableAgentSeed> SeedMembershipWithExistingAgentAsync()
    {
        var userId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var originalUpdatedUtc = DateTime.UtcNow.AddMinutes(-30);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(userId, "founder@example.com", "Founder", "dev-header", "founder"),
                new User(managerUserId, "manager@example.com", "Manager", "dev-header", "manager"));
            dbContext.Companies.Add(new Company(companyId, "Company A"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, managerUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));

            dbContext.Agents.Add(new Agent(
                agentId,
                companyId,
                "finance",
                "Nora Ledger",
                "Finance Manager",
                "Finance",
                null,
                AgentSeniority.Senior,
                AgentStatus.Active,
                objectives: Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
                kpis: Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
                tools: Payload(("allowed", new JsonArray(JsonValue.Create("erp")))),
                scopes: Payload(("read", new JsonArray(JsonValue.Create("finance"))), ("write", new JsonArray(JsonValue.Create("forecast_drafts")))),
                thresholds: Payload(("approval", new JsonObject { ["expenseUsd"] = 5000 })),
                escalationRules: Payload(("critical", new JsonArray(JsonValue.Create("cash_runway_under_90_days"))), ("escalateTo", JsonValue.Create("founder"))),
                roleBrief: "Original finance operating profile.",
                triggerLogic: Payload(("enabled", JsonValue.Create(true))),
                workingHours: Payload(
                    ("timezone", JsonValue.Create("UTC")),
                    ("windows", new JsonArray(new JsonObject
                    {
                        ["day"] = "monday",
                        ["start"] = "08:00",
                        ["end"] = "16:00"
                    })))));

            return Task.CompletedTask;
        });

        await _factory.SeedAsync(async dbContext =>
        {
            var agent = await dbContext.Agents.SingleAsync(x => x.Id == agentId);
            typeof(Agent).GetProperty(nameof(Agent.UpdatedUtc))!.SetValue(agent, originalUpdatedUtc);
        });

        return new EditableAgentSeed(companyId, agentId, originalUpdatedUtc);
    }

    private async Task<CrossCompanyAgentSeed> SeedEmployeeAndCrossCompanyAgentAsync()
    {
        var ownerUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        var managerUserId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var otherCompanyAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(ownerUserId, "founder@example.com", "Founder", "dev-header", "founder"),
                new User(employeeUserId, "employee@example.com", "Employee", "dev-header", "employee"),
                new User(managerUserId, "manager@example.com", "Manager", "dev-header", "manager"));
            dbContext.Companies.AddRange(
                new Company(companyId, "Company A"),
                new Company(otherCompanyId, "Company B"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, managerUserId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            dbContext.Agents.AddRange(
                new Agent(agentId, companyId, "finance", "Company A Finance", "Finance Manager", "Finance", null, AgentSeniority.Senior, AgentStatus.Active),
                new Agent(otherCompanyAgentId, otherCompanyId, "support", "Company B Support", "Support Lead", "Support", null, AgentSeniority.Lead, AgentStatus.Active));
            return Task.CompletedTask;
        });

        return new CrossCompanyAgentSeed(companyId, otherCompanyId, agentId, otherCompanyAgentId);
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

    private static void AssertJsonDictionaryEqual(
        IReadOnlyDictionary<string, JsonNode?> expected,
        IReadOnlyDictionary<string, JsonNode?> actual)
    {
        Assert.Equal(expected.Count, actual.Count);

        foreach (var (key, expectedValue) in expected)
        {
            Assert.True(actual.TryGetValue(key, out var actualValue), $"Expected JSON property '{key}' to be present.");
            Assert.Equal(expectedValue?.ToJsonString(), actualValue?.ToJsonString());
        }
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
    private sealed record EditableAgentSeed(Guid CompanyId, Guid AgentId, DateTime OriginalUpdatedUtc);
    private sealed record CrossCompanyAgentSeed(Guid CompanyId, Guid OtherCompanyId, Guid AgentId, Guid OtherCompanyAgentId);

    private sealed class AgentOperatingProfileResponse
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string? RoleBrief { get; set; }
        public DateTime UpdatedUtc { get; set; }
        public bool CanReceiveAssignments { get; set; }
        public string AutonomyLevel { get; set; } = string.Empty;
        public Dictionary<string, JsonElement> Objectives { get; set; } = [];
        public Dictionary<string, JsonElement> Kpis { get; set; } = [];
        public Dictionary<string, JsonElement> ToolPermissions { get; set; } = [];
        public Dictionary<string, JsonElement> ApprovalThresholds { get; set; } = [];
        public Dictionary<string, JsonElement> EscalationRules { get; set; } = [];
        public Dictionary<string, JsonElement> TriggerLogic { get; set; } = [];
        public Dictionary<string, JsonElement> WorkingHours { get; set; } = [];
        public AgentProfileVisibilityResponse Visibility { get; set; } = new();
    }

    private sealed class AgentProfileVisibilityResponse
    {
        public bool CanEditAgent { get; set; }
        public bool CanEditObjectives { get; set; }
        public bool CanEditKpis { get; set; }
        public bool CanEditWorkingHours { get; set; }
        public bool CanEditStatus { get; set; }
        public bool CanEditSensitiveGovernance { get; set; }
        public bool CanPauseOrRestrictAgent { get; set; }
    }

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
        public string? AvatarUrl { get; set; }
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
        public string? AvatarUrl { get; set; }
        public string Seniority { get; set; } = string.Empty;
        public string AutonomyLevel { get; set; } = string.Empty;
    }

    private sealed class ValidationProblemResponse
    {
        public string? Title { get; set; }
        public string? Detail { get; set; }
        public Dictionary<string, string[]> Errors { get; set; } = [];
    }
}
