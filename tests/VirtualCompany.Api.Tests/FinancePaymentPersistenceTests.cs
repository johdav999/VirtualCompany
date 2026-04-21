using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinancePaymentPersistenceTests
{
    [Fact]
    public async Task EnsureCreated_maps_finance_payment_columns_and_indexes()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var columns = await ReadColumnsAsync(connection, "finance_payments");
        Assert.Contains("company_id", columns);
        Assert.Contains("payment_type", columns);
        Assert.Contains("amount", columns);
        Assert.Contains("payment_date", columns);
        Assert.Contains("status", columns);
        Assert.Contains("counterparty_reference", columns);

        var indexes = await ReadIndexesAsync(connection, "finance_payments");
        Assert.Contains("IX_finance_payments_company_id", indexes);
        Assert.Contains("IX_finance_payments_status", indexes);
        Assert.Contains("IX_finance_payments_payment_date", indexes);

        var company = new Company(Guid.NewGuid(), "Payment Persistence Company");
        dbContext.Companies.Add(company);
        dbContext.Payments.Add(new Payment(
            Guid.NewGuid(),
            company.Id,
            "incoming",
            1250.40m,
            "usd",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            "bank_transfer",
            "completed",
            "PERSIST-001"));
        await dbContext.SaveChangesAsync();

        var stored = await dbContext.Payments.AsNoTracking().SingleAsync();
        Assert.Equal("incoming", stored.PaymentType);
        Assert.Equal("USD", stored.Currency);
        Assert.Equal("completed", stored.Status);
    }

    [Fact]
    public async Task EnsureCreated_rejects_non_positive_payment_amounts()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var companyId = await InsertCompanyAsync(connection);

        var exception = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO finance_payments (
                    id,
                    company_id,
                    payment_type,
                    amount,
                    currency,
                    payment_date,
                    method,
                    status,
                    counterparty_reference,
                    created_at,
                    updated_at)
                VALUES ($id, $companyId, $paymentType, $amount, $currency, $paymentDate, $method, $status, $reference, $createdAt, $updatedAt);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid());
            command.Parameters.AddWithValue("$companyId", companyId);
            command.Parameters.AddWithValue("$paymentType", "incoming");
            command.Parameters.AddWithValue("$amount", 0m);
            command.Parameters.AddWithValue("$currency", "USD");
            command.Parameters.AddWithValue("$paymentDate", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
            command.Parameters.AddWithValue("$method", "bank_transfer");
            command.Parameters.AddWithValue("$status", "completed");
            command.Parameters.AddWithValue("$reference", "INVALID-AMOUNT");
            command.Parameters.AddWithValue("$createdAt", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
            command.Parameters.AddWithValue("$updatedAt", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
            await command.ExecuteNonQueryAsync();
        });

        Assert.Contains("CK_finance_payments_amount_positive", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EnsureCreated_rejects_invalid_payment_type_values()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var companyId = await InsertCompanyAsync(connection);

        var exception = await Assert.ThrowsAsync<SqliteException>(async () =>
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO finance_payments (
                    id,
                    company_id,
                    payment_type,
                    amount,
                    currency,
                    payment_date,
                    method,
                    status,
                    counterparty_reference,
                    created_at,
                    updated_at)
                VALUES ($id, $companyId, $paymentType, $amount, $currency, $paymentDate, $method, $status, $reference, $createdAt, $updatedAt);
                """;
            command.Parameters.AddWithValue("$id", Guid.NewGuid());
            command.Parameters.AddWithValue("$companyId", companyId);
            command.Parameters.AddWithValue("$paymentType", "sideways");
            command.Parameters.AddWithValue("$amount", 99.95m);
            command.Parameters.AddWithValue("$currency", "USD");
            command.Parameters.AddWithValue("$paymentDate", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
            command.Parameters.AddWithValue("$method", "bank_transfer");
            command.Parameters.AddWithValue("$status", "completed");
            command.Parameters.AddWithValue("$reference", "INVALID-TYPE");
            command.Parameters.AddWithValue("$createdAt", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
            command.Parameters.AddWithValue("$updatedAt", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
            await command.ExecuteNonQueryAsync();
        });

        Assert.Contains("CK_finance_payments_payment_type", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Guid> InsertCompanyAsync(SqliteConnection connection)
    {
        var companyId = Guid.NewGuid();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO companies (Id, Name, branding_json, settings_json, OnboardingStatus, CreatedUtc, UpdatedUtc, finance_seed_status, finance_seed_status_updated_at) VALUES ($id, $name, '{}', '{}', 'not_started', $createdUtc, $updatedUtc, 'seeded', $statusUpdatedUtc);";
        command.Parameters.AddWithValue("$id", companyId);
        command.Parameters.AddWithValue("$name", "Payment Constraint Company");
        command.Parameters.AddWithValue("$createdUtc", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
        command.Parameters.AddWithValue("$updatedUtc", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
        command.Parameters.AddWithValue("$statusUpdatedUtc", new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc));
        await command.ExecuteNonQueryAsync();
        return companyId;
    }

    private static async Task<HashSet<string>> ReadColumnsAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
    }

    private static async Task<HashSet<string>> ReadIndexesAsync(SqliteConnection connection, string tableName)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA index_list('{tableName}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            values.Add(reader.GetString(1));
        }

        return values;
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