namespace VirtualCompany.Application.Finance;

public sealed record GetFinanceOperationalReportQuery(Guid CompanyId, DateOnly AsOfDate, int HorizonDays = 14,
    string? Currency = null, string? Bucket = null, string? SourceFilter = null);
public interface IFinanceOperationalReportService
{
    Task<FinanceOperationalReportDto> GetAsync(GetFinanceOperationalReportQuery query, CancellationToken token);
}
