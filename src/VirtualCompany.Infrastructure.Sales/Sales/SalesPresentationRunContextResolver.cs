using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesPresentationRunContextResolver(VirtualCompanyDbContext db):ISalesPresentationRunContextResolver
{
    public async Task<ResolvedSalesPresentationContext> ResolveAdHocAsync(Guid companyId,CreateAdHocSalesPresentationCommand command,CancellationToken ct)
    {
        var account=command.CustomerCompanyId.HasValue?await db.CustomerCompanies.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==command.CustomerCompanyId&&!x.IsDeleted,ct):null;
        var contact=command.ContactId.HasValue?await db.Contacts.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==command.ContactId&&!x.IsDeleted,ct):null;
        var lead=command.LeadId.HasValue?await db.Leads.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==command.LeadId&&!x.IsDeleted,ct):null;
        var deal=command.DealId.HasValue?await db.Deals.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.Id==command.DealId&&!x.IsDeleted,ct):null;
        if(command.CustomerCompanyId.HasValue&&account is null||command.ContactId.HasValue&&contact is null||command.LeadId.HasValue&&lead is null||command.DealId.HasValue&&deal is null)
            throw new SalesPresentationPresetConflictException(SalesPresentationAdHocProblemCodes.ContextInvalid,"One or more selected customer records are unavailable or belong to another company.");
        if(contact is not null&&lead?.PrimaryContactId is Guid leadContact&&leadContact!=contact.Id||contact is not null&&deal?.PrimaryContactId is Guid dealContact&&dealContact!=contact.Id||lead is not null&&deal?.SourceLeadId is Guid sourceLead&&sourceLead!=lead.Id)
            throw new SalesPresentationPresetConflictException(SalesPresentationAdHocProblemCodes.ContextInvalid,"The selected contact, lead, and deal do not describe the same Sales relationship.");
        var accountIds=new[]{account?.Id,contact?.CustomerCompanyId,lead?.CustomerCompanyId,deal?.CustomerCompanyId}.Where(x=>x.HasValue).Select(x=>x!.Value).Distinct().ToArray();
        if(accountIds.Length>1)throw new SalesPresentationPresetConflictException(SalesPresentationAdHocProblemCodes.ContextInvalid,"The selected records belong to different customer accounts.");
        var resolvedAccount=accountIds.SingleOrDefault();if(resolvedAccount==Guid.Empty)resolvedAccount=account?.Id??Guid.Empty;
        var evidence=new List<SalesPresentationRunArtifactDraft>();var order=0;
        if(account is not null)evidence.Add(new("confirmed_fact",$"Customer account: {account.Name}","internal",$"customer_company:{account.Id:D}",order++));
        if(contact is not null)evidence.Add(new("confirmed_fact",$"Contact: {contact.FullName} ({contact.Title??"role unavailable"})","internal",$"contact:{contact.Id:D}",order++));
        if(lead is not null)evidence.Add(new("confirmed_fact",$"Lead: {lead.Title}","internal",$"lead:{lead.Id:D}",order++));
        if(deal is not null)evidence.Add(new("confirmed_fact",$"Deal: {deal.Title}; status {deal.Status}; value {deal.Amount} {deal.Currency}.","internal",$"deal:{deal.Id:D}",order++));
        var empty=account is null&&contact is null&&lead is null&&deal is null;
        if(empty)evidence.Add(new("missing_evidence","No customer context was selected. Customer-specific facts, needs, risks, and commercial claims are unavailable and require presenter review.","needs_review",null,order));
        var label=contact?.FullName??account?.Name??deal?.Title??lead?.Title??"General audience — customer context unavailable";
        return new(resolvedAccount==Guid.Empty?null:resolvedAccount,contact?.Id,lead?.Id,deal?.Id,label,evidence,empty,empty?["customer_context_missing"]:[]);
    }
}
