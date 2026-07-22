using VirtualCompany.Application.Orchestration;

namespace VirtualCompany.Application.Communication;

public static class CommunicationTemplateKeys
{
    public const string SalesFollowUpSubject = "sales.follow_up_subject";
    public const string SalesWebsiteLeadSubject = "sales.website_lead_subject";
    public const string SupportReplySubject = "support.reply_subject";
    public const string FinanceNotificationSubject = "finance.notification_subject";
}

public sealed record CommunicationTemplateSelection(
    string Key,
    string LanguageTag,
    string Template,
    bool UsedFallback,
    bool RequiresHumanReview);

public static class CommunicationTemplateCatalog
{
    private const string SourceLanguage = "en-GB";

    private static readonly IReadOnlyDictionary<(string Key, string Language), string> Templates =
        new Dictionary<(string, string), string>
        {
            [(CommunicationTemplateKeys.SalesFollowUpSubject, "en-GB")] = "Following up",
            [(CommunicationTemplateKeys.SalesFollowUpSubject, "sv-SE")] = "Uppföljning",
            [(CommunicationTemplateKeys.SalesWebsiteLeadSubject, "en-GB")] = "Following up on your enquiry",
            [(CommunicationTemplateKeys.SalesWebsiteLeadSubject, "sv-SE")] = "Uppföljning av din förfrågan",
            [(CommunicationTemplateKeys.SupportReplySubject, "en-GB")] = "Reply to your support request",
            [(CommunicationTemplateKeys.SupportReplySubject, "sv-SE")] = "Svar på ditt supportärende",
            [(CommunicationTemplateKeys.FinanceNotificationSubject, "en-GB")] = "Finance notification",
            [(CommunicationTemplateKeys.FinanceNotificationSubject, "sv-SE")] = "Ekonomimeddelande"
        };

    public static CommunicationTemplateSelection Resolve(string key, CommunicationLanguageResolution language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Templates.TryGetValue((key, language.LanguageTag), out var exact))
        {
            return new(key, language.LanguageTag, exact, false, language.RequiresHumanReview);
        }

        var neutral = language.LanguageTag.Split('-', 2)[0];
        var regional = Templates.FirstOrDefault(x => x.Key.Key == key && x.Key.Language.StartsWith(neutral + "-", StringComparison.OrdinalIgnoreCase));
        if (!regional.Equals(default(KeyValuePair<(string Key, string Language), string>)))
        {
            return new(key, regional.Key.Language, regional.Value, true, true);
        }

        if (!Templates.TryGetValue((key, SourceLanguage), out var source))
        {
            throw new KeyNotFoundException($"Communication template '{key}' is not registered.");
        }

        return new(key, SourceLanguage, source, true, true);
    }

    public static IReadOnlyCollection<string> RequiredKeys => Templates.Keys.Select(x => x.Key).Distinct(StringComparer.Ordinal).ToArray();
    public static IReadOnlyCollection<string> SupportedLanguages => Templates.Keys.Select(x => x.Language).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
}
