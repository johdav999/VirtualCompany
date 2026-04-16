using System.Web;
using VirtualCompany.Web.Services;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ActivityFeedFilterStateTests
{
    [Fact]
    public void Query_string_round_trip_preserves_filter_state()
    {
        var from = new DateTime(2026, 4, 15, 8, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["agentId"] = " 71f7f407-a6d1-49bb-bf56-7890e8453102 ";
        query["department"] = "Finance";
        query["task"] = "560bc97c-372f-4b48-a56a-6d79aa3d3b79";
        query["eventType"] = "task_completed";
        query["status"] = "completed";
        query["timeframe"] = "custom";
        query["from"] = from.ToString("O");
        query["to"] = to.ToString("O");

        var state = ActivityFeedFilterState.FromQuery(query);
        var parameters = state.ToQueryParameters(Guid.Parse("6b4558ee-086a-4bbf-b539-2e26ec867be2"));

        Assert.Equal("71f7f407-a6d1-49bb-bf56-7890e8453102", state.AgentId);
        Assert.Equal("Finance", state.Department);
        Assert.Equal("560bc97c-372f-4b48-a56a-6d79aa3d3b79", state.TaskId);
        Assert.Equal("task_completed", state.EventType);
        Assert.Equal("completed", state.Status);
        Assert.Equal(ActivityFeedFilterState.Custom, state.Timeframe);
        Assert.Equal(from, state.FromUtc);
        Assert.Equal(to, state.ToUtc);
        Assert.Equal("560bc97c-372f-4b48-a56a-6d79aa3d3b79", parameters["task"]);
        Assert.Null(parameters["taskId"]);
        Assert.Equal(ActivityFeedFilterState.Custom, parameters["timeframe"]);
        Assert.Equal(from, parameters["from"]);
        Assert.Equal(to, parameters["to"]);
    }

    [Fact]
    public void Applying_filters_serializes_shareable_query_string_values()
    {
        var companyId = Guid.Parse("6b4558ee-086a-4bbf-b539-2e26ec867be2");
        var agentId = "71f7f407-a6d1-49bb-bf56-7890e8453102";
        var taskId = "560bc97c-372f-4b48-a56a-6d79aa3d3b79";
        var from = new DateTime(2026, 4, 15, 8, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc);
        var state = new ActivityFeedFilterState
        {
            AgentId = $" {agentId} ",
            Department = " Finance ",
            TaskId = $" {taskId} ",
            EventType = " task_completed ",
            Status = " completed ",
            Timeframe = ActivityFeedFilterState.Custom,
            FromUtc = from,
            ToUtc = to
        };

        var parameters = state.ToQueryParameters(companyId);

        Assert.Equal(companyId, parameters["companyId"]);
        Assert.Equal(agentId, parameters["agentId"]);
        Assert.Equal("Finance", parameters["department"]);
        Assert.Equal(taskId, parameters["task"]);
        Assert.Null(parameters["taskId"]);
        Assert.Equal("task_completed", parameters["eventType"]);
        Assert.Equal("completed", parameters["status"]);
        Assert.Equal(ActivityFeedFilterState.Custom, parameters["timeframe"]);
        Assert.Equal(from, parameters["from"]);
        Assert.Equal(to, parameters["to"]);
    }

    [Fact]
    public void Reload_from_query_string_restores_current_filtered_view_state()
    {
        var query = HttpUtility.ParseQueryString("agentId=71f7f407-a6d1-49bb-bf56-7890e8453102&department=Finance&task=560bc97c-372f-4b48-a56a-6d79aa3d3b79&eventType=task_completed&status=completed&timeframe=custom&from=2026-04-15T08%3A00%3A00.0000000Z&to=2026-04-15T12%3A00%3A00.0000000Z");

        var state = ActivityFeedFilterState.FromQuery(query);
        var apiFilter = state.ToApiFilter();

        Assert.Equal("71f7f407-a6d1-49bb-bf56-7890e8453102", apiFilter.AgentId);
        Assert.Equal("Finance", apiFilter.Department);
        Assert.Equal("560bc97c-372f-4b48-a56a-6d79aa3d3b79", apiFilter.TaskId);
        Assert.Equal("task_completed", apiFilter.EventType);
        Assert.Equal("completed", apiFilter.Status);
        Assert.Equal(ActivityFeedFilterState.Custom, apiFilter.Timeframe);
        Assert.Equal(new DateTime(2026, 4, 15, 8, 0, 0, DateTimeKind.Utc), apiFilter.FromUtc);
        Assert.Equal(new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc), apiFilter.ToUtc);
    }

    [Fact]
    public void Clear_filters_omits_default_query_values()
    {
        var state = new ActivityFeedFilterState
        {
            AgentId = " ",
            Department = null,
            TaskId = "",
            EventType = null,
            Status = null,
            Timeframe = ActivityFeedFilterState.AllTime,
            FromUtc = new DateTime(2026, 4, 15, 8, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc)
        };

        var parameters = state.ToQueryParameters(Guid.Parse("6b4558ee-086a-4bbf-b539-2e26ec867be2"));

        Assert.Null(parameters["agentId"]);
        Assert.Null(parameters["department"]);
        Assert.Null(parameters["task"]);
        Assert.Null(parameters["eventType"]);
        Assert.Null(parameters["status"]);
        Assert.Null(parameters["timeframe"]);
        Assert.Null(parameters["from"]);
        Assert.Null(parameters["to"]);
    }

    [Fact]
    public void Preset_timeframes_do_not_serialize_custom_dates()
    {
        var state = new ActivityFeedFilterState
        {
            Timeframe = "last-24-hours",
            FromUtc = new DateTime(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 4, 15, 12, 0, 0, DateTimeKind.Utc)
        };

        var apiFilter = state.ToApiFilter();
        var parameters = state.ToQueryParameters(Guid.Parse("6b4558ee-086a-4bbf-b539-2e26ec867be2"));

        Assert.Equal(ActivityFeedFilterState.Last24Hours, apiFilter.Timeframe);
        Assert.Null(apiFilter.FromUtc);
        Assert.Null(apiFilter.ToUtc);
        Assert.Equal(ActivityFeedFilterState.Last24Hours, parameters["timeframe"]);
        Assert.Null(parameters["from"]);
        Assert.Null(parameters["to"]);
    }

    [Fact]
    public void Query_string_accepts_stable_task_alias()
    {
        var taskId = "560bc97c-372f-4b48-a56a-6d79aa3d3b79";
        var query = HttpUtility.ParseQueryString(string.Empty);
        query["task"] = taskId;

        var state = ActivityFeedFilterState.FromQuery(query);
        var parameters = state.ToQueryParameters(Guid.Parse("6b4558ee-086a-4bbf-b539-2e26ec867be2"));

        Assert.Equal(taskId, state.TaskId);
        Assert.Equal(taskId, parameters["task"]);
    }
}
