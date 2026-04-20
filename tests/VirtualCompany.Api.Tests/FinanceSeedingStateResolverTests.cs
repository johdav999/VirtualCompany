using System.Data.Common;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSeedingStateResolverTests
{
    [Fact]
    public async Task ResolveAsync_returns_not_seeded_when_metadata_is_absent_and_no_finance_records_exist()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "No Finance Company"));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, companyId);

        var result = await resolver.ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.NotSeeded, result.State);
        Assert.Equal(FinanceSeedingState.NotSeeded, result.Diagnostics.PersistedState);
        Assert.False(result.Diagnostics.MetadataPresent);
        Assert.False(result.Diagnostics.UsedFastPath);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.RecordChecks, result.DerivedFrom);
        Assert.False(result.Diagnostics.HasAccounts);
        Assert.False(result.Diagnostics.HasCounterparties);
        Assert.False(result.Diagnostics.HasBills);
        Assert.False(result.Diagnostics.HasTransactions);
        Assert.False(result.Diagnostics.HasBalances);
        Assert.False(result.Diagnostics.HasPolicyConfiguration);
    }

    [Fact]
    public async Task ResolveAsync_returns_partially_seeded_when_only_some_foundational_records_exist()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Partial Finance Company"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            Guid.NewGuid(),
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            1000m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, companyId);

        var result = await resolver.ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.PartiallySeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.RecordChecks, result.DerivedFrom);
        Assert.False(result.Diagnostics.MetadataPresent);
        Assert.False(result.Diagnostics.UsedFastPath);
        Assert.True(result.Diagnostics.HasAccounts);
        Assert.Equal("Some finance indicators exist, but the foundational seeded dataset is incomplete.", result.Diagnostics.Reason);
        Assert.False(result.Diagnostics.HasTransactions);
        Assert.False(result.Diagnostics.HasBalances);
        Assert.False(result.Diagnostics.HasPolicyConfiguration);
    }

    [Fact]
    public async Task ResolveAsync_returns_fully_seeded_when_foundational_records_exist_without_metadata()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Seeded Finance Company"));
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, companyId);

        var result = await resolver.ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.FullySeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.RecordChecks, result.DerivedFrom);
        Assert.False(result.Diagnostics.MetadataPresent);
        Assert.True(result.Diagnostics.HasAccounts);
        Assert.False(result.Diagnostics.UsedFastPath);
        Assert.True(result.Diagnostics.HasCounterparties);
        Assert.True(result.Diagnostics.HasTransactions);
        Assert.True(result.Diagnostics.HasBalances);
        Assert.True(result.Diagnostics.HasPolicyConfiguration);
    }

    [Fact]
    public async Task ResolveAsync_returns_fully_seeded_from_metadata_fast_path_even_when_actual_finance_records_are_absent()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Metadata Fast Path Company");
        ApplyMetadata(company, FinanceSeedingState.FullySeeded);
        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, companyId);

        var result = await resolver.ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.FullySeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.Metadata, result.DerivedFrom);
        Assert.True(result.Diagnostics.MetadataPresent);
        Assert.True(result.Diagnostics.UsedFastPath);
        Assert.False(result.Diagnostics.HasAccounts);
        Assert.False(result.Diagnostics.HasTransactions);
    }

    [Fact]
    public async Task ResolveAsync_returns_fully_seeded_when_metadata_and_records_both_indicate_complete_state()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Metadata Seeded Company");
        ApplyMetadata(company, FinanceSeedingState.FullySeeded);
        dbContext.Companies.Add(company);
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, companyId);

        var result = await resolver.ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.FullySeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.Metadata, result.DerivedFrom);
        Assert.True(result.Diagnostics.MetadataPresent);
        Assert.True(result.Diagnostics.UsedFastPath);
        Assert.Equal(FinanceSeedingState.FullySeeded, result.Diagnostics.MetadataState);
        Assert.True(result.Diagnostics.MetadataIndicatesComplete);
    }

    [Fact]
    public async Task ResolveAsync_returns_partially_seeded_when_metadata_claims_full_but_record_check_metadata_is_inconsistent()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Inconsistent Metadata Company");
        company.SetFinanceSeedStatus(FinanceSeedingState.FullySeeded, DateTime.UtcNow, DateTime.UtcNow);
        company.Settings.Extensions["financeSeeding"] = new JsonObject
        {
            ["state"] = JsonValue.Create(FinanceSeedingState.FullySeeded.ToStorageValue()),
            ["recordChecks"] = new JsonObject()
        };
        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, companyId);

        var result = await resolver.ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.PartiallySeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.RecordChecks, result.DerivedFrom);
        Assert.True(result.Diagnostics.MetadataPresent);
        Assert.False(result.Diagnostics.UsedFastPath);
        Assert.Equal(FinanceSeedingState.FullySeeded, result.Diagnostics.MetadataState);
        Assert.False(result.Diagnostics.HasAccounts);
        Assert.False(result.Diagnostics.HasTransactions);
    }

    [Fact]
    public async Task ResolveAsync_prefers_company_level_finance_seed_metadata_without_json_extensions()
    {
        var companyId = Guid.NewGuid();
        var seededUtc = new DateTime(2026, 4, 17, 8, 30, 0, DateTimeKind.Utc);

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Company Metadata Only");
        company.SetFinanceSeedStatus(FinanceSeedingState.FullySeeded, seededUtc, seededUtc);
        dbContext.Companies.Add(company);
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, companyId);

        var result = await resolver.ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.FullySeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.RecordChecks, result.DerivedFrom);
        Assert.True(result.Diagnostics.MetadataPresent);
        Assert.Equal(FinanceSeedingState.FullySeeded, result.Diagnostics.MetadataState);
        Assert.False(result.Diagnostics.UsedFastPath);
        Assert.True(result.Diagnostics.MetadataIndicatesComplete);
    }


    [Fact]
    public async Task ResolveAsync_returns_partially_seeded_for_incomplete_metadata_and_partial_records()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Partial Metadata Company");
        ApplyMetadata(company, FinanceSeedingState.PartiallySeeded);
        dbContext.Companies.Add(company);
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            Guid.NewGuid(),
            companyId,
            "1001",
            "Reserve Cash",
            "asset",
            "USD",
            250m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var resolver = CreateResolver(dbContext, companyId);

        var result = await resolver.ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.PartiallySeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.Metadata, result.DerivedFrom);
        Assert.True(result.Diagnostics.MetadataPresent);
        Assert.Equal(FinanceSeedingState.PartiallySeeded, result.Diagnostics.MetadataState);
        Assert.True(result.Diagnostics.UsedFastPath);
        Assert.True(result.Diagnostics.HasAccounts);
        Assert.False(result.Diagnostics.HasTransactions);
    }

    [Fact]
    public async Task ResolveAsync_returns_not_seeded_from_metadata_fast_path_when_metadata_explicitly_marks_not_seeded()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Not Seeded Metadata Company");
        company.SetFinanceSeedStatus(FinanceSeedingState.NotSeeded, new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc));
        company.Settings.Extensions["financeSeeding"] = new JsonObject
        {
            ["state"] = JsonValue.Create(FinanceSeedingState.NotSeeded.ToStorageValue()),
            ["recordChecks"] = new JsonObject
            {
                ["accounts"] = JsonValue.Create(false),
                ["counterparties"] = JsonValue.Create(false),
                ["transactions"] = JsonValue.Create(false),
                ["balances"] = JsonValue.Create(false),
                ["policyConfiguration"] = JsonValue.Create(false)
            }
        };
        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync();

        var result = await CreateResolver(dbContext, companyId).ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.NotSeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.Metadata, result.DerivedFrom);
        Assert.True(result.Diagnostics.UsedFastPath);
    }

    [Fact]
    public async Task ResolveAsync_promotes_not_seeded_metadata_to_fully_seeded_when_foundational_records_exist()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Metadata Conflict Company");
        ApplyMetadata(company, FinanceSeedingState.NotSeeded);
        dbContext.Companies.Add(company);
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var result = await CreateResolver(dbContext, companyId).ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.FullySeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.RecordChecks, result.DerivedFrom);
        Assert.True(result.Diagnostics.MetadataPresent);
        Assert.Equal(FinanceSeedingState.NotSeeded, result.Diagnostics.MetadataState);
        Assert.False(result.Diagnostics.UsedFastPath);
        Assert.True(result.Diagnostics.HasAccounts);
        Assert.True(result.Diagnostics.HasTransactions);
        Assert.True(result.Diagnostics.HasBalances);
        Assert.True(result.Diagnostics.HasPolicyConfiguration);
    }

    [Fact]
    public async Task ResolveAsync_promotes_partial_metadata_to_fully_seeded_when_record_checks_confirm_complete_dataset()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Partial Metadata With Complete Records Company");
        ApplyMetadata(company, FinanceSeedingState.PartiallySeeded);
        dbContext.Companies.Add(company);
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var result = await CreateResolver(dbContext, companyId).ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.FullySeeded, result.State);
        Assert.Equal(FinanceSeedingStateDerivedFromValues.RecordChecks, result.DerivedFrom);
        Assert.Equal(FinanceSeedingState.PartiallySeeded, result.Diagnostics.MetadataState);
        Assert.False(result.Diagnostics.UsedFastPath);
    }

    [Fact]
    public async Task ResolveAsync_honors_active_company_context()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.AddRange(
            new Company(companyId, "Primary Company"),
            new Company(otherCompanyId, "Other Company"));
        await dbContext.SaveChangesAsync();

        var companyContextAccessor = new RequestCompanyContextAccessor();
        companyContextAccessor.SetCompanyId(companyId);
        var resolver = new CompanyFinanceSeedingStateResolver(dbContext, companyContextAccessor);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resolver.ResolveAsync(otherCompanyId, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_uses_only_company_lookup_when_metadata_fast_path_applies()
    {
        var companyId = Guid.NewGuid();
        var commandCapture = new CommandCaptureInterceptor();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection, commandCapture);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Fast Path Query Company");
        ApplyMetadata(company, FinanceSeedingState.FullySeeded);
        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync();

        commandCapture.Clear();

        var result = await CreateResolver(dbContext, companyId).ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.FullySeeded, result.State);
        Assert.True(result.Diagnostics.UsedFastPath);
        Assert.Single(commandCapture.Commands);
    }

    [Fact]
    public async Task ResolveAsync_uses_company_lookup_plus_existence_checks_when_metadata_fallback_is_required()
    {
        var companyId = Guid.NewGuid();
        var commandCapture = new CommandCaptureInterceptor();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection, commandCapture);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(companyId, "Fallback Query Company");
        ApplyMetadata(company, FinanceSeedingState.NotSeeded);
        dbContext.Companies.Add(company);
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            Guid.NewGuid(),
            companyId,
            "1005",
            "Fallback Cash",
            "asset",
            "USD",
            120m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        commandCapture.Clear();

        var result = await CreateResolver(dbContext, companyId).ResolveAsync(companyId, CancellationToken.None);

        Assert.Equal(FinanceSeedingState.PartiallySeeded, result.State);
        Assert.False(result.Diagnostics.UsedFastPath);
        Assert.Equal(8, commandCapture.Commands.Count);
    }

    private static CompanyFinanceSeedingStateResolver CreateResolver(VirtualCompanyDbContext dbContext, Guid companyId)
    {
        ICompanyContextAccessor companyContextAccessor = new RequestCompanyContextAccessor();
        companyContextAccessor.SetCompanyId(companyId);
        return new CompanyFinanceSeedingStateResolver(dbContext, companyContextAccessor);
    }

    private static void ApplyMetadata(Company company, FinanceSeedingState state)
    {
        var seededAtUtc = new DateTime(2026, 4, 17, 0, 0, 0, DateTimeKind.Utc);
        company.SetFinanceSeedStatus(state, seededAtUtc, seededAtUtc);
        company.Settings.Extensions["financeSeeding"] = new JsonObject
        {
            ["state"] = JsonValue.Create(state.ToStorageValue()),
            ["seededAtUtc"] = JsonValue.Create(seededAtUtc),
            ["recordChecks"] = new JsonObject
            {
                ["accounts"] = JsonValue.Create(state == FinanceSeedingState.FullySeeded),
                ["counterparties"] = JsonValue.Create(state == FinanceSeedingState.FullySeeded),
                ["transactions"] = JsonValue.Create(state == FinanceSeedingState.FullySeeded),
                ["balances"] = JsonValue.Create(state == FinanceSeedingState.FullySeeded),
                ["policyConfiguration"] = JsonValue.Create(state == FinanceSeedingState.FullySeeded)
            }
        };
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection, DbCommandInterceptor? interceptor = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection);

        if (interceptor is not null)
        {
            optionsBuilder.AddInterceptors(interceptor);
        }

        return new VirtualCompanyDbContext(optionsBuilder.Options);
    }

    private sealed class CommandCaptureInterceptor : DbCommandInterceptor
    {
        private readonly List<string> _commands = [];

        public IReadOnlyList<string> Commands => _commands;

        public void Clear() => _commands.Clear();

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            _commands.Add(command.CommandText);
            return base.ReaderExecuting(command, eventData, result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            _commands.Add(command.CommandText);
            return base.ScalarExecuting(command, eventData, result);
        }
    }
}