using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using Xunit;

namespace VirtualCompany.SupportGrounding.Tests;

public sealed class SupportGroundingSafetyTests
{
    [Fact]
    public void Case_context_alone_is_not_trusted_grounding()
    {
        var context = new SupportKnowledgeContext(
            Guid.NewGuid(),
            [new SupportKnowledgeSourceReference("support_case", "Customer question", Guid.NewGuid(), "How does billing work?", 1m)],
            [],
            [],
            1m,
            "Case context only.");

        Assert.False(context.HasTrustedGrounding);
    }

    [Fact]
    public void Indexed_company_knowledge_is_trusted_grounding()
    {
        var context = new SupportKnowledgeContext(
            Guid.NewGuid(),
            [new SupportKnowledgeSourceReference("knowledge_chunk", "Billing policy", Guid.NewGuid(), "Invoices are issued monthly.", 0.82m, true, Guid.NewGuid(), "billing-policy.md#monthly")],
            [],
            [],
            0.82m,
            "Indexed company knowledge found.");

        Assert.True(context.HasTrustedGrounding);
    }

    [Fact]
    public void Reply_without_trusted_company_knowledge_requires_review()
    {
        var decision = SupportReplySafetyRules.Evaluate(
            SupportCaseCategories.GeneralQuestion,
            "The product includes finance tools.",
            "[{\"type\":\"support_case\",\"trusted\":false}]");

        Assert.Equal("review", decision.Decision);
        Assert.Contains("missing_grounding", decision.ReasonCodes);
    }

    [Fact]
    public void Reply_with_traceable_trusted_knowledge_passes_grounding_check()
    {
        var documentId = Guid.NewGuid();
        var decision = SupportReplySafetyRules.Evaluate(
            SupportCaseCategories.GeneralQuestion,
            "The product includes finance tools.",
            $"[{{\"type\":\"knowledge_chunk\",\"trusted\":true,\"documentId\":\"{documentId:D}\"}}]");

        Assert.Equal("allow", decision.Decision);
    }

    [Fact]
    public void Unsafe_financial_promise_is_blocked_even_with_a_source()
    {
        var documentId = Guid.NewGuid();
        var decision = SupportReplySafetyRules.Evaluate(
            SupportCaseCategories.Refund,
            "We guarantee that we will refund the payment today.",
            $"[{{\"type\":\"knowledge_chunk\",\"trusted\":true,\"documentId\":\"{documentId:D}\"}}]");

        Assert.Equal("block", decision.Decision);
        Assert.Contains("unsupported_financial_promise", decision.ReasonCodes);
    }
}
