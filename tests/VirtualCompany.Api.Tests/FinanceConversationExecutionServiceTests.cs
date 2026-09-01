using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Infrastructure.Companies;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceConversationExecutionServiceTests
{
    [Fact]
    public async Task Read_plan_executes_and_synthesizes_only_validated_results()
    {
        var harness = new Harness(Plan(Step("cash", "get_cash_balance")));

        var result = await harness.Service.ExecuteAsync(harness.Request("cash-1"), default);

        Assert.Equal(FinanceConversationRunStates.Completed, result.State);
        Assert.True(Assert.Single(result.Steps).OutputSchemaValid);
        Assert.Equal(1, result.Metrics.ToolCalls);
        Assert.Equal(0.01m, result.Metrics.EstimatedCost);
        Assert.NotNull(result.Answer);
        Assert.Equal(1, harness.Reasoning.CallCount);
        Assert.Contains(harness.Reasoning.LastRequest!.Sources, source => source.Type == "validated_finance_tool_result");
    }

    [Fact]
    public async Task Transient_read_failure_is_retried_but_recommendations_are_not()
    {
        var harness = new Harness(Plan(Step("cash", "get_cash_balance")));
        harness.Executor.Responses.Enqueue(Failed("provider_timeout"));
        harness.Executor.Responses.Enqueue(Succeeded());

        var result = await harness.Service.ExecuteAsync(harness.Request("retry-1"), default);

        Assert.Equal(FinanceConversationRunStates.Completed, result.State);
        Assert.Equal(2, Assert.Single(result.Steps).AttemptCount);
        Assert.Equal(1, result.Metrics.RetryCount);
    }

    [Fact]
    public async Task Recommendation_transient_failure_is_not_retried()
    {
        var recommendation = Step("recommend", "recommend_transaction_category") with
        {
            ActionType = "recommend",
            ExpectedAction = "recommend"
        };
        var harness = new Harness(Plan(recommendation));
        harness.Executor.Responses.Enqueue(Failed("provider_timeout"));

        var result = await harness.Service.ExecuteAsync(harness.Request("recommend-1"), default);

        Assert.Equal(FinanceConversationRunStates.Failed, result.State);
        Assert.Equal(1, Assert.Single(result.Steps).AttemptCount);
        Assert.Equal(0, result.Metrics.RetryCount);
    }

    [Fact]
    public async Task Failed_dependency_is_skipped_and_partial_failure_is_not_reported_as_success()
    {
        var dependent = Step("dependent", "get_cash_balance", ["first"]) with { Order = 2 };
        var harness = new Harness(Plan(Step("first", "get_cash_balance"), dependent));
        harness.Executor.Responses.Enqueue(Failed("permanent_failure"));

        var result = await harness.Service.ExecuteAsync(harness.Request("partial-1"), default);

        Assert.Equal(FinanceConversationRunStates.Failed, result.State);
        Assert.Equal(FinanceConversationStepStates.Failed, result.Steps[0].State);
        Assert.Equal(FinanceConversationStepStates.Skipped, result.Steps[1].State);
        Assert.Contains("dependency:first", result.MissingEvidence);
        Assert.Null(result.Answer);
    }

    [Fact]
    public async Task One_valid_result_and_one_failed_read_produces_an_explicit_partial_answer()
    {
        var second = Step("second", "get_cash_balance") with { Order = 2 };
        var harness = new Harness(Plan(Step("first", "get_cash_balance"), second));
        harness.Executor.Responses.Enqueue(Succeeded());
        harness.Executor.Responses.Enqueue(Failed("permanent_failure"));

        var result = await harness.Service.ExecuteAsync(harness.Request("partial-2"), default);

        Assert.Equal(FinanceConversationRunStates.PartiallyCompleted, result.State);
        Assert.NotNull(result.Answer);
        Assert.Contains(result.Steps, step => step.State == FinanceConversationStepStates.Completed);
        Assert.Contains(result.Steps, step => step.State == FinanceConversationStepStates.Failed);
    }

    [Fact]
    public async Task Invalid_output_is_rejected_before_synthesis()
    {
        var harness = new Harness(Plan(Step("cash", "get_cash_balance")));
        harness.Executor.Responses.Enqueue(Succeeded(new Dictionary<string, JsonNode?>
        {
            ["schemaVersion"] = "bad",
            ["status"] = "executed",
            ["success"] = true,
            ["data"] = new JsonObject()
        }));

        var result = await harness.Service.ExecuteAsync(harness.Request("invalid-1"), default);

        Assert.Equal(FinanceConversationRunStates.Failed, result.State);
        Assert.False(Assert.Single(result.Steps).OutputSchemaValid);
        Assert.Equal(0, harness.Reasoning.CallCount);
    }

    [Fact]
    public async Task Duplicate_delivery_reuses_one_logical_run_and_one_read_attempt()
    {
        var harness = new Harness(Plan(Step("cash", "get_cash_balance")));
        var request = harness.Request("duplicate-1");

        var first = await harness.Service.ExecuteAsync(request, default);
        var duplicate = await harness.Service.ExecuteAsync(request, default);

        Assert.Equal(first.RunId, duplicate.RunId);
        Assert.False(first.IsDuplicate);
        Assert.True(duplicate.IsDuplicate);
        Assert.Equal(1, harness.Executor.CallCount);
        Assert.Equal(1, harness.Planner.CallCount);
    }

    [Fact]
    public async Task Unsupported_plan_does_not_execute_or_answer_from_general_knowledge()
    {
        var harness = new Harness(Plan(state: FinanceToolPlanStates.Unsupported, steps: []));

        var result = await harness.Service.ExecuteAsync(harness.Request("unsupported-1"), default);

        Assert.Equal(FinanceConversationRunStates.Unsupported, result.State);
        Assert.Empty(result.Steps);
        Assert.Null(result.Answer);
        Assert.Equal(0, harness.Executor.CallCount);
        Assert.Equal(0, harness.Reasoning.CallCount);
    }

    [Fact]
    public async Task Cross_tenant_context_is_rejected_before_planning()
    {
        var harness = new Harness(Plan(Step("cash", "get_cash_balance")));
        var request = harness.Request("tenant-1") with
        {
            Context = [new FinanceToolPlanContextItem(Guid.NewGuid(), "x", "record", "x", "x")]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Service.ExecuteAsync(request, default));
        Assert.Equal(0, harness.Planner.CallCount);
    }

    [Fact]
    public async Task Cancellation_is_reported_without_synthesis()
    {
        var harness = new Harness(Plan(Step("cash", "get_cash_balance")));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await harness.Service.ExecuteAsync(harness.Request("cancel-1"), cancellation.Token);

        Assert.Equal(FinanceConversationRunStates.Cancelled, result.State);
        Assert.Equal(0, harness.Executor.CallCount);
        Assert.Null(result.Answer);
    }

    [Fact]
    public async Task Internal_cancellation_is_reported_as_timeout()
    {
        var harness = new Harness(Plan(Step("cash", "get_cash_balance")));
        harness.Planner.ThrowCancellation = true;

        var result = await harness.Service.ExecuteAsync(harness.Request("timeout-1"), default);

        Assert.Equal(FinanceConversationRunStates.TimedOut, result.State);
        Assert.Equal(0, harness.Executor.CallCount);
    }

    [Fact]
    public async Task Stale_context_triggers_one_bounded_plan_revision_before_execution()
    {
        var harness = new Harness(Plan(Step("cash", "get_cash_balance")));
        harness.Projector.Freshness.Enqueue(false);
        harness.Projector.Freshness.Enqueue(true);

        var result = await harness.Service.ExecuteAsync(harness.Request("replan-1"), default);

        Assert.Equal(FinanceConversationRunStates.Completed, result.State);
        Assert.Equal(2, harness.Planner.CallCount);
        Assert.Equal(2, result.PlanRevisions.Count);
        Assert.Equal(1, harness.Executor.CallCount);
    }

    [Fact]
    public async Task Declared_dependency_resolution_signal_triggers_bounded_replanning()
    {
        var dependent = Step("dependent", "get_cash_balance", ["first"]) with { Order = 2 };
        var harness = new Harness(Plan(Step("first", "get_cash_balance"), dependent));
        var signal = Succeeded();
        signal.ExecutionResult!["resolvedPlanningDependency"] = true;
        harness.Executor.Responses.Enqueue(signal);
        harness.Executor.Responses.Enqueue(Succeeded());

        var result = await harness.Service.ExecuteAsync(harness.Request("replan-signal-1"), default);

        Assert.Equal(FinanceConversationRunStates.Completed, result.State);
        Assert.Equal(2, result.PlanRevisions.Count);
        Assert.Equal(2, harness.Planner.CallCount);
        Assert.Equal(2, harness.Executor.CallCount);
    }

    [Fact]
    public void Six_existing_analysis_capabilities_are_exposed_through_one_trusted_adapter()
    {
        var registry = new StaticCompanyToolRegistry();
        Assert.True(registry.TryGetToolDefinition(FinanceAgentAnalysisToolIds.Analyze, out var definition));
        Assert.Equal("recommend", definition.ActionType.ToStorageValue());
        var values = definition.InputSchema["properties"]!["analysisType"]!["enum"]!.AsArray()
            .Select(value => value!.GetValue<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.True(FinanceAgentAnalysisTypes.All.SetEquals(values));
    }

    private static FinanceToolPlanStep Step(string id, string tool, IReadOnlyList<string>? dependencies = null) =>
        new(id, 1, dependencies ?? [], "read", "retrieve evidence", tool, "1.0.0", "read", "finance",
            new Dictionary<string, JsonNode?>(), ["authoritative_finance_records"],
            FinanceToolPlanCheckpointStates.NotRequired, FinanceToolPlanCheckpointStates.NotRequired, 0.01m);

    private static FinanceToolPlan Plan(params FinanceToolPlanStep[] steps) => Plan(FinanceToolPlanStates.Ready, steps);

    private static FinanceToolPlan Plan(string state, FinanceToolPlanStep[] steps) =>
        new(Guid.NewGuid(), 1, FinanceToolPlanVersions.ContractV1, Guid.Empty, Guid.Empty, state,
            state == FinanceToolPlanStates.Ready ? FinanceToolPlanReasonCodes.Planned : FinanceToolPlanReasonCodes.UnsupportedRequest,
            state == FinanceToolPlanStates.Ready ? "ready" : "unsupported", steps,
            new FinanceToolPlanLimits(8, 20, 48_000, 32_000, 1, 8, 30, 5), "authority-v1", new string('a', 64),
            FinancePlanningContextVersions.V1, new string('b', 64), [], "request-hash", "correlation", DateTime.UtcNow);

    private static ExecuteAgentToolResultDto Succeeded(Dictionary<string, JsonNode?>? payload = null) =>
        new(Guid.NewGuid(), "executed", null, null!, payload ?? new Dictionary<string, JsonNode?>
        {
            ["schemaVersion"] = InternalToolExecutionResponse.SchemaVersion,
            ["status"] = "executed",
            ["success"] = true,
            ["userSafeSummary"] = "Cash balance retrieved.",
            ["data"] = new JsonObject { ["cashBalance"] = new JsonObject { ["amount"] = 1200, ["currency"] = "SEK" } }
        }, "Cash balance retrieved.");

    private static ExecuteAgentToolResultDto Failed(string code) =>
        new(Guid.NewGuid(), "failed", null, null!, new Dictionary<string, JsonNode?>
        {
            ["status"] = "failed", ["success"] = false, ["errorCode"] = code
        }, "Read failed.");

    private sealed class Harness
    {
        public Harness(FinanceToolPlan plan)
        {
            CompanyId = Guid.NewGuid();
            AgentId = Guid.NewGuid();
            plan = plan with { CompanyId = CompanyId, AgentId = AgentId };
            Planner = new PlannerStub(plan);
            Executor = new ExecutorStub();
            Reasoning = new ReasoningStub();
            Projector = new ProjectorStub();
            Service = new FinanceConversationExecutionService(Planner, Projector, Executor,
                new StaticCompanyToolRegistry(), Reasoning, new UserStub(), new FinanceConversationExecutionRegistry(),
                Options.Create(new FinanceConversationExecutionOptions()),
                NullLogger<FinanceConversationExecutionService>.Instance);
        }

        public Guid CompanyId { get; }
        public Guid AgentId { get; }
        public PlannerStub Planner { get; }
        public ExecutorStub Executor { get; }
        public ProjectorStub Projector { get; }
        public ReasoningStub Reasoning { get; }
        public FinanceConversationExecutionService Service { get; }
        public ExecuteFinanceConversationRequest Request(string idempotencyKey) =>
            new(CompanyId, AgentId, "How is cash?", idempotencyKey);
    }

    private sealed class PlannerStub(FinanceToolPlan plan) : IFinanceToolPlanner
    {
        public int CallCount { get; private set; }
        public bool ThrowCancellation { get; set; }
        public Task<FinanceToolPlan> PlanAsync(FinanceToolPlanRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (ThrowCancellation) throw new OperationCanceledException();
            return Task.FromResult(plan);
        }
    }

    private sealed class ProjectorStub : IFinancePlanningContextProjector
    {
        public Queue<bool> Freshness { get; } = new();
        public Task<FinancePlanningContextBundle> ProjectAsync(FinancePlanningContextProjectionRequest request,
            AgentEffectiveAuthorityDto authority, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<FinancePlanningContextFreshnessResult> CheckFreshnessAsync(
            FinancePlanningContextProjectionRequest request, string expectedHash, CancellationToken cancellationToken) =>
            Task.FromResult(new FinancePlanningContextFreshnessResult(
                Freshness.Count == 0 || Freshness.Dequeue(), expectedHash, expectedHash,
                Freshness.Count == 0 ? "finance_planning_context_current" : "finance_planning_context_stale"));
    }

    private sealed class ExecutorStub : IAgentToolExecutionService
    {
        public Queue<ExecuteAgentToolResultDto> Responses { get; } = new();
        public int CallCount { get; private set; }
        public Task<ExecuteAgentToolResultDto> ExecuteAsync(Guid companyId, Guid agentId,
            ExecuteAgentToolCommand command, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : Succeeded());
        }
    }

    private sealed class ReasoningStub : IAgentReasoningGateway
    {
        public int CallCount { get; private set; }
        public AgentReasoningRequest? LastRequest { get; private set; }
        public Task<AgentReasoningResult> ReasonAsync(AgentReasoningRequest request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            var source = request.Sources.First();
            return Task.FromResult(new AgentReasoningResult(Guid.NewGuid(), "completed", "v1", "Cash is SEK 1,200.",
                [new AgentAiClaim("Cash is SEK 1,200.", "fact", 1, [source.Id])], 1, [], [], [], [source.Id]));
        }
        public Task<AgentReasoningResult?> GetRunAsync(Guid companyId, Guid agentId, Guid runId,
            CancellationToken cancellationToken) => Task.FromResult<AgentReasoningResult?>(null);
    }

    private sealed class UserStub : ICurrentUserAccessor
    {
        private readonly Guid _id = Guid.NewGuid();
        public ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity());
        public bool IsAuthenticated => true;
        public Guid? UserId => _id;
        public AuthenticatedUserIdentity Current => new(true, _id, null);
    }
}
