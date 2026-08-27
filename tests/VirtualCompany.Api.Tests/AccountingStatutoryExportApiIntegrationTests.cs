using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingStatutoryExportApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Export_requests_are_authorized_tenant_scoped_backward_compatible_and_return_stable_capability_gaps()
    {
        var seed = await SeedAsync();
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var employee = Client(seed.EmployeeSubject, seed.EmployeeEmail);
        var route = $"/internal/companies/{seed.CompanyId:D}/finance/accounting/exports";

        using var generic = await owner.PostAsJsonAsync(route,
            new { fiscalPeriodId = seed.FiscalPeriodId, idempotencyKey = "generic:1" });
        using var genericJson = JsonDocument.Parse(await generic.Content.ReadAsStringAsync());
        using var statutory = await owner.PostAsJsonAsync(route,
            new { fiscalPeriodId = seed.FiscalPeriodId, idempotencyKey = "sie:1", exportType = "sie_4b" });
        using var statutoryJson = JsonDocument.Parse(await statutory.Content.ReadAsStringAsync());
        using var unsupported = await owner.PostAsJsonAsync(route,
            new { fiscalPeriodId = seed.FiscalPeriodId, idempotencyKey = "unsupported:1", exportType = "not_sie" });
        using var unsupportedJson = JsonDocument.Parse(await unsupported.Content.ReadAsStringAsync());
        using var conflict = await owner.PostAsJsonAsync(route,
            new { fiscalPeriodId = seed.FiscalPeriodId, idempotencyKey = "generic:1", exportType = "generic_csv" });
        using var conflictJson = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        using var forbidden = await employee.PostAsJsonAsync(route,
            new { fiscalPeriodId = seed.FiscalPeriodId, idempotencyKey = "generic:employee" });
        using var crossTenant = await owner.PostAsJsonAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/exports",
            new { fiscalPeriodId = seed.FiscalPeriodId, idempotencyKey = "generic:cross" });
        using var crossTenantList = await owner.GetAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/exports");
        var ownedExportId = genericJson.RootElement.GetProperty("id").GetGuid();
        using var crossTenantDownload = await owner.GetAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/exports/{ownedExportId:D}/download");

        Assert.Equal(HttpStatusCode.OK, generic.StatusCode);
        Assert.Equal(AccountingExportTypeValues.GenericJson, genericJson.RootElement.GetProperty("exportType").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, statutory.StatusCode);
        Assert.Equal(SieReasonCodes.IncompletePolicyHistory, statutoryJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Equal("accounting_export_type_unsupported", unsupportedJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("accounting_export_idempotency_conflict", conflictJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantList.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantDownload.StatusCode);
        Assert.Single(await _factory.ExecuteDbContextAsync(db => db.AccountingExportJobs.IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.CompanyId).ToListAsync()));
        Assert.Empty(await _factory.ExecuteDbContextAsync(db => db.AccountingExportJobs.IgnoreQueryFilters()
            .Where(x => x.CompanyId == seed.UnownedCompanyId).ToListAsync()));
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid(); var unowned = Guid.NewGuid(); var ownerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid(); var fiscalPeriodId = Guid.NewGuid();
        const string ownerSubject = "export-owner"; const string ownerEmail = "export-owner@example.com";
        const string employeeSubject = "export-employee"; const string employeeEmail = "export-employee@example.com";
        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(new User(ownerId, ownerEmail, "Owner", "dev-header", ownerSubject),
                new User(employeeId, employeeEmail, "Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(new Company(companyId, "Export company"), new Company(unowned, "Unowned"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.FiscalPeriods.Add(new FiscalPeriod(fiscalPeriodId, companyId, "2026",
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            return Task.CompletedTask;
        });
        return new(companyId, unowned, fiscalPeriodId, ownerSubject, ownerEmail, employeeSubject, employeeEmail);
    }

    private HttpClient Client(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private static class SieReasonCodes
    {
        public const string IncompletePolicyHistory = "sie_incomplete_policy_history";
    }

    private sealed record Seed(Guid CompanyId, Guid UnownedCompanyId, Guid FiscalPeriodId,
        string OwnerSubject, string OwnerEmail, string EmployeeSubject, string EmployeeEmail);
}
