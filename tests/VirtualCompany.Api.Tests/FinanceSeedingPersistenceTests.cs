using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSeedingPersistenceTests
{
    [Fact]
    public async Task Company_finance_seed_status_uses_canonical_storage_values_and_defaults()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);

        var company = new Company(Guid.NewGuid(), "Canonical Finance Seed Company");
        var statusProperty = dbContext.Model.FindEntityType(typeof(Company))!
            .FindProperty(nameof(Company.FinanceSeedStatus))!;
        var converter = statusProperty.GetTypeMapping().Converter;

        Assert.Equal(FinanceSeedingState.NotSeeded, company.FinanceSeedStatus);
        Assert.Equal(company.CreatedUtc, company.FinanceSeedStatusUpdatedUtc);
        Assert.Null(company.FinanceSeededUtc);
        Assert.Equal("not_seeded", converter!.ConvertToProvider(FinanceSeedingState.NotSeeded));
        Assert.Equal("seeded", converter.ConvertToProvider(FinanceSeedingState.Seeded));
        Assert.Equal(FinanceSeedingState.FullySeeded, converter.ConvertFromProvider("fully_seeded"));
        Assert.Equal(FinanceSeedingState.Failed, converter.ConvertFromProvider("failed"));
        Assert.Equal(32, statusProperty.GetMaxLength());
    }

    [Fact]
    public async Task EnsureCreated_maps_company_finance_seed_columns_and_round_trips_seed_timestamps()
    {
        var companyId = Guid.NewGuid();
        var statusUpdatedUtc = new DateTime(2026, 4, 17, 9, 0, 0, DateTimeKind.Utc);
        var seededUtc = new DateTime(2026, 4, 17, 9, 15, 0, DateTimeKind.Utc);

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var columns = await ReadCompanyColumnsAsync(connection);
        Assert.Contains("finance_seed_status", columns);
        Assert.Contains("finance_seed_status_updated_at", columns);
        Assert.Contains("finance_seeded_at", columns);

        var company = new Company(companyId, "Round Trip Finance Seed Company");
        company.SetFinanceSeedStatus(FinanceSeedingState.FullySeeded, statusUpdatedUtc, seededUtc);
        dbContext.Companies.Add(company);
        await dbContext.SaveChangesAsync();

        var stored = await dbContext.Companies
            .AsNoTracking()
            .SingleAsync(x => x.Id == companyId);

        Assert.Equal(FinanceSeedingState.FullySeeded, stored.FinanceSeedStatus);
        Assert.Equal(statusUpdatedUtc, stored.FinanceSeedStatusUpdatedUtc);
        Assert.Equal(seededUtc, stored.FinanceSeededUtc);
    }

    [Fact]
    public async Task EnsureCreated_rejects_invalid_finance_seed_status_values()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var companyId = Guid.NewGuid();
        var createdUtc = new DateTime(2026, 4, 17, 10, 0, 0, DateTimeKind.Utc);

        var exception = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO companies (
                    Id,
                    Name,
                    branding_json,
                    settings_json,
                    OnboardingStatus,
                    CreatedUtc,
                    UpdatedUtc,
                    finance_seed_status,
                    finance_seed_status_updated_at)
                VALUES (
                    $id,
                    $name,
                    '{}',
                    '{}',
                    'not_started',
                    $createdUtc,
                    $updatedUtc,
                    $status,
                    $statusUpdatedUtc);
                """;

            command.Parameters.AddWithValue("$id", companyId);
            command.Parameters.AddWithValue("$name", "Invalid Finance Seed Status Company");
            command.Parameters.AddWithValue("$createdUtc", createdUtc);
            command.Parameters.AddWithValue("$updatedUtc", createdUtc);
            command.Parameters.AddWithValue("$status", "unknown_seed_state");
            command.Parameters.AddWithValue("$statusUpdatedUtc", createdUtc);

            await command.ExecuteNonQueryAsync();
        });

        Assert.Contains("CK_companies_finance_seed_status", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HashSet<string>> ReadCompanyColumnsAsync(SqliteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA table_info('companies');";

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);
}