using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingDomainTests
{
    private static readonly DateTime Start = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddMonths(1);

    [Fact]
    public void Content_requires_submission_before_human_review()
    {
        var brief = CreateBrief();

        Assert.Throws<InvalidOperationException>(() => brief.Review(true));

        brief.Submit();
        Assert.Equal(MarketingStatuses.Submitted, brief.Status);

        brief.Review(true);
        Assert.Equal(MarketingStatuses.Approved, brief.Status);
        Assert.Throws<InvalidOperationException>(() => brief.Review(false));
    }

    [Fact]
    public void Experiment_follows_draft_active_completed_lifecycle()
    {
        var experiment = new MarketingExperiment(
            Guid.NewGuid(), Guid.NewGuid(), "Email subject test", "A shorter subject increases opens.",
            "open_rate", "unsubscribe_rate", 100, Start, End, null);

        Assert.Throws<InvalidOperationException>(() => experiment.Complete("Too early."));

        experiment.Activate();
        Assert.Equal(MarketingStatuses.Active, experiment.Status);
        Assert.Throws<InvalidOperationException>(() => experiment.Activate());

        experiment.Complete("Variant B improved opens without increasing unsubscribes.");
        Assert.Equal(MarketingStatuses.Completed, experiment.Status);
        Assert.NotNull(experiment.Decision);
    }

    [Fact]
    public void Sales_handoff_requires_target_and_can_only_be_decided_once()
    {
        var companyId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new MarketingSalesHandoff(
            Guid.NewGuid(), companyId, null, null, null, "Qualified engagement", "Call this week",
            "high", End, "observation:1", "handoff:1"));

        var handoff = new MarketingSalesHandoff(
            Guid.NewGuid(), companyId, null, Guid.NewGuid(), null, "Qualified engagement", "Call this week",
            "high", End, "observation:1", "handoff:1");
        handoff.Decide(true, "Accepted by Sales.", Guid.NewGuid(), null);

        Assert.Equal(MarketingStatuses.Accepted, handoff.Status);
        Assert.Throws<InvalidOperationException>(() => handoff.Decide(false, "Duplicate decision.", null, null));
    }

    [Fact]
    public void Objective_and_plan_reject_invalid_periods()
    {
        Assert.Throws<ArgumentException>(() => new MarketingObjective(
            Guid.NewGuid(), Guid.NewGuid(), "Qualified demand", "qualified_demand", 10, "leads", End, Start));
        Assert.Throws<ArgumentException>(() => new MarketingPlan(
            Guid.NewGuid(), Guid.NewGuid(), "Q3 plan", "Plan summary", End, Start, 1000, "SEK"));
    }

    [Fact]
    public void Qualification_definition_is_versioned_and_rejects_unsupported_audiences()
    {
        var companyId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new MarketingQualificationDefinition(
            Guid.NewGuid(), companyId, "Invalid", "public_sector", "email", 70, 30, true,
            Start, End, "{}", "{}", ownerId));

        var definition = new MarketingQualificationDefinition(
            Guid.NewGuid(), companyId, "B2B engaged demand", "b2b", "email", 70, 30, true,
            Start, End, "{}", "{}", ownerId);

        definition.Activate();

        Assert.Equal(MarketingStatuses.Active, definition.Status);
        Assert.Equal(2, definition.Version);
        Assert.Throws<InvalidOperationException>(definition.Activate);
    }

    [Fact]
    public void Qualification_evaluation_rejects_invalid_scores_and_statuses()
    {
        var companyId = Guid.NewGuid();
        var definitionId = Guid.NewGuid();
        var contactId = Guid.NewGuid();

        Assert.Throws<ArgumentOutOfRangeException>(() => new MarketingQualificationEvaluation(
            Guid.NewGuid(), companyId, definitionId, 1, contactId, 101,
            MarketingQualificationStatuses.Qualified, "[]", "[]", Start, "eval:1"));
        Assert.Throws<ArgumentException>(() => new MarketingQualificationEvaluation(
            Guid.NewGuid(), companyId, definitionId, 1, contactId, 70,
            "unknown", "[]", "[]", Start, "eval:2"));
    }

    [Fact]
    public void Event_trigger_links_durable_work_and_has_explicit_resolution_lifecycle()
    {
        var trigger = new MarketingEventTrigger(Guid.NewGuid(), Guid.NewGuid(), "provider_failure",
            "channel_action", Guid.NewGuid().ToString("N"), 2, "warning", "{\"failureCode\":\"quota\"}",
            "event:provider_failure:channel_action:1:v2:20260812", "marketing-event-test");
        var taskId = Guid.NewGuid();
        var runId = Guid.NewGuid();

        trigger.LinkTask(taskId);
        trigger.LinkRun(runId);

        Assert.Equal(taskId, trigger.RelatedTaskId);
        Assert.Equal(runId, trigger.OperatingRunId);
        Assert.Equal("processed", trigger.Status);

        trigger.Resolve();
        Assert.Equal("resolved", trigger.Status);
    }

    [Fact]
    public void Corrected_observation_retains_lineage_and_supersedes_prior_value()
    {
        var companyId = Guid.NewGuid();
        var original = new MarketingChannelObservation(Guid.NewGuid(), companyId, "linkedin", "clicks", 10,
            "count", Start, End, null, null, "provider:row:1", "observation:1");
        var correction = new MarketingChannelObservation(Guid.NewGuid(), companyId, "linkedin", "clicks", 12,
            "count", Start, End, null, null, "provider:row:1:corrected", "observation:2", original.Id);

        original.Supersede();

        Assert.True(original.IsSuperseded);
        Assert.Equal(original.Id, correction.CorrectionOfObservationId);
        Assert.False(correction.IsSuperseded);
    }

    [Fact]
    public async Task Marketing_queries_are_isolated_by_company()
    {
        using var factory = new TestWebApplicationFactory();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        await factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.AddRange(
                new Company(companyId, "Marketing tenant"),
                new Company(otherCompanyId, "Other marketing tenant"));
            dbContext.MarketingObjectives.AddRange(
                new MarketingObjective(Guid.NewGuid(), companyId, "Our objective", "qualified_demand", 10, "leads", Start, End),
                new MarketingObjective(Guid.NewGuid(), otherCompanyId, "Other objective", "qualified_demand", 20, "leads", Start, End));
            return Task.CompletedTask;
        });

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICompanyContextAccessor>().SetCompanyId(companyId);
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        var objectives = await dbContext.MarketingObjectives.AsNoTracking().ToListAsync();

        var objective = Assert.Single(objectives);
        Assert.Equal(companyId, objective.CompanyId);
        Assert.Equal("Our objective", objective.Name);
    }

    private static MarketingContentBrief CreateBrief() => new(
        Guid.NewGuid(), Guid.NewGuid(), "Launch email", "Explain the offer", "SME owners",
        "email", "en", "Clear", "Book a demo", null, null, End, Guid.NewGuid(), null);
}
