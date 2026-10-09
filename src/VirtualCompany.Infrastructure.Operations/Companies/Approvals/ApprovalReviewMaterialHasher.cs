using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class ApprovalReviewMaterialHasher(IServiceProvider services)
{
    public async Task<string> ComputeHashAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        var handler = services.GetKeyedService<IApprovalTargetHandler>(ApprovalTargetEntityTypeValues.Parse(approval.TargetEntityType));
        var material = handler is null ? null : await handler.GetReviewMaterialAsync(approval, cancellationToken);
        var node = JsonSerializer.SerializeToNode(new { approval.CompanyId, approval.TargetEntityId, approval.TargetEntityType, approval.ApprovalType, approval.RequiredRole, approval.RequiredUserId, approval.ThresholdContext, material });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(node))));
    }

    private static string Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => "{" + string.Join(",", obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => JsonSerializer.Serialize(x.Key) + ":" + Canonical(x.Value))) + "}",
        JsonArray array => "[" + string.Join(",", array.Select(Canonical)) + "]",
        _ => node?.ToJsonString() ?? "null"
    };
}
