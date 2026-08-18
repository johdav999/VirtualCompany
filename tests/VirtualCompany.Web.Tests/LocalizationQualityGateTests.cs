using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Collections;
using System.Globalization;
using System.Resources;
using VirtualCompany.Web.Localization;
using VirtualCompany.Web.Localization.Agents;
using VirtualCompany.Web.Localization.Finance;
using VirtualCompany.Web.Localization.GuidedWork;
using VirtualCompany.Web.Localization.Sales;
using VirtualCompany.Web.Localization.Shared;
using VirtualCompany.Web.Localization.Support;

namespace VirtualCompany.Web.Tests;

public sealed partial class LocalizationQualityGateTests
{
    private static readonly Type[] RequiredResourceFamilies =
    [
        typeof(CommonResources),
        typeof(NavigationResources),
        typeof(ValidationResources),
        typeof(AgentsResources),
        typeof(GuidedWorkResources),
        typeof(FinanceResources),
        typeof(SalesResources),
        typeof(SupportResources)
    ];

    [Fact]
    public void SupportedCultures_HaveValidUniqueSelectorMetadata()
    {
        Assert.Equal("en-GB", SupportedWebCultures.Default);
        Assert.Equal(2, SupportedWebCultures.All.Count);
        Assert.Equal(SupportedWebCultures.All.Count, SupportedWebCultures.All.Select(x => x.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(SupportedWebCultures.All, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.DisplayName));
            Assert.True(SupportedWebCultures.TryNormalize(item.Name, out var normalized));
            Assert.Equal(item.Name, normalized);
        });
    }

    [Fact]
    public void ResourceFiles_HaveNoDuplicateOrEmptyKeys()
    {
        var localizationRoot = Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Web", "Localization");
        foreach (var path in Directory.EnumerateFiles(localizationRoot, "*.resx", SearchOption.AllDirectories))
        {
            var entries = XDocument.Load(path).Root!.Elements("data")
                .Select(x => new { Key = (string?)x.Attribute("name"), Value = (string?)x.Element("value") })
                .ToList();
            Assert.DoesNotContain(entries, x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Value));
            Assert.Empty(entries.GroupBy(x => x.Key, StringComparer.Ordinal).Where(group => group.Count() > 1));
        }
    }

    [Fact]
    public void EverySupportedCulture_HasEveryResourceFamilyWithValidFormats()
    {
        foreach (var family in RequiredResourceFamilies)
        {
            var manager = new ResourceManager(family);
            var source = Read(manager, CultureInfo.InvariantCulture);
            foreach (var culture in SupportedWebCultures.CultureInfos)
            {
                var localized = Read(manager, culture);
                Assert.Equal(source.Keys.Order(StringComparer.Ordinal), localized.Keys.Order(StringComparer.Ordinal));
                foreach (var (key, value) in localized)
                {
                    Assert.False(string.IsNullOrWhiteSpace(value), $"{family.Name}/{culture.Name}/{key} is empty.");
                    AssertValidCompositeFormat(family.Name, culture.Name, key, value);
                }
            }
        }
    }

    [Fact]
    public void UnsupportedCulture_FallsBackToCompleteSourceResources()
    {
        var unsupported = CultureInfo.GetCultureInfo("de-DE");
        foreach (var family in RequiredResourceFamilies)
        {
            var manager = new ResourceManager(family);
            var source = Read(manager, CultureInfo.InvariantCulture);
            var fallback = Read(manager, unsupported);
            Assert.Equal(source.Keys.Order(StringComparer.Ordinal), fallback.Keys.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public void LocalizedShell_HasNoUnreviewedVisibleEnglishLiterals()
    {
        var webRoot = Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Web");
        var files = new[] { "App.razor", Path.Combine("Layout", "MainLayout.razor"), Path.Combine("Layout", "NavMenu.razor"), Path.Combine("Pages", "UserPreferences.razor") };
        var allowList = new HashSet<string>(StringComparer.Ordinal) { "Virtual Company" };
        var violations = new List<string>();
        foreach (var file in files)
        {
            var path = Path.Combine(webRoot, file);
            var markup = string.Join(
                Environment.NewLine,
                File.ReadLines(path).Where(line =>
                    !line.TrimStart().StartsWith("@using ", StringComparison.Ordinal) &&
                    !line.TrimStart().StartsWith("@inject ", StringComparison.Ordinal)));
            foreach (Match match in VisibleTextPattern().Matches(markup))
            {
                var text = Regex.Replace(match.Groups[1].Value, "\\s+", " ").Trim();
                if (text.Length > 1 && !allowList.Contains(text)) violations.Add($"{file}: {text}");
            }
        }
        Assert.Empty(violations);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static Dictionary<string, string> Read(ResourceManager manager, CultureInfo culture)
    {
        var set = manager.GetResourceSet(culture, true, true)
            ?? throw new InvalidOperationException($"No resources for {manager.BaseName} and {culture.Name}.");
        return set.Cast<DictionaryEntry>().ToDictionary(
            entry => (string)entry.Key,
            entry => (string)entry.Value!,
            StringComparer.Ordinal);
    }

    private static void AssertValidCompositeFormat(string family, string culture, string key, string value)
    {
        var indexes = Regex.Matches(value, "\\{([0-9]+)(?:[^}]*)?\\}")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();
        var arguments = Enumerable.Repeat<object>(1, indexes.Length == 0 ? 0 : indexes.Max() + 1).ToArray();
        try
        {
            _ = string.Format(CultureInfo.GetCultureInfo(culture.Length == 0 ? SupportedWebCultures.Default : culture), value, arguments);
        }
        catch (FormatException exception)
        {
            Assert.Fail($"{family}/{culture}/{key} is not a valid composite format: {exception.Message}");
        }
    }

    [GeneratedRegex(@">\s*([^<@{}]*[A-Za-z][^<@{}]*)\s*<", RegexOptions.CultureInvariant)]
    private static partial Regex VisibleTextPattern();
}
