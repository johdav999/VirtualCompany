using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AlertIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public AlertIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Detection_create_deduplicates_open_alert_by_fingerprint()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();
        var payload = new
        {
            companyId = seed.CompanyId,
            type = "risk",
            severity = "high",
            title = "Margin at risk",
            summary = "Gross margin dropped below threshold.",
            evidence = new Dictionary<string, object?> { ["metric"] = "gross_margin", ["value"] = 0.12 },
            status = "open",
            correlationId = "corr-alert-1",
            fingerprint = "risk:gross-margin:below-threshold",
            sourceAgentId = seed.AgentId
        };

        var first = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/alerts/detections", payload);
        var second = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/alerts/detections", payload);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondAlert = await second.Content.ReadFromJsonAsync<AlertMutationResponse>();
        Assert.NotNull(secondAlert);
        Assert.False(secondAlert!.Created);
        Assert.True(secondAlert.Deduplicated);
        Assert.Equal(2, secondAlert.Alert.OccurrenceCount);

        var list = await client.GetFromJsonAsync<AlertListResponse>($"/api/companies/{seed.CompanyId}/alerts?status=open");
        Assert.NotNull(list);
        Assert.Single(list!.Items);
    }

    [Fact]
    public async Task Detection_deduplicates_normalized_fingerprint()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/alerts/detections",
            CreatePayload(seed.CompanyId, "risk", "high", "open", "  FP-Normalized  ", seed.AgentId));
        var second = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/alerts/detections",
            CreatePayload(seed.CompanyId, "risk", "high", "open", "fp-normalized", seed.AgentId));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var deduplicated = await second.Content.ReadFromJsonAsync<AlertMutationResponse>();
        Assert.NotNull(deduplicated);
        Assert.True(deduplicated!.Deduplicated);
        Assert.Equal("fp-normalized", deduplicated.Alert.Fingerprint);

        var list = await client.GetFromJsonAsync<AlertListResponse>(
            $"/api/companies/{seed.CompanyId}/alerts?status=open&page=1&pageSize=10");

        Assert.NotNull(list);
        Assert.Single(list!.Items.Where(alert => alert.Fingerprint == "fp-normalized"));
    }

    [Fact]
    public async Task Alert_creation_accepts_required_classifications()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();
        var types = new[] { "risk", "anomaly", "opportunity" };
        var severities = new[] { "low", "medium", "high", "critical" };

        foreach (var type in types)
        {
            foreach (var severity in severities)
            {
                var fingerprint = $"fp-{type}-{severity}";
                var response = await client.PostAsJsonAsync(
                    $"/api/companies/{seed.CompanyId}/alerts/detections",
                    CreatePayload(seed.CompanyId, type, severity, "open", fingerprint, seed.AgentId));

                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var created = await response.Content.ReadFromJsonAsync<AlertMutationResponse>();
                Assert.NotNull(created);
                Assert.Equal(type, created!.Alert.Type);
                Assert.Equal(severity, created.Alert.Severity);
                Assert.Equal("open", created.Alert.Status);
            }
        }
    }

    [Fact]
    public async Task Same_fingerprint_in_different_companies_creates_separate_open_alerts()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();

        var first = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/alerts/detections", CreatePayload(seed.CompanyId, "risk", "high", "open", "fp-cross-company", seed.AgentId));
        var second = await client.PostAsJsonAsync($"/api/companies/{seed.OtherCompanyId}/alerts/detections", CreatePayload(seed.OtherCompanyId, "risk", "high", "open", "fp-cross-company", null));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Query_without_filters_returns_only_current_tenant_page()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();

        await CreateAlertAsync(client, seed.CompanyId, "risk", "high", "open", "fp-tenant-page-1", seed.AgentId);
        await CreateAlertAsync(client, seed.CompanyId, "anomaly", "medium", "open", "fp-tenant-page-2", seed.AgentId);
        await CreateAlertAsync(client, seed.OtherCompanyId, "opportunity", "low", "open", "fp-other-tenant-page", null);

        var list = await client.GetFromJsonAsync<AlertListResponse>(
            $"/api/companies/{seed.CompanyId}/alerts?page=1&pageSize=10");

        Assert.NotNull(list);
        Assert.Equal(2, list!.TotalCount);
        Assert.Equal(1, list.Page);
        Assert.Equal(10, list.PageSize);
        Assert.Equal(1, list.TotalPages);
        Assert.Equal(2, list.Items.Count);
        Assert.All(list.Items, alert => Assert.Equal(seed.CompanyId, alert.CompanyId));
    }

    [Fact]
    public async Task Query_supports_filters_pagination_and_tenant_scoping()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();

        await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/alerts", CreatePayload(seed.CompanyId, "risk", "critical", "open", "fp-risk", seed.AgentId));
        await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/alerts", CreatePayload(seed.CompanyId, "opportunity", "low", "open", "fp-opp", seed.AgentId));
        await client.PostAsJsonAsync($"/api/companies/{seed.OtherCompanyId}/alerts", CreatePayload(seed.OtherCompanyId, "risk", "critical", "open", "fp-risk", null));

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/alerts?type=risk&severity=critical&status=open&createdFrom=2000-01-01T00:00:00Z&createdTo=2100-01-01T00:00:00Z&skip=0&take=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<AlertListResponse>();

        Assert.NotNull(list);
        Assert.Equal(1, list!.Items.Count);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(1, list.Page);
        Assert.Equal(1, list.PageSize);
        Assert.Equal(1, list.TotalPages);
        Assert.Equal(seed.CompanyId, list.Items[0].CompanyId);
        Assert.Equal("risk", list.Items[0].Type);
        Assert.Equal("critical", list.Items[0].Severity);
        Assert.Equal("Margin at risk", list.Items[0].Title);
        Assert.Equal("Detection summary.", list.Items[0].Summary);
        Assert.NotEmpty(list.Items[0].Evidence);
        Assert.Equal("corr-fp-risk", list.Items[0].CorrelationId);
    }

    [Fact]
    public async Task Create_alert_persists_requested_non_open_status_without_deduplicating()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();

        var open = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/alerts",
            CreatePayload(seed.CompanyId, "risk", "high", "open", "fp-status-create", seed.AgentId));
        var resolved = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/alerts",
            CreatePayload(seed.CompanyId, "risk", "high", "resolved", "fp-status-create", seed.AgentId));

        Assert.Equal(HttpStatusCode.Created, open.StatusCode);
        Assert.Equal(HttpStatusCode.Created, resolved.StatusCode);

        var openAlert = await open.Content.ReadFromJsonAsync<AlertMutationResponse>();
        var resolvedAlert = await resolved.Content.ReadFromJsonAsync<AlertMutationResponse>();

        Assert.NotNull(openAlert);
        Assert.NotNull(resolvedAlert);
        Assert.NotEqual(openAlert!.Alert.Id, resolvedAlert!.Alert.Id);
        Assert.Equal("resolved", resolvedAlert.Alert.Status);

        var list = await client.GetFromJsonAsync<AlertListResponse>(
            $"/api/companies/{seed.CompanyId}/alerts?status=resolved&page=1&pageSize=10");

        Assert.NotNull(list);
        var item = Assert.Single(list!.Items);
        Assert.Equal(resolvedAlert.Alert.Id, item.Id);
    }

    [Theory]
    [InlineData("type", "risk", 1)]
    [InlineData("severity", "high", 1)]
    [InlineData("status", "open", 2)]
    public async Task Query_filters_alerts_by_classification_and_status(string filterName, string filterValue, int expectedCount)
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();

        await CreateAlertAsync(client, seed.CompanyId, "risk", "high", "open", "fp-filter-risk", seed.AgentId);
        await CreateAlertAsync(client, seed.CompanyId, "opportunity", "low", "open", "fp-filter-opportunity", seed.AgentId);

        var list = await client.GetFromJsonAsync<AlertListResponse>(
            $"/api/companies/{seed.CompanyId}/alerts?{filterName}={filterValue}&page=1&pageSize=10");

        Assert.NotNull(list);
        Assert.Equal(expectedCount, list!.TotalCount);
        Assert.All(list.Items, alert =>
        {
            if (filterName == "type")
            {
                Assert.Equal(filterValue, alert.Type);
            }
            else if (filterName == "severity")
            {
                Assert.Equal(filterValue, alert.Severity);
            }
            else
            {
                Assert.Equal(filterValue, alert.Status);
            }
        });
    }

    [Fact]
    public async Task Query_filters_alerts_by_created_at_range()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();
        var createdFrom = DateTime.UtcNow.AddMinutes(-1);

        await CreateAlertAsync(client, seed.CompanyId, "risk", "high", "open", "fp-created-range", seed.AgentId);

        var createdTo = DateTime.UtcNow.AddMinutes(1);
        var list = await client.GetFromJsonAsync<AlertListResponse>(
            $"/api/companies/{seed.CompanyId}/alerts?createdFrom={Uri.EscapeDataString(createdFrom.ToString("O"))}&createdTo={Uri.EscapeDataString(createdTo.ToString("O"))}&page=1&pageSize=10");

        Assert.NotNull(list);
        var alert = Assert.Single(list!.Items);
        Assert.Equal("risk", alert.Type);
        Assert.InRange(alert.CreatedAt, createdFrom, createdTo);
    }

    [Fact]
    public async Task Query_uses_stable_page_pagination_and_created_at_ordering()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();

        await CreateAlertAsync(client, seed.CompanyId, "risk", "low", "open", "fp-page-1", seed.AgentId);
        await CreateAlertAsync(client, seed.CompanyId, "risk", "medium", "open", "fp-page-2", seed.AgentId);
        await CreateAlertAsync(client, seed.CompanyId, "risk", "critical", "open", "fp-page-3", seed.AgentId);

        var firstPage = await client.GetFromJsonAsync<AlertListResponse>($"/api/companies/{seed.CompanyId}/alerts?page=1&pageSize=2");
        var secondPage = await client.GetFromJsonAsync<AlertListResponse>($"/api/companies/{seed.CompanyId}/alerts?page=2&pageSize=2");

        Assert.NotNull(firstPage);
        Assert.NotNull(secondPage);
        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal(1, firstPage.Page);
        Assert.Equal(2, secondPage.Page);
        Assert.True(firstPage.Items[0].CreatedAt >= firstPage.Items[1].CreatedAt);
        Assert.True(firstPage.Items[1].CreatedAt >= secondPage.Items[0].CreatedAt);
        Assert.Equal(3, firstPage.Items.Concat(secondPage.Items).Select(x => x.Id).Distinct().Count());
    }

    [Theory]
    [InlineData("page=0&pageSize=20")]
    [InlineData("page=1&pageSize=201")]
    [InlineData("createdFrom=2100-01-01T00:00:00Z&createdTo=2000-01-01T00:00:00Z")]
    public async Task Query_rejects_invalid_pagination_and_date_range(string query)
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.GetAsync($"/api/companies/{seed.CompanyId}/alerts?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Same_fingerprint_can_create_new_alert_after_resolution()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();
        var payload = CreatePayload(seed.CompanyId, "anomaly", "medium", "open", "fp-reopen", seed.AgentId);

        var first = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/alerts/detections", payload);
        var created = await first.Content.ReadFromJsonAsync<AlertMutationResponse>();
        Assert.NotNull(created);

        var update = await client.PutAsJsonAsync($"/api/companies/{seed.CompanyId}/alerts/{created!.Alert.Id}", new
        {
            severity = "medium",
            title = "Volume anomaly",
            summary = "Volume exceeded expected range.",
            evidence = new Dictionary<string, object?> { ["metric"] = "volume" },
            status = "resolved",
            metadata = new Dictionary<string, object?>()
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var second = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/alerts/detections", payload);
        var reopened = await second.Content.ReadFromJsonAsync<AlertMutationResponse>();

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.NotNull(reopened);
        Assert.True(reopened!.Created);
        Assert.NotEqual(created.Alert.Id, reopened.Alert.Id);
    }

    [Fact]
    public async Task Create_rejects_source_agent_from_another_company()
    {
        var seed = await SeedCompanyAsync();
        using var client = CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.OtherCompanyId}/alerts/detections",
            CreatePayload(seed.OtherCompanyId, "risk", "high", "open", "fp-wrong-agent-company", seed.AgentId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Contains("SourceAgentId", problem!.Errors.Keys);
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, "founder");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, "founder@example.com");
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, "Founder");
        return client;
    }

    private static async Task<AlertResponse> CreateAlertAsync(HttpClient client, Guid companyId, string type, string severity, string status, string fingerprint, Guid? sourceAgentId)
    {
        var response = await client.PostAsJsonAsync($"/api/companies/{companyId}/alerts", CreatePayload(companyId, type, severity, status, fingerprint, sourceAgentId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<AlertMutationResponse>();
        Assert.NotNull(created);
        return created!.Alert;
    }

    private static object CreatePayload(Guid companyId, string type, string severity, string status, string fingerprint, Guid? sourceAgentId) =>
        new
        {
            companyId,
            type,
            severity,
            title = type == "opportunity" ? "Upsell opportunity" : type == "anomaly" ? "Volume anomaly" : "Margin at risk",
            summary = "Detection summary.",
            evidence = new Dictionary<string, object?> { ["source"] = "test" },
            status,
            correlationId = $"corr-{fingerprint}",
            fingerprint,
            sourceAgentId,
            metadata = new Dictionary<string, object?>()
        };

    private async Task<AlertSeed> SeedCompanyAsync()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "founder@example.com", "Founder", "dev-header", "founder"));
            dbContext.Companies.AddRange(new Company(companyId, "Alert Company"), new Company(otherCompanyId, "Other Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "risk", "Risk Analyst", "Risk Analyst", "Risk", null, AgentSeniority.Lead, AgentStatus.Active));
            return Task.CompletedTask;
        });

        return new AlertSeed(companyId, otherCompanyId, agentId);
    }

    private sealed record AlertSeed(Guid CompanyId, Guid OtherCompanyId, Guid AgentId);

    private sealed class AlertMutationResponse
    {
        public AlertResponse Alert { get; set; } = new();
        public bool Created { get; set; }
        public bool Deduplicated { get; set; }
    }

    private sealed class AlertListResponse
    {
        public List<AlertResponse> Items { get; set; } = [];
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int Skip { get; set; }
    }

    private sealed class AlertResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string Fingerprint { get; set; } = string.Empty;
        public int OccurrenceCount { get; set; }
        public Dictionary<string, JsonElement> Evidence { get; set; } = [];
        public DateTime CreatedAt { get; set; }
    }
}
