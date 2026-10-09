using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Finance;

public sealed record GetGeneralLedgerQuery(
    Guid CompanyId,
    Guid FiscalPeriodId,
    Guid? FinanceAccountId = null,
    int Page = 1,
    int PageSize = 200);
public sealed record GetTrialBalanceQuery(Guid CompanyId, Guid FiscalPeriodId);
public sealed record GetAccountingTaxSummaryQuery(Guid CompanyId, Guid FiscalPeriodId);
public sealed record GetControlAccountReconciliationQuery(Guid CompanyId, Guid FiscalPeriodId);
public sealed record ReviewAccountingTaxSummaryCommand(Guid CompanyId, Guid FiscalPeriodId, Guid ActorUserId);
public sealed record RequestAccountingExportCommand(
    Guid CompanyId,
    Guid FiscalPeriodId,
    Guid ActorUserId,
    string IdempotencyKey,
    string ExportType = AccountingExportTypeValues.GenericJson,
    string? CorrelationId = null);
public sealed record GetAccountingExportQuery(Guid CompanyId, Guid ExportId);
public sealed record ListAccountingExportsQuery(
    Guid CompanyId,
    Guid? FiscalPeriodId = null,
    int Page = 1,
    int PageSize = 100);

public sealed record AccountingExportDownloadDto(string FileName, string MediaType, byte[] Content, string Checksum);

public class AccountingExportException(string reasonCode, string message, bool isConflict = false) : Exception(message)
{
    public string ReasonCode { get; } = string.IsNullOrWhiteSpace(reasonCode)
        ? throw new ArgumentException("An export reason code is required.", nameof(reasonCode))
        : reasonCode.Trim().ToLowerInvariant();
    public bool IsConflict { get; } = isConflict;
}

public interface IAccountingReportingService
{
    Task<GeneralLedgerReportDto> GetGeneralLedgerAsync(GetGeneralLedgerQuery query, CancellationToken cancellationToken);
    Task<TrialBalanceReportDto> GetTrialBalanceAsync(GetTrialBalanceQuery query, CancellationToken cancellationToken);
    Task<AccountingTaxSummaryDto> GetTaxSummaryAsync(GetAccountingTaxSummaryQuery query, CancellationToken cancellationToken);
    Task<AccountingTaxSummaryDto> ReviewTaxSummaryAsync(ReviewAccountingTaxSummaryCommand command, CancellationToken cancellationToken);
    Task<ControlAccountReconciliationDto> GetControlAccountReconciliationAsync(GetControlAccountReconciliationQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingPeriodHistoryDto>> GetPeriodHistoryAsync(Guid companyId, Guid fiscalPeriodId, CancellationToken cancellationToken);
    Task<AccountingExportJobDto> RequestExportAsync(RequestAccountingExportCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<AccountingExportJobDto>> ListExportsAsync(ListAccountingExportsQuery query, CancellationToken cancellationToken);
    Task<AccountingExportDownloadDto> DownloadExportAsync(GetAccountingExportQuery query, CancellationToken cancellationToken);
    Task<int> RunDueExportsAsync(CancellationToken cancellationToken);
}
