using System.Security.Cryptography;
using System.Text.Json;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class Bas2026AccountingChartCatalog : IAccountingChartCatalog
{
    public const int ExpectedAccountCount = 1282;
    public const string ExpectedSourceSha256 = "a86b39937fab280d4e5db895c04c2af6e145695863d4845ff14eea5d0302328a";
    public const string ExpectedCatalogSha256 = "2ed4f76eca5655bb62d77be4b30dbc6f511afa67d6470656b24a75b441672efc";
    private const string ResourceSuffix = ".Finance.Resources.bas-kontoplan-2026-v1.1.json";
    private readonly IReadOnlyDictionary<string, AccountingChartCatalogAccountDefinition> _accountsByCode;

    public Bas2026AccountingChartCatalog()
    {
        var assembly = typeof(Bas2026AccountingChartCatalog).Assembly;
        var resourceName = assembly.GetManifestResourceNames().SingleOrDefault(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Embedded BAS catalogue resource ending with '{ResourceSuffix}' was not found.");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded BAS catalogue resource '{resourceName}' could not be opened.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var bytes = buffer.ToArray();
        var catalogSha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(catalogSha256, ExpectedCatalogSha256, StringComparison.Ordinal))
            throw new InvalidOperationException("The embedded BAS catalogue content hash does not match its immutable checked-in version.");
        var document = JsonSerializer.Deserialize<CatalogDocument>(bytes, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException("The embedded BAS catalogue is empty or invalid JSON.");

        ValidateDocument(document);
        CatalogKey = document.CatalogKey;
        CatalogVersion = document.CatalogVersion;
        DisplayName = document.DisplayName;
        Locale = document.Locale;
        SourceFileName = document.Source.FileName;
        SourceSha256 = document.Source.Sha256;
        Limitations = document.Limitations.AsReadOnly();
        Accounts = document.Accounts.Select(account => new AccountingChartCatalogAccountDefinition(
            account.Code,
            account.NameSv,
            account.NameVariantsSv.AsReadOnly(),
            account.IsK2Allowed,
            account.IsSubAccount,
            account.ParentAccountCode,
            account.GroupCode,
            account.GroupNameSv,
            account.SuggestedAccountClass,
            account.SuggestedNormalBalance)).ToArray();
        _accountsByCode = Accounts.ToDictionary(account => account.Code, StringComparer.Ordinal);
    }

    public string CatalogKey { get; }
    public string CatalogVersion { get; }
    public string DisplayName { get; }
    public string Locale { get; }
    public string SourceFileName { get; }
    public string SourceSha256 { get; }
    public IReadOnlyList<string> Limitations { get; }
    public IReadOnlyList<AccountingChartCatalogAccountDefinition> Accounts { get; }

    public bool TryGetAccount(string code, out AccountingChartCatalogAccountDefinition? account)
    {
        account = null;
        return !string.IsNullOrWhiteSpace(code) && _accountsByCode.TryGetValue(code.Trim(), out account);
    }

    private static void ValidateDocument(CatalogDocument document)
    {
        if (document.SchemaVersion != "1.0" ||
            document.CatalogKey != AccountingChartCatalogDefaults.Bas2026CatalogKey ||
            document.CatalogVersion != AccountingChartCatalogDefaults.Bas2026CatalogVersion)
        {
            throw new InvalidOperationException("The embedded BAS catalogue identity or schema version is not supported.");
        }

        if (document.AccountCount != ExpectedAccountCount || document.Accounts.Count != ExpectedAccountCount)
            throw new InvalidOperationException($"The embedded BAS catalogue must contain exactly {ExpectedAccountCount} unique account codes.");
        if (!string.Equals(document.Source.Sha256, ExpectedSourceSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The embedded BAS catalogue source hash does not match the checked-in BAS 2026 workbook.");
        if (document.Accounts.Select(account => account.Code).Distinct(StringComparer.Ordinal).Count() != ExpectedAccountCount)
            throw new InvalidOperationException("The embedded BAS catalogue contains duplicate account codes.");
        var codes = document.Accounts.Select(account => account.Code).ToHashSet(StringComparer.Ordinal);

        foreach (var account in document.Accounts)
        {
            if (account.Code.Length != 4 || !account.Code.All(char.IsAsciiDigit) ||
                string.IsNullOrWhiteSpace(account.NameSv) || account.NameVariantsSv.Count == 0 ||
                !account.NameVariantsSv.Contains(account.NameSv, StringComparer.Ordinal) ||
                account.GroupCode.Length != 2 || string.IsNullOrWhiteSpace(account.GroupNameSv))
            {
                throw new InvalidOperationException($"The embedded BAS catalogue account '{account.Code}' is incomplete or invalid.");
            }

            if (account.IsSubAccount &&
                (string.IsNullOrWhiteSpace(account.ParentAccountCode) || !codes.Contains(account.ParentAccountCode)))
            {
                throw new InvalidOperationException($"The embedded BAS catalogue subaccount '{account.Code}' has no valid parent account.");
            }

            if (account.SuggestedAccountClass is not null && FinanceAccountClassValues.NormalizeOptional(account.SuggestedAccountClass) is null)
                throw new InvalidOperationException($"The embedded BAS catalogue account '{account.Code}' has an invalid suggested account class.");
            if (account.SuggestedNormalBalance is not null && FinanceNormalBalanceValues.NormalizeOptional(account.SuggestedNormalBalance) is null)
                throw new InvalidOperationException($"The embedded BAS catalogue account '{account.Code}' has an invalid suggested normal balance.");
        }
    }

    private sealed class CatalogDocument
    {
        public string SchemaVersion { get; set; } = string.Empty;
        public string CatalogKey { get; set; } = string.Empty;
        public string CatalogVersion { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Locale { get; set; } = string.Empty;
        public CatalogSource Source { get; set; } = new();
        public List<string> Limitations { get; set; } = [];
        public int AccountCount { get; set; }
        public List<CatalogAccount> Accounts { get; set; } = [];
    }

    private sealed class CatalogSource
    {
        public string FileName { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
    }

    private sealed class CatalogAccount
    {
        public string Code { get; set; } = string.Empty;
        public string NameSv { get; set; } = string.Empty;
        public List<string> NameVariantsSv { get; set; } = [];
        public bool IsK2Allowed { get; set; }
        public bool IsSubAccount { get; set; }
        public string? ParentAccountCode { get; set; }
        public string GroupCode { get; set; } = string.Empty;
        public string GroupNameSv { get; set; } = string.Empty;
        public string? SuggestedAccountClass { get; set; }
        public string? SuggestedNormalBalance { get; set; }
    }
}
