using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Api.Tests;

public sealed class StatutoryDocumentApiIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();
    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Series_admin_is_authorized_tenant_scoped_versioned_and_audited()
    {
        var seed = await SeedAsync();
        using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var employee = Client(seed.EmployeeSubject, seed.EmployeeEmail);
        var request = new
        {
            code = "CI", documentType = StatutoryDocumentTypes.CustomerInvoice,
            fiscalYearStart = new DateOnly(2026, 1, 1), fiscalYearEnd = new DateOnly(2026, 12, 31),
            prefix = "INV-", numberWidth = 6, firstNumber = 1
        };

        using var forbidden = await employee.PostAsJsonAsync(Route(seed.CompanyId, "statutory-document-series"), request);
        using var crossTenant = await owner.PostAsJsonAsync(Route(seed.UnownedCompanyId, "statutory-document-series"), request);
        using var created = await owner.PostAsJsonAsync(Route(seed.CompanyId, "statutory-document-series"), request);
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.True(created.IsSuccessStatusCode, createdBody);
        using var json = JsonDocument.Parse(createdBody);
        var seriesId = json.RootElement.GetProperty("id").GetGuid();
        var version = json.RootElement.GetProperty("version").GetInt64();

        using var updated = await owner.PutAsJsonAsync(Route(seed.CompanyId, $"statutory-document-series/{seriesId:D}"),
            new { expectedVersion = version, prefix = "F-", numberWidth = 7, isActive = true });
        using var stale = await owner.PutAsJsonAsync(Route(seed.CompanyId, $"statutory-document-series/{seriesId:D}"),
            new { expectedVersion = version, prefix = "STALE-", numberWidth = 7, isActive = true });
        using var staleJson = JsonDocument.Parse(await stale.Content.ReadAsStringAsync());
        using var list = await owner.GetAsync(Route(seed.CompanyId, "statutory-document-series"));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(StatutoryDocumentReasonCodes.VersionConflict, staleJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(2, await _factory.ExecuteDbContextAsync(db => db.AuditEvents.IgnoreQueryFilters()
            .CountAsync(x => x.CompanyId == seed.CompanyId && x.TargetType == VirtualCompany.Application.Auditing.AuditTargetTypes.StatutoryDocumentSeries)));
    }

    [Fact]
    public async Task Gap_problem_contract_is_plain_and_does_not_consume_a_number()
    {
        var seed = await SeedAsync(); using var owner = Client(seed.OwnerSubject, seed.OwnerEmail);
        using var created = await owner.PostAsJsonAsync(Route(seed.CompanyId, "statutory-document-series"), new
        {
            code = "CI", documentType = StatutoryDocumentTypes.CustomerInvoice,
            fiscalYearStart = new DateOnly(2026, 1, 1), fiscalYearEnd = new DateOnly(2026, 12, 31),
            prefix = "INV-", numberWidth = 6, firstNumber = 1
        });
        using var createdJson = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var seriesId = createdJson.RootElement.GetProperty("id").GetGuid();

        using var rejected = await owner.PostAsJsonAsync(Route(seed.CompanyId, $"statutory-document-series/{seriesId:D}/gaps"),
            new { businessKey = "gap-without-reason", sourceVersion = 1, reason = "" });
        using var json = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Equal(StatutoryDocumentReasonCodes.RequiredFieldMissing, json.RootElement.GetProperty("code").GetString());
        Assert.Contains("reason", json.RootElement.GetProperty("detail").GetString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await _factory.ExecuteDbContextAsync(db => db.StatutoryDocumentNumberAllocations.IgnoreQueryFilters().CountAsync()));
        Assert.Equal(1, await _factory.ExecuteDbContextAsync(db => db.StatutoryDocumentSeries.IgnoreQueryFilters().Select(x => x.NextNumber).SingleAsync()));
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid(); var unowned = Guid.NewGuid(); var ownerId = Guid.NewGuid(); var employeeId = Guid.NewGuid();
        const string ownerSubject = "statutory-owner"; const string ownerEmail = "statutory-owner@example.com";
        const string employeeSubject = "statutory-employee"; const string employeeEmail = "statutory-employee@example.com";
        await _factory.SeedAsync(db =>
        {
            db.Users.AddRange(new User(ownerId, ownerEmail, "Owner", "dev-header", ownerSubject),
                new User(employeeId, employeeEmail, "Employee", "dev-header", employeeSubject));
            db.Companies.AddRange(new Company(companyId, "Statutory Company"), new Company(unowned, "Unowned"));
            db.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            return Task.CompletedTask;
        });
        return new(companyId, unowned, ownerSubject, ownerEmail, employeeSubject, employeeEmail);
    }

    private HttpClient Client(string subject, string email)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeader, subject);
        return client;
    }
    private static string Route(Guid companyId, string suffix) => $"/internal/companies/{companyId:D}/finance/accounting/{suffix}";
    private sealed record Seed(Guid CompanyId, Guid UnownedCompanyId, string OwnerSubject, string OwnerEmail, string EmployeeSubject, string EmployeeEmail);
}
