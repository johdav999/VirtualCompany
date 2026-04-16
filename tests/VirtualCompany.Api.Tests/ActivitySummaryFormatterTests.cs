using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VirtualCompany.Application.Activity;
using VirtualCompany.Domain.Events;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class ActivitySummaryFormatterTests
{
    private readonly DefaultActivityEventSummaryFormatter _formatter = new();

    private const string FixtureFileName = "supported-event-summaries.json";
    private static readonly JsonSerializerOptions FixtureJsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [Theory]
    [MemberData(nameof(SupportedEventFixtures))]
    public void Supported_event_types_return_deterministic_normalized_summaries(
        ActivitySummaryFixture fixture)
    {
        var result = _formatter.Format(
            fixture.EventType,
            fixture.Status,
            fixture.PersistedSummary,
            fixture.RawPayload);

        Assert.Contains(fixture.EventType, SupportedPlatformEventTypeRegistry.Instance.SupportedEventTypes);
        Assert.Equal(fixture.EventType, result.EventType);
        Assert.Equal(fixture.ExpectedSummary.FormatterKey, result.FormatterKey);
        Assert.Equal(fixture.ExpectedSummary.Actor, result.Actor);
        Assert.Equal(fixture.ExpectedSummary.Action, result.Action);
        Assert.Equal(fixture.ExpectedSummary.Target, result.Target);
        Assert.Equal(fixture.ExpectedSummary.Outcome, result.Outcome);
        Assert.Equal(fixture.ExpectedSummary.SummaryText, result.SummaryText);
        Assert.Equal(fixture.ExpectedSummary.SummaryText, result.Text);
        Assert.DoesNotContain("null", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("undefined", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("  ", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Fixture_inventory_covers_at_least_20_real_supported_event_types()
    {
        var fixtures = LoadSupportedEventFixtures();
        var eventTypes = fixtures.Select(x => x.EventType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.True(eventTypes.Length >= 20, $"Expected at least 20 supported event type fixtures, found {eventTypes.Length}.");
        Assert.All(eventTypes, eventType => Assert.Contains(eventType, SupportedPlatformEventTypeRegistry.Instance.SupportedEventTypes));
    }

    [Fact]
    public void Missing_actor_is_omitted_without_dangling_text()
    {
        var result = _formatter.Format(
            SupportedPlatformEventTypeRegistry.TaskCompleted,
            "completed",
            null,
            Payload(("taskTitle", "Invoice Review")));

        Assert.Null(result.Actor);
        Assert.Equal("completed task Invoice Review", result.Text);
    }

    [Fact]
    public void Missing_target_is_omitted_without_empty_parentheses_or_separators()
    {
        var result = _formatter.Format(
            SupportedPlatformEventTypeRegistry.ApprovalApproved,
            "approved",
            null,
            Payload(("actor", "Alice")));

        Assert.Null(result.Target);
        Assert.Equal("Alice approved", result.Text);
        Assert.DoesNotContain("()", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_outcome_uses_status_without_leaking_null_payload_values()
    {
        var result = _formatter.Format(
            SupportedPlatformEventTypeRegistry.DocumentProcessed,
            "processed",
            null,
            Payload(("actor", "Alice"), ("documentName", "Runbook.pdf"), ("outcome", null)));

        Assert.Equal("processed", result.Outcome);
        Assert.Equal("Alice processed document Runbook.pdf", result.Text);
    }

    [Fact]
    public void Unknown_event_type_uses_safe_fallback_formatter()
    {
        var result = _formatter.Format(
            "vendor_custom_event",
            "succeeded",
            null,
            Payload(("actor", "Alice"), ("targetName", "Integration job")));

        Assert.Equal("vendor_custom_event", result.EventType);
        Assert.Equal("fallback", result.FormatterKey);
        Assert.Equal("recorded vendor custom event", result.Action);
        Assert.Equal("Alice recorded vendor custom event Integration job", result.Text);
    }

    [Fact]
    public void Malformed_payload_values_do_not_break_formatting()
    {
        var result = _formatter.Format(
            SupportedPlatformEventTypeRegistry.TaskUpdated,
            "updated",
            "Legacy summary",
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["actor"] = new JsonObject { ["displayName"] = "Alice" },
                ["taskTitle"] = new JsonArray(JsonValue.Create("unexpected")),
                ["outcome"] = JsonValue.Create("updated")
            });

        Assert.Equal("Alice updated task [\"unexpected\"]", result.Text);
        Assert.DoesNotContain("undefined", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Formatter_keeps_representative_feed_page_generation_under_300_ms_median()
    {
        var durations = new List<double>();
        var payloads = Enumerable.Range(0, 200)
            .Select(i => Payload(("actor", $"Agent {i}"), ("taskTitle", $"Task {i}"), ("outcome", "completed")))
            .ToArray();

        for (var sample = 0; sample < 9; sample++)
        {
            var stopwatch = Stopwatch.StartNew();
            foreach (var payload in payloads)
            {
                _formatter.Format(SupportedPlatformEventTypeRegistry.TaskCompleted, "completed", null, payload);
            }

            stopwatch.Stop();
            durations.Add(stopwatch.Elapsed.TotalMilliseconds);
        }

        var median = durations.OrderBy(x => x).ElementAt(durations.Count / 2);
        Assert.True(median < 300, $"Median formatter time for a 200-item feed page was {median} ms.");
    }

    public static IEnumerable<object[]> SupportedEventFixtures() =>
        LoadSupportedEventFixtures().Select(fixture => new object[] { fixture });

    private static IReadOnlyList<ActivitySummaryFixture> LoadSupportedEventFixtures()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "ActivitySummaries", FixtureFileName);
        var json = File.ReadAllText(path);
        var fixtures = JsonSerializer.Deserialize<IReadOnlyList<ActivitySummaryFixture>>(json, FixtureJsonOptions);

        if (fixtures is null || fixtures.Count == 0)
        {
            throw new InvalidOperationException($"No activity summary fixtures were loaded from {path}.");
        }

        return fixtures;
    }

    private static Dictionary<string, JsonNode?> Payload(params (string Key, string? Value)[] values) =>
        values.ToDictionary(
            x => x.Key,
            x => x.Value is null ? null : JsonValue.Create(x.Value),
            StringComparer.OrdinalIgnoreCase);

    private sealed record ActivitySummaryFixture(
        string EventType,
        string Status,
        string? PersistedSummary,
        Dictionary<string, JsonNode?> RawPayload,
        ExpectedActivitySummary ExpectedSummary)
    {
        public override string ToString() => EventType;
    }

    private sealed record ExpectedActivitySummary(
        string FormatterKey,
        string? Actor,
        string Action,
        string? Target,
        string? Outcome,
        string SummaryText);
}
