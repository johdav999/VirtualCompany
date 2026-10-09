namespace VirtualCompany.Api.Tests;

public sealed class ApiSqlServerFactAttribute : FactAttribute
{
    public const string ConnectionVariable = "VIRTUALCOMPANY_SQLSERVER_TEST_CONNECTION";

    public ApiSqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
        {
            Skip = $"Set {ConnectionVariable} to run the isolated SQL Server API scenario.";
        }
    }
}
