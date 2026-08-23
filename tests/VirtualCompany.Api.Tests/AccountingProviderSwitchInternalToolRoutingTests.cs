using System.Reflection;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingProviderSwitchInternalToolRoutingTests
{
    [Fact]
    public async Task Read_briefing_routes_through_typed_finance_contract_with_company_and_switch_scope()
    {
        var finance = new StubMigrationAgentService();
        var contract = CreateContract(finance);
        var companyId = Guid.NewGuid();
        var switchId = Guid.NewGuid();

        var response = await contract.ExecuteAsync(new InternalToolExecutionRequest(
            AccountingProviderSwitchAgentToolIds.ReadBriefing,
            new InternalToolExecutionContext(companyId, Guid.NewGuid(), Guid.NewGuid(), ToolActionType.Read, "finance"),
            new Dictionary<string, JsonNode?> { ["switchId"] = JsonValue.Create(switchId) }), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal(companyId, finance.BriefingQuery!.CompanyId);
        Assert.Equal(switchId, finance.BriefingQuery.SwitchId);
        Assert.NotNull(response.Data["briefing"]);
        Assert.Equal("accounting_provider_switch_agent_service", response.Metadata["contractName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Execute_routes_actor_agent_version_correlation_and_idempotency_to_finance_contract()
    {
        var finance = new StubMigrationAgentService();
        var contract = CreateContract(finance);
        var companyId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();
        var switchId = Guid.NewGuid();

        var response = await contract.ExecuteAsync(new InternalToolExecutionRequest(
            AccountingProviderSwitchAgentToolIds.StartAssessment,
            new InternalToolExecutionContext(companyId, agentId, Guid.NewGuid(), ToolActionType.Execute, "finance",
                CorrelationId: "corr-laura-migration", ActorUserId: actorUserId),
            new Dictionary<string, JsonNode?>
            {
                ["switchId"] = JsonValue.Create(switchId),
                ["expectedSwitchVersion"] = JsonValue.Create(7L),
                ["idempotencyKey"] = JsonValue.Create("assessment-switch-v7")
            }), CancellationToken.None);

        Assert.True(response.Success);
        var context = Assert.IsType<AccountingProviderSwitchAgentCommandContext>(finance.CommandContext);
        Assert.Equal(companyId, context.CompanyId);
        Assert.Equal(agentId, context.AgentId);
        Assert.Equal(actorUserId, context.ActorUserId);
        Assert.Equal(switchId, context.SwitchId);
        Assert.Equal(7, context.ExpectedSwitchVersion);
        Assert.Equal("corr-laura-migration", context.CorrelationId);
        Assert.Equal("assessment-switch-v7", context.IdempotencyKey);
    }

    [Fact]
    public async Task Stale_finance_result_returns_current_state_recovery_message()
    {
        var finance = new StubMigrationAgentService { ThrowStale = true };
        var contract = CreateContract(finance);
        var response = await contract.ExecuteAsync(new InternalToolExecutionRequest(
            AccountingProviderSwitchAgentToolIds.StartAssessment,
            new InternalToolExecutionContext(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), ToolActionType.Execute,
                "finance", CorrelationId: "corr", ActorUserId: Guid.NewGuid()),
            new Dictionary<string, JsonNode?>
            {
                ["switchId"] = JsonValue.Create(Guid.NewGuid()),
                ["expectedSwitchVersion"] = JsonValue.Create(2L),
                ["idempotencyKey"] = JsonValue.Create("assessment-stale-v2")
            }), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Equal(AccountingProviderSwitchReasonCodes.ConcurrencyConflict, response.ErrorCode);
        Assert.Contains("Read the current briefing", response.UserSafeSummary, StringComparison.OrdinalIgnoreCase);
    }

    private static InternalCompanyToolContract CreateContract(IAccountingProviderSwitchAgentService finance) =>
        new(
            Proxy<ICompanyTaskQueryService>(),
            Proxy<ICompanyTaskCommandService>(),
            Proxy<IProactiveTaskCreationService>(),
            Proxy<IApprovalRequestService>(),
            Proxy<ICompanyKnowledgeSearchService>(),
            Proxy<IFinanceToolProvider>(),
            Proxy<IFinanceTransactionAnomalyDetectionService>(),
            finance,
            Proxy<ILeadGenerationService>());

    private static T Proxy<T>() where T : class => DispatchProxy.Create<T, UnusedProxy>();

    private class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected call to {targetMethod?.Name}.");
    }

    private sealed class StubMigrationAgentService : IAccountingProviderSwitchAgentService
    {
        public GetAccountingProviderSwitchAgentBriefingQuery? BriefingQuery { get; private set; }
        public AccountingProviderSwitchAgentCommandContext? CommandContext { get; private set; }
        public bool ThrowStale { get; init; }

        public Task<AccountingProviderSwitchAgentBriefingDto> GetBriefingAsync(GetAccountingProviderSwitchAgentBriefingQuery query, CancellationToken cancellationToken)
        {
            BriefingQuery = query;
            return Task.FromResult(new AccountingProviderSwitchAgentBriefingDto(query.SwitchId, 4, "Draft",
                "Review intent.", [], ["Current switch"], ["Start assessment"], "Accounting administrator",
                "Start assessment", ["accounting switch"], DateTime.UtcNow));
        }

        public Task<AccountingProviderSwitchAgentCommandResultDto> StartAssessmentAsync(AccountingProviderSwitchAgentCommandContext context, CancellationToken cancellationToken)
        {
            CommandContext = context;
            if (ThrowStale)
                throw new AccountingAuthorityException(AccountingProviderSwitchReasonCodes.ConcurrencyConflict,
                    "Stale switch version.", isConflict: true);
            return Task.FromResult(new AccountingProviderSwitchAgentCommandResultDto(context.SwitchId,
                context.ExpectedSwitchVersion + 1, AccountingProviderSwitchAgentToolIds.StartAssessment,
                "Queued", "Assessment queued.", "Review gaps.", ["Finance application service"], new JsonObject()));
        }

        public Task<AccountingProviderSwitchAgentEvidenceDto> GetEvidenceAsync(GetAccountingProviderSwitchAgentEvidenceQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchAgentRecommendationDto> RecommendAsync(RecommendAccountingProviderSwitchActionQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchAgentCommandResultDto> StartRehearsalAsync(AccountingProviderSwitchAgentCommandContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchAgentCommandResultDto> StartPreparationAsync(AccountingProviderSwitchAgentCommandContext context, Guid planId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchAgentCommandResultDto> ApplyApprovedMappingAsync(AccountingProviderSwitchAgentCommandContext context, Guid stagedRecordId, Guid mappingDecisionId, long expectedRecordVersion, string disposition, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchAgentCommandResultDto> RequestPlanApprovalAsync(AccountingProviderSwitchAgentCommandContext context, Guid planId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchAgentCommandResultDto> StartApprovedFreezeAsync(AccountingProviderSwitchAgentCommandContext context, Guid cutoverExecutionId, long expectedExecutionVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchAgentCommandResultDto> RequestActivationApprovalAsync(AccountingProviderSwitchAgentCommandContext context, Guid cutoverExecutionId, long expectedExecutionVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<AccountingProviderSwitchAgentCommandResultDto> ResumeRecoveryAsync(AccountingProviderSwitchAgentCommandContext context, Guid cutoverExecutionId, long expectedExecutionVersion, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
