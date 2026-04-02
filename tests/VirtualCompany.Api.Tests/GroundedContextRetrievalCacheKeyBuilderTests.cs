using VirtualCompany.Application.Context;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Context;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class GroundedContextRetrievalCacheKeyBuilderTests
{
    private readonly GroundedContextRetrievalCacheKeyBuilder _builder = new();

    [Fact]
    public void BuildKnowledgeSectionKey_changes_when_company_changes()
    {
        var firstRequest = new GroundedContextRetrievalRequest(Guid.NewGuid(), Guid.NewGuid(), "finance payroll approvals", Guid.NewGuid());
        var secondRequest = firstRequest with { CompanyId = Guid.NewGuid() };

        var firstKey = _builder.BuildKnowledgeSectionKey("tests-v1", firstRequest, CreateDecision(firstRequest), "finance payroll approvals", 5);
        var secondKey = _builder.BuildKnowledgeSectionKey("tests-v1", secondRequest, CreateDecision(secondRequest), "finance payroll approvals", 5);

        Assert.NotEqual(firstKey, secondKey);
    }

    [Fact]
    public void BuildKnowledgeSectionKey_changes_when_agent_scope_or_query_changes()
    {
        var request = new GroundedContextRetrievalRequest(Guid.NewGuid(), Guid.NewGuid(), "finance payroll approvals", Guid.NewGuid());
        var baseDecision = CreateDecision(request, ["finance"]);
        var differentAgentDecision = baseDecision with { AgentId = Guid.NewGuid() };
        var differentScopeDecision = CreateDecision(request, ["hr"]);

        var baseKey = _builder.BuildKnowledgeSectionKey("tests-v1", request, baseDecision, "finance payroll approvals", 5);
        var differentAgentKey = _builder.BuildKnowledgeSectionKey("tests-v1", request with { AgentId = Guid.NewGuid() }, differentAgentDecision, "finance payroll approvals", 5);
        var differentScopeKey = _builder.BuildKnowledgeSectionKey("tests-v1", request, differentScopeDecision, "finance payroll approvals", 5);
        var differentQueryKey = _builder.BuildKnowledgeSectionKey("tests-v1", request, baseDecision, "hr staffing approvals", 5);

        Assert.NotEqual(baseKey, differentAgentKey);
        Assert.NotEqual(baseKey, differentScopeKey);
        Assert.NotEqual(baseKey, differentQueryKey);
    }

    [Fact]
    public void BuildMemorySectionKey_changes_when_key_version_changes()
    {
        var asOfUtc = new DateTime(2026, 4, 2, 9, 30, 0, DateTimeKind.Utc);
        var request = new GroundedContextRetrievalRequest(Guid.NewGuid(), Guid.NewGuid(), "finance payroll approvals", Guid.NewGuid(), AsOfUtc: asOfUtc);
        var decision = CreateDecision(request);

        var firstKey = _builder.BuildMemorySectionKey("tests-v1", request, decision, "finance payroll approvals", 5, asOfUtc);
        var secondKey = _builder.BuildMemorySectionKey("tests-v2", request, decision, "finance payroll approvals", 5, asOfUtc);

        Assert.NotEqual(firstKey, secondKey);
    }

    private static RetrievalAccessDecision CreateDecision(
        GroundedContextRetrievalRequest request,
        IReadOnlyList<string>? scopes = null)
    {
        var actorMembershipId = request.ActorUserId.HasValue ? Guid.NewGuid() : null;

        return new RetrievalAccessDecision(
            request.CompanyId,
            request.AgentId,
            request.ActorUserId,
            actorMembershipId,
            request.ActorUserId.HasValue ? CompanyMembershipRole.Manager : null,
            scopes ?? ["finance"],
            scopes ?? ["finance"],
            MembershipResolved: true,
            CanRetrieve: true);
    }
}
