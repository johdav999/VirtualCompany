using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Companies;

public sealed partial class CompanyAgentWorkQueryService
{
    private async Task<(List<AgentWorkLinkDto> Links, List<string> Diagnostics)> BusinessLinksAsync(CompanyWorkScope scope, WorkTask task, CancellationToken token)
    {
        var links = new List<AgentWorkLinkDto>(); var diagnostics = new List<string>(); var company=task.CompanyId;
        foreach (var candidate in new (string Kind,string Area,Guid? Id)[] {
            ("deal","sales",task.BusinessDealId),("case","support",task.BusinessCaseId),
            ("invoice","finance",task.BusinessInvoiceId),("bill","finance",task.BusinessBillId),
            ("campaign","marketing",task.BusinessCampaignId),("brief","marketing",task.BusinessBriefId) })
        {
            if (!candidate.Id.HasValue) continue;
            if (!scope.Allows(candidate.Area)) { diagnostics.Add("A related business record is restricted in your access scope."); continue; }
            var id=candidate.Id.Value;
            var exists=candidate.Kind switch {
                "deal"=>await db.Deals.AnyAsync(x=>x.CompanyId==company&&x.Id==id&&!x.IsDeleted,token),
                "case"=>await db.SupportCases.AnyAsync(x=>x.CompanyId==company&&x.Id==id,token),
                "invoice"=>await db.FinanceInvoices.AnyAsync(x=>x.CompanyId==company&&x.Id==id,token),
                "bill"=>await db.FinanceBills.AnyAsync(x=>x.CompanyId==company&&x.Id==id,token) ||
                    await db.DetectedBills.AnyAsync(x=>x.CompanyId==company&&x.Id==id,token),
                "campaign"=>await db.SalesCampaigns.AnyAsync(x=>x.CompanyId==company&&x.Id==id,token),
                _=>await db.MarketingContentBriefs.AnyAsync(x=>x.CompanyId==company&&x.Id==id,token) };
            if (!exists)
            {
                diagnostics.Add("A retained business association is unavailable in this company. Its outcome is unknown.");
                logger.LogWarning("Agent work business association unavailable: {CompanyId} {TaskId} {Kind} {RecordId}",company,task.Id,candidate.Kind,id);
                continue;
            }
            var intakeBill = candidate.Kind == "bill" && await db.DetectedBills.AnyAsync(x => x.CompanyId == company && x.Id == id, token);
            var route=candidate.Kind switch {
                "deal"=>$"/app/sales/deals/{id:D}?companyId={company:D}","case"=>$"/support/cases/{id:D}?companyId={company:D}",
                "invoice"=>$"/finance/reviews/{id:D}?companyId={company:D}",
                "bill"=>$"/finance/{(intakeBill ? "bill-inbox" : "supplier-bills")}/{id:D}?companyId={company:D}&financeSource=operational",
                _=>$"/marketing/review?companyId={company:D}&{(candidate.Kind=="brief"?"briefId":"campaignId")}={id:D}" };
            links.Add(new("Open business record",route,task.UpdatedUtc));
        }
        return (links,diagnostics);
    }
}
