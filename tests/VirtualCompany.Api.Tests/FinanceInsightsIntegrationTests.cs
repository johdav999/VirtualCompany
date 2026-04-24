using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceInsightsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceInsightsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Finance_insights_endpoint_returns_normalized_company_scoped_payload()
    {
        var seed = await SeedCompanyAsync(includeOtherCompanyMembership: false);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/finance/insights");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!["companyId"]!.GetValue<Guid>());
        Assert.True(payload["generatedAt"]!.GetValue<DateTime>() > DateTime.MinValue);
        Assert.NotNull(payload["items"]);
        Assert.True(payload["items"]!.AsArray().Count > 0);

        var first = payload["items"]!.AsArray()[0]!.AsObject();
        Assert.False(string.IsNullOrWhiteSpace(first["checkCode"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(first["checkName"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(first["conditionKey"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(first["severity"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(first["message"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(first["recommendation"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(first["status"]!.GetValue<string>()));
        Assert.True(first["confidence"]!.GetValue<decimal>() >= 0m);
        Assert.True(first["confidence"]!.GetValue<decimal>() <= 1m);
        Assert.NotNull(first["affectedEntities"]);
    }

    [Fact]
    public async Task Finance_insights_endpoint_supports_entity_filtered_reads_with_the_same_payload_shape()
    {
        var seed = await SeedCompanyAsync(includeOtherCompanyMembership: false);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var fullResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/finance/insights");
        var fullPayload = await fullResponse.Content.ReadFromJsonAsync<JsonObject>();
        var sourceInsight = fullPayload!["items"]!.AsArray()
            .Select(x => x!.AsObject())
            .First(x => x["primaryEntity"] is JsonObject);
        var primaryEntity = sourceInsight["primaryEntity"]!.AsObject();
        var entityType = primaryEntity["entityType"]!.GetValue<string>();
        var entityId = primaryEntity["entityId"]!.GetValue<string>();

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/finance/insights?entityType={Uri.EscapeDataString(entityType)}&entityId={Uri.EscapeDataString(entityId)}&includeResolved=false");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!["companyId"]!.GetValue<Guid>());
        Assert.True(payload["items"]!.AsArray().Count > 0);
        Assert.All(payload["items"]!.AsArray(), item => Assert.False(string.IsNullOrWhiteSpace(item!["conditionKey"]!.GetValue<string>())));
        Assert.All(payload["items"]!.AsArray(), item =>
            Assert.Contains(item!["affectedEntities"]!.AsArray(), entity =>
                string.Equals(entity!["entityType"]!.GetValue<string>(), entityType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entity!["entityId"]!.GetValue<string>(), entityId, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task Finance_insight_refresh_endpoint_primes_snapshot_cache_for_subsequent_reads()
    {
        var seed = await SeedCompanyAsync(includeOtherCompanyMembership: true);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var refreshResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/insights/refresh",
            new
            {
                expenseWindowDays = 90,
                trendWindowDays = 30,
                payableWindowDays = 14,
                retentionMinutes = 360,
                runInBackground = false
            });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshPayload = await refreshResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(refreshPayload);
        Assert.True(refreshPayload!["refreshed"]!.GetValue<bool>());
        Assert.NotNull(refreshPayload["expiresAtUtc"]);

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/finance/insights");
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.True(payload!["fromSnapshot"]!.GetValue<bool>());
        Assert.NotNull(payload["snapshotExpiresAtUtc"]);
    }

    [Fact]
    public async Task Finance_insights_endpoint_is_tenant_scoped()
    {
        var seed = await SeedCompanyAsync(includeOtherCompanyMembership: false);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/finance/insights");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Finance_analytics_endpoint_returns_unified_operational_planning_and_statement_payload()
    {
        var seed = await SeedCompanyAsync(includeOtherCompanyMembership: false);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/finance/analytics");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!["companyId"]!.GetValue<Guid>());
        Assert.NotNull(payload["operationalInsights"]);
        Assert.NotNull(payload["cashPosition"]);
        Assert.NotNull(payload["summary"]);
        Assert.NotNull(payload["narrative"]);
        Assert.NotNull(payload["planning"]);
        Assert.NotNull(payload["statements"]);
        Assert.False(string.IsNullOrWhiteSpace(payload["narrative"]!["headline"]!.GetValue<string>()));
        Assert.NotNull(payload["planning"]!["budgetVersions"]);
        Assert.NotNull(payload["planning"]!["forecastVersions"]);
        Assert.NotNull(payload["statements"]!["snapshots"]);
    }

    [Fact]
    public async Task Finance_bootstrap_rerun_endpoint_is_idempotent_for_existing_company()
    {
        var seed = await SeedCompanyAsync(includeOtherCompanyMembership: true, membershipRole: CompanyMembershipRole.Admin);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bootstrap/rerun",
            new { batchSize = 250, rerunPlanningBackfill = true, rerunApprovalBackfill = true, correlationId = "finance-bootstrap-rerun-admin-001" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(payload);
        Assert.True(payload!["planningBackfillRan"]!.GetValue<bool>());
        Assert.True(payload["approvalBackfillRan"]!.GetValue<bool>());
        Assert.NotEqual(DateTime.MinValue, payload["completedAtUtc"]!.GetValue<DateTime>());
    }

    [Fact]
    public async Task Finance_bootstrap_rerun_endpoint_requires_owner_or_admin_membership()
    {
        var seed = await SeedCompanyAsync(includeOtherCompanyMembership: false, membershipRole: CompanyMembershipRole.Employee);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.PostAsJsonAsync($"/internal/companies/{seed.CompanyId}/finance/bootstrap/rerun", new { batchSize = 250 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Finance_bootstrap_rerun_endpoint_remains_idempotent_for_existing_company()
    {
        var seed = await SeedCompanyAsync(includeOtherCompanyMembership: true, membershipRole: CompanyMembershipRole.Owner);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var firstResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bootstrap/rerun",
            new { batchSize = 250, correlationId = "finance-bootstrap-rerun-api-001" });
        var secondResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/bootstrap/rerun",
            new { batchSize = 250, correlationId = "finance-bootstrap-rerun-api-001" });

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var first = await firstResponse.Content.ReadFromJsonAsync<JsonObject>();
        var second = await secondResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(first);
        Assert.NotNull(second);

        Assert.Equal(seed.CompanyId, first!["companyId"]!.GetValue<Guid>());
        Assert.True(first["planningBackfillRan"]!.GetValue<bool>());
        Assert.True(first["approvalBackfillRan"]!.GetValue<bool>());
        Assert.True(first["approvalBackfill"]!["createdCount"]!.GetValue<int>() >= 0);
        Assert.Equal(0, second!["planningRowsInserted"]!.GetValue<int>());
        Assert.Equal(0, second["approvalBackfill"]!["createdCount"]!.GetValue<int>());

        var duplicateTargets = await _factory.ExecuteDbContextAsync(async dbContext =>
            await dbContext.ApprovalTasks
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == seed.CompanyId)
                .GroupBy(x => new { x.TargetType, x.TargetId })
                .CountAsync(x => x.Count() > 1));
        Assert.Equal(0, duplicateTargets);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.SubjectHeaderName, subject);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.EmailHeaderName, email);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.DisplayNameHeaderName, displayName);
        client.DefaultRequestHeaders.Add(DevAuthHeaderDefaults.ProviderHeaderName, "dev-header");
        return client;
    }

    private async Task<SeedContext> SeedCompanyAsync(bool includeOtherCompanyMembership, CompanyMembershipRole membershipRole = CompanyMembershipRole.Owner)
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var subject = $"finance-insight-{membershipRole.ToStorageValue()}";
        var email = $"finance.{membershipRole.ToStorageValue()}@example.com";
        var displayName = $"Finance {membershipRole}";

        await _factory.SeedAsync(async dbContext =>
        {
            var primaryCompany = new Company(companyId, "Finance Insight Company");
            primaryCompany.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
            var otherCompany = new Company(otherCompanyId, "Other Finance Insight Company");
            otherCompany.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);

            dbContext.Companies.AddRange(primaryCompany, otherCompany);
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, membershipRole, CompanyMembershipStatus.Active));
            if (includeOtherCompanyMembership)
            {
                dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, membershipRole, CompanyMembershipStatus.Active));
            }

            FinanceSeedData.AddMockFinanceData(dbContext, companyId);
            FinanceSeedData.AddMockFinanceData(dbContext, otherCompanyId);

            var primaryPolicy = dbContext.FinancePolicyConfigurations.Local.Single(x => x.CompanyId == companyId);
            primaryPolicy.Update(
                primaryPolicy.ApprovalCurrency,
                primaryPolicy.InvoiceApprovalThreshold,
                1000m,
                primaryPolicy.RequireCounterpartyForTransactions,
                primaryPolicy.AnomalyDetectionLowerBound,
                primaryPolicy.AnomalyDetectionUpperBound,
                primaryPolicy.CashRunwayWarningThresholdDays,
                primaryPolicy.CashRunwayCriticalThresholdDays);

            var otherPolicy = dbContext.FinancePolicyConfigurations.Local.Single(x => x.CompanyId == otherCompanyId);
            otherPolicy.Update(
                otherPolicy.ApprovalCurrency,
                otherPolicy.InvoiceApprovalThreshold,
                1000m,
                otherPolicy.RequireCounterpartyForTransactions,
                otherPolicy.AnomalyDetectionLowerBound,
                otherPolicy.AnomalyDetectionUpperBound,
                otherPolicy.CashRunwayWarningThresholdDays,
                otherPolicy.CashRunwayCriticalThresholdDays);

            await Task.CompletedTask;
        });

        return new SeedContext(companyId, otherCompanyId, subject, email, displayName);
    }

    private sealed record SeedContext(
        Guid CompanyId,
        Guid OtherCompanyId,
        string Subject,
        string Email,
        string DisplayName);
}
