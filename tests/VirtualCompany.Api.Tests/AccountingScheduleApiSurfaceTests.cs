using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using VirtualCompany.Api.Controllers;
using VirtualCompany.Application.Authorization;

namespace VirtualCompany.Api.Tests;

public sealed class AccountingScheduleApiSurfaceTests
{
    [Theory]
    [InlineData("ListAccountingSchedulesAsync", CompanyPolicies.AccountingView)]
    [InlineData("GetAccountingScheduleAsync", CompanyPolicies.AccountingView)]
    [InlineData("CreateAccountingScheduleAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("UpdateAccountingScheduleAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("SubmitAccountingScheduleAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("ActivateAccountingScheduleAsync", CompanyPolicies.AccountingAdmin)]
    [InlineData("DecideAccountingScheduleApprovalAsync", CompanyPolicies.FinanceApproval)]
    public void Schedule_endpoints_require_the_expected_company_policy(string methodName, string policy)
    {
        var method = typeof(InternalFinanceController).GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.Contains(method!.GetCustomAttributes<AuthorizeAttribute>(), attribute => attribute.Policy == policy);
    }

    [Fact]
    public void Schedule_migration_contains_version_occurrence_lease_and_idempotency_storage()
    {
        var migration = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "VirtualCompany.Persistence.Migrations",
            "Persistence", "Migrations", "20260829145851_AddAccountingSchedules.cs"));
        Assert.Contains("accounting_schedule_versions", migration, StringComparison.Ordinal);
        Assert.Contains("accounting_schedule_occurrences", migration, StringComparison.Ordinal);
        Assert.Contains("lease_expires_utc", migration, StringComparison.Ordinal);
        Assert.Contains("accounting_schedule_operations", migration, StringComparison.Ordinal);
        Assert.Contains("idempotency_key", migration, StringComparison.Ordinal);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
