using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Support;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class FinanceIntegrationWriteApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IServiceProvider _serviceProvider;
    public FinanceIntegrationWriteApprovalTargetHandler(VirtualCompanyDbContext dbContext, IServiceProvider serviceProvider)
    {
        _dbContext = dbContext;
        _serviceProvider = serviceProvider;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.FinanceIntegrationWriteCommands.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task<object?> GetReviewMaterialAsync(ApprovalRequest approval, CancellationToken ct)
    {
        object? material = null;
        var write = await _dbContext.FinanceIntegrationWriteCommands.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        material = new
        {
            write.CommandType,
            write.HttpMethod,
            write.Path,
            write.TargetCompany,
            write.ConnectionId,
            write.PayloadHash,
            write.SanitizedPayloadJson
        };
        return material;
    }

    public async Task<ApprovalTargetReviewDetails> GetReviewDetailsAsync(ApprovalRequest approval, CancellationToken ct)
    {
        var comparisons = new List<ApprovalComparisonDto>();
        var evidence = new List<ApprovalEvidenceDto>();
        string? executionStatus = null;
        executionStatus = (await _dbContext.FinanceIntegrationWriteCommands.Where(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId).Select(x => x.Status).SingleAsync(ct)).Replace('_', ' ');
        return new(comparisons, evidence, executionStatus);
    }

    public async Task<ApprovalTargetStateTransition?> ApplyDecisionAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status == ApprovalRequestStatus.Pending)
            return null;
        var command = await _dbContext.FinanceIntegrationWriteCommands.Include(x => x.Connection).SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, cancellationToken);
        var previousStatus = command.Status;
        var now = DateTime.UtcNow;
        if (approval.Status == ApprovalRequestStatus.Approved)
        {
            var approver = approval.Steps.FirstOrDefault(x => x.DecidedByUserId.HasValue)?.DecidedByUserId;
            command.MarkApproved(approval.Id, approver, now);
            _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(Guid.NewGuid(), command.CompanyId, command.ConnectionId, command.Connection?.ProviderKey ?? "accounting_system", "write_approval_approved", FinanceIntegrationAuditOutcomes.Succeeded, command.CommandType, command.Id, null, approval.Id.ToString("N"), "Approved accounting-system action is ready for provider execution.", now));
            return ApprovalTargetStateTransition.ForFinanceIntegrationWrite(command.Id, previousStatus, command.Status);
        }

        if (approval.Status is ApprovalRequestStatus.Rejected or ApprovalRequestStatus.ChangesRequested or ApprovalRequestStatus.Stale or ApprovalRequestStatus.Superseded or ApprovalRequestStatus.Revoked or ApprovalRequestStatus.Cancelled)
        {
            command.MarkRejected(now);
            return ApprovalTargetStateTransition.ForFinanceIntegrationWrite(command.Id, previousStatus, command.Status);
        }

        if (approval.Status == ApprovalRequestStatus.Expired)
        {
            command.MarkExpired(now);
            return ApprovalTargetStateTransition.ForFinanceIntegrationWrite(command.Id, previousStatus, command.Status);
        }

        return null;
    }

    public async Task AfterDecisionPersistedAsync(ApprovalRequest approval, CancellationToken cancellationToken)
    {
        if (approval.Status != ApprovalRequestStatus.Approved)
            return;
        await _serviceProvider.GetRequiredService<IFinanceAccountingActionService>().RetryApprovedAsync(approval.CompanyId, approval.TargetEntityId, cancellationToken);
        await _serviceProvider.GetRequiredService<ISupportRefundFinanceService>().RefreshByWriteRequestAsync(approval.CompanyId, approval.TargetEntityId, cancellationToken);
    }
}
