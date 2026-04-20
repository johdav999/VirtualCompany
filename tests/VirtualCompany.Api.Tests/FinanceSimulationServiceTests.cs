using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceSimulationServiceTests
{
    [Fact]
    public async Task Advancing_simulated_time_generates_finance_records_and_execution_logs()
    {
        var companyId = Guid.Parse("33333333-4444-5555-6666-777777777777");
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Simulation Company"));
        await dbContext.SaveChangesAsync();

        var service = CreateService(dbContext, timeProvider);
        var initialClock = await service.GetClockAsync(
            new GetCompanySimulationClockQuery(companyId),
            CancellationToken.None);

        var result = await service.AdvanceAsync(
            new AdvanceCompanySimulationTimeCommand(companyId, 72, 24, accelerated: true),
            CancellationToken.None);

        Assert.Equal(initialClock.SimulatedUtc.AddHours(72), result.CurrentUtc);
        Assert.Equal(72, result.TotalHoursProcessed);
        Assert.Equal(24, result.ExecutionStepHours);
        Assert.Equal(3, result.Logs.Count);
        Assert.True(result.TransactionsGenerated > 0);
        Assert.True(result.InvoicesGenerated > 0);
        Assert.True(result.BillsGenerated > 0);
        Assert.True(result.RecurringExpenseInstancesGenerated > 0);
        Assert.Equal(result.Logs.Count, result.EventsEmitted);
        Assert.All(result.Logs, log =>
        {
            Assert.Equal(companyId, log.CompanyId);
            Assert.True(log.WindowEndUtc > log.WindowStartUtc);
            Assert.Equal(1, log.EventsEmitted);
        });

        var transactions = await dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync();
        var invoices = await dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync();
        var bills = await dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToArrayAsync();
        var activityEvents = await dbContext.ActivityEvents
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.EventType == "finance.simulation.progressed")
            .OrderBy(x => x.OccurredUtc)
            .ToArrayAsync();

        Assert.NotEmpty(transactions);
        Assert.NotEmpty(invoices);
        Assert.NotEmpty(bills);
        Assert.Equal(result.EventsEmitted, activityEvents.Length);
        Assert.All(transactions, transaction =>
        {
            Assert.InRange(transaction.TransactionUtc, initialClock.SimulatedUtc, result.CurrentUtc);
            Assert.InRange(transaction.CreatedUtc, initialClock.SimulatedUtc, result.CurrentUtc);
        });
        Assert.All(invoices, invoice =>
        {
            Assert.InRange(invoice.IssuedUtc, initialClock.SimulatedUtc, result.CurrentUtc);
            Assert.InRange(invoice.CreatedUtc, initialClock.SimulatedUtc, result.CurrentUtc);
            Assert.InRange(invoice.UpdatedUtc, initialClock.SimulatedUtc, result.CurrentUtc);
        });
        Assert.All(bills, bill =>
        {
            Assert.InRange(bill.ReceivedUtc, initialClock.SimulatedUtc, result.CurrentUtc);
            Assert.InRange(bill.CreatedUtc, initialClock.SimulatedUtc, result.CurrentUtc);
            Assert.InRange(bill.UpdatedUtc, initialClock.SimulatedUtc, result.CurrentUtc);
        });
        Assert.All(activityEvents, activityEvent =>
        {
            Assert.InRange(activityEvent.CreatedUtc, initialClock.SimulatedUtc, result.CurrentUtc);
            Assert.Equal(companyId.ToString(), activityEvent.SourceMetadata["companyId"]?.GetValue<string>());
            Assert.Equal(1, activityEvent.SourceMetadata["eventsEmitted"]?.GetValue<int>());
        });
    }

    [Fact]
    public async Task Accelerated_mode_can_process_thirty_simulated_days_quickly()
    {
        var companyId = Guid.Parse("44444444-5555-6666-7777-888888888888");
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Accelerated Simulation Company"));
        await dbContext.SaveChangesAsync();

        var service = CreateService(
            dbContext,
            timeProvider,
            new CompanySimulationOptions
            {
                DefaultStepHours = 24,
                MaxStepHours = 168,
                AllowAcceleratedExecution = true
            });

        var stopwatch = Stopwatch.StartNew();
        var result = await service.AdvanceAsync(
            new AdvanceCompanySimulationTimeCommand(companyId, 24 * 30, 24, accelerated: true),
            CancellationToken.None);
        stopwatch.Stop();

        Assert.Equal(24 * 30, result.TotalHoursProcessed);
        Assert.Equal(30, result.Logs.Count);
        Assert.True(result.TransactionsGenerated > 0);
        Assert.True(result.InvoicesGenerated > 0);
        Assert.True(result.BillsGenerated > 0);
        Assert.True(result.RecurringExpenseInstancesGenerated > 0);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(60), $"Simulation took {stopwatch.Elapsed.TotalSeconds:0.00} seconds.");
    }

    private static CompanySimulationService CreateService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        CompanySimulationOptions? options = null) =>
        new(
            dbContext,
            timeProvider,
            Options.Create(options ?? new CompanySimulationOptions
            {
                DefaultStepHours = 24,
                MaxStepHours = 168,
                AllowAcceleratedExecution = true
            }),
            NullLogger<CompanySimulationService>.Instance);

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

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
