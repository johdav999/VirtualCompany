using System.Net;

namespace VirtualCompany.Api.Tests;

public sealed class YearEndRolloverAuthorizationIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Governance_list_requires_allowed_role_in_exact_route_company()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(
                new User(ownerId, "year-end-owner@example.com", "Year-end owner", "dev-header", "year-end-owner"),
                new User(employeeId, "year-end-employee@example.com", "Year-end employee", "dev-header", "year-end-employee"));
            db.Companies.AddRange(new Company(companyId, "Year-end company"), new Company(otherCompanyId, "Other company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        using var owner = Client("year-end-owner", "year-end-owner@example.com");
        using var employee = Client("year-end-employee", "year-end-employee@example.com");
        using var allowed = await owner.GetAsync($"/api/companies/{companyId:D}/finance/year-end-runs");
        using var employeeDenied = await employee.GetAsync($"/api/companies/{companyId:D}/finance/year-end-runs");
        using var crossTenantDenied = await owner.GetAsync($"/api/companies/{otherCompanyId:D}/finance/year-end-runs");

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeDenied.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantDenied.StatusCode);
    }

    private HttpClient Client(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }
}
