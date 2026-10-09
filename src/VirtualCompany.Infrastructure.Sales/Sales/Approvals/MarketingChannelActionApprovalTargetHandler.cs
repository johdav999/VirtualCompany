using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class MarketingChannelActionApprovalTargetHandler : IApprovalTargetHandler
{
    private readonly VirtualCompanyDbContext _dbContext;
    public MarketingChannelActionApprovalTargetHandler(VirtualCompanyDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> ExistsAsync(Guid companyId, Guid targetEntityId, CancellationToken cancellationToken) => await _dbContext.MarketingChannelActions.AsNoTracking().AnyAsync(x => x.CompanyId == companyId && x.Id == targetEntityId, cancellationToken);
    public async Task<object?> GetReviewMaterialAsync(ApprovalRequest approval, CancellationToken ct)
    {
        object? material = null;
        var action = await _dbContext.MarketingChannelActions.AsNoTracking().SingleAsync(x => x.CompanyId == approval.CompanyId && x.Id == approval.TargetEntityId, ct);
        material = new
        {
            action.MarketingChannelConnectionId,
            action.MarketingContentBriefId,
            action.ContentBriefVersion,
            action.DestinationReference,
            action.ActionType,
            action.PayloadJson,
            action.ScheduledUtc
        };
        return material;
    }
}
