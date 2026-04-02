using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class MemoryIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public MemoryIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_company_wide_and_agent_specific_memory_items_persist_with_expected_scope()
    {
        var seed = await SeedMembershipWithAgentAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var companyWideResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            memoryType = "company_memory",
            summary = "Payroll approvals require dual sign-off.",
            salience = 0.92m,
            metadata = new Dictionary<string, JsonNode?> { ["category"] = JsonValue.Create("finance") }
        });

        var agentSpecificResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            agentId = seed.AgentId,
            memoryType = "preference",
            summary = "Prefers concise stand-up updates before 09:00 UTC.",
            salience = 0.61m,
            validFromUtc = DateTime.UtcNow.AddMinutes(-5)
        });

        Assert.Equal(HttpStatusCode.Created, companyWideResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, agentSpecificResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var items = await dbContext.MemoryItems.AsNoTracking().OrderBy(x => x.Summary).ToListAsync();
        Assert.Equal(2, items.Count);

        var companyWide = Assert.Single(items, x => x.AgentId is null);
        Assert.Equal(seed.CompanyId, companyWide.CompanyId);
        Assert.Equal(MemoryType.CompanyMemory, companyWide.MemoryType);
        Assert.True(companyWide.IsCompanyWide);
        Assert.Equal("Payroll approvals require dual sign-off.", companyWide.Summary);
        Assert.Equal(0.920m, companyWide.Salience);
        Assert.NotNull(companyWide.Embedding);

        var agentSpecific = Assert.Single(items, x => x.AgentId == seed.AgentId);
        Assert.Equal(MemoryType.Preference, agentSpecific.MemoryType);
        Assert.True(agentSpecific.IsAgentSpecific);
        Assert.Equal("Prefers concise stand-up updates before 09:00 UTC.", agentSpecific.Summary);
        Assert.Equal(seed.AgentId, agentSpecific.AgentId);
        Assert.NotNull(agentSpecific.Embedding);
    }

    [Fact]
    public async Task Create_memory_defaults_valid_from_to_creation_time_when_omitted()
    {
        var seed = await SeedMembershipWithAgentAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var beforeRequestUtc = DateTime.UtcNow;

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            memoryType = "company_memory",
            summary = "Default validity window should start immediately.",
            salience = 0.73m
        });

        var afterRequestUtc = DateTime.UtcNow;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<MemoryItemResponse>();
        Assert.NotNull(created);
        Assert.True(created!.ValidFromUtc >= beforeRequestUtc.AddSeconds(-1));
        Assert.True(created.ValidFromUtc <= afterRequestUtc.AddSeconds(1));
        Assert.Null(created.ValidToUtc);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var stored = await dbContext.MemoryItems.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(created.ValidFromUtc, stored.ValidFromUtc);
        Assert.Null(stored.ValidToUtc);
    }

    [Theory]
    [InlineData("preference", true)]
    [InlineData("decision_pattern", true)]
    [InlineData("summary", true)]
    [InlineData("role_memory", true)]
    [InlineData("company_memory", false)]
    public async Task Create_memory_accepts_all_supported_types(string memoryType, bool agentSpecific)
    {
        var seed = await SeedMembershipWithAgentAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var payload = new Dictionary<string, object?>
        {
            ["memoryType"] = memoryType,
            ["summary"] = $"Canonical {memoryType} memory item.",
            ["salience"] = 0.55m
        };

        if (agentSpecific)
        {
            payload["agentId"] = seed.AgentId;
        }

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<MemoryItemResponse>();
        Assert.NotNull(created);
        Assert.Equal(memoryType, created!.MemoryType);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var stored = await dbContext.MemoryItems.AsNoTracking().SingleAsync(x => x.Id == created.Id);
        Assert.Equal(memoryType, stored.MemoryType.ToStorageValue());
    }

    [Theory]
    [InlineData("decisionPattern", "decision_pattern")]
    [InlineData("role-memory", "role_memory")]
    [InlineData("companyMemory", "company_memory")]
    public async Task Create_memory_normalizes_supported_aliases_to_canonical_storage_value(string requestedType, string expectedType)
    {
        var seed = await SeedMembershipWithAgentAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            agentId = seed.AgentId,
            memoryType = requestedType,
            summary = $"Alias-backed {requestedType} memory item.",
            salience = 0.50m
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<MemoryItemResponse>();
        Assert.NotNull(created);
        Assert.Equal(expectedType, created!.MemoryType);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.Equal(expectedType, (await dbContext.MemoryItems.AsNoTracking().SingleAsync(x => x.Id == created.Id)).MemoryType.ToStorageValue());
    }

    [Fact]
    public async Task Create_memory_rejects_invalid_type_and_invalid_validity_window()
    {
        var seed = await SeedMembershipWithAgentAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var validFromUtc = DateTime.UtcNow.AddHours(1);
        var validToUtc = validFromUtc.AddMinutes(-30);

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            agentId = seed.AgentId,
            memoryType = "unsupported",
            summary = "Invalid memory record",
            salience = 0.5m,
            validFromUtc,
            validToUtc
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(CreateMemoryRequest.MemoryType), problem!.Errors.Keys);
        Assert.Contains(nameof(CreateMemoryRequest.ValidToUtc), problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_memory_rejects_raw_reasoning_in_summary_metadata_and_extension_fields()
    {
        var seed = await SeedMembershipWithAgentAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            agentId = seed.AgentId,
            memoryType = "summary",
            summary = "Hidden reasoning: inspect the invoice trail, compare private notes, then decide what to store.",
            salience = 0.5m,
            reasoning = "This internal reasoning must never be persisted.",
            metadata = new Dictionary<string, JsonNode?>
            {
                ["category"] = JsonValue.Create("finance"),
                ["scratchpad"] = JsonValue.Create("private working notes")
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(CreateMemoryRequest.Summary), problem!.Errors.Keys);
        Assert.Contains(nameof(CreateMemoryRequest.Metadata), problem.Errors.Keys);
        Assert.Contains("reasoning", problem.Errors.Keys);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        Assert.False(await dbContext.MemoryItems.AsNoTracking().AnyAsync(x => x.CompanyId == seed.CompanyId));
    }

    [Fact]
    public async Task Create_memory_rejects_agent_from_another_company()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var foreignCompanyId = Guid.NewGuid();
        var foreignAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(foreignCompanyId, "Foreign Memory Company"));
            dbContext.Agents.Add(new Agent(
                foreignAgentId,
                foreignCompanyId,
                "support",
                "Foreign Agent",
                "Support Lead",
                "Support",
                null,
                AgentSeniority.Lead,
                AgentStatus.Active));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            agentId = foreignAgentId,
            memoryType = "preference",
            summary = "Cross-tenant memory should be rejected.",
            salience = 0.5m
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_memory_rejects_invalid_memory_type_filter()
    {
        var seed = await SeedMembershipWithAgentAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/memory?memoryType=unsupported");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains(nameof(CreateMemoryRequest.MemoryType), problem!.Errors.Keys);
        Assert.Contains("Unsupported memory type", problem.Errors[nameof(CreateMemoryRequest.MemoryType)][0]);
    }


    [Fact]
    public async Task Listing_memory_for_agent_can_include_company_wide_records()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var otherAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Agents.Add(new Agent(otherAgentId, seed.CompanyId, "support", "Other Agent", "Support Lead", "Support", null, AgentSeniority.Lead, AgentStatus.Active));
            var otherCompanyId = Guid.NewGuid();
            dbContext.Companies.Add(new Company(otherCompanyId, "Isolated Company"));
            dbContext.MemoryItems.AddRange(
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, null, MemoryType.CompanyMemory, "Company policy memory", null, null, 0.9m, DateTime.UtcNow.AddHours(-2), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.RoleMemory, "Agent-specific role memory", null, null, 0.8m, DateTime.UtcNow.AddHours(-2), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, otherAgentId, MemoryType.Preference, "Other agent preference", null, null, 0.7m, DateTime.UtcNow.AddHours(-2), null),
                new MemoryItem(Guid.NewGuid(), otherCompanyId, null, MemoryType.CompanyMemory, "Other company memory", null, null, 1.0m, DateTime.UtcNow.AddHours(-2), null));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.GetFromJsonAsync<MemorySearchResponse>($"/api/companies/{seed.CompanyId}/memory?agentId={seed.AgentId}&includeCompanyWide=true&onlyActive=true");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Items.Count);
        Assert.Contains(result.Items, x => x.AgentId is null && x.MemoryType == "company_memory");
        Assert.Contains(result.Items, x => x.Scope == "company_wide");
        Assert.Contains(result.Items, x => x.AgentId == seed.AgentId && x.MemoryType == "role_memory");
        Assert.Contains(result.Items, x => x.Scope == "agent_specific");
        Assert.DoesNotContain(result.Items, x => x.AgentId == otherAgentId);
        Assert.DoesNotContain(result.Items, x => x.Summary == "Other company memory");
    }

    [Fact]
    public async Task Listing_memory_without_agent_returns_company_wide_records_by_default()
    {
        var seed = await SeedMembershipWithAgentAsync();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, null, MemoryType.CompanyMemory, "Company memory", null, null, 0.9m, DateTime.UtcNow.AddHours(-2), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Preference, "Agent memory", null, null, 0.8m, DateTime.UtcNow.AddHours(-2), null));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?onlyActive=true");

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.Null(item.AgentId);
        Assert.Equal("company_wide", item.Scope);
        Assert.Equal("Company memory", item.Summary);
    }


    [Fact]
    public async Task Listing_memory_can_return_company_wide_scope_only()
    {
        var seed = await SeedMembershipWithAgentAsync();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, null, MemoryType.CompanyMemory, "Company memory", null, null, 0.9m, DateTime.UtcNow.AddHours(-2), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Preference, "Agent memory", null, null, 0.8m, DateTime.UtcNow.AddHours(-2), null));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?scope=company_wide&onlyActive=true");

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.Null(item.AgentId);
        Assert.Equal("company_wide", item.Scope);
        Assert.Equal("Company memory", item.Summary);
    }

    [Fact]
    public async Task Listing_memory_can_return_agent_specific_scope_only()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var otherAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Agents.Add(new Agent(otherAgentId, seed.CompanyId, "support", "Other Agent", "Support Lead", "Support", null, AgentSeniority.Lead, AgentStatus.Active));
            dbContext.MemoryItems.AddRange(
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, null, MemoryType.CompanyMemory, "Company memory", null, null, 0.9m, DateTime.UtcNow.AddHours(-2), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Preference, "Target agent memory", null, null, 0.8m, DateTime.UtcNow.AddHours(-2), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, otherAgentId, MemoryType.Preference, "Other agent memory", null, null, 0.7m, DateTime.UtcNow.AddHours(-2), null));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?agentId={seed.AgentId}&scope=agent_specific&onlyActive=true");

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.Equal(seed.AgentId, item.AgentId);
        Assert.Equal("agent_specific", item.Scope);
        Assert.Equal("Target agent memory", item.Summary);
    }

    [Fact]
    public async Task Listing_memory_honors_active_salience_and_recency_filters()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var oldCreatedUtc = DateTime.UtcNow.AddDays(-10);
        var recentCreatedUtc = DateTime.UtcNow.AddMinutes(-20);
        var cutoffUtc = DateTime.UtcNow.AddDays(-1);

        var oldItemId = Guid.NewGuid();
        var recentItemId = Guid.NewGuid();
        var expiredItemId = Guid.NewGuid();

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(oldItemId, seed.CompanyId, seed.AgentId, MemoryType.Summary, "Old but strong memory", null, null, 0.95m, DateTime.UtcNow.AddDays(-30), null),
                new MemoryItem(recentItemId, seed.CompanyId, seed.AgentId, MemoryType.Summary, "Recent but low-salience memory", null, null, 0.20m, DateTime.UtcNow.AddHours(-2), null),
                new MemoryItem(expiredItemId, seed.CompanyId, seed.AgentId, MemoryType.Summary, "Expired memory", null, null, 0.99m, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddHours(-1)));

            await dbContext.SaveChangesAsync();

            await SetCreatedUtcAsync(dbContext, oldItemId, oldCreatedUtc);
            await SetCreatedUtcAsync(dbContext, recentItemId, recentCreatedUtc);
            await SetCreatedUtcAsync(dbContext, expiredItemId, recentCreatedUtc);
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?agentId={seed.AgentId}&minSalience=0.90&createdAfterUtc={Uri.EscapeDataString(cutoffUtc.ToString("O"))}&onlyActive=true");

        Assert.NotNull(result);
        var item = Assert.Single(result!.Items);
        Assert.Equal(oldItemId, item.Id);
        Assert.Equal(1, result.TotalCount);
    }

    [Fact]
    public async Task Listing_memory_only_active_excludes_future_and_expired_windows()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var asOfUtc = DateTime.UtcNow;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Summary, "Active open-ended memory", null, null, 0.90m, asOfUtc.AddHours(-3), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Summary, "Active bounded memory", null, null, 0.85m, asOfUtc.AddHours(-3), asOfUtc.AddHours(2)),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Summary, "Future memory", null, null, 0.95m, asOfUtc.AddMinutes(10), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Summary, "Expired memory", null, null, 0.99m, asOfUtc.AddHours(-5), asOfUtc.AddMinutes(-1)));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?agentId={seed.AgentId}&scope=agent_specific&onlyActive=true&asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O"))}");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Items.Count);
        Assert.Contains(result.Items, item => item.Summary == "Active open-ended memory");
        Assert.Contains(result.Items, item => item.Summary == "Active bounded memory");
        Assert.DoesNotContain(result.Items, item => item.Summary == "Future memory");
        Assert.DoesNotContain(result.Items, item => item.Summary == "Expired memory");
    }

    [Fact]
    public async Task Listing_memory_treats_valid_to_equal_to_as_of_as_inactive()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var asOfUtc = DateTime.UtcNow;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.MemoryItems.Add(new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Summary, "Boundary memory", null, null, 0.88m, asOfUtc.AddHours(-1), asOfUtc));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?agentId={seed.AgentId}&scope=agent_specific&onlyActive=true&asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O"))}");

        Assert.NotNull(result);
        Assert.Empty(result!.Items);
    }

    [Fact]
    public async Task Listing_memory_orders_by_salience_when_no_semantic_query_is_supplied()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var highSalienceId = Guid.NewGuid();
        var lowSalienceId = Guid.NewGuid();
        var createdUtc = DateTime.UtcNow.AddHours(-1);

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(highSalienceId, seed.CompanyId, seed.AgentId, MemoryType.Summary, "High salience memory", null, null, 0.95m, DateTime.UtcNow.AddDays(-2), null),
                new MemoryItem(lowSalienceId, seed.CompanyId, seed.AgentId, MemoryType.Summary, "Low salience memory", null, null, 0.30m, DateTime.UtcNow.AddDays(-2), null));

            await dbContext.SaveChangesAsync();
            await SetCreatedUtcAsync(dbContext, highSalienceId, createdUtc);
            await SetCreatedUtcAsync(dbContext, lowSalienceId, createdUtc);
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?agentId={seed.AgentId}&scope=agent_specific&onlyActive=true");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Items.Count);
        Assert.Equal(highSalienceId, result.Items[0].Id);
        Assert.Equal(lowSalienceId, result.Items[1].Id);
    }

    [Fact]
    public async Task Listing_memory_orders_by_recency_when_salience_is_equal()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var olderId = Guid.NewGuid();
        var recentId = Guid.NewGuid();

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(olderId, seed.CompanyId, seed.AgentId, MemoryType.Summary, "Older memory", null, null, 0.70m, DateTime.UtcNow.AddDays(-5), null),
                new MemoryItem(recentId, seed.CompanyId, seed.AgentId, MemoryType.Summary, "Recent memory", null, null, 0.70m, DateTime.UtcNow.AddDays(-5), null));

            await dbContext.SaveChangesAsync();
            await SetCreatedUtcAsync(dbContext, olderId, DateTime.UtcNow.AddDays(-30));
            await SetCreatedUtcAsync(dbContext, recentId, DateTime.UtcNow.AddMinutes(-10));
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?agentId={seed.AgentId}&scope=agent_specific&onlyActive=true");

        Assert.NotNull(result);
        Assert.Equal(2, result!.Items.Count);
        Assert.Equal(recentId, result.Items[0].Id);
        Assert.Equal(olderId, result.Items[1].Id);
    }

    [Fact]
    public async Task Search_memory_can_filter_multiple_types_and_as_of_timestamp()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var asOfUtc = DateTime.UtcNow.AddHours(-2);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, null, MemoryType.CompanyMemory, "Company baseline", null, null, 0.8m, asOfUtc.AddDays(-10), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Summary, "Agent summary", null, null, 0.7m, asOfUtc.AddDays(-3), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, seed.AgentId, MemoryType.Preference, "Agent preference", null, null, 0.9m, asOfUtc.AddDays(-3), null),
                new MemoryItem(Guid.NewGuid(), seed.CompanyId, null, MemoryType.CompanyMemory, "Future company policy", null, null, 0.95m, asOfUtc.AddHours(1), null));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        var result = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory/search", new
        {
            agentId = seed.AgentId,
            memoryTypes = new[] { "company_memory", "summary" },
            asOfUtc,
            onlyActive = true,
            limit = 10
        });

        var payload = await result.Content.ReadFromJsonAsync<MemorySearchResponse>();
        Assert.NotNull(payload);
        Assert.Equal(2, payload!.Items.Count);
        Assert.All(payload.Items, item => Assert.Contains(item.MemoryType, new[] { "company_memory", "summary" }));
        Assert.DoesNotContain(payload.Items, item => item.Summary == "Future company policy");
        Assert.DoesNotContain(payload.Items, item => item.MemoryType == "preference");
    }

    [Fact]
    public async Task Search_can_rank_semantically_relevant_memory_within_tenant_scope()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var otherCompany = await SeedSecondCompanyAsync();

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);
        using var otherClient = CreateAuthenticatedClient(otherCompany.Subject, otherCompany.Email, otherCompany.DisplayName);

        await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            memoryType = "company_memory",
            summary = "Finance approval thresholds require CFO review for expenses above fifty thousand dollars.",
            salience = 0.95m
        });

        await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            memoryType = "summary",
            summary = "Office snacks are restocked on Thursdays and the break room budget is tracked weekly.",
            salience = 1.00m
        });

        await otherClient.PostAsJsonAsync($"/api/companies/{otherCompany.CompanyId}/memory", new
        {
            memoryType = "company_memory",
            summary = "Finance approval thresholds require CFO review for expenses above fifty thousand dollars.",
            salience = 1.00m
        });

        var result = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory/search", new
        {
            queryText = "Finance approval thresholds require CFO review for expenses above fifty thousand dollars.",
            onlyActive = true,
            limit = 5
        });

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var payload = await result.Content.ReadFromJsonAsync<MemorySearchResponse>();
        Assert.NotNull(payload);
        Assert.True(payload!.SemanticSearchApplied);
        Assert.Equal("Finance approval thresholds require CFO review for expenses above fifty thousand dollars.", payload.Items[0].Summary);
        Assert.All(payload.Items, item => Assert.Equal(seed.CompanyId, item.CompanyId));
    }

    [Fact]
    public async Task Search_memory_prefers_semantic_relevance_over_higher_salience_noise()
    {
        var seed = await SeedMembershipWithAgentAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            memoryType = "company_memory",
            summary = "Expense reimbursements above ten thousand dollars require controller review.",
            salience = 0.45m
        });

        await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory", new
        {
            memoryType = "company_memory",
            summary = "The office coffee machine uses a higher quality filter and should be cleaned daily.",
            salience = 1.00m
        });

        var result = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory/search", new
        {
            queryText = "Expense reimbursements above ten thousand dollars require controller review.",
            limit = 5
        });

        var payload = await result.Content.ReadFromJsonAsync<MemorySearchResponse>();
        Assert.NotNull(payload);
        Assert.Equal("Expense reimbursements above ten thousand dollars require controller review.", payload!.Items[0].Summary);
    }

    [Fact]
    public async Task Manager_policy_is_scope_and_type_restricted_for_memory_lifecycle_operations()
    {
        var seed = await SeedMembershipWithAgentAsync(
            CompanyMembershipRole.Manager,
            "memory-manager",
            "memory-manager@example.com",
            "Memory Manager");

        var companyWideMemoryId = Guid.NewGuid();
        var agentScopedMemoryId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(companyWideMemoryId, seed.CompanyId, null, MemoryType.CompanyMemory, "Company-wide policy memory", null, null, 0.80m, DateTime.UtcNow.AddDays(-1), null),
                new MemoryItem(agentScopedMemoryId, seed.CompanyId, seed.AgentId, MemoryType.Preference, "Agent preference memory", null, null, 0.75m, DateTime.UtcNow.AddDays(-1), null));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var deniedExpire = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/memory/{companyWideMemoryId}/expire",
            new
            {
                validToUtc = DateTime.UtcNow,
                reason = "Manager should not expire company-wide company memory."
            });

        Assert.Equal(HttpStatusCode.Forbidden, deniedExpire.StatusCode);

        var allowedExpire = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/memory/{agentScopedMemoryId}/expire",
            new
            {
                validToUtc = DateTime.UtcNow,
                reason = "Agent-specific preference is no longer current."
            });

        Assert.Equal(HttpStatusCode.OK, allowedExpire.StatusCode);

        var deniedDelete = await client.SendAsync(CreateDeleteMemoryRequest(
            seed.CompanyId,
            agentScopedMemoryId,
            "Manager should not delete memory items."));

        Assert.Equal(HttpStatusCode.Forbidden, deniedDelete.StatusCode);
    }

    [Fact]
    public async Task Delete_memory_items_within_tenant_supports_company_wide_and_agent_specific_scope()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var companyWideMemoryId = Guid.NewGuid();
        var agentScopedMemoryId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(companyWideMemoryId, seed.CompanyId, null, MemoryType.CompanyMemory, "Company-wide memory item", null, null, 0.80m, DateTime.UtcNow.AddDays(-1), null),
                new MemoryItem(agentScopedMemoryId, seed.CompanyId, seed.AgentId, MemoryType.Preference, "Agent-specific memory item", null, null, 0.75m, DateTime.UtcNow.AddDays(-1), null));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var companyWideDelete = await client.SendAsync(CreateDeleteMemoryRequest(
            seed.CompanyId,
            companyWideMemoryId,
            "Company-wide memory must stay tenant-scoped."));
        var agentScopedDelete = await client.SendAsync(CreateDeleteMemoryRequest(
            seed.CompanyId,
            agentScopedMemoryId,
            "Agent-specific memory must stay tenant-scoped."));

        Assert.Equal(HttpStatusCode.NoContent, companyWideDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, agentScopedDelete.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var storedItems = await dbContext.MemoryItems
            .AsNoTracking()
            .Where(x => x.CompanyId == seed.CompanyId && (x.Id == companyWideMemoryId || x.Id == agentScopedMemoryId))
            .ToListAsync();

        Assert.Equal(2, storedItems.Count);
        Assert.Contains(storedItems, item => item.Id == companyWideMemoryId && item.AgentId is null && item.DeletedUtc.HasValue && item.DeletionReason == "Company-wide memory must stay tenant-scoped.");
        Assert.Contains(storedItems, item => item.Id == agentScopedMemoryId && item.AgentId == seed.AgentId && item.DeletedUtc.HasValue && item.DeletionReason == "Agent-specific memory must stay tenant-scoped.");
    }

    [Fact]
    public async Task Expire_and_delete_memory_items_enforce_tenant_boundaries()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var otherCompany = await SeedSecondCompanyAsync();
        var ownMemoryId = Guid.NewGuid();
        var otherMemoryId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(ownMemoryId, seed.CompanyId, null, MemoryType.CompanyMemory, "Own memory item", null, null, 0.8m, DateTime.UtcNow.AddDays(-1), null),
                new MemoryItem(otherMemoryId, otherCompany.CompanyId, null, MemoryType.CompanyMemory, "Other company memory", null, null, 0.8m, DateTime.UtcNow.AddDays(-1), null));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var expireResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory/{ownMemoryId}/expire", new
        {
            validToUtc = DateTime.UtcNow,
            reason = "This memory should no longer be considered active."
        });

        Assert.Equal(HttpStatusCode.OK, expireResponse.StatusCode);

        var activeAfterExpire = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?onlyActive=true");
        Assert.NotNull(activeAfterExpire);
        Assert.DoesNotContain(activeAfterExpire!.Items, x => x.Id == ownMemoryId);

        var crossTenantExpire = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/memory/{otherMemoryId}/expire", new
        {
            validToUtc = DateTime.UtcNow,
            reason = "Cross-tenant expiration must not disclose anything."
        });
        Assert.Equal(HttpStatusCode.NotFound, crossTenantExpire.StatusCode);

        var crossTenantDelete = await client.DeleteAsync($"/api/companies/{seed.CompanyId}/memory/{otherMemoryId}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenantDelete.StatusCode);

        var deleteResponse = await client.SendAsync(CreateDeleteMemoryRequest(
            seed.CompanyId,
            ownMemoryId,
            "Superseded by a new privacy-safe memory record."));
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var stored = await dbContext.MemoryItems.AsNoTracking().SingleAsync(x => x.Id == ownMemoryId);
        var auditEvents = await dbContext.AuditEvents
            .AsNoTracking()
            .Where(x => x.CompanyId == seed.CompanyId && x.TargetType == AuditTargetTypes.MemoryItem && x.TargetId == ownMemoryId.ToString("N"))
            .OrderBy(x => x.OccurredUtc)
            .ToListAsync();

        Assert.NotNull(stored.DeletedUtc);
        Assert.Equal(seed.UserId, stored.DeletedByActorId);
        Assert.Equal("Superseded by a new privacy-safe memory record.", stored.DeletionReason);
        Assert.Contains(auditEvents, x => x.Action == AuditEventActions.MemoryItemExpired && x.Outcome == AuditEventOutcomes.Succeeded);
        Assert.Contains(auditEvents, x => x.Action == AuditEventActions.MemoryItemDeleted && x.Outcome == AuditEventOutcomes.Succeeded);

        var activeAfterDelete = await client.GetFromJsonAsync<MemorySearchResponse>(
            $"/api/companies/{seed.CompanyId}/memory?onlyActive=true");
        Assert.NotNull(activeAfterDelete);
        Assert.DoesNotContain(activeAfterDelete!.Items, x => x.Id == ownMemoryId);
    }

    [Fact]
    public async Task Get_memory_by_id_excludes_not_yet_valid_and_expired_items_by_default()
    {
        var seed = await SeedMembershipWithAgentAsync();
        var futureMemoryId = Guid.NewGuid();
        var expiredMemoryId = Guid.NewGuid();
        var activeMemoryId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.MemoryItems.AddRange(
                new MemoryItem(futureMemoryId, seed.CompanyId, null, MemoryType.CompanyMemory, "Future memory", null, null, 0.70m, DateTime.UtcNow.AddHours(1), null),
                new MemoryItem(expiredMemoryId, seed.CompanyId, null, MemoryType.CompanyMemory, "Expired memory", null, null, 0.70m, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddMinutes(-5)),
                new MemoryItem(activeMemoryId, seed.CompanyId, null, MemoryType.CompanyMemory, "Active memory", null, null, 0.70m, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(1)));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var futureResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/memory/{futureMemoryId}");
        var expiredResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/memory/{expiredMemoryId}");
        var activeResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/memory/{activeMemoryId}");

        Assert.Equal(HttpStatusCode.NotFound, futureResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, expiredResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);

        var activePayload = await activeResponse.Content.ReadFromJsonAsync<MemoryItemResponse>();
        Assert.NotNull(activePayload);
        Assert.Equal(activeMemoryId, activePayload!.Id);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private static HttpRequestMessage CreateDeleteMemoryRequest(Guid companyId, Guid memoryId, string? reason = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/companies/{companyId}/memory/{memoryId}");
        request.Content = JsonContent.Create(new { reason });
        return request;
    }

    private async Task<SeededCompanyContext> SeedMembershipWithAgentAsync(CompanyMembershipRole role = CompanyMembershipRole.Owner, string subject = "memory-owner", string email = "memory-owner@example.com", string displayName = "Memory Owner")
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Memory Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                role,
                CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(
                agentId,
                companyId,
                "finance",
                "Nora Ledger",
                "Finance Manager",
                "Finance",
                null,
                AgentSeniority.Senior,
                AgentStatus.Active));

            return Task.CompletedTask;
        });

        return new SeededCompanyContext(companyId, agentId, userId, subject, email, displayName);
    }

    private async Task<SeededCompanyContext> SeedSecondCompanyAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        const string subject = "memory-other";
        const string email = "memory-other@example.com";
        const string displayName = "Other Owner";

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Other Memory Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new SeededCompanyContext(companyId, Guid.Empty, userId, subject, email, displayName);
    }

    private static async Task SetCreatedUtcAsync(VirtualCompanyDbContext dbContext, Guid memoryId, DateTime createdUtc)
    {
        var item = await dbContext.MemoryItems.IgnoreQueryFilters().SingleAsync(x => x.Id == memoryId);
        typeof(MemoryItem).GetProperty(nameof(MemoryItem.CreatedUtc))!.SetValue(item, createdUtc);
        await dbContext.SaveChangesAsync();
    }

    private sealed record SeededCompanyContext(Guid CompanyId, Guid AgentId, Guid UserId, string Subject, string Email, string DisplayName);

    private sealed class CreateMemoryRequest
    {
        public string? MemoryType { get; set; }
        public string? Summary { get; set; }
        public Dictionary<string, JsonNode?>? Metadata { get; set; }
        public DateTime? ValidToUtc { get; set; }
    }

    private sealed class MemorySearchResponse
    {
        public List<MemoryItemResponse> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public bool SemanticSearchApplied { get; set; }
    }

    private sealed class MemoryItemResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public Guid? AgentId { get; set; }
        public string Scope { get; set; } = string.Empty;
        public string MemoryType { get; set; } = string.Empty;
        public string? SourceEntityType { get; set; }
        public string Summary { get; set; } = string.Empty;
        public decimal Salience { get; set; }
        public DateTime ValidFromUtc { get; set; }
        public DateTime? ValidToUtc { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}