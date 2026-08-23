using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingProviderSwitchCutoverPolicy : IAccountingProviderSwitchCutoverPolicy
{
    public AccountingProviderSwitchCutoverAllowedActionsDto AllowedActions(string status,
        bool targetActivityRecorded, bool retryIsSafe, bool providerReconciliationRequired,
        bool hasApprovedActivation)
    {
        status = AccountingProviderSwitchCutoverStatuses.Normalize(status);
        return new(
            CanStartFreeze: status == AccountingProviderSwitchCutoverStatuses.Queued,
            CanRequestActivationApproval: status == AccountingProviderSwitchCutoverStatuses.AwaitingActivationApproval && !hasApprovedActivation,
            CanActivate: status == AccountingProviderSwitchCutoverStatuses.AwaitingActivationApproval && hasApprovedActivation,
            CanCancel: status == AccountingProviderSwitchCutoverStatuses.Queued,
            CanRetry: status == AccountingProviderSwitchCutoverStatuses.Blocked && retryIsSafe && !providerReconciliationRequired,
            CanRecoverSource: status == AccountingProviderSwitchCutoverStatuses.Blocked && !targetActivityRecorded,
            RequiresProviderReconciliation: providerReconciliationRequired,
            RequiresCorrectiveCutover: status == AccountingProviderSwitchCutoverStatuses.CorrectiveCutoverRequired ||
                (status == AccountingProviderSwitchCutoverStatuses.Blocked && targetActivityRecorded));
    }
}

public sealed class FortnoxAccountingProviderSwitchFinalTransferExecutor : IAccountingProviderSwitchFinalTransferExecutor
{
    private readonly IFortnoxOutboundActionExecutor _executor;
    public FortnoxAccountingProviderSwitchFinalTransferExecutor(IFortnoxOutboundActionExecutor executor) => _executor = executor;
    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task<AccountingProviderSwitchFinalTransferExecutionResult> ExecuteApprovedAsync(Guid companyId,
        Guid writeRequestId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _executor.ExecuteApprovedAsync(companyId, writeRequestId, cancellationToken);
            var succeeded = result.Status == FinanceIntegrationWriteCommandRecordStatuses.Executed;
            return new(succeeded, false,
                !succeeded && result.Status == FinanceIntegrationWriteCommandRecordStatuses.Failed,
                null, result.Summary);
        }
        catch (FortnoxApprovalRequiredException exception)
        {
            return new(false, false, true, null, exception.Message);
        }
    }
}
