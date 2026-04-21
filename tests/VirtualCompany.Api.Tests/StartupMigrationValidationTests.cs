using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class StartupMigrationValidationTests
{
    [Fact]
    public void Pending_migrations_throw_actionable_exception()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            StartupMigrationValidation.EnsureNoPendingMigrations(
                ["20260425090000_AddFinanceInsightSnapshotCache", "20260425093000_BackfillFinanceInsightJobs"],
                NullLogger.Instance,
                "Test"));

        Assert.Contains("20260425090000_AddFinanceInsightSnapshotCache", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Test", exception.Message, StringComparison.Ordinal);
        Assert.Contains("dotnet ef database update", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Noop_when_no_pending_migrations_exist()
    {
        StartupMigrationValidation.EnsureNoPendingMigrations(Array.Empty<string>(), NullLogger.Instance, "Development");
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Test", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    public void Fail_fast_guard_matches_expected_environments(string environmentName, bool expected)
    {
        var environment = new TestHostEnvironment(environmentName);

        var result = StartupMigrationValidation.ShouldFailFastOnPendingMigrations(environment);

        Assert.Equal(expected, result);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = nameof(VirtualCompany);
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

file static class VirtualCompany
{
}

static class HostEnvironmentExtensions
{
    public static bool IsDevelopment(this IHostEnvironment environment) =>
        string.Equals(environment.EnvironmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);

    public static bool IsEnvironment(this IHostEnvironment environment, string environmentName) =>
        string.Equals(environment.EnvironmentName, environmentName, StringComparison.OrdinalIgnoreCase);
    }
}