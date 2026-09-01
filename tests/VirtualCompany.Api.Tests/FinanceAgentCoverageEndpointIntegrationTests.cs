using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAgentCoverageEndpointIntegrationTests : IDisposable
{
    private readonly TestWebApplicationFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task Finance_authorized_administrator_receives_effective_Laura_projection()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.OwnerSubject, seed.OwnerEmail, "Owner");

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId:D}/agents/{seed.AgentId:D}/finance-coverage");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var coverage = await response.Content.ReadFromJsonAsync<FinanceAgentEffectiveCoverageDto>();
        Assert.NotNull(coverage);
        Assert.Equal(FinanceAgentCoverageVersions.V1, coverage!.CatalogueVersion);
        Assert.Equal(seed.CompanyId, coverage.CompanyId);
        Assert.Equal(seed.AgentId, coverage.AgentId);
        Assert.Equal("Laura", coverage.AgentName);
        Assert.Equal(FinanceAgentCoverageCatalogue.Manifests.Count, coverage.Counts.TotalCapabilities);
        Assert.Equal(FinanceAgentCoverageCatalogue.OwnedToolNames.Count, coverage.Counts.RegisteredTools);
        Assert.Contains(coverage.Gaps, gap => gap.OperationId == "self_approval" &&
                                             gap.SupportState == FinanceAgentCoverageSupportStates.HumanOnly);
        Assert.False(string.IsNullOrWhiteSpace(coverage.AuthorityHash));
    }

    [Fact]
    public async Task Company_member_without_FinanceView_cannot_read_effective_coverage()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.EmployeeSubject, seed.EmployeeEmail, "Employee");

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId:D}/agents/{seed.AgentId:D}/finance-coverage");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();
        const string ownerSubject = "finance-coverage-owner";
        const string ownerEmail = "finance-coverage-owner@example.com";
        const string employeeSubject = "finance-coverage-employee";
        const string employeeEmail = "finance-coverage-employee@example.com";

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(ownerId, ownerEmail, "Owner", "dev-header", ownerSubject),
                new User(employeeId, employeeEmail, "Employee", "dev-header", employeeSubject));
            dbContext.Companies.Add(new Company(companyId, "Finance coverage company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, ownerId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), companyId, employeeId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(
                agentId,
                companyId,
                "finance",
                "Laura",
                "Finance Manager",
                "Finance",
                null,
                AgentSeniority.Senior,
                AgentStatus.Active,
                AgentAutonomyLevel.Guided));
            return Task.CompletedTask;
        });

        return new Seed(companyId, agentId, ownerSubject, ownerEmail, employeeSubject, employeeEmail);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record Seed(
        Guid CompanyId,
        Guid AgentId,
        string OwnerSubject,
        string OwnerEmail,
        string EmployeeSubject,
        string EmployeeEmail);
}
