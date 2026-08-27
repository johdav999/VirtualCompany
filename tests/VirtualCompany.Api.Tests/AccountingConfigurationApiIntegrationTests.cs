using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Api.Controllers;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingConfigurationApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Owner_can_create_read_and_version_update_statutory_profile_while_stale_write_is_rejected()
    {
        var seed = await SeedCompaniesAsync();
        using var client = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var created = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/statutory-profile",
            StatutoryProfileRequest());
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var version = createdJson.RootElement.GetProperty("profile").GetProperty("version").GetInt64();

        var updateRequest = StatutoryProfileRequest();
        updateRequest.ExpectedVersion = version;
        updateRequest.LegalName = "Updated Legal AB";
        using var updated = await client.PutAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/statutory-profile", updateRequest);
        using var read = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/statutory-profile");

        var staleRequest = StatutoryProfileRequest();
        staleRequest.ExpectedVersion = version;
        staleRequest.LegalName = "Stale Legal AB";
        using var stale = await client.PutAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/statutory-profile", staleRequest);
        using var staleJson = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(CompanyStatutoryProfileReasonCodes.ConcurrencyConflict, staleJson.RootElement.GetProperty("code").GetString());
        Assert.Equal("Updated Legal AB", readJson.RootElement.GetProperty("profile").GetProperty("legalName").GetString());
        Assert.False(readJson.RootElement.GetProperty("isExternallyVerified").GetBoolean());

        var auditActions = await _factory.ExecuteDbContextAsync(db => db.AuditEvents.IgnoreQueryFilters()
            .Where(item => item.CompanyId == seed.CompanyId && item.TargetType == AuditTargetTypes.CompanyStatutoryProfile)
            .Select(item => item.Action)
            .ToListAsync());
        Assert.Equal(2, auditActions.Count);
        Assert.Contains(AuditEventActions.CompanyStatutoryProfileCreated, auditActions);
        Assert.Contains(AuditEventActions.CompanyStatutoryProfileUpdated, auditActions);
    }

    [Fact]
    public async Task Statutory_profile_endpoints_enforce_admin_and_company_scope()
    {
        var seed = await SeedCompaniesAsync();
        using var employee = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var employeeCreate = await employee.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/statutory-profile", StatutoryProfileRequest());
        using var crossCompanyRead = await owner.GetAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/statutory-profile");
        using var crossCompanyWrite = await owner.PostAsJsonAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/statutory-profile", StatutoryProfileRequest());

        Assert.Equal(HttpStatusCode.Forbidden, employeeCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossCompanyWrite.StatusCode);
        Assert.Equal(0, await _factory.ExecuteDbContextAsync(db => db.CompanyStatutoryProfiles.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Swedish_company_without_profile_gets_explicit_missing_facts_without_mutation()
    {
        var seed = await SeedCompaniesAsync();
        await _factory.ExecuteDbContextAsync(async db =>
        {
            var company = await db.Companies.SingleAsync(item => item.Id == seed.SecondOwnedCompanyId);
            company.UpdateWorkspaceProfile(company.Name, null, null, "Europe/Stockholm", "SEK", "sv-SE", "SE");
            await db.SaveChangesAsync();
        });
        using var client = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var response = await client.GetAsync(
            $"/internal/companies/{seed.SecondOwnedCompanyId:D}/finance/accounting/setup-status");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(json.RootElement.GetProperty("isConfigured").GetBoolean());
        Assert.Contains(json.RootElement.GetProperty("missingLegalFacts").EnumerateArray(),
            item => item.GetString() == StatutoryProfileFactKeys.OrganisationNumber);
        Assert.Equal(0, await _factory.ExecuteDbContextAsync(db => db.CompanyStatutoryProfiles.IgnoreQueryFilters()
            .CountAsync(item => item.CompanyId == seed.SecondOwnedCompanyId)));
    }

    [Fact]
    public async Task Owner_can_create_country_neutral_internal_ledger_configuration_without_a_provider()
    {
        var seed = await SeedCompaniesAsync();
        using var client = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/configuration",
            new
            {
                baseCurrency = "usd",
                fiscalYearStartMonth = 1,
                fiscalYearStartDay = 1,
                roundingPrecision = 2,
                roundingMode = "midpoint_to_even"
            });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.True(root.GetProperty("isConfigured").GetBoolean());
        Assert.True(root.GetProperty("canUseInternalLedger").GetBoolean());
        Assert.Equal("internal_ledger", root.GetProperty("authority").GetString());
        Assert.Equal("USD", root.GetProperty("configuration").GetProperty("baseCurrency").GetString());
        Assert.Equal("country-neutral", root.GetProperty("configuration").GetProperty("policyPackKey").GetString());
        Assert.False(root.GetProperty("isCountrySpecificComplianceConfigured").GetBoolean());
        Assert.Contains(
            root.GetProperty("warnings").EnumerateArray(),
            warning => warning.GetProperty("reasonCode").GetString() == AccountingConfigurationReasonCodes.CountrySpecificCapabilityUnavailable);

        var persisted = await _factory.ExecuteDbContextAsync(db => db.AccountingConfigurations
            .IgnoreQueryFilters()
            .SingleAsync(configuration => configuration.CompanyId == seed.CompanyId));
        var auditActions = await _factory.ExecuteDbContextAsync(db => db.AuditEvents
            .IgnoreQueryFilters()
            .Where(audit => audit.CompanyId == seed.CompanyId)
            .Select(audit => audit.Action)
            .ToListAsync());

        Assert.Equal(AccountingAuthorityValues.InternalLedger, persisted.Authority);
        Assert.Contains(AuditEventActions.AccountingConfigurationCreated, auditActions);
        Assert.Contains(AuditEventActions.AccountingPolicyPackSelected, auditActions);
    }

    [Fact]
    public async Task Setup_validation_and_country_specific_capability_are_safe_and_explicit()
    {
        var seed = await SeedCompaniesAsync();
        using var client = CreateClient(seed.OwnerSubject, seed.OwnerEmail);
        using var createResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/configuration",
            new { baseCurrency = "EUR", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, roundingPrecision = 2 });
        createResponse.EnsureSuccessStatusCode();

        using var statusResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/setup-status");
        using var validationResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/accounting/validation");
        using var capabilityResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/capabilities/{AccountingPolicyCapabilityKeys.CountrySpecificReporting}");
        using var capabilityJson = JsonDocument.Parse(await capabilityResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, validationResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, capabilityResponse.StatusCode);
        Assert.False(capabilityJson.RootElement.GetProperty("isAvailable").GetBoolean());
        Assert.Equal(
            AccountingConfigurationReasonCodes.CountrySpecificCapabilityUnavailable,
            capabilityJson.RootElement.GetProperty("reasonCode").GetString());
        Assert.Contains("No rules were guessed", capabilityJson.RootElement.GetProperty("explanation").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_policy_pack_is_rejected_with_stable_reason_and_no_mutation()
    {
        var seed = await SeedCompaniesAsync();
        using var client = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.SecondOwnedCompanyId:D}/finance/accounting/configuration",
            new
            {
                baseCurrency = "USD",
                fiscalYearStartMonth = 1,
                fiscalYearStartDay = 1,
                policyPackKey = "unknown-pack",
                policyPackVersion = "9.9.9",
                roundingPrecision = 2
            });
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AccountingConfigurationReasonCodes.UnsupportedPackVersion, json.RootElement.GetProperty("code").GetString());
        var count = await _factory.ExecuteDbContextAsync(db => db.AccountingConfigurations
            .IgnoreQueryFilters()
            .CountAsync(configuration => configuration.CompanyId == seed.SecondOwnedCompanyId));
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task Accounting_endpoints_enforce_edit_authorization_and_tenant_membership()
    {
        var seed = await SeedCompaniesAsync();
        using var employeeClient = CreateClient(seed.EmployeeSubject, seed.EmployeeEmail);
        using var ownerClient = CreateClient(seed.OwnerSubject, seed.OwnerEmail);

        using var employeeCreate = await employeeClient.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/configuration",
            new { baseCurrency = "USD", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, roundingPrecision = 2 });
        using var employeePreview = await employeeClient.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/policy-pack/preview",
            new { policyPackKey = "country-neutral", policyPackVersion = "1.0.0", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) });
        using var employeeApply = await employeeClient.PutAsJsonAsync(
            $"/internal/companies/{seed.CompanyId:D}/finance/accounting/policy-pack",
            new { policyPackKey = "country-neutral", policyPackVersion = "1.0.0", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), expectedVersion = 1 });
        using var crossTenantRead = await ownerClient.GetAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/setup-status");
        using var crossTenantValidation = await ownerClient.GetAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/validation");
        using var crossTenantCapability = await ownerClient.GetAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/capabilities/{AccountingPolicyCapabilityKeys.CountrySpecificReporting}");
        using var crossTenantWrite = await ownerClient.PostAsJsonAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/configuration",
            new { baseCurrency = "USD", fiscalYearStartMonth = 1, fiscalYearStartDay = 1, roundingPrecision = 2 });
        using var crossTenantPreview = await ownerClient.PostAsJsonAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/policy-pack/preview",
            new { policyPackKey = "country-neutral", policyPackVersion = "1.0.0", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1) });
        using var crossTenantApply = await ownerClient.PutAsJsonAsync(
            $"/internal/companies/{seed.UnownedCompanyId:D}/finance/accounting/policy-pack",
            new { policyPackKey = "country-neutral", policyPackVersion = "1.0.0", effectiveFrom = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), expectedVersion = 1 });

        Assert.Equal(HttpStatusCode.Forbidden, employeeCreate.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeePreview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, employeeApply.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantValidation.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantCapability.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantPreview.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenantApply.StatusCode);
    }

    private async Task<Seed> SeedCompaniesAsync()
    {
        var companyId = Guid.NewGuid();
        var secondOwnedCompanyId = Guid.NewGuid();
        var unownedCompanyId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var employeeUserId = Guid.NewGuid();
        const string ownerSubject = "accounting-owner";
        const string ownerEmail = "accounting-owner@example.com";
        const string employeeSubject = "accounting-employee";
        const string employeeEmail = "accounting-employee@example.com";

        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(
                new User(ownerUserId, ownerEmail, "Accounting Owner", "dev-header", ownerSubject),
                new User(employeeUserId, employeeEmail, "Accounting Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(
                new Company(companyId, "Accounting Company"),
                new Company(secondOwnedCompanyId, "Second Accounting Company"),
                new Company(unownedCompanyId, "Unowned Accounting Company"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), secondOwnedCompanyId, ownerUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });

        return new Seed(
            companyId,
            secondOwnedCompanyId,
            unownedCompanyId,
            ownerSubject,
            ownerEmail,
            employeeSubject,
            employeeEmail);
    }

    private HttpClient CreateClient(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }

    private static SaveCompanyStatutoryProfileRequest StatutoryProfileRequest() => new()
    {
        LegalName = "Example Legal AB",
        SwedishOrganisationNumber = "556016-0680",
        VatRegistrationNumber = "SE556016068001",
        VatRegistrationStatus = StatutoryVatRegistrationStatusValues.Registered,
        RegisteredAddress = new StatutoryAddressDto("Examplegatan 1", null, "111 22", "Stockholm", "SE"),
        CountryCode = "SE",
        AccountingCurrency = "SEK",
        FiscalYearBasis = StatutoryFiscalYearBasisValues.CalendarYear,
        BookkeepingMethod = StatutoryBookkeepingMethodValues.Accrual,
        OrganisationRegistrationEffectiveFrom = new DateOnly(2000, 1, 1),
        VatRegistrationEffectiveFrom = new DateOnly(2000, 1, 1),
        IsUserAttested = true,
        VerificationStatus = StatutoryVerificationStatusValues.Unverified,
        SourceKind = "user_entry",
        SourceReference = "accounting-setup",
        SourceCapturedUtc = new DateTime(2026, 8, 24, 8, 0, 0, DateTimeKind.Utc)
    };

    private sealed record Seed(
        Guid CompanyId,
        Guid SecondOwnedCompanyId,
        Guid UnownedCompanyId,
        string OwnerSubject,
        string OwnerEmail,
        string EmployeeSubject,
        string EmployeeEmail);
}
