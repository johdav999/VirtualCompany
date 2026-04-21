using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceApprovalTaskWorkflowTests
{
    [Fact]
    public async Task EnsureTaskAsync_creates_pending_payment_approval_and_prevents_duplicates()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        var approverUser = new User(Guid.NewGuid(), "approver@example.com", "Payment Approver", "dev-header", "payment-approver");
        var payment = new Payment(
            Guid.NewGuid(),
            companyId,
            PaymentTypes.Outgoing,
            640.10m,
            "USD",
            new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            "ach",
            "pending",
            "PAY-640");

        dbContext.Companies.Add(new Company(companyId, "Payment approval company"));
        dbContext.Users.Add(approverUser);
        dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, approverUser.Id, CompanyMembershipRole.FinanceApprover, CompanyMembershipStatus.Active));
        dbContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(Guid.NewGuid(), companyId, "USD", 10000m, 500m, true));
        dbContext.Payments.Add(payment);
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceApprovalTaskService(dbContext, new TestCompanyContextAccessor(companyId), new ScopeCapturingLogger<CompanyFinanceApprovalTaskService>());

        var created = await service.EnsureTaskAsync(
            new EnsureFinanceApprovalTaskCommand(companyId, ApprovalTargetType.Payment, payment.Id, payment.Amount, payment.Currency, payment.PaymentDate),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        var duplicated = await service.EnsureTaskAsync(
            new EnsureFinanceApprovalTaskCommand(companyId, ApprovalTargetType.Payment, payment.Id, payment.Amount, payment.Currency, payment.PaymentDate),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.True(created);
        Assert.False(duplicated);

        var tasks = await dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TargetType == ApprovalTargetType.Payment && x.TargetId == payment.Id)
            .ToListAsync();
        var task = Assert.Single(tasks);
        Assert.Equal(ApprovalTaskStatus.Pending, task.Status);
        Assert.Equal(approverUser.Id, task.AssigneeId);
        Assert.Equal(payment.PaymentDate, task.DueDate);
    }

    [Fact]
    public async Task EnsureTaskAsync_does_not_create_task_for_below_threshold_targets()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Below threshold company"));
        dbContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(Guid.NewGuid(), companyId, "USD", 10000m, 5000m, true));
        await dbContext.SaveChangesAsync();

        var service = new CompanyFinanceApprovalTaskService(dbContext, new TestCompanyContextAccessor(companyId), new ScopeCapturingLogger<CompanyFinanceApprovalTaskService>());
        var created = await service.EnsureTaskAsync(
            new EnsureFinanceApprovalTaskCommand(companyId, ApprovalTargetType.Bill, Guid.NewGuid(), 4999.99m, "USD", new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        await dbContext.SaveChangesAsync();

        Assert.False(created);
        Assert.Empty(await dbContext.ApprovalTasks.IgnoreQueryFilters().AsNoTracking().ToListAsync());
    }

    [Fact]
    public void Approval_task_terminal_transitions_are_guarded_but_escalated_tasks_remain_actionable()
    {
        var pendingTask = new ApprovalTask(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApprovalTargetType.Bill,
            Guid.NewGuid(),
            dueDate: new DateTime(2026, 4, 25, 0, 0, 0, DateTimeKind.Utc));

        pendingTask.Escalate();
        pendingTask.Approve();

        Assert.Equal(ApprovalTaskStatus.Approved, pendingTask.Status);

        var terminalTask = new ApprovalTask(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ApprovalTargetType.Payment,
            Guid.NewGuid(),
            status: ApprovalTaskStatus.Rejected);

        Assert.Throws<InvalidOperationException>(() => terminalTask.Approve());
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

    private sealed class TestCompanyContextAccessor : ICompanyContextAccessor
    {
        public TestCompanyContextAccessor(Guid companyId)
        {
            CompanyId = companyId;
        }

        public Guid? CompanyId { get; private set; }
        public Guid? UserId => null;
        public bool IsResolved => CompanyId.HasValue;
        public ResolvedCompanyMembershipContext? Membership => null;

        public void SetCompanyId(Guid? companyId)
        {
            CompanyId = companyId;
        }

        public void SetCompanyContext(ResolvedCompanyMembershipContext? companyContext)
        {
            CompanyId = companyContext?.CompanyId;
        }
    }
}
