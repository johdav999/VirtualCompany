using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
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
    public async Task GetMemberships_returns_current_users_memberships()
    {
        var ids = await SeedAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var memberships = await client.GetFromJsonAsync<List<MembershipResponse>>("/api/auth/memberships");

        Assert.NotNull(memberships);
        Assert.Equal(2, memberships!.Count);
        Assert.Contains(memberships, x => x.CompanyId == ids.CompanyAId && x.Status == "Active");
        Assert.Contains(memberships, x => x.CompanyId == ids.CompanyPendingId && x.Status == "Pending");
    }

    [Fact]
    public async Task Company_access_succeeds_for_active_membership()
    {
        var ids = await SeedAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyAId}/access");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Company_access_is_forbidden_without_membership()
    {
        var ids = await SeedAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyNoMembershipId}/access");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Company_admin_policy_reads_persisted_membership_role()
    {
        var ids = await SeedAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyAId}/access/admin");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Company_admin_policy_rejects_non_admin_membership_role()
    {
        var ids = await SeedAsync(adminRole: CompanyMembershipRole.Employee);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyAId}/access/admin");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Company_owned_query_filters_by_company_id()
    {
        var ids = await SeedAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyAId}/notes/{ids.CompanyBNoteId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Pending_membership_cannot_access_company_scoped_endpoint()
    {
        var ids = await SeedAsync(adminRole: CompanyMembershipRole.Admin);

        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.GetAsync($"/api/companies/{ids.CompanyPendingId}/access");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private async Task<SeedIds> SeedAsync(CompanyMembershipRole adminRole)
    {
        var userId = Guid.NewGuid();
        var companyAId = Guid.NewGuid();
        var companyBId = Guid.NewGuid();
        var companyPendingId = Guid.NewGuid();
        var companyNoMembershipId = Guid.NewGuid();
        var companyANoteId = Guid.NewGuid();
        var companyBNoteId = Guid.NewGuid();

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
                new CompanyOwnedNote(companyBNoteId, companyBId, "B note", "inside company B"));

            return Task.CompletedTask;
        });

        return new SeedIds(
            UserId: userId,
            CompanyAId: companyAId,
            CompanyBId: companyBId,
            CompanyPendingId: companyPendingId,
            CompanyNoMembershipId: companyNoMembershipId,
            CompanyANoteId: companyANoteId,
            CompanyBNoteId: companyBNoteId);
    }

    private sealed record SeedIds(
        Guid UserId,
        Guid CompanyAId,
        Guid CompanyBId,
        Guid CompanyPendingId,
        Guid CompanyNoMembershipId,
        Guid CompanyANoteId,
        Guid CompanyBNoteId);

    private sealed class MembershipResponse
    {
        public Guid CompanyId { get; set; }
        public string Status { get; set; } = string.Empty;
    }
}