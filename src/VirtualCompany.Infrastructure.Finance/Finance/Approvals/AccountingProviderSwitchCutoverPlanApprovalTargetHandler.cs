using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchCutoverPlanApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _auditEventWriter;
    public AccountingProviderSwitchCutoverPlanApprovalTargetHandler(VirtualCompanyDbContext dbContext, IAuditEventWriter auditEventWriter)
    {
        _dbContext = dbContext;
        _auditEventWriter = auditEventWriter;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.AccountingProviderSwitchCutoverPlans.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var plan = await _dbContext.AccountingProviderSwitchCutoverPlans.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var actorUserId = approval.Steps.Where(x => x.DecidedByUserId.HasValue).OrderByDescending(x => x.DecidedUtc).Select(x => x.DecidedByUserId!.Value).FirstOrDefault();
        await _auditEventWriter.WriteAsync(new AuditEventWriteRequest(approval.CompanyId, AuditActorTypes.User, actorUserId == Guid.Empty ? approval.RequestedByActorId : actorUserId, approval.Status == ApprovalRequestStatus.Approved ? "accounting.provider_switch.plan_approved" : "accounting.provider_switch.plan_rejected", AuditTargetTypes.AccountingProviderSwitchCutoverPlan, plan.Id.ToString("D"), approval.Status.ToStorageValue(), approval.Status == ApprovalRequestStatus.Approved ? "The immutable accounting migration cutover plan was approved." : "The immutable accounting migration cutover plan was not approved.", ["approval", "accounting_provider_switch", "cutover_plan"], new Dictionary<string, string?> { ["switchId"] = plan.SwitchId.ToString("D"), ["planVersion"] = plan.PlanVersion.ToString(), ["planHash"] = plan.PlanHash, ["approvalRequestId"] = approval.Id.ToString("D") }), cancellationToken);
        return null;
    }
}
