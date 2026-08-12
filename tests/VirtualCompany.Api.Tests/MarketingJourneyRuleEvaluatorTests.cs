using VirtualCompany.Application.Marketing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Sales;

namespace VirtualCompany.Api.Tests;

public sealed class MarketingJourneyRuleEvaluatorTests
{
    private readonly MarketingJourneyRuleEvaluator _sut = new();

    [Fact]
    public void Evaluator_enforces_allowlisted_eligibility_entry_exit_and_goal_rules()
    {
        var eligibility = "{\"all\":[{\"field\":\"status\",\"operator\":\"equals\",\"value\":\"active\"},{\"field\":\"has_email\",\"operator\":\"equals\",\"value\":true}]}";
        var transitions = "{\"entry\":{\"field\":\"has_customer_company\",\"operator\":\"equals\",\"value\":true},\"exit\":{\"field\":\"event_type\",\"operator\":\"equals\",\"value\":\"unsubscribe\"},\"goal\":{\"field\":\"event_type\",\"operator\":\"equals\",\"value\":\"converted\"}}";
        var facts = new MarketingJourneyContactFacts(Guid.NewGuid(), "active", true, true, "en",
            DateTime.UtcNow.AddDays(-5), new HashSet<string>(), new HashSet<Guid>());

        var eligible = _sut.Evaluate(eligibility, transitions, facts, "enrollment");
        var exited = _sut.Evaluate(eligibility, transitions, facts with
        { EventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "unsubscribe" } }, "step");
        var converted = _sut.Evaluate(eligibility, transitions, facts with
        { EventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "converted" } }, "step");

        Assert.True(eligible.Allowed);
        Assert.True(exited.ShouldExit); Assert.False(exited.Allowed);
        Assert.True(converted.GoalReached); Assert.False(converted.Allowed);
    }

    [Fact]
    public void Validator_rejects_arbitrary_fields_and_operators()
    {
        var result = _sut.Validate("{\"field\":\"race\",\"operator\":\"execute\",\"value\":\"x\"}",
            "{}", "{}", 1);

        Assert.False(result.Valid);
        Assert.Contains(result.Errors, x => x.Contains("field", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Errors, x => x.Contains("operator", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Enrollment_lease_is_exclusive_and_dead_letters_after_bounded_attempts()
    {
        var enrollment = new MarketingJourneyEnrollment(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 1, "consent:1", "enrollment:1", DateTime.UtcNow);
        var now = DateTime.UtcNow;
        enrollment.Claim("worker-a", TimeSpan.FromMinutes(1), now);
        Assert.Throws<InvalidOperationException>(() => enrollment.Claim("worker-b", TimeSpan.FromMinutes(1), now));

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            enrollment.Retry("worker-a", "temporary_failure", now);
            if (attempt < 5) enrollment.Claim("worker-a", TimeSpan.FromMinutes(1), now);
        }
        Assert.Equal("dead_letter", enrollment.Status);
    }
}
