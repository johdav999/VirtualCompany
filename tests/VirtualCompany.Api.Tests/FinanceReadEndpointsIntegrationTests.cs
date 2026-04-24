using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceReadEndpointsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceReadEndpointsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Internal_finance_read_endpoints_return_typed_company_scoped_payloads()
    {
        var seed = await SeedFinanceCompanyAsync();
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var asOfUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var cashResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/cash-balance?asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, cashResponse.StatusCode);
        var cashBalance = await cashResponse.Content.ReadFromJsonAsync<CashBalanceResponse>();
        Assert.NotNull(cashBalance);
        Assert.Equal(seed.CompanyId, cashBalance!.CompanyId);
        Assert.Equal("USD", cashBalance.Currency);
        Assert.NotEmpty(cashBalance.Accounts);

        var cashPositionResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/cash-position?asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, cashPositionResponse.StatusCode);
        var cashPosition = await cashPositionResponse.Content.ReadFromJsonAsync<CashPositionResponse>();
        Assert.NotNull(cashPosition);
        Assert.Equal(seed.CompanyId, cashPosition!.CompanyId);
        Assert.Equal(cashBalance.Amount, cashPosition.AvailableBalance);
        Assert.Equal(cashBalance.Currency, cashPosition.Currency);
        Assert.NotNull(cashPosition.Thresholds);

        var seedingStateResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/seeding-state");
        Assert.Equal(HttpStatusCode.OK, seedingStateResponse.StatusCode);
        var seedingState = await seedingStateResponse.Content.ReadFromJsonAsync<FinanceSeedingStateResponse>();

        Assert.NotNull(seedingState);
        Assert.Equal(seed.CompanyId, seedingState!.CompanyId);
        Assert.Equal(FinanceSeedingStateContractValues.FullySeeded, seedingState.SeedingState);
        Assert.Equal("record_checks", seedingState.DerivedFrom);
        Assert.True(seedingState.CheckedAtUtc > DateTime.MinValue);
        Assert.NotNull(seedingState.Diagnostics);
        Assert.True(seedingState.Diagnostics.HasAccounts);
        Assert.True(seedingState.Diagnostics.HasPolicyConfiguration);

        var profitAndLossResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/profit-and-loss/monthly?year=2025&month=12");
        Assert.Equal(HttpStatusCode.OK, profitAndLossResponse.StatusCode);
        var profitAndLoss = await profitAndLossResponse.Content.ReadFromJsonAsync<MonthlyProfitAndLossResponse>();
        Assert.NotNull(profitAndLoss);
        Assert.Equal(seed.CompanyId, profitAndLoss!.CompanyId);
        Assert.Equal(2025, profitAndLoss.Year);
        Assert.Equal(12, profitAndLoss.Month);
        Assert.Equal(profitAndLoss.Revenue - profitAndLoss.Expenses, profitAndLoss.NetResult);

        var startUtc = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var breakdownResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/expense-breakdown?startUtc={Uri.EscapeDataString(startUtc.ToString("O"))}&endUtc={Uri.EscapeDataString(endUtc.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, breakdownResponse.StatusCode);
        var breakdown = await breakdownResponse.Content.ReadFromJsonAsync<ExpenseBreakdownResponse>();
        Assert.NotNull(breakdown);
        Assert.Equal(seed.CompanyId, breakdown!.CompanyId);
        Assert.NotEmpty(breakdown.Categories);
        Assert.Equal(breakdown.Categories.Sum(x => x.Amount), breakdown.TotalExpenses);

        var transactionsResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions?limit=3");
        Assert.Equal(HttpStatusCode.OK, transactionsResponse.StatusCode);
        var transactions = await transactionsResponse.Content.ReadFromJsonAsync<List<TransactionResponse>>();
        Assert.NotNull(transactions);
        Assert.Equal(3, transactions!.Count);
        Assert.All(transactions, transaction => Assert.NotEqual(Guid.Empty, transaction.Id));
        Assert.Contains(transactions, transaction => transaction.LinkedDocument is not null);

        var invoicesResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices?limit=2");
        Assert.Equal(HttpStatusCode.OK, invoicesResponse.StatusCode);
        var invoices = await invoicesResponse.Content.ReadFromJsonAsync<List<InvoiceResponse>>();
        Assert.NotNull(invoices);
        Assert.Equal(2, invoices!.Count);
        Assert.All(invoices, invoice => Assert.NotEqual(Guid.Empty, invoice.Id));
        Assert.Contains(invoices, invoice => invoice.LinkedDocument is not null);

        var billsResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/bills?limit=5");
        Assert.Equal(HttpStatusCode.OK, billsResponse.StatusCode);
        var bills = await billsResponse.Content.ReadFromJsonAsync<List<BillResponse>>();
        Assert.NotNull(bills);
        Assert.Equal(5, bills!.Count);
        Assert.Contains(bills, bill => bill.LinkedDocument is not null);

        var customersResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/customers?limit=10");
        Assert.Equal(HttpStatusCode.OK, customersResponse.StatusCode);
        var customers = await customersResponse.Content.ReadFromJsonAsync<List<CounterpartyResponse>>();
        Assert.NotNull(customers);
        Assert.NotEmpty(customers!);
        Assert.All(customers, x =>
        {
            Assert.Equal("customer", x.CounterpartyType);
            Assert.False(string.IsNullOrWhiteSpace(x.PaymentTerms));
            Assert.NotNull(x.CreditLimit);
            Assert.False(string.IsNullOrWhiteSpace(x.PreferredPaymentMethod));
            Assert.False(string.IsNullOrWhiteSpace(x.DefaultAccountMapping));
        });

        var suppliersResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/suppliers?limit=10");
        Assert.Equal(HttpStatusCode.OK, suppliersResponse.StatusCode);
        var suppliers = await suppliersResponse.Content.ReadFromJsonAsync<List<CounterpartyResponse>>();
        Assert.NotNull(suppliers);
        Assert.NotEmpty(suppliers!);
        Assert.All(suppliers, x =>
        {
            Assert.Equal("supplier", x.CounterpartyType);
            Assert.False(string.IsNullOrWhiteSpace(x.PaymentTerms));
            Assert.NotNull(x.CreditLimit);
            Assert.False(string.IsNullOrWhiteSpace(x.PreferredPaymentMethod));
            Assert.False(string.IsNullOrWhiteSpace(x.DefaultAccountMapping));
        });

        var linkedDocuments = transactions.Select(x => x.LinkedDocument)
            .Concat(invoices.Select(x => x.LinkedDocument))
            .Concat(bills.Select(x => x.LinkedDocument))
            .Where(x => x is not null)
            .ToArray();
        Assert.All(linkedDocuments, document => Assert.Contains(document!.Id, seed.DocumentIds));

        var balancesResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/balances?asOfUtc={Uri.EscapeDataString(asOfUtc.ToString("O"))}");
        Assert.Equal(HttpStatusCode.OK, balancesResponse.StatusCode);
        var balances = await balancesResponse.Content.ReadFromJsonAsync<List<AccountBalanceResponse>>();
        Assert.NotNull(balances);
        Assert.Equal(3, balances!.Count);
        Assert.All(balances, balance => Assert.Equal("USD", balance.Currency));

        var anomaliesResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/anomalies");
        Assert.Equal(HttpStatusCode.OK, anomaliesResponse.StatusCode);
        var anomalies = await anomaliesResponse.Content.ReadFromJsonAsync<List<SeedAnomalyResponse>>();
        Assert.NotNull(anomalies);
        Assert.Single(anomalies!);
        Assert.Equal(seed.AnomalyId, anomalies[0].Id);

        var anomalyByTypeResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/anomalies?type=missing_receipt");
        Assert.Equal(HttpStatusCode.OK, anomalyByTypeResponse.StatusCode);
        var anomaliesByType = await anomalyByTypeResponse.Content.ReadFromJsonAsync<List<SeedAnomalyResponse>>();
        Assert.NotNull(anomaliesByType);
        Assert.Single(anomaliesByType!);
        Assert.Equal(seed.AnomalyId, anomaliesByType[0].Id);

        var anomalyByAffectedRecordResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/anomalies?affectedRecordId={seed.AffectedRecordId}");
        Assert.Equal(HttpStatusCode.OK, anomalyByAffectedRecordResponse.StatusCode);
        var anomaliesByAffectedRecord = await anomalyByAffectedRecordResponse.Content.ReadFromJsonAsync<List<SeedAnomalyResponse>>();
        Assert.NotNull(anomaliesByAffectedRecord);
        Assert.Single(anomaliesByAffectedRecord!);
        Assert.Equal(seed.AnomalyId, anomaliesByAffectedRecord[0].Id);

        var filteredTransactionsResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions?category=customer_payment&flagged=flagged&limit=10");
        Assert.Equal(HttpStatusCode.OK, filteredTransactionsResponse.StatusCode);
        var filteredTransactions = await filteredTransactionsResponse.Content.ReadFromJsonAsync<List<TransactionResponse>>();
        Assert.NotNull(filteredTransactions);
        Assert.NotEmpty(filteredTransactions!);
        Assert.All(filteredTransactions, transaction =>
        {
            Assert.Equal("customer_payment", transaction.TransactionType);
            Assert.True(transaction.IsFlagged);
        });

        var firstInvoice = Assert.Single(invoices.Where(x => x.Id == seed.ReviewInvoiceId));
        var invoiceCounterparty = await client.GetFromJsonAsync<CounterpartyResponse>($"/internal/companies/{seed.CompanyId}/finance/customers/{firstInvoice.CounterpartyId}");
        Assert.NotNull(invoiceCounterparty);
        Assert.Equal(firstInvoice.CounterpartyId, invoiceCounterparty!.Id);
        Assert.Equal(firstInvoice.CounterpartyName, invoiceCounterparty.Name);
        Assert.False(string.IsNullOrWhiteSpace(invoiceCounterparty.PaymentTerms));
        Assert.NotNull(invoiceCounterparty.CreditLimit);
        Assert.False(string.IsNullOrWhiteSpace(invoiceCounterparty.PreferredPaymentMethod));
        Assert.False(string.IsNullOrWhiteSpace(invoiceCounterparty.DefaultAccountMapping));

        var firstBill = bills.First();
        var billCounterparty = await client.GetFromJsonAsync<CounterpartyResponse>($"/internal/companies/{seed.CompanyId}/finance/suppliers/{firstBill.CounterpartyId}");
        Assert.NotNull(billCounterparty);
        Assert.Equal(firstBill.CounterpartyId, billCounterparty!.Id);
        Assert.Equal(firstBill.CounterpartyName, billCounterparty.Name);
        Assert.False(string.IsNullOrWhiteSpace(billCounterparty.PaymentTerms));
        Assert.NotNull(billCounterparty.CreditLimit);
        Assert.False(string.IsNullOrWhiteSpace(billCounterparty.PreferredPaymentMethod));
        Assert.False(string.IsNullOrWhiteSpace(billCounterparty.DefaultAccountMapping));
    }

    [Fact]
    public async Task Finance_seeding_state_endpoint_uses_metadata_fast_path_when_complete_metadata_is_self_consistent()
    {
        var seed = await SeedFinanceSeedingStateCompanyAsync((_, company, _) =>
        {
            ApplySeedingMetadata(company, FinanceSeedingState.FullySeeded);
        });
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/seeding-state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var seedingState = await response.Content.ReadFromJsonAsync<FinanceSeedingStateResponse>();

        Assert.NotNull(seedingState);
        Assert.Equal(seed.CompanyId, seedingState!.CompanyId);
        Assert.Equal(FinanceSeedingStateContractValues.FullySeeded, seedingState.SeedingState);
        Assert.Equal("metadata", seedingState.DerivedFrom);
        Assert.True(seedingState.Diagnostics.MetadataPresent);
        Assert.True(seedingState.Diagnostics.UsedFastPath);
        Assert.False(seedingState.Diagnostics.HasAccounts);
        Assert.False(seedingState.Diagnostics.HasTransactions);
        Assert.False(seedingState.Diagnostics.HasPolicyConfiguration);
    }

    [Fact]
    public async Task Finance_read_endpoint_returns_structured_not_initialized_response_and_requests_fallback_seed()
    {
        _factory.FinanceSeedTelemetry.Reset();
        var seed = await SeedFinanceSeedingStateCompanyAsync((_, company, _) =>
        {
            ApplySeedingMetadata(company, FinanceSeedingState.NotSeeded);
        });
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/cash-balance");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<FinanceInitializationProblemResponse>();
        Assert.NotNull(problem);
        Assert.Equal(FinanceInitializationProblemCodeValues.NotInitialized, problem!.Code);
        Assert.Equal(FinanceInitializationDomainValues.Finance, problem.Domain);
        Assert.True(problem.CanTriggerSeed);
        Assert.True(problem.CanGenerate);
        Assert.Equal(FinanceRecommendedActionContractValues.Generate, problem.RecommendedAction);
        Assert.Equal([FinanceManualSeedModes.Replace], problem.SupportedModes);
        Assert.True(problem.FallbackTriggered);
        Assert.True(problem.SeedRequested);
        Assert.True(problem.SeedJobActive);
        Assert.Equal(FinanceEntryProgressStateContractValues.SeedingRequested, problem.ProgressState);

        var snapshot = await _factory.ExecuteDbContextAsync(async dbContext =>
            await Task.FromResult(new
            {
                JobCount = dbContext.BackgroundExecutions.Count(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.ExecutionType == BackgroundExecutionType.FinanceSeed),
                RequestedAuditCount = dbContext.AuditEvents.Count(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.Action == "finance.seed.job.requested" &&
                    x.ActorType == "system"),
                AuditCount = dbContext.AuditEvents.Count(x =>
                    x.CompanyId == seed.CompanyId &&
                    x.Action == "finance.request.not_initialized")
            }));

        Assert.Equal(1, snapshot.JobCount);
        Assert.Equal(1, snapshot.RequestedAuditCount);
        Assert.Equal(1, snapshot.AuditCount);
        var telemetryEvent = Assert.Single(_factory.FinanceSeedTelemetry.Events
            .Where(x => x.EventName == FinanceSeedTelemetryEventNames.Requested && x.Context.CompanyId == seed.CompanyId));
        Assert.Equal(FinanceEntrySources.FallbackRead, telemetryEvent.Context.TriggerSource);
        Assert.Equal("system", telemetryEvent.Context.ActorType);
    }

    [Fact]
    public async Task Finance_read_endpoint_returns_structured_not_initialized_response_without_triggering_seed_when_fallback_is_disabled()
    {
        using var factory = new TestWebApplicationFactory(new Dictionary<string, string?>
        {
            [$"{FinanceInitializationOptions.SectionName}:MissingDatasetBehavior"] = FinanceMissingDatasetBehaviorValues.ReturnNotInitialized
        });
        factory.FinanceSeedTelemetry.Reset();

        var seed = await SeedFinanceSeedingStateCompanyAsync(factory, (_, company, _) =>
        {
            ApplySeedingMetadata(company, FinanceSeedingState.NotSeeded);
        });
        using var client = CreateAuthenticatedClient(factory, seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/cash-balance");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<FinanceInitializationProblemResponse>();
        Assert.NotNull(problem);
        Assert.Equal(FinanceInitializationProblemCodeValues.NotInitialized, problem!.Code);
        Assert.False(problem.FallbackTriggered);
        Assert.False(problem.SeedRequested);
        Assert.False(problem.SeedJobActive);
        Assert.Equal(FinanceEntryProgressStateContractValues.NotSeeded, problem.ProgressState);

        var snapshot = await factory.ExecuteDbContextAsync(async dbContext =>
            await Task.FromResult(dbContext.BackgroundExecutions.Count(x =>
                x.CompanyId == seed.CompanyId &&
                x.ExecutionType == BackgroundExecutionType.FinanceSeed)));

        Assert.Equal(1, snapshot);
        var telemetryEvent = Assert.Single(_factory.FinanceSeedTelemetry.Events
            .Where(x => x.EventName == FinanceSeedTelemetryEventNames.Requested && x.Context.CompanyId == seed.CompanyId));
        Assert.Equal(FinanceEntrySources.FallbackRead, telemetryEvent.Context.TriggerSource);
        Assert.Empty(factory.FinanceSeedTelemetry.Events
            .Where(x => x.EventName == FinanceSeedTelemetryEventNames.Requested && x.Context.CompanyId == seed.CompanyId));
    }

    [Fact]
    public async Task Finance_review_listing_returns_structured_not_initialized_response_instead_of_server_error()
    {
        var seed = await SeedFinanceSeedingStateCompanyAsync((_, company, _) =>
        {
            ApplySeedingMetadata(company, FinanceSeedingState.NotSeeded);
        });
        using var client = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var response = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/reviews?limit=5");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<FinanceInitializationProblemResponse>();
        Assert.NotNull(problem);
        Assert.Equal(FinanceInitializationProblemCodeValues.NotInitialized, problem!.Code);
    }
    [Fact]
    public async Task Finance_detail_endpoints_surface_route_drilldown_data_and_guard_linked_documents()
    {
        var seed = await SeedFinanceCompanyAsync();
        using var ownerClient = CreateAuthenticatedClient(seed.Subject, seed.Email, seed.DisplayName);

        var reviewResponse = await ownerClient.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.ReviewInvoiceId}/review-workflow",
            new { });
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);

        var transactionDetailResponse = await ownerClient.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions/{seed.AccessibleTransactionId}");
        Assert.Equal(HttpStatusCode.OK, transactionDetailResponse.StatusCode);
        var transactionDetail = await transactionDetailResponse.Content.ReadFromJsonAsync<TransactionDetailResponse>();
        Assert.NotNull(transactionDetail);
        Assert.Equal(seed.AccessibleTransactionId, transactionDetail!.Id);
        Assert.True(transactionDetail.IsFlagged);
        Assert.NotEmpty(transactionDetail.Flags);
        Assert.Equal("needs_review", transactionDetail.AnomalyState);
        Assert.NotNull(transactionDetail.LinkedDocument);
        Assert.Equal("available", transactionDetail.LinkedDocument.Availability);
        Assert.True(transactionDetail.LinkedDocument.CanNavigate);
        Assert.NotNull(transactionDetail.LinkedDocument.Document);

        var missingTransactionDetailResponse = await ownerClient.GetAsync($"/internal/companies/{seed.CompanyId}/finance/transactions/{seed.MissingTransactionId}");
        Assert.Equal(HttpStatusCode.OK, missingTransactionDetailResponse.StatusCode);
        var missingTransactionDetail = await missingTransactionDetailResponse.Content.ReadFromJsonAsync<TransactionDetailResponse>();
        Assert.NotNull(missingTransactionDetail);
        Assert.Equal("missing", missingTransactionDetail!.LinkedDocument.Availability);
        Assert.False(missingTransactionDetail.LinkedDocument.CanNavigate);
        Assert.Null(missingTransactionDetail.LinkedDocument.Document);

        var invoiceDetailResponse = await ownerClient.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.ReviewInvoiceId}");
        Assert.Equal(HttpStatusCode.OK, invoiceDetailResponse.StatusCode);
        var invoiceDetail = await invoiceDetailResponse.Content.ReadFromJsonAsync<InvoiceDetailResponse>();
        Assert.NotNull(invoiceDetail);
        Assert.Equal(seed.ReviewInvoiceId, invoiceDetail!.Id);
        Assert.NotNull(invoiceDetail.WorkflowContext);
        Assert.Equal("Invoice review workflow", invoiceDetail.WorkflowContext!.WorkflowName);
        Assert.NotEqual(Guid.Empty, invoiceDetail.WorkflowContext!.TaskId);
        Assert.False(string.IsNullOrWhiteSpace(invoiceDetail.WorkflowContext.ReviewTaskStatus));
        if (invoiceDetail.WorkflowContext.ApprovalRequestId.HasValue)
        {
            Assert.False(string.IsNullOrWhiteSpace(invoiceDetail.WorkflowContext.ApprovalStatus));
            Assert.True(invoiceDetail.WorkflowContext.CanNavigateToApproval);
        }

        var missingInvoiceDetailResponse = await ownerClient.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.MissingInvoiceId}");
        Assert.Equal(HttpStatusCode.OK, missingInvoiceDetailResponse.StatusCode);
        var missingInvoiceDetail = await missingInvoiceDetailResponse.Content.ReadFromJsonAsync<InvoiceDetailResponse>();
        Assert.NotNull(missingInvoiceDetail);
        Assert.Equal("missing", missingInvoiceDetail!.LinkedDocument.Availability);
        Assert.False(missingInvoiceDetail.LinkedDocument.CanNavigate);

        using var approverClient = CreateAuthenticatedClient(seed.ApproverSubject, seed.ApproverEmail, seed.ApproverDisplayName);
        var restrictedInvoiceDetailResponse = await approverClient.GetAsync($"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.RestrictedInvoiceId}");
        Assert.Equal(HttpStatusCode.OK, restrictedInvoiceDetailResponse.StatusCode);
        var restrictedInvoiceDetail = await restrictedInvoiceDetailResponse.Content.ReadFromJsonAsync<InvoiceDetailResponse>();
        Assert.NotNull(restrictedInvoiceDetail);
        Assert.Equal("inaccessible", restrictedInvoiceDetail!.LinkedDocument.Availability);
        Assert.False(restrictedInvoiceDetail.LinkedDocument.CanNavigate);
        Assert.Null(restrictedInvoiceDetail.LinkedDocument.Document);
    }

    [Fact]
    public async Task Invoice_review_detail_actions_follow_workflow_actionable_state()
    {
        var seed = await FinanceDetailTestScenarioFactory.CreateAsync(_factory);
        using var client = FinanceDetailTestScenarioFactory.CreateAuthenticatedClient(
            _factory,
            seed.OwnerSubject,
            seed.OwnerEmail,
            seed.OwnerDisplayName);

        var reviewResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/invoices/{seed.MissingInvoiceId}/review-workflow",
            new { });
        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);

        var actionableDetailResponse = await client.GetAsync($"/internal/companies/{seed.CompanyId}/finance/reviews/{seed.MissingInvoiceId}");
        Assert.Equal(HttpStatusCode.OK, actionableDetailResponse.StatusCode);
        var actionableDetail = await actionableDetailResponse.Content.ReadFromJsonAsync<FinanceInvoiceReviewDetailResponse>();
        Assert.NotNull(actionableDetail);
        Assert.Equal("awaiting_approval", actionableDetail!.RecommendationStatus);
        Assert.True(actionableDetail.Actions.IsActionable);
        Assert.NotNull(actionableDetail.RecommendationDetails);
        Assert.Equal("overdue_invoice", actionableDetail.RecommendationDetails!.Classification);
        Assert.Equal(actionableDetail.RiskLevel, actionableDetail.RecommendationDetails.Risk);
        Assert.Equal(actionableDetail.RecommendationSummary, actionableDetail.RecommendationDetails.RationaleSummary);
        Assert.Equal(actionableDetail.RecommendedAction, actionableDetail.RecommendationDetails.RecommendedAction);
        Assert.Equal(actionableDetail.RecommendationStatus, actionableDetail.RecommendationDetails.CurrentWorkflowStatus);
        Assert.NotNull(actionableDetail.WorkflowHistory);
        Assert.NotEmpty(actionableDetail.WorkflowHistory);
        Assert.Equal(
            actionableDetail.WorkflowHistory.Select(x => x.EventId).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            actionableDetail.WorkflowHistory.Count);
        Assert.True(actionableDetail.WorkflowHistory.SequenceEqual(
            actionableDetail.WorkflowHistory.OrderByDescending(x => x.OccurredAtUtc)));
        Assert.True(actionableDetail.Actions.CanApprove);
        Assert.True(actionableDetail.Actions.CanReject);
        Assert.True(actionableDetail.Actions.CanSendForFollowUp);

        var followUpResponse = await client.PostAsJsonAsync(
            $"/internal/companies/{seed.CompanyId}/finance/reviews/{seed.MissingInvoiceId}/follow-up",
            new { });
        Assert.Equal(HttpStatusCode.OK, followUpResponse.StatusCode);
        var followUpDetail = await followUpResponse.Content.ReadFromJsonAsync<FinanceInvoiceReviewDetailResponse>();
        Assert.NotNull(followUpDetail);
        Assert.Equal("open", followUpDetail!.Status);
        Assert.Equal("completed", followUpDetail.RecommendationStatus);
        Assert.NotNull(followUpDetail.RecommendationDetails);
        Assert.NotNull(followUpDetail.WorkflowHistory);
        Assert.False(followUpDetail.Actions.IsActionable);
        Assert.False(followUpDetail.Actions.CanApprove);
        Assert.False(followUpDetail.Actions.CanReject);
        Assert.False(followUpDetail.Actions.CanSendForFollowUp);
    }

    private async Task<FinanceEndpointSeed> SeedFinanceCompanyAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var subject = $"finance-reader-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Reader";
        var employeeUserId = Guid.NewGuid();
        var employeeSubject = $"finance-employee-{Guid.NewGuid():N}";
        var employeeEmail = $"{employeeSubject}@example.com";
        var approverUserId = Guid.NewGuid();
        var approverSubject = $"finance-approver-{Guid.NewGuid():N}";
        var approverEmail = $"{approverSubject}@example.com";
        const string approverDisplayName = "Finance Approver";
        var anomalyId = Guid.Empty;
        var affectedRecordId = Guid.Empty;
        var accessibleTransactionId = Guid.Empty;
        var restrictedInvoiceId = Guid.Empty;
        var missingTransactionId = Guid.Empty;
        var missingInvoiceId = Guid.Empty;
        var reviewInvoiceId = Guid.Empty;
        const string employeeDisplayName = "Finance Employee";
        FinanceSeedResult? financeSeed = null;

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Users.Add(new User(employeeUserId, employeeEmail, employeeDisplayName, "dev-header", employeeSubject));
            dbContext.Users.Add(new User(approverUserId, approverEmail, approverDisplayName, "dev-header", approverSubject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Finance Read Company"),
                new Company(otherCompanyId, "Other Finance Read Company"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, employeeUserId, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, approverUserId, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));

            financeSeed = FinanceSeedData.AddMockFinanceData(dbContext, companyId);
            accessibleTransactionId = financeSeed.TransactionIds[0];
            restrictedInvoiceId = financeSeed.InvoiceIds[4];
            reviewInvoiceId = financeSeed.InvoiceIds[0];
            var templateTransaction = dbContext.FinanceTransactions.Local.First(x => x.CompanyId == companyId);
            var missingTransaction = new FinanceTransaction(
                Guid.NewGuid(),
                companyId,
                templateTransaction.AccountId,
                templateTransaction.CounterpartyId,
                null,
                null,
                templateTransaction.TransactionUtc.AddMinutes(15),
                "software",
                -89.42m,
                templateTransaction.Currency,
                "Missing linked document transaction",
                $"missing-tx-{Guid.NewGuid():N}".Substring(0, 22),
                Guid.NewGuid());
            dbContext.FinanceTransactions.Add(missingTransaction);
            missingTransactionId = missingTransaction.Id;

            var templateInvoice = dbContext.FinanceInvoices.Local.First(x => x.CompanyId == companyId);
            var missingInvoice = new FinanceInvoice(
                Guid.NewGuid(),
                companyId,
                templateInvoice.CounterpartyId,
                $"{templateInvoice.InvoiceNumber}-MISSING",
                templateInvoice.IssuedUtc.AddDays(3),
                templateInvoice.DueUtc.AddDays(3),
                templateInvoice.Amount + 42m,
                templateInvoice.Currency,
                "pending_approval",
                Guid.NewGuid());
            dbContext.FinanceInvoices.Add(missingInvoice);
            missingInvoiceId = missingInvoice.Id;

            var seedAnomaly = new FinanceSeedAnomaly(
                Guid.NewGuid(),
                companyId,
                "missing_receipt",
                "integration",
                [financeSeed.TransactionIds[0]],
                """{"expectedDetector":"receipt_completeness","expectedSignal":"missing_supporting_document"}""");
            anomalyId = seedAnomaly.Id;
            affectedRecordId = financeSeed.TransactionIds[0];
            dbContext.FinanceSeedAnomalies.Add(seedAnomaly);
            FinanceSeedData.AddMockFinanceData(dbContext, otherCompanyId);
            return Task.CompletedTask;
        });

        return new FinanceEndpointSeed(
            companyId, otherCompanyId, subject, email, displayName,
            employeeSubject, employeeEmail, employeeDisplayName,
            approverSubject, approverEmail, approverDisplayName,
            financeSeed!.DocumentIds, anomalyId, affectedRecordId,
            financeSeed!.DocumentIds, anomalyId, affectedRecordId,
            accessibleTransactionId, restrictedInvoiceId, reviewInvoiceId, missingTransactionId, missingInvoiceId);
    }

    private async Task<FinanceSeedingStateEndpointSeed> SeedFinanceSeedingStateCompanyAsync(
        Action<VirtualCompanyDbContext, Company, Guid> configureCompany)
    {
        var userId = Guid.NewGuid();
        return await SeedFinanceSeedingStateCompanyAsync(_factory, configureCompany);
    }

    private static async Task<FinanceSeedingStateEndpointSeed> SeedFinanceSeedingStateCompanyAsync(
        TestWebApplicationFactory factory,
        Action<VirtualCompanyDbContext, Company, Guid> configureCompany)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var subject = $"finance-seeding-state-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com}";
        const string displayName = "Finance Seeding State Reader";

        await factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));

            var company = new Company(companyId, "Finance Seeding State Company");
            configureCompany(dbContext, company, companyId);

            dbContext.Companies.Add(company);
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.Owner,
                CompanyMembershipStatus.Active));

            return Task.CompletedTask;
        });

        return new FinanceSeedingStateEndpointSeed(companyId, subject, email, displayName);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName) =>
        CreateAuthenticatedClient(_factory, subject, email, displayName);

    private static HttpClient CreateAuthenticatedClient(TestWebApplicationFactory factory, string subject, string email, string displayName)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        return client;
    }
    private sealed record FinanceSeedingStateEndpointSeed(
        Guid CompanyId,
        string Subject,
        string Email,
        string DisplayName);

    private sealed record FinanceEndpointSeed(
        Guid CompanyId,
        Guid OtherCompanyId,
        string Subject,
        string Email,
        string DisplayName,
        string EmployeeSubject,
        string EmployeeEmail,
        string EmployeeDisplayName,
        string ApproverSubject,
        string ApproverEmail,
        string ApproverDisplayName,
        IReadOnlyList<Guid> DocumentIds,
        Guid AnomalyId,
        Guid AffectedRecordId,
        Guid AccessibleTransactionId,
        Guid RestrictedInvoiceId,
        Guid ReviewInvoiceId,
        Guid MissingTransactionId,
        Guid MissingInvoiceId);

    private sealed class CashBalanceResponse
    {
        public Guid CompanyId { get; set; }
        public DateTime AsOfUtc { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public List<AccountBalanceResponse> Accounts { get; set; } = [];
    }

    private sealed class CashPositionResponse
    {
        public Guid CompanyId { get; set; }
        public DateTime AsOfUtc { get; set; }
        public decimal AvailableBalance { get; set; }
        public string Currency { get; set; } = string.Empty;
        public decimal AverageMonthlyBurn { get; set; }
        public int? EstimatedRunwayDays { get; set; }
        public CashPositionThresholdsResponse Thresholds { get; set; } = new();
        public CashPositionAlertStateResponse AlertState { get; set; } = new();
    }

    private sealed class CashPositionThresholdsResponse
    {
        public int WarningRunwayDays { get; set; }
        public int CriticalRunwayDays { get; set; }
        public decimal? WarningCashAmount { get; set; }
        public decimal? CriticalCashAmount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class AccountBalanceResponse
    {
        public Guid AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string AccountType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public DateTime AsOfUtc { get; set; }
    }

    private sealed class CashPositionAlertStateResponse
    {
        public bool IsLowCash { get; set; }
        public string RiskLevel { get; set; } = string.Empty;
        public bool AlertCreated { get; set; }
        public bool AlertDeduplicated { get; set; }
        public Guid? AlertId { get; set; }
        public string? AlertStatus { get; set; }
        public string Rationale { get; set; } = string.Empty;
    }

    private sealed class MonthlyProfitAndLossResponse
    {
        public Guid CompanyId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public decimal Revenue { get; set; }
        public decimal Expenses { get; set; }
        public decimal NetResult { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class ExpenseBreakdownResponse
    {
        public Guid CompanyId { get; set; }
        public DateTime StartUtc { get; set; }
        public DateTime EndUtc { get; set; }
        public decimal TotalExpenses { get; set; }
        public string Currency { get; set; } = string.Empty;
        public List<ExpenseCategoryResponse> Categories { get; set; } = [];
    }

    private sealed class ExpenseCategoryResponse
    {
        public string Category { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
    }

    private sealed class TransactionResponse
    {
        public Guid Id { get; set; }
        public Guid AccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public Guid? CounterpartyId { get; set; }
        public string? CounterpartyName { get; set; }
        public Guid? InvoiceId { get; set; }
        public Guid? BillId { get; set; }
        public DateTime TransactionUtc { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ExternalReference { get; set; } = string.Empty;
        public LinkedDocumentResponse? LinkedDocument { get; set; }
        public bool IsFlagged { get; set; }
        public string AnomalyState { get; set; } = string.Empty;
    }

    private sealed class InvoiceResponse
    {
        public Guid Id { get; set; }
        public Guid CounterpartyId { get; set; }
        public string CounterpartyName { get; set; } = string.Empty;
        public string InvoiceNumber { get; set; } = string.Empty;
        public DateTime IssuedUtc { get; set; }
        public DateTime DueUtc { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public LinkedDocumentResponse? LinkedDocument { get; set; }
    }

    private sealed class CounterpartyResponse
    {
        public Guid Id { get; set; }
        public string CounterpartyType { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? PaymentTerms { get; set; }
        public string? TaxId { get; set; }
        public decimal? CreditLimit { get; set; }
        public string? PreferredPaymentMethod { get; set; }
        public string? DefaultAccountMapping { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class BillResponse
    {
        public Guid Id { get; set; }
        public Guid CounterpartyId { get; set; }
        public string CounterpartyName { get; set; } = string.Empty;
        public string BillNumber { get; set; } = string.Empty;
        public DateTime ReceivedUtc { get; set; }
        public DateTime DueUtc { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public LinkedDocumentResponse? LinkedDocument { get; set; }
    }

    private sealed class LinkedDocumentResponse
    {
        public Guid Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
    }

    private sealed class LinkedDocumentAccessResponse
    {
        public string Availability { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool CanNavigate { get; set; }
        public LinkedDocumentResponse? Document { get; set; }
    }

    private sealed class TransactionDetailResponse
    {
        public Guid Id { get; set; }
        public bool IsFlagged { get; set; }
        public string AnomalyState { get; set; } = string.Empty;
        public List<string> Flags { get; set; } = [];
        public LinkedDocumentAccessResponse LinkedDocument { get; set; } = new();
    }

    private sealed class InvoiceDetailResponse
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public InvoiceWorkflowContextResponse? WorkflowContext { get; set; }
        public List<NormalizedInsightResponse> AgentInsights { get; set; } = [];
        public LinkedDocumentAccessResponse LinkedDocument { get; set; } = new();
    }

    private sealed class InvoiceWorkflowContextResponse
    {
        public Guid? WorkflowInstanceId { get; set; }
        public Guid TaskId { get; set; }
        public string WorkflowName { get; set; } = string.Empty;
        public string ReviewTaskStatus { get; set; } = string.Empty;
        public Guid? ApprovalRequestId { get; set; }
        public string Classification { get; set; } = string.Empty;
        public string RiskLevel { get; set; } = string.Empty;
        public string RecommendedAction { get; set; } = string.Empty;
        public string Rationale { get; set; } = string.Empty;
        public string? ApprovalStatus { get; set; }
        public string? ApprovalAssigneeSummary { get; set; }
        public decimal Confidence { get; set; }
        public bool RequiresHumanApproval { get; set; }
        public bool CanNavigateToWorkflow { get; set; }
        public bool CanNavigateToApproval { get; set; }
    }

    private sealed class NormalizedInsightResponse
    {
        public Guid Id { get; set; }
        public string Severity { get; set; } = string.Empty;
        public string CheckCode { get; set; } = string.Empty;
        public string ConditionKey { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Recommendation { get; set; } = string.Empty;
    }

    private sealed class FinanceInitializationProblemResponse
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string Domain { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty;
        public bool CanNavigateToWorkflow { get; set; }
        public bool CanNavigateToApproval { get; set; }
        public bool CanTriggerSeed { get; set; }
        public bool CanGenerate { get; set; }
        public string RecommendedAction { get; set; } = string.Empty;
        public List<string> SupportedModes { get; set; } = [];
        public bool FallbackTriggered { get; set; }
        public bool SeedRequested { get; set; }
        public bool SeedJobActive { get; set; }
        public string ProgressState { get; set; } = string.Empty;
        public string SeedingState { get; set; } = string.Empty;
        public string InitializationStatus { get; set; } = string.Empty;
        public string? JobStatus { get; set; }
        public string? CorrelationId { get; set; }
        public string? StatusEndpoint { get; set; }
        public string? SeedEndpoint { get; set; }
    }

    private sealed class SeedAnomalyResponse
    {
        public Guid Id { get; set; }
        public string AnomalyType { get; set; } = string.Empty;
        public string ScenarioProfile { get; set; } = string.Empty;
        public List<Guid> AffectedRecordIds { get; set; } = [];
        public string ExpectedDetectionMetadataJson { get; set; } = string.Empty;
    }

    private sealed class CompanyDocumentResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
    }
}
