using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingOperationsApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Operations_endpoints_return_typed_readiness_and_idempotent_migration_state()
    {
        var seed = await SeedAsync();
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var initial = await owner.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/operations");
        using var started = await owner.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/operations/migrations",
            new { idempotencyKey = "accounting-operations-api:no-op" });
        using var replay = await owner.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/operations/migrations",
            new { idempotencyKey = "accounting-operations-api:no-op" });
        using var recovery = await owner.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/operations/recovery-verification",
            new { fiscalPeriodId = (Guid?)null, verifyObjectContent = true });

        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Equal(HttpStatusCode.OK, started.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);

        using var initialJson = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        using var startedJson = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        using var recoveryJson = JsonDocument.Parse(await recovery.Content.ReadAsStringAsync());
        Assert.Equal(seed.CompanyId, initialJson.RootElement.GetProperty("companyId").GetGuid());
        Assert.Equal("blocked", initialJson.RootElement.GetProperty("readiness").GetProperty("status").GetString());
        Assert.Equal("not_required", startedJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(startedJson.RootElement.GetProperty("id").GetGuid(), replayJson.RootElement.GetProperty("id").GetGuid());
        Assert.True(recoveryJson.RootElement.GetProperty("isValid").GetBoolean());

        Assert.Equal(1, await _factory.ExecuteDbContextAsync(db => db.AccountingMigrationRuns.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId)));
    }

    [Fact]
    public async Task Operations_enforce_accounting_permissions_and_company_membership()
    {
        var seed = await SeedAsync();
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var employeeRead = await employee.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/operations");
        using var employeeMigration = await employee.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/operations/migrations",
            new { idempotencyKey = "accounting-operations-api:forbidden" });
        using var crossTenantRead = await owner.GetAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/operations");
        using var crossTenantRecovery = await owner.PostAsJsonAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/operations/recovery-verification",
            new { fiscalPeriodId = (Guid?)null, verifyObjectContent = false });

        Assert.Equal(HttpStatusCode.Forbidden, employeeRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeMigration.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantRecovery.StatusCode);
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var unownedCompanyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        const string ownerSubject = "accounting-operations-owner";
        const string ownerEmail = "accounting-operations-owner@example.com";
        const string employeeSubject = "accounting-operations-employee";
        const string employeeEmail = "accounting-operations-employee@example.com";

        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(
                new User(ownerId, ownerEmail, "Accounting Owner", "dev-header", ownerSubject),
                new User(employeeId, employeeEmail, "Accounting Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(
                new Company(companyId, "Accounting operations company"),
                new Company(unownedCompanyId, "Unowned operations company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new Seed(companyId, unownedCompanyId, ownerSubject, ownerEmail, employeeSubject, employeeEmail);
    }

    private HttpClient CreateClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private sealed record Seed(
        Guid CompanyId,
        Guid UnownedCompanyId,
        string OwnerSubject,
        string OwnerEmail,
        string EmployeeSubject,
        string EmployeeEmail);
}
