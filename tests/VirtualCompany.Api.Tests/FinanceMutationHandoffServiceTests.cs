using System.Security.Claims;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceMutationHandoffServiceTests
{
    [Fact]
    public async Task Preview_binds_exact_effect_and_confirmation_executes_once_after_authoritative_reread()
    {
        await using var harness = await Harness.CreateAsync();
        var preview = await harness.PreviewAsync();

        var step = Assert.Single(preview.Steps);
        Assert.Equal(FinanceMutationPreviewStates.Ready, preview.State);
        Assert.Equal("finance_transaction", step.Target.EntityType);
        Assert.Equal(harness.TransactionId, step.Target.EntityId);
        Assert.Equal("uncategorized", step.Target.State["category"]!.GetValue<string>());
        Assert.Equal(FinanceToolReversibility.Reversible, step.Reversibility);
        Assert.Equal(FinanceToolRiskTiers.Low, step.RiskTier);
        Assert.Equal("finance.edit", step.RequiredPermission);
        Assert.Equal(PolicyDecisionOutcomeValues.Allow, step.PolicyOutcome);

        var request = new ConfirmFinanceMutationRequest(harness.CompanyId, harness.AgentId, step.ConfirmationToken);
        var first = await harness.Service.ConfirmAsync(request, default);
        var replay = await harness.Service.ConfirmAsync(request, default);

        Assert.Equal(FinanceMutationConfirmationStates.Executed, first.State);
        Assert.Equal("office_costs", first.AuthoritativeState!.State["category"]!.GetValue<string>());
        Assert.False(first.IsDuplicate);
        Assert.True(replay.IsDuplicate);
        Assert.Equal(first.ExecutionId, replay.ExecutionId);
        Assert.Equal(1, harness.Executor.CallCount);
    }

    [Fact]
    public async Task Actor_mismatch_and_tampered_token_are_rejected_before_execution()
    {
        await using var harness = await Harness.CreateAsync();
        var token = Assert.Single((await harness.PreviewAsync()).Steps).ConfirmationToken;

        harness.User.UserIdValue = Guid.NewGuid();
        var mismatch = await harness.Service.ConfirmAsync(
            new ConfirmFinanceMutationRequest(harness.CompanyId, harness.AgentId, token), default);
        var tampered = await harness.Service.ConfirmAsync(
            new ConfirmFinanceMutationRequest(harness.CompanyId, harness.AgentId, token + "x"), default);

        Assert.Equal(FinanceMutationConfirmationStates.Invalid, mismatch.State);
        Assert.Equal(FinanceMutationConfirmationStates.Invalid, tampered.State);
        Assert.Equal(0, harness.Executor.CallCount);
    }

    [Fact]
    public async Task Expired_authority_policy_and_target_stale_confirmations_are_blocked()
    {
        await using var expiredHarness = await Harness.CreateAsync();
        var expiredToken = Assert.Single((await expiredHarness.PreviewAsync()).Steps).ConfirmationToken;
        expiredHarness.Clock.Advance(TimeSpan.FromMinutes(6));
        var expired = await expiredHarness.Service.ConfirmAsync(
            new ConfirmFinanceMutationRequest(expiredHarness.CompanyId, expiredHarness.AgentId, expiredToken), default);
        Assert.Equal(FinanceMutationConfirmationStates.Expired, expired.State);

        await using var authorityHarness = await Harness.CreateAsync();
        var authorityToken = Assert.Single((await authorityHarness.PreviewAsync()).Steps).ConfirmationToken;
        authorityHarness.Authority.Value = authorityHarness.Authority.Value with { AuthorityHash = new string('c', 64) };
        var authorityStale = await authorityHarness.Service.ConfirmAsync(
            new ConfirmFinanceMutationRequest(authorityHarness.CompanyId, authorityHarness.AgentId, authorityToken), default);
        Assert.Equal("finance_confirmation_authority_stale", authorityStale.ReasonCode);

        await using var policyHarness = await Harness.CreateAsync();
        var policyToken = Assert.Single((await policyHarness.PreviewAsync()).Steps).ConfirmationToken;
        policyHarness.Profile.Value.ApprovalThresholds["categorization"] =
            new JsonObject { ["policyVersion"] = "v2" };
        var policyStale = await policyHarness.Service.ConfirmAsync(
            new ConfirmFinanceMutationRequest(policyHarness.CompanyId, policyHarness.AgentId, policyToken), default);
        Assert.Equal("finance_confirmation_policy_stale", policyStale.ReasonCode);

        await using var targetHarness = await Harness.CreateAsync();
        var targetToken = Assert.Single((await targetHarness.PreviewAsync()).Steps).ConfirmationToken;
        var transaction = await targetHarness.Db.FinanceTransactions.IgnoreQueryFilters().SingleAsync(item => item.Id == targetHarness.TransactionId);
        transaction.ChangeCategory("travel");
        await targetHarness.Db.SaveChangesAsync();
        var targetStale = await targetHarness.Service.ConfirmAsync(
            new ConfirmFinanceMutationRequest(targetHarness.CompanyId, targetHarness.AgentId, targetToken), default);
        Assert.Equal("finance_confirmation_target_stale", targetStale.ReasonCode);
        Assert.Equal(0, targetHarness.Executor.CallCount);
    }

    [Fact]
    public async Task Approval_confirmation_creates_handoff_result_without_mutation()
    {
        await using var harness = await Harness.CreateAsync(approvalRequired: true);
        harness.Executor.Mode = ExecutorMode.ApprovalRequired;
        var preview = await harness.PreviewAsync();

        Assert.Equal(FinanceMutationPreviewStates.ApprovalRequired, preview.State);
        var result = await harness.Service.ConfirmAsync(new ConfirmFinanceMutationRequest(
            harness.CompanyId, harness.AgentId, Assert.Single(preview.Steps).ConfirmationToken), default);

        Assert.Equal(FinanceMutationConfirmationStates.ApprovalRequired, result.State);
        Assert.NotNull(result.ApprovalRequestId);
        Assert.Equal("uncategorized", (await harness.Db.FinanceTransactions.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(item => item.Id == harness.TransactionId)).TransactionType);
    }

    [Fact]
    public async Task Provider_ack_is_queued_until_authoritative_state_reconciles()
    {
        await using var harness = await Harness.CreateAsync();
        harness.Executor.Mode = ExecutorMode.Queued;
        var token = Assert.Single((await harness.PreviewAsync()).Steps).ConfirmationToken;

        var queued = await harness.Service.ConfirmAsync(
            new ConfirmFinanceMutationRequest(harness.CompanyId, harness.AgentId, token), default);
        Assert.Equal(FinanceMutationConfirmationStates.Queued, queued.State);

        var transaction = await harness.Db.FinanceTransactions.IgnoreQueryFilters().SingleAsync(item => item.Id == harness.TransactionId);
        transaction.ChangeCategory("office_costs");
        await harness.Db.SaveChangesAsync();
        var reconciled = await harness.Service.ReconcileAsync(
            new ReconcileFinanceMutationRequest(harness.CompanyId, harness.AgentId, token), default);
        Assert.Equal(FinanceMutationConfirmationStates.Executed, reconciled.State);
    }

    private sealed class Harness : IAsyncDisposable
    {
        private Harness(bool approvalRequired)
        {
            CompanyId = Guid.NewGuid();
            AgentId = Guid.NewGuid();
            TransactionId = Guid.NewGuid();
            var authorityState = approvalRequired ? AgentCapabilityStates.ApprovalRequired : AgentCapabilityStates.Available;
            var approvalBehavior = approvalRequired ? "required" : "policy_determined";
            Authority = new MutableAuthority(new AgentEffectiveAuthorityDto(
                CompanyId, AgentId, "Laura", "Finance", AgentStatusValues.Active, true, "supervised",
                "authority-v1", new string('a', 64), [], [],
                [new EffectiveAgentToolAuthorityDto("categorize_transaction", "1.0.0", "execute", "finance",
                    authorityState, AgentAuthorityReasonCodes.Available, "Available", "configured", "v1", [], [])
                    { ActorPermission = "FinanceCategorize", ApprovalBehavior = approvalBehavior }], DateTime.UtcNow));
            var step = new FinanceToolPlanStep("categorize", 1, [], "Categorize transaction",
                "Change the transaction category to office costs.", "categorize_transaction", "1.0.0",
                "execute", "finance", new Dictionary<string, JsonNode?>
                {
                    ["transactionId"] = TransactionId,
                    ["category"] = "office_costs"
                }, ["finance_transaction"], FinanceToolPlanCheckpointStates.Required,
                approvalRequired ? FinanceToolPlanCheckpointStates.Pending : FinanceToolPlanCheckpointStates.NotRequired, 0);
            var plan = new FinanceToolPlan(Guid.NewGuid(), 1, FinanceToolPlanVersions.ContractV1, CompanyId, AgentId,
                approvalRequired ? FinanceToolPlanStates.ApprovalRequired : FinanceToolPlanStates.ConfirmationRequired,
                approvalRequired ? FinanceToolPlanReasonCodes.ApprovalRequired : FinanceToolPlanReasonCodes.ConfirmationRequired,
                "Review the exact effect.", [step], new FinanceToolPlanLimits(8, 20, 48_000, 32_000, 1, 8, 30, 5),
                Authority.Value.AuthorityVersion, Authority.Value.AuthorityHash, FinancePlanningContextVersions.V1,
                new string('b', 64), [], "request-hash", "correlation", DateTime.UtcNow);
            Planner = new PlannerStub(plan);
            User = new UserStub(Guid.NewGuid());
            Clock = new MutableTimeProvider(DateTimeOffset.UtcNow);
            Db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            Executor = new ExecutorStub(Db, TransactionId);
            Profile = new ProfileStub(CreateProfile(CompanyId, AgentId));
            Service = new FinanceMutationHandoffService(Planner, Executor, Authority,
                Profile, new StaticCompanyToolRegistry(), new GuardrailStub(approvalRequired), User, Db, new AuditStub(),
                new FinanceMutationConfirmationRegistry(), new EphemeralDataProtectionProvider(), Clock,
                Options.Create(new FinanceMutationHandoffOptions { ConfirmationLifetimeSeconds = 300 }));
        }

        public Guid CompanyId { get; }
        public Guid AgentId { get; }
        public Guid TransactionId { get; }
        public VirtualCompanyDbContext Db { get; }
        public PlannerStub Planner { get; }
        public ExecutorStub Executor { get; }
        public MutableAuthority Authority { get; }
        public ProfileStub Profile { get; }
        public UserStub User { get; }
        public MutableTimeProvider Clock { get; }
        public FinanceMutationHandoffService Service { get; }

        public static async Task<Harness> CreateAsync(bool approvalRequired = false)
        {
            var harness = new Harness(approvalRequired);
            harness.Db.FinanceTransactions.Add(new FinanceTransaction(harness.TransactionId, harness.CompanyId,
                Guid.NewGuid(), null, null, null, DateTime.UtcNow, "uncategorized", 125m, "SEK",
                "Office supplies", "bank-1"));
            await harness.Db.SaveChangesAsync();
            return harness;
        }

        public Task<FinanceMutationPreviewResult> PreviewAsync() => Service.PreviewAsync(
            new PreviewFinanceMutationRequest(CompanyId, AgentId, "Categorize this transaction as office costs."), default);

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class PlannerStub(FinanceToolPlan plan) : IFinanceToolPlanner
    {
        public Task<FinanceToolPlan> PlanAsync(FinanceToolPlanRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(plan);
    }

    private sealed class MutableAuthority(AgentEffectiveAuthorityDto value) : IAgentEffectiveAuthorityResolver
    {
        public AgentEffectiveAuthorityDto Value { get; set; } = value;
        public Task<AgentEffectiveAuthorityDto> ResolveAsync(Guid companyId, Guid agentId, CancellationToken cancellationToken) =>
            Task.FromResult(Value);
    }

    private sealed class ProfileStub(AgentRuntimeProfileDto profile) : IAgentRuntimeProfileResolver
    {
        public AgentRuntimeProfileDto Value { get; set; } = profile;
        public Task<AgentRuntimeProfileDto> GetCurrentProfileAsync(Guid companyId, Guid agentId,
            CancellationToken cancellationToken, string? generationPath = null, string? correlationId = null) =>
            Task.FromResult(Value);
    }

    private enum ExecutorMode { Executed, ApprovalRequired, Queued }

    private sealed class ExecutorStub(VirtualCompanyDbContext db, Guid transactionId) : IAgentToolExecutionService
    {
        public ExecutorMode Mode { get; set; }
        public int CallCount { get; private set; }

        public async Task<ExecuteAgentToolResultDto> ExecuteAsync(Guid companyId, Guid agentId,
            ExecuteAgentToolCommand command, CancellationToken cancellationToken)
        {
            CallCount++;
            var decision = new ToolExecutionDecisionDto(
                Mode == ExecutorMode.ApprovalRequired ? PolicyDecisionOutcomeValues.RequireApproval : PolicyDecisionOutcomeValues.Allow,
                [], "Policy evaluated.", "supervised", "execute", "finance",
                Mode == ExecutorMode.ApprovalRequired, new Dictionary<string, JsonNode?>());
            if (Mode == ExecutorMode.ApprovalRequired)
                return new ExecuteAgentToolResultDto(Guid.NewGuid(), "awaiting_approval", Guid.NewGuid(), decision,
                    new Dictionary<string, JsonNode?> { ["status"] = "awaiting_approval", ["success"] = false },
                    "Approval required.");
            if (Mode == ExecutorMode.Queued)
                return new ExecuteAgentToolResultDto(Guid.NewGuid(), "executed", null, decision,
                    new Dictionary<string, JsonNode?> { ["status"] = "queued", ["success"] = true },
                    "Accepted by provider.");

            var target = await db.FinanceTransactions.IgnoreQueryFilters().SingleAsync(item => item.Id == transactionId, cancellationToken);
            target.ChangeCategory(command.RequestPayload!["category"]!.GetValue<string>());
            await db.SaveChangesAsync(cancellationToken);
            return new ExecuteAgentToolResultDto(Guid.NewGuid(), "executed", null, decision,
                new Dictionary<string, JsonNode?> { ["status"] = "executed", ["success"] = true },
                "Executed.");
        }
    }

    private sealed class UserStub(Guid userId) : ICurrentUserAccessor
    {
        public Guid UserIdValue { get; set; } = userId;
        public ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity());
        public bool IsAuthenticated => true;
        public Guid? UserId => UserIdValue;
        public AuthenticatedUserIdentity Current => new(true, UserIdValue, null);
    }

    private sealed class AuditStub : IAuditEventWriter
    {
        public Task WriteAsync(AuditEventWriteRequest auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class GuardrailStub(bool approvalRequired) : IPolicyGuardrailEngine
    {
        public ToolExecutionDecisionDto Evaluate(PolicyEvaluationRequest request) => new(
            approvalRequired ? PolicyDecisionOutcomeValues.RequireApproval : PolicyDecisionOutcomeValues.Allow,
            [], "Policy evaluated.", request.EvaluatedAutonomyLevel, request.ActionType!.Value.ToStorageValue(),
            request.Scope, approvalRequired, new Dictionary<string, JsonNode?>());
    }

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }

    private static AgentRuntimeProfileDto CreateProfile(Guid companyId, Guid agentId) => new(
        agentId, companyId, "laura-finance", "Laura", "Finance manager", "Finance", "senior",
        AgentStatusValues.Active, null, new(), new(), new(), new(), new(),
        new Dictionary<string, JsonNode?> { ["categorization"] = new JsonObject { ["policyVersion"] = "v1" } },
        new(), new(), new(), AgentCommunicationProfileDto.Empty,
        new(), true, DateTime.UtcNow, "supervised");
}
