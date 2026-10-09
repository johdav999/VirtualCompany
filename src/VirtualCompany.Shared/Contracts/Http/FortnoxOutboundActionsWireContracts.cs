using System.Text.Json.Nodes;
using VirtualCompany.Application.Finance;
namespace VirtualCompany.Shared.Contracts.FortnoxOutboundActions;
public sealed record FortnoxOutboundActionRequest(
        string? CommandType,
        string HttpMethod,
        string Path,
        string TargetCompany,
        JsonNode? Payload,
        Guid? ConnectionId = null,
        Guid? WriteRequestId = null,
        string? PayloadSummary = null,
        string? PayloadHash = null,
        string? ProviderPayloadType = null,
        string? CorrelationId = null);
