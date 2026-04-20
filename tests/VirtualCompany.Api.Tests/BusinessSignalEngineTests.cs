using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Signals;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class BusinessSignalEngineTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public BusinessSignalEngineTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GenerateSignals_emits_operational_load_and_approval_bottleneck_signals()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "signals@example.com", "Signals", "dev-header", "signals-user"));
            dbContext.Companies.Add(new Company(companyId, "Signals Co"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "ops", "Avery Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));

            var task = new WorkTask(taskId, companyId, "ops", "Resolve blocked queue", null, WorkTaskPriority.High, agentId, null, "user", userId);
            task.UpdateStatus(WorkTaskStatus.InProgress);
            dbContext.WorkTasks.Add(task);

            dbContext.ApprovalRequests.Add(
                ApprovalRequest.CreateForTarget(
                    approvalId,
                    companyId,
                    ApprovalTargetEntityType.Task,
                    taskId,
                    "user",
                    userId,
                    "threshold",
                    [],
                    null,
                    userId,
                    []));

            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ISignalEngine>();

        var signals = await engine.GenerateSignals(companyId);

        Assert.Contains(signals, x => x.Type == BusinessSignalType.OperationalLoad);
        Assert.Contains(signals, x => x.Type == BusinessSignalType.ApprovalBottleneck);
    }

    [Fact]
    public async Task Seeded_operational_data_generates_at_least_one_business_signal()
    {
        var companyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var agentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.Add(new User(userId, "seed-signals@example.com", "Seed Signals", "dev-header", "seed-signals-user"));
            dbContext.Companies.Add(new Company(companyId, "Seed Signals Co"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));
            dbContext.Agents.Add(new Agent(agentId, companyId, "ops", "Avery Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));

            var task = new WorkTask(Guid.NewGuid(), companyId, "ops", "Publish founder update", null, WorkTaskPriority.Normal, agentId, null, "user", userId);
            task.UpdateStatus(WorkTaskStatus.InProgress);
            dbContext.WorkTasks.Add(task);
            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ISignalEngine>();

        var signals = await engine.GenerateSignals(companyId);

        Assert.NotEmpty(signals);
    }

    [Fact]
    public async Task GenerateSignals_returns_populated_signals_for_requested_company_without_cross_tenant_leakage()
    {
        var companyId = Guid.NewGuid();
        var otherCompanyId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var otherAgentId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Users.AddRange(
                new User(userId, "requested-signals@example.com", "Requested Signals", "dev-header", "requested-signals-user"),
                new User(otherUserId, "other-signals@example.com", "Other Signals", "dev-header", "other-signals-user"));

            dbContext.Companies.AddRange(
                new Company(companyId, "Requested Signals Co"),
                new Company(otherCompanyId, "Other Signals Co"));

            dbContext.CompanyMemberships.AddRange(
                new CompanyMembership(Guid.NewGuid(), companyId, userId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), otherCompanyId, otherUserId, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active));

            dbContext.Agents.AddRange(
                new Agent(agentId, companyId, "ops", "Avery Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active),
                new Agent(otherAgentId, otherCompanyId, "ops", "Otis Ops", "Operations Lead", "Operations", null, AgentSeniority.Lead, AgentStatus.Active));

            var requestedTaskOne = new WorkTask(Guid.NewGuid(), companyId, "ops", "Follow up on delayed order", null, WorkTaskPriority.High, agentId, null, "user", userId);
            requestedTaskOne.UpdateStatus(WorkTaskStatus.InProgress);
            var requestedTaskTwo = new WorkTask(Guid.NewGuid(), companyId, "ops", "Review support backlog", null, WorkTaskPriority.Normal, agentId, null, "user", userId);
            requestedTaskTwo.UpdateStatus(WorkTaskStatus.AwaitingApproval);

            var otherTaskOne = new WorkTask(Guid.NewGuid(), otherCompanyId, "ops", "Escalate payroll issue", null, WorkTaskPriority.High, otherAgentId, null, "user", otherUserId);
            otherTaskOne.UpdateStatus(WorkTaskStatus.Blocked);
            var otherTaskTwo = new WorkTask(Guid.NewGuid(), otherCompanyId, "ops", "Patch finance importer", null, WorkTaskPriority.High, otherAgentId, null, "user", otherUserId);
            otherTaskTwo.UpdateStatus(WorkTaskStatus.InProgress);
            var otherTaskThree = new WorkTask(Guid.NewGuid(), otherCompanyId, "ops", "Review outage notes", null, WorkTaskPriority.High, otherAgentId, null, "user", otherUserId);
            otherTaskThree.UpdateStatus(WorkTaskStatus.New);

            dbContext.WorkTasks.AddRange(requestedTaskOne, requestedTaskTwo, otherTaskOne, otherTaskTwo, otherTaskThree);

            dbContext.ApprovalRequests.AddRange(
                ApprovalRequest.CreateForTarget(
                    Guid.NewGuid(),
                    companyId,
                    ApprovalTargetEntityType.Task,
                    requestedTaskTwo.Id,
                    "user",
                    userId,
                    "threshold",
                    [],
                    null,
                    userId,
                    []),
                ApprovalRequest.CreateForTarget(
                    Guid.NewGuid(),
                    otherCompanyId,
                    ApprovalTargetEntityType.Task,
                    otherTaskOne.Id,
                    "user",
                    otherUserId,
                    "threshold",
                    [],
                    null,
                    otherUserId,
                    []));

            return Task.CompletedTask;
        });

        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<ISignalEngine>();

        var signals = await engine.GenerateSignals(companyId);

        Assert.NotEmpty(signals);

        var operationalLoad = Assert.Single(signals.Where(signal => signal.Type == BusinessSignalType.OperationalLoad));
        Assert.Equal(BusinessSignalSeverity.Warning, operationalLoad.Severity);
        Assert.Equal(2m, operationalLoad.MetricValue.GetValueOrDefault());
        Assert.False(string.IsNullOrWhiteSpace(operationalLoad.Title));
        Assert.False(string.IsNullOrWhiteSpace(operationalLoad.Summary));
        Assert.Contains("2 open work item(s)", operationalLoad.Summary);
        Assert.Contains($"companyId={companyId:D}", operationalLoad.ActionUrl!);

        var approvalBottleneck = Assert.Single(signals.Where(signal => signal.Type == BusinessSignalType.ApprovalBottleneck));
        Assert.Equal(BusinessSignalSeverity.Info, approvalBottleneck.Severity);
        Assert.Equal(1m, approvalBottleneck.MetricValue.GetValueOrDefault());
        Assert.False(string.IsNullOrWhiteSpace(approvalBottleneck.Title));
        Assert.False(string.IsNullOrWhiteSpace(approvalBottleneck.Summary));
        Assert.Contains("1 approval(s) are pending.", approvalBottleneck.Summary);
        Assert.Contains($"companyId={companyId:D}", approvalBottleneck.ActionUrl!);
    }
}