using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchMappingDecisionApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _auditEventWriter;
    public AccountingProviderSwitchMappingDecisionApprovalTargetHandler(VirtualCompanyDbContext dbContext, IAuditEventWriter auditEventWriter)
    {
        _dbContext = dbContext;
        _auditEventWriter = auditEventWriter;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var decision = await _dbContext.AccountingProviderSwitchMappingDecisions.IgnoreQueryFilters().Include(x => x.AffectedRecords).SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var recordIds = decision.AffectedRecords.Select(x => x.StagedRecordId).ToArray();
        var currentRecords = await _dbContext.AccountingProviderSwitchStagedRecords.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == approval.CompanyId && x.SwitchId == decision.SwitchId && recordIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var bindingCurrent = decision.AffectedRecords.Count > 0 && decision.AffectedRecords.All(link => currentRecords.TryGetValue(link.StagedRecordId, out var record) && record.IsCurrent && record.SourceHash == link.StagedSourceHash && record.NormalizedHash == link.StagedNormalizedHash);
        var actorUserId = approval.Steps.Where(x => x.DecidedByUserId.HasValue).OrderByDescending(x => x.DecidedUtc).Select(x => x.DecidedByUserId!.Value).FirstOrDefault();
        string action;
        string summary;
        string outcome;
        if (!bindingCurrent || decision.Status == AccountingProviderSwitchMappingStatuses.Stale)
        {
            if (decision.Status != AccountingProviderSwitchMappingStatuses.Stale)
                decision.MarkStale(DateTime.UtcNow);
            action = AuditEventActions.AccountingProviderSwitchStaleDecisionRejected;
            summary = "The approval decision was recorded, but the mapping evidence was stale and cannot be used.";
            outcome = AuditEventOutcomes.Blocked;
        }
        else if (approval.Status == ApprovalRequestStatus.Approved)
        {
            decision.RecordApprovalDecision(approval.Id, approved: true, DateTime.UtcNow);
            action = AuditEventActions.AccountingProviderSwitchMappingApproved;
            summary = "The versioned accounting migration mapping was approved with current source evidence.";
            outcome = AuditEventOutcomes.Approved;
        }
        else
        {
            decision.RecordApprovalDecision(approval.Id, approved: false, DateTime.UtcNow);
            action = AuditEventActions.AccountingProviderSwitchMappingRejected;
            summary = "The versioned accounting migration mapping was rejected.";
            outcome = AuditEventOutcomes.Rejected;
        }

        await _auditEventWriter.WriteAsync(new AuditEventWriteRequest(approval.CompanyId, AuditActorTypes.User, actorUserId == Guid.Empty ? approval.RequestedByActorId : actorUserId, action, AuditTargetTypes.AccountingProviderSwitchMappingDecision, decision.Id.ToString("D"), outcome, summary, ["approval", "accounting_provider_switch", "mapping_decision"], new Dictionary<string, string?> { ["switchId"] = decision.SwitchId.ToString("D"), ["mappingVersion"] = decision.MappingVersion.ToString(), ["bindingHash"] = decision.BindingHash, ["approvalRequestId"] = approval.Id.ToString("D"), ["bindingCurrent"] = bindingCurrent.ToString() }), cancellationToken);
        return null;
    }
}
