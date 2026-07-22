using System.Globalization;
using Microsoft.Extensions.Localization;
using VirtualCompany.Web.Localization.Shared;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Tests;

public sealed class LocalizedProblemPresenterTests
{
    [Fact]
    public void KnownCode_UsesLocalizedResourceInsteadOfFallbackProse()
    {
        var text = new FakeLocalizer(new() { ["ProblemCompanyContextRequired"] = "Välj ett företag innan du fortsätter." });
        var result = LocalizedProblemPresenter.Present("identity.company_context_required", "English fallback", "trace-1", text);
        Assert.Equal("Välj ett företag innan du fortsätter.", result.Message);
        Assert.True(result.IsKnownCode);
    }

    [Fact]
    public void UnknownCode_PreservesSafeLegacyFallback()
    {
        var result = LocalizedProblemPresenter.Present("future.code", "Safe fallback", "trace-2", new FakeLocalizer([]));
        Assert.Equal("Safe fallback", result.Message);
        Assert.False(result.IsKnownCode);
    }

    [Fact]
    public void CommunicationLanguageCode_UsesPlainLocalizedMessage()
    {
        var text = new FakeLocalizer(new() { ["ProblemCommunicationLanguageInvalid"] = "Ange ett giltigt kommunikationsspr\u00e5k." });
        var result = LocalizedProblemPresenter.Present("communication.language_invalid", "technical fallback", null, text);

        Assert.Equal("Ange ett giltigt kommunikationsspr\u00e5k.", result.Message);
        Assert.True(result.IsKnownCode);
    }

    private sealed class FakeLocalizer(Dictionary<string, string> values) : IStringLocalizer<CommonResources>
    {
        public LocalizedString this[string name] => new(name, values.GetValueOrDefault(name, name), !values.ContainsKey(name));
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(CultureInfo.InvariantCulture, values.GetValueOrDefault(name, name), arguments), !values.ContainsKey(name));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => values.Select(pair => new LocalizedString(pair.Key, pair.Value));
    }
}
