using VirtualCompany.Shared;

namespace VirtualCompany.Application.Finance;

public sealed record GetStatementWorkspaceQuery(Guid CompanyId, Guid FiscalPeriodId, string ReportKind,
    Guid? ComparisonFiscalPeriodId = null, Guid? SnapshotId = null, Guid? ComparisonSnapshotId = null);

public interface IFinancialStatementWorkspaceService
{
    Task<StatementWorkspaceReport> GetAsync(GetStatementWorkspaceQuery query, CancellationToken cancellationToken);
}
