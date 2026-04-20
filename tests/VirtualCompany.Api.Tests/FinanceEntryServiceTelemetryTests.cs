using System.Security.Claims;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auditing;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Shared;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceEntryServiceTelemetryTests
{
    [Fact]
    public async Task RequestEntryStateAsync_emits_requested_log_and_telemetry_when_job_is_enqueued()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Finance Entry Telemetry Company"));
        await dbContext.SaveChangesAsync();

        var telemetry = new TestFinanceSeedTelemetry();
        var logger = new ScopeCapturingLogger<CompanyFinanceEntryService>();
        var service = CreateService(dbContext, telemetry, logger, userId, "finance-entry-correlation");

        var state = await service.RequestEntryStateAsync(new GetFinanceEntryStateQuery(companyId), CancellationToken.None);

        Assert.True(state.SeedJobEnqueued);
        var telemetryEvent = Assert.Single(telemetry.Events);
        Assert.Equal(FinanceSeedTelemetryEventNames.Requested, telemetryEvent.EventName);
        Assert.Equal(companyId, telemetryEvent.Context.CompanyId);
        Assert.Equal(userId, telemetryEvent.Context.UserId);
        Assert.Equal(FinanceEntrySources.FinanceEntry, telemetryEvent.Context.TriggerSource);
        Assert.Equal(FinanceSeedingState.NotSeeded, telemetryEvent.Context.SeedStateBefore);
        Assert.Equal(FinanceSeedingState.Seeding, telemetryEvent.Context.SeedStateAfter);
        Assert.False(telemetryEvent.Context.JobAlreadyRunning);
        Assert.Equal(FinanceSeedRequestModes.Replace, telemetryEvent.Context.SeedMode);
        Assert.Equal(AuditActorTypes.User, telemetryEvent.Context.ActorType);
        Assert.Equal(userId, telemetryEvent.Context.ActorId);

        var logEntry = Assert.Single(logger.Entries.Where(x => x.Message.Contains("Finance seed orchestration requested", StringComparison.Ordinal)));
        Assert.Equal(FinanceEntrySources.FinanceEntry, Assert.IsType<string>(logEntry.State["TriggerSource"]));
        Assert.Equal(companyId, Assert.IsType<Guid>(logEntry.State["CompanyId"]));
        Assert.Equal(AuditActorTypes.User, Assert.IsType<string>(logEntry.State["ActorType"]));
        Assert.Equal(userId, Assert.IsType<Guid>(logEntry.State["ActorId"]));
        Assert.Equal(FinanceSeedRequestModes.Replace, Assert.IsType<string>(logEntry.State["SeedMode"]));
        Assert.Equal("not_seeded", Assert.IsType<string>(logEntry.State["SeedStateBefore"]));
        Assert.Equal("seeding", Assert.IsType<string>(logEntry.State["SeedStateAfter"]));
    }

    [Fact]
    public async Task RequestEntryStateAsync_logs_duplicate_active_request_without_duplicate_requested_telemetry()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Finance Entry Duplicate Company"));
        await dbContext.SaveChangesAsync();

        var telemetry = new TestFinanceSeedTelemetry();
        var logger = new ScopeCapturingLogger<CompanyFinanceEntryService>();
        var service = CreateService(dbContext, telemetry, logger, Guid.NewGuid(), "finance-entry-duplicate");

        var firstState = await service.RequestEntryStateAsync(new GetFinanceEntryStateQuery(companyId), CancellationToken.None);
        var secondState = await service.RequestEntryStateAsync(new GetFinanceEntryStateQuery(companyId), CancellationToken.None);

        Assert.True(firstState.SeedJobEnqueued);
        Assert.False(secondState.SeedJobEnqueued);
        Assert.True(secondState.SeedJobActive);
        Assert.Equal(
            1,
            telemetry.Events.Count(x =>
                x.EventName == FinanceSeedTelemetryEventNames.Requested &&
                x.Context.CompanyId == companyId));

        var duplicateLog = Assert.Single(logger.Entries.Where(x => x.Message.Contains("reused existing execution", StringComparison.Ordinal)));
        Assert.Equal(FinanceEntrySources.FinanceEntry, Assert.IsType<string>(duplicateLog.State["TriggerSource"]));
        Assert.Equal(companyId, Assert.IsType<Guid>(duplicateLog.State["CompanyId"]));
        Assert.Equal("pending", Assert.IsType<string>(duplicateLog.State["JobStatus"]));
        Assert.Equal(FinanceSeedRequestModes.Replace, Assert.IsType<string>(duplicateLog.State["SeedMode"]));
        Assert.Equal(AuditActorTypes.User, Assert.IsType<string>(duplicateLog.State["ActorType"]));
        Assert.NotEqual(Guid.Empty, Assert.IsType<Guid>(duplicateLog.State["ExecutionId"]));
    }

    [Fact]
    public async Task Manual_replace_without_confirmation_is_rejected_without_requested_telemetry()
    {
        var companyId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Finance Entry Manual Replace Company"));
        FinanceSeedData.AddMockFinanceData(dbContext, companyId);
        await dbContext.SaveChangesAsync();

        var telemetry = new TestFinanceSeedTelemetry();
        var logger = new ScopeCapturingLogger<CompanyFinanceEntryService>();
        var service = CreateService(dbContext, telemetry, logger, Guid.NewGuid(), "finance-entry-manual-replace");

        var state = await service.RequestEntryStateAsync(
            new GetFinanceEntryStateQuery(
                companyId,
                ForceSeed: true,
                Source: FinanceEntrySources.ManualSeed,
                SeedMode: FinanceSeedRequestModes.Replace,
                ConfirmReplace: false),
            CancellationToken.None);

        Assert.True(state.DataAlreadyExists);
        Assert.True(state.ConfirmationRequired);
        Assert.Equal(FinanceSeedOperationContractValues.Rejected, state.SeedOperation);
        Assert.Empty(telemetry.Events);
        var logEntry = Assert.Single(logger.Entries.Where(x => x.Message.Contains("explicit confirmation was not provided", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(AuditActorTypes.User, Assert.IsType<string>(logEntry.State["ActorType"]));
        Assert.Equal(FinanceSeedRequestModes.Replace, Assert.IsType<string>(logEntry.State["SeedMode"]));
    }

    private static CompanyFinanceEntryService CreateService(
        VirtualCompanyDbContext dbContext,
        IFinanceSeedTelemetry telemetry,
        ScopeCapturingLogger<CompanyFinanceEntryService> logger,
        Guid userId,
        string correlationId)
    {
        var currentUserAccessor = new TestCurrentUserAccessor
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "tests")),
            UserId = userId
        };

        return new CompanyFinanceEntryService(
            dbContext,
            new CompanyFinanceSeedingStateResolver(dbContext),
            new DefaultBackgroundExecutionIdentityFactory(),
            new AuditEventWriter(dbContext),
            TimeProvider.System,
            logger,
            telemetry,
            currentUserAccessor,
            new RequestCorrelationContextAccessor
            {
                CorrelationId = correlationId
            });
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