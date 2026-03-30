using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AuthAndTenantIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuthAndTenantIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetCurrentUser_provisions_internal_user_and_returns_empty_memberships()
    {
        using var client = CreateAuthenticatedClient("new-user", "new.user@example.com", "New User");
        var response = await client.GetFromJsonAsync<CurrentUserContextResponse>("/api/auth/me");

        Assert.NotNull(response);
        Assert.NotEqual(Guid.Empty, response!.User.Id);
        Assert.Equal("new.user@example.com", response.User.Email);
        Assert.Equal("New User", response.User.DisplayName);
        Assert.Equal("dev-header", response.User.AuthProvider);
        Assert.Equal("new-user", response.User.AuthSubject);
        Assert.Empty(response.Memberships);
        Assert.Null(response.ActiveCompany);
        Assert.False(response.CompanySelectionRequired);
    }

    [Fact]
    public async Task GetCurrentUser_resolves_existing_user_by_provider_and_subject_when_email_changes()
    {
        var userId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "alice@example.com", "Alice", "entra-id", "alice"));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient("alice", "alice.updated@example.com", "Alice Updated", "entra-id");
        var response = await client.GetFromJsonAsync<CurrentUserContextResponse>("/api/auth/me");

        Assert.NotNull(response);
        Assert.Equal(userId, response!.User.Id);
        Assert.Equal("alice.updated@example.com", response.User.Email);
        Assert.Equal("Alice Updated", response.User.DisplayName);
        Assert.Equal("entra-id", response.User.AuthProvider);
        Assert.Equal("alice", response.User.AuthSubject);
    }

    [Fact]
    public async Task GetCurrentUser_creates_distinct_internal_users_for_same_subject_and_email_across_providers()
    {
        using var firstClient = CreateAuthenticatedClient("shared-subject", "shared@example.com", "Shared User", "entra-id");
        using var secondClient = CreateAuthenticatedClient("shared-subject", "shared@example.com", "Shared User", "partner-saml");

        var firstResponse = await firstClient.GetFromJsonAsync<CurrentUserContextResponse>("/api/auth/me");
        var secondResponse = await secondClient.GetFromJsonAsync<CurrentUserContextResponse>("/api/auth/me");

        Assert.NotNull(firstResponse);
        Assert.NotNull(secondResponse);
        Assert.NotEqual(firstResponse!.User.Id, secondResponse!.User.Id);
        Assert.Equal("entra-id", firstResponse.User.AuthProvider);
        Assert.Equal("partner-saml", secondResponse.User.AuthProvider);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var users = await dbContext.Users.Where(x => x.Email == "shared@example.com").ToListAsync();

        Assert.Equal(2, users.Count);
        Assert.Contains(users, x => x.AuthProvider == "entra-id" && x.AuthSubject == "shared-subject");
        Assert.Contains(users, x => x.AuthProvider == "partner-saml" && x.AuthSubject == "shared-subject");
    }

    [Fact]
    public async Task GetCurrentUser_auto_resolves_single_active_membership()
    {
        var ids = await SeedSingleMembershipAsync(CompanyMembershipRole.Manager);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetFromJsonAsync<CurrentUserContextResponse>("/api/auth/me");

        Assert.NotNull(response);
        Assert.Single(response!.Memberships);
        Assert.False(response.CompanySelectionRequired);
        Assert.NotNull(response.ActiveCompany);
        Assert.Equal(ids.CompanyAId, response.ActiveCompany!.CompanyId);
        Assert.Equal("manager", response.ActiveCompany.Role);
        Assert.Equal("active", response.ActiveCompany.Status);
    }

    [Fact]
    public async Task GetCurrentUser_requires_selection_when_user_has_multiple_active_memberships()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetFromJsonAsync<CurrentUserContextResponse>("/api/auth/me");

        Assert.NotNull(response);
        Assert.Equal(3, response!.Memberships.Count);
        Assert.Null(response.ActiveCompany);
        Assert.True(response.CompanySelectionRequired);
    }

    [Fact]
    public async Task GetCurrentUser_resolves_requested_company_context_when_user_selects_one()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        client.DefaultRequestHeaders.Add(CompanyContextResolutionMiddleware.CompanyHeaderName, ids.CompanyBId.ToString());

        var response = await client.GetFromJsonAsync<CurrentUserContextResponse>("/api/auth/me");

        Assert.NotNull(response);
        Assert.NotNull(response!.ActiveCompany);
        Assert.Equal(ids.CompanyBId, response.ActiveCompany!.CompanyId);
        Assert.Equal("employee", response.ActiveCompany.Role);
        Assert.False(response.CompanySelectionRequired);
    }

    [Fact]
    public async Task GetMemberships_returns_current_users_memberships()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var memberships = await client.GetFromJsonAsync<List<MembershipResponse>>("/api/auth/memberships");

        Assert.NotNull(memberships);
        Assert.Equal(3, memberships!.Count);
        Assert.Contains(memberships, x => x.CompanyId == ids.CompanyAId && x.Role == "admin" && x.Status == "active");
        Assert.Contains(memberships, x => x.CompanyId == ids.CompanyBId && x.Role == "employee" && x.Status == "active");
        Assert.Contains(memberships, x => x.CompanyId == ids.CompanyPendingId && x.Role == "employee" && x.Status == "pending");
    }

    [Fact]
    public async Task SelectCompany_returns_header_contract_for_subsequent_tenant_scoped_requests()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.PostAsJsonAsync("/api/auth/select-company", new { CompanyId = ids.CompanyBId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<CompanySelectionResponse>();
        Assert.NotNull(payload);
        Assert.Equal(ids.CompanyBId, payload!.CompanyId);
        Assert.Equal(CompanyContextResolutionMiddleware.CompanyHeaderName, payload.HeaderName);
        Assert.Equal(ids.CompanyBId.ToString(), payload.HeaderValue);
        Assert.Equal(ids.CompanyBId, payload.ActiveCompany.CompanyId);
        Assert.Equal("employee", payload.ActiveCompany.Role);
        Assert.Equal("active", payload.ActiveCompany.Status);
    }

    [Fact]
    public async Task SelectCompany_forbids_company_selection_without_active_membership()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.PostAsJsonAsync("/api/auth/select-company", new { CompanyId = ids.CompanyNoMembershipId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Company_access_succeeds_for_active_membership()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyAId}/access");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Company_admin_policy_succeeds_for_admin_membership_in_requested_company()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyAId}/access/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Company_owned_resource_requires_authentication()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = _factory.CreateClient();
        var response = await client.GetAsync($"/api/companies/{ids.CompanyAId}/notes/{ids.CompanyANoteId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Company_access_is_forbidden_without_membership()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyNoMembershipId}/access");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Company_owned_resource_is_forbidden_without_membership()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyNoMembershipId}/notes/{ids.CompanyNoMembershipNoteId}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Company_owned_resource_returns_not_found_when_note_belongs_to_another_company()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyBId}/notes/{ids.CompanyANoteId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Company_admin_policy_is_tenant_specific_when_user_has_different_roles_per_company()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyBId}/access/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pending_membership_cannot_access_company_scoped_endpoint()
    {
        var ids = await SeedMultipleMembershipsAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyPendingId}/access");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public void CompanyMembership_role_and_status_use_canonical_storage_values()
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var roleConverter = dbContext.Model.FindEntityType(typeof(CompanyMembership))!
            .FindProperty(nameof(CompanyMembership.Role))!
            .GetTypeMapping().Converter;
        var statusConverter = dbContext.Model.FindEntityType(typeof(CompanyMembership))!
            .FindProperty(nameof(CompanyMembership.Status))!
            .GetTypeMapping().Converter;

        Assert.Equal("finance_approver", roleConverter!.ConvertToProvider(CompanyMembershipRole.FinanceApprover));
        Assert.Equal(CompanyMembershipRole.FinanceApprover, roleConverter.ConvertFromProvider("FinanceApprover"));
        Assert.Equal("active", statusConverter!.ConvertToProvider(CompanyMembershipStatus.Active));
        Assert.Equal(CompanyMembershipStatus.Active, statusConverter.ConvertFromProvider("Active"));
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName, string? provider = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        if (!string.IsNullOrWhiteSpace(provider))
        {
            client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.ProviderHeader, provider);
        }
        return client;
    }

    private async Task<SeedIds> SeedMultipleMembershipsAsync(CompanyMembershipRole adminRole)
    {
        var userId = Guid.NewGuid();
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var companyPendingId = Guid.NewGuid();
        var companyNoMembershipId = Guid.NewGuid();
        var companyANoteId = Guid.NewGuid();
        var companyBNoteId = Guid.NewGuid();
        var companyNoMembershipNoteId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "alice@example.com", "Alice", "dev-header", "alice"));
            dbContext.Companies.AddRange(
                new Company(companyAId, "Company A"),
                new Company(companyBId, "Company B"),
                new Company(companyPendingId, "Company Pending"),
                new Company(companyNoMembershipId, "Company No Membership"));

            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyAId, userId, adminRole, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyBId, userId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyPendingId, userId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Pending));

            dbContext.CompanyNotes.AddRange(
                new CompanyOwnedNote(companyANoteId, companyAId, "A note", "inside company A"),
                new CompanyOwnedNote(companyBNoteId, companyBId, "B note", "inside company B"),
                new CompanyOwnedNote(companyNoMembershipNoteId, companyNoMembershipId, "No membership note", "inside company without membership"));

            return Task.CompletedTask;
        });

        return new SeedIds(
            UserId: userId,
            CompanyAId: companyAId,
            CompanyBId: companyBId,
            CompanyPendingId: companyPendingId,
            CompanyNoMembershipId: companyNoMembershipId,
            CompanyANoteId: companyANoteId,
            CompanyBNoteId: companyBNoteId,
            CompanyNoMembershipNoteId: companyNoMembershipNoteId);
    }

    private async Task<SeedIds> SeedSingleMembershipAsync(CompanyMembershipRole role)
    {
        var userId = Guid.NewGuid();
        var companyAId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "alice@example.com", "Alice", "dev-header", "alice"));
            dbContext.Companies.Add(new Company(companyAId, "Company A"));
            dbContext.CompanyMemberships.Add(
                new CompanyMembership(Guid.NewGuid(), companyAId, userId, role, CompanyMembershipStatus.Active));

            return Task.CompletedTask;
        });

        return new SeedIds(
            UserId: userId,
            CompanyAId: companyAId,
            CompanyBId: Guid.Empty,
            CompanyPendingId: Guid.Empty,
            CompanyANoteId: Guid.Empty,
            CompanyBNoteId: Guid.Empty,
            CompanyNoMembershipId: Guid.Empty,
            CompanyNoMembershipNoteId: Guid.Empty);
    }

    private sealed record SeedIds(
        Guid UserId,
        Guid CompanyAId,
        Guid CompanyBId,
        Guid CompanyPendingId,
        Guid CompanyNoMembershipId,
        Guid CompanyANoteId,
        Guid CompanyBNoteId,
        Guid CompanyNoMembershipNoteId);

    private sealed class MembershipResponse
    {
        public Guid MembershipId { get; set; }
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private sealed class CurrentUserContextResponse
    {
        public CurrentUserResponse User { get; set; } = new();
        public List<MembershipResponse> Memberships { get; set; } = new();
        public ActiveCompanyResponse? ActiveCompany { get; set; }
        public bool CompanySelectionRequired { get; set; }
    }

    private sealed class CurrentUserResponse
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string AuthProvider { get; set; } = string.Empty;
        public string AuthSubject { get; set; } = string.Empty;
    }

    private sealed class ActiveCompanyResponse
    {
        public Guid MembershipId { get; set; }
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
    }

    private sealed class CompanySelectionResponse
    {
        public Guid CompanyId { get; set; }
        public string HeaderName { get; set; } = string.Empty;
        public string HeaderValue { get; set; } = string.Empty;
        public ActiveCompanyResponse ActiveCompany { get; set; } = new();
    }
}
