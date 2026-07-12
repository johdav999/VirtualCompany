using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesSourceService(VirtualCompanyDbContext db) : ISalesSourceService
{
    public async Task<SalesSourceTouchDto> RecordAsync(Guid companyId, RecordSalesSourceTouchRequest r, CancellationToken ct)
    {
        var result = await StageAsync(companyId, r, ct);
        await db.SaveChangesAsync(ct);
        return result;
    }

    public async Task<SalesSourceTouchDto> StageAsync(Guid companyId, RecordSalesSourceTouchRequest r, CancellationToken ct)
    {
        if (companyId == Guid.Empty || r.SubjectId == Guid.Empty) throw new ArgumentException("Company and subject are required.");
        await EnsureSubjectAsync(companyId, r.SubjectType, r.SubjectId, ct);
        if (r.CampaignId.HasValue && !await db.SalesAcquisitionCampaigns.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == r.CampaignId.Value, ct))
            throw new KeyNotFoundException("The acquisition campaign was not found in this company.");
        var touch = new SalesSourceTouch(Guid.NewGuid(), companyId, r.SubjectType, r.SubjectId, r.Category, r.Provider,
            r.Channel, r.InteractionType, r.SourceReference, r.ObservedUtc ?? DateTime.UtcNow, r.ActorType,
            r.ActorReference, r.CampaignId, r.Evidence, r.LandingPage, r.Referrer, r.UtmSource, r.UtmMedium,
            r.UtmCampaign, r.UtmContent, r.UtmTerm, r.Cost, r.Currency, r.MetadataJson);
        var existing = await db.SalesSourceTouches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.DedupeKey == touch.DedupeKey, ct);
        if (existing is not null) return Map(existing);
        db.SalesSourceTouches.Add(touch);
        var attribution = await db.SalesSourceAttributions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SubjectType == touch.SubjectType && x.SubjectId == touch.SubjectId, ct);
        if (attribution is null) db.SalesSourceAttributions.Add(new SalesSourceAttribution(Guid.NewGuid(), companyId, touch.SubjectType, touch.SubjectId, touch.Id, touch.Cost, touch.Currency, r.IsConversion));
        else
        {
            var firstObserved = await db.SalesSourceTouches.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Id == attribution.FirstTouchId).Select(x => (DateTime?)x.ObservedUtc).SingleOrDefaultAsync(ct);
            var lastObserved = await db.SalesSourceTouches.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Id == attribution.LastTouchId).Select(x => (DateTime?)x.ObservedUtc).SingleOrDefaultAsync(ct);
            attribution.Record(touch.Id, touch.Cost, touch.Currency, r.IsConversion, !firstObserved.HasValue || touch.ObservedUtc < firstObserved.Value, !lastObserved.HasValue || touch.ObservedUtc >= lastObserved.Value);
        }
        return Map(touch);
    }

    public async Task<SalesAttributionDto?> GetAsync(Guid companyId, string subjectType, Guid subjectId, CancellationToken ct)
    {
        var a = await db.SalesSourceAttributions.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.CompanyId == companyId && x.SubjectType == subjectType.ToLower() && x.SubjectId == subjectId, ct);
        if (a is null) return null;
        var timeline = await TimelineAsync(companyId, subjectType, subjectId, ct);
        return new(a.SubjectId, a.SubjectType, a.OriginalTouchId, a.FirstTouchId, a.LastTouchId, a.ConversionTouchId, a.TouchCount, a.TotalAcquisitionCost, a.Currency, timeline);
    }

    public async Task<IReadOnlyList<SalesSourceTouchDto>> TimelineAsync(Guid companyId, string subjectType, Guid subjectId, CancellationToken ct) =>
        (await db.SalesSourceTouches.IgnoreQueryFilters().AsNoTracking().Where(x => x.CompanyId == companyId && x.SubjectType == subjectType.ToLower() && x.SubjectId == subjectId).OrderBy(x => x.ObservedUtc).ToListAsync(ct)).Select(Map).ToList();
    public async Task<IReadOnlyList<SalesAcquisitionCampaignDto>> ListCampaignsAsync(Guid companyId, CancellationToken ct) => (await db.SalesAcquisitionCampaigns.IgnoreQueryFilters().AsNoTracking().Where(x=>x.CompanyId==companyId).OrderByDescending(x=>x.CreatedUtc).ToListAsync(ct)).Select(Map).ToList();
    public async Task<SalesAcquisitionCampaignDto> CreateCampaignAsync(Guid companyId, SaveSalesAcquisitionCampaignRequest r, CancellationToken ct) { var x=new SalesAcquisitionCampaign(Guid.NewGuid(),companyId,r.Name,r.Category,r.Provider,r.ExternalReference,r.Budget,r.Currency,r.StartsUtc,r.EndsUtc); db.Add(x); await db.SaveChangesAsync(ct); return Map(x); }
    private static SalesSourceTouchDto Map(SalesSourceTouch x) => new(x.Id, x.Category, x.Provider, x.Channel, x.InteractionType, x.SourceReference, x.Evidence, x.ObservedUtc, x.CampaignId, x.Cost, x.Currency);
    private static SalesAcquisitionCampaignDto Map(SalesAcquisitionCampaign x)=>new(x.Id,x.Name,x.Category,x.Provider,x.ExternalReference,x.Budget,x.Currency,x.StartsUtc,x.EndsUtc,x.Status);
    private async Task EnsureSubjectAsync(Guid companyId, string subjectType, Guid subjectId, CancellationToken ct)
    {
        var exists = subjectType.Trim().ToLowerInvariant() switch
        {
            "lead" => db.Leads.Local.Any(x => x.CompanyId == companyId && x.Id == subjectId) || await db.Leads.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == subjectId && !x.IsDeleted, ct),
            "prospect_account" => db.ProspectAccounts.Local.Any(x => x.CompanyId == companyId && x.Id == subjectId) || await db.ProspectAccounts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == subjectId, ct),
            "contact" => db.Contacts.Local.Any(x => x.CompanyId == companyId && x.Id == subjectId) || await db.Contacts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == subjectId && !x.IsDeleted, ct),
            "deal" => db.Deals.Local.Any(x => x.CompanyId == companyId && x.Id == subjectId) || await db.Deals.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.Id == subjectId && !x.IsDeleted, ct),
            _ => throw new ArgumentException("Source touches support leads, prospects, contacts, and deals.")
        };
        if (!exists) throw new KeyNotFoundException("The source subject was not found in this company.");
    }
}
