using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceApprovalTaskPersistenceTests
{
    [Fact]
    public async Task EnsureCreated_maps_approval_task_columns_and_indexes()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var columns = await ReadColumnsAsync(connection, "approval_tasks");
        Assert.Contains("company_id", columns);
        Assert.Contains("target_type", columns);
        Assert.Contains("target_id", columns);
        Assert.Contains("assignee_id", columns);
        Assert.Contains("status", columns);
        Assert.Contains("due_date", columns);

        var indexes = await ReadIndexesAsync(connection, "approval_tasks");
        Assert.Contains("IX_approval_tasks_company_id", indexes);
        Assert.Contains("IX_approval_tasks_company_id_assignee_id_status", indexes);
        Assert.Contains("IX_approval_tasks_assignee_id", indexes);
        Assert.Contains("IX_approval_tasks_status", indexes);
        Assert.Contains("IX_approval_tasks_due_date", indexes);
        Assert.Contains("IX_approval_tasks_company_id_status_due_date", indexes);
        Assert.Contains("IX_approval_tasks_company_id_target_type_target_id", indexes);

        var company = new Company(Guid.NewGuid(), "Approval Task Persistence Company");
        dbContext.Companies.Add(company);
        dbContext.ApprovalTasks.Add(new ApprovalTask(
            Guid.NewGuid(),
            company.Id,
            ApprovalTargetType.Bill,
            Guid.NewGuid(),
            dueDate: new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc)));
        await dbContext.SaveChangesAsync();

        var stored = await dbContext.ApprovalTasks.AsNoTracking().SingleAsync();
        Assert.Equal(ApprovalTargetType.Bill, stored.TargetType);
        Assert.Equal(ApprovalTaskStatus.Pending, stored.Status);
        Assert.Equal(new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc), stored.DueDate);
    }

    [Fact]
    public async Task Finance_bills_do_not_require_approval_tasks_and_target_uniqueness_is_enforced()
    {
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var company = new Company(Guid.NewGuid(), "Approval Optional Bill Company");
        var counterparty = new FinanceCounterparty(Guid.NewGuid(), company.Id, "Approval Optional Vendor", "supplier", "vendor@example.com");
        var bill = new FinanceBill(
            Guid.NewGuid(),
            company.Id,
            counterparty.Id,
            "BILL-APPROVAL-001",
            new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            4500m,
            "USD",
            "open");

        dbContext.Companies.Add(company);
        dbContext.FinanceCounterparties.Add(counterparty);
        dbContext.FinanceBills.Add(bill);
        await dbContext.SaveChangesAsync();

        var storedBill = await dbContext.FinanceBills.AsNoTracking().SingleAsync();
        Assert.Equal(bill.Id, storedBill.Id);
        Assert.Empty(await dbContext.ApprovalTasks.AsNoTracking().ToListAsync());

        dbContext.ApprovalTasks.Add(new ApprovalTask(
            Guid.NewGuid(),
            company.Id,
            ApprovalTargetType.Bill,
            bill.Id,
            dueDate: bill.DueUtc));
        await dbContext.SaveChangesAsync();

        dbContext.ApprovalTasks.Add(new ApprovalTask(
            Guid.NewGuid(),
            company.Id,
            ApprovalTargetType.Bill,
            bill.Id,
            status: ApprovalTaskStatus.Escalated,
            dueDate: bill.DueUtc));

        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
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