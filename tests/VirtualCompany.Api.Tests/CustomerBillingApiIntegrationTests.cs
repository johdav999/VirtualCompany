using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class CustomerBillingApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Owner_can_save_read_and_history_profile_while_stale_write_returns_conflict()
    {
        var seed = await SeedAsync(); using var client = Client(seed.OwnerSubject, seed.OwnerEmail);
        var request = new UpsertCustomerBillingProfileRequest(Profile(), null);
        using var created = await client.PutAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/customers/{seed.CustomerId:D}/billing-profile", request);
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var version = createdJson.RootElement.GetProperty("version").GetInt64();
        using var read = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/customers/{seed.CustomerId:D}/billing-profile");
        using var history = await client.GetAsync($"/internal/companies/{seed.CompanyId:D}/finance/customers/{seed.CustomerId:D}/billing-profile/history");
        using var stale = await client.PutAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/customers/{seed.CustomerId:D}/billing-profile",
            new UpsertCustomerBillingProfileRequest(Profile() with { BuyerReference = "stale" }, version - 1));

        Assert.Equal(HttpStatusCode.OK, created.StatusCode); Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var staleJson = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        Assert.Equal(CustomerBillingReasonCodes.ConcurrencyConflict, staleJson.RootElement.GetProperty("reasonCode").GetString());
        Assert.Equal(1, await _factory.ExecuteDbContextAsync(db => db.CustomerBillingProfiles.IgnoreQueryFilters().CountAsync()));
    }

    [Fact]
    public async Task Profile_and_duplicate_governance_enforce_admin_and_tenant_scope()
    {
        var seed = await SeedAsync(); using var employee = Client(seed.EmployeeSubject, seed.EmployeeEmail);
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var employeeWrite = await employee.PutAsJsonAsync($"/internal/companies/{seed.CompanyId:D}/finance/customers/{seed.CustomerId:D}/billing-profile",
            new UpsertCustomerBillingProfileRequest(Profile(), null));
        using var crossRead = await owner.GetAsync($"/internal/companies/{seed.UnownedCompanyId:D}/finance/customer-duplicates");
        using var crossWrite = await owner.PutAsJsonAsync($"/internal/companies/{seed.UnownedCompanyId:D}/finance/customers/{seed.UnownedCustomerId:D}/billing-profile",
            new UpsertCustomerBillingProfileRequest(Profile(), null));

        Assert.Equal(HttpStatusCode.Forbidden, employeeWrite.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossRead.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossWrite.StatusCode);
        Assert.Equal(0, await _factory.ExecuteDbContextAsync(db => db.CustomerBillingProfiles.IgnoreQueryFilters().CountAsync()));
    }

    private async Task<Seed> SeedAsync()
    {
        var company = Guid.NewGuid(); var unowned = Guid.NewGuid(); var customer = Guid.NewGuid();
        var unownedCustomer = Guid.NewGuid(); var owner = Guid.NewGuid(); var employee = Guid.NewGuid();
        const string ownerSubject = "billing-owner"; const string ownerEmail = "billing-owner@example.test";
        const string employeeSubject = "billing-employee"; const string employeeEmail = "billing-employee@example.test";
        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(new User(owner, ownerEmail, "Billing Owner", "dev-header", ownerSubject),
                new User(employee, employeeEmail, "Billing Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(new Company(company, "Billing Company"), new Company(unowned, "Unowned"));
            db.CompanyMemberships.AddRange(new CompanyMembership(Guid.NewGuid(), company, owner, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), company, employee, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            db.FinanceCounterparties.AddRange(new FinanceCounterparty(customer, company, "Customer", "customer"),
                new FinanceCounterparty(unownedCustomer, unowned, "Other", "customer"));
            return Task.CompletedTask;
        });
        return new(company, unowned, customer, unownedCustomer, ownerSubject, ownerEmail, employeeSubject, employeeEmail);
    }

    private HttpClient Client(string subject, string email)
    {
        var client = _factory.CreateClient(); client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject); return client;
    }

    private static CustomerBillingProfileInputDto Profile() => new("Example AB", "Example", CustomerBillingPartyKinds.Organization,
        "SE5560160680", "SE556016068001", CustomerBillingValidationStates.UserAttested,
        new CustomerBillingAddressDto("Examplegatan 1", null, "111 22", "Stockholm", null, "SE"), null,
        "sv-SE", "SEK", CustomerBillingPaymentTermKinds.FixedDays, 30, "bank_transfer",
        CustomerBillingDeliveryChannels.Email, "billing@example.test", "Buyer-1", null, null, 10000m,
        CustomerBillingCreditStatuses.Active, "1510", null, new DateOnly(2026, 1, 1), null,
        CustomerBillingSourceKinds.User, "api-test", DateTime.UtcNow, null, null);

    private sealed record Seed(Guid CompanyId, Guid UnownedCompanyId, Guid CustomerId, Guid UnownedCustomerId,
        string OwnerSubject, string OwnerEmail, string EmployeeSubject, string EmployeeEmail);
}
