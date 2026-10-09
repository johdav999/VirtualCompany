namespace VirtualCompany.Application.Support;

public interface ISupportOperationalReportService
{
    Task<SupportOperationalReport> GetAsync(Guid companyId, SupportOperationalReportQuery query, CancellationToken cancellationToken);
}
