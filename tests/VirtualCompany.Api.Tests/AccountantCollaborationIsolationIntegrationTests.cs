using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;

namespace VirtualCompany.Api.Tests;

public sealed class AccountantCollaborationIsolationIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Explicit_grant_isolates_portfolio_and_routes_then_revocation_blocks_old_link_but_retains_history()
    {
        var seed = await SeedAsync();
        using var client = CreateClient(seed.Subject, seed.Email);

        using var portfolio = await client.GetAsync("/api/accountant/portfolio");
        using var granted = await client.GetAsync(EngagementRoute(seed.GrantedCompanyId, seed.EngagementId));
        using var guessedCompany = await client.GetAsync(EngagementRoute(seed.UngrantedCompanyId, seed.EngagementId));

        Assert.Equal(HttpStatusCode.OK, portfolio.StatusCode);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, guessedCompany.StatusCode);
        using (var json = JsonDocument.Parse(await portfolio.Content.ReadAsStringAsync()))
        {
            Assert.Equal(1, json.RootElement.GetProperty("activeCompanyCount").GetInt32());
            var company = Assert.Single(json.RootElement.GetProperty("companies").EnumerateArray());
            Assert.Equal(seed.GrantedCompanyId, company.GetProperty("companyId").GetGuid());
        }

        await _factory.ExecuteDbContextAsync(async db =>
        {
            var grant = await db.AccountantCompanyGrants.IgnoreQueryFilters().SingleAsync(x => x.Id == seed.GrantId);
            grant.Revoke(Guid.NewGuid(), "Engagement completed", DateTime.UtcNow);
            await db.SaveChangesAsync();
        });

        using var revokedDeepLink = await client.GetAsync(EngagementRoute(seed.GrantedCompanyId, seed.EngagementId));
        Assert.Equal(HttpStatusCode.Forbidden, revokedDeepLink.StatusCode);
        Assert.Equal(1, await _factory.ExecuteDbContextAsync(db => db.AccountantReviewHistory.IgnoreQueryFilters()
            .CountAsync(x => x.EngagementId == seed.EngagementId)));
    }

    [Fact]
    public async Task Accountant_cannot_sign_off_an_engagement_they_prepared()
    {
        var seed = await SeedAsync();
        using var client = CreateClient(seed.Subject, seed.Email);

        using var response = await client.PostAsJsonAsync(
            $"{EngagementRoute(seed.GrantedCompanyId, seed.EngagementId)}/sign-off",
            new { conclusion = "Ready for independent approval.", expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("self_signoff_forbidden", json.RootElement.GetProperty("reasonCode").GetString());
        Assert.Equal(0, await _factory.ExecuteDbContextAsync(db => db.AccountantEngagementSignOffs.IgnoreQueryFilters()
            .CountAsync(x => x.EngagementId == seed.EngagementId)));
    }

    private async Task<Seed> SeedAsync()
    {
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var grantedCompanyId = Guid.NewGuid();
        var ungrantedCompanyId = Guid.NewGuid();
        var grantedMembershipId = Guid.NewGuid();
        var grantId = Guid.NewGuid();
        var engagementId = Guid.NewGuid();
        const string subject = "external-accountant-isolation";
        const string email = "external-accountant-isolation@example.com";

        await _factory.SeedAsync(db =>
        {
            db.Users.Add(new User(userId, email, "External accountant", "dev-header", subject));
            db.Companies.AddRange(new Company(grantedCompanyId, "Granted company"),
                new Company(ungrantedCompanyId, "Ungranted company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(grantedMembershipId, grantedCompanyId, userId,
                    CompanyMembershipRole.Accountant, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), ungrantedCompanyId, userId,
                    CompanyMembershipRole.Accountant, CompanyMembershipStatus.Active));

            var grant = new AccountantCompanyGrant(grantId, grantedCompanyId, grantedMembershipId, userId,
                AccountantGrantScopes.AccountingReview, true, true, true, now.AddMinutes(-5), now.AddDays(30),
                Guid.NewGuid(), now.AddMinutes(-10));
            grant.Approve(Guid.NewGuid(), now.AddMinutes(-9));
            db.AccountantCompanyGrants.Add(grant);
            db.AccountantReviewEngagements.Add(new AccountantReviewEngagement(engagementId, grantedCompanyId,
                grantId, null, "Month-end review", "close_review", userId, userId, now.AddDays(7), now));
            db.AccountantReviewHistory.Add(new AccountantReviewHistory(Guid.NewGuid(), grantedCompanyId,
                engagementId, "engagement_created", "engagement", engagementId, userId,
                "Review engagement created.", now));
            return Task.CompletedTask;
        });

        return new Seed(userId, grantedCompanyId, ungrantedCompanyId, grantId, engagementId, subject, email);
    }

    private HttpClient CreateClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, "External accountant");
        return client;
    }

    private static string EngagementRoute(Guid companyId, Guid engagementId) =>
        $"/api/companies/{companyId:D}/accountant-collaboration/engagements/{engagementId:D}";

    private sealed record Seed(Guid UserId, Guid GrantedCompanyId, Guid UngrantedCompanyId, Guid GrantId,
        Guid EngagementId, string Subject, string Email);
}
