using System.Net;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AuditAuthorizationIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AuditAuthorizationIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Audit_list_and_detail_allow_manager_in_current_company()
    {
        var seed = await SeedAuditMembershipAsync("audit-manager", "audit.manager@example.com", CompanyMembershipRole.Manager);
        using var client = CreateAuthenticatedClient("audit-manager", "audit.manager@example.com", "Audit Manager");

        var listResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/audit");
        var detailResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/audit/{seed.AuditEventId}");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
    }

    [Theory]
    [InlineData(CompanyMembershipRole.Employee)]
    [InlineData(CompanyMembershipRole.FinanceApprover)]
    [InlineData(CompanyMembershipRole.SupportSupervisor)]
    public async Task Audit_list_and_detail_forbid_members_without_audit_review_role(CompanyMembershipRole role)
    {
        var subject = $"audit-{role.ToStorageValue()}";
        var seed = await SeedAuditMembershipAsync(subject, $"{subject}@example.com", role);
        using var client = CreateAuthenticatedClient(subject, $"{subject}@example.com", role.ToDisplayName());

        var listResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/audit");
        var detailResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/audit/{seed.AuditEventId}");

        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detailResponse.StatusCode);
    }

    [Fact]
    public async Task Audit_detail_returns_not_found_for_cross_tenant_event_id_even_for_authorized_reviewer()
    {
        var reviewerSubject = "audit-owner";
        var reviewerEmail = "audit.owner@example.com";
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var otherAuditEventId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, reviewerEmail, "Audit Owner", "dev-header", reviewerSubject));
            dbContext.Companies.AddRange(new Company(companyId, "Company A"), new Company(otherCompanyId, "Company B"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));
            dbContext.AuditEvents.Add(new AuditEvent(
                otherAuditEventId,
                otherCompanyId,
                AuditActorTypes.System,
                null,
                AuditEventActions.WorkflowInstanceStarted,
                AuditTargetTypes.WorkflowInstance,
                Guid.NewGuid().ToString(),
                AuditEventOutcomes.Succeeded,
                "Other tenant event."));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(reviewerSubject, reviewerEmail, "Audit Owner");

        var response = await client.GetAsync($"/api/companies/{companyId}/audit/{otherAuditEventId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Audit_list_forbids_company_without_membership()
    {
        var seed = await SeedAuditMembershipAsync("audit-admin", "audit.admin@example.com", CompanyMembershipRole.Admin);
        var noMembershipCompanyId = Guid.NewGuid();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(noMembershipCompanyId, "Company No Membership"));
            dbContext.AuditEvents.Add(new AuditEvent(
                Guid.NewGuid(),
                noMembershipCompanyId,
                AuditActorTypes.System,
                null,
                AuditEventActions.WorkflowInstanceStarted,
                AuditTargetTypes.WorkflowInstance,
                Guid.NewGuid().ToString(),
                AuditEventOutcomes.Succeeded,
                "Unrelated company event."));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient("audit-admin", "audit.admin@example.com", "Audit Admin");

        var response = await client.GetAsync($"/api/companies/{noMembershipCompanyId}/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.NotEqual(seed.CompanyId, noMembershipCompanyId);
    }

    private async Task<AuditSeed> SeedAuditMembershipAsync(string subject, string email, CompanyMembershipRole role)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var auditEventId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, role.ToDisplayName(), "dev-header", subject));
            dbContext.Companies.Add(new Company(companyId, "Audit Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                role,
                CompanyMembershipStatus.Active));
            dbContext.AuditEvents.Add(new AuditEvent(
                auditEventId,
                companyId,
                AuditActorTypes.System,
                null,
                AuditEventActions.WorkflowInstanceStarted,
                AuditTargetTypes.WorkflowInstance,
                Guid.NewGuid().ToString(),
                AuditEventOutcomes.Succeeded,
                "Tenant-scoped audit event."));
            return Task.CompletedTask;
        });

        return new AuditSeed(companyId, auditEventId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record AuditSeed(Guid CompanyId, Guid AuditEventId);
}