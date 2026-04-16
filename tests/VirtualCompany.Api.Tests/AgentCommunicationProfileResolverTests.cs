using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Agents;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class AgentCommunicationProfileResolverTests
{
    [Fact]
    public void Resolve_uses_explicit_profile_and_default_fills_missing_fields()
    {
        var logger = new CapturingLogger<AgentCommunicationProfileResolver>();
        var resolver = new AgentCommunicationProfileResolver(new DefaultAgentCommunicationProfileProvider(), logger);
        var profile = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tone"] = JsonValue.Create("direct and calm"),
            ["communicationRules"] = new JsonArray(JsonValue.Create("Name risks before recommendations."))
        };

        var result = resolver.Resolve(profile, CreateContext());

        Assert.False(result.IsFallback);
        Assert.Equal(AgentCommunicationProfileSources.Explicit, result.ProfileSource);
        Assert.Equal("direct and calm", result.Tone);
        Assert.Equal("reliable business assistant", result.Persona);
        Assert.Contains("Name risks before recommendations.", result.CommunicationRules);
        Assert.Contains("hostile", result.ForbiddenToneRules);
        Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Information);
    }

    [Fact]
    public void Resolve_uses_fallback_profile_and_logs_when_explicit_profile_is_missing()
    {
        var logger = new CapturingLogger<AgentCommunicationProfileResolver>();
        var resolver = new AgentCommunicationProfileResolver(new DefaultAgentCommunicationProfileProvider(), logger);

        var result = resolver.Resolve([], CreateContext());

        Assert.True(result.IsFallback);
        Assert.Equal(AgentCommunicationProfileSources.Fallback, result.ProfileSource);
        Assert.Equal("professional, clear, helpful", result.Tone);
        Assert.Contains("Use business-appropriate language.", result.CommunicationRules);
        var entry = Assert.Single(logger.Entries, item => item.Level == LogLevel.Information);
        Assert.Contains("Applied fallback agent communication profile", entry.Message);
        Assert.Contains("chat", entry.Message);
    }

    [Fact]
    public void Resolve_reads_updated_profile_values_on_subsequent_calls()
    {
        var logger = new CapturingLogger<AgentCommunicationProfileResolver>();
        var resolver = new AgentCommunicationProfileResolver(new DefaultAgentCommunicationProfileProvider(), logger);
        var first = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tone"] = JsonValue.Create("formal"),
            ["persona"] = JsonValue.Create("controller")
        };
        var second = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["tone"] = JsonValue.Create("plainspoken"),
            ["persona"] = JsonValue.Create("operator")
        };

        var firstResult = resolver.Resolve(first, CreateContext());
        var secondResult = resolver.Resolve(second, CreateContext());

        Assert.Equal("formal", firstResult.Tone);
        Assert.Equal("controller", firstResult.Persona);
        Assert.Equal("plainspoken", secondResult.Tone);
        Assert.Equal("operator", secondResult.Persona);
    }

    private static CommunicationProfileResolutionContext CreateContext() =>
        new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            PromptGenerationPathValues.Chat,
            "profile-test");

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message);

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}