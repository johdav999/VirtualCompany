using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Support;

namespace VirtualCompany.Api.Tests;

public sealed class SupportAgentDecisionServiceTests
{
    [Fact]
    public async Task Queue_makes_security_signal_a_mandatory_critical_escalation()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 16, 9, 0, 0, DateTimeKind.Utc);
        var supportCase = new SupportCase(Guid.NewGuid(), companyId, "SUP-1",
            "Possible security breach", "Customer reports that an account may have been hacked.", "email",
            contactId: Guid.NewGuid(), createdUtc: now.AddHours(-2));
        supportCase.SetSla(now.AddHours(1), now.AddHours(8));
        db.SupportCases.Add(supportCase);
        await db.SaveChangesAsync();

        var result = await CreateService(db).AnalyzeQueueAsync(companyId, Guid.NewGuid(), null,
            new SupportQueueAnalysisRequest(AsOfUtc: now), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.True(item.MandatoryEscalation);
        Assert.Equal("critical", item.PriorityBand);
        Assert.Contains("mandatory_escalation", item.ReasonCodes);
        Assert.True(item.RequiresReview);
    }

    [Fact]
    public async Task Recurring_issue_groups_only_same_company_and_emits_no_raw_subject_text()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 16, 9, 0, 0, DateTimeKind.Utc);
        var first = Case(companyId, "Secret customer A detail", now.AddDays(-3));
        var second = Case(companyId, "Secret customer B detail", now.AddDays(-2));
        var other = Case(otherCompanyId, "Other tenant secret", now.AddDays(-1));
        db.SupportCases.AddRange(first, second, other);
        await db.SaveChangesAsync();

        var result = await CreateService(db).AnalyzeRecurringIssuesAsync(companyId, Guid.NewGuid(), null,
            new SupportRecurringIssueRequest(AsOfUtc: now), CancellationToken.None);

        var cluster = Assert.Single(result.Clusters);
        Assert.Equal(2, cluster.CaseCount);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), cluster.CaseIds.Order());
        var evidenceText = string.Join(' ', cluster.SharedConfirmedFacts.Concat(cluster.Differences).Concat(cluster.RootCauseHypotheses));
        Assert.DoesNotContain("Secret customer", evidenceText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(other.Id, cluster.CaseIds);
        Assert.True(cluster.RequiresReview);
    }

    [Fact]
    public async Task Answerability_blocks_security_case_even_with_trusted_grounding()
    {
        await using var db = CreateDb();
        var companyId = Guid.NewGuid();
        var supportCase = new SupportCase(Guid.NewGuid(), companyId, "SUP-SEC",
            "Account security incident", "Customer reports unauthorized account access.", "email",
            contactId: Guid.NewGuid(), createdUtc: DateTime.UtcNow.AddHours(-1));
        db.SupportCases.Add(supportCase);
        await db.SaveChangesAsync();
        var context = new SupportKnowledgeContext(supportCase.Id,
            [new SupportKnowledgeSourceReference("knowledge_chunk", "Approved security response policy",
                Guid.NewGuid(), "Escalate immediately.", .95m, true)], [], [], .95m, "Trusted policy found.");

        var result = await CreateService(db, new StubKnowledge(context)).AnalyzeAnswerabilityAsync(
            companyId, Guid.NewGuid(), null, new SupportAnswerabilityRequest(supportCase.Id), CancellationToken.None);

        Assert.False(result.CanDraft);
        Assert.True(result.RequiresReview);
        Assert.Equal("partially_answerable", result.State);
        Assert.Equal(.95m, result.Score);
        Assert.NotEmpty(result.TrustedSourceIds);
    }

    private static SupportCase Case(Guid companyId, string subject, DateTime createdUtc)
    {
        var value = new SupportCase(Guid.NewGuid(), companyId, $"SUP-{Guid.NewGuid():N}"[..12], subject,
            "Private customer message", "email", contactId: Guid.NewGuid(), createdUtc: createdUtc);
        value.SetCategory(SupportCaseCategories.TechnicalIssue);
        return value;
    }

    private static SupportAgentDecisionService CreateService(VirtualCompanyDbContext db,
        ISupportKnowledgeContextProvider? knowledge = null) =>
        new(db, new StubAnalysis(), knowledge!, null!);

    private static VirtualCompanyDbContext CreateDb() => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options);

    private sealed class StubAnalysis : ISupportAgentAnalysisService
    {
        public Task<RoleAgentAnalysisResult> AnalyzeAsync(Guid companyId, Guid agentId, Guid? actorUserId,
            RoleAgentAnalysisRequest request, CancellationToken cancellationToken) => Task.FromResult(
            new RoleAgentAnalysisResult(Guid.Parse("00000000-0000-0000-0000-000000000002"), "test",
                AgentAiRunStatuses.Completed, "Test advice", .8m, request.AsOfUtc ?? DateTime.UtcNow,
                [], [], [], [], [], [], false));
    }

    private sealed class StubKnowledge(SupportKnowledgeContext context) : ISupportKnowledgeContextProvider
    {
        public Task<SupportKnowledgeContext> RetrieveAsync(Guid companyId, Guid supportCaseId,
            CancellationToken cancellationToken) => Task.FromResult(context);
    }
}
