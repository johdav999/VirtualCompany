using VirtualCompany.Application.Agents;

namespace VirtualCompany.Infrastructure.Companies;

public sealed record AgentHealthDerivationInput(
    int BlockedTaskCount,
    int FailedTaskCount,
    int ActiveTaskCount,
    DateTime? LastActivityUtc);

public static class AgentHealthSummaryCalculator
{
    private const int BusyTaskThreshold = 3;
    private static readonly TimeSpan BusyWindow = TimeSpan.FromHours(24);
    private static readonly TimeSpan InactiveThreshold = TimeSpan.FromDays(3);

    public static TimeSpan BusyActivityWindow => BusyWindow;

    public static AgentHealthSummaryDto Calculate(AgentHealthDerivationInput input, DateTime utcNow)
    {
        var lastActivityUtc = NormalizeUtc(input.LastActivityUtc);

        // ST-204 starts with a simple derived summary from task state and last activity.
        // This can evolve later without introducing persisted health state into the domain model.
        if (input.BlockedTaskCount > 0)
        {
            return new AgentHealthSummaryDto(
                "blocked",
                "Blocked",
                input.BlockedTaskCount == 1
                    ? "1 task is waiting on approval or unblock."
                    : $"{input.BlockedTaskCount} tasks are waiting on approval or unblock.");
        }

        if (input.FailedTaskCount > 0)
        {
            return new AgentHealthSummaryDto(
                "needs_attention",
                "Needs attention",
                input.FailedTaskCount == 1
                    ? "1 failed task needs review."
                    : $"{input.FailedTaskCount} failed tasks need review.");
        }

        if (lastActivityUtc is null)
        {
            return new AgentHealthSummaryDto("inactive", "Inactive", "No recent task activity has been recorded yet.");
        }

        if (utcNow - lastActivityUtc.Value >= InactiveThreshold)
        {
            return new AgentHealthSummaryDto(
                "inactive",
                "Inactive",
                $"No task activity in the last {(int)InactiveThreshold.TotalDays} days.");
        }

        if (input.ActiveTaskCount >= BusyTaskThreshold)
        {
            return new AgentHealthSummaryDto(
                "busy",
                "Busy",
                $"{input.ActiveTaskCount} recent tasks were handled in the last {(int)BusyWindow.TotalHours} hours.");
        }

        return new AgentHealthSummaryDto("healthy", "Healthy", "Recent task activity is within the expected range.");
    }

    public static string BuildSummaryText(AgentHealthSummaryDto summary) =>
        $"{summary.Label} - {summary.Reason}";

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
            _ => value.Value.ToUniversalTime()
        };
    }
}