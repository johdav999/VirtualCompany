using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Finance;
using System.Diagnostics;
using System.Text.Json;

namespace VirtualCompany.Finance.Tests;

public sealed class AccountingCloseWorkspacePolicyTests
{
    [Fact]
    public void Current_success_checks_are_not_presented_as_attention_issues()
    {
        AccountingCloseReadinessCheckDto Check(string category, string code, bool blocking) =>
            new(Guid.NewGuid(), category, code, "Retained evidence", blocking, false, 0m, "SEK", 0, "hash", DateTime.UtcNow);
        Assert.Equal(1, AccountingCloseWorkspaceService.CountAttentionChecks([
            Check("bank", "bank_ready", false), Check("reports", "reports_ready", false),
            Check("tasks", "close_tasks_incomplete", true), Check("exports", "waived_export_gap", false)]));
    }
    [Fact]
    public void Manager_receives_only_actions_allowed_by_current_backend_states()
    {
        var actions = AccountingCloseWorkspaceActionPolicy.Evaluate(CompanyMembershipRole.Manager,
            ["complete"], ["refresh", "lock"], AuditPackageStatuses.PendingApproval,
            hasYearEnd: true, hasOpenAccountantEngagement: true);

        Assert.Contains(AccountingCloseWorkspaceActions.CompleteTask, actions);
        Assert.Contains(AccountingCloseWorkspaceActions.RefreshReadiness, actions);
        Assert.Contains(AccountingCloseWorkspaceActions.Lock, actions);
        Assert.Contains(AccountingCloseWorkspaceActions.ApprovePackage, actions);
        Assert.Contains(AccountingCloseWorkspaceActions.CancelPackage, actions);
        Assert.Contains(AccountingCloseWorkspaceActions.RunYearEndAction, actions);
        Assert.DoesNotContain(AccountingCloseWorkspaceActions.RequestReopen, actions);
    }

    [Fact]
    public void External_accountant_cannot_receive_close_lock_package_or_rollover_authority()
    {
        var actions = AccountingCloseWorkspaceActionPolicy.Evaluate(CompanyMembershipRole.Accountant,
            ["complete"], ["refresh", "lock", "request_reopen"], null,
            hasYearEnd: true, hasOpenAccountantEngagement: true);

        Assert.Equal([AccountingCloseWorkspaceActions.SignOff], actions);
    }

    [Fact]
    public void Locked_snapshot_exposes_only_governed_reopen_decision()
    {
        var actions = AccountingCloseWorkspaceActionPolicy.Evaluate(CompanyMembershipRole.Owner,
            [], ["execute_reopen"], AuditPackageStatuses.Final,
            hasYearEnd: false, hasOpenAccountantEngagement: false);

        Assert.Contains(AccountingCloseWorkspaceActions.ExecuteReopen, actions);
        Assert.DoesNotContain(AccountingCloseWorkspaceActions.Lock, actions);
        Assert.DoesNotContain(AccountingCloseWorkspaceActions.RequestPackage, actions);
    }

    [Fact]
    public void Supported_volume_read_model_remains_bounded_and_serializable()
    {
        var now = new DateTime(2026, 8, 30, 12, 0, 0, DateTimeKind.Utc);
        var tasks = Enumerable.Range(0, 500).Select(index => new AccountingCloseWorkspaceTaskDto(
            Guid.NewGuid(), $"task-{index}", $"Close task {index}", "not_started", Guid.NewGuid(),
            "finance_manager", now.AddMinutes(index), index, 1, [], [], [], [], ["complete"],
            $"/finance/accounting/close-workspace?taskId={index}")).ToArray();
        var model = new AccountingCloseWorkspaceDto(Guid.NewGuid(), "Supported volume company", "manager",
            now, null, Guid.NewGuid(), "Monthly close", "active", 1, [], null, tasks, [], [], [],
            [AccountingCloseWorkspaceActions.CompleteTask]);

        var timer = Stopwatch.StartNew();
        var json = JsonSerializer.Serialize(model);
        timer.Stop();

        Assert.Contains("Close task 499", json, StringComparison.Ordinal);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"Serialization took {timer.Elapsed}.");
    }
}
