namespace VirtualCompany.Api.Tests;

public sealed class AccountingPerformanceFactAttribute : FactAttribute
{
    public const string ProfileVariable = "VIRTUALCOMPANY_ACCOUNTING_PERF_PROFILE";

    public AccountingPerformanceFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiSqlServerFactAttribute.ConnectionVariable)))
        {
            Skip = $"Set {ApiSqlServerFactAttribute.ConnectionVariable} to a dedicated disposable SQL Server instance.";
            return;
        }

        var profile = Environment.GetEnvironmentVariable(ProfileVariable);
        if (profile is not ("small" or "medium"))
        {
            Skip = $"Set {ProfileVariable} to small or medium to run the accounting capacity fixture.";
        }
    }
}
