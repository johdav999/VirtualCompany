using VirtualCompany.Application.Approvals;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Companies;

internal sealed record ReviewedTaskPolicyMessage(Guid CompanyId, Guid ApprovalId, Guid AttemptId)
{
    public const string Topic = "task_policy.reviewed_execution_requested";
}

public sealed partial class CompanyApprovalRequestService
{
    internal Task DispatchReviewedTaskPolicyAsync(ReviewedTaskPolicyMessage message, CancellationToken ct) =>
        ((ActionApprovalTargetHandler)_serviceProvider.GetRequiredKeyedService<IApprovalTargetHandler>(ApprovalTargetEntityType.Action))
            .DispatchReviewedTaskPolicyAsync(message, ct);
}
