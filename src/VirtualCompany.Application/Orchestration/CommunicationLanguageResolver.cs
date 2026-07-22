using VirtualCompany.Domain.ValueObjects;

namespace VirtualCompany.Application.Orchestration;

public sealed record CommunicationLanguageResolution(
    string LanguageTag,
    string Source,
    decimal Confidence,
    string Evidence,
    bool RequiresHumanReview);

public static class CommunicationLanguageResolver
{
    public static CommunicationLanguageResolution Resolve(
        string? recipientLanguage,
        string? conversationLanguage,
        string? campaignLanguage,
        string? companyDefaultLanguage)
    {
        var explicitRecipient = Normalize(recipientLanguage);
        if (explicitRecipient is not null)
            return new(explicitRecipient, "recipient_explicit", 1m, "Recipient language preference", false);

        var conversation = Normalize(conversationLanguage);
        var campaign = Normalize(campaignLanguage);
        if (conversation is not null && campaign is not null && !string.Equals(conversation, campaign, StringComparison.OrdinalIgnoreCase))
            return new(conversation, "conversation_conflict", 0.6m, "Conversation and campaign language evidence conflict", true);
        if (conversation is not null)
            return new(conversation, "conversation", 0.9m, "Conversation language", false);
        if (campaign is not null)
            return new(campaign, "campaign", 0.85m, "Campaign language", false);

        var company = Normalize(companyDefaultLanguage);
        if (company is not null)
            return new(company, "company_default", 0.7m, "Company communication default", false);

        return new("en-GB", "system_fallback", 0.5m, "No valid language evidence was available", true);
    }

    public static string? Normalize(string? value)
    {
        return CommunicationLanguageTag.TryNormalize(value, out var normalized) ? normalized : null;
    }
}
