using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class CompanyTaskService
{
    private async Task ValidateBusinessAssociationsAsync(WorkTask task, CompanyWorkScope scope, CancellationToken token)
    {
        var company = task.CompanyId;
        var valid =
            (!task.BusinessDealId.HasValue || scope.Allows("sales") && await _dbContext.Deals.AnyAsync(x => x.CompanyId == company && x.Id == task.BusinessDealId && !x.IsDeleted, token)) &&
            (!task.BusinessCaseId.HasValue || scope.Allows("support") && await _dbContext.SupportCases.AnyAsync(x => x.CompanyId == company && x.Id == task.BusinessCaseId, token)) &&
            (!task.BusinessInvoiceId.HasValue || scope.Allows("finance") && await _dbContext.FinanceInvoices.AnyAsync(x => x.CompanyId == company && x.Id == task.BusinessInvoiceId, token)) &&
            (!task.BusinessBillId.HasValue || scope.Allows("finance") &&
                (await _dbContext.FinanceBills.AnyAsync(x => x.CompanyId == company && x.Id == task.BusinessBillId, token) ||
                 await _dbContext.DetectedBills.AnyAsync(x => x.CompanyId == company && x.Id == task.BusinessBillId, token))) &&
            (!task.BusinessCampaignId.HasValue || (scope.Allows("marketing") || scope.Allows("sales")) && await _dbContext.SalesCampaigns.AnyAsync(x => x.CompanyId == company && x.Id == task.BusinessCampaignId, token)) &&
            (!task.BusinessBriefId.HasValue || scope.Allows("marketing") && await _dbContext.MarketingContentBriefs.AnyAsync(x => x.CompanyId == company && x.Id == task.BusinessBriefId, token));
        if (!valid) throw new TaskValidationException(new Dictionary<string, string[]>
        { ["InputPayload"] = ["The business association is unavailable in the current company and access scope. No task was created."] });
    }
}
