namespace VirtualCompany.Application.Finance;

public sealed record GetFinanceBillInboxQuery(Guid CompanyId, int Limit = 100);

public sealed record GetFinanceBillInboxDetailQuery(Guid CompanyId, Guid BillId);

public sealed record ApproveFinanceBillCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Rationale);

public sealed record RejectFinanceBillCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Rationale);

public sealed record RequestFinanceBillClarificationCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Rationale);

public sealed record RequestFinanceBillFortnoxRegistrationCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId,
    string ActorDisplayName,
    string Rationale);

public sealed record ExecuteFinanceBillFortnoxRegistrationCommand(
    Guid CompanyId,
    Guid BillId,
    Guid? ActorUserId = null);

public sealed record GetSupplierApprovalAutomationQuery(Guid CompanyId, Guid BillId);

public sealed record SetSupplierApprovalAutomationCommand(
    Guid CompanyId,
    Guid BillId,
    string Stage,
    bool Enabled,
    Guid? ActorUserId,
    string ActorDisplayName);

public interface IFinanceBillInboxService
{
    Task<IReadOnlyList<FinanceBillInboxRowDto>> GetInboxAsync(GetFinanceBillInboxQuery query, CancellationToken cancellationToken);
    Task<FinanceBillInboxDetailDto?> GetDetailAsync(GetFinanceBillInboxDetailQuery query, CancellationToken cancellationToken);
    Task<FinanceBillReviewActionResultDto> ApproveAsync(ApproveFinanceBillCommand command, CancellationToken cancellationToken);
    Task<FinanceBillReviewActionResultDto> RejectAsync(RejectFinanceBillCommand command, CancellationToken cancellationToken);
    Task<FinanceBillReviewActionResultDto> RequestClarificationAsync(RequestFinanceBillClarificationCommand command, CancellationToken cancellationToken);
    Task<FinanceBillFortnoxRegistrationDto> RequestFortnoxRegistrationAsync(RequestFinanceBillFortnoxRegistrationCommand command, CancellationToken cancellationToken);
    Task<FinanceBillFortnoxRegistrationDto> ExecuteFortnoxRegistrationAsync(ExecuteFinanceBillFortnoxRegistrationCommand command, CancellationToken cancellationToken);
    Task<FinanceBillFortnoxRegistrationDto> SendFortnoxRegistrationDirectAsync(ExecuteFinanceBillFortnoxRegistrationCommand command, CancellationToken cancellationToken);
    Task<SupplierApprovalAutomationDto> GetApprovalAutomationAsync(GetSupplierApprovalAutomationQuery query, CancellationToken cancellationToken);
    Task<SupplierApprovalAutomationDto> SetApprovalAutomationAsync(SetSupplierApprovalAutomationCommand command, CancellationToken cancellationToken);
}
