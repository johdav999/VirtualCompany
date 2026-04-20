using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceToolExecutionFlowIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceToolExecutionFlowIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [MemberData(nameof(SuccessfulFinanceToolRequests))]
    public async Task Finance_read_tools_execute_through_policy_executor_provider_and_persist_execution_record(
        string toolName,
        object requestPayload,
        string expectedDataProperty,
        string expectedProviderCall)
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create(toolName))),
                ("actions", new JsonArray(JsonValue.Create("read")))),
            scopes: Payload(("read", new JsonArray(JsonValue.Create("finance")))));

        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName,
            actionType = "read",
            scope = "finance",
            requestPayload
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("executed", payload!.Status);
        Assert.Equal("allow", payload.PolicyDecision.Outcome);
        Assert.NotNull(payload.ExecutionResult);
        Assert.Equal("executed", payload.ExecutionResult!["status"].GetString());
        Assert.Equal(toolName, payload.ExecutionResult["toolName"].GetString());
        Assert.Equal("read", payload.ExecutionResult["actionType"].GetString());
        Assert.True(payload.ExecutionResult["success"].GetBoolean());
        Assert.True(payload.ExecutionResult["data"].TryGetProperty(expectedDataProperty, out _));

        var metadata = payload.ExecutionResult["metadata"];
        Assert.Equal("finance_tool_provider", metadata.GetProperty("contractName").GetString());
        Assert.Equal("1.0.0", metadata.GetProperty("toolVersion").GetString());
        Assert.Equal(InternalToolExecutionResponse.SchemaVersion, metadata.GetProperty("contractSchemaVersion").GetString());

        var trackingFinance = financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>();
        Assert.Equal(1, trackingFinance.TotalCallCount);
        Assert.Equal(expectedProviderCall, Assert.Single(trackingFinance.CallNames));

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal(seed.CompanyId, attempt.CompanyId);
        Assert.Equal(seed.AgentId, attempt.AgentId);
        Assert.Equal(toolName, attempt.ToolName);
        Assert.Equal("1.0.0", attempt.ToolVersion);
        Assert.Equal(ToolActionType.Read, attempt.ActionType);
        Assert.Equal("finance", attempt.Scope);
        Assert.Equal(ToolExecutionStatus.Executed, attempt.Status);
        Assert.Equal("allow", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal("executed", attempt.ResultPayload["status"]!.GetValue<string>());
        Assert.Equal(toolName, attempt.ResultPayload["toolName"]!.GetValue<string>());
        Assert.Equal("read", attempt.ResultPayload["actionType"]!.GetValue<string>());
        Assert.True(attempt.ResultPayload["success"]!.GetValue<bool>());
        Assert.NotNull(attempt.ResultPayload["data"]![expectedDataProperty]);
        Assert.Equal("finance_tool_provider", attempt.ResultPayload["metadata"]!["contractName"]!.GetValue<string>());
        Assert.Equal("1.0.0", attempt.ResultPayload["metadata"]!["toolVersion"]!.GetValue<string>());
        Assert.Equal(InternalToolExecutionResponse.SchemaVersion, attempt.ResultPayload["metadata"]!["contractSchemaVersion"]!.GetValue<string>());
        Assert.NotEqual(default, attempt.StartedUtc);
        Assert.NotEqual(default, attempt.CreatedUtc);
        Assert.NotEqual(default, attempt.UpdatedUtc);
        Assert.NotNull(attempt.CompletedUtc);
        Assert.NotNull(attempt.ExecutedUtc);
        Assert.True(attempt.CompletedUtc >= attempt.StartedUtc);
        Assert.Null(attempt.DenialReason);
    }

    [Fact]
    public async Task Finance_tool_policy_denial_is_persisted_and_blocks_provider_dispatch()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("list_transactions"))),
                ("actions", new JsonArray(JsonValue.Create("read")))),
            scopes: Payload(("read", new JsonArray(JsonValue.Create("finance")))));

        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "get_cash_balance",
            actionType = "read",
            scope = "finance",
            requestPayload = new { asOfUtc = "2026-04-16T00:00:00Z" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("denied", payload!.Status);
        Assert.Equal("deny", payload.PolicyDecision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.ToolNotPermitted, payload.PolicyDecision.ReasonCodes);
        Assert.NotNull(payload.Denial);
        Assert.Equal("policy_denied", payload.Denial!.Code);
        Assert.Equal(payload.Message, payload.Denial.UserFacingMessage);

        var trackingFinance = financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>();
        Assert.Equal(0, trackingFinance.TotalCallCount);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        Assert.Equal("get_cash_balance", attempt.ToolName);
        Assert.Equal("1.0.0", attempt.ToolVersion);
        Assert.Equal(ToolActionType.Read, attempt.ActionType);
        Assert.Equal("finance", attempt.Scope);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal("deny", attempt.PolicyDecision["outcome"]!.GetValue<string>());
        Assert.Equal(PolicyDecisionReasonCodes.ToolNotPermitted, attempt.PolicyDecision["reasons"]![0]!["code"]!.GetValue<string>());
        Assert.Equal(payload.Message, attempt.DenialReason);
        Assert.Equal("denied", attempt.ResultPayload["status"]!.GetValue<string>());
        Assert.Equal("policy_denied", attempt.ResultPayload["errorCode"]!.GetValue<string>());
        Assert.Equal(payload.Message, attempt.ResultPayload["errorMessage"]!.GetValue<string>());
        Assert.NotEqual(default, attempt.StartedUtc);
        Assert.NotEqual(default, attempt.CreatedUtc);
        Assert.NotEqual(default, attempt.UpdatedUtc);
        Assert.NotNull(attempt.CompletedUtc);
        Assert.Null(attempt.ExecutedUtc);
    }

    [Fact]
    public async Task Finance_execute_action_with_disallowed_action_type_is_denied_before_provider_dispatch()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("categorize_transaction"))),
                ("actions", new JsonArray(JsonValue.Create("read")))),
            scopes: Payload(
                ("read", new JsonArray(JsonValue.Create("finance"))),
                ("execute", new JsonArray(JsonValue.Create("finance")))));

        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "categorize_transaction",
            actionType = "execute",
            scope = "finance",
            requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("denied", payload!.Status);
        Assert.Equal("deny", payload.PolicyDecision.Outcome);
        Assert.Contains(PolicyDecisionReasonCodes.ToolActionNotPermitted, payload.PolicyDecision.ReasonCodes);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await dbContext.FinanceTransactions.AsNoTracking().SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);

        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.Denied, attempt.Status);
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Finance_execute_action_above_threshold_creates_approval_request_without_state_change()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("categorize_transaction"))),
                ("actions", new JsonArray(JsonValue.Create("execute")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("finance")))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));

        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var response = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "categorize_transaction",
            actionType = "execute",
            scope = "finance",
            thresholdCategory = "approval",
            thresholdKey = "financeMutationUsd",
            thresholdValue = 250,
            sensitiveAction = true,
            taskId = financeSeed.TaskId,
            requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(payload);
        Assert.Equal("awaiting_approval", payload!.Status);
        Assert.Equal("require_approval", payload.PolicyDecision.Outcome);
        Assert.NotNull(payload.ApprovalRequestId);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await dbContext.FinanceTransactions.AsNoTracking().SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == payload.ExecutionId);
        var approval = await dbContext.ApprovalRequests.AsNoTracking().SingleAsync(x => x.Id == payload.ApprovalRequestId);

        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.AwaitingApproval, attempt.Status);
        Assert.Equal(payload.ApprovalRequestId, attempt.ApprovalRequestId);
        Assert.Equal(attempt.Id, approval.ToolExecutionAttemptId);
        Assert.Equal(attempt.Id, approval.TargetEntityId);
        Assert.Equal("action", approval.TargetEntityType);
        Assert.Equal(financeSeed.TaskId, approval.ThresholdContext["taskId"]!.GetValue<Guid>());
        Assert.Equal(financeSeed.TaskId, approval.ThresholdContext["originatingTaskId"]!.GetValue<Guid>());
        Assert.Equal(attempt.Id, approval.ThresholdContext["toolExecutionId"]!.GetValue<Guid>());
        Assert.Equal(attempt.Id, approval.ThresholdContext["toolExecutionAttemptId"]!.GetValue<Guid>());
        Assert.Equal(financeSeed.TaskId, approval.DecisionChain["originatingTaskId"]!.GetValue<Guid>());
        Assert.Equal(attempt.Id, approval.DecisionChain["toolExecutionAttemptId"]!.GetValue<Guid>());
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Approved_finance_execute_approval_runs_state_change_and_marks_execution_executed()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("categorize_transaction"))),
                ("actions", new JsonArray(JsonValue.Create("execute")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("finance")))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));

        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executeResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "categorize_transaction",
            actionType = "execute",
            scope = "finance",
            thresholdCategory = "approval",
            thresholdKey = "financeMutationUsd",
            thresholdValue = 250,
            sensitiveAction = true,
            taskId = financeSeed.TaskId,
            requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
        });
        var executePayload = await executeResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(executePayload?.ApprovalRequestId);

        var approval = await GetApprovalAsync(financeFactory.Services, seed.CompanyId, executePayload!.ApprovalRequestId!.Value);
        var decisionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/approvals/{approval.Id}/decisions", new
        {
            decision = "approve",
            stepId = approval.CurrentStep!.Id,
            comment = "Approved for categorization."
        });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await dbContext.FinanceTransactions.AsNoTracking().SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == executePayload.ExecutionId);

        Assert.Equal("software", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.Executed, attempt.Status);
        Assert.NotNull(attempt.ExecutedUtc);
        Assert.Equal("executed", attempt.ResultPayload["status"]!.GetValue<string>());
        Assert.Equal(1, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    [Fact]
    public async Task Rejected_finance_execute_approval_leaves_state_unchanged_and_marks_execution_rejected()
    {
        using var financeFactory = CreateFinanceContractFactory();
        var seed = await SeedAgentAsync(
            financeFactory.Services,
            tools: Payload(
                ("allowed", new JsonArray(JsonValue.Create("categorize_transaction"))),
                ("actions", new JsonArray(JsonValue.Create("execute")))),
            scopes: Payload(("execute", new JsonArray(JsonValue.Create("finance")))),
            thresholds: Payload(("approval", new JsonObject { ["financeMutationUsd"] = 100 })));

        var financeSeed = await SeedFinanceRecordAsync(financeFactory.Services, seed.CompanyId);
        using var client = CreateAuthenticatedClient(financeFactory, seed);
        var executeResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/agents/{seed.AgentId}/executions", new
        {
            toolName = "categorize_transaction",
            actionType = "execute",
            scope = "finance",
            thresholdCategory = "approval",
            thresholdKey = "financeMutationUsd",
            thresholdValue = 250,
            sensitiveAction = true,
            taskId = financeSeed.TaskId,
            requestPayload = new { transactionId = financeSeed.TransactionId, category = "software" }
        });
        var executePayload = await executeResponse.Content.ReadFromJsonAsync<AgentToolExecutionResponse>();
        Assert.NotNull(executePayload?.ApprovalRequestId);

        var approval = await GetApprovalAsync(financeFactory.Services, seed.CompanyId, executePayload!.ApprovalRequestId!.Value);
        var decisionResponse = await client.PostAsJsonAsync($"/api/companies/{seed.CompanyId}/approvals/{approval.Id}/decisions", new
        {
            decision = "reject",
            stepId = approval.CurrentStep!.Id,
            comment = "Needs more evidence."
        });

        Assert.Equal(HttpStatusCode.OK, decisionResponse.StatusCode);

        using var scope = financeFactory.Services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(seed.CompanyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var transaction = await dbContext.FinanceTransactions.AsNoTracking().SingleAsync(x => x.Id == financeSeed.TransactionId);
        var attempt = await dbContext.ToolExecutionAttempts.AsNoTracking().SingleAsync(x => x.Id == executePayload.ExecutionId);

        Assert.Equal("uncategorized", transaction.TransactionType);
        Assert.Equal(ToolExecutionStatus.Rejected, attempt.Status);
        Assert.Null(attempt.ExecutedUtc);
        Assert.Equal(executePayload.ApprovalRequestId, attempt.ApprovalRequestId);
        Assert.Equal(PolicyDecisionReasonCodes.ApprovalRejected, attempt.DenialReason);
        Assert.Equal("rejected", attempt.ResultPayload["status"]!.GetValue<string>());
        Assert.Equal(0, financeFactory.Services.GetRequiredService<TrackingFinanceToolProvider>().TotalCallCount);
    }

    public static IEnumerable<object[]> SuccessfulFinanceToolRequests()
    {
        yield return
        [
            "get_cash_balance",
            new { asOfUtc = "2026-04-16T00:00:00Z" },
            "cashBalance",
            nameof(TrackingFinanceToolProvider.GetCashBalanceAsync)
        ];

        yield return
        [
            "list_transactions",
            new { startUtc = "2026-04-01T00:00:00Z", endUtc = "2026-04-16T00:00:00Z", limit = 25 },
            "transactions",
            nameof(TrackingFinanceToolProvider.GetTransactionsAsync)
        ];

        yield return
        [
            "list_uncategorized_transactions",
            new { limit = 10 },
            "transactions",
            nameof(TrackingFinanceToolProvider.GetTransactionsAsync)
        ];

        yield return
        [
            "list_invoices_awaiting_approval",
            new { limit = 10 },
            "invoices",
            nameof(TrackingFinanceToolProvider.GetInvoicesAsync)
        ];

        yield return
        [
            "get_profit_and_loss_summary",
            new { year = 2026, month = 4 },
            "profitAndLossSummary",
            nameof(TrackingFinanceToolProvider.GetMonthlyProfitAndLossAsync)
        ];
    }

    private WebApplicationFactory<Program> CreateFinanceContractFactory() =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFinanceToolProvider>();
                services.RemoveAll<IInternalCompanyToolContract>();
                services.AddSingleton<TrackingFinanceToolProvider>();
                services.AddScoped<IFinanceToolProvider>(provider => provider.GetRequiredService<TrackingFinanceToolProvider>());
                services.AddScoped<IInternalCompanyToolContract, InternalCompanyToolContract>();
            });
        });

    private static async Task<SeededExecutionAgent> SeedAgentAsync(
        IServiceProvider services,
        Dictionary<string, JsonNode?> tools,
        Dictionary<string, JsonNode?> scopes,
        Dictionary<string, JsonNode?>? thresholds = null)
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var subject = $"finance-tool-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Finance Tool Tester";

        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
        dbContext.Companies.Add(new Company(companyId, "Finance Tool Company"));
        dbContext.CompanyMemberships.Add(new CompanyMembership(
            Guid.NewGuid(),
            companyId,
            userId,
            CompanyMembershipRole.Owner,
            CompanyMembershipStatus.Active));
        dbContext.Agents.Add(new Agent(
            agentId,
            companyId,
            "finance",
            "Nora Ledger",
            "Finance Manager",
            "Finance",
            null,
            AgentSeniority.Senior,
            AgentStatus.Active,
            autonomyLevel: AgentAutonomyLevel.Level2,
            objectives: Payload(("primary", new JsonArray(JsonValue.Create("Protect cash flow")))),
            kpis: Payload(("targets", new JsonArray(JsonValue.Create("forecast_accuracy")))),
            tools: tools,
            scopes: scopes,
            thresholds: thresholds ?? Payload(("approval", new JsonObject { ["cashReadUsd"] = 100000 })),
            escalationRules: Payload(("escalateTo", JsonValue.Create("founder"))),
            roleBrief: "Execution-ready finance profile.",
            triggerLogic: Payload(("enabled", JsonValue.Create(false))),
            workingHours: Payload(
                ("timezone", JsonValue.Create("UTC")),
                ("windows", new JsonArray(
                    new JsonObject
                    {
                        ["day"] = "monday",
                        ["start"] = "08:00",
                        ["end"] = "16:00"
                    })))));

        await dbContext.SaveChangesAsync();

        return new SeededExecutionAgent(companyId, agentId, userId, subject, email, displayName);
    }

    private static async Task<SeededFinanceRecord> SeedFinanceRecordAsync(IServiceProvider services, Guid companyId)
    {
        var accountId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        using var scope = services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(companyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            1000m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(counterpartyId, companyId, "Vendor", "vendor"));
        dbContext.FinanceTransactions.Add(new FinanceTransaction(
            transactionId,
            companyId,
            accountId,
            counterpartyId,
            null,
            null,
            new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
            "uncategorized",
            -250m,
            "USD",
            "Cloud tools",
            $"txn-{transactionId:N}"));
        dbContext.WorkTasks.Add(new WorkTask(
            taskId,
            companyId,
            "finance",
            "Review transaction category",
            "Requires approval before mutation.",
            WorkTaskPriority.Normal,
            null,
            null,
            "agent",
            Guid.NewGuid(),
            new Dictionary<string, JsonNode?> { ["transactionId"] = JsonValue.Create(transactionId) }));

        await dbContext.SaveChangesAsync();
        return new SeededFinanceRecord(transactionId, taskId);
    }

    private static async Task<ApprovalRequestDto> GetApprovalAsync(IServiceProvider services, Guid companyId, Guid approvalId)
    {
        using var scope = services.CreateScope();
        var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
        companyContextAccessor.SetCompanyId(companyId);
        var service = scope.ServiceProvider.GetRequiredService<IApprovalRequestService>();
        return await service.GetAsync(companyId, approvalId, CancellationToken.None);
    }

    private static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory, SeededExecutionAgent seed)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, seed.Subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, seed.Email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, seed.DisplayName);
        return client;
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, JsonNode? Value)[] properties)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in properties)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }

    private sealed record SeededExecutionAgent(
        Guid CompanyId,
        Guid AgentId,
        Guid UserId,
        string Subject,
        string Email,
        string DisplayName);

    private sealed record SeededFinanceRecord(
        Guid TransactionId,
        Guid TaskId);

    public sealed class TrackingFinanceToolProvider : IFinanceToolProvider
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentQueue<string> _callNames = new();

        public TrackingFinanceToolProvider(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public IReadOnlyList<string> CallNames => _callNames.ToArray();
        public int TotalCallCount => _callNames.Count;

        public Task<FinanceCashBalanceDto> GetCashBalanceAsync(
            GetFinanceCashBalanceQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetCashBalanceAsync));
            return Task.FromResult(new FinanceCashBalanceDto(
                query.CompanyId,
                query.AsOfUtc ?? new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc),
                9876.54m,
                "USD",
                [
                    new FinanceAccountBalanceDto(
                        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        "1000",
                        "Tracked Cash",
                        "asset",
                        9876.54m,
                        "USD",
                        query.AsOfUtc ?? new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc))
                ]));
        }

        public Task<FinanceMonthlyProfitAndLossDto> GetMonthlyProfitAndLossAsync(
            GetFinanceMonthlyProfitAndLossQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetMonthlyProfitAndLossAsync));
            return Task.FromResult(new FinanceMonthlyProfitAndLossDto(
                query.CompanyId,
                query.Year,
                query.Month,
                new DateTime(query.Year, query.Month, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(query.Year, query.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1),
                5000m,
                1750m,
                3250m,
                "USD"));
        }

        public Task<FinanceExpenseBreakdownDto> GetExpenseBreakdownAsync(
            GetFinanceExpenseBreakdownQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetExpenseBreakdownAsync));
            return Task.FromResult(new FinanceExpenseBreakdownDto(
                query.CompanyId,
                query.StartUtc,
                query.EndUtc,
                100m,
                "USD",
                [new FinanceExpenseCategoryDto("software", 100m, "USD")]));
        }

        public Task<IReadOnlyList<FinanceTransactionDto>> GetTransactionsAsync(
            GetFinanceTransactionsQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetTransactionsAsync));
            IReadOnlyList<FinanceTransactionDto> transactions =
            [
                new FinanceTransactionDto(
                    Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "Tracked Cash",
                    null,
                    null,
                    null,
                    null,
                    new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc),
                    "uncategorized",
                    -42m,
                    "USD",
                    "Tracked uncategorized transaction",
                    "tracked-uncategorized",
                    null),
                new FinanceTransactionDto(
                    Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "Tracked Cash",
                    null,
                    null,
                    null,
                    null,
                    new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc),
                    "revenue",
                    250m,
                    "USD",
                    "Tracked categorized transaction",
                    "tracked-revenue",
                    null)
            ];
            return Task.FromResult(transactions);
        }

        public Task<IReadOnlyList<FinanceInvoiceDto>> GetInvoicesAsync(
            GetFinanceInvoicesQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetInvoicesAsync));
            IReadOnlyList<FinanceInvoiceDto> invoices =
            [
                new FinanceInvoiceDto(
                    Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                    Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    "Tracked Customer",
                    "INV-TRACKED-001",
                    new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc),
                    1200m,
                    "USD",
                    "awaiting_approval",
                    null),
                new FinanceInvoiceDto(
                    Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                    Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee"),
                    "Tracked Customer",
                    "INV-TRACKED-002",
                    new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
                    800m,
                    "USD",
                    "paid",
                    null)
            ];
            return Task.FromResult(invoices);
        }

        public Task<IReadOnlyList<FinanceBillDto>> GetBillsAsync(
            GetFinanceBillsQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetBillsAsync));
            IReadOnlyList<FinanceBillDto> bills = [];
            return Task.FromResult(bills);
        }

        public Task<IReadOnlyList<FinanceAccountBalanceDto>> GetBalancesAsync(
            GetFinanceBalancesQuery query,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(GetBalancesAsync));
            IReadOnlyList<FinanceAccountBalanceDto> balances =
            [
                new FinanceAccountBalanceDto(
                    Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    "1000",
                    "Tracked Cash",
                    "asset",
                    9876.54m,
                    "USD",
                    query.AsOfUtc ?? new DateTime(2026, 4, 16, 0, 0, 0, DateTimeKind.Utc))
            ];
            return Task.FromResult(balances);
        }

        public Task<FinanceTransactionCategoryRecommendationDto> RecommendTransactionCategoryAsync(
            InternalToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(RecommendTransactionCategoryAsync));
            var transactionId = request.Payload.TryGetValue("transactionId", out var node) && node is JsonValue value && value.TryGetValue<Guid>(out var guid)
                ? guid
                : Guid.Empty;
            return Task.FromResult(new FinanceTransactionCategoryRecommendationDto(transactionId, "software", 0.8m));
        }

        public Task<FinanceInvoiceApprovalRecommendationDto> RecommendInvoiceApprovalDecisionAsync(
            InternalToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(RecommendInvoiceApprovalDecisionAsync));
            var invoiceId = request.Payload.TryGetValue("invoiceId", out var node) && node is JsonValue value && value.TryGetValue<Guid>(out var guid)
                ? guid
                : Guid.Empty;
            return Task.FromResult(new FinanceInvoiceApprovalRecommendationDto(invoiceId, "approved", 0.8m));
        }

        public async Task<FinanceTransactionDto> UpdateTransactionCategoryAsync(
            UpdateFinanceTransactionCategoryCommand command,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(UpdateTransactionCategoryAsync));
            using var scope = _scopeFactory.CreateScope();
            var companyContextAccessor = scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>();
            companyContextAccessor.SetCompanyId(command.CompanyId);
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var transaction = await dbContext.FinanceTransactions
                .Include(x => x.Account)
                .Include(x => x.Counterparty)
                .SingleAsync(x => x.CompanyId == command.CompanyId && x.Id == command.TransactionId, cancellationToken);

            transaction.ChangeCategory(command.Category);
            await dbContext.SaveChangesAsync(cancellationToken);
            return new FinanceTransactionDto(transaction.Id, transaction.AccountId, transaction.Account.Name, transaction.CounterpartyId, transaction.Counterparty?.Name, transaction.InvoiceId, transaction.BillId, transaction.TransactionUtc, transaction.TransactionType, transaction.Amount, transaction.Currency, transaction.Description, transaction.ExternalReference, null);
        }

        public async Task<FinanceInvoiceDto> UpdateInvoiceApprovalStatusAsync(
            UpdateFinanceInvoiceApprovalStatusCommand command,
            CancellationToken cancellationToken)
        {
            _callNames.Enqueue(nameof(UpdateInvoiceApprovalStatusAsync));
            using var scope = _scopeFactory.CreateScope();
            var service = ActivatorUtilities.CreateInstance<VirtualCompany.Infrastructure.Finance.CompanyFinanceCommandService>(
                scope.ServiceProvider,
                scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>());
            return await service.UpdateInvoiceApprovalStatusAsync(command, cancellationToken);
        }
    }
}
