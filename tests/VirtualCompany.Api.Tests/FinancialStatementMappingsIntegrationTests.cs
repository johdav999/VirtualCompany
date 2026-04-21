using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancialStatementMappingsIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinancialStatementMappingsIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task List_endpoint_returns_only_requested_company_mappings()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.FinancialStatementMappings.AddRange(
                new FinancialStatementMapping(
                    Guid.NewGuid(),
                    seed.CompanyId,
                    seed.AssetAccountId,
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetAssets,
                    FinancialStatementLineClassification.CurrentAsset),
                new FinancialStatementMapping(
                    Guid.NewGuid(),
                    seed.OtherCompanyId,
                    seed.OtherCompanyAssetAccountId,
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetAssets,
                    FinancialStatementLineClassification.CurrentAsset));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed);

        var response = await client.GetFromJsonAsync<List<FinancialStatementMappingResponse>>(
            $"/api/companies/{seed.CompanyId}/financial-statement-mappings");

        Assert.NotNull(response);
        var item = Assert.Single(response!);
        Assert.Equal(seed.CompanyId, item.CompanyId);
        Assert.Equal(seed.AssetAccountId, item.AccountId);
        Assert.Equal("1000", item.AccountCode);
    }

    [Fact]
    public async Task Create_endpoint_persists_valid_mapping()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/financial-statement-mappings",
            new
            {
                accountId = seed.AssetAccountId,
                statementType = "balance_sheet",
                reportSection = "balance_sheet_assets",
                lineClassification = "current_asset",
                isActive = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinancialStatementMappingResponse>();
        Assert.NotNull(payload);
        Assert.Equal(seed.CompanyId, payload!.CompanyId);
        Assert.Equal(seed.AssetAccountId, payload.AccountId);
        Assert.Equal("balance_sheet", payload.StatementType);
        Assert.Equal("balance_sheet_assets", payload.ReportSection);
        Assert.Equal("current_asset", payload.LineClassification);
        Assert.True(payload.IsActive);

        var persisted = await _factory.ExecuteDbContextAsync(async dbContext =>
            await dbContext.FinancialStatementMappings.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == payload.Id));

        Assert.Equal(seed.CompanyId, persisted.CompanyId);
        Assert.Equal(seed.AssetAccountId, persisted.FinanceAccountId);
        Assert.Equal(FinancialStatementType.BalanceSheet, persisted.StatementType);
    }

    [Fact]
    public async Task Update_endpoint_modifies_mapping_fields()
    {
        var seed = await SeedAsync();
        var mappingId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.FinancialStatementMappings.Add(
                new FinancialStatementMapping(
                    mappingId,
                    seed.CompanyId,
                    seed.LiabilityAccountId,
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetLiabilities,
                    FinancialStatementLineClassification.CurrentLiability));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed);
        var response = await client.PutAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/financial-statement-mappings/{mappingId}",
            new
            {
                accountId = seed.LiabilityAccountId,
                statementType = "balance_sheet",
                reportSection = "balance_sheet_liabilities",
                lineClassification = "non_current_liability",
                isActive = false
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<FinancialStatementMappingResponse>();
        Assert.NotNull(payload);
        Assert.False(payload!.IsActive);
        Assert.Equal("non_current_liability", payload.LineClassification);

        var persisted = await _factory.ExecuteDbContextAsync(async dbContext =>
            await dbContext.FinancialStatementMappings.IgnoreQueryFilters()
                .SingleAsync(x => x.Id == mappingId));

        Assert.False(persisted.IsActive);
        Assert.Equal(FinancialStatementLineClassification.NonCurrentLiability, persisted.LineClassification);
    }

    [Fact]
    public async Task Create_endpoint_rejects_cross_company_account_reference_with_deterministic_code()
    {
        var seed = await SeedAsync();
        using var client = CreateAuthenticatedClient(seed);

        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/financial-statement-mappings",
            new
            {
                accountId = seed.OtherCompanyAssetAccountId,
                statementType = "balance_sheet",
                reportSection = "balance_sheet_assets",
                lineClassification = "current_asset",
                isActive = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<FinancialStatementMappingProblemResponse>();
        Assert.NotNull(problem);
        Assert.Equal(FinancialStatementMappingValidationErrorCodes.AccountCompanyMismatch, problem!.Code);
        var error = Assert.Single(problem.Errors);
        Assert.Equal(FinancialStatementMappingValidationErrorCodes.AccountCompanyMismatch, error.Code);
        Assert.Equal("AccountId", error.Field);
    }

    [Fact]
    public async Task Create_endpoint_translates_duplicate_active_mapping_conflict()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.FinancialStatementMappings.Add(
                new FinancialStatementMapping(
                    Guid.NewGuid(),
                    seed.CompanyId,
                    seed.AssetAccountId,
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetAssets,
                    FinancialStatementLineClassification.CurrentAsset));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed);
        var response = await client.PostAsJsonAsync(
            $"/api/companies/{seed.CompanyId}/financial-statement-mappings",
            new
            {
                accountId = seed.AssetAccountId,
                statementType = "balance_sheet",
                reportSection = "balance_sheet_assets",
                lineClassification = "non_current_asset",
                isActive = true
            });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<FinancialStatementMappingProblemResponse>();
        Assert.NotNull(problem);
        Assert.Equal(FinancialStatementMappingValidationErrorCodes.ConflictDuplicateActive, problem!.Code);
    }

    [Fact]
    public async Task Validation_endpoint_returns_unmapped_accounts_in_deterministic_order()
    {
        var seed = await SeedAsync();
        await _factory.SeedAsync(dbContext =>
        {
            dbContext.FinancialStatementMappings.Add(
                new FinancialStatementMapping(
                    Guid.NewGuid(),
                    seed.CompanyId,
                    seed.LiabilityAccountId,
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetLiabilities,
                    FinancialStatementLineClassification.CurrentLiability));
            return Task.CompletedTask;
        });

        using var client = CreateAuthenticatedClient(seed);
        var response = await client.GetFromJsonAsync<FinancialStatementMappingValidationResponse>(
            $"/api/companies/{seed.CompanyId}/financial-statement-mappings/validation");

        Assert.NotNull(response);
        Assert.Equal(seed.CompanyId, response!.CompanyId);
        Assert.Equal(2, response.UnmappedCount);
        Assert.Equal(0, response.ConflictCount);
        Assert.Equal(2, response.IssueCount);
        Assert.Equal(
            new[] { "1000", "4000" },
            response.Issues.Select(x => x.AccountCode).ToArray());
        Assert.All(
            response.Issues,
            issue => Assert.Equal(FinancialStatementMappingValidationErrorCodes.UnmappedActiveReportableAccount, issue.Code));
    }

    [Fact]
    public async Task Validation_endpoint_returns_conflicting_active_mappings_deterministically()
    {
        var seed = await SeedAsync();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.FinancialStatementMappings.AddRange(
                new FinancialStatementMapping(
                    Guid.NewGuid(),
                    seed.CompanyId,
                    seed.LiabilityAccountId,
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetLiabilities,
                    FinancialStatementLineClassification.CurrentLiability),
                new FinancialStatementMapping(
                    Guid.NewGuid(),
                    seed.CompanyId,
                    seed.RevenueAccountId,
                    FinancialStatementType.ProfitAndLoss,
                    FinancialStatementReportSection.ProfitAndLossRevenue,
                    FinancialStatementLineClassification.Revenue));
            return Task.CompletedTask;
        });

        await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                "DROP INDEX IF EXISTS IX_financial_statement_mappings_company_id_finance_account_id_statement_type");

            dbContext.FinancialStatementMappings.AddRange(
                new FinancialStatementMapping(
                    Guid.NewGuid(),
                    seed.CompanyId,
                    seed.AssetAccountId,
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetAssets,
                    FinancialStatementLineClassification.CurrentAsset,
                    createdUtc: new DateTime(2026, 4, 20, 8, 0, 0, DateTimeKind.Utc),
                    updatedUtc: new DateTime(2026, 4, 20, 8, 0, 0, DateTimeKind.Utc)),
                new FinancialStatementMapping(
                    Guid.NewGuid(),
                    seed.CompanyId,
                    seed.AssetAccountId,
                    FinancialStatementType.BalanceSheet,
                    FinancialStatementReportSection.BalanceSheetAssets,
                    FinancialStatementLineClassification.NonCurrentAsset,
                    createdUtc: new DateTime(2026, 4, 20, 9, 0, 0, DateTimeKind.Utc),
                    updatedUtc: new DateTime(2026, 4, 20, 9, 0, 0, DateTimeKind.Utc)));

            await dbContext.SaveChangesAsync();
            return 0;
        });

        using var client = CreateAuthenticatedClient(seed);
        var response = await client.GetFromJsonAsync<FinancialStatementMappingValidationResponse>(
            $"/api/companies/{seed.CompanyId}/financial-statement-mappings/validation");

        Assert.NotNull(response);
        Assert.Equal(0, response!.UnmappedCount);
        Assert.Equal(2, response.ConflictCount);
        Assert.Equal(2, response.Issues.Count);
        Assert.All(
            response.Issues,
            issue => Assert.Equal(FinancialStatementMappingValidationErrorCodes.ConflictDuplicateActive, issue.Code));
        Assert.True(response.Issues[0].MappingId != response.Issues[1].MappingId);
        Assert.True(response.Issues.SequenceEqual(
            response.Issues.OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.StatementType, StringComparer.Ordinal)
                .ThenBy(x => x.MappingId)));
    }

    private async Task<MappingSeed> SeedAsync()
    {
        var userId = Guid.NewGuid();
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var assetAccountId = Guid.NewGuid();
        var liabilityAccountId = Guid.NewGuid();
        var revenueAccountId = Guid.NewGuid();
        var otherCompanyAssetAccountId = Guid.NewGuid();
        var subject = $"mapping-user-{Guid.NewGuid():N}";
        var email = $"{subject}@example.com";
        const string displayName = "Mapping User";
        var openedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, email, displayName, "dev-header", subject));
            dbContext.Companies.AddRange(
                new Company(companyId, "Mapping Company"),
                new Company(otherCompanyId, "Other Mapping Company"));
            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.FinanceAccounts.AddRange(
                new FinanceAccount(assetAccountId, companyId, "1000", "Operating Cash", "asset", "USD", 0m, openedUtc),
                new FinanceAccount(liabilityAccountId, companyId, "2000", "Accounts Payable", "liability", "USD", 0m, openedUtc),
                new FinanceAccount(revenueAccountId, companyId, "4000", "Product Revenue", "revenue", "USD", 0m, openedUtc),
                new FinanceAccount(otherCompanyAssetAccountId, otherCompanyId, "1000", "Other Operating Cash", "asset", "USD", 0m, openedUtc));
            return Task.CompletedTask;
        });

        return new MappingSeed(
            companyId,
            otherCompanyId,
            assetAccountId,
            liabilityAccountId,
            revenueAccountId,
            otherCompanyAssetAccountId,
            subject,
            email,
            displayName);
    }

    private HttpClient CreateAuthenticatedClient(MappingSeed seed)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, seed.Subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, seed.Email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, seed.DisplayName);
        return client;
    }

    private sealed record MappingSeed(
        Guid CompanyId,
        Guid OtherCompanyId,
        Guid AssetAccountId,
        Guid LiabilityAccountId,
        Guid RevenueAccountId,
        Guid OtherCompanyAssetAccountId,
        string Subject,
        string Email,
        string DisplayName);

    private sealed class FinancialStatementMappingResponse
    {
        public Guid Id { get; set; }
        public Guid CompanyId { get; set; }
        public Guid AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public string StatementType { get; set; } = string.Empty;
        public string ReportSection { get; set; } = string.Empty;
        public string LineClassification { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    private sealed class FinancialStatementMappingValidationResponse
    {
        public Guid CompanyId { get; set; }
        public int AccountCount { get; set; }
        public int MappingCount { get; set; }
        public int IssueCount { get; set; }
        public int UnmappedCount { get; set; }
        public int ConflictCount { get; set; }
        public List<FinancialStatementMappingValidationIssueResponse> Issues { get; set; } = [];
    }

    private sealed class FinancialStatementMappingValidationIssueResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public Guid AccountId { get; set; }
        public string AccountCode { get; set; } = string.Empty;
        public string AccountName { get; set; } = string.Empty;
        public Guid? MappingId { get; set; }
        public string? StatementType { get; set; }
    }

    private sealed class FinancialStatementMappingProblemResponse
    {
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public int Status { get; set; }
        public string Code { get; set; } = string.Empty;
        public JsonElement Errors { get; set; }

        public IReadOnlyList<FinancialStatementMappingProblemErrorResponse> ErrorsList =>
            Errors.ValueKind != JsonValueKind.Array
                ? []
                : Errors.Deserialize<List<FinancialStatementMappingProblemErrorResponse>>() ?? [];

        public IReadOnlyList<FinancialStatementMappingProblemErrorResponse> Errors =>
            ErrorsList;
    }

    private sealed class FinancialStatementMappingProblemErrorResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Field { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }
}
