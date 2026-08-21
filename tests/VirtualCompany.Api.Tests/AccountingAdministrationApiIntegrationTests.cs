using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingAdministrationApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Owner_can_preview_complete_and_replay_default_setup_without_duplicates()
    {
        var seed = await SeedAsync();
        using var client = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        var request = SetupRequest(seed.CompanyId);

        using var preview = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/setup/preview",
            request);
        using var completed = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/setup/complete",
            request);
        using var replay = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/setup/complete",
            request);

        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var previewJson = JsonDocument.Parse(await preview.Content.ReadAsStringAsync());
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.True(previewJson.RootElement.GetProperty("isValid").GetBoolean());
        Assert.True(previewJson.RootElement.GetProperty("isCountryNeutral").GetBoolean());
        Assert.False(previewJson.RootElement.GetProperty("isStatutoryComplianceValidated").GetBoolean());
        Assert.True(replayJson.RootElement.GetProperty("wasAlreadyApplied").GetBoolean());

        var counts = await _factory.ExecuteDbContextAsync(async db => new
        {
            Configurations = await db.AccountingConfigurations.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            Accounts = await db.FinanceAccounts.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.AccountClass != null),
            Roles = await db.AccountingConfigurationAccountRoles.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            Periods = await db.FiscalPeriods.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            Series = await db.VoucherSeries.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            SetupAudits = await db.AuditEvents.IgnoreQueryFilters().CountAsync(x =>
                x.CompanyId == seed.CompanyId && x.Action == AuditEventActions.AccountingSetupCompleted)
        });
        Assert.Equal(1, counts.Configurations);
        Assert.Equal(6, counts.Accounts);
        Assert.Equal(6, counts.Roles);
        Assert.Equal(12, counts.Periods);
        Assert.Equal(5, counts.Series);
        Assert.Equal(1, counts.SetupAudits);
    }

    [Fact]
    public async Task Account_and_period_administration_enforce_protection_and_return_real_state()
    {
        var seed = await SeedAsync();
        using var client = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var setup = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/setup/complete",
            SetupRequest(seed.CompanyId));
        setup.EnsureSuccessStatusCode();

        using var accounts = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/accounts");
        using var accountsJson = JsonDocument.Parse(await accounts.Content.ReadAsStringAsync());
        var protectedAccount = accountsJson.RootElement.EnumerateArray().First(item => item.GetProperty("isProtected").GetBoolean());
        using var protectedDeactivate = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/accounts/{protectedAccount.GetProperty("id").GetGuid():D}/deactivate",
            new
            {
                effectiveTo = new DateOnly(2026, 12, 31),
                expectedUpdatedUtc = protectedAccount.GetProperty("updatedUtc").GetDateTime()
            });

        using var created = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/accounts",
            new { code = "5100", name = "Professional services", accountClass = "expense", normalBalance = "debit", effectiveFrom = new DateOnly(2026, 1, 1) });
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var customId = createdJson.RootElement.GetProperty("id").GetGuid();
        using var renamed = await client.PutAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/accounts/{customId:D}/name",
            new { name = "Advisory services", expectedUpdatedUtc = createdJson.RootElement.GetProperty("updatedUtc").GetDateTime() });
        using var renamedJson = JsonDocument.Parse(await renamed.Content.ReadAsStringAsync());
        using var deactivated = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/accounts/{customId:D}/deactivate",
            new { effectiveTo = new DateOnly(2026, 12, 31), expectedUpdatedUtc = renamedJson.RootElement.GetProperty("updatedUtc").GetDateTime() });
        using var periods = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/fiscal-years");

        Assert.Equal(HttpStatusCode.OK, accounts.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, protectedDeactivate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, periods.StatusCode);
        using var deactivatedJson = JsonDocument.Parse(await deactivated.Content.ReadAsStringAsync());
        using var periodsJson = JsonDocument.Parse(await periods.Content.ReadAsStringAsync());
        Assert.False(deactivatedJson.RootElement.GetProperty("isPostingEnabled").GetBoolean());
        Assert.Equal("Advisory services", deactivatedJson.RootElement.GetProperty("name").GetString());
        Assert.Equal(12, periodsJson.RootElement[0].GetProperty("periods").GetArrayLength());
    }

    [Fact]
    public async Task Accounting_admin_mutations_are_denied_without_permission_or_company_membership()
    {
        var seed = await SeedAsync();
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var employeeSetup = await employee.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/setup/complete",
            SetupRequest(seed.CompanyId));
        using var crossTenantRead = await owner.GetAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/accounts");
        using var crossTenantPeriodCreate = await owner.PostAsJsonAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/fiscal-years",
            new { fiscalYearStart = new DateOnly(2027, 1, 1) });

        Assert.Equal(HttpStatusCode.Forbidden, employeeSetup.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantPeriodCreate.StatusCode);
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var unownedCompanyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        const string ownerSubject = "accounting-admin-owner";
        const string ownerEmail = "accounting-admin-owner@example.com";
        const string employeeSubject = "accounting-admin-employee";
        const string employeeEmail = "accounting-admin-employee@example.com";

        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(
                new User(ownerId, ownerEmail, "Accounting Owner", "dev-header", ownerSubject),
                new User(employeeId, employeeEmail, "Accounting Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(
                new Company(companyId, "Accounting administration company"),
                new Company(unownedCompanyId, "Unowned accounting company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new Seed(companyId, unownedCompanyId, ownerSubject, ownerEmail, employeeSubject, employeeEmail);
    }

    private static object SetupRequest(Guid companyId) => new
    {
        baseCurrency = "USD",
        fiscalYearStart = new DateOnly(2026, 1, 1),
        policyPackKey = "country-neutral",
        policyPackVersion = "1.0.0",
        chartTemplateKey = "generic-accrual",
        accountRoleCodeAssignments = new Dictionary<string, string>(),
        idempotencyKey = $"accounting-setup:{companyId:N}"
    };

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
