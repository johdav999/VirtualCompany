using VirtualCompany.Shared.Mobile;

namespace VirtualCompany.Application.Mobile;

public sealed record GetMobileHomeSummaryQuery(Guid CompanyId, int TaskFollowUpLimit = 5);

public interface IMobileSummaryService
{
    Task<MobileHomeSummaryResponse> GetHomeSummaryAsync(
        GetMobileHomeSummaryQuery query,
        CancellationToken cancellationToken);
}
