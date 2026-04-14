using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Escalations;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class EscalationApiIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public EscalationApiIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Escalation_list_filters_by_source_policy_correlation_and_tenant()
    {
        var seed = await SeedEscalationHistoryAsync("list");
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Escalation Reviewer");

        var all = await client.GetFromJsonAsync<EscalationRecordListResult>(
            $"/api/companies/{seed.CompanyId}/escalations");
        var bySource = await client.GetFromJsonAsync<EscalationRecordListResult>(
            $"/api/companies/{seed.CompanyId}/escalations?sourceEntityId={seed.SourceEntityId}&sourceEntityType=work_task");
        var byPolicy = await client.GetFromJsonAsync<EscalationRecordListResult>(
            $"/api/companies/{seed.CompanyId}/escalations?policyId={seed.PolicyId}");
        var byCorrelation = await client.GetFromJsonAsync<EscalationRecordListResult>(
            $"/api/companies/{seed.CompanyId}/escalations?correlationId=corr-escalation-list");

        Assert.NotNull(all);
        Assert.NotNull(bySource);
        Assert.NotNull(byPolicy);
        Assert.NotNull(byCorrelation);
        var allItem = Assert.Single(all!.Items);
        Assert.Equal(seed.EscalationId, allItem.Id);
        Assert.Equal(seed.PolicyId, allItem.PolicyId);
        Assert.Equal(seed.SourceEntityId, allItem.SourceEntityId);
        Assert.Equal("work_task", allItem.SourceEntityType);
        Assert.Equal(2, allItem.EscalationLevel);
        Assert.Equal("Critical task breached response target.", allItem.Reason);
        Assert.Equal("corr-escalation-list", allItem.CorrelationId);
        Assert.Equal(EscalationStatus.Triggered.ToStorageValue(), allItem.Status);
        Assert.Single(bySource!.Items);
        Assert.Single(byPolicy!.Items);
        Assert.Single(byCorrelation!.Items);
    }

    [Fact]
    public async Task Escalation_detail_does_not_return_cross_tenant_records()
    {
        var seed = await SeedEscalationHistoryAsync("detail");
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Escalation Reviewer");

        var ownResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/escalations/{seed.EscalationId}");
        var crossTenantResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/escalations/{seed.OtherEscalationId}");

        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, crossTenantResponse.StatusCode);
    }

    [Fact]
    public async Task Policy_evaluation_history_filters_by_correlation_and_projects_audit_traceability()
    {
        var seed = await SeedEscalationHistoryAsync("history");
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Escalation Reviewer");

        var result = await client.GetFromJsonAsync<PolicyEvaluationHistoryResult>(
            $"/api/companies/{seed.CompanyId}/escalations/history?correlationId=corr-escalation-history&sourceEntityId={seed.SourceEntityId}&policyId={seed.PolicyId}");

        Assert.NotNull(result);
        Assert.Equal(2, result!.TotalCount);
        Assert.All(result.Items, item =>
        {
            Assert.Equal(seed.CompanyId, item.CompanyId);
            Assert.Equal(seed.PolicyId, item.PolicyId);
            Assert.Equal(seed.SourceEntityId, item.SourceEntityId);
            Assert.Equal("corr-escalation-history", item.CorrelationId);
        });
        Assert.Contains(result.Items, item =>
            item.Action == AuditEventActions.EscalationPolicyEvaluationResult &&
            item.ConditionsMet == true &&
            item.EvaluationResult == "matched");
        Assert.Contains(result.Items, item =>
            item.Action == AuditEventActions.EscalationCreated &&
            item.EscalationRecordId == seed.EscalationId);
    }

    [Fact]
    public async Task Escalation_history_forbids_members_without_audit_review_role()
    {
        var seed = await SeedEscalationHistoryAsync("forbidden", CompanyMembershipRole.Employee);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, "Escalation Employee");

        var listResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/escalations");
        var historyResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/escalations/history");

        Assert.Equal(HttpStatusCode.Forbidden, listResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, historyResponse.StatusCode);
    }

    private async Task<EscalationHistorySeed> SeedEscalationHistoryAsync(
        string suffix,
        CompanyMembershipRole role = CompanyMembershipRole.Manager)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var policyId = Guid.NewGuid();
        var otherPolicyId = Guid.NewGuid();
        var sourceEntityId = Guid.NewGuid();
        var otherSourceEntityId = Guid.NewGuid();
        var escalationId = Guid.NewGuid();
        var otherEscalationId = Guid.NewGuid();
        var correlationId = $"corr-escalation-{suffix}";
        var otherCorrelationId = $"corr-other-escalation-{suffix}";
        var subject = $"escalation-reviewer-{suffix}-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        var triggeredAt = new DateTime(2026, 4, 14, 9, 15, 0, DateTimeKind.Utc);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, "Escalation Reviewer", "dev-header", subject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Escalation Company"),
                new Company(otherCompanyId, "Other Escalation Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                role,
                CompanyMembershipStatus.Active));

            dbContext.Escalations.AddRange(
                new Escalation(
                    escalationId,
                    companyId,
                    policyId,
                    sourceEntityId,
                    EscalationSourceEntityTypes.WorkTask,
                    2,
                    "Critical task breached response target.",
                    triggeredAt,
                    correlationId,
                    0),
                new Escalation(
                    otherEscalationId,
                    otherCompanyId,
                    otherPolicyId,
                    otherSourceEntityId,
                    EscalationSourceEntityTypes.WorkTask,
                    1,
                    "Other tenant escalation.",
                    triggeredAt,
                    otherCorrelationId,
                    0));

            dbContext.AuditEvents.AddRange(
                new AuditEvent(
                    Guid.NewGuid(),
                    companyId,
                    AuditActorTypes.System,
                    null,
                    AuditEventActions.EscalationPolicyEvaluationResult,
                    AuditTargetTypes.EscalationPolicy,
                    sourceEntityId.ToString(),
                    AuditEventOutcomes.Succeeded,
                    "Critical task breached response target.",
                    ["escalation_policy", EscalationSourceEntityTypes.WorkTask],
                    new Dictionary<string, string?>
                    {
                        ["policyId"] = policyId.ToString(),
                        ["policyName"] = "Critical task response policy",
                        ["sourceEntityId"] = sourceEntityId.ToString(),
                        ["sourceEntityType"] = EscalationSourceEntityTypes.WorkTask,
                        ["escalationLevel"] = "2",
                        ["conditionsMet"] = "true"
                    },
                    correlationId,
                    triggeredAt),
                new AuditEvent(
                    Guid.NewGuid(),
                    companyId,
                    AuditActorTypes.System,
                    null,
                    AuditEventActions.EscalationCreated,
                    AuditTargetTypes.EscalationPolicy,
                    sourceEntityId.ToString(),
                    AuditEventOutcomes.Succeeded,
                    "Critical task breached response target.",
                    ["escalation_policy", EscalationSourceEntityTypes.WorkTask],
                    new Dictionary<string, string?>
                    {
                        ["policyId"] = policyId.ToString(),
                        ["policyName"] = "Critical task response policy",
                        ["sourceEntityId"] = sourceEntityId.ToString(),
                        ["sourceEntityType"] = EscalationSourceEntityTypes.WorkTask,
                        ["escalationLevel"] = "2",
                        ["conditionsMet"] = "true",
                        ["escalationId"] = escalationId.ToString()
                    },
                    correlationId,
                    triggeredAt.AddSeconds(1)),
                new AuditEvent(
                    Guid.NewGuid(),
                    otherCompanyId,
                    AuditActorTypes.System,
                    null,
                    AuditEventActions.EscalationPolicyEvaluationResult,
                    AuditTargetTypes.EscalationPolicy,
                    otherSourceEntityId.ToString(),
                    AuditEventOutcomes.Succeeded,
                    "Other tenant policy matched.",
                    ["escalation_policy", EscalationSourceEntityTypes.WorkTask],
                    new Dictionary<string, string?>
                    {
                        ["policyId"] = otherPolicyId.ToString(),
                        ["sourceEntityId"] = otherSourceEntityId.ToString(),
                        ["sourceEntityType"] = EscalationSourceEntityTypes.WorkTask,
                        ["escalationLevel"] = "1",
                        ["conditionsMet"] = "true"
                    },
                    otherCorrelationId,
                    triggeredAt));

            return Task.CompletedTask;
        });

        return new EscalationHistorySeed(
            companyId,
            otherCompanyId,
            policyId,
            sourceEntityId,
            escalationId,
            otherEscalationId,
            subject,
            email);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record EscalationHistorySeed(
        Guid CompanyId,
        Guid OtherCompanyId,
        Guid PolicyId,
        Guid SourceEntityId,
        Guid EscalationId,
        Guid OtherEscalationId,
        string Subject,
        string Email);
}
