using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceInsightQueryEndpointsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceInsightQueryEndpointsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Dashboard_endpoint_returns_normalized_persisted_insights_and_supports_status_filter()
    {
        var seed = await SeedAsync(includeOtherCompanyMembership: true);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/finance/insights/dashboard?status=resolved&sortBy=createdAt&sortDirection=asc");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!["companyId"]!.GetValue<Guid>());

        var items = payload["items"]!.AsArray();
        var item = Assert.Single(items).AsObject();

        Assert.Equal(seed.ResolvedInsightId, item["id"]!.GetValue<Guid>());
        Assert.Equal("resolved", item["status"]!.GetValue<string>());
        Assert.False(string.IsNullOrWhiteSpace(item["severity"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(item["message"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(item["recommendation"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(item["checkCode"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(item["checkName"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(item["conditionKey"]!.GetValue<string>()));
        Assert.NotEqual(DateTime.MinValue, item["createdAt"]!.GetValue<DateTime>());
        Assert.NotEqual(DateTime.MinValue, item["updatedAt"]!.GetValue<DateTime>());

        var entityReference = item["entityReference"]!.AsObject();
        Assert.False(string.IsNullOrWhiteSpace(entityReference["entityType"]!.GetValue<string>()));
        Assert.False(string.IsNullOrWhiteSpace(entityReference["entityId"]!.GetValue<string>()));
        Assert.NotNull(item["affectedEntities"]);
    }

    [Fact]
    public async Task Entity_endpoint_returns_same_normalized_shape_and_matches_primary_or_affected_entities()
    {
        var seed = await SeedAsync(includeOtherCompanyMembership: false);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var dashboardResponse = await client.GetAsync($"/api/companies/{seed.CompanyId}/finance/insights/dashboard");
        var entityResponse = await client.GetAsync(
            $"/api/companies/{seed.CompanyId}/finance/insights/entities/counterparty/customer-1?sortBy=updatedAt&sortDirection=desc");

        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, entityResponse.StatusCode);

        var dashboardPayload = await dashboardResponse.Content.ReadFromJsonAsync<JsonObject>();
        var entityPayload = await entityResponse.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(dashboardPayload);
        Assert.NotNull(entityPayload);

        var dashboardItem = dashboardPayload!["items"]!.AsArray()[0]!.AsObject();
        var entityItems = entityPayload!["items"]!.AsArray();
        Assert.Equal(2, entityItems.Count);

        var dashboardKeys = dashboardItem.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var entityKeys = entityItems[0]!.AsObject().Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(dashboardKeys, entityKeys);

        var returnedIds = entityItems
            .Select(x => x!["id"]!.GetValue<Guid>())
            .OrderBy(x => x)
            .ToArray();
        Assert.Equal(
            new[] { seed.ActiveInsightId, seed.ResolvedInsightId }.OrderBy(x => x).ToArray(),
            returnedIds);

        Assert.All(entityItems, item =>
        {
            var insight = item!.AsObject();
            var entityReference = insight["entityReference"]!.AsObject();
            var matchesPrimary = string.Equals(entityReference["entityType"]!.GetValue<string>(), "counterparty", StringComparison.OrdinalIgnoreCase) &&
                                 string.Equals(entityReference["entityId"]!.GetValue<string>(), "customer-1", StringComparison.OrdinalIgnoreCase);
            var matchesAffected = insight["affectedEntities"]!.AsArray().Any(entity =>
                string.Equals(entity!["entityType"]!.GetValue<string>(), "counterparty", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entity!["entityId"]!.GetValue<string>(), "customer-1", StringComparison.OrdinalIgnoreCase));

            Assert.True(matchesPrimary || matchesAffected);
        });
    }

    [Fact]
    public async Task Dashboard_endpoint_is_tenant_scoped()
    {
        var seed = await SeedAsync(includeOtherCompanyMembership: false);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/api/companies/{seed.OtherCompanyId}/finance/insights/dashboard");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_endpoint_returns_bad_request_for_unknown_status_filter()
    {
        var seed = await SeedAsync(includeOtherCompanyMembership: false);
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/finance/insights/dashboard?status=not-a-status");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private async Task<SeedContext> SeedAsync(bool includeOtherCompanyMembership)
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var activeInsightId = Guid.NewGuid();
        var resolvedInsightId = Guid.NewGuid();
        var otherCompanyInsightId = Guid.NewGuid();
        var subject = $"finance-insight-query-{Guid.NewGuid():N}";
        var email = $"finance-insight-query-{Guid.NewGuid():N}@example.com";
        var displayName = "Finance Insight Query User";

        await _factory.SeedAsync(async dbContext =>
        {
            dbContext.Companies.AddRange(
                new Company(companyId, "Finance Insight Query Company"),
                new Company(otherCompanyId, "Other Finance Insight Query Company"));
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            if (includeOtherCompanyMembership)
            {
                dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            }

            var customerPrimary = new FinanceInsightEntityReferenceDto("counterparty", "customer-1", "Contoso", true);
            var customerAffected = new FinanceInsightEntityReferenceDto("counterparty", "customer-1", "Contoso", false);
            var invoicePrimary = new FinanceInsightEntityReferenceDto("invoice", "invoice-7", "INV-7", true);
            var invoiceAffected = new FinanceInsightEntityReferenceDto("invoice", "invoice-7", "INV-7", false);
            var otherCounterparty = new FinanceInsightEntityReferenceDto("counterparty", "customer-99", "Fabrikam", true);

            dbContext.FinanceAgentInsights.Add(
                new FinanceAgentInsight(
                    activeInsightId,
                    companyId,
                    FinancialCheckDefinitions.OverdueReceivables.Code,
                    "overdue_receivables:customer-1",
                    customerPrimary.EntityType,
                    customerPrimary.EntityId,
                    FinancialCheckSeverity.High,
                    "Contoso has overdue receivables.",
                    "Start collections outreach.",
                    0.82m,
                    customerPrimary.DisplayName,
                    JsonSerializer.Serialize(new[] { customerPrimary, invoiceAffected }),
                    null,
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 7, 30, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 7, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 8, 0, 0, DateTimeKind.Utc)));

            dbContext.FinanceAgentInsights.Add(
                new FinanceAgentInsight(
                    resolvedInsightId,
                    companyId,
                    FinancialCheckDefinitions.TransactionAnomaly.Code,
                    "transaction_anomaly:invoice-7",
                    invoicePrimary.EntityType,
                    invoicePrimary.EntityId,
                    FinancialCheckSeverity.Medium,
                    "Invoice INV-7 was previously flagged for review.",
                    "Keep the audit trail and monitor related activity.",
                    0.71m,
                    invoicePrimary.DisplayName,
                    JsonSerializer.Serialize(new[] { invoicePrimary, customerAffected }),
                    "{\"source\":\"persisted\"}",
                    FinanceInsightStatus.Resolved,
                    new DateTime(2026, 4, 21, 12, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 21, 14, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 21, 14, 0, 0, DateTimeKind.Utc)));

            dbContext.FinanceAgentInsights.Add(
                new FinanceAgentInsight(
                    otherCompanyInsightId,
                    otherCompanyId,
                    FinancialCheckDefinitions.PayablesPressure.Code,
                    "payables_pressure:customer-99",
                    otherCounterparty.EntityType,
                    otherCounterparty.EntityId,
                    FinancialCheckSeverity.Critical,
                    "Other company payables pressure insight.",
                    "This record must never leak into another tenant response.",
                    0.95m,
                    otherCounterparty.DisplayName,
                    JsonSerializer.Serialize(new[] { otherCounterparty }),
                    null,
                    FinanceInsightStatus.Active,
                    new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 22, 9, 15, 0, DateTimeKind.Utc)));

            await dbContext.SaveChangesAsync();
        });

        return new SeedContext(
            companyId,
            otherCompanyId,
            subject,
            email,
            displayName,
            activeInsightId,
            resolvedInsightId);
    }

    private sealed record SeedContext(
        Guid CompanyId,
        Guid OtherCompanyId,
        string Subject,
        string Email,
        string DisplayName,
        Guid ActiveInsightId,
        Guid ResolvedInsightId);
}