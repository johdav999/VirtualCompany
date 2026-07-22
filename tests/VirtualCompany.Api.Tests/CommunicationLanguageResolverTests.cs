using VirtualCompany.Application.Orchestration;
using VirtualCompany.Application.Communication;
using VirtualCompany.Domain.ValueObjects;

namespace VirtualCompany.Api.Tests;

public sealed class CommunicationLanguageResolverTests
{
    [Fact]
    public void ExplicitRecipientLanguageWins()
    {
        var result = CommunicationLanguageResolver.Resolve("sv-SE", "en-GB", "de-DE", "en-GB");
        Assert.Equal("sv-SE", result.LanguageTag);
        Assert.Equal("recipient_explicit", result.Source);
        Assert.False(result.RequiresHumanReview);
    }

    [Fact]
    public void ConversationWinsCompanyDefault()
    {
        var result = CommunicationLanguageResolver.Resolve(null, "sv-SE", null, "en-GB");
        Assert.Equal("sv-SE", result.LanguageTag);
        Assert.Equal("conversation", result.Source);
    }

    [Fact]
    public void ConflictingConversationAndCampaignRequireReview()
    {
        var result = CommunicationLanguageResolver.Resolve(null, "sv-SE", "en-GB", "en-GB");
        Assert.Equal("sv-SE", result.LanguageTag);
        Assert.True(result.RequiresHumanReview);
    }

    [Fact]
    public void InvalidEvidenceFallsBackSafely()
    {
        var result = CommunicationLanguageResolver.Resolve("not a language", null, null, "invalid");
        Assert.Equal("en-GB", result.LanguageTag);
        Assert.Equal("system_fallback", result.Source);
        Assert.True(result.RequiresHumanReview);
    }

    [Theory]
    [InlineData("SV-se", "sv-SE")]
    [InlineData("en-gb", "en-GB")]
    public void LanguageTags_AreNormalizedToCanonicalBcp47(string value, string expected)
    {
        Assert.Equal(expected, CommunicationLanguageTag.NormalizeOptional(value, nameof(value)));
    }

    [Fact]
    public void InvalidLanguageTag_IsRejectedByDomainBoundary()
    {
        Assert.Throws<ArgumentException>(() => CommunicationLanguageTag.NormalizeOptional("Swedish please", "language"));
    }

    [Fact]
    public void CommunicationTemplate_UsesExactSwedishTemplate()
    {
        var language = CommunicationLanguageResolver.Resolve("sv-SE", null, null, "en-GB");
        var template = CommunicationTemplateCatalog.Resolve(CommunicationTemplateKeys.SalesFollowUpSubject, language);

        Assert.Equal("sv-SE", template.LanguageTag);
        Assert.Equal("Uppf\u00f6ljning", template.Template);
        Assert.False(template.UsedFallback);
        Assert.False(template.RequiresHumanReview);
    }

    [Fact]
    public void UnsupportedTemplateLanguage_FallsBackToSourceAndRequiresReview()
    {
        var language = CommunicationLanguageResolver.Resolve("de-DE", null, null, "en-GB");
        var template = CommunicationTemplateCatalog.Resolve(CommunicationTemplateKeys.SupportReplySubject, language);

        Assert.Equal("en-GB", template.LanguageTag);
        Assert.True(template.UsedFallback);
        Assert.True(template.RequiresHumanReview);
    }

    [Fact]
    public void EveryCommunicationTemplate_IsCompleteForCurrentLanguages()
    {
        foreach (var key in CommunicationTemplateCatalog.RequiredKeys)
        foreach (var language in new[] { "en-GB", "sv-SE" })
        {
            var resolution = new CommunicationLanguageResolution(language, "test", 1m, "test", false);
            var template = CommunicationTemplateCatalog.Resolve(key, resolution);
            Assert.False(template.UsedFallback, $"Template {key} is missing {language}.");
            Assert.False(string.IsNullOrWhiteSpace(template.Template));
        }
    }
}
