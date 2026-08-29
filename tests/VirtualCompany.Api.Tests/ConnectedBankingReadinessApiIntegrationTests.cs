using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;

namespace VirtualCompany.Api.Tests;

public sealed class ConnectedBankingReadinessApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Readiness_route_is_permission_guarded_and_rejects_cross_company_access()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, seed.Subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, seed.Email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, "Connected banking owner");

        using var owned = await client.GetAsync($"{Route(seed.CompanyId)}?profile=medium");
        using var unowned = await client.GetAsync(Route(seed.UnownedCompanyId));
        using var recovery = await client.PostAsJsonAsync($"{Route(seed.CompanyId)}/recovery-verification",
            new { verifyObjectContent = false, correlationId = "api-restore-drill" });

        Assert.Equal(HttpStatusCode.OK, owned.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, unowned.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        using var json = JsonDocument.Parse(await owned.Content.ReadAsStringAsync());
        Assert.Equal(seed.CompanyId, json.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal("medium", json.RootElement.GetProperty("profileKey").GetString());
        Assert.False(json.RootElement.GetProperty("isReady").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("checks").EnumerateArray(), check =>
            check.GetProperty("key").GetString() == "control_account_differences" &&
            check.GetProperty("status").GetString() == "not_measured");
        using var recoveryJson = JsonDocument.Parse(await recovery.Content.ReadAsStringAsync());
        Assert.True(recoveryJson.RootElement.GetProperty("isValid").GetBoolean());
        Assert.Equal(64, recoveryJson.RootElement.GetProperty("evidenceChecksum").GetString()!.Length);
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var unownedCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        const string subject = "connected-banking-readiness-owner";
        const string email = "connected-banking-readiness-owner@example.com";
        await _factory.SeedAsync(db =>
        {
            db.Users.Add(new User(userId, email, "Connected banking owner", "dev-header", subject));
            db.Companies.AddRange(new Company(companyId, "Connected banking company"),
                new Company(unownedCompanyId, "Unowned connected banking company"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId,
                CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });
        return new Seed(companyId, unownedCompanyId, subject, email);
    }

    private static string Route(Guid companyId) =>
        $"/api/companies/{companyId:D}/finance/connected-banking-readiness";

    private sealed record Seed(Guid CompanyId, Guid UnownedCompanyId, string Subject, string Email);
}
