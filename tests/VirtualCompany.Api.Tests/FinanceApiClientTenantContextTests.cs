using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Web.Services;
using Xunit;
using VirtualCompany.Shared;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceApiClientTenantContextTests
{
    [Fact]
    public async Task Finance_summary_requests_include_the_active_company_header()
    {
        var companyId = Guid.NewGuid();
        var anomalyId = Guid.NewGuid();
        var referenceUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var handler = new RecordingFinanceHttpMessageHandler(companyId, referenceUtc);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var financeClient = new FinanceApiClient(httpClient);

        await financeClient.GetCashPositionAsync(companyId, referenceUtc);
        await financeClient.GetBalancesAsync(companyId, referenceUtc);
        await financeClient.GetMonthlySummaryAsync(companyId, referenceUtc);
        await financeClient.GetAnomaliesAsync(companyId);
        await financeClient.GetAnomalyWorkbenchAsync(companyId, page: 1, pageSize: 50);
        await financeClient.GetAnomalyDetailAsync(companyId, anomalyId);

        Assert.Equal(7, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal($"/internal/companies/{companyId}/finance", request.PathPrefix);
            Assert.Equal(companyId.ToString(), request.CompanyHeaderValue);
        });
    }

    [Fact]
    public async Task Seed_generation_mutation_includes_the_active_company_header_and_payload()
    {
        var companyId = Guid.NewGuid();
        var request = new FinanceSandboxSeedGenerationRequest
        {
            CompanyId = companyId,
            SeedValue = 302,
            AnchorDateUtc = new DateTime(2026, 4, 15, 0, 0, 0, DateTimeKind.Utc),
            GenerationMode = FinanceSandboxSeedGenerationModes.RefreshWithAnomalies
        };
        var handler = new RecordingSeedGenerationHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var financeClient = new FinanceApiClient(httpClient);
        await financeClient.GenerateSandboxSeedDatasetAsync(companyId, request);

        Assert.NotNull(handler.CapturedRequest);
        Assert.Equal($"/internal/companies/{companyId}/finance/sandbox-admin/seed-generation", handler.CapturedRequest!.Path);
        Assert.Equal(companyId.ToString(), handler.CapturedRequest.CompanyHeaderValue);
        Assert.NotNull(handler.CapturedRequest.Payload);
        Assert.Equal(companyId, handler.CapturedRequest.Payload!.CompanyId);
        Assert.Equal(302, handler.CapturedRequest.Payload.SeedValue);
        Assert.Equal(FinanceSandboxSeedGenerationModes.RefreshWithAnomalies, handler.CapturedRequest.Payload.GenerationMode);
    }

    [Fact]
    public async Task Sandbox_admin_mutations_include_the_active_company_header_and_payload()
    {
        var companyId = Guid.NewGuid();
        var handler = new RecordingSandboxAdminMutationHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var financeClient = new FinanceApiClient(httpClient);

        await financeClient.InjectSandboxAnomalyAsync(companyId, new FinanceSandboxAnomalyInjectionRequest
        {
            CompanyId = companyId,
            ScenarioProfileCode = "duplicate_vendor_charge"
        });

        await financeClient.AdvanceSandboxSimulationAsync(companyId, new FinanceSandboxSimulationAdvanceRequest
        {
            CompanyId = companyId,
            IncrementHours = 24,
            ExecutionStepHours = 24,
            Accelerated = true
        });

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(companyId.ToString(), request.CompanyHeaderValue);
            Assert.Equal(companyId, request.CompanyId);
        });
        Assert.Contains(handler.Requests, request => request.Path.EndsWith("/sandbox-admin/anomaly-injection", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Path.EndsWith("/sandbox-admin/simulation-controls/advance", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetSeedingStateAsync_uses_shared_company_scoped_endpoint_and_preserves_contract()
    {
        var companyId = Guid.NewGuid();
        var referenceUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc);
        var handler = new RecordingFinanceHttpMessageHandler(companyId, referenceUtc);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var financeClient = new FinanceApiClient(httpClient);

        var response = await financeClient.GetSeedingStateAsync(companyId);

        Assert.NotNull(response);
        Assert.Equal(companyId, response!.CompanyId);
        Assert.Equal(FinanceSeedingStateContractValues.PartiallySeeded, response.SeedingState);
        Assert.Equal("record_checks", response.DerivedFrom);
        Assert.Single(handler.Requests);
        Assert.Equal($"/internal/companies/{companyId}/finance/seeding-state", handler.Requests[0].Path);
        Assert.Equal(companyId.ToString(), handler.Requests[0].CompanyHeaderValue);
    }

    [Fact]
    public async Task RequestEntryInitializationAsync_uses_shared_company_scoped_endpoint_and_preserves_contract()
    {
        var companyId = Guid.NewGuid();
        var referenceUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc);
        var handler = new RecordingFinanceHttpMessageHandler(companyId, referenceUtc);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var financeClient = new FinanceApiClient(httpClient);
        var response = await financeClient.RequestEntryInitializationAsync(companyId);

        Assert.Equal(companyId, response.CompanyId);
        Assert.Equal(FinanceEntryProgressStateContractValues.SeedingRequested, response.ProgressState);
        Assert.Single(handler.Requests);
        Assert.Equal($"/internal/companies/{companyId}/finance/entry-state/request", handler.Requests[0].Path);
        Assert.Equal(companyId.ToString(), handler.Requests[0].CompanyHeaderValue);
    }

    [Fact]
    public async Task RequestManualSeedAsync_uses_shared_company_scoped_endpoint_and_preserves_contract()
    {
        var companyId = Guid.NewGuid();
        var referenceUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc);
        var handler = new RecordingFinanceHttpMessageHandler(companyId, referenceUtc);
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost/")
        };

        var financeClient = new FinanceApiClient(httpClient);
        var response = await financeClient.RequestManualSeedAsync(companyId, new FinanceManualSeedRequest { Mode = FinanceManualSeedModes.Replace, ConfirmReplace = true });

        Assert.Equal(companyId, response.CompanyId);
        Assert.Equal(FinanceEntryProgressStateContractValues.SeedingRequested, response.ProgressState);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal($"/internal/companies/{companyId}/finance/manual-seed", handler.Requests[1].Path);
        Assert.Equal(companyId.ToString(), handler.Requests[1].CompanyHeaderValue);
        Assert.NotNull(handler.LastManualSeedRequest);
        Assert.True(handler.LastManualSeedRequest!.ConfirmReplace);
    }

    private sealed class RecordingFinanceHttpMessageHandler : HttpMessageHandler
    {
        private readonly Guid _companyId;
        private readonly DateTime _referenceUtc;

        public RecordingFinanceHttpMessageHandler(Guid companyId, DateTime referenceUtc)
        {
            _companyId = companyId;
            _referenceUtc = referenceUtc;
        }

        public List<CapturedRequest> Requests { get; } = [];
        public FinanceManualSeedRequest? LastManualSeedRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/manual-seed", StringComparison.Ordinal) && request.Content is not null)
            {
                LastManualSeedRequest = await request.Content.ReadFromJsonAsync<FinanceManualSeedRequest>(cancellationToken: cancellationToken);
            }

            Requests.Add(new CapturedRequest(
                path,
                $"/internal/companies/{_companyId}/finance",
                request.Headers.TryGetValues("X-Company-Id", out var values) ? values.Single() : null));

            return path switch
            {
                var value when value.EndsWith("/cash-position", StringComparison.Ordinal) => Json(new
                {
                    companyId = _companyId,
                    asOfUtc = _referenceUtc,
                    availableBalance = 150000.25m,
                    currency = "USD",
                    averageMonthlyBurn = 25000m,
                    estimatedRunwayDays = 180,
                    thresholds = new
                    {
                        warningRunwayDays = 90,
                        criticalRunwayDays = 45,
                        warningCashAmount = 50000m,
                        criticalCashAmount = 25000m,
                        currency = "USD"
                    },
                    alertState = new
                    {
                        isLowCash = false,
                        riskLevel = "healthy",
                        alertCreated = false,
                        alertDeduplicated = false,
                        alertId = (Guid?)null,
                        alertStatus = "no_active_alert",
                        rationale = "Runway is stable."
                    },
                    classification = "stable",
                    riskLevel = "healthy",
                    recommendedAction = "Continue monitoring.",
                    rationale = "Runway is stable.",
                    confidence = 0.85m,
                    sourceWorkflow = "cash_position_monitor"
                }),
                var value when value.EndsWith("/balances", StringComparison.Ordinal) => Json(new[]
                {
                    new
                    {
                        accountId = Guid.NewGuid(),
                        accountCode = "1000",
                        accountName = "Operating",
                        accountType = "asset",
                        amount = 150000.25m,
                        currency = "USD",
                        asOfUtc = _referenceUtc
                    }
                }),
                var value when value.EndsWith("/profit-and-loss/monthly", StringComparison.Ordinal) => Json(new
                {
                    companyId = _companyId,
                    year = _referenceUtc.Year,
                    month = _referenceUtc.Month,
                    startUtc = new DateTime(_referenceUtc.Year, _referenceUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                    endUtc = new DateTime(_referenceUtc.Year, _referenceUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
                    revenue = 80000m,
                    expenses = 45000m,
                    netResult = 35000m,
                    currency = "USD"
                }),
                var value when value.EndsWith("/expense-breakdown", StringComparison.Ordinal) => Json(new
                {
                    companyId = _companyId,
                    startUtc = new DateTime(_referenceUtc.Year, _referenceUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                    endUtc = new DateTime(_referenceUtc.Year, _referenceUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
                    totalExpenses = 45000m,
                    currency = "USD",
                    categories = new[]
                    {
                        new
                        {
                            category = "software",
                            amount = 15000m,
                            currency = "USD"
                        }
                    }
                }),
                var value when value.EndsWith("/seeding-state", StringComparison.Ordinal) => Json(new
                {
                    companyId = _companyId,
                    seedingState = FinanceSeedingStateContractValues.PartiallySeeded,
                    derivedFrom = "record_checks",
                    checkedAtUtc = _referenceUtc,
                    diagnostics = new
                    {
                        persistedState = FinanceSeedingStateContractValues.NotSeeded,
                        metadataState = (string?)null,
                        metadataPresent = false,
                        metadataIndicatesComplete = false,
                        usedFastPath = false,
                        reason = "Some finance indicators exist, but the foundational seeded dataset is incomplete.",
                        hasAccounts = true,
                        hasTransactions = false,
                        hasBalances = false,
                        hasPolicyConfiguration = false
                    }
                }),
                var value when value.EndsWith("/entry-state", StringComparison.Ordinal) => Json(new
                {
                    companyId = _companyId,
                    initializationStatus = FinanceEntryInitializationContractValues.Initializing,
                    progressState = FinanceEntryProgressStateContractValues.NotSeeded,
                    seedingState = FinanceSeedingStateContractValues.NotSeeded,
                    seedJobEnqueued = false,
                    seedJobActive = false,
                    canRetry = false,
                    canRefresh = false,
                    message = "Finance data has not been initialized yet.",
                    checkedAtUtc = _referenceUtc
                }),
                var value when value.EndsWith("/entry-state/request", StringComparison.Ordinal) => Json(new
                {
                    companyId = _companyId,
                    initializationStatus = FinanceEntryInitializationContractValues.Initializing,
                    progressState = FinanceEntryProgressStateContractValues.SeedingRequested,
                    seedingState = FinanceSeedingStateContractValues.NotSeeded,
                    seedJobEnqueued = true,
                    seedJobActive = true,
                    canRetry = false,
                    canRefresh = true,
                    message = "Finance setup was requested in the background.",
                    checkedAtUtc = _referenceUtc,
                    jobStatus = "pending"
                }),
                var value when value.EndsWith("/manual-seed", StringComparison.Ordinal) => Json(new
                {
                    companyId = _companyId,
                    initializationStatus = FinanceEntryInitializationContractValues.Initializing,
                    progressState = FinanceEntryProgressStateContractValues.SeedingRequested,
                    seedingState = FinanceSeedingStateContractValues.Seeding,
                    seedJobEnqueued = true,
                    seedJobActive = true,
                    canRetry = false,
                    canRefresh = true,
                    message = "Finance seed regeneration was requested in the background.",
                    checkedAtUtc = _referenceUtc,
                    jobStatus = "pending"
                }),
                var value when value.EndsWith("/anomalies", StringComparison.Ordinal) => Json(new[]
                {
                    new
                    {
                        id = Guid.NewGuid(),
                        anomalyType = "missing_receipt",
                        scenarioProfile = "integration",
                        affectedRecordIds = new[] { Guid.NewGuid() },
                        expectedDetectionMetadataJson = """{"expectedDetector":"receipt_completeness"}"""
                    }
                }),
                var value when value.EndsWith("/anomalies/workbench", StringComparison.Ordinal) => Json(new
                {
                    totalCount = 1,
                    page = 1,
                    pageSize = 50,
                    items = new[]
                    {
                        new
                        {
                            id = Guid.NewGuid(),
                            anomalyType = "threshold_breach",
                            status = "awaiting_approval",
                            confidence = 0.94m,
                            supplierName = "Contoso Supplies",
                            affectedRecordId = Guid.NewGuid(),
                            affectedRecordReference = "TX-1001",
                            explanationSummary = "Threshold exceeded.",
                            recommendedAction = "Review supplier evidence.",
                            detectedAtUtc = _referenceUtc,
                            deduplication = new
                            {
                                key = "finance-transaction-anomaly:dedupe",
                                windowStartUtc = _referenceUtc,
                                windowEndUtc = _referenceUtc.AddHours(24)
                            },
                            followUpTaskId = Guid.NewGuid(),
                            followUpTaskStatus = "awaiting_approval",
                            relatedInvoiceId = (Guid?)null,
                            relatedBillId = (Guid?)null
                        }
                    }
                }),
                var value when value.Contains("/anomalies/workbench/", StringComparison.Ordinal) => Json(new
                {
                    id = Guid.NewGuid(),
                    anomalyType = "threshold_breach",
                    status = "awaiting_approval",
                    confidence = 0.94m,
                    supplierName = "Contoso Supplies",
                    explanation = "Threshold exceeded.",
                    recommendedAction = "Review supplier evidence.",
                    detectedAtUtc = _referenceUtc,
                    deduplication = new
                    {
                        key = "finance-transaction-anomaly:dedupe",
                        windowStartUtc = _referenceUtc,
                        windowEndUtc = _referenceUtc.AddHours(24)
                    },
                    affectedRecord = new
                    {
                        id = Guid.NewGuid(),
                        reference = "TX-1001",
                        occurredAtUtc = _referenceUtc,
                        amount = 1234.56m,
                        currency = "USD",
                        supplierName = "Contoso Supplies"
                    },
                    relatedInvoiceId = (Guid?)null,
                    relatedInvoiceReference = (string?)null,
                    relatedBillId = (Guid?)null,
                    relatedBillReference = (string?)null,
                    followUpTasks = new[]
                    {
                        new
                        {
                            id = Guid.NewGuid(),
                            title = "Review anomalous transaction TX-1001",
                            status = "awaiting_approval",
                            createdUtc = _referenceUtc,
                            dueUtc = _referenceUtc.AddDays(2),
                            updatedUtc = _referenceUtc
                        }
                    }
                }),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        }

        private static HttpResponseMessage Json<T>(T payload) =>
            new(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(payload)
            };
    }

    private sealed class RecordingSeedGenerationHttpMessageHandler : HttpMessageHandler
    {
        public CapturedSeedGenerationRequest? CapturedRequest { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CapturedRequest = new CapturedSeedGenerationRequest(
                request.RequestUri?.AbsolutePath ?? string.Empty,
                request.Headers.TryGetValues("X-Company-Id", out var values) ? values.Single() : null,
                request.Content is null ? null : await request.Content.ReadFromJsonAsync<FinanceSandboxSeedGenerationRequest>(cancellationToken: cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new FinanceSandboxSeedGenerationResponse
                {
                    CompanyId = CapturedRequest.Payload?.CompanyId ?? Guid.Empty,
                    SeedValue = CapturedRequest.Payload?.SeedValue ?? 0,
                    AnchorDateUtc = CapturedRequest.Payload?.AnchorDateUtc ?? DateTime.UtcNow,
                    GenerationMode = CapturedRequest.Payload?.GenerationMode ?? string.Empty,
                    Succeeded = true,
                    CreatedCount = 42,
                    UpdatedCount = 0,
                    Message = "Seed dataset generated successfully."
                })
            };
        }
    }

    private sealed class RecordingSandboxAdminMutationHttpMessageHandler : HttpMessageHandler
    {
        public List<CapturedSandboxMutationRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/sandbox-admin/anomaly-injection", StringComparison.Ordinal))
            {
                var payload = await request.Content!.ReadFromJsonAsync<FinanceSandboxAnomalyInjectionRequest>(cancellationToken: cancellationToken);
                Requests.Add(new CapturedSandboxMutationRequest(path, request.Headers.GetValues("X-Company-Id").Single(), payload!.CompanyId));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new FinanceSandboxAnomalyDetailResponse
                    {
                        Id = Guid.NewGuid(),
                        Type = "duplicate_vendor_charge",
                        Status = "registered",
                        ScenarioProfileCode = payload.ScenarioProfileCode,
                        ScenarioProfileName = "Duplicate vendor charge",
                        AffectedRecordType = "transaction",
                        AffectedRecordId = Guid.NewGuid(),
                        AffectedRecordReference = "SIM-TX-1001",
                        CreatedUtc = DateTime.UtcNow,
                        ExpectedDetectionMetadataJson = """{"scenarioProfileCode":"duplicate_vendor_charge"}"""
                    })
                };
            }

            var simulationPayload = await request.Content!.ReadFromJsonAsync<FinanceSandboxSimulationAdvanceRequest>(cancellationToken: cancellationToken);
            Requests.Add(new CapturedSandboxMutationRequest(path, request.Headers.GetValues("X-Company-Id").Single(), simulationPayload!.CompanyId));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new FinanceSandboxProgressionRunSummaryResponse
                {
                    RunType = "progression_run",
                    Status = "completed",
                    StartedUtc = DateTime.UtcNow.AddHours(-24),
                    CompletedUtc = DateTime.UtcNow,
                    AdvancedHours = simulationPayload.IncrementHours,
                    ExecutionStepHours = simulationPayload.ExecutionStepHours ?? 24,
                    TransactionsGenerated = 4,
                    InvoicesGenerated = 2,
                    BillsGenerated = 1,
                    RecurringExpenseInstancesGenerated = 1,
                    EventsEmitted = 1
                })
            };
        }
    }

    private sealed record CapturedRequest(
        string Path,
        string PathPrefix,
        string? CompanyHeaderValue);

    private sealed record CapturedSeedGenerationRequest(
        string Path,
        string? CompanyHeaderValue,
        FinanceSandboxSeedGenerationRequest? Payload);

    private sealed record CapturedSandboxMutationRequest(
        string Path,
        string? CompanyHeaderValue,
        Guid CompanyId);
}