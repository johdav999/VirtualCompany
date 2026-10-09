namespace VirtualCompany.Application.Finance;

public static class AccountingCloseWorkspaceActions
{
    public const string CompleteTask = "complete_task";
    public const string RefreshReadiness = "refresh_readiness";
    public const string ProposeWaiver = "propose_waiver";
    public const string SignOff = "sign_off";
    public const string Lock = "lock";
    public const string RequestReopen = "request_reopen";
    public const string ExecuteReopen = "execute_reopen";
    public const string RequestPackage = "request_package";
    public const string ApprovePackage = "approve_package";
    public const string CancelPackage = "cancel_package";
    public const string OpenYearEnd = "open_year_end";
    public const string RunYearEndAction = "run_year_end_action";
}

public sealed record GetAccountingCloseWorkspaceQuery(Guid CompanyId, Guid? FiscalPeriodId = null,
    Guid? CloseInstanceId = null);

public interface IAccountingCloseWorkspaceService
{
    Task<AccountingCloseWorkspaceDto> GetAsync(GetAccountingCloseWorkspaceQuery query,
        CancellationToken cancellationToken);
}
