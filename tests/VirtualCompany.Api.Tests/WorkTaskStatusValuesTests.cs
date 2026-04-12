using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class WorkTaskStatusValuesTests
{
    public static TheoryData<string, WorkTaskStatus> CanonicalStatuses => new()
    {
        { "new", WorkTaskStatus.New },
        { "in_progress", WorkTaskStatus.InProgress },
        { "blocked", WorkTaskStatus.Blocked },
        { "awaiting_approval", WorkTaskStatus.AwaitingApproval },
        { "completed", WorkTaskStatus.Completed },
        { "failed", WorkTaskStatus.Failed }
    };

    [Theory]
    [MemberData(nameof(CanonicalStatuses))]
    public void Parse_accepts_canonical_task_status_values(string value, WorkTaskStatus expectedStatus)
    {
        Assert.True(WorkTaskStatusValues.TryParse(value, out var parsedStatus));
        Assert.Equal(expectedStatus, parsedStatus);
        Assert.Equal(value, parsedStatus.ToStorageValue());
    }

    [Fact]
    public void Allowed_values_are_exactly_canonical_task_status_values()
    {
        Assert.Equal(
            ["new", "in_progress", "blocked", "awaiting_approval", "completed", "failed"],
            WorkTaskStatusValues.AllowedValues);
    }

    [Theory]
    [InlineData("InProgress")]
    [InlineData("pending")]
    [InlineData("done")]
    [InlineData("")]
    public void TryParse_rejects_noncanonical_task_status_values(string value)
    {
        Assert.False(WorkTaskStatusValues.TryParse(value, out _));
    }

    [Fact]
    public void New_task_defaults_to_new_status()
    {
        var task = new WorkTask(Guid.NewGuid(), Guid.NewGuid(), "orchestration", "Check launch readiness", null, WorkTaskPriority.Normal, null, null, "human", Guid.NewGuid());

        Assert.Equal(WorkTaskStatus.New, task.Status);
        Assert.Null(task.CompletedUtc);
    }

    [Fact]
    public void Completed_status_sets_completed_timestamp_and_noncompleted_status_clears_it()
    {
        var task = new WorkTask(Guid.NewGuid(), Guid.NewGuid(), "orchestration", "Check launch readiness", null, WorkTaskPriority.Normal, null, null, "human", Guid.NewGuid());

        task.UpdateStatus(WorkTaskStatus.Completed);
        Assert.NotNull(task.CompletedUtc);

        task.UpdateStatus(WorkTaskStatus.Blocked);
        Assert.Null(task.CompletedUtc);
    }
}