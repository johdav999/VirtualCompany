using System.Security.Claims;
using VirtualCompany.Infrastructure.Tenancy;
using VirtualCompany.Shared;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceAgentAuthorizationServiceTests
{
    [Theory]
    [InlineData(CompanyMembershipRole.Owner, "get_cash_balance", ToolActionType.Read, true, FinancePermissions.View)]
    [InlineData(CompanyMembershipRole.Admin, "categorize_transaction", ToolActionType.Execute, true, FinancePermissions.Edit)]
    [InlineData(CompanyMembershipRole.Manager, "recommend_transaction_category", ToolActionType.Recommend, true, FinancePermissions.View)]
    [InlineData(CompanyMembershipRole.FinanceApprover, "recommend_invoice_approval_decision", ToolActionType.Recommend, true, FinancePermissions.View)]
    [InlineData(CompanyMembershipRole.FinanceApprover, "categorize_transaction", ToolActionType.Execute, false, FinancePermissions.Edit)]
    [InlineData(CompanyMembershipRole.Employee, "get_cash_balance", ToolActionType.Read, false, FinancePermissions.View)]
    [InlineData(CompanyMembershipRole.Employee, "recommend_transaction_category", ToolActionType.Recommend, false, FinancePermissions.View)]
    [InlineData(CompanyMembershipRole.Employee, "categorize_transaction", ToolActionType.Execute, false, FinancePermissions.Edit)]
    [InlineData(CompanyMembershipRole.FinanceApprover, "get_cash_balance", ToolActionType.Read, true, FinancePermissions.View)]
    [InlineData(CompanyMembershipRole.Manager, "approve_invoice", ToolActionType.Execute, true, FinancePermissions.Approve)]
    [InlineData(CompanyMembershipRole.Manager, "post_paid_supplier_bill_expense", ToolActionType.Execute, true, FinancePermissions.AccountingAdmin)]
    [InlineData(CompanyMembershipRole.Manager, "finance.migration.start_assessment", ToolActionType.Execute, false, FinancePermissions.ManageIntegrations)]
    [InlineData(CompanyMembershipRole.Admin, "finance.migration.start_assessment", ToolActionType.Execute, true, FinancePermissions.ManageIntegrations)]
    public async Task Permission_mapping_returns_structured_authoritative_decision(
        CompanyMembershipRole role,
        string toolName,
        ToolActionType actionType,
        bool expectedAllowed,
        string expectedPermission)
    {
        await using var fixture = await Fixture.CreateAsync(role);

        var decision = await fixture.Service.AuthorizeAsync(
            fixture.Request(toolName, actionType) with
            {
                ActorUserId = fixture.ActorUserId,
                IsApprovedContinuation = true
            },
            CancellationToken.None);

        Assert.Equal(expectedAllowed, decision.IsAllowed);
        Assert.Equal(FinanceAgentActorTypes.Human, decision.ActorType);
        Assert.Equal(FinanceAgentMembershipStates.Active, decision.MembershipState);
        Assert.Contains(expectedPermission, decision.RequiredFinancePermissions);
        Assert.Equal(expectedAllowed
            ? FinanceAgentAuthorizationReasonCodes.Authorized
            : FinanceAgentAuthorizationReasonCodes.PermissionMissing, decision.ReasonCode);
        Assert.NotEmpty(decision.Evidence);
        Assert.Equal(FinanceAgentAuthorizationService.PolicyVersion, decision.PolicyVersion);
    }

    [Fact]
    public async Task Missing_actor_is_denied_without_falling_back_to_agent_identity()
    {
        await using var fixture = await Fixture.CreateAsync(CompanyMembershipRole.Owner);

        var decision = await fixture.Service.AuthorizeAsync(
            fixture.Request("get_cash_balance", ToolActionType.Read), CancellationToken.None);

        Assert.False(decision.IsAllowed);
        Assert.Equal(FinanceAgentActorTypes.Missing, decision.ActorType);
        Assert.Equal(FinanceAgentAuthorizationReasonCodes.ActorMissing, decision.ReasonCode);
        Assert.Null(decision.ActorId);
    }

    [Fact]
    public async Task Proactive_context_requires_the_explicit_finance_autonomy_policy_in_addition_to_actor_permission()
    {
        await using var fixture = await Fixture.CreateAsync(
            CompanyMembershipRole.Owner, new DenyAutonomyPolicy());
        var autonomy = new FinanceAutonomyEvaluationRequest(
            fixture.CompanyId, fixture.AgentId, FinanceAgentCoverageCapabilityIds.DailyCash,
            FinanceAutonomyTriggers.Schedule, "read", "get_cash_balance",
            1, null, DateTime.UtcNow);

        var decision = await fixture.Service.AuthorizeAsync(
            fixture.Request("get_cash_balance", ToolActionType.Read) with
            {
                ActorUserId = fixture.ActorUserId,
                IsDurableRunContinuation = true,
                AutonomyEvaluation = autonomy
            }, default);

        Assert.False(decision.IsAllowed);
        Assert.Equal(FinanceAutonomyDecisionReasonCodes.GrantMissing, decision.ReasonCode);
    }

    [Theory]
    [InlineData("valid", true, FinanceAgentAuthorizationReasonCodes.Authorized)]
    [InlineData("expired", false, FinanceAgentAuthorizationReasonCodes.DelegationExpired)]
    [InlineData("wrong_agent", false, FinanceAgentAuthorizationReasonCodes.DelegationAgentMismatch)]
    [InlineData("wrong_company", false, FinanceAgentAuthorizationReasonCodes.DelegationMissing)]
    [InlineData("wrong_workflow", false, FinanceAgentAuthorizationReasonCodes.DelegationWorkflowMismatch)]
    [InlineData("wrong_action", false, FinanceAgentAuthorizationReasonCodes.DelegationActionMismatch)]
    [InlineData("wrong_scope", false, FinanceAgentAuthorizationReasonCodes.DelegationScopeMismatch)]
    public async Task Background_authority_is_persisted_and_bound_to_exact_context(
        string variation,
        bool expectedAllowed,
        string expectedReason)
    {
        await using var fixture = await Fixture.CreateAsync(CompanyMembershipRole.Owner);
        var now = DateTime.UtcNow;
        var delegationId = Guid.NewGuid();
        var authority = new FinanceAgentDelegationAuthority(
            delegationId,
            variation == "wrong_company" ? Guid.NewGuid() : fixture.CompanyId,
            variation == "wrong_agent" ? Guid.NewGuid() : fixture.AgentId,
            fixture.ActorUserId,
            fixture.ActorUserId,
            fixture.WorkflowId,
            "finance",
            variation == "wrong_action" ? [ToolActionType.Recommend] : [ToolActionType.Read],
            variation == "wrong_scope" ? ["restricted"] : ["finance"],
            now.AddHours(-2),
            variation == "expired" ? now.AddMinutes(-1) : now.AddHours(2));
        fixture.Db.FinanceAgentDelegationAuthorities.Add(authority);
        await fixture.Db.SaveChangesAsync();

        var decision = await fixture.Service.AuthorizeAsync(
            fixture.Request("get_cash_balance", ToolActionType.Read) with
            {
                DelegationAuthorityId = delegationId,
                WorkflowInstanceId = variation == "wrong_workflow" ? Guid.NewGuid() : fixture.WorkflowId
            }, CancellationToken.None);

        Assert.Equal(expectedAllowed, decision.IsAllowed);
        Assert.Equal(expectedReason, decision.ReasonCode);
        Assert.Equal(FinanceAgentActorTypes.DelegatedBackground, decision.ActorType);
        Assert.Equal(delegationId, decision.DelegationAuthorityId);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(
            VirtualCompanyDbContext db,
            FinanceAgentAuthorizationService service,
            Guid companyId,
            Guid agentId,
            Guid actorUserId,
            Guid workflowId)
        {
            Db = db;
            Service = service;
            CompanyId = companyId;
            AgentId = agentId;
            ActorUserId = actorUserId;
            WorkflowId = workflowId;
        }

        public VirtualCompanyDbContext Db { get; }
        public FinanceAgentAuthorizationService Service { get; }
        public Guid CompanyId { get; }
        public Guid AgentId { get; }
        public Guid ActorUserId { get; }
        public Guid WorkflowId { get; }

        public static async Task<Fixture> CreateAsync(
            CompanyMembershipRole role,
            IFinanceAutonomyPolicyEvaluator? autonomyPolicy = null)
        {
            var companyId = Guid.NewGuid();
            var agentId = Guid.NewGuid();
            var actorUserId = Guid.NewGuid();
            var workflowId = Guid.NewGuid();
            var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);
            var company = new Company(companyId, "Finance authorization company");
            db.Companies.Add(company);
            db.Users.Add(new User(actorUserId, $"{actorUserId:N}@example.com", "Finance actor", "test", actorUserId.ToString("N")));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, actorUserId, role, CompanyMembershipStatus.Active));
            await db.SaveChangesAsync();

            var service = new FinanceAgentAuthorizationService(
                db, new AnonymousCurrentUserAccessor(), new NullMembershipResolver(), autonomyPolicy);
            return new Fixture(db, service, companyId, agentId, actorUserId, workflowId);
        }

        public FinanceAgentAuthorizationRequest Request(string toolName, ToolActionType actionType) =>
            new(CompanyId, AgentId, Guid.NewGuid(), toolName, actionType, "finance", WorkflowId, "finance-auth-test");

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class AnonymousCurrentUserAccessor : ICurrentUserAccessor
    {
        public ClaimsPrincipal Principal { get; } = new(new ClaimsIdentity());
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public AuthenticatedUserIdentity Current { get; } = new(false, null, null);
    }

    private sealed class NullMembershipResolver : ICompanyMembershipContextResolver
    {
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(CancellationToken cancellationToken) => Task.FromResult<ResolvedCompanyMembershipContext?>(null);
        public Task<ResolvedCompanyMembershipContext?> ResolveAsync(Guid companyId, CancellationToken cancellationToken) => Task.FromResult<ResolvedCompanyMembershipContext?>(null);
    }

    private sealed class DenyAutonomyPolicy : IFinanceAutonomyPolicyEvaluator
    {
        public Task<FinanceAutonomyDecisionDto> EvaluateAsync(
            FinanceAutonomyEvaluationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new FinanceAutonomyDecisionDto(
                false, FinanceAutonomyDecisionReasonCodes.GrantMissing,
                "No active Finance autonomy grant exists.",
                null, null, null, null, false, false, 0, 0, null,
                FinanceAutonomyPolicyVersions.V1, null, null, null, DateTime.UtcNow));
    }
}
