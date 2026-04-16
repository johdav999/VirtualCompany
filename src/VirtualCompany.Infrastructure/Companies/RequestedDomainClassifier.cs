using System.Text.Json.Nodes;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class RequestedDomainClassifier : IRequestedDomainClassifier
{
    public RequestedDomainClassification Classify(OrchestrationRequest request, TaskDetailDto task)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(task);

        var explicitDomain = ReadString(request.ActorMetadata, "requestedDomain");
        if (!string.IsNullOrWhiteSpace(explicitDomain))
        {
            return Classification(
                explicitDomain,
                RequestedDomainClassifierRules.ActorMetadataRequestedDomain,
                "Requested domain was supplied by actor metadata.");
        }

        explicitDomain = ReadString(request.ActorMetadata, "domain");
        if (!string.IsNullOrWhiteSpace(explicitDomain))
        {
            return Classification(
                explicitDomain,
                RequestedDomainClassifierRules.ActorMetadataDomain,
                "Domain was supplied by actor metadata.");
        }

        explicitDomain = ReadString(task.InputPayload, "requestedDomain");
        if (!string.IsNullOrWhiteSpace(explicitDomain))
        {
            return Classification(
                explicitDomain,
                RequestedDomainClassifierRules.TaskInputRequestedDomain,
                "Requested domain was supplied by task input.");
        }

        explicitDomain = ReadString(task.InputPayload, "domain");
        if (!string.IsNullOrWhiteSpace(explicitDomain))
        {
            return Classification(
                explicitDomain,
                RequestedDomainClassifierRules.TaskInputDomain,
                "Domain was supplied by task input.");
        }

        return Classification(
            task.Type,
            RequestedDomainClassifierRules.TaskTypeFallback,
            "No explicit requested domain was supplied; task type was used.");
    }

    private static RequestedDomainClassification Classification(string? domain, string rule, string reason) =>
        new(
            ResponsibilityDomainRules.NormalizeRequestedDomain(domain, "unknown"),
            rule,
            reason);

    private static string? ReadString(IReadOnlyDictionary<string, JsonNode?>? values, string key) =>
        values is not null &&
        values.TryGetValue(key, out var node) &&
        node is JsonValue value &&
        value.TryGetValue<string>(out var text) &&
        !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;
}
