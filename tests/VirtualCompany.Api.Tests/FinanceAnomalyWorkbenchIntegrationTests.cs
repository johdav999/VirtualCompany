using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAnomalyWorkbenchIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceAnomalyWorkbenchIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Finance_anomaly_workbench_endpoints_filter_paginate_and_expand_detail()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var filteredResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/anomalies/workbench?type=threshold_breach&status=awaiting_approval&confidenceMin=0.90&confidenceMax=1.00&supplier={Uri.EscapeDataString(seed.SupplierName)}&dateFromUtc={Uri.EscapeDataString(DateTime.UtcNow.AddDays(-1).ToString("O"))}&dateToUtc={Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O"))}&page=1&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);

        var filtered = await filteredResponse.Content.ReadFromJsonAsync<FinanceAnomalyWorkbenchResponse>();
        Assert.NotNull(filtered);
        Assert.Equal(1, filtered!.TotalCount);
        Assert.Single(filtered.Items);
        Assert.Equal(seed.FirstAlertId, filtered.Items[0].Id);
        Assert.Equal(seed.SupplierName, filtered.Items[0].SupplierName);
        Assert.Equal("finance-transaction-anomaly:dedupe", filtered.Items[0].Deduplication!.Key);
        Assert.True(filtered.Items[0].Deduplication!.WindowStartUtc.HasValue);
        Assert.True(filtered.Items[0].Deduplication!.WindowEndUtc.HasValue);
        Assert.NotNull(filtered.Items[0].Deduplication);
        Assert.Equal(seed.FirstTaskId, filtered.Items[0].FollowUpTaskId);
        Assert.Equal("awaiting_approval", filtered.Items[0].FollowUpTaskStatus);

        var secondPageResponse = await client.GetAsync(
            $"/internal/companies/{seed.CompanyId}/finance/anomalies/workbench?page=2&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, secondPageResponse.StatusCode);
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<FinanceAnomalyWorkbenchResponse>();
        Assert.NotNull(secondPage);
        Assert.Equal(2, secondPage!.TotalCount);
        Assert.Single(secondPage.Items);
        Assert.Equal(seed.SecondAlertId, secondPage.Items[0].Id);

        var detailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/anomalies/workbench/{seed.FirstAlertId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = await detailResponse.Content.ReadFromJsonAsync<FinanceAnomalyDetailResponse>();
        Assert.NotNull(detail);
        Assert.Equal(seed.FirstAlertId, detail!.Id);
        Assert.Equal("threshold_breach", detail.AnomalyType);
        Assert.Equal("awaiting_approval", detail.Status);
        Assert.NotNull(detail.AffectedRecord);
        Assert.Equal(seed.FirstTransactionId, detail.AffectedRecord!.Id);
        Assert.Contains(detail.RelatedRecordLinks, link => link.RecordType == "transaction" && link.RecordId == seed.FirstTransactionId);
        Assert.Equal(seed.FirstTaskId, Assert.Single(detail.FollowUpTasks).Id);
        Assert.NotNull(detail.Deduplication);
        Assert.Equal("finance-transaction-anomaly:dedupe", detail.Deduplication!.Key);
        Assert.True(detail.Deduplication.WindowStartUtc.HasValue);
        Assert.True(detail.Deduplication.WindowEndUtc.HasValue);

        var secondDetailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/anomalies/workbench/{seed.SecondAlertId}");
        Assert.Equal(HttpStatusCode.OK, secondDetailResponse.StatusCode);
        var secondDetail = await secondDetailResponse.Content.ReadFromJsonAsync<FinanceAnomalyDetailResponse>();
        Assert.NotNull(secondDetail);
        Assert.Null(secondDetail!.Deduplication);
    }

    [Fact]
    public async Task Finance_anomaly_workbench_respects_company_tenant_scope()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.OtherCompanyId}/finance/anomalies/workbench");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<FinanceAnomalySeed> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"finance-anomaly-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Anomaly Reviewer";
        var firstAlertId = Guid.Empty;
        var secondAlertId = Guid.Empty;
        var firstTaskId = Guid.Empty;
        var firstTransactionId = Guid.Empty;
        var supplierName = string.Empty;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Finance Anomaly Company"),
                new Company(otherCompanyId, "Other Finance Anomaly Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            FinanceSeedData.AddMockFinanceData(dbContext, companyId);
            FinanceSeedData.AddMockFinanceData(dbContext, otherCompanyId);

            var candidateTransactions = dbContext.FinanceTransactions.Local
                .Where(x => x.CompanyId == companyId && x.CounterpartyId.HasValue)
                .Take(2)
                .ToArray();

            var firstTransaction = candidateTransactions[0];
            var secondTransaction = candidateTransactions[1];
            var firstCounterparty = dbContext.FinanceCounterparties.Local.Single(x => x.Id == firstTransaction.CounterpartyId);
            var secondCounterparty = dbContext.FinanceCounterparties.Local.Single(x => x.Id == secondTransaction.CounterpartyId);
            supplierName = firstCounterparty.Name;
            firstTransactionId = firstTransaction.Id;

            var firstCorrelationId = $"fin-anom:{companyId:N}:{firstTransaction.Id:N}:threshold_breach:2026041600";
            var firstAlert = new Alert(
                Guid.NewGuid(),
                companyId,
                AlertType.Anomaly,
                AlertSeverity.High,
                $"Finance anomaly: threshold breach on {firstTransaction.ExternalReference}",
                "Transaction exceeded the configured threshold for review.",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["transactionId"] = JsonValue.Create(firstTransaction.Id),
                    ["transactionExternalReference"] = JsonValue.Create(firstTransaction.ExternalReference),
                    ["counterpartyName"] = JsonValue.Create(firstCounterparty.Name),
                    ["confidence"] = JsonValue.Create(0.94m),
                    ["recommendedAction"] = JsonValue.Create("Confirm the supplier intent and hold payment until reviewed."),
                    ["anomalyType"] = JsonValue.Create("threshold_breach"),
                    ["deduplicationWindowStartUtc"] = JsonValue.Create(DateTime.UtcNow.Date),
                    ["deduplicationWindowEndUtc"] = JsonValue.Create(DateTime.UtcNow.Date.AddHours(24))
                },
                firstCorrelationId,
                $"finance-transaction-anomaly:{companyId:N}:{firstTransaction.Id:N}:threshold_breach:window:2026041600",
                AlertStatus.Open,
                null,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["anomalyType"] = JsonValue.Create("threshold_breach"),
                    ["confidence"] = JsonValue.Create(0.94m),
                    ["recommendedAction"] = JsonValue.Create("Confirm the supplier intent and hold payment until reviewed."),
                    ["dedupeKey"] = JsonValue.Create("finance-transaction-anomaly:dedupe"),
                    ["deduplicationWindowStartUtc"] = JsonValue.Create(DateTime.UtcNow.Date),
                    ["deduplicationWindowEndUtc"] = JsonValue.Create(DateTime.UtcNow.Date.AddHours(24))
                });
            dbContext.Alerts.Add(firstAlert);
            firstAlertId = firstAlert.Id;

            var firstTask = new WorkTask(
                Guid.NewGuid(),
                companyId,
                "finance_transaction_anomaly_follow_up",
                $"Review anomalous transaction {firstTransaction.ExternalReference}",
                "Investigate the transaction and capture supplier evidence.",
                WorkTaskPriority.High,
                null,
                null,
                "agent",
                null,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["transactionId"] = JsonValue.Create(firstTransaction.Id),
                    ["alertId"] = JsonValue.Create(firstAlert.Id),
                    ["anomalyType"] = JsonValue.Create("threshold_breach")
                },
                null,
                null,
                "Investigate the anomaly before the payment proceeds.",
                0.94m,
                firstCorrelationId,
                WorkTaskSourceTypes.Agent,
                null,
                "finance_workflow_queue",
                "Finance anomaly workbench coverage",
                $"threshold-breach:{firstTransaction.Id:N}",
                WorkTaskStatus.AwaitingApproval);
            firstTask.SetDueDate(DateTime.UtcNow.AddDays(2));
            dbContext.WorkTasks.Add(firstTask);
            firstTaskId = firstTask.Id;

            var secondCorrelationId = $"fin-anom:{companyId:N}:{secondTransaction.Id:N}:historical_baseline_deviation:2026041600";
            var secondAlert = new Alert(
                Guid.NewGuid(),
                companyId,
                AlertType.Anomaly,
                AlertSeverity.Medium,
                $"Finance anomaly: historical baseline deviation on {secondTransaction.ExternalReference}",
                "Transaction deviates from the historical baseline.",
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["transactionId"] = JsonValue.Create(secondTransaction.Id),
                    ["transactionExternalReference"] = JsonValue.Create(secondTransaction.ExternalReference),
                    ["counterpartyName"] = JsonValue.Create(secondCounterparty.Name),
                    ["confidence"] = JsonValue.Create(0.68m),
                    ["recommendedAction"] = JsonValue.Create("Compare against similar transactions before closing the case."),
                    ["anomalyType"] = JsonValue.Create("historical_baseline_deviation")
                },
                secondCorrelationId,
                $"finance-transaction-anomaly:{companyId:N}:{secondTransaction.Id:N}:historical_baseline_deviation:window:2026041600",
                AlertStatus.Open,
                null,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["anomalyType"] = JsonValue.Create("historical_baseline_deviation"),
                    ["confidence"] = JsonValue.Create(0.68m),
                    ["recommendedAction"] = JsonValue.Create("Compare against similar transactions before closing the case.")
                });
            dbContext.Alerts.Add(secondAlert);
            secondAlertId = secondAlert.Id;

            var secondTask = new WorkTask(
                Guid.NewGuid(),
                companyId,
                "finance_transaction_anomaly_follow_up",
                $"Review anomalous transaction {secondTransaction.ExternalReference}",
                "Review the outlier transaction.",
                WorkTaskPriority.Normal,
                null,
                null,
                "agent",
                null,
                new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["transactionId"] = JsonValue.Create(secondTransaction.Id),
                    ["alertId"] = JsonValue.Create(secondAlert.Id),
                    ["anomalyType"] = JsonValue.Create("historical_baseline_deviation")
                },
                null,
                null,
                "Close after validating the comparison set.",
                0.68m,
                secondCorrelationId,
                WorkTaskSourceTypes.Agent,
                null,
                "finance_workflow_queue",
                "Finance anomaly workbench coverage",
                $"historical-baseline:{secondTransaction.Id:N}",
                WorkTaskStatus.Completed);
            dbContext.WorkTasks.Add(secondTask);

            return Task.CompletedTask;
        });

        return new FinanceAnomalySeed(companyId, otherCompanyId, subject, email, displayName, supplierName, firstAlertId, secondAlertId, firstTaskId, firstTransactionId);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }

    private sealed record FinanceAnomalySeed(
        Guid CompanyId,
        Guid OtherCompanyId,
        string Subject,
        string Email,
        string DisplayName,
        string SupplierName,
        Guid FirstAlertId,
        Guid SecondAlertId,
        Guid FirstTaskId,
        Guid FirstTransactionId);

    private sealed class FinanceAnomalyWorkbenchResponse
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public List<FinanceAnomalyWorkbenchItemResponse> Items { get; set; } = [];
    }

    private sealed class FinanceAnomalyWorkbenchItemResponse
    {
        public Guid Id { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public Guid? FollowUpTaskId { get; set; }
        public string? FollowUpTaskStatus { get; set; }
        public FinanceAnomalyDeduplicationResponse? Deduplication { get; set; }
    }

    private sealed class FinanceAnomalyDetailResponse
    {
        public Guid Id { get; set; }
        public string AnomalyType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public FinanceAnomalyRelatedRecordResponse? AffectedRecord { get; set; }
        public List<FinanceAnomalyRecordLinkResponse> RelatedRecordLinks { get; set; } = [];
        public FinanceAnomalyDeduplicationResponse? Deduplication { get; set; }
        public List<FinanceAnomalyFollowUpTaskResponse> FollowUpTasks { get; set; } = [];
    }

    private sealed class FinanceAnomalyRelatedRecordResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class FinanceAnomalyRecordLinkResponse
    {
        public Guid? RecordId { get; set; }
        public string RecordType { get; set; } = string.Empty;
        public string Reference { get; set; } = string.Empty;
    }

    private sealed class FinanceAnomalyFollowUpTaskResponse
    {
        public Guid Id { get; set; }
    }

    private sealed class FinanceAnomalyDeduplicationResponse
    {
        public string? Key { get; set; }
        public DateTime? WindowStartUtc { get; set; }
        public DateTime? WindowEndUtc { get; set; }
    }
}