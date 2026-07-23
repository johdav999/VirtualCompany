using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Communication;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.ValueObjects;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Communication;

public sealed class CommunicationLanguageService(
    VirtualCompanyDbContext db,
    IAuditEventWriter audit) : ICommunicationLanguageService
{
    public async Task<CommunicationLanguagePreferenceDto?> GetContactAsync(Guid companyId, Guid contactId, CancellationToken cancellationToken)
    {
        var entity = await db.Contacts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == contactId && !x.IsDeleted, cancellationToken);
        return entity is null ? null : new(entity.Id, "contact", entity.PreferredLanguage, entity.UpdatedUtc);
    }

    public async Task<CommunicationLanguagePreferenceDto?> UpdateContactAsync(Guid companyId, Guid userId, Guid contactId, UpdateCommunicationLanguageRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.Contacts.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == contactId && !x.IsDeleted, cancellationToken);
        if (entity is null) return null;
        var previous = entity.PreferredLanguage;
        SetLanguage(() => entity.SetPreferredLanguage(request.LanguageTag));
        await SaveAndAuditAsync(companyId, userId, "communication.contact_language_changed", "contact", entity.Id, previous, entity.PreferredLanguage, cancellationToken);
        return new(entity.Id, "contact", entity.PreferredLanguage, entity.UpdatedUtc);
    }

    public async Task<CommunicationLanguagePreferenceDto?> GetCampaignAsync(Guid companyId, Guid campaignId, CancellationToken cancellationToken)
    {
        var entity = await db.SalesCampaigns.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == campaignId, cancellationToken);
        return entity is null ? null : new(entity.Id, "sales_campaign", entity.CommunicationLanguage, entity.UpdatedUtc);
    }

    public async Task<CommunicationLanguagePreferenceDto?> UpdateCampaignAsync(Guid companyId, Guid userId, Guid campaignId, UpdateCommunicationLanguageRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.SalesCampaigns.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == campaignId, cancellationToken);
        if (entity is null) return null;
        var previous = entity.CommunicationLanguage;
        SetLanguage(() => entity.SetCommunicationLanguage(request.LanguageTag));
        await SaveAndAuditAsync(companyId, userId, "communication.campaign_language_changed", "sales_campaign", entity.Id, previous, entity.CommunicationLanguage, cancellationToken);
        return new(entity.Id, "sales_campaign", entity.CommunicationLanguage, entity.UpdatedUtc);
    }

    public async Task<CommunicationLanguagePreferenceDto?> GetSupportCaseAsync(Guid companyId, Guid supportCaseId, CancellationToken cancellationToken)
    {
        var entity = await db.SupportCases.AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        return entity is null ? null : new(entity.Id, "support_case", entity.ConversationLanguage, entity.UpdatedUtc);
    }

    public async Task<CommunicationLanguagePreferenceDto?> UpdateSupportCaseAsync(Guid companyId, Guid userId, Guid supportCaseId, UpdateCommunicationLanguageRequest request, CancellationToken cancellationToken)
    {
        var entity = await db.SupportCases.SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == supportCaseId, cancellationToken);
        if (entity is null) return null;
        var previous = entity.ConversationLanguage;
        SetLanguage(() => entity.SetConversationLanguage(request.LanguageTag));
        await SaveAndAuditAsync(companyId, userId, "communication.support_case_language_changed", "support_case", entity.Id, previous, entity.ConversationLanguage, cancellationToken);
        return new(entity.Id, "support_case", entity.ConversationLanguage, entity.UpdatedUtc);
    }

    public async Task<CommunicationLanguageResolution> ResolveAsync(Guid companyId, Guid? contactId, Guid? supportCaseId, Guid? campaignId, CancellationToken cancellationToken)
    {
        var recipient = contactId.HasValue
            ? await db.Contacts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Id == contactId && !x.IsDeleted).Select(x => x.PreferredLanguage).SingleOrDefaultAsync(cancellationToken)
            : null;
        var conversation = supportCaseId.HasValue
            ? await db.SupportCases.Where(x => x.CompanyId == companyId && x.Id == supportCaseId).Select(x => x.ConversationLanguage).SingleOrDefaultAsync(cancellationToken)
            : null;
        var campaign = campaignId.HasValue
            ? await db.SalesCampaigns.IgnoreQueryFilters().Where(x => x.CompanyId == companyId && x.Id == campaignId).Select(x => x.CommunicationLanguage).SingleOrDefaultAsync(cancellationToken)
            : null;
        var company = await db.Companies.IgnoreQueryFilters().Where(x => x.Id == companyId).Select(x => x.Language).SingleOrDefaultAsync(cancellationToken);
        return CommunicationLanguageResolver.Resolve(recipient, conversation, campaign, company);
    }

    private static void SetLanguage(Action update)
    {
        try
        {
            update();
        }
        catch (ArgumentException exception)
        {
            throw new CommunicationLanguageValidationException(exception.Message);
        }
    }

    private async Task SaveAndAuditAsync(Guid companyId, Guid userId, string action, string targetType, Guid targetId, string? previous, string? current, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEventWriteRequest(
            companyId,
            userId == Guid.Empty ? AuditActorTypes.System : AuditActorTypes.Human,
            userId == Guid.Empty ? null : userId,
            action,
            targetType,
            targetId.ToString("D"),
            AuditEventOutcomes.Succeeded,
            "Communication language preference updated.",
            Metadata: new Dictionary<string, string?> { ["previousLanguage"] = previous, ["newLanguage"] = current }), cancellationToken);
    }
}
