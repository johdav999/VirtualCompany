using System.Collections.ObjectModel;
using System.Text.Json.Nodes;

namespace VirtualCompany.Application.Orchestration;


public sealed record CompanyGoalDto(
    Guid Id, Guid CompanyId, string Name, string Outcome, string Status, string Priority,
    string? MetricKey, string? MetricUnit, decimal? BaselineValue, decimal? TargetValue,
    DateTime StartUtc, DateTime TargetUtc, Guid? OwnerUserId, Guid? OwnerAgentId,
    IReadOnlyDictionary<string, JsonNode?> Constraints, int Version, DateTime CreatedUtc,
    DateTime UpdatedUtc, DateTime? CompletedUtc);
