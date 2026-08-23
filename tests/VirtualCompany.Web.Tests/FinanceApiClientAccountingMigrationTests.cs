using System.Net;
using System.Text;
using System.Text.Json;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class FinanceApiClientAccountingMigrationTests
{
    [Fact]
    public async Task Migration_client_uses_narrow_company_scoped_routes_verbs_and_serialization()
    {
        var companyId = Guid.NewGuid();
        var switchId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var transport = new RecordingTransport();
        var client = new FinanceApiClient(transport);

        await client.GetAccountingProviderSwitchesAsync(companyId);
        await client.GetAccountingProviderSwitchAllowedActionsAsync(companyId, switchId);
        await client.GetAccountingMigrationGuidanceAsync(companyId, switchId);
        await client.GetAccountingMigrationEvidenceAsync(companyId, switchId, "gaps", 12);
        await client.GetAccountingProviderSwitchMappingsAsync(companyId, switchId, 25);
        await client.StartAccountingProviderSwitchAssessmentAsync(companyId, switchId, new()
        {
            ExpectedSwitchVersion = 7,
            IdempotencyKey = "assessment-7"
        });
        await client.ReconcileAccountingProviderSwitchTargetTransferItemAsync(companyId, switchId, batchId, itemId,
            new() { ProviderConfirmedSuccess = false, Summary = "Provider confirmed no action.", ExpectedItemVersion = 3 });

        Assert.Collection(transport.Requests,
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/accounting/provider-switches?limit=50"),
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/allowed-actions"),
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/guidance"),
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/evidence/gaps?limit=12"),
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/mappings?limit=25"),
            request =>
            {
                AssertRequest(request, companyId, HttpMethod.Post,
                    $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/assessments");
                using var json = JsonDocument.Parse(request.Content!);
                Assert.Equal(7, json.RootElement.GetProperty("expectedSwitchVersion").GetInt64());
                Assert.Equal("assessment-7", json.RootElement.GetProperty("idempotencyKey").GetString());
            },
            request =>
            {
                AssertRequest(request, companyId, HttpMethod.Post,
                    $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/target-transfer-batches/{batchId}/items/{itemId}/reconcile");
                using var json = JsonDocument.Parse(request.Content!);
                Assert.False(json.RootElement.GetProperty("providerConfirmedSuccess").GetBoolean());
                Assert.Equal(3, json.RootElement.GetProperty("expectedItemVersion").GetInt64());
            });
    }

    [Fact]
    public async Task Monitoring_client_uses_company_scoped_reads_and_explicit_recovery_mutations()
    {
        var companyId = Guid.NewGuid(); var switchId = Guid.NewGuid(); var incidentId = Guid.NewGuid();
        var periodId = Guid.NewGuid(); var transport = new RecordingTransport(); var client = new FinanceApiClient(transport);

        await client.GetAccountingProviderSwitchMonitoringAsync(companyId, switchId);
        await client.GetAccountingProviderSwitchOperationsAsync(companyId);
        await client.RunAccountingProviderSwitchMonitoringActionAsync(companyId, switchId, "retry", 7);
        await client.AcceptAccountingProviderSwitchMonitoringExceptionAsync(companyId, switchId, incidentId,
            new() { ExpectedVersion = 3, Explanation = "Immaterial.", Scope = "One item", FinancialImpact = 5m,
                EvidenceReference = "archive://evidence/5" });
        await client.CreateCorrectiveAccountingProviderSwitchAsync(companyId, switchId,
            new() { EffectiveFiscalPeriodId = periodId, ExpectedVersion = 8, Reason = "Blocking variance." });

        Assert.Collection(transport.Requests,
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/monitoring"),
            request => AssertRequest(request, companyId, HttpMethod.Get,
                $"internal/companies/{companyId}/finance/accounting/provider-switches/operations"),
            request => AssertRequest(request, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/monitoring/retry"),
            request => AssertRequest(request, companyId, HttpMethod.Post,
                $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/monitoring/incidents/{incidentId}/accept-exception"),
            request =>
            {
                AssertRequest(request, companyId, HttpMethod.Post,
                    $"internal/companies/{companyId}/finance/accounting/provider-switches/{switchId}/monitoring/corrective-cutover");
                using var json = JsonDocument.Parse(request.Content!);
                Assert.Equal(periodId, json.RootElement.GetProperty("effectiveFiscalPeriodId").GetGuid());
                Assert.Equal(8, json.RootElement.GetProperty("expectedVersion").GetInt64());
            });
    }

    [Fact]
    public async Task Migration_optional_reads_map_not_found_to_empty_and_failures_to_safe_exception()
    {
        var companyId = Guid.NewGuid();
        var switchId = Guid.NewGuid();
        var notFound = new RecordingTransport(HttpStatusCode.NotFound);
        var missing = await new FinanceApiClient(notFound)
            .GetLatestAccountingProviderSwitchRehearsalAsync(companyId, switchId);
        Assert.Null(missing);

        var conflict = new RecordingTransport(HttpStatusCode.Conflict,
            "{\"title\":\"Migration changed\",\"detail\":\"Refresh current evidence before trying again.\"}");
        var exception = await Assert.ThrowsAsync<FinanceApiException>(() => new FinanceApiClient(conflict)
            .StartAccountingProviderSwitchAssessmentAsync(companyId, switchId, new()));
        Assert.Contains("Refresh current evidence", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migration_client_forwards_cancellation_to_company_transport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transport = new RecordingTransport();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FinanceApiClient(transport)
            .GetAccountingMigrationGuidanceAsync(Guid.NewGuid(), Guid.NewGuid(), cancellation.Token));
        Assert.True(transport.LastCancellationToken.IsCancellationRequested);
    }

    private static void AssertRequest(RecordedRequest request, Guid companyId, HttpMethod method, string uri)
    {
        Assert.Equal(companyId, request.CompanyId);
        Assert.Equal(method, request.Method);
        Assert.Equal(uri, request.Uri);
    }

    private sealed class RecordingTransport(HttpStatusCode status = HttpStatusCode.OK, string? responseJson = null)
        : ICompanyApiTransport
    {
        public Uri? BaseAddress => new("https://example.test/");
        public List<RecordedRequest> Requests { get; } = [];
        public CancellationToken LastCancellationToken { get; private set; }

        public async Task<HttpResponseMessage> SendAsync(Guid companyId, HttpMethod method, string uri,
            HttpContent? content, CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            cancellationToken.ThrowIfCancellationRequested();
            var body = content is null ? null : await content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new(companyId, method, uri, body));
            var isList = method == HttpMethod.Get &&
                (uri.Contains("provider-switches?", StringComparison.Ordinal) ||
                 uri.Contains("/mappings?", StringComparison.Ordinal));
            var json = responseJson ?? (isList ? "[]" : "{}");
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8,
                    status == HttpStatusCode.Conflict ? "application/problem+json" : "application/json")
            };
        }
    }

    private sealed record RecordedRequest(Guid CompanyId, HttpMethod Method, string Uri, string? Content);
}
