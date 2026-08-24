using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingCapacityApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Capacity_and_retention_routes_are_company_scoped_and_permission_guarded()
    {
        var seed = await SeedAsync();
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);

        using var read = await owner.GetAsync($"{Route(seed.CompanyId)}?profile=medium");
        using var crossCompanyRead = await owner.GetAsync(Route(seed.UnownedCompanyId));
        using var employeePreview = await employee.PostAsJsonAsync($"{Route(seed.CompanyId)}/retention/preview",
            new { batchSize = 10 });
        using var ownerPreview = await owner.PostAsJsonAsync($"{Route(seed.CompanyId)}/retention/preview",
            new { batchSize = 10 });

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeePreview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ownerPreview.StatusCode);

        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
        Assert.Equal(seed.CompanyId, readJson.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal("medium", readJson.RootElement.GetProperty("profileKey").GetString());
        Assert.Contains(readJson.RootElement.GetProperty("retentionClasses").EnumerateArray(),
            item => item.GetProperty("key").GetString() == "accounting_truth" &&
                    item.GetProperty("mode").GetString() == "preserve");

        using var previewJson = JsonDocument.Parse(await ownerPreview.Content.ReadAsStringAsync());
        Assert.Equal(seed.CompanyId, previewJson.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal(0, previewJson.RootElement.GetProperty("eligibleCount").GetInt64());
        Assert.Equal(64, previewJson.RootElement.GetProperty("previewToken").GetString()!.Length);
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var unownedCompanyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        const string ownerSubject = "accounting-capacity-owner";
        const string ownerEmail = "accounting-capacity-owner@example.com";
        const string employeeSubject = "accounting-capacity-employee";
        const string employeeEmail = "accounting-capacity-employee@example.com";

        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(
                new User(ownerId, ownerEmail, "Accounting capacity owner", "dev-header", ownerSubject),
                new User(employeeId, employeeEmail, "Accounting capacity employee", "dev-header", employeeSubject));
            db.Companies.AddRange(new Company(companyId, "Capacity company"),
                new Company(unownedCompanyId, "Unowned capacity company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner,
                    CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee,
                    CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new Seed(companyId, unownedCompanyId, ownerSubject, ownerEmail, employeeSubject, employeeEmail);
    }

    private static string Route(Guid companyId) =>
        $"/api/companies/{companyId:D}/finance/accounting-capacity";

    private HttpClient CreateClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed record Seed(Guid CompanyId, Guid UnownedCompanyId, string OwnerSubject,
        string OwnerEmail, string EmployeeSubject, string EmployeeEmail);
}
