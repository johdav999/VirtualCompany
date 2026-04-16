using System.Text.Json.Nodes;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Infrastructure.Companies;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class RequestedDomainClassifierTests
{
    private readonly RequestedDomainClassifier _classifier = new();

    [Fact]
    public void Actor_metadata_requested_domain_takes_precedence()
    {
        var classification = _classifier.Classify(
            new OrchestrationRequest(
                Guid.NewGuid(),
                ActorMetadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["requestedDomain"] = JsonValue.Create(" Legal.Contracts "),
                    ["domain"] = JsonValue.Create("finance.payments")
                }),
            Task("finance_execution", ("requestedDomain", JsonValue.Create("support.tickets"))));

        Assert.Equal("legal.contracts", classification.RequestedDomain);
        Assert.Equal(RequestedDomainClassifierRules.ActorMetadataRequestedDomain, classification.MatchedClassifierRule);
    }

    [Fact]
    public void Actor_metadata_domain_is_used_before_task_input()
    {
        var classification = _classifier.Classify(
            new OrchestrationRequest(
                Guid.NewGuid(),
                ActorMetadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["domain"] = JsonValue.Create("support.tickets")
                }),
            Task("finance_execution", ("requestedDomain", JsonValue.Create("finance.payments"))));

        Assert.Equal("support.tickets", classification.RequestedDomain);
        Assert.Equal(RequestedDomainClassifierRules.ActorMetadataDomain, classification.MatchedClassifierRule);
    }

    [Fact]
    public void Task_input_requested_domain_is_used_when_actor_metadata_is_absent()
    {
        var classification = _classifier.Classify(
            new OrchestrationRequest(Guid.NewGuid()),
            Task("finance_execution", ("requestedDomain", JsonValue.Create("finance.payments"))));

        Assert.Equal("finance.payments", classification.RequestedDomain);
        Assert.Equal(RequestedDomainClassifierRules.TaskInputRequestedDomain, classification.MatchedClassifierRule);
    }

    [Fact]
    public void Task_type_is_deterministic_fallback()
    {
        var classification = _classifier.Classify(
            new OrchestrationRequest(Guid.NewGuid()),
            Task("finance_execution"));

        Assert.Equal("finance_execution", classification.RequestedDomain);
        Assert.Equal(RequestedDomainClassifierRules.TaskTypeFallback, classification.MatchedClassifierRule);
    }

    private static TaskDetailDto Task(string type, params (string Key, JsonNode? Value)[] input) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            type,
            "Title",
            "Description",
            "normal",
            "open",
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            Input(input),
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase),
            null,
            null,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow);

    private static Dictionary<string, JsonNode?> Input(params (string Key, JsonNode? Value)[] input)
    {
        var payload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in input)
        {
            payload[key] = value?.DeepClone();
        }

        return payload;
    }
}