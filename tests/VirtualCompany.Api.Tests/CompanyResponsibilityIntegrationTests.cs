using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyResponsibilityIntegrationTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Micro_preset_is_idempotent_assigns_all_areas_and_compatible_agents()
    {
        var seed = await SeedAsync();
        using var client = Client("owner", "owner@example.com", "Owner");
        var request = new { companySize = "micro", ownerMembershipId = seed.OwnerMembershipId, mode = "fill_missing", reason = "Initial setup" };

        var preview = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/presets/preview", request);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var first = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/presets/apply", request);
        var second = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/presets/apply", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var assignments = await db.CompanyResponsibilityAssignments.IgnoreQueryFilters().Where(x => x.CompanyId == seed.CompanyId).ToListAsync();
        Assert.Equal(6, assignments.Count);
        Assert.All(assignments, x => Assert.Equal(seed.OwnerMembershipId, x.AssignedMembershipId));
        Assert.Equal(5, assignments.Count(x => x.PrimaryAgentId.HasValue));
        Assert.Equal(CompanySizeBand.Micro, (await db.Companies.FindAsync(seed.CompanyId))!.SizeBand);
    }

    [Fact]
    public async Task Fill_missing_does_not_replace_explicit_sales_assignment()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(db =>
        {
            db.CompanyResponsibilityAssignments.Add(new(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Sales,
                ResponsibilityAssignmentKind.Primary, seed.ManagerMembershipId, seed.SalesAgentId,
                AgentAutonomyLevel.Level2, null, seed.OwnerMembershipId));
            return Task.CompletedTask;
        });
        using var client = Client("owner", "owner@example.com", "Owner");
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/presets/apply",
            new { companySize = "micro", ownerMembershipId = seed.OwnerMembershipId, mode = "fill_missing" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var sales = await db.CompanyResponsibilityAssignments.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId && x.ResponsibilityArea == ResponsibilityArea.Sales);
        Assert.Equal(seed.ManagerMembershipId, sales.AssignedMembershipId);
        Assert.Equal(AgentAutonomyLevel.Level2, sales.AuthorityLevel);
    }

    [Fact]
    public async Task Explicit_replace_changes_an_existing_primary_assignment()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(db =>
        {
            db.CompanyResponsibilityAssignments.Add(new(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Sales,
                ResponsibilityAssignmentKind.Primary, seed.ManagerMembershipId, seed.SalesAgentId,
                AgentAutonomyLevel.Level2, null, seed.OwnerMembershipId));
            return Task.CompletedTask;
        });
        using var client = Client("owner", "owner@example.com", "Owner");
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/presets/apply",
            new { companySize = "micro", ownerMembershipId = seed.OwnerMembershipId, mode = "replace_existing", reason = "Return ownership" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var sales = await db.CompanyResponsibilityAssignments.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId && x.ResponsibilityArea == ResponsibilityArea.Sales);
        Assert.Equal(seed.OwnerMembershipId, sales.AssignedMembershipId);
    }

    [Fact]
    public async Task Medium_preset_assigns_selected_manager_and_owner_oversight()
    {
        var seed = await SeedAsync();
        using var client = Client("owner", "owner@example.com", "Owner");
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/presets/apply", new
        {
            companySize = "medium", ownerMembershipId = seed.OwnerMembershipId,
            managerMembershipIds = new Dictionary<string, Guid> { ["sales"] = seed.ManagerMembershipId },
            mode = "fill_missing"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var assignments = await db.CompanyResponsibilityAssignments.IgnoreQueryFilters().Where(x => x.CompanyId == seed.CompanyId).ToListAsync();
        Assert.Equal(seed.ManagerMembershipId, assignments.Single(x => x.ResponsibilityArea == ResponsibilityArea.Sales && x.AssignmentKind == ResponsibilityAssignmentKind.Primary).AssignedMembershipId);
        Assert.Equal(6, assignments.Count(x => x.AssignmentKind == ResponsibilityAssignmentKind.ExecutiveOversight && x.AssignedMembershipId == seed.OwnerMembershipId));
    }

    [Fact]
    public async Task Member_can_read_but_cannot_mutate()
    {
        var seed = await SeedAsync();
        using var client = Client("member", "member@example.com", "Member");
        var read = await client.GetAsync($"/api/companies/{seed.CompanyId}/responsibilities");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var payload = await read.Content.ReadFromJsonAsync<CompanyResponsibilitiesDto>(JsonOptions);
        Assert.NotNull(payload);
        Assert.False(payload!.CanManage);
        Assert.All(payload.Members, member => Assert.Equal(CompanyMembershipStatus.Active, member.Status));
        Assert.Contains(payload.Agents, agent => agent.AgentId == seed.SalesAgentId && agent.CompatibleAreas.Contains(ResponsibilityArea.Sales));
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/responsibilities/presets/preview",
            new { companySize = "micro", ownerMembershipId = seed.OwnerMembershipId })).StatusCode);
        var mutation = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/presets/apply",
            new { companySize = "micro", ownerMembershipId = seed.OwnerMembershipId });
        Assert.Equal(HttpStatusCode.Forbidden, mutation.StatusCode);
    }

    [Fact]
    public async Task Owner_read_contract_exposes_backend_manage_capability_and_tenant_scoped_picker_options()
    {
        var seed = await SeedAsync();
        using var client = Client("owner", "owner@example.com", "Owner");
        var payload = await client.GetFromJsonAsync<CompanyResponsibilitiesDto>($"/api/companies/{seed.CompanyId}/responsibilities", JsonOptions);

        Assert.NotNull(payload);
        Assert.True(payload!.CanManage);
        Assert.DoesNotContain(payload.Members, member => member.MembershipId == seed.OtherMembershipId);
        Assert.DoesNotContain(payload.Agents, agent => agent.AgentId == seed.OtherAgentId);
    }

    [Fact]
    public async Task Cross_company_membership_and_agent_are_rejected_without_disclosure()
    {
        var seed = await SeedAsync();
        using var client = Client("owner", "owner@example.com", "Owner");
        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/assignments", new
        {
            responsibilityArea = "sales", assignmentKind = "primary", assignedMembershipId = seed.OtherMembershipId,
            primaryAgentId = seed.OtherAgentId, authorityLevel = "level_1", reason = "invalid cross-company attempt"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Other Company", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Inactive_membership_is_rejected()
    {
        var seed = await SeedAsync();
        Guid inactiveMembershipId = Guid.NewGuid();
        await _factory.SeedAsync(db =>
        {
            var userId = Guid.NewGuid();
            db.Users.Add(new User(userId, "inactive@example.com", "Inactive", "dev-header", "inactive"));
            db.CompanyMemberships.Add(new CompanyMembership(inactiveMembershipId, seed.CompanyId, userId,
                CompanyMembershipRole.Manager, CompanyMembershipStatus.Revoked));
            return Task.CompletedTask;
        });
        using var client = Client("owner", "owner@example.com", "Owner");
        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/assignments", new
        {
            responsibilityArea = "sales", assignmentKind = "primary", assignedMembershipId = inactiveMembershipId,
            authorityLevel = "level_1"
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Tenant_query_filter_returns_only_the_active_company_assignments()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(db =>
        {
            db.CompanyResponsibilityAssignments.AddRange(
                new(Guid.NewGuid(), seed.CompanyId, ResponsibilityArea.Sales, ResponsibilityAssignmentKind.Primary,
                    seed.OwnerMembershipId, seed.SalesAgentId, AgentAutonomyLevel.Level1, null, null),
                new(Guid.NewGuid(), seed.OtherCompanyId, ResponsibilityArea.Sales, ResponsibilityAssignmentKind.Primary,
                    seed.OtherMembershipId, seed.OtherAgentId, AgentAutonomyLevel.Level1, null, null));
            return Task.CompletedTask;
        });
        using var scope = _factory.Services.CreateScope();
        var companyContext = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContext.SetCompanyId(seed.CompanyId);
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var visible = await db.CompanyResponsibilityAssignments.AsNoTracking().ToListAsync();
        Assert.Single(visible);
        Assert.Equal(seed.CompanyId, visible[0].CompanyId);
    }

    [Fact]
    public async Task Mutation_writes_structured_audit_evidence()
    {
        var seed = await SeedAsync();
        using var client = Client("owner", "owner@example.com", "Owner");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "responsibility-audit-correlation");
        var response = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/responsibilities/assignments", new
        {
            responsibilityArea = "sales", assignmentKind = "primary", assignedMembershipId = seed.ManagerMembershipId,
            primaryAgentId = seed.SalesAgentId, authorityLevel = "level_2", escalationMembershipId = seed.OwnerMembershipId,
            reason = "Manager owns sales"
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var audit = await db.AuditEvents.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == seed.CompanyId && x.Action == "company.responsibility.assignment_changed");
        Assert.Equal("responsibility-audit-correlation", audit.CorrelationId);
        Assert.Equal("sales", audit.Metadata["responsibilityArea"]);
        Assert.Equal("Manager owns sales", audit.Metadata["reason"]);
        Assert.False(string.IsNullOrWhiteSpace(audit.PayloadDiffJson));
    }

    [Fact]
    public void Storage_values_and_primary_unique_index_are_stable()
    {
        Assert.Equal("company_performance", ResponsibilityArea.CompanyPerformance.ToStorageValue());
        Assert.Equal("cash_and_accounting", ResponsibilityArea.CashAndAccounting.ToStorageValue());
        Assert.Equal("executive_oversight", ResponsibilityAssignmentKind.ExecutiveOversight.ToStorageValue());
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var entity = db.Model.FindEntityType(typeof(CompanyResponsibilityAssignment))!;
        var index = entity.GetIndexes().Single(x => x.GetDatabaseName() == "UX_company_responsibility_primary");
        Assert.True(index.IsUnique);
    }

    private HttpClient Client(string subject, string email, string name)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, name);
        return client;
    }

    private async Task<Seed> SeedAsync()
    {
        var seed = new Seed(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await _factory.SeedAsync(db =>
        {
            var ownerId = Guid.NewGuid(); var managerId = Guid.NewGuid(); var memberId = Guid.NewGuid(); var otherOwnerId = Guid.NewGuid();
            db.Users.AddRange(new User(ownerId, "owner@example.com", "Owner", "dev-header", "owner"),
                new User(managerId, "manager@example.com", "Manager", "dev-header", "manager"),
                new User(memberId, "member@example.com", "Member", "dev-header", "member"),
                new User(otherOwnerId, "other@example.com", "Other Owner", "dev-header", "other"));
            db.Companies.AddRange(new Company(seed.CompanyId, "Company A"), new Company(seed.OtherCompanyId, "Other Company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(seed.OwnerMembershipId, seed.CompanyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(seed.ManagerMembershipId, seed.CompanyId, managerId, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), seed.CompanyId, memberId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active),
                new CompanyMembership(seed.OtherMembershipId, seed.OtherCompanyId, otherOwnerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            db.Agents.AddRange(
                new Agent(Guid.NewGuid(), seed.CompanyId, "finance", "Laura", "Finance Manager", "Finance", null, AgentSeniority.Senior),
                new Agent(seed.SalesAgentId, seed.CompanyId, "sales", "Alex", "Sales Manager", "Sales", null, AgentSeniority.Senior),
                new Agent(Guid.NewGuid(), seed.CompanyId, "marketing", "Maya", "Marketing Manager", "Marketing", null, AgentSeniority.Senior),
                new Agent(Guid.NewGuid(), seed.CompanyId, "support", "Ben", "Support Manager", "Support", null, AgentSeniority.Senior),
                new Agent(seed.OtherAgentId, seed.OtherCompanyId, "sales", "Other Agent", "Sales", "Sales", null, AgentSeniority.Senior));
            return Task.CompletedTask;
        });
        return seed;
    }

    private sealed record Seed(Guid CompanyId, Guid OtherCompanyId, Guid OwnerMembershipId, Guid ManagerMembershipId,
        Guid OtherMembershipId, Guid SalesAgentId, Guid OtherAgentId);
}
