using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class VatReturnApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Filing_period_admin_is_authorized_tenant_scoped_audited_and_rejects_overlap()
    {
        var seed = await SeedAsync();
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var employee = Client(seed.EmployeeSubject, seed.EmployeeEmail);
        var request = new { periodCode = "2026-08", startDate = new DateOnly(2026, 8, 1),
            endDate = new DateOnly(2026, 8, 31), currency = "SEK", fiscalPeriodId = seed.FiscalPeriodId };

        using var forbidden = await employee.PostAsJsonAsync(Route(seed.CompanyId, "filing-periods"), request);
        using var crossTenant = await owner.PostAsJsonAsync(Route(seed.UnownedCompanyId, "filing-periods"),
            new { periodCode = "2026-08", startDate = new DateOnly(2026, 8, 1),
                endDate = new DateOnly(2026, 8, 31), currency = "SEK", fiscalPeriodId = (Guid?)null });
        using var crossTenantRead = await owner.GetAsync(Route(seed.UnownedCompanyId, "filing-periods"));
        using var created = await owner.PostAsJsonAsync(Route(seed.CompanyId, "filing-periods"), request);
        using var overlap = await owner.PostAsJsonAsync(Route(seed.CompanyId, "filing-periods"),
            new { periodCode = "2026-08-B", startDate = new DateOnly(2026, 8, 15),
                endDate = new DateOnly(2026, 9, 15), currency = "SEK", fiscalPeriodId = (Guid?)null });
        using var overlapJson = JsonDocument.Parse(await overlap.Content.ReadAsStringAsync());
        using var list = await owner.GetAsync(Route(seed.CompanyId, "filing-periods"));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantRead.StatusCode);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);
        Assert.Equal(VatReturnIssueCodes.FilingPeriodAmbiguous,
            overlapJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Single(await _factory.ExecuteDbContextAsync(db => db.VatFilingPeriods.IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.CompanyId).ToListAsync()));
        Assert.Single(await _factory.ExecuteDbContextAsync(db => db.AuditEvents.IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.CompanyId && x.Action ==
                VirtualCompany.Application.Auditing.AuditEventActions.VatFilingPeriodCreated).ToListAsync()));
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid(); var unowned = Guid.NewGuid(); var ownerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid(); var fiscalId = Guid.NewGuid();
        const string ownerSubject = "vat-owner"; const string ownerEmail = "vat-owner@example.com";
        const string employeeSubject = "vat-employee"; const string employeeEmail = "vat-employee@example.com";
        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(new User(ownerId, ownerEmail, "Owner", "dev-header", ownerSubject),
                new User(employeeId, employeeEmail, "Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(new Company(companyId, "VAT Company"), new Company(unowned, "Unowned"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(fiscalId, companyId, "August 2026",
                new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
            return Task.CompletedTask;
        });
        return new(companyId, unowned, fiscalId, ownerSubject, ownerEmail, employeeSubject, employeeEmail);
    }

    private HttpClient Client(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private static string Route(Guid companyId, string suffix) =>
        $"/internal/companies/{companyId:D}/finance/accounting/vat/{suffix}";
    private sealed record Seed(Guid CompanyId, Guid UnownedCompanyId, Guid FiscalPeriodId,
        string OwnerSubject, string OwnerEmail, string EmployeeSubject, string EmployeeEmail);
}
